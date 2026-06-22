#!/usr/bin/env bash
# SPDX-License-Identifier: MIT
# Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
# License: LICENSES/Freya
#
# wreck.sh [full-shot.png] -- detect whether the ship has been DESTROYED. When you
# are wrecked the game shows a death panel with a "Request Tow" button (returns you
# to your last registered station). We find that button by OCRing the screen for the
# word "Tow" (the most distinctive, reliably-spelled token) and locating its box --
# we do NOT hardcode a coord, because the button position can only be calibrated
# against a real death. Once you DO know the coord from a real wreck, set
# ENB_TOW_BTN="x y" to skip OCR and click it directly.
#
# Prints one of:
#   WRECKED <winx> <winy>     dead; click (winx,winy) to Request Tow
#   ALIVE                     no death panel detected
#
# Pass a screenshot path as $1 to inspect an existing shot.
set -uo pipefail
SKILL_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"; . "$SKILL_DIR/lib.sh"

# Hardcode escape hatch once a real death pins the button.
if [ -n "${ENB_TOW_BTN:-}" ]; then
    # still confirm we are actually wrecked before reporting the hardcoded button
    :
fi

if [ -n "${1:-}" ]; then
    full="$1"
else
    full="$(explore_shot "")" || { elog "screenshot failed"; exit 1; }
fi

[ -x "$(command -v tesseract)" ] || { echo "ALIVE"; elog "no tesseract"; exit 0; }

tsv="$SCRATCH/wreck-ocr"
# Upscale + grayscale for steadier OCR of the dialog text over the starfield.
convert "$full" -colorspace Gray -normalize -resize 150% "$SCRATCH/wreck-pre.png" 2>/dev/null
tesseract "$SCRATCH/wreck-pre.png" "$tsv" tsv 2>/dev/null || { echo "ALIVE"; exit 0; }

# tsv columns: level page block par line word left top width height conf text
# Find a "tow" token with decent confidence; scale boxes back from the 150% image.
read -r cx cy <<< "$(
  awk -F'\t' 'NR>1 && $11>30 {
      t=tolower($12); gsub(/[^a-z]/,"",t);
      if (t=="tow" || t=="requesttow") {
          print int(($7 + $9/2)/1.5), int(($8 + $10/2)/1.5); exit
      }
  }' "$tsv.tsv"
)"

if [ -n "${ENB_TOW_BTN:-}" ] && [ -n "$cx" ]; then
    echo "WRECKED ${ENB_TOW_BTN}"          # confirmed dead; use the pinned coord
elif [ -n "$cx" ]; then
    echo "WRECKED $cx $cy"
else
    echo "ALIVE"
fi
