#!/usr/bin/env python3
# SPDX-License-Identifier: MIT
# Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
# License: LICENSES/Freya
#
# gen_prospecting.py -- turn docs/prospecting/*.jsonl into committed SQL that
# (A) maps which prospectable resources spawn in which sector (base_ore_list)
# and (B) creates resource FIELDS the data indicates but the DB lacks.
#
# Why this is correct (server selection path):
#   ItemBaseManager::GetOreTemplate(level, obj_type, sector_id, field) builds a
#   candidate pool from base_ore_list for the sector whose item sub_category is
#   in asteroid_content_selection[obj_type] AND whose item level == the node's
#   level, then picks one uniformly. base_ore_list is SECTOR-LEVEL (no node-type
#   / level column): an item is reachable in a sector iff its sub_category is an
#   ore sub_category for SOME node type. So Part A just needs the right items
#   present per sector; the runtime filter routes each to the right node.
#
# Part A (base_ore_list): additive. For every (resource, sector) in
#   prospecting.jsonl, resolve the item by name, and if its sub_category is a
#   real ore sub_category, add (item_id, sector_id, name, 0) unless already
#   present. Never prunes existing rows (prospecting covers ~54 of 97 sectors).
#   frequency is 0 -- the server loads but does not use it (unweighted pick).
#
# Part B (fields): for every (sector, nav) in prospect_fields.jsonl with no
#   existing FIELD within NEAR units of the nav, create one invisible field
#   container per distinct node LEVEL at the nav position:
#     sector_objects        type 38 (OT_RESOURCE), base_asset_id 0 (a field's
#                           model is 0; its children render from restypes)
#     sector_objects_harvestable  max_field_radius>0 + res_count>0 => it loads
#                           as OT_FIELD (a container that spawns child rocks)
#     ..._restypes          one representative child model per node type present
#                           at that level (Rock=Metal, Crystal=Gem, Glowing=
#                           Reactive, Hydrocarbon, Gas, Hulk=Inorganic)
#   No oretypes row is emitted: the children draw from base_ore_list (Part A)
#   via the sector+sub_category+level filter, exactly like the base-data fields.
#   No sector_nav_points row: the nav already exists as its own object; the
#   field container is invisible.
#
# Determinism + idempotency:
#   Part A inserts are guarded by NOT EXISTS (base_ore_list has no unique key),
#   so re-applying is a no-op. Part B uses synthetic ids from SYNTH_BASE upward
#   assigned in sorted order, and a purge of the whole synth range up front, so
#   a full re-apply is a clean replace.
#
# DB note: every introspection query is a constant -- no value is concatenated
# into a query. Resolution + spatial matching happen in Python. The generated
# .sql is a script artifact (no runtime parameter channel), so it legitimately
# emits literal values; string literals are quote-doubled.
import collections
import difflib
import json
import math
import os
import re
import subprocess
import sys

PG_CONTAINER = os.environ.get("ENB_PG_CONTAINER", "freya-dev-postgres-1")
HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", ".."))
DOCS = os.path.join(REPO, "docs", "prospecting")
OUT_DIR = os.path.join(REPO, "db", "postgres", "prospecting")
WRAPPER = os.path.join(REPO, "db", "postgres", "seed_prospecting.sql")

SYNTH_BASE = 3_000_000          # synthetic sector_object_id: above captures (1-2M) + turrets (2M+)
NEAR = 8000.0                   # a field within NEAR of the nav counts as "already present"
FUZZY_MIN = 0.80               # min name similarity to accept a fuzzy nav resolution

# nodeType -> asteroid_content_selection key (resource category)
NODE_CAT = {"Rock": 2, "Crystal": 4, "Hydrocarbon": 3, "Glowing": 1, "Gas": 7, "Hulk": 10}
# nodeType -> a representative child model asset (must be an Asteroids/Hulks model)
NODE_MODEL = {"Rock": 1825, "Crystal": 1831, "Glowing": 1822,
              "Hydrocarbon": 1828, "Gas": 1834, "Hulk": 1131}
