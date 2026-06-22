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
EXIT_HANG=42
EXIT_STUCK=43
MAX_RELOGIN="${ENB_MAX_RELOGIN:-4}"

sector="${1:?usage: run-sector.sh <Sector> [drive.py args...]}"; shift || true

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
             "see state/stuck-$sector.png. NOT relogin-resuming." >&2
        exit "$EXIT_STUCK"
    fi
    if [ "$rc" -ne "$EXIT_HANG" ]; then
        exit "$rc"
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
