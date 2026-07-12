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
import calendar
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

# Confidence for a mob/resource whose sector was set by WALL-CLOCK binning against
# the survey driver's sector-map.tsv boundaries. Ground truth from the driver's
# in-game map-title reads, so it wins gid collisions against a marker/nav-vote
# attribution the same way an authoritative nav does.
WALLCLOCK_CONF = 10_000


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


# jsonl files whose basename does not norm-match the DB sector name (abbreviations
# and divergent spellings). Without this, every nav in these catalogs resolves to
# NO sector and is dropped -- including navs for sectors we import (Asteroid Belt
# Alpha, Pluto and Charon). Maps basename-norm -> the DB sector NAME (resolved via
# norm2id below). PLANET-SURFACE catalogs (PlanetDahin, PlanetArduinne, ...) are
# deliberately NOT listed: their navs are on the planet surface, a different sector
# instance than the space sector, so folding them into the space sector would be
# wrong. MoonRisco / FishBowl have no matching DB sector and stay ignored.
JSONL_SECTOR_ALIAS = {
    "aba": "Asteroid Belt Alpha",
    "abb": "Asteroid Belt Beta",
    "abg": "Asteroid Belt Gamma",
    "altair3": "Altair III",
    "ceres": "Ceres/Thule",
    "equatorial": "Equatorial Earth",
    "mazzaroth": "Mazzaroth Maelstrom",
    "mondara": "Mondara Maelstrom",
    "nifleheim": "Nifleheim Cloud",
    "pluto": "Pluto and Charon",
    "todesengel": "der Todesengel",
    # Not a jsonl basename: "Maeldun's Weft" is a wormhole-exit pocket the driver's
    # map-title read reports as the current sector, but it has no DB sector row of
    # its own -- it is a nav WITHIN Kailaasa (docs/sectors/json/Kailaasa.jsonl).
    # Alias its wall-clock window to Kailaasa so its mobs/resources are attributed
    # to their true home instead of being dropped (and silence the unresolved
    # -boundary warning, which is reserved for genuinely-missing real sectors).
    "maeldunsweft": "Kailaasa",
}


def load_nav_jsonl(norm2id):
    """norm(nav name) -> set of sector_ids it is listed in, from
    docs/sectors/json/<Sector>.jsonl. THE authoritative nav membership source."""
    root = repo_root()
    nav2secs = collections.defaultdict(set)
    unmatched_files = []
    for path in sorted(glob.glob(os.path.join(root, "docs", "sectors", "json", "*.jsonl"))):
        base = os.path.basename(path)[:-6]
        sid = norm2id.get(norm(base))
        if sid is None and norm(base) in JSONL_SECTOR_ALIAS:
            sid = norm2id.get(norm(JSONL_SECTOR_ALIAS[norm(base)]))
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


def iso_epoch(iso):
    """UTC epoch seconds from an ISO `YYYY-MM-DDThh:mm:ssZ` string, or None."""
    m = re.match(r"(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2}):(\d{2})Z", iso)
    if not m:
        return None
    return calendar.timegm(tuple(int(g) for g in m.groups()) + (0, 0, 0))


def _is_gate_or_placeholder(name):
    """True for sector-map.tsv rows that legitimately have no DB sector: the survey
    driver's transient "entry" marker and the in-flight gate titles it reads off the
    map while crossing ("Gate to Sol System", "Sector Gate to Dahin", "System Gate
    to Aragoth"). These are the rows load_sector_bounds skips silently; any OTHER
    unresolved name is a real sector and must be surfaced, not swallowed."""
    n = norm(name)
    if n == "entry":
        return True
    low = name.strip().lower()
    return low.startswith("gate to") or low.startswith("sector gate to") \
        or low.startswith("system gate to")


