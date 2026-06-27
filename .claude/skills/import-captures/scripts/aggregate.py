#!/usr/bin/env python3
# SPDX-License-Identifier: MIT
# Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
# License: LICENSES/Freya
#
# aggregate.py -- turn the per-capture decoder JSONs (WORK/objjson/*.json) into
# one aggregated, de-duplicated object list PER SECTOR (WORK/sectors/<id>.json).
#
# Sector assignment, and why it works the way it does:
#
#   * NAVS are resolved AUTHORITATIVELY from docs/sectors/json/*.jsonl. That file
#     set is the ground truth for which nav belongs to which sector: if a nav name
#     is not in any sector's jsonl, the nav DOES NOT EXIST and is dropped; if it is
#     in exactly one sector's jsonl, it belongs to that sector regardless of which
#     sector marker the capture stream had at the time (markers are noisy around
#     gate handoffs and instanced sub-sectors, and the same gid can appear in two
#     captures under conflicting markers). Names are matched alphanumeric-normalized
#     (drop case/punctuation/spaces) because the jsonl uses curly apostrophes and
#     accents the wire names render as ASCII.
#
#   * MOBS / RESOURCES have no name catalog to anchor to, so they take the sector
#     of the marker-SEGMENT they were created in -- BUT that segment's marker is
#     cross-checked against the navs seen in the same segment (resolved via jsonl).
#     If the marker sector has ZERO nav corroboration in its segment and another
#     sector clearly dominates the segment's navs, the whole segment was
#     mis-marked (a capture that gated and never emitted the right handoff), so we
#     relabel it to the dominant sector. This is deliberately conservative: a
#     marker with ANY nav support is trusted, so legitimately mixed gate segments
#     (you see the next sector's navs across the gate while still in this one) keep
#     their marker. This is the "if a nav was misattributed by believing the wrong
#     sector, the mobs from that same belief are too" correction.
#
#   * Dedup is two-level, because object gids (GameIDs) are session-scoped: the
#     same gid number is REUSED for unrelated objects across different captures, so
#     a global gid-only key wrongly collapses different objects. WITHIN one capture
#     a gid IS stable, so we first collapse per-capture by gid -- keeping the
#     higher-confidence sector when a gid spans a relabel boundary (the same mob
#     seen in both the mis-marked and the corrected segment of one capture lands in
#     ONE sector, killing the cross-sector duplicate at its root). ACROSS captures
#     we then key navs by (sector, normalized-name) -- authoritative, gid-free, so a
#     nav can never duplicate into two sectors -- and mobs/resources by
#     (sector, gid). On collision we keep the latest position / any non-null fields.
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

# jsonl nav types -- the only types that count as a "nav" for membership.
NAV_JSONL_TYPES = {"nav-point", "hidden-nav-point", "special-nav-point"}

# Confidence assigned to an authoritative (jsonl-resolved) nav sector. Higher than
# any mob segment's nav-vote count, so a nav never loses a gid collision to a mob.
NAV_CONF = 10_000


def norm(s):
    """Alphanumeric-normalize a name for matching (jsonl uses curly apostrophes
    and accents that the wire names render as bare ASCII)."""
    return re.sub(r"[^a-z0-9]", "", (s or "").lower())


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
            norm2id[norm(name)] = sid
    return id2name, norm2id


def repo_root():
    """Walk up from this script until docs/sectors/json exists."""
    d = os.path.dirname(os.path.abspath(__file__))
    for _ in range(8):
        if os.path.isdir(os.path.join(d, "docs", "sectors", "json")):
            return d
        d = os.path.dirname(d)
    sys.exit("aggregate: could not locate repo root (docs/sectors/json) above this script")


def load_nav_jsonl(norm2id):
    """norm(nav name) -> set of sector_ids it is listed in, from
    docs/sectors/json/<Sector>.jsonl. THE authoritative nav membership source."""
    root = repo_root()
    nav2secs = collections.defaultdict(set)
    unmatched_files = []
    for path in sorted(glob.glob(os.path.join(root, "docs", "sectors", "json", "*.jsonl"))):
        sid = norm2id.get(norm(os.path.basename(path)[:-6]))
        if sid is None:
            unmatched_files.append(os.path.basename(path))
            continue
        with open(path) as fh:
            for line in fh:
                line = line.strip()
                if not line:
                    continue
                o = json.loads(line)
                if o.get("type") in NAV_JSONL_TYPES and o.get("name"):
                    nav2secs[norm(o["name"])].add(sid)
    return nav2secs, unmatched_files


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
        if k in ("x", "y", "z", "sector", "_cat", "_conf"):
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


def segment_true_sector(marker_sec, seg_navs, nav2secs):
    """Decide the sector a marker-segment's MOBS/RESOURCES belong to.

    Trust the marker unless it has ZERO nav corroboration in this segment AND
    another sector clearly dominates the segment's navs (the whole segment was
    mis-marked). Returns (true_sector, confidence) where confidence is the number
    of segment navs that resolve to the chosen sector -- used to break gid
    collisions across captures (a more strongly corroborated sector wins)."""
    votes = collections.Counter()
    for nm in seg_navs:
        for s in nav2secs.get(norm(nm), ()):  # a nav may list in >1 sector (rare)
            votes[s] += 1
    marker_votes = votes.get(marker_sec, 0)
    if votes:
        top_sec, top_votes = votes.most_common(1)[0]
        # Relabel only on strong, marker-uncorroborated evidence: another sector
        # with >=3 navs and >3x the marker's own nav support. marker_votes==0 ->
        # any top>=3 wins; marker_votes>=1 -> top must exceed 3x it.
        if top_sec != marker_sec and top_votes >= 3 and top_votes > 3 * marker_votes:
            return top_sec, top_votes
    return marker_sec, marker_votes


