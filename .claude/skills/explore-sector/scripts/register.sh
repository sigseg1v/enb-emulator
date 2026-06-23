#!/usr/bin/env bash
# SPDX-License-Identifier: MIT
# Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
# License: LICENSES/Freya
#
# register.sh [full-shot.png] -- locate the station REGISTER button.
#
# When a STATION is selected the HUD shows action buttons at the bottom-right:
#   - the MAIN button is "Dock", at the SAME spot as the gate jump icon
#     (ENB_GATE_ICON, default 1236,641);
#   - the SECONDARY button to its LEFT, when present, is "Register".
# Registering sets that station as our recall/respawn point, so a later wreck tows
# us to THIS (closer) station instead of the previous one, and grants explore XP.
# We register once per station per sector.
#
# The button is POSITIONAL (left of Dock), not OCR'd. The exact left offset is
# pinned by ENB_REGISTER_BTN once a real station calibrates it; until then we use a
# best-estimate offset left of the Dock/gate coord. Every call drops the current
# frame at ../state/shielddbg/register_seen.png so the coord can be eyeballed and
# pinned the first time we actually reach a station.
#
# Prints:
#   REGISTER <winx> <winy>    click (winx,winy) to register
#
# Env:
#   ENB_REGISTER_BTN="x y"    pin the Register button outright (skips the estimate)
#   ENB_GATE_ICON="x y"       Dock/gate coord (default "1236 641")
#   ENB_REGISTER_DX=N         pixels LEFT of Dock for the Register disc (default 49,
#                             pinned 2026-06-21 from Margesi Station: the two action
#                             discs sit ~49px centre-to-centre, Dock at 1236,641)
# Pass a screenshot path as $1 to just snapshot the frame for calibration.
set -uo pipefail
SKILL_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"; . "$SKILL_DIR/lib.sh"
DBG="${ENB_EXPLORE_WORKDIR:-$SKILL_DIR/../state}/shielddbg"; mkdir -p "$DBG"

if [ -n "${1:-}" ]; then
    full="$1"
else
    full="$(explore_shot "")" || { elog "screenshot failed"; exit 1; }
fi
cp -f "$full" "$DBG/register_seen.png" 2>/dev/null || true

if [ -n "${ENB_REGISTER_BTN:-}" ]; then
    echo "REGISTER ${ENB_REGISTER_BTN}"
    exit 0
fi

read -r dx dy <<< "${ENB_GATE_ICON:-1236 641}"
off="${ENB_REGISTER_DX:-49}"
echo "REGISTER $((dx - off)) $dy"