def load_sector_bounds(norm2id):
    """token (the `YYYYMMDDThhmmssZ` in a pcap name) -> sorted list of
    (epoch_seconds, sector_id) sector BOUNDARIES the survey driver recorded for
    that run, from <ENB_CAPS_DIR>/captures/sector-map.tsv.

    A continuous survey run writes one boundary row at each in-game sector change
    (`<iso-utc>\\t<sector-name>\\t<pcap-filename>`); all rows of one run share the
    run's START token in their pcap filename (the single continuous capture is
    relabelled at each boundary, so its final on-disk name is the LAST sector). The
    iso-utc column is the driver's wall-clock at the moment it read the new sector
    from the in-game MAP TITLE -- the SAME system clock the pcap packet timestamps
    use -- so an object's capture epoch bins directly to the sector whose boundary
    most recently preceded it (`wallclock_sector`).

    This is the AUTHORITATIVE segmenter for a multi-sector transit capture. The
    in-stream sector carriers (0x0036 SERVER_REDIRECT, 0x003A SERVER_HANDOFF,
    0x006F GLOBAL_TICKET) are galaxy-map/adjacency NOISE decoupled from actual
    flight -- for a transit blob they never reliably name the sector currently
    being flown, so a whole ~30-min entry-sector stay can land with NO usable
    marker and its mobs/resources get mis-tagged or dropped (the Grissom
    data-loss). Wall-clock against the driver's map reads is the only reliable fix.
    Navs are unaffected: they always resolve by NAME via the jsonl.

    Placeholder / gate rows ("entry", "Gate to ... System", "Sector Gate to ...")
    do not resolve to a DB sector and are skipped, so a real sector's window
    absorbs the brief gate interval. Missing file / unset ENB_CAPS_DIR yields an
    empty map (the feature is optional; captures with no run in the map fall back
    to the marker/nav-vote logic)."""
    caps = os.environ.get("ENB_CAPS_DIR")
    if not caps:
        return {}
    path = os.path.join(caps, "captures", "sector-map.tsv")
    if not os.path.isfile(path):
        alt = os.path.join(caps, "sector-map.tsv")
        if os.path.isfile(alt):
            path = alt
    bounds = collections.defaultdict(list)
    unresolved = collections.Counter()
    try:
        fh = open(path)
    except OSError:
        return {}
    with fh:
        for line in fh:
            parts = line.rstrip("\n").split("\t")
            if len(parts) < 3:
                continue
            iso, name, pcap = parts[0], parts[1], parts[2]
            m = re.search(r"\d{8}T\d{6}Z", pcap)
            if not m:
                continue
            n = norm(name)
            sid = norm2id.get(n)
            if sid is None and n in JSONL_SECTOR_ALIAS:
                sid = norm2id.get(norm(JSONL_SECTOR_ALIAS[n]))
            if sid is None:
                # Gate/placeholder rows ("entry", "Gate to ... System", "Sector Gate
                # to ...") legitimately have no DB sector -- a real sector's window
                # absorbs the brief gate interval, so skip them silently. But a name
                # that is NOT a gate/placeholder and still does not resolve is a REAL
                # sector whose whole wall-clock window we would silently mis-bin into
                # its neighbour (the ABA/Pluto data-loss). Surface it loudly so a
                # missing alias gets added, never silently dropped.
                if not _is_gate_or_placeholder(name):
                    unresolved[name] += 1
                continue
            ep = iso_epoch(iso)
            if ep is None:
                continue
            bounds[m.group(0)].append((ep, sid))
    for tok in bounds:
        bounds[tok].sort()
    if unresolved:
        print("aggregate: WARNING -- sector-map.tsv boundary name(s) did NOT resolve "
              "to a DB sector and were dropped (their window mis-bins into the "
              "previous sector -- add a JSONL_SECTOR_ALIAS entry): "
              + ", ".join(f"{nm!r}x{c}" for nm, c in unresolved.most_common()),
              file=sys.stderr)
    return dict(bounds)


