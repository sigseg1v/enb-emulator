#!/usr/bin/env bash
# SPDX-License-Identifier: MIT
# Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
# License: LICENSES/Freya
#
# recover.sh -- bring a crashed/wedged live client back to in-game, no OCR.
#
# Called two ways:
#   * by explore-live.sh at startup when the client is not yet in-game, and
#   * by drive_lua.py via ENB_RECOVER_CMD when the survey detects a wedge.
# Either way the environment already carries the resolved config (ENB_WINEPREFIX,
# ENB_LAUNCHNET7, ENB_CLIENT_DIR, ENB_ACC_NAME/PASS/CHARACTER, ENB_EULA); this
# script is self-sufficient if run standalone too (it re-sources the .env).
#
# Flow:
#   1. If a client already answers the enbmod channel in-game, exit 0 (no-op).
#   2. Kill the wedged/stale client.exe.
#   3. Launch the real Net-7 launcher (LaunchNet7.exe) under WINE with the autologin
#      credentials exported, so the client.exe it spawns inherits them.
#   4. Click Play in the launcher (Play launches net7proxy + client.exe).
#   5. Wait for the client window, then inject enbmod (inject.exe --proc client.exe).
#   6. enbmod autologin accepts the EULA, logs in, and enters ENB_CHARACTER from the
#      inherited env -- wait for the enbmod channel to report in-game (08-wait-ingame).
# Exit 0 == in-game; non-zero == could not recover (operator/agent must step in).
#
# UNVERIFIED end-to-end on this box: there is currently no LaunchNet7 GUI up to pin
# the Play-button coordinate, so the Play click is coordinate-driven from
# ENB_LAUNCHNET7_PLAY_XY and, if that is unset, this script screenshots the launcher
# and stops asking for the coordinate (see step 4). Everything else (launch, window
# wait, inject, autologin wait) uses proven primitives.
set -uo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SKILL_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"
. "$SCRIPT_DIR/../../login-to-client/scripts/lib.sh"
r_log()  { printf '[recover] %s\n' "$*" >&2; }
r_die()  { printf '[recover] FATAL: %s\n' "$*" >&2; exit 1; }

# Re-source .env when run standalone (values already exported when called in-process).
ENV_FILE="$SKILL_DIR/.env"
[ -f "$ENV_FILE" ] && { set -a; . "$ENV_FILE"; set +a; }

: "${DISPLAY:=:0}"; export DISPLAY
: "${ENB_WINEPREFIX:=${HOME}/.wine-enb}"; export WINEPREFIX="$ENB_WINEPREFIX"
: "${ENB_LAUNCHNET7:=$ENB_WINEPREFIX/drive_c/Program Files (x86)/Net-7/bin/LaunchNet7.exe}"
: "${ENB_CLIENT_DIR:=$ENB_WINEPREFIX/drive_c/Program Files/EA GAMES/Earth & Beyond/release}"
: "${ENB_EULA:=ACCEPT}"
WORKDIR="${ENB_EXPLORE_WORKDIR:-$SKILL_DIR/state}"; mkdir -p "$WORKDIR" 2>/dev/null
INGAME="$SCRIPT_DIR/../../login-to-client/scripts/08-wait-ingame.sh"
WINE="${ENB_WINE:-/usr/bin/wine}"

for v in ENB_ACC_NAME ENB_ACC_PASS ENB_CHARACTER; do
    [ -n "${!v:-}" ] || r_die "$v is not set (need it for enbmod autologin). Fill $ENV_FILE."
done
[ -f "$ENB_LAUNCHNET7" ] || r_die "LaunchNet7.exe not found at ENB_LAUNCHNET7=$ENB_LAUNCHNET7. Set it in $ENV_FILE."

# ---- 1. already in-game? no-op ----------------------------------------------
if ENB_CLIENT_DIR="$ENB_CLIENT_DIR" bash "$INGAME" >/dev/null 2>&1; then
    r_log "client already in-game -- nothing to recover."
    exit 0
fi

# ---- 2. kill the wedged/stale client ----------------------------------------
r_log "killing stale client.exe (if any) before relaunch"
pkill -9 -f 'EA GAMES\\Earth & Beyond\\release\\client.exe' 2>/dev/null
pkill -9 -f 'release/client.exe' 2>/dev/null
pkill -9 -x 'client.exe' 2>/dev/null
sleep 2

