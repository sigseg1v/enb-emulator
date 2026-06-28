#!/usr/bin/env python3
# SPDX-License-Identifier: MIT
# Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
# License: LICENSES/Freya
#
# gen_turrets.py -- BASELINE guardian-turret seed. Rings every stargate (type 11)
# and starbase (type 12) in the galaxy with guardian turrets, modelled on the
# turrets seen in the accurate captures AND on the turrets already present in the
# base data.
#
# Why this is baseline (not a mod, not capture import):
#   The captures show guardian turrets ringing gates and starbases -- "Gate
#   Guardian Turret" / "Starbase Guardian Turret", asset 14 -- but a single-pass
#   survey only catches the 1-3 turrets nearest the flight path, and only in the
#   handful of sectors we flew. The base data carries ~129 real turrets too, but
#   only near some gates/starbases. The retail server placed a small ring around
#   EVERY gate/starbase. This generator FILLS THE GAPS: it rings every
#   gate/starbase that does NOT already have a turret nearby, so the defensive
#   ring exists everywhere, as baseline content.
#
# The turret row shape (matches the existing type-42 turrets in the base data,
# e.g. sector_object 1733 "Starbase Guardian Turret"):
#   * sector_objects.type = 42 (OT_MOB / stationary turret -- NOT a type-0 mob
#     SPAWN). The server's SectorContentSQL classifies type 42 into the turret
#     branch, which reads the MODEL directly from sector_objects.base_asset_id.
#   * base_asset_id = 14 (the guardian-turret model, shared by Gate/Starbase/
#     EarthCorps guardian turrets), scale = 1.
#   * one sector_nav_points row (nav_type 1, signature 20000, exploration_range
#     3000), exactly like the existing turrets.
#   * NO sector_objects_mob and NO mob_spawn_group rows -- a type-42 turret is
#     self-contained; it does not spawn from a template. (An earlier version of
#     this generator wrongly modelled turrets as type-0 mob spawns with
#     base_asset_id 0 -> they loaded as OT_MOBSPAWN with no model and were
#     invisible. The self-delete below still purges those stale rows.)
#
# Placement, derived from the captures:
#   * The recurring captured offset has 3D distance ~1428 with the turret ~1400
#     above the gate; larger gates/starbases showed turrets out to ~2000-2500.
#     Captured bearings span the full circle -- a ring, of which a fly-through
#     catches one arc. We place an evenly-spaced ring at horizontal radius RADIUS,
#     elevated +Z_OFFSET; 3D distance per turret ~= 1980, inside the 1428-2500
#     band. 3 per gate, 4 per (larger) starbase.
#
# Synthetic ids live in their OWN range (TURRET_BASE) clear of the capture import
# (1000000) and Phase Y, and the file deletes its own range first, so a re-apply
# is a clean replace.
import math
import os
import subprocess

PG_CONTAINER = os.environ.get("ENB_PG_CONTAINER", "freya-postgres-1")
HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", "..", "..", ".."))
OUT = os.path.join(REPO, "db", "postgres", "seed_turrets.sql")

TURRET_BASE = 2_000_000          # synthetic sector_object_id: clear of capture import (1000000)
TURRET_TYPE = 42                 # OT_MOB / stationary turret (server SectorContentSQL turret branch)
TURRET_ASSET = 14                # guardian-turret model (Gate/Starbase/EarthCorps turrets)
GATE_TYPE = 11                   # stargate
STATION_TYPE = 12                # starbase
N_GATE = 3                       # turrets ringing a gate
N_STATION = 4                    # turrets ringing a (larger) starbase
RADIUS = 1400.0                  # horizontal ring radius
Z_OFFSET = 1400.0                # turrets sit above the anchor plane
PHASE = math.pi / 4              # 45deg ring phase so turrets avoid the axes
DEDUP_RADIUS = 6000.0            # skip an anchor that already has a real turret this close


def pg(sql):
    out = subprocess.run(
        ["docker", "exec", PG_CONTAINER, "psql", "-U", "net7", "-d", "net7",
         "-tA", "-F", "\x1f", "-c", sql],
        capture_output=True, text=True, check=True).stdout
    return [line.split("\x1f") for line in out.splitlines() if line]


def sql_str(s):
    return "'" + str(s).replace("'", "''") + "'"


def num(v):
    return repr(float(v))


def emit_sector_object(oid, x, y, z, name, sec):
    # type 42 stationary turret; model comes from base_asset_id directly (the
    # server turret branch reads it). Matches the existing base-data turrets
    # (e.g. sector_object 1733): scale 1, appears_in_radar 0, radar_range 5000,
    # sound_effect_id -1.
    return (
        "INSERT INTO sector_objects (sector_object_id, base_asset_id, h, s, v, "
        "type, scale, position_x, position_y, position_z, orientation_u, "
        "orientation_v, orientation_w, orientation_z, name, appears_in_radar, "
        "radar_range, sector_id, gate_to, sound_effect_id, sound_effect_range) "
        f"VALUES ({oid}, {TURRET_ASSET}, 0.0, 0.0, 0.0, {TURRET_TYPE}, 1.0, "
        f"{num(x)}, {num(y)}, {num(z)}, 0.0, 0.0, 0.0, 0.0, {sql_str(name)}, 0, "
        f"{num(5000.0)}, {sec}, NULL, -1, 0) ON CONFLICT (sector_object_id) DO NOTHING;")


