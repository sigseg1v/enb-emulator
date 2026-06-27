#!/usr/bin/env python3
# SPDX-License-Identifier: MIT
# Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
# License: LICENSES/Freya
#
# gen_sql.py -- turn the aggregated per-sector JSON (WORK/sectors/*.json) into
# committed per-sector SQL files that import the captured objects.
#
# Policy (the captures are accurate and take priority over current data):
#   NAVS       insert-if-absent by name (case-insensitive) within the sector.
#              Never delete an existing nav.
#   RESOURCES  the captures are the source of truth. For each captured rock,
#              any EXISTING harvestable within 5k whose ore type matches is
#              removed (only that specific object), then the captured rock is
#              inserted as an exact-position single-rock harvestable.
#   MOBS       likewise: resolve name(+asset/level) -> mob_base template; any
#              EXISTING mob spawn within 5k that spawns the SAME template is
#              removed, then the captured mob is inserted as a 1-mob spawn at
#              its exact position. Unresolved names are skipped and reported.
#   STATIONS / GATES / PLANETS  report-only. The capture lacks their child-row
#              data (dock/cap-ship/stargate routing), so creating them from a
#              capture would break them -- we only print what we saw.
#
# Determinism + idempotency: synthetic sector_object_ids are assigned from
# SYNTH_BASE upward, sorted by (sector_id, gid). Each per-sector file first
# deletes ITS OWN prior synthetic rows (sector_id = S AND id >= SYNTH_BASE), so
# re-applying the file is a clean replace. The 5k-replacement deletes target
# explicit existing ids (computed against the live DB now), so they can never
# over-delete; regenerate after the base seeds change.
#
# DB note: every introspection query below is a constant (no value is ever
# concatenated into a query). Resolution + spatial matching happen in Python.
# The generated .sql is a script artifact (no runtime parameter channel), so it
# legitimately emits literal values; string literals are quote-doubled.
import collections
import glob
import json
import math
import os
import re
import subprocess
import sys

WORK = os.environ.get("ENB_IMPORT_WORK", "/tmp/enb-import-captures")
PG_CONTAINER = os.environ.get("ENB_PG_CONTAINER", "freya-postgres-1")
HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", "..", "..", ".."))
OUT_DIR = os.path.join(REPO, "db", "postgres", "capture_import")
WRAPPER = os.path.join(REPO, "db", "postgres", "seed_captures.sql")

SYNTH_BASE = 1_000_000          # synthetic sector_object_id: above max existing (~200528) + Phase Y
MOB_SYNTH_BASE = 900_000        # synthetic mob_base.mob_id: above max real (~2114), below SYNTH_BASE
REPLACE_RADIUS = 5000.0         # "within 5k" replacement radius
DEFAULT_RES_LEVEL = 1


def pg(sql):
    # \x1e record sep + \x1f field sep so text columns containing embedded
    # newlines (e.g. mob_base.ai notes) do not split a row across lines.
    out = subprocess.run(
        ["docker", "exec", PG_CONTAINER, "psql", "-U", "net7", "-d", "net7",
         "-tA", "-R", "\x1e", "-F", "\x1f", "-c", sql],
        capture_output=True, text=True, check=True).stdout
    out = out.rstrip("\n")   # psql appends one trailing newline to the whole result
    return [rec.split("\x1f") for rec in out.split("\x1e") if rec] if out else []


def norm(s):
    """Alphanumeric-normalize a name (jsonl uses curly apostrophes / accents the
    wire names render as bare ASCII)."""
    return re.sub(r"[^a-z0-9]", "", (s or "").lower())


