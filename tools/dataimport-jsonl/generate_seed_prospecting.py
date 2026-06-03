#!/usr/bin/env python3
"""Generate Postgres seed SQL for Phase Y9 (prospecting / harvestable nodes)
from the JSONL reconstruct dataset.

What this imports
-----------------
The reconstruct `prospecting/prospecting.jsonl` rows say, per resource,
which navs (and with what probability / density) that resource is found
near, in a given sector. The runtime models a harvestable as a *type-38
sector_object* (an asteroid / resource field). The server's loader joins:

    sector_objects.sector_object_id = sector_objects_harvestable.resource_id

(see server/src/SectorContentSQL.cpp ParseSectorContent, AddFieldOreIDs,
AddResourceTypes). So a runnable harvestable node is:

  * one `sector_objects` row, type=38, positioned in a sector, with
    max_field_radius>0 and res_count>0 so the loader makes it an OT_FIELD;
  * one `sector_objects_harvestable` row keyed by resource_id == that
    sector_object_id (carrying level, field-type, res_count, and the
    field mechanics columns);
  * N `sector_objects_harvestable_oretypes` rows (resource_id == node id,
    additional_ore_item_id == ore item, frequency == apportioned weight)
    -- the ore items the field can yield;
  * M `sector_objects_harvestable_restypes` rows (group_id == node id,
    type == a rock base-asset id) -- the visible rock variants so the
    field renders and PopulateField has rocks to place.

Linkage (REAL, attested by the dataset + resolved by name against the
runtime)
-----------------------------------------------------------------------
  * resource NAME  -> ore item_id   via base_ore_list.name, falling back
    to item_base.name (case/punctuation-insensitive). 99% resolve.
  * (sector, nav) NAME -> a runtime sector_object (a type-37 nav-point in
    the same sector). 93% of (sector,nav,resource) triples resolve. The
    matched nav-point supplies the POSITION and sector_id for the new
    type-38 node -- the dataset says "this resource is found near this
    nav", and we place the field at that nav's coordinates. We prefer the
    dataset's `match=="exact"` nav rows over `fallback` rows.

Fabricated (NO dataset source -- grounded in real neighbouring rows, not
invented constants)
-----------------------------------------------------------------------
  * res_count: per the project intent "each node has 1..5 resource units",
    set to the count of distinct gap resources placed at the node, clamped
    to 1..5. (The runtime's own res_count is a rock count an order of
    magnitude larger; we deliberately use the small per-node unit count the
    intent calls for, distributed via oretype frequency.)
  * oretype frequency: the dataset per-nav prob/weight/density apportioned
    across the resources at that node and normalized to sum 1.0 -- higher
    prob/weight/density -> larger share. NOTE: the current server picks an
    ore uniformly at random (ItemBaseManager.cpp ~244: "randomly choose
    from ore choice - TODO: add frequency weightings") and existing
    oretype rows store frequency 0, so this value is INERT in today's
    runtime. It is written as a faithful, forward-compatible encoding of
    the dataset weights for when the weighting TODO lands; it does not
    change current behaviour.
  * field (field-type enum), spawn_radius, pop_rock_chance,
    max_field_radius, respawn_timer, base_asset_id: copied from the
    typical (modal field-type / median mechanics / modal base_asset) of
    EXISTING OT_FIELD harvestables in the SAME level band, with a global
    OT_FIELD fallback. max_field_radius is forced >0 so the loader treats
    the row as a field.
  * restypes rock-asset `type` set: copied from a representative existing
    node at the same level band (the modal rock-count node). These are
    visual base-asset ids the dataset does not carry.
  * orientation / h / s / v / scale / radar: copied from the matched
    nav-point's own sector_object row.

Determining the importable GAP
------------------------------
The runtime ships sector_objects_harvestable_oretypes rows for only ~82 of
its 882 nodes, so a (sector, resource-item) "already a node drop" dedup
flags very little -- almost the entire resolvable dataset reads as "gap".
That is an artefact of the runtime modelling most fields via per-sector
base_ore_list pools + restypes rock-assets rather than per-node oretypes;
it does NOT mean 600+ real fields are missing. We therefore dedup
conservatively at the FIELD level: a candidate (sector, nav) node is
dropped if the matched nav-point already coincides (within DEDUP_EPS game
units) with an EXISTING type-38 harvestable in that sector, OR if every
gap resource at that nav is already a known node-drop in that sector. What
survives is the residual, genuinely-absent set. The honest read on this
gap is printed at the end and recorded in the seed header.

Output
------
db/postgres/seed_phase_y_prospecting.sql -- self-contained, idempotent.
Y9 synth ids (sector_object_id, harvestable.resource_id == the node's
sector_object_id, restypes.group_id == that id, and restypes.id) are
allocated from PROSPECTING_ID_BASE (200000) to stay clear of the Y4 navs
that already occupy [100000, 100048) in the shared sector_objects table.
The seed DELETEs everything at/above SYNTH_ID_FLOOR (100000) first (child
rows before parents; sector_objects scoped to type=38 so Y4 navs survive)
and every INSERT is ON CONFLICT DO NOTHING. Applied after the other Phase
Y seeds by the docker-compose schema-init service. This script READS the
DB + JSONL and WRITES the .sql file only; it never executes the generated
SQL.
"""
from __future__ import annotations

