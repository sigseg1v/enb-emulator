#!/usr/bin/env bash
# SPDX-License-Identifier: MIT
# Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
# License: LICENSES/Freya
#
# read-navname.sh [out.png] -- after you click a node on the sector map, the
# selected nav prints as "Dest: <nav name>" in a fixed strip at the bottom of the
# map body. This crops that strip, upscales + thresholds it, runs tesseract, strips
# the "Dest: " prefix, and prints the nav name:
#   NAVNAME <name>
#   (also: RAW <full ocr line>  and  CROP <png> for inspection)
#
# The strip is a 580x20 box whose top-left is window-rel (105, 758). That is the
# owner-given (105, 790) measured INCLUDING the 32px WM title bar; `import -window`
# captures the content area only, so we subtract 32 from Y. Override via:
#   ENB_NAVNAME_BOX="l t w h"   default "105 758 580 20"
#   ENB_NAVNAME_SCALE=N         upscale factor, default 4
set -uo pipefail
SKILL_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"; . "$SKILL_DIR/lib.sh"

read -r l t w h <<< "${ENB_NAVNAME_BOX:-105 758 580 20}"
scale="${ENB_NAVNAME_SCALE:-4}"

full="$(explore_shot "")" || { elog "screenshot failed"; exit 1; }
crop="$WORKDIR/$(basename "${full%.png}")-navname.png"
out="${1:-$crop}"

# Crop the strip, scale up, grayscale + contrast-boost so the light text on the
# dark HUD reads cleanly for tesseract.
convert "$full" -crop "${w}x${h}+${l}+${t}" +repage \
    -filter Lanczos -resize "$((scale * 100))%" \
    -colorspace Gray -normalize -level 25%,100% "$out" 2>/dev/null \
    || { elog "crop/scale failed"; exit 1; }

raw=""
if command -v tesseract >/dev/null 2>&1; then
    raw="$(tesseract "$out" - --psm 7 2>/dev/null | tr -d '\r' | sed 's/[[:space:]]\+/ /g; s/^ *//; s/ *$//')"
else
    elog "tesseract not installed -- READ $out by eye"
fi

# Strip the leading "Dest" prefix + whatever punctuation/space follows it. Tesseract
# renders the colon inconsistently ("Dest:", "Dest.", or just "Dest "), so we match
# the word "Dest" (case-insensitive) and eat any non-alphanumerics after it. If the
# line has no such prefix, fall back to the whole line.
name="$(printf '%s\n' "$raw" | sed -E 's/^[Dd][Ee][Ss][Tt][^[:alnum:]]*//')"
[ -z "$name" ] && name="$raw"

echo "NAVNAME $name"
echo "RAW $raw"
echo "CROP $out"
