#!/usr/bin/env bash
# SPDX-License-Identifier: MIT
# Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
# License: LICENSES/Freya
#
# open-map.sh [out.png] -- ensure the in-game sector map is open, center it on the
# ship, ensure the X/Y/Z readout is on, screenshot the client, and crop to the node
# panel.
# Prints:
#   FULL <full-shot.png>
#   MAP  <cropped-overlay.png> <X0> <Y0>
# Read the MAP png to see the nav nodes (gray pentagons = stations, gray/green
# "?" = unidentified/nearby navs, green square = your location, coloured labels =
# named navs). Convert a node at crop pixel (cx,cy) to a click with:
#   click-map.sh $((X0+cx)) $((Y0+cy))
#
# What this client's map actually is (verified live 2026-06-20, vanilla/mods-off):
# when MAXIMIZED the map fills window-rel (22,222)-(760,786) as a semi-transparent
# overlay (the 3D world/planet shows through). Its toolbar sits at the TOP-LEFT of
# that body (window-rel y~232): [? | center | xyz | dot | labels]; the −/X window
# buttons are top-right. The toolbar buttons we use (each clicked ONCE):
#   - 2nd "center" at ~(84,232):  LEFT-click once = center map on your ship;
#     RIGHT-click once = zoom ALL the way out (one right-click does it -- it is not
#     incremental, so click it exactly once; the mouse wheel only drives the 3D
#     chase camera, never the map).
#   - 3rd "xyz"    at ~(116,232): toggles the X:/Y:/Z: position readout at the map's
#     top-left. BLUE = your ship's current X/Y/Z; hover a node and it turns YELLOW
#     and shows THAT node's X/Y/Z (read-xyz.sh / identify by coord, no Dest click).
# Even fully zoomed out, a node only appears once it is in scanner range (warp
# toward it, PB-22) -- zoom changes the view scale, not what is discovered.
# The nav nodes render across the body BELOW the toolbar. "Dest: <node>" prints at
# the body's bottom; after clicking a node, OCR that line (read-navname.sh).
#
# There are TWO map sizes: a small one and the large (maximized) one above. The
# "+" maximize button at ~(455,431) takes the small map to the large one. When the
# map is ALREADY large that spot is just transparent map area, so clicking it is a
# harmless no-op -- so we ALWAYS left-click (455,431) FIRST to guarantee the large
# map (per owner). All other coords assume the large map.
#
# The open-map button (3rd of the four round bottom-left HUD buttons, ~(132,870))
# is a TOGGLE: clicking it when the map is already open CLOSES it. So we DETECT
# whether the map is already open (the blue "center" button at (84,232) present)
# and only click the toggle when the map is closed.
#
# Pointer note: this box runs a QEMU VM with a usb-tablet ABSOLUTE pointer device
# that fights `xdotool mousemove --sync` (it blocks forever). So we use plain
# `mousemove` + a short settle sleep, never `--sync`.
#
#   ENB_MAP_BTN="x y"      open-map toggle button   (default "132 870")
#   ENB_MAP_MAX="x y"      maximize "+" button      (default "455 431")
#   ENB_MAP_CENTER="x y"   center/zoom button       (default "84 232")
#   ENB_MAP_XYZ="x y"      X/Y/Z readout toggle     (default "116 232")
#   ENB_MAP_CROP="l t w h" map body crop            (default "22 222 738 564")
#   ENB_MAP_FORCE_OPEN=1   click the toggle even if the map looks already open
set -uo pipefail
SKILL_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"; . "$SKILL_DIR/lib.sh"

# Click a window-relative coord with an explicit X button (1=left, 3=right).
# Plain mousemove (NO --sync, which hangs against the VM's absolute pointer).
click_btn_win() {
    local id="$1" rx="$2" ry="$3" btn="$4" g gx gy gw gh
    raise_win "$id"; sleep 0.15
    g="$(win_abs "$id")" || { elog "no geometry for $id"; return 1; }
    read -r gx gy gw gh <<< "$g"
    xdotool mousemove $((gx + rx)) $((gy + ry)); sleep 0.15
    xdotool click "$btn"; sleep 0.18
}