def emit_nav_point(oid, sec):
    # one nav point per turret, matching the existing turrets (nav_type 1,
    # signature 20000, exploration_range 3000).
    return (
        "INSERT INTO sector_nav_points (sector_object_id, nav_type, signature, "
        "is_huge, sector_id, base_xp, exploration_range, object_radius_patch) "
        f"VALUES ({oid}, 1, {num(20000.0)}, 0, {sec}, 0, {num(3000.0)}, NULL) "
        "ON CONFLICT (sector_object_id) DO NOTHING;")


def main():
    # anchors: every gate/starbase with a known position.
    anchors = []   # (sector_id, type, x, y, z)
    for sec, typ, x, y, z in pg(
            "SELECT sector_id, type, position_x, position_y, position_z "
            f"FROM sector_objects WHERE type IN ({GATE_TYPE}, {STATION_TYPE}) "
            "AND sector_id IS NOT NULL AND position_x IS NOT NULL "
            "AND position_y IS NOT NULL AND position_z IS NOT NULL "
            "ORDER BY sector_object_id"):
        anchors.append((int(sec), int(typ), float(x), float(y), float(z)))

    # existing REAL turrets (type 42, non-synth) per sector, for gap-fill dedup:
    # an anchor that already has a turret nearby keeps its authentic placement.
    existing = {}  # sector_id -> [(x,y,z)]
    for sec, x, y, z in pg(
            f"SELECT sector_id, position_x, position_y, position_z "
            f"FROM sector_objects WHERE type = {TURRET_TYPE} "
            f"AND sector_object_id < {TURRET_BASE} AND sector_id IS NOT NULL "
            "AND position_x IS NOT NULL AND position_y IS NOT NULL "
            "AND position_z IS NOT NULL"):
        existing.setdefault(int(sec), []).append((float(x), float(y), float(z)))

    def has_turret_near(sec, ax, ay, az):
        for (x, y, z) in existing.get(sec, ()):
            if math.dist((x, y, z), (ax, ay, az)) < DEDUP_RADIUS:
                return True
        return False

    parents, navs = [], []
    oid = TURRET_BASE
    n_gate = n_station = n_skip = 0
    for sec, typ, ax, ay, az in anchors:
        if has_turret_near(sec, ax, ay, az):
            n_skip += 1
            continue
        if typ == STATION_TYPE:
            n, label = N_STATION, "Starbase Guardian Turret"
            n_station += 1
        else:
            n, label = N_GATE, "Gate Guardian Turret"
            n_gate += 1
        for i in range(n):
            theta = PHASE + 2.0 * math.pi * i / n
            tx = ax + RADIUS * math.cos(theta)
            ty = ay + RADIUS * math.sin(theta)
            tz = az + Z_OFFSET
            parents.append(emit_sector_object(oid, tx, ty, tz, label, sec))
            navs.append(emit_nav_point(oid, sec))
            oid += 1

    total = oid - TURRET_BASE
    body = [
        "-- seed_turrets.sql -- GENERATED by .claude/skills/import-captures/scripts/gen_turrets.py.",
        "-- BASELINE guardian turrets ringing every stargate (type 11) and starbase",
        f"-- (type 12) that lacks one, modelled on the captured + base-data turrets.",
        f"-- {total} turrets around {n_gate} gates ({N_GATE} each) and {n_station}",
        f"-- starbases ({N_STATION} each); {n_skip} anchors skipped (already had a turret",
        f"-- within {int(DEDUP_RADIUS)}u). Type 42, model asset {TURRET_ASSET}. Synthetic ids >= {TURRET_BASE}.",
        "-- Idempotent: deletes its own id range first, so a re-apply is a clean replace.",
        "BEGIN;",
        "",
        "-- drop our own prior turret rows (children before parents). The mob/spawn",
        "-- deletes purge rows left by an earlier (wrong) version that modelled",
        "-- turrets as type-0 mob spawns; the current shape writes neither table.",
        f"DELETE FROM mob_spawn_group WHERE spawn_group_id >= {TURRET_BASE};",
        f"DELETE FROM sector_objects_mob WHERE mob_id >= {TURRET_BASE};",
        f"DELETE FROM sector_nav_points WHERE sector_object_id >= {TURRET_BASE};",
        f"DELETE FROM sector_objects WHERE sector_object_id >= {TURRET_BASE};",
        "",
        "-- parents (sector_objects) then nav points",
    ]
    body += parents + [""] + navs
    body += ["", "COMMIT;", ""]
    with open(OUT, "w") as fh:
        fh.write("\n".join(body))
    print(f"gen_turrets: {total} turrets ({n_gate} gates x{N_GATE}, "
          f"{n_station} starbases x{N_STATION}), {n_skip} anchors already had one -> {OUT}")


if __name__ == "__main__":
    main()
