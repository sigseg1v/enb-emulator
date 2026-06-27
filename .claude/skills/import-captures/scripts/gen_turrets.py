#!/usr/bin/env python3
# SPDX-License-Identifier: MIT
# Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
# License: LICENSES/Freya
#
# gen_turrets.py -- BASELINE guardian-turret seed. Rings every stargate (type 11)
# and starbase (type 12) in the galaxy with guardian turrets, modelled on the
# turrets seen in the accurate captures.
#
# Why this is baseline (not a mod, not capture import):
#   The captures show guardian turrets ringing gates and starbases -- "Gate
#   Guardian Turret" / "Starbase Guardian Turret", asset 14, level 66 -- but a
#   single-pass survey only catches the 1-3 turrets nearest the flight path, and
#   only in the handful of sectors we flew. The retail server placed a small ring
#   (2-4) around EVERY gate/starbase. This generator extrapolates that pattern to
#   the whole galaxy so the defensive ring exists everywhere, as baseline content.
#
# Placement, derived from the captures (see plans/54):
#   * The recurring exact captured offset has 3D distance 1428 with the turret
#     ~1400 ABOVE the gate (offset ~(-199, -199, +1400)); larger gates/starbases
#     showed turrets out to ~2000-2500. Captured bearings span the full circle
#     across different gates -- i.e. a ring, of which we caught one arc.
#   * We therefore place an evenly-spaced ring: N turrets at horizontal radius
#     RADIUS around the anchor, elevated +Z_OFFSET. 3D distance per turret is
#     sqrt(RADIUS^2 + Z_OFFSET^2) ~= 1980, squarely inside the captured 1428-2500
#     band. 3 per gate, 4 per (larger) starbase.
#
# Template: the captured turrets resolve to mob_base 573 "Gate Guardian Turret"
# (asset 14, level 66), a real baseline row -- so we do NOT synthesize a template.
# The per-spawn DISPLAY name lives on sector_objects.name (independent of the
# mob_base), so the same template backs both the "Gate" and "Starbase" turret
# names. Synthetic ids live in their OWN range (TURRET_BASE) clear of the capture
# import (1000000) and Phase Y, and the file deletes its own range first, so a
# re-apply is a clean replace.
import math
import os
import subprocess
import sys

PG_CONTAINER = os.environ.get("ENB_PG_CONTAINER", "freya-postgres-1")
HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", "..", "..", ".."))
OUT = os.path.join(REPO, "db", "postgres", "seed_turrets.sql")

TURRET_BASE = 2_000_000          # synthetic sector_object_id: clear of capture import (1000000)
TURRET_TEMPLATE = 573            # mob_base "Gate Guardian Turret" (asset 14, level 66)
GATE_TYPE = 11                   # stargate
STATION_TYPE = 12                # starbase
N_GATE = 3                       # turrets ringing a gate
N_STATION = 4                    # turrets ringing a (larger) starbase
RADIUS = 1400.0                  # horizontal ring radius
Z_OFFSET = 1400.0                # turrets sit above the anchor plane
PHASE = math.pi / 4              # 45deg ring phase so turrets avoid the axes


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
    # type 0 mob spawn; base_asset_id 0 (model comes from the mob_base template via
    # the spawn group). Mirrors the capture-import mob emission shape exactly.
    return (
        "INSERT INTO sector_objects (sector_object_id, base_asset_id, h, s, v, "
        "type, scale, position_x, position_y, position_z, orientation_u, "
        "orientation_v, orientation_w, orientation_z, name, appears_in_radar, "
        "radar_range, sector_id, gate_to, sound_effect_id, sound_effect_range) "
        f"VALUES ({oid}, 0, 0.0, 0.0, 0.0, 0, 1.0, {num(x)}, {num(y)}, {num(z)}, "
        f"0.0, 0.0, 0.0, 0.0, {sql_str(name)}, 0, {num(5000.0)}, {sec}, NULL, "
        "NULL, NULL) ON CONFLICT (sector_object_id) DO NOTHING;")