def load_nav_jsonl(norm2id):
    """sector_id -> {norm(nav name)} from docs/sectors/json/<Sector>.jsonl -- THE
    authoritative source of which nav belongs to which sector. A nav not listed
    here does not exist in that sector and must not be imported into it."""
    NAV_TYPES = {"nav-point", "hidden-nav-point", "special-nav-point"}
    idx = collections.defaultdict(set)
    jdir = os.path.join(REPO, "docs", "sectors", "json")
    for path in sorted(glob.glob(os.path.join(jdir, "*.jsonl"))):
        sid = norm2id.get(norm(os.path.basename(path)[:-6]))
        if sid is None:
            continue
        with open(path) as fh:
            for line in fh:
                line = line.strip()
                if not line:
                    continue
                o = json.loads(line)
                if o.get("type") in NAV_TYPES and o.get("name"):
                    idx[sid].add(norm(o["name"]))
    return idx


def sql_str(s):
    return "'" + str(s).replace("'", "''") + "'"


def num(v):
    return repr(float(v))


def dist(a, b):
    return math.dist((a[0], a[1], a[2]), (b[0], b[1], b[2]))


class DB:
    """Snapshot of the live DB needed to resolve templates + find replacements.
    All loads are constant queries."""

    def __init__(self):
        self.valid = {int(r[0]) for r in pg("SELECT sector_id FROM sectors")}

        # Authoritative nav membership: which nav names belong to which sector,
        # straight from docs/sectors/json/*.jsonl. A captured nav whose name is not
        # listed for the sector it was bucketed into does not belong there and is
        # NOT imported. aggregate.py already resolves nav sectors from this same
        # source; this is the defensive gate that makes the constraint impossible
        # to bypass at SQL-generation time.
        norm2id = {norm(r[1]): int(r[0]) for r in pg("SELECT sector_id, name FROM sectors") if r[1]}
        self.nav_jsonl = load_nav_jsonl(norm2id)

        # full mob_base snapshot, in column order, so a captured mob with no
        # name match can be resolved by CLONING the nearest-level same-asset
        # template (a complete, valid row -- mob_base has no hull/shield columns;
        # those derive from level + modifiers, all of which the clone inherits).
        self.mob_cols = [r[0] for r in pg(
            "SELECT column_name FROM information_schema.columns "
            "WHERE table_name = 'mob_base' ORDER BY ordinal_position")]
        self.mob_text_cols = {r[0] for r in pg(
            "SELECT column_name FROM information_schema.columns "
            "WHERE table_name = 'mob_base' AND data_type = 'text'")}
        collist = ", ".join(self.mob_cols)
        self.mob_full = {}                                   # mob_id -> {col: value-or-None}
        self.mob_by_name = collections.defaultdict(list)     # lower(name) -> [(mob_id, level, asset)]
        self.mob_by_asset = collections.defaultdict(list)    # asset -> [(mob_id, level)]
        self.mob_asset = {}                                  # mob_id -> asset
        for row in pg(f"SELECT {collist} FROM mob_base "
                      f"WHERE mob_id < {MOB_SYNTH_BASE}"):
            r = {c: (v if v != "" else None) for c, v in zip(self.mob_cols, row)}
            mid = int(r["mob_id"]); lvl = int(r["level"] or 0)
            asset = int(r["base_asset_id"] or 0)
            self.mob_full[mid] = r
            self.mob_asset[mid] = asset
            self.mob_by_asset[asset].append((mid, lvl))
            if r["name"]:
                self.mob_by_name[r["name"].strip().lower()].append((mid, lvl, asset))

        # synthesized clone templates, deduped across sectors by (name, asset, level)
        self.synth_key = {}                                  # (lname, asset, level) -> mob_id
        self.synth_rows = []                                 # [{col: value}] to emit
        self._next_mob = MOB_SYNTH_BASE

        # ore-type sets per harvestable container id
        rt = collections.defaultdict(set)
        for group_id, otype in pg(
                "SELECT group_id, type FROM sector_objects_harvestable_restypes"):
            rt[int(group_id)].add(int(otype))

        # template sets per mob spawn container -> existing asset per spawn
        sg = collections.defaultdict(set)
        for spawn_group_id, mob_id in pg(
                "SELECT spawn_group_id, mob_id FROM mob_spawn_group"):
            a = self.mob_asset.get(int(mob_id))
            if a is not None:
                sg[int(spawn_group_id)].add(a)

        # harvestable level per resource_id (for level derivation)
        hlevel = {}
        for rid, lvl in pg(
                "SELECT resource_id, level FROM sector_objects_harvestable"):
            hlevel[int(rid)] = int(lvl or 0)

        # existing objects, bucketed per sector. Mobs/resources carry the set of
        # specific types present (mob = base_asset_id, resource = ore type) so
        # the 5k replacement only ever targets the SAME physical object.
        self.nav_names = collections.defaultdict(set)        # sector -> {lower(name)}
        self.exist_mobs = collections.defaultdict(list)      # sector -> [(id,x,y,z,assetset)]
        self.exist_res = collections.defaultdict(list)       # sector -> [(id,x,y,z,oreset)]
        self.level_sec = collections.defaultdict(collections.Counter)
        self.level_glob = collections.defaultdict(collections.Counter)
        for sid, sec, typ, name, x, y, z in pg(
                "SELECT sector_object_id, sector_id, type, name, "
                "position_x, position_y, position_z FROM sector_objects"):
            if not sec or not typ or x == "" or y == "" or z == "":
                continue
            sid = int(sid); sec = int(sec); typ = int(typ)
            if sid >= SYNTH_BASE:
                continue   # our own prior import -- not "existing data" to replace
            pos = (float(x), float(y), float(z))
            if typ == 37 and name:
                self.nav_names[sec].add(name.strip().lower())
            elif typ in (0, 1, 42):
                self.exist_mobs[sec].append((sid, *pos, sg.get(sid, set())))
            elif typ == 38:
                ores = rt.get(sid, set())
                self.exist_res[sec].append((sid, *pos, ores))
                lvl = hlevel.get(sid)
                if lvl:
                    for ore in ores:
                        self.level_sec[(sec, ore)][lvl] += 1
                        self.level_glob[ore][lvl] += 1

    def _synth_template(self, name, asset, level):
        """Clone the nearest-level same-asset mob_base row into a new synthetic
        template carrying the captured name/level/asset. Returns its mob_id, or
        None if the asset has no sibling to clone. Deduped across sectors."""
        cands = self.mob_by_asset.get(asset)
        if not cands:
            return None
        lname = name.strip().lower()
        key = (lname, asset, level or 0)
        if key in self.synth_key:
            return self.synth_key[key]
        src_id = min(cands, key=lambda c: abs(c[1] - (level or 0)))[0]
        row = dict(self.mob_full[src_id])
        new_id = self._next_mob; self._next_mob += 1
        row["mob_id"] = new_id
        row["name"] = name
        row["level"] = level if level is not None else row["level"]
        row["base_asset_id"] = asset
        self.synth_key[key] = new_id
        self.synth_rows.append(row)
        return new_id

    def resolve_mob(self, name, asset, level):
        """Captured mob -> a valid mob_base template id. Exact name match first
        (disambiguated by asset then nearest level); otherwise SYNTHESIZE a clone
        from the nearest-level same-asset template so the captured name+level are
        preserved and the spawn is always fillable. None only if there is no
        same-asset sibling to clone (never observed in the capture corpus)."""
        if name:
            cands = self.mob_by_name.get(name.strip().lower())
            if cands:
                if len(cands) == 1:
                    return cands[0][0]
                by_asset = [c for c in cands if asset is not None and c[2] == asset]
                pool = by_asset or cands
                if level is not None:
                    pool = sorted(pool, key=lambda c: abs(c[1] - level))
                return pool[0][0]
        if asset is None:
            return None
        return self._synth_template(name or "Mob", int(asset), level)

    def template_asset(self, mob_id, captured):
        """Model asset id for a RESOLVED template -- the key the 5k mob
        replacement must use. A capture can record a different model id for a
        creature than our `mob_base` does (observed: captured "Scuttle Larva"
        asset 1050 vs mob_base asset 1180), and `resolve_mob` matches the
        template by NAME, so keying the replacement on the raw captured asset
        misses the co-located existing spawn of the SAME creature. Real
        templates carry their `mob_base` asset; synthesized clones (>=
        MOB_SYNTH_BASE, absent from `mob_asset`) carry the captured asset they
        were built from."""
        a = self.mob_asset.get(int(mob_id))
        return a if a is not None else captured

    def res_level(self, sec, ore):
        # Captures do not carry a resource level, so derive it: the most common
        # level among existing same-ore harvestables in this sector, else
        # globally. Tie-break DETERMINISTICALLY (most frequent, then lowest level)
        # -- Counter.most_common breaks ties by insertion order, i.e. by DB row
        # order, which Postgres does not guarantee, so it would vary run-to-run
        # and churn the generated SQL on every regen.
        h = self.level_sec.get((sec, ore)) or self.level_glob.get(ore)
        if not h:
            return DEFAULT_RES_LEVEL
        top = max(h.values())
        return min(lvl for lvl, cnt in h.items() if cnt == top)