def wallclock_sector(epoch, seq):
    """Sector of the last boundary at/before this object's capture epoch. An object
    seen before the first boundary is clamped to the first (entry) sector -- a
    capture opens a few seconds into the entry sector, before the driver's first
    map read. seq is the sorted [(epoch, sector_id)] for the capture's run."""
    sec = seq[0][1]
    for bep, bsid in seq:
        if epoch >= bep:
            sec = bsid
        else:
            break
    return sec


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


# A capture starts in whatever sector we were flying when it began -- i.e. the
# sector the PREVIOUS, time-adjacent capture ended in. We only chain that across a
# small time gap: a larger gap means the survey stopped and re-logged in somewhere
# unknown, so the previous end sector is no longer where we are. Sector-to-sector
# in one continuous session is minutes; 30 min is a generous continuity window.
SESSION_GAP_SECS = 30 * 60


def cap_epoch(path):
    """UTC epoch seconds from the filename stamp (for session-gap chaining), or
    the file mtime, or None."""
    m = re.search(r"(\d{8})T(\d{6})Z", os.path.basename(path))
    if m:
        try:
            return calendar.timegm(
                (int(m.group(1)[:4]), int(m.group(1)[4:6]), int(m.group(1)[6:8]),
                 int(m.group(2)[:2]), int(m.group(2)[2:4]), int(m.group(2)[4:6]), 0, 0, 0))
        except ValueError:
            pass
    try:
        return int(os.path.getmtime(path))
    except OSError:
        return None


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


def premarker_nav_sector(objs, first_frame, nav2secs):
    """The sector the pre-first-marker window was flown in, derived from the navs
    seen in that window (resolved authoritatively by name). A capture that has no
    time-adjacent predecessor to chain from can still be anchored this way: the
    navs visible before the first gate marker ARE the starting sector. Returns the
    dominant sector only when the evidence is unambiguous (>=3 resolving votes and
    a strict majority over every other sector combined), else None -- a split or
    thin window stays anchorless rather than inventing a location for its mobs."""
    votes = collections.Counter()
    for o in objs:
        if (o.get("frame") or 0) >= first_frame:
            continue                              # has a real marker segment
        if categorize(o) != "nav" or not o.get("name"):
            continue
        for s in nav2secs.get(norm(o["name"]), ()):
            votes[s] += 1
    if not votes:
        return None
    top_sec, top_votes = votes.most_common(1)[0]
    if top_votes >= 3 and top_votes > sum(votes.values()) - top_votes:
        return top_sec
    return None