# ---- 3. launch LaunchNet7 with autologin creds in the environment -----------
# enbmod autologin (autologin.cpp) reads FREYA_/ENB_ ACC_NAME/ACC_PASS/CHARACTER/EULA
# from the client PROCESS env; client.exe inherits it from LaunchNet7, which inherits
# it from here. No credentials are typed or screen-read.
export ENB_ACC_NAME ENB_ACC_PASS ENB_CHARACTER ENB_EULA
export FREYA_ACC_NAME="$ENB_ACC_NAME" FREYA_ACC_PASS="$ENB_ACC_PASS" \
       FREYA_CHARACTER="$ENB_CHARACTER" FREYA_EULA="$ENB_EULA"
r_log "launching LaunchNet7.exe (prefix=$WINEPREFIX)"
"$WINE" start /d "C:\\Program Files (x86)\\Net-7\\bin" "LaunchNet7.exe" >/dev/null 2>&1 &

# ---- 4. click Play in the launcher ------------------------------------------
# Wait for the launcher window (LaunchNet7 is a .NET WinForms app; match its title).
launchnet7_win() {
    local id
    for pat in '^LaunchNet7' 'LaunchNet7' 'Net-7' 'Net7'; do
        id="$(xdotool search --name "$pat" 2>/dev/null | head -1)"
        [ -n "$id" ] && { echo "$id"; return 0; }
    done
    return 1
}
r_log "waiting for the LaunchNet7 window ..."
if ! wait_until 60 2 launchnet7_win >/dev/null; then
    r_die "LaunchNet7 window never appeared (launch failed? check the WINE prefix)."
fi
LN7_WIN="$(launchnet7_win)"
xdotool windowactivate --sync "$LN7_WIN" 2>/dev/null
sleep 1

if [ -n "${ENB_LAUNCHNET7_PLAY_XY:-}" ]; then
    # window-relative "x,y" -> absolute root coords (XTEST, focus-independent).
    read -r ax ay _ _ <<<"$(win_abs "$LN7_WIN")"
    rx="${ENB_LAUNCHNET7_PLAY_XY%,*}"; ry="${ENB_LAUNCHNET7_PLAY_XY#*,}"
    r_log "clicking Play at window-rel ($rx,$ry) -> abs ($((ax+rx)),$((ay+ry)))"
    xdotool mousemove --sync $((ax+rx)) $((ay+ry)) click 1
else
    # No pinned coordinate: try the launcher's default action (Enter), then, if that
    # does not spawn the client, screenshot the launcher and ask for the coordinate.
    r_log "no ENB_LAUNCHNET7_PLAY_XY set -- trying Enter as the default action"
    xdotool key --window "$LN7_WIN" Return 2>/dev/null
    if ! wait_until 25 2 win_by_class client.exe >/dev/null 2>&1; then
        shot="$WORKDIR/launchnet7.png"
        import -window "$LN7_WIN" "$shot" 2>/dev/null || xdotool getdisplaygeometry >/dev/null
        r_die "Enter did not start the client. Set ENB_LAUNCHNET7_PLAY_XY=<x>,<y> in
    $ENV_FILE (window-relative pixel of the Play button). Screenshot of the
    launcher saved to: $shot -- read it to find Play's coordinate."
    fi
fi

# ---- 5. wait for the client window, then inject enbmod -----------------------
r_log "waiting for client.exe window ..."
if ! wait_until 120 3 win_by_class client.exe >/dev/null 2>&1; then
    r_die "client.exe window never appeared after Play (patch in progress? launcher stuck?)."
fi
# Let the client settle onto the login screen before injecting (autologin needs the
# login/char UI classes loaded to drive them).
sleep 12
INJECT="$ENB_CLIENT_DIR/inject.exe"
[ -f "$INJECT" ] || r_die "inject.exe not found at $INJECT (deploy it next to enbmod.dll)."
r_log "injecting enbmod: $INJECT --proc client.exe"
"$WINE" "$INJECT" --proc client.exe >/dev/null 2>&1 || \
    r_log "inject.exe returned non-zero (may already be injected) -- continuing to the in-game check"

# ---- 6. wait for enbmod autologin to reach in-game --------------------------
r_log "waiting for enbmod autologin to reach in-game (char=$ENB_CHARACTER) ..."
for _ in $(seq 1 24); do   # ~4 min: EULA + login + char-enter + scene load
    if ENB_CLIENT_DIR="$ENB_CLIENT_DIR" bash "$INGAME" >/dev/null 2>&1; then
        r_log "==== RECOVERED: client is in-game ===="
        exit 0
    fi
    sleep 10
done
r_die "client relaunched + injected but never reached in-game (autologin creds wrong, or stuck at a screen)."
