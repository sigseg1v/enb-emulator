#!/usr/bin/env bash
# SPDX-License-Identifier: MIT
# Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
# License: LICENSES/Freya
#
# click-map.sh <winx> <winy> -- left-click the client at WINDOW-RELATIVE coords
# (0,0 = window top-left, the same frame as a full client screenshot). Use it to
# click a nav node on the open map. When you read a nav at pixel (cx,cy) of a
# CROPPED map shot, the window coords are (X0+cx, Y0+cy) using the X0/Y0 that
# open-map.sh printed.
set -uo pipefail
SKILL_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"; . "$SKILL_DIR/lib.sh"

[ "$#" -eq 2 ] || { echo "usage: click-map.sh <winx> <winy>" >&2; exit 2; }
id="$(client_win)" || { elog "no client window"; exit 1; }
click_win "$id" "$1" "$2"