def main():
    id2name, norm2id = sector_names()
    valid = set(id2name)
    nav2secs, unmatched = load_nav_jsonl(norm2id)
    if unmatched:
        print(f"aggregate: WARNING -- {len(unmatched)} jsonl file(s) did not match a "
              f"sector and were ignored: {', '.join(unmatched[:8])}"
              f"{' ...' if len(unmatched) > 8 else ''}")
    bounds_by_tok = load_sector_bounds(norm2id)
    if bounds_by_tok:
        multi = sum(1 for b in bounds_by_tok.values() if len(b) > 1)
        print(f"aggregate: sector-map.tsv loaded -- {len(bounds_by_tok)} capture "
              f"run(s) have driver wall-clock boundaries ({multi} multi-sector)")

    files = sorted(glob.glob(os.path.join(WORK, "objjson", "*.json")),
                   key=cap_timestamp)
    if not files:
        sys.exit(f"aggregate: no decoder JSON in {WORK}/objjson -- run decode.sh first")

    # Sectors the driver was PHYSICALLY in: every sector named by a wall-clock
    # boundary (a real map-title read) plus every sector that is the resolved
    # subject of a capture's own filename. A mob/resource placed by the MARKER
    # FALLBACK (Pass 3a) may only land in one of these -- the in-stream 0x003A
    # marker also carries a gate's DESTINATION sector, so a capture taken while
    # parked AT a gate ("Sector Gate to <X>", no wall-clock window) would other-
    # wise tag gate-adjacent objects with a sector we never entered (observed:
    # 2 objects mis-placed into Menorb from a "Sector Gate to Menorb" park).
    # Wall-clock and nav placements are already authoritative and are not gated.
    physical_sectors = {sid for seq in bounds_by_tok.values() for _, sid in seq}
    for path in files:
        tok = os.path.basename(path).split("__")[0]
        sid = norm2id.get(norm(tok))
        if sid is not None:
            physical_sectors.add(sid)

    merged = {}          # global gkey -> kept record (with ._cat, .sector, ._conf)
    excl = collections.Counter()
    relabels = []        # (capture, marker_sec, true_sec, votes) for the report
    wc_runs = []         # (capture, {sector_id: count}) wall-clock-binned windows
    prev_end = None      # end sector of the previous (time-adjacent) capture
    prev_ts = None       # its epoch, for the session-gap chain check

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

        # The filename sector prefix -- the no-marker fallback, and the end-sector
        # for chaining into the NEXT capture's pre-marker objects.
        fname_sector = norm2id.get(norm(fname.split("__")[0]))
        cur_ts = cap_epoch(path)
        objs = d.get("objects", [])

        cap_sector = None
        if not markers:
            cap_sector = fname_sector
            if cap_sector is None:
                excl["capture_unresolved_no_marker"] += len(objs)
                prev_end, prev_ts = None, cur_ts   # unknown sector breaks the chain
                continue

        # Objects before the first in-stream marker belong to the sector we were
        # flying when the capture began. Two independent ways to know that sector,
        # in increasing authority:
        #   (a) CHAIN -- the sector the previous, time-adjacent capture ended in
        #       (only within one session, see SESSION_GAP_SECS; a larger gap means
        #       we re-logged in somewhere unknown).
        #   (b) OWN NAVS -- the navs visible in the pre-marker window resolve by
        #       name to the starting sector directly. This is authoritative and
        #       overrides the chained guess: a capture with no predecessor to chain
        #       from is still fully anchored whenever its opening window names a
        #       sector, so its mobs/resources are placed instead of dropped.
        #   (c) DRIVER GROUND TRUTH -- the survey driver's sector-map.tsv records
        #       the sector we entered this session in, read from the in-game map
        #       title. This BEATS (a) and (b): a transit run's entry sector often
        #       has NO in-stream marker (its whole stay is pre-marker) and its
        #       pre-marker navs are galaxy-map adjacency noise (foreign sectors),
        #       so (b) returns nothing and the entry sector's mobs/resources would
        #       be dropped -- exactly the Grissom data-loss. When we have it, we
        #       also EXEMPT this segment from the Pass-2 nav-vote relabel (below):
        #       the noise navs would otherwise vote the entry sector's own mobs to
        #       a foreign sector.
        pre_sector = None
        if markers and prev_end is not None and prev_ts is not None \
                and cur_ts is not None and 0 <= cur_ts - prev_ts <= SESSION_GAP_SECS:
            pre_sector = prev_end
        if markers:
            own = premarker_nav_sector(objs, markers[0][0], nav2secs)
            if own is not None:
                pre_sector = own

        # The driver's wall-clock boundary sequence for this run, if we have one.
        # When present it is the AUTHORITATIVE sector source for mobs/resources
        # (binned by each object's capture epoch); the marker/pre_sector logic
        # above is the fallback for captures/objects not covered by it.
        m = re.search(r"\d{8}T\d{6}Z", fname)
        wseq = bounds_by_tok.get(m.group(0)) if m else None

        # This capture's end sector, for the NEXT capture's chain.
        prev_end = markers[-1][1] if markers else cap_sector
        prev_ts = cur_ts

        # Pass 1: tag each object with its category, its WALL-CLOCK sector
        # (authoritative for mobs/resources when the run has a boundary sequence),
        # and its marker-segment sector (the fallback, and the nav multi-sector
        # tiebreak). The mob/resource drop decision is deferred to Pass 3a because
        # wall-clock can place an object the marker logic could not.
        tagged = []
        for o in objs:
            cat = categorize(o)
            if cat == "player":
                excl["player"] += 1
                continue
            wc = None
            if wseq is not None and o.get("epoch") is not None:
                wc = wallclock_sector(o["epoch"], wseq)
            if cap_sector is not None:
                mseg = cap_sector
            else:
                mseg = derive_sector(o.get("frame") or 0, markers)
                if mseg is None:                 # pre-first-marker
                    mseg = pre_sector
            tagged.append((o, mseg, cat, wc))

        # Pass 2: per marker-segment, decide the MOB/RESOURCE sector from the navs
        # seen in that same segment (jsonl-resolved). Navs themselves are resolved
        # authoritatively below, independent of this.
        seg_navs = collections.defaultdict(list)
        for o, mseg, cat, wc in tagged:
            if cat == "nav" and o.get("name"):
                seg_navs[mseg].append(o["name"])
        seg_true = {}
        cap_relabels = []    # deferred: only reported if the marker path is used
        for mseg in set(m for _, m, _, _ in tagged):
            if mseg is None:
                continue
            ts, conf = segment_true_sector(mseg, seg_navs.get(mseg, []), nav2secs)
            seg_true[mseg] = (ts, conf)
            if ts != mseg:
                cap_relabels.append((fname, mseg, ts, conf))

        # Pass 3a: assign each object's final sector + confidence and collapse by
        # (category, gid) WITHIN this capture. The server RECYCLES a GameID across
        # unrelated objects within one long transit capture -- the same gid is a mob
        # (0x0004) in one sector and a resource (0x2019) in another -- so folding on
        # the gid ALONE would collapse those two real objects into one (the later /
        # equal-confidence write wins, and resources dominate, so the mob is silently
        # converted to a resource -- observed 189 of 241 mob gids lost in a single
        # capture). Keying on (cat, gid) keeps each real object; within one store a
        # gid maps to exactly one object, so this still merges an object seen twice.
        cap = {}
        wc_placed = collections.Counter()
        used_marker = False   # did any mob/resource fall through to the marker path?
        for o, mseg, cat, wc in tagged:
            if cat == "nav":
                secs = nav2secs.get(norm(o.get("name")))
                if not secs:
                    excl["nav_not_in_jsonl"] += 1
                    continue
                if len(secs) == 1:
                    sec = next(iter(secs))
                else:
                    # Prefer the sector we were physically in: wall-clock, then the
                    # marker segment's corroborated sector, else lowest id.
                    pref = wc if wc in secs else \
                        (seg_true.get(mseg, (None,))[0] if mseg is not None else None)
                    sec = pref if pref in secs else min(secs)
                conf = NAV_CONF
            elif wc is not None:                 # authoritative wall-clock bin
                sec, conf = wc, WALLCLOCK_CONF
                wc_placed[sec] += 1
            elif mseg is not None:               # marker/nav-vote fallback
                sec, conf = seg_true[mseg]
                if sec not in physical_sectors:
                    # Marker names a sector the driver was never physically in
                    # (gate-destination adjacency noise) -- placing it here would
                    # be inventing a location. Drop it.
                    excl["marker_sector_never_visited"] += 1
                    continue
                used_marker = True
            else:
                # No wall-clock and no marker segment -- placing this mob/resource
                # would be inventing a location, so drop it.
                excl["before_first_marker"] += 1
                continue
            o = dict(o)
            o["sector"] = sec
            o["_cat"] = cat
            o["_conf"] = conf
            fold(cap, (cat, o["gid"]), o)

        if wc_placed:
            wc_runs.append((fname, dict(wc_placed)))
        if used_marker:
            relabels.extend(cap_relabels)

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
    if wc_runs:
        print("\nwall-clock-binned runs (sector-map.tsv boundaries, mob/resource "
              "objects per sector):")
        for fn, dist in wc_runs:
            parts = ", ".join(f"{id2name.get(s, s)}={n}"
                              for s, n in sorted(dist.items(), key=lambda kv: -kv[1]))
            print(f"  {fn}: {parts}")
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