_REPLACE_RE = re.compile(
    r"DELETE FROM sector_objects WHERE sector_object_id IN \(([\d,\s]+)\)")


def assert_pristine_base():
    """Fail loud if regenerating against a NON-pristine DB.

    The 5k replace blocks delete base-seed rows the import itself removes, so
    they can only be regenerated against a DB that still HAS those base rows --
    i.e. base + Phase Y seeds with seed_captures NOT yet applied. If gen runs
    against a DB where seed_captures already ran (the base dups are gone), the
    replace queries find nothing and the committed replace blocks SILENTLY
    vanish, resurrecting the duplicates forever. We catch that here: read the
    currently-committed per-sector files (about to be overwritten), collect their
    replace-target ids, and confirm those base rows still exist. If any are
    already gone, the DB is non-pristine -- refuse rather than lose data."""
    committed = set()
    for f in glob.glob(os.path.join(OUT_DIR, "[0-9]*.sql")):
        with open(f) as fh:
            for m in _REPLACE_RE.finditer(fh.read()):
                committed |= {int(x) for x in m.group(1).replace(" ", "").split(",") if x}
    if not committed:
        return   # no prior replace blocks to protect (first-ever gen, or none)
    have = {int(r[0]) for r in pg(
        f"SELECT sector_object_id FROM sector_objects WHERE sector_object_id < {SYNTH_BASE}")}
    missing = sorted(committed - have)
    if missing:
        sys.exit(
            "gen_sql: REFUSING -- DB is NOT pristine. "
            f"{len(missing)} committed replace-target base row(s) are already "
            f"deleted (e.g. {missing[:5]}). This means seed_captures.sql has been "
            "applied to this DB, so the 5k replace blocks would regenerate EMPTY "
            "and the base-seed duplicates would silently come back.\n"
            "  Fix: bring up a pristine base DB (seed_captures skipped) --\n"
            "    docker compose down -v\n"
            "    ENB_SKIP_CAPTURE_SEED=1 docker compose up -d schema-init\n"
            "  then re-run this skill, then apply with import.sh --apply.")


