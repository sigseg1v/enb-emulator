#!/usr/bin/env bash
# SPDX-License-Identifier: MIT
# Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
# License: LICENSES/Freya
#
# map-shot.sh [out.png] -- screenshot the client and crop to the map region
# WITHOUT touching the map key. Use this for every shot AFTER the map is already
# open (open-map.sh presses M, which TOGGLES, so calling it twice closes the
# map). Prints:
#   FULL <full-shot.png>
#   MAP  <cropped-map.png> <X0> <Y0>
set -uo pipefail
SKILL_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"; . "$SKILL_DIR/lib.sh"

full="$(explore_shot "${1:-}")" || { elog "screenshot failed"; exit 1; }
crop="$WORKDIR/$(basename "${full%.png}")-map.png"
read -r cpath x0 y0 <<< "$(crop_map "$full" "$crop")"
echo "FULL $full"
echo "MAP $cpath $x0 $y0"