# asteroid_content_selection contents (server SectorContentSQL.cpp): the ore
# sub_categories each node type can select. Union = every real ore sub_category.
ACS = {1: {204}, 2: {110, 201, 202, 203, 208}, 3: {206, 207, 211},
       4: {110, 200, 205, 209}, 5: {207}, 7: {210}, 8: {201, 208}, 9: set(),
       10: {100, 101, 102, 103, 110, 120, 121, 122, 141, 142, 150, 151}}
ACS_UNION = set().union(*ACS.values())

# a few resource names differ in spelling between the prospecting source and the
# item table; map source -> item table.
RES_ALIAS = {"aluminium ore": "aluminum ore"}

# field container parameters (mirrors the common base-data field shape)
FIELD_RES_COUNT = 10
FIELD_MAX_RADIUS = 8000.0
FIELD_SPAWN_RADIUS = 0.0
FIELD_POP_ROCK = 3.0
FIELD_RESPAWN = 10
FIELD_FIELD = 0                  # `field` column: 0 dominates the base data


def pg(sql):
    out = subprocess.run(
        ["docker", "exec", PG_CONTAINER, "psql", "-U", "net7", "-d", "net7",
         "-tA", "-R", "\x1e", "-F", "\x1f", "-c", sql],
        capture_output=True, text=True, check=True).stdout.rstrip("\n")
    return [rec.split("\x1f") for rec in out.split("\x1e") if rec] if out else []


def norm(s):
    return re.sub(r"[^a-z0-9]", "", (s or "").lower())


def norm_sec(s):
    return (s or "").lower().replace("'", "").strip()


def strip_ann(s):
    s = s or ""
    s = re.sub(r"\(.*?\)", "", s)          # drop ( ... ) direction/notes
    s = re.sub(r"\bspaced.*", "", s, flags=re.I)
    return s.strip()


def sql_str(s):
    return "'" + str(s).replace("'", "''") + "'"


def num(v):
    return repr(float(v))


def dist(a, b):
    return math.dist(a, b)


class DB:
    def __init__(self):
        # sector name -> id (two normalizations, both consulted)
        self.sec_by_name = {}
        self.sec_by_norm = {}
        for sid, name in pg("SELECT sector_id, name FROM sectors"):
            if name:
                self.sec_by_name[norm_sec(name)] = int(sid)
                self.sec_by_norm[norm(name)] = int(sid)

        # item name (normalized) -> [(id, level, sub_category)]; id -> name;
        # id -> (level, sub_category)
        self.item_by_name = collections.defaultdict(list)
        self.item_name = {}
        self.item_by_id = {}
        for iid, name, lvl, sub in pg(
                "SELECT id, name, level, sub_category FROM item_base"):
            if not name:
                continue
            iid = int(iid)
            lvl = int(lvl) if lvl not in ("", None) else 0
            sub = int(sub) if sub not in ("", None) else 0
            self.item_by_name[norm(name)].append((iid, lvl, sub))
            self.item_name[iid] = name
            self.item_by_id[iid] = (lvl, sub)

        # existing base_ore_list pairs
        self.ore_pairs = set()
        for iid, sid in pg("SELECT item_id, sector_id FROM base_ore_list"):
            self.ore_pairs.add((int(iid), int(sid)))

        # all named objects per sector: [(type, raw, norm, pos)] for nav resolve
        self.objs = collections.defaultdict(list)
        for sid, typ, name, x, y, z in pg(
                "SELECT sector_id, type, name, position_x, position_y, "
                "position_z FROM sector_objects WHERE name IS NOT NULL AND name<>''"):
            if x == "" or y == "" or z == "":
                continue
            self.objs[int(sid)].append(
                (int(typ or 0), name, norm(name), (float(x), float(y), float(z))))

        # existing harvestables per sector: [(pos, is_field)]
        self.harv = collections.defaultdict(list)
        for sid, x, y, z, rad, rc in pg(
                "SELECT o.sector_id, o.position_x, o.position_y, o.position_z, "
                "h.max_field_radius, h.res_count FROM sector_objects o "
                "JOIN sector_objects_harvestable h ON h.resource_id=o.sector_object_id "
                "WHERE o.type=38"):
            if x == "" or y == "" or z == "":
                continue
            is_field = (float(rad or 0) > 0) and (int(rc or 0) > 0)
            self.harv[int(sid)].append(((float(x), float(y), float(z)), is_field))

        # confirm representative models are real Asteroids/Hulks assets
        valid_models = {int(r[0]) for r in pg(
            "SELECT base_id FROM assets WHERE main_cat IN ('Asteroids','Hulks')")}
        bad = [m for m in NODE_MODEL.values() if m not in valid_models]
        if bad:
            sys.exit(f"gen_prospecting: representative models not in assets: {bad}")

    def sector_id(self, name):
        return self.sec_by_name.get(norm_sec(name)) or self.sec_by_norm.get(norm(name))

    def resolve_item(self, resname):
        rn = RES_ALIAS.get(resname.lower(), resname.lower())
        cands = self.item_by_name.get(norm(rn))
        return cands[0] if cands else None     # (id, level, sub)

    def resolve_pos(self, sid, nav):
        """Resolve a prospect_fields nav label to a position. Returns
        (pos, how) or (None, None)."""
        cand = self.objs.get(sid, [])
        nn = norm(nav)
        ns = norm(strip_ann(nav))
        for o in cand:
            if o[2] == nn:
                return o[3], "exact"
        for part in re.split(r"[/]", nav):     # "Kitara's Gate/Nav P18" -> try each
            pnn = norm(part)
            if pnn:
                for o in cand:
                    if o[2] == pnn:
                        return o[3], "slash"
        for o in cand:
            if ns and o[2] == ns:
                return o[3], "ann"
        names = [o[1] for o in cand]
        best = difflib.get_close_matches(nav, names, n=1, cutoff=0.0)
        if best:
            ratio = difflib.SequenceMatcher(None, norm(nav), norm(best[0])).ratio()
            if ratio >= FUZZY_MIN:
                for o in cand:
                    if o[1] == best[0]:
                        return o[3], f"fuzzy{ratio:.2f}"
        return None, None

    def has_field_near(self, sid, pos):
        return any(f[1] and dist(pos, f[0]) <= NEAR for f in self.harv.get(sid, []))