def main():
    id2name, norm2id = sector_names()
    valid = set(id2name)
    nav2secs, unmatched = load_nav_jsonl(norm2id)
    if unmatched:
        print(f"aggregate: WARNING -- {len(unmatched)} jsonl file(s) did not match a "
              f"sector and were ignored: {', '.join(unmatched[:8])}"
              f"{' ...' if len(unmatched) > 8 else ''}")

    files = sorted(glob.glob(os.path.join(WORK, "objjson", "*.json")),
                   key=cap_timestamp)
    if not files:
        sys.exit(f"aggregate: no decoder JSON in {WORK}/objjson -- run decode.sh first")

    merged = {}          # global gkey -> kept record (with ._cat, .sector, ._conf)
    excl = collections.Counter()
    relabels = []        # (capture, marker_sec, true_sec, votes) for the report

    def fold(store, key, rec):
        """Merge rec into store[key], keeping the higher-confidence sector and the
        newest position / non-null fields."""
        cur = store.get(key)
        if cur is None:
            store[key] = rec
            return
        merge(cur, rec)
        if rec["_conf"] >= cur.get("_conf", -1):
            cur["sector"] = rec["sector"]
            cur["_cat"] = rec["_cat"]
            cur["_conf"] = rec["_conf"]

    for path in files:
        with open(path) as fh:
            d = json.load(fh)
        fname = os.path.basename(path)
        markers = sorted(
            (m["frame"], s) for m in d.get("markers", [])
            for s in (norm_marker(int(m["sector"]), valid),) if s is not None)

        cap_sector = None
        if not markers:
            cap_sector = norm2id.get(norm(fname.split("__")[0]))
            if cap_sector is None:
                excl["capture_unresolved_no_marker"] += len(d.get("objects", []))
                continue

        objs = d.get("objects", [])

        # Pass 1: tag each object with its marker-segment sector and category.
        tagged = []
        for o in objs:
            mseg = cap_sector if cap_sector is not None \
                else derive_sector(o.get("frame") or 0, markers)
            if mseg is None:
                excl["before_first_marker"] += 1
                continue
            cat = categorize(o)
            if cat == "player":
                excl["player"] += 1
                continue
            tagged.append((o, mseg, cat))

        # Pass 2: per marker-segment, decide the MOB/RESOURCE sector from the navs
        # seen in that same segment (jsonl-resolved). Navs themselves are resolved
        # authoritatively below, independent of this.
        seg_navs = collections.defaultdict(list)
        for o, mseg, cat in tagged:
            if cat == "nav" and o.get("name"):
                seg_navs[mseg].append(o["name"])
        seg_true = {}
        for mseg in set(m for _, m, _ in tagged):
            ts, conf = segment_true_sector(mseg, seg_navs.get(mseg, []), nav2secs)
            seg_true[mseg] = (ts, conf)
            if ts != mseg:
                relabels.append((fname, mseg, ts, conf))

        # Pass 3a: assign each object's final sector + confidence and collapse by
        # gid WITHIN this capture (gid is session-stable here). This is where the
        # same physical object seen on both sides of a relabel boundary collapses
        # to its highest-confidence sector.
        cap = {}
        for o, mseg, cat in tagged:
            if cat == "nav":
                secs = nav2secs.get(norm(o.get("name")))
                if not secs:
                    excl["nav_not_in_jsonl"] += 1
                    continue
                if len(secs) == 1:
                    sec = next(iter(secs))
                else:
                    ts = seg_true[mseg][0]
                    sec = ts if ts in secs else min(secs)
                conf = NAV_CONF
            else:
                sec, conf = seg_true[mseg]
            o = dict(o)
            o["sector"] = sec
            o["_cat"] = cat
            o["_conf"] = conf
            fold(cap, o["gid"], o)

        # Pass 3b: fold this capture's deduped objects into the global store.
        # Navs key on (sector, normalized-name) -- gid-free, so a nav can never
        # live in two sectors. Everything else keys on (sector, gid).
        for o in cap.values():
            if o["_cat"] == "nav":
                gkey = ("nav", o["sector"], norm(o.get("name")))
            else:
                gkey = (o["_cat"], o["sector"], o["gid"])
            fold(merged, gkey, o)

    # bucket per sector
    sectors = collections.defaultdict(lambda: {
        "navs": [], "resources": [], "mobs": [],
        "stations": [], "gates": [], "planets": [],
    })
    cat_to_bucket = {"nav": "navs", "resource": "resources", "mob": "mobs",
                     "station": "stations", "gate": "gates", "planet": "planets"}
    for o in merged.values():
        sec = o["sector"]
        cat = o["_cat"]
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
    if relabels:
        print("\nsegment relabels (marker -> jsonl-corroborated sector):")
        for fn, mseg, ts, conf in relabels:
            print(f"  {fn}: {id2name.get(mseg, mseg)} ({mseg}) -> "
                  f"{id2name.get(ts, ts)} ({ts})  [{conf} navs]")
    print(f"\naggregate: {len(sectors)} sectors -> {outdir}")
    print("totals:", dict(total))
    print("excluded:", dict(excl))


if __name__ == "__main__":
    main()
