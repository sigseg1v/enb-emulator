#!/usr/bin/env bash
# SPDX-License-Identifier: MIT
# Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
# License: LICENSES/Freya
#
# read-speed.sh [full-shot.png] -- read the WARP SPEED readout, the green number
# immediately LEFT of the round warp button in the bottom-centre HUD.
#
# This is the authoritative "are we actually warping?" signal. 0 means the ship is
# stationary -- a warp click that still reads 0 did NOT engage (a dropped click, or
# a target under the ~2k warp floor) and must be re-issued. A non-zero value means
# the warp drive is spun up and the ship is moving. Distance-delta is a noisy proxy
# (OCR spikes, the client auto-retargets the nearest nav on arrival); the speed
# digit is the direct readout.
#
# The number is a fixed antialiased green font on a dark HUD, only ~9px tall, and
# it is VARIABLE WIDTH: parked it reads "000" (3 digits) but warp speed is 2000 by
# default and higher on many ships (4+ digits). We isolate the green pixels, invert
# to black-on-white, upscale with LANCZOS, add a white border (so tesseract can
# segment the edge digits -- without the border the leftmost/rightmost digit is
# dropped and a 4-digit 2000 mis-reads), and OCR with a digit whitelist. Validated
# live 2026-06-21 against real frames: "000" parked and "2000" mid-warp both read
# exactly. (The old per-cell 7-segment classifier was deleted: this is NOT a
# 7-segment font -- it has a curved "2" -- so the segment patterns mis-classified
# every non-zero curved digit as 0, which read 2000 as 0 and cancelled live warps.)
#
# The digits sit at window-relative x[578..618] y[852..874] (the client renders a
# fixed 1280x960, so this is stable). The round warp orb's blue/green glow bleeds in
# at ~x639, kept OUT of the box by the right edge at 618.
#
# Prints:
#   SPEED <n>   integer >= 0 (0 == not warping)
#   SPEED ?     no green digits found (panel obscured / not in space)
#
# Override the box if the UI ever moves:
#   ENB_SPEED_BOX="l r t b"   default "578 618 852 874"
# Pass a screenshot path as $1 to read an existing shot instead of capturing one.
set -uo pipefail
SKILL_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"; . "$SKILL_DIR/lib.sh"

if [ -n "${1:-}" ]; then
    full="$1"
else
    full="$(explore_shot "")" || { elog "screenshot failed"; echo "SPEED ?"; exit 1; }
fi

ENB_SPEED_BOX="${ENB_SPEED_BOX:-578 618 852 874}" ENB_SPEED_OCR_PNG="$SCRATCH/.speed_ocr.png" python3 - "$full" <<'PY'
import os, sys, subprocess
import numpy as np
from PIL import Image, ImageOps

L, R, T, B = (int(v) for v in os.environ["ENB_SPEED_BOX"].split())
im = np.asarray(Image.open(sys.argv[1]).convert("RGB")).astype(int)
box = im[T:B, L:R]
r, g, b = box[:, :, 0], box[:, :, 1], box[:, :, 2]
green = (g > 95) & (g - r > 25) & (g - b > 25)
H, W = green.shape
ys, xs = np.where(green)
if len(xs) < 5:
    print("SPEED ?")
    sys.exit(0)

# DENOISE by connected component (owner, 2026-06-22). The animated green nebula
# bleeds a tendril into the box (a tall-thin blob hugging the LEFT edge) and the
# warp-orb glow can clip the RIGHT edge; either one sits flush against the "000"
# and tesseract then segments nothing and returns "?", so warp.sh -- which refuses
# to click the orb unless it confirms a solid 000 -- never engages warp and the
# ship SITS ("you just didn't press warp"). The speed digits are interior, digit
# sized blobs; box-edge noise and sub-glyph specks are not. So label 8-connected
# components and keep only digit-shaped ones (area >= 10, >= 6px tall, >= 3px wide)
# that do NOT touch the left/right edge. This reads "000" parked through the
# tendril; multi-digit warp speeds sit centred with margin and survive the filter.
from collections import deque
lab = np.zeros((H, W), int)
keep = np.zeros((H, W), bool)
cur = 0
for sy in range(H):
    for sx in range(W):
        if green[sy, sx] and lab[sy, sx] == 0:
            cur += 1
            dq = deque([(sy, sx)])
            lab[sy, sx] = cur
            comp = []
            while dq:
                y, x = dq.popleft()
                comp.append((y, x))
                for dy in (-1, 0, 1):
                    for dx in (-1, 0, 1):
                        ny, nx = y + dy, x + dx
                        if 0 <= ny < H and 0 <= nx < W and green[ny, nx] and lab[ny, nx] == 0:
                            lab[ny, nx] = cur
                            dq.append((ny, nx))
            cxs = [p[1] for p in comp]
            cys = [p[0] for p in comp]
            area = len(comp)
            h = max(cys) - min(cys) + 1
            w = max(cxs) - min(cxs) + 1
            touch_lr = min(cxs) == 0 or max(cxs) == W - 1
            if area >= 10 and h >= 6 and w >= 3 and not touch_lr:
                for y, x in comp:
                    keep[y, x] = True
green = keep
ys, xs = np.where(green)
if len(xs) < 5:
    print("SPEED ?")
    sys.exit(0)

# Tight-crop to the lit pixels, invert to black-glyph-on-white, upscale, then add a
# white border so tesseract can segment the edge digits (no border drops the
# leftmost/rightmost digit -- that bug read 2000 as 000).
pad = 2
sub = green[max(0, ys.min() - pad):ys.max() + pad + 1,
            max(0, xs.min() - pad):xs.max() + pad + 1]
SCALE = 12
img = Image.fromarray(np.where(sub, 0, 255).astype("uint8"), "L")
img = img.resize((img.width * SCALE, img.height * SCALE), Image.LANCZOS)
img = ImageOps.expand(img, border=SCALE * 3, fill=255)
ocr_png = os.environ.get("ENB_SPEED_OCR_PNG", "/tmp/.speed_ocr.png")
img.save(ocr_png)

out = subprocess.run(
    ["tesseract", ocr_png, "-", "--psm", "8",
     "-c", "tessedit_char_whitelist=0123456789"],
    capture_output=True, text=True).stdout
digits = "".join(ch for ch in out if ch.isdigit())
print(f"SPEED {int(digits)}" if digits else "SPEED ?")
PY
