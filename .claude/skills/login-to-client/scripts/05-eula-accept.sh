#!/usr/bin/env bash
# SPDX-License-Identifier: MIT
# Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
# License: LICENSES/Freya
#
# 05-eula-accept.sh -- accept the "Service Rules of Conduct" (EULA) dialog.
# The EULA is its OWN top-level window (506x362), distinct from the main 1280x960
# client window that appears after. We confirm the EULA by its window size, click
# "I Agree" (bottom-right), then wait for the 1280x960 game window to appear.
# Idempotent: if the big window is already up, the EULA is already past -- exit 0.
SKILL_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
. "$SKILL_DIR/lib.sh"

_big_client_win() {
    local id; id="$(client_win)" || return 1
    read -r _ _ ww _ <<< "$(win_abs "$id")"
    [ -n "$ww" ] && [ "$ww" -ge 1000 ]
}

W="$(client_win)" || { err "STUCK=eula: no client.exe window"; exit 1; }
read -r x y w h <<< "$(win_abs "$W")"
log "client window $W is ${w}x${h} at $x,$y"

# Already past the EULA? (big window present)
if [ "$w" -ge 1000 ]; then
    log "STATE=past-eula (window already ${w}x${h})"
    exit 0
fi

read -r ew eh <<< "$(coord eula window_size)"
if [ "$w" != "$ew" ] || [ "$h" != "$eh" ]; then
    err "STUCK=eula: window is ${w}x${h}, expected EULA ${ew}x${eh}"
    exit 1
fi

log "accepting EULA (I Agree)"
click_coord "$W" eula agree

# Wait for the EULA window to be replaced by the 1280x960 game window.
log "waiting for 1280x960 game window ..."
if wait_until 30 1 _big_client_win; then
    NW="$(client_win)"
    log "STATE=past-eula (window $NW, $(win_abs "$NW"))"
    exit 0
fi
err "STUCK=eula: no 1280x960 window appeared after I Agree"
exit 1
