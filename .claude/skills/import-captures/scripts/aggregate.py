#!/usr/bin/env python3
# SPDX-License-Identifier: MIT
# Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
# License: LICENSES/Freya
#
# aggregate.py -- turn the per-capture decoder JSONs (WORK/objjson/*.json) into
# one aggregated, de-duplicated object list PER SECTOR (WORK/sectors/<id>.json).
#
# What it does, and why:
#   * Sector assignment. A capture starts in the PREVIOUS sector and gates into
#     the target, so an object's sector is NOT the capture filename -- it is the
#     last in-stream sector marker (0x003A handoff) seen BEFORE that object was
#     created. We re-derive each object's sector from the marker stream by frame,
#     dropping markers whose id is not a real sector (validated against the DB)
#     and folding instanced sub-sector ids (realid*10+1) back to the real id.
#   * Dedup. The stable key is (sector, gid). The same object seen in many frames
#     / many captures is kept ONCE; we keep its LATEST position (captures ordered
#     by timestamp, newest wins) and merge in any non-null fields.
#   * Removal opcodes are ignored upstream already (the decoder only tracks
#     creates), so an object that left and re-entered range is still one object.
#   * Exclusions: players (gid >= 0x40000000 or isAvatar), and anything that is
#     not a thing we import (loot/corpses/decorative objects) -- counted, dropped.
#
# Output per sector: navs / resources / mobs (the import sets) plus stations /
# gates / planets (report-only) and the exclusion tally.
import collections
import glob
import json
import os
import re
import subprocess
import sys

WORK = os.environ.get("ENB_IMPORT_WORK", "/tmp/enb-import-captures")
PG_CONTAINER = os.environ.get("ENB_PG_CONTAINER", "freya-postgres-1")

PLAYER_GID_BASE = 0x40000000


def pg(sql):
    """Run a parameterless query against net7; return list of field-lists."""
    out = subprocess.run(
        ["docker", "exec", PG_CONTAINER, "psql", "-U", "net7", "-d", "net7",
         "-tA", "-F", "\x1f", "-c", sql],
        capture_output=True, text=True, check=True).stdout
    return [line.split("\x1f") for line in out.splitlines() if line]


def sector_names():
    """id -> name and normalized-name -> id, from the authoritative sectors table."""
    id2name, norm2id = {}, {}
    for sid, name in pg("SELECT sector_id, name FROM sectors"):
        sid = int(sid)
        id2name[sid] = name
        if name:
            norm2id[name.strip().lower().replace(" ", "")] = sid
    return id2name, norm2id


def cap_timestamp(path):
    """Order key for 'latest position'. Prefer the UTC stamp in the filename;
    fall back to file mtime so un-stamped captures still order sanely."""
    m = re.search(r"(\d{8}T\d{6}Z)", os.path.basename(path))
    if m:
        return m.group(1)
    try:
        return "0_%020d" % int(os.path.getmtime(path))
    except OSError:
        return "0"