# ---- Part A: base_ore_list --------------------------------------------------

def part_a(db):
    pairs = {}        # (resname, secname) -> (nodeType, level)  unique
    for line in open(os.path.join(DOCS, "prospecting.jsonl")):
        line = line.strip()
        if not line:
            continue
        r = json.loads(line)
        pairs[(r["resource"], r["sector"])] = (r["nodeType"], r["level"])

    to_add = []       # (item_id, sector_id, name)
    unresolved_res = collections.Counter()
    unresolved_sec = collections.Counter()
    not_ore = collections.Counter()
    already = 0
    seen = set()
    for (resname, secname), _ in sorted(pairs.items()):
        sid = db.sector_id(secname)
        if sid is None:
            unresolved_sec[secname] += 1
            continue
        item = db.resolve_item(resname)
        if item is None:
            unresolved_res[resname] += 1
            continue
        iid, _ilvl, isub = item
        if isub not in ACS_UNION:
            not_ore[f"{resname} (sub{isub})"] += 1
            continue
        if (iid, sid) in db.ore_pairs:
            already += 1
            continue
        if (iid, sid) in seen:
            continue
        seen.add((iid, sid))
        to_add.append((iid, sid, db.item_name[iid]))

    to_add.sort(key=lambda t: (t[1], t[0]))
    lines = [
        "-- base_ore_list.sql -- GENERATED by tools/prospecting-import.",
        "-- Maps which prospectable resources spawn in which sector, driven by",
        "-- docs/prospecting/prospecting.jsonl. Additive: each row is inserted",
        "-- only if (item_id, sector_id) is not already present (base_ore_list",
        "-- has no unique key, so NOT EXISTS is the idempotency guard).",
        "-- frequency is 0: the server loads but does not use it.",
        "BEGIN;",
    ]
    for iid, sid, name in to_add:
        lines.append(
            "INSERT INTO base_ore_list (item_id, name, sector_id, frequency) "
            f"SELECT {iid}, {sql_str(name)}, {sid}, 0 WHERE NOT EXISTS "
            f"(SELECT 1 FROM base_ore_list WHERE item_id={iid} AND sector_id={sid});")
    lines.append("COMMIT;")
    lines.append("")
    with open(os.path.join(OUT_DIR, "base_ore_list.sql"), "w") as fh:
        fh.write("\n".join(lines))

    # Reachability set AFTER these additions: (sector_id, sub_category, level)
    # present in base_ore_list once base_ore_list.sql is applied. Part B uses it
    # to drop field children that could never spawn (a node category+level with
    # no matching ore in the sector -- the empty-field defect).
    reachable = set()
    for iid, sid in db.ore_pairs:
        lv = db.item_by_id.get(iid)
        if lv:
            reachable.add((sid, lv[1], lv[0]))
    for iid, sid, _ in to_add:
        lv = db.item_by_id.get(iid)
        if lv:
            reachable.add((sid, lv[1], lv[0]))

    report = [
        f"Part A (base_ore_list): {len(to_add)} additions across "
        f"{len({s for _, s, _ in to_add})} sectors",
        f"  already present       : {already}",
        f"  not an ore sub_cat    : {sum(not_ore.values())} ({len(not_ore)} distinct)",
        f"  unresolved resource   : {sum(unresolved_res.values())} "
        f"({len(unresolved_res)} distinct) {unresolved_res.most_common(5)}",
        f"  unresolved sector     : {sum(unresolved_sec.values())} {dict(unresolved_sec)}",
    ]
    return report, reachable