def near_ids(captured_pts, existing):
    """Existing ids within REPLACE_RADIUS of a captured point whose specific type
    (template/ore `key`) the existing object also has. captured_pts:
    [(x,y,z, key)], existing: [(id,x,y,z, keyset)]."""
    hit = set()
    for sid, ex, ey, ez, exset in existing:
        for cx, cy, cz, key in captured_pts:
            if key in exset and dist((cx, cy, cz), (ex, ey, ez)) <= REPLACE_RADIUS:
                hit.add(sid)
                break
    return hit


# ---- SQL emit helpers -------------------------------------------------------

def emit_sector_object(oid, typ, x, y, z, name, radar, rrange, sec):
    return (
        "INSERT INTO sector_objects (sector_object_id, base_asset_id, h, s, v, "
        "type, scale, position_x, position_y, position_z, orientation_u, "
        "orientation_v, orientation_w, orientation_z, name, appears_in_radar, "
        "radar_range, sector_id, gate_to, sound_effect_id, sound_effect_range) "
        f"VALUES ({oid}, 0, 0.0, 0.0, 0.0, {typ}, 1.0, {num(x)}, {num(y)}, "
        f"{num(z)}, 0.0, 0.0, 0.0, 0.0, {sql_str(name)}, {radar}, {num(rrange)}, "
        f"{sec}, NULL, NULL, NULL) ON CONFLICT (sector_object_id) DO NOTHING;")


