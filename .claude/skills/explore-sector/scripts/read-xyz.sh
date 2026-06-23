#!/usr/bin/env bash
# SPDX-License-Identifier: MIT
# Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
# License: LICENSES/Freya
#
# read-xyz.sh [out.png] -- read the X:/Y:/Z: position readout the map's 3rd toolbar
# button ("xyz", enabled by open-map.sh) prints at the map top-left. The readout is
# BLUE when it shows YOUR SHIP's current position and YELLOW when you HOVER a nav
# node (it then shows THAT node's position). This crops the readout, OCRs the three
# numbers, and classifies the colour:
#   XYZ <x> <y> <z>
#   COLOUR blue|yellow|none      (blue=ship, yellow=hovered node)
#   RAW <ocr text>
#   CROP <png>
#
# Identify a node WITHOUT clicking it: hover its marker (hover-node.sh / mousemove),
# read-xyz.sh, then `navdata.py nearest <Sector> <x> <y> <z>` to snap to the node.
#
# The readout box is window-rel; override if the UI moves:
#   ENB_XYZ_BOX="l t w h"   default "34 250 130 48"
#   ENB_XYZ_SCALE=N         upscale factor, default 5
set -uo pipefail
SKILL_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"; . "$SKILL_DIR/lib.sh"

read -r l t w h <<< "${ENB_XYZ_BOX:-34 250 130 48}"
scale="${ENB_XYZ_SCALE:-5}"

full="$(explore_shot "")" || { elog "screenshot failed"; exit 1; }
crop="$SCRATCH/$(basename "${full%.png}")-xyz.png"
out="${1:-$crop}"

# Classify the readout colour from the raw crop (before grayscale) so we can tell a
# ship reading (blue) from a hovered-node reading (yellow).
colour="$(python3 - "$full" "$l" "$t" "$w" "$h" <<'PY'
import sys, numpy as np
from PIL import Image
p, l, t, w, h = sys.argv[1], *(int(a) for a in sys.argv[2:6])
im = np.asarray(Image.open(p).convert("RGB")).astype(int)
b = im[t:t+h, l:l+w]
R, G, B = b[:, :, 0], b[:, :, 1], b[:, :, 2]
blue = ((B > 110) & (B - R > 40) & (B - G > 25)).sum()
yellow = ((R > 140) & (G > 120) & (B < 110) & (R - B > 50) & (G - B > 40)).sum()
if blue < 12 and yellow < 12:
    print("none")
elif yellow > blue:
    print("yellow")
else:
    print("blue")
PY
)"

# OCR prep: the glyphs are thin BLUE (ship) or YELLOW (node) text on a near-black,
# semi-transparent overlay. A plain grayscale dims blue text (luminance weights
# green low), which misreads digits. Instead build a clean BLACK-on-WHITE binary
# from the text colour mask (blue OR yellow lit), then upscale for tesseract.
python3 - "$full" "$l" "$t" "$w" "$h" "$scale" "$out" <<'PY'
import sys, numpy as np
from PIL import Image
p, l, t, w, h, sc, out = sys.argv[1], *(int(a) for a in sys.argv[2:7]), sys.argv[7]
im = np.asarray(Image.open(p).convert("RGB")).astype(int)
b = im[t:t+h, l:l+w]
R, G, B = b[:, :, 0], b[:, :, 1], b[:, :, 2]
blue = (B > 95) & (B - R > 30) & (B - G > 18)
yellow = (R > 130) & (G > 110) & (B < 120) & (R - B > 40) & (G - B > 30)
text = blue | yellow
# black text on white background
img = np.where(text[:, :, None], 0, 255).astype("uint8").repeat(3, axis=2)
im2 = Image.fromarray(img).resize((w * sc, h * sc), Image.LANCZOS)
im2.save(out)
PY
[ -f "$out" ] || { elog "crop/binarize failed"; exit 1; }

raw=""
if command -v tesseract >/dev/null 2>&1; then
    # whitelist digits, signs, dots and the axis letters; psm 6 = uniform block
    raw="$(tesseract "$out" - --psm 6 \
        -c tessedit_char_whitelist='XYZxyz:-.0123456789' 2>/dev/null \
        | tr -d '\r' | tr '\n' ' ' | sed 's/[[:space:]]\+/ /g; s/^ *//; s/ *$//')"
else
    elog "tesseract not installed -- READ $out by eye"
fi

# Pull the three signed decimals in order. The labels are X/Y/Z top-to-bottom.
read -r x y z <<< "$(printf '%s\n' "$raw" | grep -oE '[-]?[0-9]+\.[0-9]+' | head -3 | tr '\n' ' ')"

echo "XYZ ${x:-?} ${y:-?} ${z:-?}"
echo "COLOUR $colour"
echo "RAW $raw"
echo "CROP $out"
