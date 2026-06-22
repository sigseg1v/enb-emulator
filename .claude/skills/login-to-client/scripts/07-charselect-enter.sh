#!/usr/bin/env bash
# SPDX-License-Identifier: MIT
# Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
# License: LICENSES/Freya
#
# 07-charselect-enter.sh -- on the character-select screen, click the first
# character's nameplate, then click Enter. Confirms success by the screen
# LEAVING character-select (the load screen begins). The actual "in space /
# in station, fully loaded" confirmation is 08-wait-ingame.sh via the Lua channel.
SKILL_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
. "$SKILL_DIR/lib.sh"

_left_charselect() { ! on_charselect; }

W="$(client_win)" || { err "STUCK=charselect: no client.exe window"; exit 1; }

if ! on_charselect; then
    # Maybe we are already loading / in game.
    err "WARN=charselect: not on character-select screen (already entered? check 08)"
    exit 0
fi

log "selecting first character"
click_coord "$W" charselect first
sleep 0.6

# Per-sector packet capture (explore-sector survey): start the FIRST sector's
# capture BEFORE pressing Enter so it includes the entry handshake. Opt-in via
# ENB_PCAP=1; drive.py relabels this placeholder to the real sector name once the
# sector is identified. Best-effort, cross-skill -- never blocks login.
if [ "${ENB_PCAP:-0}" = 1 ]; then
    PCAP="$SKILL_DIR/../../explore-sector/scripts/pcap.sh"
    [ -x "$PCAP" ] && bash "$PCAP" start "${ENB_PCAP_SECTOR:-entry}" || true
fi

log "clicking Enter"
click_coord "$W" charselect enter

log "waiting to leave character-select (load screen begins) ..."
# on_charselect should go false once the load starts.
if wait_until 30 2 _left_charselect; then
    log "STATE=loading (left character-select)"
    exit 0
fi
err "STUCK=charselect: still on character-select after Enter"
exit 1