import argparse
import json
import os
import re
import subprocess
import sys
from collections import Counter, defaultdict
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
DEFAULT_PROSPECTING = "/data/dev/enb-emu-data-reconstruct-backup/db/prospecting/prospecting.jsonl"
DEFAULT_OUT = REPO_ROOT / "db" / "postgres" / "seed_phase_y_prospecting.sql"

DEFAULT_PG_HOST = "127.0.0.1"
DEFAULT_PG_PORT = "5434"
DEFAULT_PG_USER = "net7"
DEFAULT_PG_PASS = "net7"
DEFAULT_PG_DB = "net7"

SYNTH_ID_FLOOR = 100_000
# sector_objects is shared: Phase Y4 navs already own type=37 ids
# [100000, 100048) allocated from SYNTH_ID_FLOOR. Y9 prospecting nodes are
# type=38 in the SAME table with the SAME single-column PK, so they MUST
# allocate from a disjoint band or ON CONFLICT DO NOTHING silently drops the
# overlapping rows (and mis-attaches harvestable children to Y4 navs). Keep
# read-filters and DELETEs at SYNTH_ID_FLOOR (they correctly span both bands);
# only the allocation base moves up.
PROSPECTING_ID_BASE = 200_000
DEDUP_EPS = 8000.0  # game units -- a candidate node coincident with an existing
                    # type-38 field within this radius is treated as already present.

# How a dataset density string scales the apportioned frequency weight,
# when present. Mirrors the dataset's own coarse density vocabulary.
DENSITY_WEIGHT = {
    "Rich": 4.0,
    "Good": 3.0,
    "Poor": 1.5,
    "VERY Poor": 1.0,
}


def norm(s: str | None) -> str:
    return re.sub(r"[^a-z0-9]", "", (s or "").lower())


def sql_str(s: str | None) -> str:
    if s is None:
        return "NULL"
    return "'" + s.replace("'", "''") + "'"


def fnum(v) -> str:
    return repr(float(v))


def inum(v) -> str:
    return str(int(v))


def run_psql(host, port, user, password, dbname, sql: str) -> list[list[str]]:
    """Shell out to psql -tA with a tab field separator. READ-ONLY: this
    generator only ever issues SELECTs."""
    env = dict(os.environ, PGPASSWORD=password)
    out = subprocess.check_output(
        ["psql", "-h", host, "-p", port, "-U", user, "-d", dbname,
         "-tA", "-F", "\t", "-c", sql],
        env=env, text=True,
    )
    rows = []
    for line in out.splitlines():
        if line.strip() == "":
            continue
        rows.append(line.split("\t"))
    return rows


def load_prospecting(path: str):
    with open(path) as fh:
        for line in fh:
            line = line.strip()
            if line:
                yield json.loads(line)