# Is the map already open? The "center" toolbar button is a blue circle at the
# center coord; detect its blue pixels in a small box. Prints "open"/"closed".
map_is_open() {
    local id="$1" cx="$2" cy="$3" tmp
    tmp="$SCRATCH/.mapprobe.png"
    shot "$tmp" >/dev/null 2>&1 || { echo closed; return; }
    python3 - "$tmp" "$cx" "$cy" <<'PY'
import sys, numpy as np
from PIL import Image
p, cx, cy = sys.argv[1], int(sys.argv[2]), int(sys.argv[3])
im = np.asarray(Image.open(p).convert("RGB")).astype(int)
H, W, _ = im.shape
x0, y0, x1, y1 = max(0, cx-14), max(0, cy-14), min(W, cx+14), min(H, cy+14)
b = im[y0:y1, x0:x1]
R, G, B = b[:, :, 0], b[:, :, 1], b[:, :, 2]
blue = (B > 90) & (B - R > 25) & (B - G > 15) & (R > 20) & (R < 150)
print("open" if blue.sum() > 25 else "closed")
PY
}

# Is the X/Y/Z readout already on? When on, blue "X:/Y:/Z:" text sits at the map
# top-left (~window-rel (34,250)-(110,295)). Detect its blue text pixels.
xyz_is_on() {
    local id="$1" tmp
    tmp="$SCRATCH/.xyzprobe.png"
    shot "$tmp" >/dev/null 2>&1 || { echo off; return; }
    python3 - "$tmp" <<'PY'
import sys, numpy as np
from PIL import Image
im = np.asarray(Image.open(sys.argv[1]).convert("RGB")).astype(int)
b = im[250:296, 34:160]
R, G, B = b[:, :, 0], b[:, :, 1], b[:, :, 2]
blue = (B > 110) & (B - R > 40) & (B - G > 25)
print("on" if blue.sum() > 18 else "off")
PY
}

id="$(client_win)" || { elog "no client window"; exit 1; }
read -r bx by  <<< "${ENB_MAP_BTN:-132 870}"
read -r mx my  <<< "${ENB_MAP_MAX:-455 431}"
read -r cx cy  <<< "${ENB_MAP_CENTER:-84 232}"
read -r zx zy  <<< "${ENB_MAP_XYZ:-116 232}"

raise_win "$id"; sleep 0.2
state="$(map_is_open "$id" "$cx" "$cy")"
if [ "$state" = "closed" ] || [ -n "${ENB_MAP_FORCE_OPEN:-}" ]; then
    elog "map closed -> opening (toggle button $bx,$by)"
    click_btn_win "$id" "$bx" "$by" 1; sleep 0.6
else
    elog "map already open -> not toggling"
fi

# Maximize: the small map's "+" lives at its top-right corner (~455,431) and grows
# the map toward the top-right. When the map is already large that spot is interior
# transparent map, so the click is a harmless no-op. Always click it first.
elog "maximize (left-click + at $mx,$my; no-op if already large)"
click_btn_win "$id" "$mx" "$my" 1; sleep 0.4
elog "center on ship (left-click $cx,$cy)"
click_btn_win "$id" "$cx" "$cy" 1; sleep 0.3
elog "zoom all the way out (one right-click $cx,$cy)"
click_btn_win "$id" "$cx" "$cy" 3; sleep 0.3
# Ensure the X/Y/Z readout is on (one click toggles it; only click if currently off
# so we never toggle it back off).
if [ "$(xyz_is_on "$id")" = "off" ]; then
    elog "X/Y/Z readout off -> enabling (left-click $zx,$zy)"
    click_btn_win "$id" "$zx" "$zy" 1; sleep 0.3
else
    elog "X/Y/Z readout already on"
fi
# park the pointer away from the toolbar so its tooltip does not cover the nodes
g="$(win_abs "$id")"; read -r gx gy gw gh <<< "$g"
xdotool mousemove $((gx + 1000)) $((gy + 550)); sleep 0.4

full="$(explore_shot "${1:-}")" || { elog "screenshot failed"; exit 1; }
crop="$SCRATCH/$(basename "${full%.png}")-map.png"
MAP_CROP="${ENB_MAP_CROP:-22 222 738 564}"
read -r cpath x0 y0 <<< "$(crop_map "$full" "$crop")"
echo "FULL $full"
echo "MAP $cpath $x0 $y0"
