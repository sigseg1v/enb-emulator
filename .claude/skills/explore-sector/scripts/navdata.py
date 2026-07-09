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
    # match <Sector> <ocr name...> -- map a possibly-misspelled nav name to the
    # closest authoritative node name in the sector. Prints "<best name>\t<ratio>".
    # A manual helper; the driver joins live<->ledger names in-process (build_alias).
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
        # GUARD (owner, 2026-06-22): require >=3 title chars before any startswith
        # scoring. A garbage map-title OCR (e.g. "; ”") norms to "" -- and
        # `fn.startswith("")` is True for EVERY sector, so an empty title used to
        # score every sector title=2 and return the alphabetically-first one
        # ("Aba"). That bogus "ABA" is what the driver then "drove". An empty or
        # 1-2 char title must match NOTHING here; only real node-label hits below
        # can then carry it, otherwise identify reports no match and the caller
        # refuses to drive.
        if len(title) >= 3:
            if title == fn:
                title_score = 3
            elif title.startswith(fn) or fn.startswith(title):
                title_score = 2
        names = {norm(n.get("name")) for n in load(s)}
        label_score = len(labels & names)
        key = (title_score, label_score)
        if key > best_key:
            best, best_key = s, key
    if (not best or best_key == (0, 0)) and len(title) >= 3:
        # FUZZY TITLE FALLBACK (owner, 2026-06-22): map-title OCR is imperfect and
        # exact/startswith above is unforgiving -- "Akeron`s Gate" came back as
        # "Alserons Gate" (norm "alseronsgate" vs file "akeronsgate"), which the
        # strict pass rejects even though they are 87% the same string. So when
        # nothing matched exactly, score every sector by string similarity and
        # take the CLOSEST one, accepting it only above a similarity floor (0.75)
        # so a genuinely garbage read still matches nothing. This is nearest-by-
        # character-distance gated by a 75% ratio -- both of the owner's asks.
        import difflib
        fz_best, fz_r = None, 0.0
        for s in all_sectors():
            r = difflib.SequenceMatcher(None, title, norm(s)).ratio()
            if r > fz_r:
                fz_best, fz_r = s, r
        if fz_best and fz_r >= 0.75:
            print(f"{fz_best}\ttitle=fuzzy({fz_r:.2f}) labels=0")
            return
    if not best or best_key == (0, 0):
        sys.exit("identify: no sector matched the title or any of those labels")
    print(f"{best}\ttitle={best_key[0]} labels={best_key[1]}")


def main():
    if len(sys.argv) < 2:
        sys.exit("usage: navdata.py {list|sectors|identify} ...")
    cmds = {"list": cmd_list, "sectors": cmd_sectors, "identify": cmd_identify,
            "match": cmd_match}
    cmd = sys.argv[1]
    if cmd not in cmds:
        sys.exit(f"unknown command: {cmd}")
    cmds[cmd](sys.argv[2:])


if __name__ == "__main__":
    main()