# ---- Part B: missing fields -------------------------------------------------

def part_b(db, reachable):
    def spawnable(sid, nt, lvl):
        """True iff some ore in this sector's base_ore_list (post Part A) matches
        the node category at this level -- i.e. GetOreTemplate would find a
        candidate. Otherwise the child would spawn nothing."""
        cat = NODE_CAT[nt]
        return any((sid, sub, lvl) in reachable for sub in ACS[cat])

    fields = []       # (sid, secname, nav, pos, level, [node types])
    have_field = 0
    noresolve = []
    unresolved_sec = collections.Counter()
    unknown_nav = 0
    dropped_empty = 0
    rows = []
    for line in open(os.path.join(DOCS, "prospect_fields.jsonl")):
        line = line.strip()
        if line:
            rows.append(json.loads(line))

    for r in sorted(rows, key=lambda r: (r["sector"], r.get("nav") or "")):
        sid = db.sector_id(r["sector"])
        if sid is None:
            unresolved_sec[r["sector"]] += 1
            continue
        nav = r.get("nav")
        if not nav or nav == "?":
            unknown_nav += 1
            continue
        pos, how = db.resolve_pos(sid, nav)
        if pos is None:
            noresolve.append((r["sector"], nav))
            continue
        if db.has_field_near(sid, pos):
            have_field += 1
            continue
        # group node types by level, keeping only children that can actually
        # spawn an ore in this sector at that level (drop the empty-field defect)
        types = r.get("types") or {}
        by_level = collections.defaultdict(list)
        for nt, levels in types.items():
            if nt not in NODE_MODEL:
                continue
            for lvl in levels:
                if spawnable(sid, nt, int(lvl)):
                    by_level[int(lvl)].append(nt)
        for lvl in sorted(by_level):
            nts = sorted(set(by_level[lvl]))
            if nts:
                fields.append((sid, r["sector"], nav, pos, lvl, nts))
            else:
                dropped_empty += 1

    next_id = SYNTH_BASE
    lines = [
        "-- fields.sql -- GENERATED by tools/prospecting-import.",
        "-- Resource FIELDS that docs/prospecting/prospect_fields.jsonl indicates",
        "-- but the DB lacked (no field within "
        f"{int(NEAR)} units of the nav). One invisible",
        "-- field container per distinct node level at the nav position; children",
        "-- render from the restypes models and draw ore from base_ore_list.",
        "BEGIN;",
        "-- purge any prior prospecting-generated synthetic rows (clean re-apply)",
        f"DELETE FROM sector_objects_harvestable_restypes WHERE group_id >= {SYNTH_BASE};",
        f"DELETE FROM sector_objects_harvestable WHERE resource_id >= {SYNTH_BASE};",
        f"DELETE FROM sector_objects WHERE sector_object_id >= {SYNTH_BASE};",
        "",
    ]
    for sid, secname, nav, pos, lvl, nts in fields:
        oid = next_id
        next_id += 1
        nm = f"{nav} Field L{lvl}"
        lines.append(
            "INSERT INTO sector_objects (sector_object_id, base_asset_id, h, s, v, "
            "type, scale, position_x, position_y, position_z, orientation_u, "
            "orientation_v, orientation_w, orientation_z, name, appears_in_radar, "
            "radar_range, sector_id, gate_to, sound_effect_id, sound_effect_range) "
            f"VALUES ({oid}, 0, 0.0, 0.0, 0.0, 38, 1.0, {num(pos[0])}, {num(pos[1])}, "
            f"{num(pos[2])}, 0.0, 0.0, 0.0, 0.0, {sql_str(nm)}, 0, 5000.0, {sid}, "
            "NULL, NULL, NULL);")
        lines.append(
            "INSERT INTO sector_objects_harvestable (resource_id, level, field, "
            "res_count, spawn_radius, pop_rock_chance, max_field_radius, respawn_timer) "
            f"VALUES ({oid}, {lvl}, {FIELD_FIELD}, {FIELD_RES_COUNT}, "
            f"{num(FIELD_SPAWN_RADIUS)}, {num(FIELD_POP_ROCK)}, {num(FIELD_MAX_RADIUS)}, "
            f"{FIELD_RESPAWN});")
        for j, nt in enumerate(nts):
            lines.append(
                "INSERT INTO sector_objects_harvestable_restypes (id, group_id, type) "
                f"VALUES ({oid * 100 + j}, {oid}, {NODE_MODEL[nt]});")
        lines.append("")
    lines.append("COMMIT;")
    lines.append("")
    with open(os.path.join(OUT_DIR, "fields.sql"), "w") as fh:
        fh.write("\n".join(lines))

    report = [
        f"Part B (fields): {len(fields)} field containers across "
        f"{len({(s, n) for s, _, n, _, _, _ in fields})} navs",
        f"  navs that already have a field : {have_field}",
        f"  empty (no spawnable ore) drop  : {dropped_empty}",
        f"  nav unresolved (report-only)   : {len(noresolve)} {noresolve}",
        f"  sector unresolved              : {sum(unresolved_sec.values())} "
        f"{dict(unresolved_sec)}",
        f"  nav '?'/blank                  : {unknown_nav}",
    ]
    return report, bool(fields)


