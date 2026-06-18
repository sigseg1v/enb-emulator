#!/usr/bin/env python3
# SPDX-License-Identifier: MIT
# Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
# License: LICENSES/Freya
#
# detect.py -- tiny PIL+numpy screen-state classifier for the login skill.
# This box has no tesseract and no cv2, so detection is region statistics +
# fixed-location reference-crop matching (the client is a deterministic
# 1280x960 render, so a crop taken once matches on every later run).
#
# Subcommands (exit 0 = yes/match, 1 = no/mismatch; some also print a value):
#   mean   IMG X Y W H                 -> prints "R G B" mean of the region
#   black  IMG X Y W H [FRAC] [THR]    -> exit0 if >=FRAC(.85) of pixels are
#                                         darker than THR(40) (load screen)
#   bright IMG X Y W H [FRAC] [THR]    -> exit0 if >=FRAC of pixels brighter
#   match  IMG REF X Y [TOL]           -> exit0 if crop at (X,Y) ~= REF within
#                                         mean-abs-diff TOL (default 18/255)
#   savecrop IMG REF X Y W H           -> write region as a reference PNG
#   size   IMG                         -> prints "W H"
import sys
import numpy as np
from PIL import Image


def _load(p):
    return np.asarray(Image.open(p).convert("RGB"), dtype=np.float32)


def main():
    a = sys.argv
    if len(a) < 2:
        print("usage: detect.py <cmd> ...", file=sys.stderr)
        return 2
    cmd = a[1]

    if cmd == "size":
        im = Image.open(a[2])
        print(im.size[0], im.size[1])
        return 0

    if cmd == "mean":
        img = _load(a[2])
        x, y, w, h = (int(v) for v in a[3:7])
        reg = img[y:y + h, x:x + w]
        m = reg.reshape(-1, 3).mean(axis=0)
        print(f"{m[0]:.1f} {m[1]:.1f} {m[2]:.1f}")
        return 0

    if cmd in ("black", "bright"):
        img = _load(a[2])
        x, y, w, h = (int(v) for v in a[3:7])
        frac = float(a[7]) if len(a) > 7 else 0.85
        thr = float(a[8]) if len(a) > 8 else 40.0
        reg = img[y:y + h, x:x + w].reshape(-1, 3)
        lum = reg.mean(axis=1)
        if cmd == "black":
            hit = (lum < thr).mean()
        else:
            hit = (lum > (255.0 - thr)).mean()
        print(f"{hit:.3f}")
        return 0 if hit >= frac else 1

    if cmd == "match":
        img = _load(a[2])
        ref = _load(a[3])
        x, y = int(a[4]), int(a[5])
        tol = float(a[6]) if len(a) > 6 else 18.0
        rh, rw, _ = ref.shape
        crop = img[y:y + rh, x:x + rw]
        if crop.shape != ref.shape:
            print("shape-mismatch", file=sys.stderr)
            return 1
        mad = np.abs(crop - ref).mean()
        print(f"{mad:.2f}")
        return 0 if mad <= tol else 1

    if cmd == "savecrop":
        im = Image.open(a[2]).convert("RGB")
        x, y, w, h = (int(v) for v in a[4:8])
        im.crop((x, y, x + w, y + h)).save(a[3])
        print(f"saved {a[3]} ({w}x{h})")
        return 0

    print(f"unknown cmd {cmd}", file=sys.stderr)
    return 2


if __name__ == "__main__":
    sys.exit(main())