def norm_marker(sec, valid):
    """Fold a raw marker sector id to a real DB sector id, or None to drop it.
    Instanced sub-sectors arrive as realid*10+1 (e.g. 40151 -> 4015)."""
    if sec in valid:
        return sec
    if sec % 10 == 1 and (sec // 10) in valid:
        return sec // 10
    return None


def derive_sector(frame, markers):
    """Last marker at/before this frame (markers: sorted [(frame, sector)])."""
    sec = None
    for mf, ms in markers:
        if mf <= frame:
            sec = ms
        else:
            break
    return sec


def merge(dst, src):
    """Merge a newer create (src) into the kept record (dst): newer position
    always wins; any field that is null in dst takes src's non-null value."""
    for k in ("x", "y", "z"):
        if src.get(k) is not None:
            dst[k] = src[k]
    for k, v in src.items():
        if k in ("x", "y", "z"):
            continue
        if dst.get(k) is None and v is not None:
            dst[k] = v


def categorize(o):
    """Return one of: nav, resource, mob, station, gate, planet, player, drop."""
    gid = o.get("gid") or 0
    if gid >= PLAYER_GID_BASE or o.get("isAvatar"):
        return "player"
    ct = o.get("createType")
    kind = (o.get("kind") or "")
    if kind == "resource" or ct == 38:
        return "resource"
    if o.get("isNav") and kind.startswith("nav"):
        return "nav"
    if ct == 12:
        return "station"
    if ct in (10, 11):
        return "gate"
    if ct == 3:
        return "planet"
    if ct in (0, 1, 42) and not o.get("isNav"):
        return "mob"
    return "drop"


def main():
    id2name, norm2id = sector_names()
    valid = set(id2name)
    files = sorted(glob.glob(os.path.join(WORK, "objjson", "*.json")),
                   key=cap_timestamp)
    if not files:
        sys.exit(f"aggregate: no decoder JSON in {WORK}/objjson -- run decode.sh first")

    # merged objects keyed by (sector, gid)
    merged = {}
    excl = collections.Counter()

    for path in files:
        with open(path) as fh:
            d = json.load(fh)
        fname = os.path.basename(path)
        markers = sorted(
            (m["frame"], s) for m in d.get("markers", [])
            for s in (norm_marker(int(m["sector"]), valid),) if s is not None)
        # A capture WITH gate markers: derive each object's sector from the last
        # marker before it (handles the gate-in and multi-gate captures). A
        # capture WITHOUT markers has no gate crossing in it, so it is a single
        # sector -- resolve it from the filename prefix (the survey's own record
        # of where it was), validated against the DB. Gate-named captures whose
        # prefix is not a sector resolve to nothing and are dropped.
        cap_sector = None
        if not markers:
            cap_sector = norm2id.get(
                fname.split("__")[0].strip().lower().replace("_", "").replace(" ", ""))
            if cap_sector is None:
                excl["capture_unresolved_no_marker"] += len(d.get("objects", []))
                continue
        for o in d.get("objects", []):
            sec = cap_sector if cap_sector is not None \
                else derive_sector(o.get("frame") or 0, markers)
            if sec is None:
                excl["before_first_marker"] += 1
                continue
            cat = categorize(o)
            if cat == "player":
                excl["player"] += 1
                continue
            o = dict(o)
            o["sector"] = sec
            o["_cat"] = cat
            key = (sec, o["gid"])
            if key in merged:
                merge(merged[key], o)
            else:
                merged[key] = o

    # bucket per sector
    sectors = collections.defaultdict(lambda: {
        "navs": [], "resources": [], "mobs": [],
        "stations": [], "gates": [], "planets": [],
    })
    cat_to_bucket = {"nav": "navs", "resource": "resources", "mob": "mobs",
                     "station": "stations", "gate": "gates", "planet": "planets"}
    for (sec, _gid), o in merged.items():
        cat = categorize(o)  # recompute from merged fields
        if cat == "player":
            excl["player"] += 1
            continue
        b = cat_to_bucket.get(cat)
        if b is None:
            excl["drop_loot_or_deco"] += 1
            continue
        if not o.get("hasPos"):
            excl["no_position"] += 1
            continue
        rec = {
            "gid": o["gid"], "name": o.get("name"),
            "x": o["x"], "y": o["y"], "z": o["z"],
            "baseAsset": o.get("baseAsset"), "createType": o.get("createType"),
            "level": o.get("level"), "faction": o.get("faction"),
            "navType": o.get("navType"), "onRadar": o.get("onRadar"),
            "signature": o.get("signature"),
        }
        sectors[sec][b].append(rec)

    outdir = os.path.join(WORK, "sectors")
    os.makedirs(outdir, exist_ok=True)
    for f in glob.glob(os.path.join(outdir, "*.json")):
        os.remove(f)

    total = collections.Counter()
    for sec in sorted(sectors):
        s = sectors[sec]
        for b in ("navs", "resources", "mobs", "stations", "gates", "planets"):
            s[b].sort(key=lambda r: r["gid"])
            total[b] += len(s[b])
        out = {"sector_id": sec, "sector_name": id2name.get(sec, str(sec))}
        out.update(s)
        with open(os.path.join(outdir, f"{sec}.json"), "w") as fh:
            json.dump(out, fh, indent=1)
        print(f"sector {sec:5d} {out['sector_name']:<16} "
              f"navs={len(s['navs']):3d} resources={len(s['resources']):4d} "
              f"mobs={len(s['mobs']):3d} stations={len(s['stations'])} "
              f"gates={len(s['gates'])} planets={len(s['planets'])}")
    print(f"\naggregate: {len(sectors)} sectors -> {outdir}")
    print("totals:", dict(total))
    print("excluded:", dict(excl))


if __name__ == "__main__":
    main()