def assert_pristine():
    """Refuse to regenerate against a DB that already has the prospecting import
    applied. Part A is an additions-over-base diff, so a DB carrying its own
    prior additions makes base_ore_list.sql regenerate EMPTY (everything reads as
    'already present'). The synthetic field range (>= SYNTH_BASE) is the marker.
    Bring up a clean base+captures DB first:
        docker compose -p clean down -v
        ENB_SKIP_PROSPECTING_SEED=1 docker compose -p clean up -d schema-init
    then point ENB_PG_CONTAINER at it."""
    n = pg(f"SELECT count(*) FROM sector_objects WHERE sector_object_id >= {SYNTH_BASE}")
    if n and int(n[0][0]) > 0:
        sys.exit(
            f"gen_prospecting: REFUSING -- {n[0][0]} prospecting field row(s) "
            f"(id >= {SYNTH_BASE}) already exist in this DB, so it is NOT a clean "
            "base. base_ore_list.sql would regenerate EMPTY. Regenerate against a "
            "base+captures DB with ENB_SKIP_PROSPECTING_SEED=1 (see the docstring).")


def main():
    os.makedirs(OUT_DIR, exist_ok=True)
    db = DB()
    assert_pristine()
    a_report, reachable = part_a(db)
    b_report, has_fields = part_b(db, reachable)

    wrapper = [
        "-- seed_prospecting.sql -- GENERATED by tools/prospecting-import.",
        "-- Applies prospecting-driven data: base_ore_list (which resources spawn",
        "-- in which sector) and the resource fields the prospecting data indicates",
        "-- but the DB lacked. Regenerate with tools/prospecting-import; do not",
        "-- hand-edit. Runs AFTER the base seeds and the capture import.",
        "",
        "\\ir prospecting/base_ore_list.sql",
    ]
    if has_fields:
        wrapper.append("\\ir prospecting/fields.sql")
    with open(WRAPPER, "w") as fh:
        fh.write("\n".join(wrapper) + "\n")

    print("\n".join(a_report))
    print()
    print("\n".join(b_report))
    print(f"\n-> {OUT_DIR}\n-> {WRAPPER}")


if __name__ == "__main__":
    main()