def emit_nav_points(oid, nav_type, signature, sec, expl=3000.0):
    return (
        "INSERT INTO sector_nav_points (sector_object_id, nav_type, signature, "
        "is_huge, sector_id, base_xp, exploration_range, object_radius_patch) "
        f"VALUES ({oid}, {nav_type}, {num(signature)}, 0, {sec}, 0, {num(expl)}, "
        "NULL) ON CONFLICT (sector_object_id) DO NOTHING;")


def emit_mob_base(db, row):
    """A full mob_base INSERT from a cloned row dict (text cols quoted, NULL
    preserved, numerics emitted verbatim)."""
    vals = []
    for c in db.mob_cols:
        v = row.get(c)
        if c in db.mob_text_cols:
            vals.append(sql_str(v if v is not None else ""))   # text cols are NOT NULL
        elif v is None:
            vals.append("NULL")
        else:
            vals.append(str(v))
    cols = ", ".join(db.mob_cols)
    return (f"INSERT INTO mob_base ({cols}) VALUES ({', '.join(vals)}) "
            "ON CONFLICT (mob_id) DO NOTHING;")


def write_purge():
    """Emit a GLOBAL purge of every prior capture-import synthetic row, run ONCE
    before any per-sector inserts. The per-sector files each delete only their OWN
    sector's synth rows, which is enough for a single-file re-apply but NOT for a
    full re-apply over a DIFFERENT prior mapping: synthetic sector_object_ids are
    assigned in generation order, so when the object set changes every id shifts.
    An old row squatting on an id the new mapping now hands to an earlier-applied
    sector blocks that sector's insert (ON CONFLICT DO NOTHING), and is then
    deleted by its own (later) sector file -- silently dropping rows. Purging the
    whole synth range up front makes a full re-apply a clean replace regardless of
    the prior mapping. On a fresh DB it deletes nothing (harmless)."""
    lines = [
        "-- _purge.sql -- GENERATED by .claude/skills/import-captures.",
        "-- Remove ALL prior capture-import synthetic rows so a full re-apply over",
        "-- a different prior id mapping is a clean replace, not a silent drop.",
        "-- Runs FIRST (before _mob_templates.sql and the per-sector files).",
        "-- Children before parents; keyed directly on the synthetic id range.",
        "BEGIN;",
        f"DELETE FROM mob_spawn_group WHERE spawn_group_id >= {SYNTH_BASE};",
        f"DELETE FROM sector_objects_mob WHERE mob_id >= {SYNTH_BASE};",
        f"DELETE FROM sector_objects_harvestable_restypes WHERE group_id >= {SYNTH_BASE};",
        f"DELETE FROM sector_objects_harvestable_oretypes WHERE resource_id >= {SYNTH_BASE};",
        f"DELETE FROM sector_objects_harvestable WHERE resource_id >= {SYNTH_BASE};",
        f"DELETE FROM sector_nav_points WHERE sector_object_id >= {SYNTH_BASE};",
        f"DELETE FROM sector_objects WHERE sector_object_id >= {SYNTH_BASE};",
        "COMMIT;",
        "",
    ]
    fn = "_purge.sql"
    with open(os.path.join(OUT_DIR, fn), "w") as fh:
        fh.write("\n".join(lines))
    return fn