def main():
    # Verify the template exists, so we never seed spawns that reference a missing
    # mob_base (which spawns the NULL-data "Default" mob and crashes the sector
    # server -- see fix_orphan_mob_spawns.sql).
    if not pg(f"SELECT 1 FROM mob_base WHERE mob_id = {TURRET_TEMPLATE}"):
        sys.exit(f"gen_turrets: mob_base {TURRET_TEMPLATE} (turret template) missing -- aborting")

    anchors = []   # (sector_object_id, sector_id, type, name, x, y, z)
    for sid, sec, typ, name, x, y, z in pg(
            "SELECT sector_object_id, sector_id, type, name, position_x, "
            "position_y, position_z FROM sector_objects "
            f"WHERE type IN ({GATE_TYPE}, {STATION_TYPE}) AND sector_id IS NOT NULL "
            "AND position_x IS NOT NULL AND position_y IS NOT NULL "
            "AND position_z IS NOT NULL ORDER BY sector_object_id"):
        anchors.append((int(sid), int(sec), int(typ),
                        name or "", float(x), float(y), float(z)))

    parents, mobs, groups, navs = [], [], [], []
    oid = TURRET_BASE
    n_gate = n_station = 0
    for aid, sec, typ, aname, ax, ay, az in anchors:
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
            mobs.append(
                "INSERT INTO sector_objects_mob (mob_id, mob_count, "
                "mob_spawn_radius, respawn_time, delayed_spawn, group_aggro) "
                f"VALUES ({oid}, 1, 0, 90, 0, 0) ON CONFLICT DO NOTHING;")
            groups.append(
                "INSERT INTO mob_spawn_group (id, spawn_group_id, mob_id, "
                f"group_index) VALUES ({oid}, {oid}, {TURRET_TEMPLATE}, 0) "
                "ON CONFLICT (id) DO NOTHING;")
            navs.append(
                "INSERT INTO sector_nav_points (sector_object_id, nav_type, "
                "signature, is_huge, sector_id, base_xp, exploration_range, "
                f"object_radius_patch) VALUES ({oid}, 0, {num(1000.0)}, 0, {sec}, "
                "0, 0.0, NULL) ON CONFLICT (sector_object_id) DO NOTHING;")
            oid += 1

    total = oid - TURRET_BASE
    body = [
        "-- seed_turrets.sql -- GENERATED by .claude/skills/import-captures/scripts/gen_turrets.py.",
        "-- BASELINE guardian turrets ringing every stargate (type 11) and starbase",
        f"-- (type 12), modelled on the captured guardian-turret pattern. {total} turrets",
        f"-- around {n_gate} gates ({N_GATE} each) and {n_station} starbases ({N_STATION} each).",
        f"-- Template: mob_base {TURRET_TEMPLATE} (asset 14, level 66). Synthetic ids >= {TURRET_BASE}.",
        "-- Idempotent: deletes its own id range first, so a re-apply is a clean replace.",
        "BEGIN;",
        "",
        "-- drop our own prior turret rows (children before parents)",
        f"DELETE FROM mob_spawn_group WHERE spawn_group_id >= {TURRET_BASE};",
        f"DELETE FROM sector_objects_mob WHERE mob_id >= {TURRET_BASE};",
        f"DELETE FROM sector_nav_points WHERE sector_object_id >= {TURRET_BASE};",
        f"DELETE FROM sector_objects WHERE sector_object_id >= {TURRET_BASE};",
        "",
        "-- parents (sector_objects) then child rows",
    ]
    body += parents + [""] + mobs + [""] + groups + [""] + navs
    body += ["", "COMMIT;", ""]
    with open(OUT, "w") as fh:
        fh.write("\n".join(body))
    print(f"gen_turrets: {total} turrets ({n_gate} gates x{N_GATE}, "
          f"{n_station} starbases x{N_STATION}) -> {OUT}")


if __name__ == "__main__":
    main()
