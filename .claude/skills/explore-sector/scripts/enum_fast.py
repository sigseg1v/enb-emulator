#!/usr/bin/env python3
# SPDX-License-Identifier: MIT
# Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
# License: LICENSES/Freya
"""Fast target-cycle enumerator.

The slow way to learn the target cycle was: per slot, click NEXT, spawn `import`
to screenshot (~355ms of process/X-connection startup EACH), spawn tesseract
(~250ms cold start EACH). ~0.6-1s per slot, 20-40s for a full cycle.

This does it in ONE process holding ONE X connection, driving the game's own
nav-target keybinds (no map click, no >> button needed):

  - W  locks the target filter to NAVS and selects the NEAREST nav (slot 0).
  - D  steps one nav FARTHER out; C steps one nav closer. The cycle is
       distance-ordered, and W always re-anchors to the current nearest -- so a
       W then repeated D walks the full nav set in increasing-distance order.

  1. Press W (seed = nearest). For each slot, BURST-grab the name+dist boxes a
     few times with no blocking wait (a grab is ~0.6ms), then press D. Oversampling
     means the HUD repaint is always caught somewhere in the burst, so we never have
     to settle-then-grab-once and never stall in a re-grab retry loop -- "spam D and
     a picture superfast, OCR the pile after." Distances ride along in the same
     burst (a second small box per frame), so one walk yields every nav's distance.
  2. Dedup CONSECUTIVE byte-identical name frames -> the ordered set of distinct
     navs (a slot that did not repaint yet just duplicates its neighbour and folds
     away; two different navs have different name pixels so never merge). Each kept
     name frame pairs with the dist frame grabbed alongside it.
  3. OCR all kept name frames in ONE tesseract pass over a stacked montage (one
     ~0.4s cold start instead of N), and the dist frames in a second pass, mapping
     each text line back to its row by montage geometry (robust to blank slots: we
     bucket OCR word boxes by y, so a slot that read nothing leaves a gap, it does
     not shift the ranks).

Output (stdout), one line per nav, in increasing-distance (rank) order:

    <rank>\t<raw_ocr>\t<canon_or_blank>\t<ratio>\t<dist_k_or_blank>

The canon/ratio columns are filled by the in-process matcher when --sector is given.
The dist column is the panel "Dist: <n>k" reading grabbed in the SAME walk (a second
small box per slot, OCR'd in its own batched pass) so the caller knows every nav's
distance from ONE enumeration -- no per-nav re-select/read trip, which is what left
the ship sitting minutes between warps.
The caller can re-select any rank cheaply: W then D x rank (W re-anchors to nearest,
so ranks are stable as long as the ship has not moved between enumerate and select).
Because warping toward a nav makes it the new nearest, the natural sweep is
"W -> warp nearest-unvisited -> repeat".

Usage:
    enum_fast.py [--count 30] [--gap 0.02] [--burst 5] [--sector <Name>]
                 [--name-box "l t w h"] [--dist-box "l t w h"]
                 [--seed-key w] [--next-key d] [--save-montage path]
"""
import argparse
import difflib
import os
import re
import subprocess
import sys
import time

from Xlib import X, display
from PIL import Image, ImageOps

HERE = os.path.dirname(os.path.abspath(__file__))
# Scratch (the OCR montage) goes under the survey work root so one folder holds
# everything; override with ENB_EXPLORE_WORKDIR. Default: the skill's state/ dir.
WORKDIR = os.environ.get("ENB_EXPLORE_WORKDIR") or os.path.join(HERE, "..", "state")
SCRATCH = os.path.join(WORKDIR, "scratch")  # transient OCR images, mirrors drive.py/lib.sh
sys.path.insert(0, HERE)
import navdata  # noqa: E402  -- in-process match (avoids 30 python cold starts)

# Defaults mirror read-target.sh / drive.py so the boxes stay in one mental model.
DEF_NAME_BOX = (1035, 686, 220, 30)   # window-relative l,t,w,h of the target NAME line
DEF_DIST_BOX = (992, 906, 160, 30)    # the "Dist: <n>k" line under the target panel
UPSCALE = 3                           # per-crop upscale before stacking (OCR legibility)
SEP = 12                              # blank separator rows between stacked crops


