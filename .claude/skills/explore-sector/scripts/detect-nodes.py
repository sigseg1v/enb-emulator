#!/usr/bin/env python3
# SPDX-License-Identifier: MIT
# Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
# License: LICENSES/Freya
#
# detect-nodes.py <full-shot.png> [l t w h] -- find the nav-node markers drawn on
# the sector-map panel and print their window-relative centroids, one per line:
#   NODE <wx> <wy> <colour> <pixels>
# Colours: green / cyan / red / gray (the map legend: gray pentagon = station,
# gray "?" = unidentified nav, green "?" = nearby nav, green square = you,
# coloured labels = named navs). Use a centroid as a click target:
#   click-map.sh <wx> <wy>      # selects that node; then read-navname.sh
#
# The map panel on this client is anchored bottom-left; nodes render in the body
# ABOVE the toolbar (window-rel y~232) and LEFT of the chat window. Default search
# box excludes the chat (top-right), the bottom HUD, and the right-side panels.
# Override the box with "l t w h" if the panel ever moves.
#
# Pure-numpy connected components (no scipy): threshold colour masks, then
# grid-cluster nearby lit pixels into blobs.
import sys
import numpy as np
from PIL import Image

if len(sys.argv) < 2:
    sys.exit("usage: detect-nodes.py <shot.png> [l t w h]")
img = np.asarray(Image.open(sys.argv[1]).convert("RGB")).astype(int)
H, W, _ = img.shape
if len(sys.argv) >= 6:
    l, t, w, h = (int(x) for x in sys.argv[2:6])
else:
    # maximized map body (22,222)-(760,786); skip the top ~48px (toolbar + the
    # "<sector> Earth" title row) and the bottom ~30px "Dest:" line so neither is
    # mistaken for a node.
    l, t, w, h = 22, 270, 738, 486
r, b = min(W, l + w), min(H, t + h)

# node markers are SMALL (pentagons/"?"/squares ~4-120 px); reject big blobs --
# the 3D station hull bleeding through the transparent map and the red Dest/title
# label text both form large connected regions.
MAXPIX = int(sys.argv[6]) if len(sys.argv) >= 7 else 120

R, G, B = img[:, :, 0], img[:, :, 1], img[:, :, 2]
masks = {
    "green": (G > 110) & (G - R > 35) & (G - B > 20),
    # nav pentagons render a pale cyan (~RGB 110,164,176): G,B clearly above R, with
    # G~B. The R ceiling was 110 (strict), which dropped every pentagon sitting right
    # at R=110 -- the bulk of the in-range navs. 135 catches them without reaching the
    # red planet (R high, G/B low -> fails G,B>120).
    "cyan":  (G > 120) & (B > 120) & (R < 135) & (G - R > 30) & (abs(G - B) < 70),
    "red":   (R > 150) & (R - G > 70) & (R - B > 70),
    # pentagons/"?" sitting OVER the red planet glow render warm-white, NOT cyan:
    # the glow saturates R while the marker's bright outline keeps G,B high, giving
    # ~(255,190,180) with G~B. Pure red glow (G,B low) and red label text both fail
    # G,B>140, so this catches only the markers the cyan mask drops over the planet.
    # This is the difference between the map-click fallback seeing the spread-out
    # navs near a planet vs. conceding them (verified: AdrielPrime Radiation Marker
    # 1/3 etc. render here, not in cyan).
    "glow":  (R > 240) & (G > 150) & (B > 140) & (R - G > 25) & (R - G < 110)
             & (abs(G - B) < 45),
    # gray "?" / pentagon: low-saturation marks on the dark panel. These are dim,
    # so the floor is lower than the coloured markers; the size cap rejects the big
    # title/Dest text and the 3D hull bleeding through.
    "gray":  (R > 90) & (G > 90) & (B > 90) & (abs(R - G) < 28)
             & (abs(G - B) < 28) & (abs(R - B) < 28),
}

def clusters(mask, cell=10, minpix=4):
    ys, xs = np.where(mask)
    # confine to the search box
    keep = (xs >= l) & (xs < r) & (ys >= t) & (ys < b)
    xs, ys = xs[keep], ys[keep]
    if len(xs) == 0:
        return []
    # bucket lit pixels into a coarse grid, merge 8-neighbour buckets via BFS
    cells = {}
    for x, y in zip(xs, ys):
        cells.setdefault((x // cell, y // cell), []).append((x, y))
    seen, out = set(), []
    for c in list(cells):
        if c in seen:
            continue
        stack, comp = [c], []
        seen.add(c)
        while stack:
            cc = stack.pop()
            comp.extend(cells.get(cc, []))
            for dx in (-1, 0, 1):
                for dy in (-1, 0, 1):
                    nc = (cc[0] + dx, cc[1] + dy)
                    if nc in cells and nc not in seen:
                        seen.add(nc)
                        stack.append(nc)
        if minpix <= len(comp) <= MAXPIX:
            cxs = [p[0] for p in comp]
            cys = [p[1] for p in comp]
            out.append((sum(cxs) // len(cxs), sum(cys) // len(cys), len(comp)))
    return out

# prefer the most specific colour at a location (red>green>cyan>gray) to avoid
# double-counting one marker under several masks
claimed = []
for colour in ("red", "glow", "green", "cyan", "gray"):
    for cx, cy, n in clusters(masks[colour]):
        if any((cx - px) ** 2 + (cy - py) ** 2 < 12 ** 2 for px, py, _ in claimed):
            continue
        claimed.append((cx, cy, colour))
        print(f"NODE {cx} {cy} {colour} {n}")
