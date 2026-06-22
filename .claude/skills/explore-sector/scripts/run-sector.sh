#!/usr/bin/env bash
# SPDX-License-Identifier: MIT
# Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
# License: LICENSES/Freya
#
# run-sector.sh <Sector> [drive.py args...] -- drive a sector to completion, and if
# the live client HARD-HANGS mid-run (drive.py detects a frozen frame and exits
# EXIT_HANG=42), automatically kill + re-login and RESUME the same sector. The
# ledger (state.py) persists every visit, so a resume continues where it left off
# -- it does NOT restart the sector. Any other drive.py exit code is passed through
# (0 = done, 3 = refused gravity-well sector, etc.).
#
# EXIT_STUCK=43 is the deliberate exception to "auto-recover": it means the fast
# loop made no progress for many rounds and is in a bad state a blind relogin would
# NOT fix (it would just resume the same spin). So we HALT loudly with the dumped
# screenshot path and let a human / the LLM interpret and re-route. Do not turn this
# into another relogin loop.
#
# This is the unattended-safe entry point; prefer it over calling drive.py directly
# for a long sweep so a one-off client wedge does not strand the run.
set -uo pipefail
SKILL_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
LOGIN_DIR="$SKILL_DIR/../../login-to-client/scripts"
# Survey work root (ledgers + action log + pcaps + stuck shots); override per survey
# with ENB_EXPLORE_WORKDIR (inherited by drive.py/state.py/logaction/pcap). Used here
# only to point operators at the right stuck-shot path.
WORKDIR="${ENB_EXPLORE_WORKDIR:-$SKILL_DIR/../state}"
EXIT_HANG=42
EXIT_STUCK=43
EXIT_KILL=44     # deadman: drive.py killed the client on no-progress; do NOT relogin
MAX_RELOGIN="${ENB_MAX_RELOGIN:-4}"
# Manual-client mode: the operator launches (and owns) the client + proxy
# themselves (e.g. a real client + Net7Proxy.exe under WINE). We then never
# launch or relaunch the client -- on a hard hang we cannot relogin a client we
# do not own, so we halt for the operator to relaunch and re-run, rather than
# bouncing the stack out from under their manual session.
MANUAL_CLIENT="${ENB_EXPLORE_MANUAL_CLIENT:-0}"

# Sector is OPTIONAL: omit it (or pass "auto") and drive.py reads the current
# sector off the map title. Pass a name to target one specific sector. We keep a
# resolved label only for the human-facing log lines below; drive.py is the one
# that actually identifies + verifies the sector each invocation. Only consume $1
# as the sector when it is a real name -- a leading flag (e.g. --max-rounds) or
# "auto" means auto-detect, and the flag must pass through to drive.py untouched.
sector="auto"
if [ "$#" -gt 0 ] && [ "${1#-}" = "$1" ] && [ "$1" != "auto" ]; then
    sector="$1"; shift
fi

relogin() {
    echo "[run-sector] hard hang -> kill + relogin ..." >&2
    bash "$LOGIN_DIR/login.sh" || true
    # login.sh occasionally stalls on the character-select Enter flake; force the
    # last two steps until we are confirmed in-game (08 exits non-zero on timeout).
    for _ in 1 2 3; do
        if bash "$LOGIN_DIR/08-wait-ingame.sh"; then return 0; fi
        bash "$LOGIN_DIR/07-charselect-enter.sh" || true
    done
    bash "$LOGIN_DIR/08-wait-ingame.sh"
}

relogins=0
while :; do
    python3 "$SKILL_DIR/drive.py" "$sector" "$@"
    rc=$?
    if [ "$rc" -eq "$EXIT_STUCK" ]; then
        echo "[run-sector] $sector STUCK (no progress) -- halting for operator/LLM; " \
             "see $WORKDIR/scratch/stuck-$sector.png. NOT relogin-resuming." >&2
        exit "$EXIT_STUCK"
    fi
    if [ "$rc" -eq "$EXIT_KILL" ]; then
        echo "[run-sector] $sector hit ENB_EXPLORE_KILL_NO_PROGRESS -- drive.py killed " \
             "the client and is stopping the run. NOT relogin-resuming." >&2
        exit "$EXIT_KILL"
    fi
    if [ "$rc" -ne "$EXIT_HANG" ]; then
        exit "$rc"
    fi
    if [ "$MANUAL_CLIENT" = 1 ]; then
        echo "[run-sector] $sector hard-hung and ENB_EXPLORE_MANUAL_CLIENT=1 -- not" \
             "auto-relaunching a client we do not own. Relaunch the client (and your" \
             "proxy) yourself, then re-run: run-sector.sh $sector  to resume (the" \
             "ledger persists, so it continues where it left off)." >&2
        exit "$EXIT_HANG"
    fi
    relogins=$((relogins + 1))
    if [ "$relogins" -gt "$MAX_RELOGIN" ]; then
        echo "[run-sector] $relogins hangs on $sector; giving up" >&2
        exit 1
    fi
    if ! relogin; then
        echo "[run-sector] relogin #$relogins failed; aborting" >&2
        exit 1
    fi
    echo "[run-sector] resumed in-game after hang #$relogins; continuing $sector" >&2
done
