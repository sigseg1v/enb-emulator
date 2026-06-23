#!/usr/bin/env bash
# SPDX-License-Identifier: MIT
# Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
# License: LICENSES/Freya
#
# explore-live.sh -- the ONE reliable command to run the survey against a LIVE,
# already-running client. "explore live" maps to this. It exists so a run never
# again depends on remembering a long inline env string or re-exporting vars by
# hand: the config lives in a gitignored `.env` next to SKILL.md, this script
# loads it, fills in the live-mode invariants, PREFLIGHTS every precondition that
# has silently broken a run before, and only then hands off to survey.sh.
#
# It assumes YOU launched the client + proxy under WINE and left the client at
# the character-select screen. It does NOT start our docker stack, never launches
# or kills the client itself (beyond the opt-in no-progress deadman), and reads
# the screen only. survey.sh's first step enters the character from char-select.
#
#   bash scripts/explore-live.sh            # load .env, preflight, survey
#   bash scripts/explore-live.sh <Sector>   # same, with a first-sector label hint
#   any extra args pass straight through to survey.sh -> run-sector.sh -> drive.py
#
# Exit codes: 2 = a preflight precondition failed (nothing was driven); otherwise
# whatever survey.sh returns (0 done, 3 gravity well, 43 stuck, 44 deadman, ...).
set -uo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SKILL_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

die() { printf '[explore-live] FATAL: %s\n' "$*" >&2; exit 2; }
note() { printf '[explore-live] %s\n' "$*" >&2; }

# ---- 1. load .env ---------------------------------------------------------
# Canonical location is the skill folder (next to SKILL.md); also accept one in
# scripts/ so either placement works. Every assignment in it is exported.
ENV_FILE=""
for cand in "$SKILL_ROOT/.env" "$SCRIPT_DIR/.env"; do
    [ -f "$cand" ] && { ENV_FILE="$cand"; break; }
done
if [ -n "$ENV_FILE" ]; then
    note "loading config: $ENV_FILE"
    set -a
    # shellcheck source=/dev/null
    . "$ENV_FILE"
    set +a
else
    die "no .env found. Copy the template and fill it in:
       cp '$SKILL_ROOT/.env.example' '$SKILL_ROOT/.env'  &&  edit it"
fi

# ---- 2. live-mode invariants ---------------------------------------------
# These define "live": never touch our stack, capture the WINE proxy, capture
# from char-select. We FORCE manual-client on (the whole point), and default the
# rest only when .env did not set them.
export ENB_EXPLORE_MANUAL_CLIENT=1
export ENB_EXPLORE_PROXY_NAME="${ENB_EXPLORE_PROXY_NAME:-Net7Proxy.exe}"
export ENB_PCAP="${ENB_PCAP:-1}"
export DISPLAY="${DISPLAY:-:0}"

# ---- 3. preflight (fail LOUD, before anything is driven) ------------------
# Required config.
[ -n "${ENB_EXPLORE_CHARACTER_NAME:-}" ] || \
    die "ENB_EXPLORE_CHARACTER_NAME is not set in $ENV_FILE -- char-select entry needs it."
[ -n "${ENB_EXPLORE_WORKDIR:-}" ] || \
    die "ENB_EXPLORE_WORKDIR is not set in $ENV_FILE -- set a persistent path outside the repo."
export ENB_EXPLORE_WORKDIR ENB_EXPLORE_CHARACTER_NAME

# Workdir must be creatable + writable (ledgers/captures land here).
mkdir -p "$ENB_EXPLORE_WORKDIR/captures" 2>/dev/null || \
    die "cannot create ENB_EXPLORE_WORKDIR=$ENB_EXPLORE_WORKDIR"
[ -w "$ENB_EXPLORE_WORKDIR" ] || die "ENB_EXPLORE_WORKDIR not writable: $ENB_EXPLORE_WORKDIR"

# X reachable (the client is a WINE GUI; no display == every shot/click fails).
command -v xdotool >/dev/null 2>&1 || die "xdotool not found (need it to drive the client)."
xdotool getdisplaygeometry >/dev/null 2>&1 || \
    die "cannot reach X on DISPLAY=$DISPLAY. Is the desktop session up?"

# The live client must already be running. We do NOT launch it -- that is the
# operator's job in live mode. Reuse the skill's own window finder.
# shellcheck source=/dev/null
. "$SCRIPT_DIR/lib.sh"
if ! CLIENT_WIN="$(client_win)"; then
    die "no client.exe window found. Launch your live client (and Net7Proxy.exe)
       under WINE and leave it at the character-select screen, then re-run."
fi
note "client.exe window: $CLIENT_WIN"

# The proxy must be running -- the live client talks through it, and the capture
# scopes to it. pgrep -x matches the exact comm name the pcap worker resolves.
if ! pgrep -x -- "$ENB_EXPLORE_PROXY_NAME" >/dev/null 2>&1; then
    die "proxy '$ENB_EXPLORE_PROXY_NAME' is not running. Start it (the client
       connects through it); without it the client is not online and the
       capture has nothing to record."
fi
note "proxy running: $ENB_EXPLORE_PROXY_NAME"

# Capture is the POINT of a live survey, so a broken capture is a hard stop here
# rather than a silent empty captures/ dir discovered hours later.
if [ "$ENB_PCAP" = 1 ]; then
    WORKER="${ENB_PCAP_WORKER:-/usr/local/sbin/freya-pcap-capture}"
    OUTDIR="${ENB_PCAP_DIR:-$ENB_EXPLORE_WORKDIR/captures}"
    if [ ! -x "$WORKER" ] || ! sudo -n "$WORKER" status "$OUTDIR" >/dev/null 2>&1; then
        die "ENB_PCAP=1 but the capture worker is not usable
       ($WORKER, passwordless sudo). Install it once:
           sudo bash '$SCRIPT_DIR/pcap-install.sh'
       or set ENB_PCAP=0 in $ENV_FILE to survey without capturing."
    fi
    note "capture worker ready -> $OUTDIR"
fi

# ---- 4. hand off to the survey -------------------------------------------
note "config OK. character=$ENB_EXPLORE_CHARACTER_NAME workdir=$ENB_EXPLORE_WORKDIR" \
     "pcap=$ENB_PCAP deadman=${ENB_EXPLORE_KILL_NO_PROGRESS:-off}"
note "starting survey (manual-client, screen-read only) -- entering from char-select ..."
exec bash "$SCRIPT_DIR/survey.sh" "$@"
