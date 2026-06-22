#!/usr/bin/env python3
# SPDX-License-Identifier: MIT
# Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
# License: LICENSES/Freya
#
# navdata.py -- read the authoritative per-sector node lists shipped in
# docs/sectors/json/<Sector>.jsonl and answer questions the explore-sector
# skill needs:
#
#   list <Sector>                 -- dump every node (type, hidden, x,y,z, name)
#   sectors                       -- list every sector file we have data for
#   identify <label> [<label>...] -- given nav labels READ OFF THE MAP, return the
#                                    sector whose node set best matches (so the
#                                    skill can tell which sector it is in without a
#                                    sector-name memory read)
#
# A node is "hidden" (needs discovery by flying within scanner range) when its
# type starts with "hidden-". Everything else is normally drawn on the map.
import json
import os
import sys

REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", "..", ".."))
DATA = os.path.join(REPO, "docs", "sectors", "json")


def load(sector):
    path = os.path.join(DATA, sector + ".jsonl")
    if not os.path.exists(path):
        sys.exit(f"no sector data: {path}")
    out = []
    with open(path) as f:
        for line in f:
            line = line.strip()
            if not line:
                continue
            d = json.loads(line)
            d["hidden"] = str(d.get("type", "")).startswith("hidden-")
            out.append(d)
    return out


def all_sectors():
    return sorted(
        f[:-6] for f in os.listdir(DATA) if f.endswith(".jsonl")
    )


def norm(s):
    # Loose match for map-read labels: lowercase, drop the accent in "Akeron`s",
    # collapse whitespace. Map OCR is imperfect, so be forgiving.
    return "".join(c.lower() for c in str(s) if c.isalnum())


def cmd_list(args):
    for n in load(args[0]):
        h = "HIDDEN" if n["hidden"] else "shown "
        print(f"{h} {n.get('type'):20} {n.get('x'):>8} {n.get('y'):>8} {n.get('z'):>8}  {n.get('name')}")


def cmd_sectors(_args):
    for s in all_sectors():
        nodes = load(s)
        hid = sum(1 for n in nodes if n["hidden"])
        print(f"{s:24} {len(nodes):3} nodes ({hid} hidden)")


def cmd_match(args):
    # match <Sector> <ocr name...> -- map an OCR'd nav name (which may have small
    # errors like "Soace" for "Space") to the closest authoritative node name in
    # the sector. Prints "<best name>\t<ratio>". Used after read-navname.sh.
    if len(args) < 2:
        sys.exit("match: usage: match <Sector> <ocr name...>")
    import difflib
    sector, ocr = args[0], " ".join(args[1:])
    names = [n.get("name") for n in load(sector)]
    target = norm(ocr)
    best, best_r = None, 0.0
    for nm in names:
        r = difflib.SequenceMatcher(None, target, norm(nm)).ratio()
        if r > best_r:
            best, best_r = nm, r
    if not best:
        sys.exit("match: sector has no nodes")
    print(f"{best}\t{best_r:.2f}")


def cmd_predict(args):
    # predict <Sector> -- given a few ANCHORS (a map pixel paired with the node's
    # NAME, which we identify reliably via click->Dest->match) read from stdin, solve
    # the affine map world(x,y) -> screen pixel and print the PREDICTED pixel for
    # every node in the sector. Use it to click nodes the detector misses and to see
    # which known nodes should be on-screen (in body) vs need a warp to bring in.
    #
    # We anchor on NAMES, not on the X/Y/Z readout's OCR'd numbers: the game font's
    # digits mis-OCR (8->6), but the node name is reliable and its true x/y comes
    # from the JSON -- so the transform carries no OCR-number error.
    #
    # stdin: one anchor per line "<px> <py> <node name...>" (>=3, non-colinear).
    # stdout (one per node): "<px> <py> <inbody|OUT> <type> <name>"
    if not args:
        sys.exit("predict: usage: predict <Sector>  (anchors '<px> <py> <name>' on stdin)")
    sector = args[0]
    nodes = load(sector)
    by_name = {norm(n.get("name")): n for n in nodes}
    P, S = [], []   # world rows [x,y,1] and screen rows [px,py]
    for line in sys.stdin:
        line = line.strip()
        if not line:
            continue
        parts = line.split()
        if len(parts) < 3:
            continue
        px, py, name = parts[0], parts[1], " ".join(parts[2:])
        n = by_name.get(norm(name))
        if not n:
            sys.stderr.write(f"predict: anchor name not in sector: {name}\n")
            continue
        P.append([float(n["x"]), float(n["y"]), 1.0])
        S.append([float(px), float(py)])
    if len(P) < 3:
        sys.exit("predict: need >=3 valid anchors (got %d)" % len(P))
    import numpy as np
    A = np.array(P)
    B = np.array(S)
    M, *_ = np.linalg.lstsq(A, B, rcond=None)   # 3x2: world[x,y,1] -> [px,py]
    # map body bounds (window-rel), same as detect-nodes default search box-ish
    L, T, R, Bb = 22, 232, 760, 786
    for n in nodes:
        w = np.array([float(n["x"]), float(n["y"]), 1.0])
        px, py = (w @ M)
        inb = "inbody" if (L <= px <= R and T <= py <= Bb) else "OUT"
        print(f"{int(round(px))} {int(round(py))} {inb} {n.get('type')} {n.get('name')}")


def cmd_identify(args):
    # Accepts the map-window TITLE (e.g. "Equatorial Earth") and/or nav labels
    # read off the map. Sector FILES are CamelCase of the title ("High Earth" ->
    # HighEarth, "Aragoth Prime" -> AragothPrime); a few titles also append the
    # planet ("Equatorial Earth" -> Equatorial). So we score each sector by:
    #   - title match: the joined args, spaces removed, equals or starts-with the
    #     filename (handles both CamelCase and planet-suffixed titles), and
    #   - label match: how many individual args are node names in that sector.
    # Highest combined score wins; ties favour the title match.
    if not args:
        sys.exit("identify: give the map title and/or one or more nav labels")
    title = norm(" ".join(args))
    labels = {norm(a) for a in args if norm(a)}
    best, best_key = None, (0, 0)
    for s in all_sectors():
        fn = norm(s)
        title_score = 0
        if title == fn:
            title_score = 3
        elif title.startswith(fn) or fn.startswith(title):
            title_score = 2
        names = {norm(n.get("name")) for n in load(s)}
        label_score = len(labels & names)
        key = (title_score, label_score)
        if key > best_key:
            best, best_key = s, key
    if not best or best_key == (0, 0):
        sys.exit("identify: no sector matched the title or any of those labels")
    print(f"{best}\ttitle={best_key[0]} labels={best_key[1]}")


def main():
    if len(sys.argv) < 2:
        sys.exit("usage: navdata.py {list|sectors|identify} ...")
    cmds = {"list": cmd_list, "sectors": cmd_sectors, "identify": cmd_identify,
            "match": cmd_match, "predict": cmd_predict}
    cmd = sys.argv[1]
    if cmd not in cmds:
        sys.exit(f"unknown command: {cmd}")
    cmds[cmd](sys.argv[2:])


if __name__ == "__main__":
    main()
