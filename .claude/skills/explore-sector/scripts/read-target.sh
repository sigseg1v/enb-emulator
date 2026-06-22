#!/usr/bin/env bash
# SPDX-License-Identifier: MIT
# Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
# License: LICENSES/Freya
#
# read-target.sh [full-shot.png] -- read the SELECTED-TARGET panel in the screen's
# bottom-right corner. It shows the currently selected / closest-in-warp-path node
# as a NAME line (top) over a thumbnail and a "Dist: <n>k" line (bottom). While the
# ship is warping, the game keeps this panel updated with the closest node on the
# warp path and the live distance to it -- so polling this panel during a warp is
# how we detect every node we pass and how close we got (<=2k == "visited").
#
# Prints:
#   TARGET <name>        node name OCR'd from the panel ("?" if blank)
#   DIST   <k>           distance in k (thousands of units) as a float, "?" if none
#   RAWNAME <ocr>
#   RAWDIST <ocr>
#
# Both boxes are window-relative (the client renders a fixed 1280x960). The text is
# the clean white HUD font (NOT the stylized blue/yellow X/Y/Z readout), so a plain
# grayscale + normalize + tesseract reads it reliably. Override if the UI moves:
#   ENB_TGT_NAME_BOX="l t w h"   default "1035 686 220 30"
#   ENB_TGT_DIST_BOX="l t w h"   default "992 906 160 30"
# Pass a screenshot path as $1 to read an existing shot instead of capturing one.
set -uo pipefail
SKILL_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"; . "$SKILL_DIR/lib.sh"

read -r nl nt nw nh <<< "${ENB_TGT_NAME_BOX:-1035 686 220 30}"
read -r dl dt dw dh <<< "${ENB_TGT_DIST_BOX:-992 906 160 30}"

# ox,oy map window-relative box coords into the captured image. For a passed-in
# full window shot they are 0 (the shot IS the window). For the fast path we grab
# ONLY the bounding region of the two boxes straight off the root window
# (`import -window root -crop` is ~3x faster than a full 1280x960 window grab and
# we OCR nothing else), so window-rel (x,y) sits at (x-L, y-T) in that region.
ox=0; oy=0
if [ -n "${1:-}" ]; then
    full="$1"
else
    win="$(client_win)" || { elog "no client window"; exit 1; }
    raise_win "$win"
    read -r gx gy gw gh <<< "$(win_abs "$win")"
    L=$(( nl < dl ? nl : dl )); T=$(( nt < dt ? nt : dt ))
    R=$(( nl+nw > dl+dw ? nl+nw : dl+dw )); B=$(( nt+nh > dt+dh ? nt+nh : dt+dh ))
    rw=$((R - L)); rh=$((B - T))
    full="$WORKDIR/tgt-region.png"
    if ! timeout -k 2 8 import -window root -crop "${rw}x${rh}+$((gx+L))+$((gy+T))" \
            +repage "$full" 2>/dev/null || [ ! -s "$full" ]; then
        pkill -9 import 2>/dev/null
        elog "region grab failed"; exit 1
    fi
    ox=$L; oy=$T
fi
base="$WORKDIR/$(basename "${full%.png}")"

ocr_box() {  # <l> <t> <w> <h> <out> <psm> [whitelist] -> stdout: cleaned OCR line
    local l="$1" t="$2" w="$3" h="$4" out="$5" psm="$6" wl="${7:-}"
    convert "$full" -crop "${w}x${h}+${l}+${t}" +repage \
        -filter Lanczos -resize 500% \
        -colorspace Gray -normalize -level 25%,100% "$out" 2>/dev/null || return 1
    [ -x "$(command -v tesseract)" ] || { elog "no tesseract -- read $out by eye"; return 0; }
    local args=(--psm "$psm")
    [ -n "$wl" ] && args+=(-c "tessedit_char_whitelist=$wl")
    tesseract "$out" - "${args[@]}" 2>/dev/null \
        | tr -d '\r' | tr '\n' ' ' | sed 's/[[:space:]]\+/ /g; s/^ *//; s/ *$//'
}

rawname="$(ocr_box "$((nl - ox))" "$((nt - oy))" "$nw" "$nh" "$base-tgtname.png" 7)"
# ENB_TGT_NAMEONLY=1 skips the distance OCR (a second crop+upscale+tesseract). The
# Always-Be-Warping sweep reads ONLY the name while walking past already-visited navs
# -- it needs the distance only once it lands on a fresh candidate -- so dropping the
# dist pass roughly halves the per-slot OCR cost and shrinks the 000->warp gap.
if [ "${ENB_TGT_NAMEONLY:-0}" = "1" ]; then
    rawdist=""
else
    # Dist is purely numeric + a trailing "k"; whitelist digits/dot/k so the font's
    # round "0" is not misread as the letter "O" (it was: "0.00k" -> "O.O0Kk").
    rawdist="$(ocr_box "$((dl - ox))" "$((dt - oy))" "$dw" "$dh" "$base-tgtdist.png" 7 '0123456789.k')"
fi

# Name: drop stray leading/trailing non-alnum (panel border reads as "|"/"."), keep
# inner spaces (real names have them: "Net-7 Access Beta").
name="$(printf '%s' "$rawname" | sed -E 's/^[^[:alnum:]]+//; s/[^[:alnum:])]+$//')"
[ -z "$name" ] && name="?"

# Dist: pull the float before the trailing k ("Dist: 0.00k" -> 0.00). With the
# whitelist the box is now just like "0.00k"; fall back to O->0/l->1 if needed.
dist="$(printf '%s' "$rawdist" | grep -oE '[0-9]+\.[0-9]+' | head -1)"
if [ -z "$dist" ]; then
    dist="$(printf '%s' "$rawdist" | tr 'OoIl' '0011' | grep -oE '[0-9]+\.[0-9]+' | head -1)"
fi
[ -z "$dist" ] && dist="?"

echo "TARGET $name"
echo "DIST $dist"
echo "RAWNAME $rawname"
echo "RAWDIST $rawdist"