def write_mob_templates(db):
    """Emit the synthetic clone templates (global, used across sectors) to their
    own file, idempotent via a delete-our-own-synth-rows prelude. Returns the
    filename, or None if nothing was synthesized."""
    if not db.synth_rows:
        return None
    lines = [
        "-- _mob_templates.sql -- GENERATED by .claude/skills/import-captures.",
        "-- Synthetic mob_base templates for captured mobs whose exact name is",
        "-- not in mob_base. Each is a clone of the nearest-level same-asset",
        "-- template carrying the captured name/level/asset, so the captured mob",
        "-- spawns with the right model + valid stats. Included FIRST so the",
        "-- per-sector spawns can reference them.",
        "BEGIN;",
        f"DELETE FROM mob_base WHERE mob_id >= {MOB_SYNTH_BASE};",
    ]
    for row in db.synth_rows:
        lines.append(emit_mob_base(db, row))
    lines.append("COMMIT;")
    lines.append("")
    fn = "_mob_templates.sql"
    with open(os.path.join(OUT_DIR, fn), "w") as fh:
        fh.write("\n".join(lines))
    return fn


def main():
    db = DB()
    sfiles = sorted(glob.glob(os.path.join(WORK, "sectors", "*.json")),
                    key=lambda p: int(os.path.splitext(os.path.basename(p))[0]))
    if not sfiles:
        sys.exit(f"gen_sql: no per-sector JSON in {WORK}/sectors -- run aggregate.py first")

    os.makedirs(OUT_DIR, exist_ok=True)
    assert_pristine_base()   # refuse to regen against a captures-applied DB
    for f in glob.glob(os.path.join(OUT_DIR, "*.sql")):
        os.remove(f)

    next_id = SYNTH_BASE
    sector_includes = []
    report = []

    for path in sfiles:
        with open(path) as fh:
            s = json.load(fh)
        sec = s["sector_id"]
        if sec not in db.valid:
            report.append(f"sector {sec}: NOT a valid DB sector -- skipped entirely")
            continue
        name = s.get("sector_name", str(sec))

        body = []
        replace_ids = set()
        inserts_obj, inserts_child = [], []
        n_nav = n_nav_skip = n_nav_notjsonl = n_res = n_mob = n_mob_skip = 0
        unresolved = collections.Counter()

        # ---- MOBS ----------------------------------------------------------
        # Replacement is keyed by base_asset_id (the physical model at the spot),
        # not the template id: a captured mob replaces any existing mob of the
        # same asset within 5k -- the same rule shape as resources (ore type).
        # The key is the RESOLVED template's asset, not the raw captured asset:
        # see template_asset() (capture/mob_base model-id disagreement otherwise
        # leaves a co-located base spawn of the same creature un-replaced).
        mob_pts = []   # (x,y,z, asset) for replacement matching
        mob_rows = []  # (oid, template, rec)
        for m in s["mobs"]:
            tmpl = db.resolve_mob(m.get("name"), m.get("baseAsset"), m.get("level"))
            if tmpl is None:
                n_mob_skip += 1
                unresolved[m.get("name") or "<noname>"] += 1
                continue
            oid = next_id; next_id += 1
            mob_rows.append((oid, tmpl, m))
            ta = db.template_asset(tmpl, m.get("baseAsset"))
            if ta is not None:
                mob_pts.append((m["x"], m["y"], m["z"], int(ta)))
            n_mob += 1
        replace_ids |= near_ids(mob_pts, db.exist_mobs[sec])
        for oid, tmpl, m in mob_rows:
            inserts_obj.append(emit_sector_object(
                oid, 0, m["x"], m["y"], m["z"], m.get("name") or "Mob", 0, 5000.0, sec))
            inserts_child.append(
                "INSERT INTO sector_objects_mob (mob_id, mob_count, "
                "mob_spawn_radius, respawn_time, delayed_spawn, group_aggro) "
                f"VALUES ({oid}, 1, 0, 90, 0, 0) ON CONFLICT DO NOTHING;")
            inserts_child.append(
                "INSERT INTO mob_spawn_group (id, spawn_group_id, mob_id, "
                f"group_index) VALUES ({oid}, {oid}, {tmpl}, 0) "
                "ON CONFLICT (id) DO NOTHING;")
            inserts_child.append(emit_nav_points(oid, 0, 1000.0, sec))

        # ---- RESOURCES -----------------------------------------------------
        res_pts = []
        res_rows = []
        for r in s["resources"]:
            ore = r.get("baseAsset")
            if ore is None:
                continue
            oid = next_id; next_id += 1
            res_rows.append((oid, int(ore), r))
            res_pts.append((r["x"], r["y"], r["z"], int(ore)))
            n_res += 1
        replace_ids |= near_ids(res_pts, db.exist_res[sec])
        for oid, ore, r in res_rows:
            lvl = db.res_level(sec, ore)
            inserts_obj.append(emit_sector_object(
                oid, 38, r["x"], r["y"], r["z"],
                r.get("name") or "Resource", 0, 5000.0, sec))
            inserts_child.append(
                "INSERT INTO sector_objects_harvestable (resource_id, level, "
                "field, res_count, spawn_radius, pop_rock_chance, "
                "max_field_radius, respawn_timer) "
                f"VALUES ({oid}, {lvl}, 0, 1, 0.0, 1.0, 0.0, 10) "
                "ON CONFLICT DO NOTHING;")
            inserts_child.append(
                "INSERT INTO sector_objects_harvestable_restypes (id, group_id, "
                f"type) VALUES ({oid}, {oid}, {ore}) ON CONFLICT (id) DO NOTHING;")

        # ---- NAVS (insert-if-absent by name) -------------------------------
        for nv in s["navs"]:
            nm = (nv.get("name") or "").strip()
            if not nm:
                continue
            if norm(nm) not in db.nav_jsonl.get(sec, set()):
                # Not in this sector's authoritative jsonl -> it does not belong
                # here. Should never fire (aggregate already enforced it); counted
                # so a regression is loud rather than silent.
                n_nav_notjsonl += 1
                continue
            if nm.lower() in db.nav_names[sec]:
                n_nav_skip += 1
                continue
            db.nav_names[sec].add(nm.lower())  # avoid intra-run dup
            oid = next_id; next_id += 1
            sig = nv.get("signature") or 20000
            radar = 1 if nv.get("onRadar") else 0
            inserts_obj.append(emit_sector_object(
                oid, 37, nv["x"], nv["y"], nv["z"], nm, radar, float(sig), sec))
            inserts_child.append(
                emit_nav_points(oid, nv.get("navType") or 0, float(sig), sec))
            n_nav += 1

        # ---- assemble per-sector file --------------------------------------
        body.append(f"-- capture import for sector {sec} ({name}) -- GENERATED.")
        body.append("-- Accurate captured data; takes priority over current data.")
        body.append("BEGIN;")
        body.append("")
        body.append("-- 1. drop our own prior synthetic rows for this sector (idempotent re-apply)")
        body.append(f"DELETE FROM mob_spawn_group WHERE spawn_group_id IN (SELECT sector_object_id FROM sector_objects WHERE sector_id = {sec} AND sector_object_id >= {SYNTH_BASE});")
        body.append(f"DELETE FROM sector_objects_mob WHERE mob_id IN (SELECT sector_object_id FROM sector_objects WHERE sector_id = {sec} AND sector_object_id >= {SYNTH_BASE});")
        body.append(f"DELETE FROM sector_objects_harvestable_restypes WHERE group_id IN (SELECT sector_object_id FROM sector_objects WHERE sector_id = {sec} AND sector_object_id >= {SYNTH_BASE});")
        body.append(f"DELETE FROM sector_objects_harvestable_oretypes WHERE resource_id IN (SELECT sector_object_id FROM sector_objects WHERE sector_id = {sec} AND sector_object_id >= {SYNTH_BASE});")
        body.append(f"DELETE FROM sector_objects_harvestable WHERE resource_id IN (SELECT sector_object_id FROM sector_objects WHERE sector_id = {sec} AND sector_object_id >= {SYNTH_BASE});")
        body.append(f"DELETE FROM sector_nav_points WHERE sector_object_id IN (SELECT sector_object_id FROM sector_objects WHERE sector_id = {sec} AND sector_object_id >= {SYNTH_BASE});")
        body.append(f"DELETE FROM sector_objects WHERE sector_id = {sec} AND sector_object_id >= {SYNTH_BASE};")
        body.append("")
        if replace_ids:
            ids = ", ".join(str(i) for i in sorted(replace_ids))
            body.append("-- 2. capture-priority replace: remove existing mobs/resources within "
                        f"{int(REPLACE_RADIUS)} units of a captured object of the same specific type")
            body.append(f"DELETE FROM mob_spawn_group WHERE spawn_group_id IN ({ids});")
            body.append(f"DELETE FROM sector_objects_mob WHERE mob_id IN ({ids});")
            body.append(f"DELETE FROM sector_objects_harvestable_restypes WHERE group_id IN ({ids});")
            body.append(f"DELETE FROM sector_objects_harvestable_oretypes WHERE resource_id IN ({ids});")
            body.append(f"DELETE FROM sector_objects_harvestable WHERE resource_id IN ({ids});")
            body.append(f"DELETE FROM sector_nav_points WHERE sector_object_id IN ({ids});")
            body.append(f"DELETE FROM sector_objects WHERE sector_object_id IN ({ids});")
            body.append("")
        body.append("-- 3. inserts (parents first, then child rows)")
        body.extend(inserts_obj)
        body.extend(inserts_child)
        body.append("")
        body.append("COMMIT;")
        body.append("")

        if not (inserts_obj or replace_ids):
            report.append(f"sector {sec:5d} {name:<16} nothing to import")
            continue

        safe = "".join(c if c.isalnum() else "_" for c in name)
        fn = f"{sec}_{safe}.sql"
        with open(os.path.join(OUT_DIR, fn), "w") as fh:
            fh.write("\n".join(body))
        sector_includes.append(f"\\ir capture_import/{fn}")

        report.append(
            f"sector {sec:5d} {name:<16} navs+{n_nav} (skip {n_nav_skip} exist"
            f"{f', {n_nav_notjsonl} not-in-jsonl' if n_nav_notjsonl else ''}) "
            f"res+{n_res} mob+{n_mob} (skip {n_mob_skip} unresolved) "
            f"replace {len(replace_ids)} existing"
            + (f"  STATIONS={len(s['stations'])} GATES={len(s['gates'])} "
               f"PLANETS={len(s['planets'])} (report-only)"
               if (s['stations'] or s['gates'] or s['planets']) else ""))
        for nm, c in unresolved.most_common():
            report.append(f"        unresolved mob: {nm} x{c}")

    purge_fn = write_purge()
    templates_fn = write_mob_templates(db)

    wrapper_lines = [
        "-- seed_captures.sql -- GENERATED by .claude/skills/import-captures.",
        "-- Imports accurate captured object data (resources, mobs, navs) that",
        "-- takes priority over the current emulator data. Per-sector files do",
        "-- the work; this wrapper includes them. Regenerate with the skill;",
        "-- do not hand-edit. See the skill SKILL.md for the policy.",
        "",
        "-- global purge of any prior synthetic rows (clean re-apply, see _purge.sql)",
        f"\\ir capture_import/{purge_fn}",
        "",
    ]
    if templates_fn:
        wrapper_lines.append("-- synthetic mob_base clone templates (must load before the spawns)")
        wrapper_lines.append(f"\\ir capture_import/{templates_fn}")
        wrapper_lines.append("")
    wrapper_lines.extend(sector_includes)
    with open(WRAPPER, "w") as fh:
        fh.write("\n".join(wrapper_lines) + "\n")

    print("\n".join(report))
    print(f"\ngen_sql: {next_id - SYNTH_BASE} objects, "
          f"{len(db.synth_rows)} synthetic mob_base templates -> {OUT_DIR}")
    print(f"         wrapper -> {WRAPPER}")


if __name__ == "__main__":
    main()
