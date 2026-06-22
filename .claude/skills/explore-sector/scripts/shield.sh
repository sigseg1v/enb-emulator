#!/usr/bin/env bash
# SPDX-License-Identifier: MIT
# Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
# License: LICENSES/Freya
#
# shield.sh [full-shot.png] -- read the ship's SHIELD level from the bottom HUD.
#
# The shield is the long horizontal BLUE bar at the bottom of the screen, right of
# centre (~75% down, ~20% inward from centre), to the right of the short red hull
# bar. We do NOT hover for a tooltip: the EnB HUD tooltip does not render under
# synthetic (XTEST) hover in WINE, and hovering would also knock the mouse off the
# target-cycle arrows. Instead we measure the bar's FILL directly -- it shrinks
# from the right as the shield drops and is empty at 0.
#
# The bar is SEMI-TRANSPARENT, so its rendered colour tracks the background; no
# single absolute colour is stable across every scene. What IS stable is that a
# FILLED segment is saturated blue where blue strongly dominates red and green,
# while the EMPTY background track stays dark (low blue):
#   - FILLED  = saturated blue: b>130, b>r+60, b>g+50.
#   - EMPTY   = dark-blue BACKGROUND track (b~50-110). An earlier "any blue"
#     reader falsely read ~99% on a 0%/wrecked ship by counting this track as fill.
# We do NOT use the rightmost single matching pixel -- a few specular highlight
# pixels in the dark bg peg that to ~full. Instead we scan by COLUMN: a real fill
# column is saturated-blue over >= MINROWS rows; stray noise (1-2 px) is rejected.
# The shield level is the rightmost FILLED column.
# Validated 2026-06-21 on the live in-space HUD: full -> 97.2, wrecked/0% -> 0.0.
# Known limitation: a full bar rendered over a BRIGHT background (washes toward
# white-blue, g approaches b) can under-read -- the cost of a translucent bar.
#
# Prints:
#   SHIELD <pct>   fill 0..100 (rightmost light-blue FILLED column in the frame)
#   BLUE   <n>     light-blue fill pixel count in the box (secondary / sanity)
#
# Band is window-relative (client renders a fixed 1280x960). Override if UI moves:
#   ENB_SHIELD_BOX="l r t b"   default "823 965 852 866"
# Pass a screenshot path as $1 to read an existing shot instead of capturing one.
set -uo pipefail
SKILL_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"; . "$SKILL_DIR/lib.sh"

if [ -n "${1:-}" ]; then
    full="$1"
else
    full="$(explore_shot "")" || { elog "screenshot failed"; exit 1; }
fi

ENB_SHIELD_BOX="${ENB_SHIELD_BOX:-823 965 852 866}" python3 - "$full" <<'PY'
import os, sys
from PIL import Image
L, R, T, B = (int(v) for v in os.environ["ENB_SHIELD_BOX"].split())
MINROWS = int(os.environ.get("ENB_SHIELD_MINROWS", "4"))   # column must be this tall to count as fill
im = Image.open(sys.argv[1]).convert("RGB"); px = im.load()
edge, cnt = -1, 0
for x in range(L, R):
    col = 0
    for y in range(T, B):
        r, g, b = px[x, y]
        if b > 130 and b > r + 60 and b > g + 50:   # saturated-blue FILL (not dark-blue empty bg)
            col += 1
            cnt += 1
    if col >= MINROWS and x > edge:
        edge = x
span = R - L
pct = round(100 * (edge - L) / span, 1) if edge >= 0 else 0.0
print(f"SHIELD {pct}")
print(f"BLUE {cnt}")
PY
