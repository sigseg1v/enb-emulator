#!/usr/bin/env bash
# SPDX-License-Identifier: MIT
# Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
# License: LICENSES/Freya
#
# hover-node.sh <winx> <winy> -- move the mouse ONTO a nav marker on the open map
# (window-relative coords, same frame as a full screenshot) WITHOUT clicking, and
# settle. When the pointer is over a node the map's X/Y/Z readout turns YELLOW and
# shows that node's position -- read it with read-xyz.sh and snap it to a name with
# `navdata.py nearest <Sector> <x> <y>`. This identifies a node without selecting it
# (no Dest change, no warp-path replot).
#
# Plain mousemove (NO --sync, which hangs against the VM's absolute pointer device).
set -uo pipefail
SKILL_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"; . "$SKILL_DIR/lib.sh"

[ "$#" -eq 2 ] || { echo "usage: hover-node.sh <winx> <winy>" >&2; exit 2; }
id="$(client_win)" || { elog "no client window"; exit 1; }
raise_win "$id"; sleep 0.15
g="$(win_abs "$id")" || { elog "no geometry for $id"; exit 1; }
read -r gx gy gw gh <<< "$g"
xdotool mousemove $((gx + $1)) $((gy + $2)); sleep 0.45
