#!/usr/bin/env bash
# SPDX-License-Identifier: MIT
# Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
# License: LICENSES/Freya
#
# read-sector.sh -- read the AUTHORITATIVE current sector name off the in-game
# map's top-centre title.
#
# WHY THIS EXISTS: inferring the sector from the W/D/C nav-label OCR is unsafe --
# a Request Tow can silently carry the ship to a DIFFERENT sector (e.g. a wreck in
# Freya towed us to Adriel Prime), and navdata's fuzzy match will then force the
# foreign nav names onto the closest in-sector names at garbage ratios, so the
# driver flies the wrong sector while believing it is on track. The map prints the
# real sector name in a fixed spot; that is the ground truth.
#
# The sector name renders as white text at the TOP-CENTRE of the map body, just
# below the toolbar/XYZ readout. After open-map.sh normalises the map (maximize +
# center + zoom-out), it is always at the same window-relative box.
#
# Prints:
#   SECTOR <file-name>       canonical navdata sector (CamelCase), e.g. AdrielPrime
#   TITLE  <raw ocr title>   the OCR'd map title, e.g. "Adriel Prime"
# Exit 0 on a confident read, 1 if the title could not be read/matched.
#
#   ENB_SECTOR_BOX="l t w h"  title crop box (default "333 246 327 32")
set -uo pipefail
SKILL_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"; . "$SKILL_DIR/lib.sh"

read -r l t w h <<< "${ENB_SECTOR_BOX:-333 246 327 32}"

# Make sure the map is open + normalised so the title is at the known box.
read -r _ FULL <<< "$(bash "$SKILL_DIR/open-map.sh" 2>/dev/null | grep '^FULL')"
[ -n "${FULL:-}" ] && [ -s "$FULL" ] || { full="$(explore_shot "")" || { echo "FAIL no shot"; exit 1; }; FULL="$full"; }

# Crop the title box, isolate the bright text, upscale, OCR.
raw="$(python3 - "$FULL" "$l" "$t" "$w" "$h" "$SCRATCH/.sectorname_bw.png" <<'PY'
import sys, subprocess
from PIL import Image
src,l,t,w,h = sys.argv[1], *map(int, sys.argv[2:6])
bw = sys.argv[6]
im = Image.open(src).convert("RGB").crop((l,t,l+w,t+h))
px = im.load(); W,H = im.size
o = Image.new("L",(W,H),255); op=o.load()
for y in range(H):
    for x in range(W):
        r,g,b = px[x,y]
        if r>120 and g>120 and b>120: op[x,y]=0
o = o.resize((W*4,H*4))
o.save(bw)
out = subprocess.run(["tesseract",bw,"-","--psm","7"],
                     capture_output=True, text=True).stdout
print(out.strip())
PY
)"
raw="$(printf '%s\n' "$raw" | tr -d '\r' | sed 's/[[:space:]]\+/ /g; s/^ *//; s/ *$//')"

[ -n "$raw" ] || { echo "FAIL empty title"; exit 1; }

# Resolve the OCR title to a canonical navdata sector file via navdata.py identify
# (its title-match handles CamelCase + planet-suffix folding).
sector="$(python3 "$SKILL_DIR/navdata.py" identify "$raw" 2>/dev/null | awk -F'\t' 'NR==1{print $1}')"

echo "TITLE $raw"
if [ -n "$sector" ]; then
    echo "SECTOR $sector"
    exit 0
fi
echo "FAIL no match for '$raw'"
exit 1