def resolve_window():
    """Return (winid, abs_x, abs_y) of the live client window via lib.sh -- the SAME
    discovery the rest of the skill uses (win_by_class filters to the viewable WINE
    top-level). Bare `xdotool search --class client.exe` can return a sibling/frame
    window whose id is NOT focusable (SetInputFocus BadMatch) and whose origin is
    off by the frame -- so never trust the first search hit; resolve through lib."""
    out = subprocess.run(
        ["bash", "-c", f'. "{HERE}/lib.sh"; id=$(client_win) || exit 1; '
         'echo "$id"; win_abs "$id"'],
        capture_output=True, text=True, timeout=15)
    lines = out.stdout.split()
    if len(lines) < 3:
        sys.exit(f"resolve_window: lib.sh did not return id+geometry: "
                 f"{out.stdout!r} {out.stderr!r}")
    winid = int(lines[0])
    ax, ay = int(lines[1]), int(lines[2])
    return winid, ax, ay


def focus(winid):
    """Raise + activate the client window so key events land in it. xdotool
    windowactivate --sync is the proven path here (Xlib SetInputFocus BadMatch'd on
    the reparented WINE window); focus is silently stolen on this desktop, so the
    caller re-asserts before a key walk."""
    subprocess.run(["xdotool", "windowactivate", "--sync", str(winid)],
                   capture_output=True, timeout=10)


def key(ch):
    """Tap a single in-game key (e.g. 'w','d','c') into the active window. xdotool
    key is ~tens of ms -- negligible next to the per-key HUD settle, and unlike
    XTEST-into-focus it does not depend on a focusable window id."""
    subprocess.run(["xdotool", "key", "--clearmodifiers", ch],
                   capture_output=True, timeout=10)


def grab(d, ax, ay, w, h):
    """Grab a region off the root window as a PIL RGB image (~0.6ms)."""
    raw = d.screen().root.get_image(ax, ay, w, h, X.ZPixmap, 0xFFFFFFFF)
    # Xlib returns BGRX for a 24/32-bit TrueColor visual on this server.
    return Image.frombytes("RGB", (w, h), raw.data, "raw", "BGRX")


def prep(im):
    """Per-crop preprocessing matched to the HUD name font: grayscale, autocontrast,
    upscale. Kept light -- the montage OCR is forgiving and we re-verify before warp."""
    g = ImageOps.autocontrast(im.convert("L"))
    return g.resize((g.width * UPSCALE, g.height * UPSCALE), Image.LANCZOS)


def montage_ocr(frames, save_path=None):
    """Stack the prepped frames vertically with separators, OCR in ONE tesseract
    pass, and map each OCR line back to its frame index by y-bucket. Returns a list
    aligned to `frames`: index -> raw name string ('' if that slot read nothing)."""
    if not frames:
        return []
    cw = frames[0].width
    rh = frames[0].height
    stride = rh + SEP
    canvas = Image.new("L", (cw, stride * len(frames)), 255)
    for i, fr in enumerate(frames):
        canvas.paste(fr, (0, i * stride))
    path = save_path or os.path.join(SCRATCH, "enum-montage.png")
    os.makedirs(os.path.dirname(path), exist_ok=True)
    canvas.save(path)
    # TSV gives per-word boxes (top,height,conf,text) so we can bucket by row and be
    # robust to blank slots -- a single --psm 6 plain read would drop blank lines and
    # shift every index after the gap.
    out = subprocess.run(
        ["tesseract", path, "stdout", "--psm", "6", "tsv"],
        capture_output=True, text=True, timeout=60).stdout
    rows = [""] * len(frames)
    buckets = {}
    for line in out.splitlines()[1:]:
        f = line.split("\t")
        if len(f) < 12:
            continue
        try:
            top = int(f[7]); conf = float(f[10])
        except ValueError:
            continue
        text = f[11].strip()
        if conf < 0 or not text:
            continue
        idx = top // stride
        if 0 <= idx < len(frames):
            buckets.setdefault(idx, []).append(text)
    for idx, words in buckets.items():
        rows[idx] = " ".join(words).strip()
    return rows


def build_matcher(sector):
    """Return a fn raw -> (canon_name, ratio_str), or None when no sector given.
    Loads the sector node names once (in-process) instead of spawning navdata.py
    per slot -- the whole point of this script is to not pay per-slot process cost."""
    if not sector:
        return None
    names = [n.get("name") for n in navdata.load(sector) if n.get("name")]
    norms = [(nm, navdata.norm(nm)) for nm in names]

    def m(raw):
        if not raw:
            return "", "0"
        t = navdata.norm(raw)
        best, best_r = "", 0.0
        for nm, nn in norms:
            r = difflib.SequenceMatcher(None, t, nn).ratio()
            if r > best_r:
                best, best_r = nm, r
        return best, f"{best_r:.2f}"

    return m