def main():
    ap = argparse.ArgumentParser(description="Generate Phase Y9 prospecting-node seed SQL.")
    ap.add_argument("--prospecting", default=DEFAULT_PROSPECTING)
    ap.add_argument("--out", default=str(DEFAULT_OUT))
    ap.add_argument("--pg-host", default=os.environ.get("PGHOST", DEFAULT_PG_HOST))
    ap.add_argument("--pg-port", default=os.environ.get("PGPORT", DEFAULT_PG_PORT))
    ap.add_argument("--pg-user", default=os.environ.get("PGUSER", DEFAULT_PG_USER))
    ap.add_argument("--pg-password", default=os.environ.get("PGPASSWORD", DEFAULT_PG_PASS))
    ap.add_argument("--pg-db", default=os.environ.get("PGDATABASE", DEFAULT_PG_DB))
    args = ap.parse_args()

    pg = (args.pg_host, args.pg_port, args.pg_user, args.pg_password, args.pg_db)

    # ---- resource NAME -> ore item_id -----------------------------------
    ore_by_name: dict[str, set[int]] = defaultdict(set)
    for iid, name in run_psql(*pg, "SELECT item_id, name FROM base_ore_list WHERE name IS NOT NULL;"):
        ore_by_name[norm(name)].add(int(iid))
    item_by_name: dict[str, set[int]] = defaultdict(set)
    for iid, name in run_psql(*pg, "SELECT id, name FROM item_base WHERE name IS NOT NULL;"):
        item_by_name[norm(name)].add(int(iid))

    def resolve_item(name: str) -> int | None:
        s = ore_by_name.get(norm(name))
        if s:
            return min(s)  # deterministic pick
        s = item_by_name.get(norm(name))
        if s:
            return min(s)
        return None

    # ---- (sector, nav) NAME -> runtime sector_object (position + sector) -
    # Keyed by (runtime_sector_name, normalized nav name) -> the lowest
    # matching sector_object id (deterministic) plus its full positional row.
    so_id_by_key: dict[tuple[str, str], int] = {}
    so_row: dict[int, dict] = {}
    so_names_by_sector: dict[str, set[str]] = defaultdict(set)
    sector_id_by_name: dict[str, int] = {}
    for row in run_psql(
        *pg,
        "SELECT o.sector_object_id, o.name, s.name, o.sector_id, "
        "coalesce(o.base_asset_id,0), coalesce(o.h,0), coalesce(o.s,0), coalesce(o.v,0), "
        "coalesce(o.scale,1), o.position_x, o.position_y, o.position_z, "
        "coalesce(o.orientation_u,0), coalesce(o.orientation_v,0), coalesce(o.orientation_w,0), coalesce(o.orientation_z,1), "
        "coalesce(o.appears_in_radar,0), coalesce(o.radar_range,0) "
        "FROM sector_objects o JOIN sectors s ON s.sector_id=o.sector_id "
        f"WHERE o.name IS NOT NULL AND o.name<>'' AND o.sector_object_id < {SYNTH_ID_FLOOR};",
    ):
        soid = int(row[0])
        nm, sec = row[1], row[2]
        sector_id_by_name[sec] = int(row[3])
        so_names_by_sector[sec].add(norm(nm))
        key = (sec, norm(nm))
        # keep the lowest id for determinism
        if key not in so_id_by_key or soid < so_id_by_key[key]:
            so_id_by_key[key] = soid
        try:
            so_row[soid] = {
                "base_asset_id": int(row[4]), "h": float(row[5]), "s": float(row[6]), "v": float(row[7]),
                "scale": float(row[8]),
                "x": float(row[9]), "y": float(row[10]), "z": float(row[11]),
                "ou": float(row[12]), "ov": float(row[13]), "ow": float(row[14]), "oz": float(row[15]),
                "radar": int(row[16]), "radar_range": float(row[17]),
            }
        except (TypeError, ValueError):
            so_row[soid] = None
    rt_sector_by_norm = {norm(s): s for s in sector_id_by_name}

    # ---- existing type-38 harvestable nodes (for field-level dedup) ------
    # sector_name -> [(x, y)] of existing field positions.
    existing_field_pos: dict[str, list[tuple[float, float]]] = defaultdict(list)
    for row in run_psql(
        *pg,
        "SELECT s.name, o.position_x, o.position_y "
        "FROM sector_objects_harvestable h "
        "JOIN sector_objects o ON o.sector_object_id=h.resource_id "
        "JOIN sectors s ON s.sector_id=o.sector_id "
        f"WHERE o.sector_object_id < {SYNTH_ID_FLOOR};",
    ):
        try:
            existing_field_pos[row[0]].append((float(row[1]), float(row[2])))
        except (TypeError, ValueError):
            pass

    # existing per-node ore drops: (sector_name, item_id) already produced
    existing_drops: set[tuple[str, int]] = set()
    for row in run_psql(
        *pg,
        "SELECT s.name, ot.additional_ore_item_id "
        "FROM sector_objects_harvestable_oretypes ot "
        "JOIN sector_objects o ON o.sector_object_id=ot.resource_id "
        "JOIN sectors s ON s.sector_id=o.sector_id "
        f"WHERE o.sector_object_id < {SYNTH_ID_FLOOR};",
    ):
        try:
            existing_drops.add((row[0], int(row[1])))
        except (TypeError, ValueError):
            pass

    # ---- per-level field mechanics profile (median/modal of real fields) -
    level_profile: dict[int, dict] = {}
    for row in run_psql(
        *pg,
        "SELECT h.level, "
        "mode() WITHIN GROUP (ORDER BY h.field), "
        "mode() WITHIN GROUP (ORDER BY o.base_asset_id), "
        "percentile_disc(0.5) WITHIN GROUP (ORDER BY h.spawn_radius), "
        "percentile_disc(0.5) WITHIN GROUP (ORDER BY h.pop_rock_chance), "
        "percentile_disc(0.5) WITHIN GROUP (ORDER BY h.max_field_radius), "
        "percentile_disc(0.5) WITHIN GROUP (ORDER BY h.respawn_timer) "
        "FROM sector_objects_harvestable h JOIN sector_objects o ON o.sector_object_id=h.resource_id "
        "WHERE h.max_field_radius>0 AND h.res_count>0 GROUP BY h.level;",
    ):
        lvl = int(row[0])
        level_profile[lvl] = {
            "field": int(row[1] or 0),
            "base_asset_id": int(row[2] or 0),
            "spawn_radius": float(row[3] or 0),
            "pop_rock_chance": float(row[4] or 0),
            "max_field_radius": float(row[5] or 0),
            "respawn_timer": int(row[6] or 0),
        }
    grow = run_psql(
        *pg,
        "SELECT mode() WITHIN GROUP (ORDER BY field), "
        "percentile_disc(0.5) WITHIN GROUP (ORDER BY spawn_radius), "
        "percentile_disc(0.5) WITHIN GROUP (ORDER BY pop_rock_chance), "
        "percentile_disc(0.5) WITHIN GROUP (ORDER BY max_field_radius), "
        "percentile_disc(0.5) WITHIN GROUP (ORDER BY respawn_timer) "
        "FROM sector_objects_harvestable WHERE max_field_radius>0 AND res_count>0;",
    )[0]
    global_profile = {
        "field": int(grow[0] or 0), "base_asset_id": 0,
        "spawn_radius": float(grow[1] or 0), "pop_rock_chance": float(grow[2] or 0),
        "max_field_radius": float(grow[3] or 7000), "respawn_timer": int(grow[4] or 5),
    }

    def profile_for(level: int) -> dict:
        p = dict(level_profile.get(level) or global_profile)
        if not p.get("max_field_radius"):
            p["max_field_radius"] = global_profile["max_field_radius"] or 7000.0
        return p

    # ---- representative restype rock-asset set per level -----------------
    # Pick, per level, the modal rock-count node and use that node's `type`
    # multiset as the rock-asset template for fabricated nodes at that level.
    restypes_raw: dict[int, dict[int, list[int]]] = defaultdict(lambda: defaultdict(list))
    for row in run_psql(
        *pg,
        "SELECT h.level, rt.group_id, rt.type "
        "FROM sector_objects_harvestable h "
        "JOIN sector_objects_harvestable_restypes rt ON rt.group_id=h.resource_id "
        "WHERE h.max_field_radius>0 AND h.res_count>0;",
    ):
        restypes_raw[int(row[0])][int(row[1])].append(int(row[2]))
    rock_template: dict[int, list[int]] = {}
    all_rock_types: list[int] = []
    for lvl, groups in restypes_raw.items():
        count_hist = Counter(len(v) for v in groups.values())
        modal_n = count_hist.most_common(1)[0][0]
        rep = next(v for v in groups.values() if len(v) == modal_n)
        rock_template[lvl] = sorted(rep)
        all_rock_types.extend(t for v in groups.values() for t in v)
    global_rock_template = [t for t, _ in Counter(all_rock_types).most_common(3)] or [0]

    def rocks_for(level: int) -> list[int]:
        return rock_template.get(level) or global_rock_template

    # ===================================================================
    # Build candidate nodes from the dataset.
    # Group resolvable (sector, nav) -> {item_id: best apportion weight},
    # preferring exact-match nav rows, deduping at the field level.
    # ===================================================================
    rows = list(load_prospecting(args.prospecting))

    # node key = (rt_sector_name, nav_soid). value = dict with level + items.
    node_items: dict[tuple[str, int], dict[int, float]] = defaultdict(dict)
    node_level: dict[tuple[str, int], int] = {}
    node_navname: dict[tuple[str, int], str] = {}
    node_exactness: dict[tuple[str, int], bool] = {}

    triples_total = 0
    triples_resolvable = 0
    unresolved_res: set[str] = set()
    unresolved_nav: set[tuple[str, str]] = set()
    resolved_res: set[str] = set()

    for o in rows:
        ds_sector = o.get("sector")
        rt_sector = rt_sector_by_norm.get(norm(ds_sector))
        item = resolve_item(o["resource"])
        level = int(o.get("level") or o.get("itemLevel") or 1)
        if item is None:
            unresolved_res.add(o["resource"])
        else:
            resolved_res.add(o["resource"])
        for nd in (o.get("navDistribution") or []):
            triples_total += 1
            nav = nd["nav"]
            navsoid = so_id_by_key.get((rt_sector, norm(nav))) if rt_sector else None
            if navsoid is None:
                unresolved_nav.add((ds_sector, nav))
            if rt_sector is None or navsoid is None or item is None:
                continue
            triples_resolvable += 1
            # apportion weight: prob (or weight) scaled by density
            prob = nd.get("prob")
            weight = nd.get("weight")
            base_w = float(prob) if prob else (float(weight) if weight else 1.0)
            dens_mult = DENSITY_WEIGHT.get(nd.get("density"), 1.0)
            w = base_w * dens_mult
            is_exact = (nd.get("match") == "exact")
            key = (rt_sector, navsoid)
            # Prefer exact-match nav rows: if we already have exact data for
            # this node and this row is fallback, only fill items not present.
            if key in node_exactness and node_exactness[key] and not is_exact:
                node_items[key].setdefault(item, w)
            else:
                cur = node_items[key].get(item, 0.0)
                node_items[key][item] = max(cur, w)
            node_exactness[key] = node_exactness.get(key, False) or is_exact
            node_navname[key] = nav
            # node level = min level seen (the lowest tier a node first appears)
            node_level[key] = min(node_level.get(key, level), level)

    # ---- field-level dedup + gap filtering -------------------------------
    PLACED: list[tuple[tuple[str, int], dict[int, float]]] = []
    dropped_existing_field = 0
    dropped_all_known = 0
    for key, items in node_items.items():
        rt_sector, navsoid = key
        row = so_row.get(navsoid)
        if not row:
            continue
        cx, cy = row["x"], row["y"]
        # coincident with an existing type-38 field? -> already present.
        if any((px - cx) ** 2 + (py - cy) ** 2 < DEDUP_EPS * DEDUP_EPS
               for px, py in existing_field_pos.get(rt_sector, [])):
            dropped_existing_field += 1
            continue
        # keep only items that are NOT already a known node-drop in sector
        gap_items = {iid: w for iid, w in items.items()
                     if (rt_sector, iid) not in existing_drops}
        if not gap_items:
            dropped_all_known += 1
            continue
        PLACED.append((key, gap_items))

    # ===================================================================
    # Emit SQL.
    # ===================================================================
    out = Path(args.out)
    out.parent.mkdir(parents=True, exist_ok=True)
    f = out.open("w")
    w = lambda s: f.write(s + "\n")

    w("-- Generated by tools/dataimport-jsonl/generate_seed_prospecting.py.")
    w("-- Do not edit by hand. Re-run the script to regenerate.")
    w(f"-- Source: {args.prospecting}")
    w("-- Phase Y9: prospecting / harvestable nodes attested by the reconstruct")
    w("-- dataset (named resource near a named nav, in a sector) that are absent")
    w("-- from the runtime. Each node is a type-38 sector_object placed at the")
    w("-- matched nav-point's coordinates, plus its harvestable + oretype +")
    w("-- restype rows. Linkage is by NAME (resource->item, sector+nav->object);")
    w("-- field mechanics + rock-asset ids are copied from same-level existing")
    w(f"-- fields (no dataset source). Synth ids >= {PROSPECTING_ID_BASE}, clear of the")
    w(f"-- Phase Y4 navs that start at {SYNTH_ID_FLOOR} in the shared sector_objects")
    w("-- table; child rows deleted before parents; every INSERT is ON")
    w("-- CONFLICT DO NOTHING (idempotent).")
    w("-- This file is applied by the schema-init service; it is NOT executed by")
    w("-- the generator.")
    w("")
    w("BEGIN;")
    w("")
    w("-- Idempotent cleanup of synth rows from any prior revision.")
    w("-- Children (oretypes/restypes/harvestable) before parent (sector_objects).")
    w(f"DELETE FROM sector_objects_harvestable_oretypes WHERE resource_id >= {SYNTH_ID_FLOOR};")
    w(f"DELETE FROM sector_objects_harvestable_restypes WHERE group_id   >= {SYNTH_ID_FLOOR};")
    w(f"DELETE FROM sector_objects_harvestable_restypes WHERE id         >= {SYNTH_ID_FLOOR};")
    w(f"DELETE FROM sector_objects_harvestable          WHERE resource_id >= {SYNTH_ID_FLOOR};")
    w(f"DELETE FROM sector_objects                      WHERE sector_object_id >= {SYNTH_ID_FLOOR} AND type = 38;")
    w("")

    next_node_id = PROSPECTING_ID_BASE
    next_restype_id = PROSPECTING_ID_BASE
    nodes_emitted = 0
    oretype_links = 0
    restype_rows = 0
    res_count_hist: Counter = Counter()
    by_sector: Counter = Counter()

    for (rt_sector, navsoid), gap_items in sorted(PLACED, key=lambda kv: (kv[0][0], kv[0][1])):
        prow = so_row[navsoid]
        sid = sector_id_by_name[rt_sector]
        level = node_level[(rt_sector, navsoid)]
        prof = profile_for(level)
        node_id = next_node_id
        next_node_id += 1

        # res_count: 1..5 resource UNITS per node (project intent). Keep only
        # the top res_count resources by apportioned weight so the oretype
        # list and the unit count stay consistent ("5x one item, or 2x one
        # and 3x other") -- a nav-point otherwise accumulates every resource
        # that ever mentions it across all level tiers, which would claim a
        # 5-unit field with dozens of ores. The kept resources' weights are
        # renormalized below.
        ranked = sorted(gap_items.items(), key=lambda kv: (-kv[1], kv[0]))
        res_count = max(1, min(5, len(ranked)))
        gap_items = dict(ranked[:res_count])
        res_count_hist[res_count] += 1
        by_sector[rt_sector] += 1

        navname = node_navname[(rt_sector, navsoid)]
        node_name = f"{navname} Resource Field"[:64]

        # --- sector_objects (type 38) -- position from the matched nav-point.
        w(
            "INSERT INTO sector_objects (sector_object_id, base_asset_id, h, s, v, type, scale, "
            "position_x, position_y, position_z, orientation_u, orientation_v, orientation_w, orientation_z, "
            "name, appears_in_radar, radar_range, sector_id, gate_to, sound_effect_id, sound_effect_range) VALUES ("
            f"{node_id}, {inum(prof['base_asset_id'])}, {fnum(prow['h'])}, {fnum(prow['s'])}, {fnum(prow['v'])}, "
            f"38, {fnum(prow['scale'])}, {fnum(prow['x'])}, {fnum(prow['y'])}, {fnum(prow['z'])}, "
            f"{fnum(prow['ou'])}, {fnum(prow['ov'])}, {fnum(prow['ow'])}, {fnum(prow['oz'])}, "
            f"{sql_str(node_name)}, {inum(prow['radar'])}, {fnum(prow['radar_range'])}, {sid}, NULL, NULL, NULL) "
            "ON CONFLICT (sector_object_id) DO NOTHING;"
        )
        # --- sector_objects_harvestable -- resource_id == node id.
        w(
            "INSERT INTO sector_objects_harvestable (resource_id, level, field, res_count, "
            "spawn_radius, pop_rock_chance, max_field_radius, respawn_timer) VALUES ("
            f"{node_id}, {inum(level)}, {inum(prof['field'])}, {inum(res_count)}, "
            f"{fnum(prof['spawn_radius'])}, {fnum(prof['pop_rock_chance'])}, "
            f"{fnum(prof['max_field_radius'])}, {inum(prof['respawn_timer'])}) "
            # sector_objects_harvestable has no unique constraint, so a column
            # conflict target won't bind -- bare DO NOTHING is a valid no-op
            # here (idempotency is via the DELETE above) and would start
            # guarding if a unique constraint on resource_id is ever added.
            "ON CONFLICT DO NOTHING;"
        )
        nodes_emitted += 1

        # --- oretypes -- one per gap resource, frequency = normalized weight.
        total_w = sum(gap_items.values()) or 1.0
        for iid in sorted(gap_items):
            freq = round(gap_items[iid] / total_w, 6)
            w(
                "INSERT INTO sector_objects_harvestable_oretypes (resource_id, additional_ore_item_id, frequency) "
                f"VALUES ({node_id}, {inum(iid)}, {fnum(freq)}) "
                "ON CONFLICT DO NOTHING;"
            )
            oretype_links += 1

        # --- restypes -- rock-asset visuals copied from same-level template.
        for rock_type in rocks_for(level):
            rid = next_restype_id
            next_restype_id += 1
            w(
                "INSERT INTO sector_objects_harvestable_restypes (id, group_id, type) "
                f"VALUES ({rid}, {node_id}, {inum(rock_type)}) "
                "ON CONFLICT (id) DO NOTHING;"
            )
            restype_rows += 1

    w("")
    w("COMMIT;")
    f.close()

    # ---- summary ----------------------------------------------------------
    e = lambda s: print(s, file=sys.stderr)
    e(f"wrote {out}")
    e(f"  dataset (sector,nav,resource) triples: {triples_total}")
    e(f"  fully name-resolvable triples (sector+nav->object AND resource->item): "
      f"{triples_resolvable} ({100*triples_resolvable/max(1,triples_total):.1f}%)")
    e(f"  resources resolved by name: {len(resolved_res)} / {len(resolved_res)+len(unresolved_res)} "
      f"(unresolved: {sorted(unresolved_res)})")
    e(f"  distinct (sector,nav) pairs that did NOT resolve to an object: {len(unresolved_nav)}")
    e(f"  candidate resolvable nodes (sector,nav): {len(node_items)}")
    e(f"    dropped -- coincident with existing field: {dropped_existing_field}")
    e(f"    dropped -- all resources already known node-drops: {dropped_all_known}")
    e(f"  NODES emitted (type-38 sector_objects + harvestable rows): {nodes_emitted} "
      f"(sector_object_id {PROSPECTING_ID_BASE}..{next_node_id-1})")
    e(f"  oretype links emitted: {oretype_links}")
    e(f"  restype rock-asset rows emitted: {restype_rows}")
    e(f"  res_count distribution (per node): {dict(sorted(res_count_hist.items()))}")
    e(f"  top sectors: {by_sector.most_common(10)}")


if __name__ == "__main__":
    main()
