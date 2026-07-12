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
# has silently broken a run before, and only then hands off to drive_lua.py.
#
# The survey drives the client entirely through the injected enbmod Lua command
# channel (drive_lua.py) -- no screenshots, no OCR. So the one precondition beyond
# a running client + proxy is that the character is already IN-GAME (the channel
# only answers once enb.self() is a table). It does NOT start our docker stack and
# never launches or kills the client itself.
#
#   bash scripts/explore-live.sh            # load .env, preflight, survey
#   bash scripts/explore-live.sh <Sector>   # same (extra args pass through to drive_lua.py)
#
# Exit codes: 2 = a preflight precondition failed (nothing was driven); otherwise
# whatever drive_lua.py returns (0 done, 3 gravity well, 42 client hang in
# manual-client mode, ...).
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
# The survey character is also the one a (local-mode) relogin re-enters, so feed
# it to the login skill's ENB_LOGIN_CHAR unless that was set explicitly. In
# manual/live mode we halt rather than relogin, so this is only load-bearing on a
# local stack -- but it keeps the required name from being a var nothing consumes.
export ENB_LOGIN_CHAR="${ENB_LOGIN_CHAR:-$ENB_EXPLORE_CHARACTER_NAME}"

# Workdir must be creatable + writable (ledgers/captures land here).
mkdir -p "$ENB_EXPLORE_WORKDIR/captures" 2>/dev/null || \
    die "cannot create ENB_EXPLORE_WORKDIR=$ENB_EXPLORE_WORKDIR"
[ -w "$ENB_EXPLORE_WORKDIR" ] || die "ENB_EXPLORE_WORKDIR not writable: $ENB_EXPLORE_WORKDIR"

# X reachable (the client is a WINE GUI; no display == every shot/click fails).
command -v xdotool >/dev/null 2>&1 || die "xdotool not found (need it to drive the client)."
xdotool getdisplaygeometry >/dev/null 2>&1 || \
    die "cannot reach X on DISPLAY=$DISPLAY. Is the desktop session up?"

# The live client must already be running. We do NOT launch it -- that is the
# operator's job in live mode. Reuse the login-to-client skill's window finder
# (client_win); the OCR-era explore-sector lib.sh is gone with the screen driver.
# shellcheck source=/dev/null
. "$SCRIPT_DIR/../../login-to-client/scripts/lib.sh"
if ! CLIENT_WIN="$(client_win)"; then
    die "no client.exe window found. Launch your live client (and Net7Proxy.exe)
       under WINE and leave it at the character-select screen, then re-run."
fi
note "client.exe window: $CLIENT_WIN"

# The proxy must be running -- the client talks through it. WHICH proxy is up
# also decides the run mode and whether we capture:
#   * WINE Net7Proxy.exe running  -> LIVE run against the real reference server;
#     capture is the point, keep ENB_PCAP as configured.
#   * docker 'net7proxy' running (no WINE proxy) -> LOCAL run against our own
#     freya stack. Owner rule (2026-06-22): NEVER pcap a local freya run --
#     capture is for the live server only. Force ENB_PCAP=0 and point the proxy
#     name at the docker proxy so the rest of the survey is unaffected.
if pgrep -x -- "$ENB_EXPLORE_PROXY_NAME" >/dev/null 2>&1; then
    note "proxy running (LIVE): $ENB_EXPLORE_PROXY_NAME -- capture stays ENB_PCAP=$ENB_PCAP"
elif pgrep -x net7proxy >/dev/null 2>&1 || pgrep -f '/app/net7proxy' >/dev/null 2>&1; then
    export ENB_EXPLORE_PROXY_NAME=net7proxy ENB_PCAP=0
    note "WINE proxy down but docker net7proxy is up -> LOCAL freya run: capture disabled (ENB_PCAP=0)"
else
    die "no proxy running (neither WINE '$ENB_EXPLORE_PROXY_NAME' nor docker
       net7proxy). Start the proxy the client connects through, then re-run."
fi

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

# The Lua driver needs the character IN-GAME -- the enbmod channel only answers
# once enb.self() is a table. Confirm it now (08 returns at once if already
# in-game) rather than letting drive_lua.py exit on a silent channel. We do NOT
# log the character in here: that is the login-to-client skill's job (and in live
# mode we do not own the client to relaunch it).
LOGIN_DIR="$SCRIPT_DIR/../../login-to-client/scripts"
if ! bash "$LOGIN_DIR/08-wait-ingame.sh" >&2; then
    die "client is not in-game (enb.self() is not a table). Log the character into
       the world first (run the login-to-client skill, or enter your character on
       the live client), then re-run explore-live.sh."
fi

# ---- 4. hand off to the survey -------------------------------------------
note "config OK. character=$ENB_EXPLORE_CHARACTER_NAME workdir=$ENB_EXPLORE_WORKDIR" \
     "pcap=$ENB_PCAP"
note "starting survey (Lua command channel, no OCR) ..."
exec python3 "$SCRIPT_DIR/drive_lua.py" "$@"