def enumerate_navs(count, gap, name_box, dist_box, seed_key="w", next_key="d",
                   save_montage=None, burst=5):
    """W-seed then D-walk `count` slots. For each slot BURST-grab the name+dist boxes
    `burst` times spaced `gap` seconds apart (no blocking settle -- a grab is ~0.6ms),
    then press D. Oversampling guarantees the HUD repaint lands inside some burst, so
    we catch every distinct nav without a per-slot wait. Returns (names, dists): two
    lists aligned to the deduped distinct slots in increasing-distance (rank) order."""
    nl, nt, nw, nh = name_box
    dl, dt, dw, dh = dist_box
    winid, ax, ay = resolve_window()
    nx, ny = ax + nl, ay + nt
    dx, dy = ax + dl, ay + dt
    d = display.Display()
    focus(winid)
    time.sleep(0.2)

    # W locks the filter to NAVS and selects the NEAREST nav (rank 0); each D steps one
    # nav farther out (distance-ordered) so frames come back nearest-first. We snap a
    # burst BEFORE each advancing D, oversampling across the repaint.
    key(seed_key)
    time.sleep(max(gap, 0.25))
    name_frames = []   # (bytes, PIL) in capture order
    dist_frames = []   # PIL dist crop captured alongside each name frame
    for slot in range(count):
        for _ in range(burst):
            nim = grab(d, nx, ny, nw, nh)
            dim = grab(d, dx, dy, dw, dh)
            name_frames.append((nim.tobytes(), nim))
            dist_frames.append(dim)
            time.sleep(gap)
        key(next_key)

    # Dedup CONSECUTIVE byte-identical name frames -> ordered distinct slots. A frame
    # that did not repaint yet just repeats its predecessor and folds away; distinct
    # navs differ in name pixels so never merge. Keep the dist crop grabbed alongside
    # the FIRST frame of each distinct run (the panel's distance for that nav).
    keep_names, keep_dists = [], []
    last = None
    for (b, nim), dim in zip(name_frames, dist_frames):
        if b != last:
            keep_names.append(prep(nim))
            keep_dists.append(prep(dim))
            last = b
    names = montage_ocr(keep_names, save_montage)
    dists = montage_ocr(keep_dists, None)
    return names, dists


def parse_dist(raw):
    """Pull the kilometre number out of a panel "Dist: <n>k" OCR line. Returns the
    float (e.g. 0.99, 40.67) or '' if nothing numeric read. Tolerates the octagonal-
    zero font reading O/o for 0 and stray spaces around the decimal point."""
    if not raw:
        return ""
    t = raw.lower().replace("o", "0").replace(" ", "")
    m = re.search(r"(\d+\.\d+|\d+)", t)
    return m.group(1) if m else ""


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--count", type=int, default=30,
                    help="slots to walk: pass the sector's nav count + a few for the wrap")
    ap.add_argument("--gap", "--settle", dest="gap", type=float, default=0.02,
                    help="seconds between burst grabs (no blocking per-slot settle)")
    ap.add_argument("--burst", type=int, default=5,
                    help="name+dist grabs per slot; oversamples across the HUD repaint")
    ap.add_argument("--sector", default="")
    ap.add_argument("--name-box", default=" ".join(map(str, DEF_NAME_BOX)))
    ap.add_argument("--dist-box", default=" ".join(map(str, DEF_DIST_BOX)))
    ap.add_argument("--seed-key", default="w",
                    help="key that locks the target filter to Navs + selects nearest")
    ap.add_argument("--next-key", default="d", help="key that advances to the next-farther nav")
    ap.add_argument("--save-montage", default="")
    a = ap.parse_args()

    name_box = tuple(int(v) for v in a.name_box.split())
    dist_box = tuple(int(v) for v in a.dist_box.split())
    names, dists = enumerate_navs(a.count, a.gap, name_box, dist_box, a.seed_key,
                                  a.next_key, a.save_montage or None, a.burst)
    matcher = build_matcher(a.sector)
    # Emit EVERY distinct slot in rank order (raw, canon, ratio, dist_k). We do NOT cut
    # at a "wrap" here: distinct navs OCR-collide onto one canon, so a repeated canon is
    # not a reliable wrap marker. The consumer dedups with sector knowledge (it knows
    # how many navs exist) and keeps the first (nearest) occurrence of each.
    for i, raw in enumerate(names):
        canon, ratio = matcher(raw) if (matcher and raw) else ("", "0")
        dist = parse_dist(dists[i] if i < len(dists) else "")
        print(f"{i}\t{raw}\t{canon}\t{ratio}\t{dist}")


if __name__ == "__main__":
    main()
