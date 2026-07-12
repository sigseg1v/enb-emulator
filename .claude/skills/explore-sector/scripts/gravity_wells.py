#!/usr/bin/env python3
# SPDX-License-Identifier: MIT
# Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
# License: LICENSES/Freya
#
# gravity_wells.py -- the authoritative list of sectors that contain a Gravity
# Well, so the explore-sector skill can REFUSE to fly through them.
#
# Why a gravity well is disqualifying for screen-only exploration: the server
# treats it as an environmental object of type 41 (OT_GWELL). Flying into one
# yanks you out of warp -- server/src/PlayerClass.cpp:1806 "detect gravity well
# in warp" -> if (m_GWell != -1) TerminateWarp(); -- and degrades speed/handling
# (m_GravityField, AdjustAndSetSpeeds). Our whole method is warp-to-the-next-nav;
# a sector that terminates warp mid-flight and slows the ship is exactly where a
# blind screen-only run drifts off course and gets wrecked. So we skip the sector.
#
# Source of truth (game logic, NOT a guess): rows in `sector_objects` whose
# `type` column == 41. server/src/SectorContentSQL.cpp:378 maps type 41 ->
# AddNewObject(OT_GWELL). Coordinates below are the DB position / 1000, i.e. the
# same k-units the target panel and docs/sectors/json data use. radius_k is the
# object's radar_range / 1000 (the well's effective size).
#
# Keyed by the docs/sectors/json sector name (the name drive_lua.py resolves to).
# Sectors that carry a gravity well but have NO nav-data file -- so we never
# explore them anyway -- are listed in _UNMAPPED below for completeness.

# sector (jsonl name) -> list of wells [{name, x, y, z, radius_k}]
GRAVITY_WELLS = {
    "ABB": [
        {"name": "Gravity Well 01", "x": 25.393, "y": -9.048, "z": 0.0, "radius_k": 5.0},
    ],
    "ABG": [
        {"name": "Gravity Well 01", "x": 153.714, "y": -108.186, "z": 0.0, "radius_k": 5.0},
        {"name": "Gravity Well 02", "x": 135.534, "y": -114.872, "z": 0.0, "radius_k": 5.0},
        {"name": "Gravity Well 03", "x": 117.704, "y": -122.938, "z": 0.0, "radius_k": 5.0},
        {"name": "Gravit Well 04", "x": 100.723, "y": -136.947, "z": 0.0, "radius_k": 5.0},
        {"name": "Gravity Well 06", "x": 88.836, "y": -153.079, "z": 0.0, "radius_k": 5.0},
        {"name": "Gravity Well 06b", "x": 182.996, "y": -112.967, "z": 0.0, "radius_k": 5.0},
        {"name": "Gravity Well 07", "x": 210.251, "y": -124.211, "z": 0.0, "radius_k": 5.0},
        {"name": "Gravity Well 08", "x": 244.214, "y": -152.655, "z": 0.0, "radius_k": 5.0},
        {"name": "Gravity Well 09", "x": 284.032, "y": -234.794, "z": 0.0, "radius_k": 5.0},
        {"name": "Gravity Well 10", "x": 74.826, "y": -167.938, "z": 0.0, "radius_k": 5.0},
        {"name": "Gravity Well 11", "x": 62.94, "y": -184.495, "z": 0.0, "radius_k": 5.0},
        {"name": "Gravity Well 12", "x": 51.203, "y": -238.707, "z": 0.0, "radius_k": 5.0},
        {"name": "Gravity Well 13", "x": 145.117, "y": -278.62, "z": 0.0, "radius_k": 5.0},
    ],
    "Achenar": [
        {"name": "Gravity Well", "x": -16.49, "y": 2.174, "z": 0.0, "radius_k": 50.0},
    ],
    "AragothPrime": [
        {"name": "Gravity Well", "x": -237.26, "y": 194.649, "z": 0.0, "radius_k": 90.0},
    ],
    "BlackbeardsWake": [
        {"name": "controller grav well", "x": -25.392, "y": 203.18, "z": 0.0, "radius_k": 5.0},
    ],
    "Lagarto": [
        {"name": "GW 1", "x": -177.915, "y": 203.327, "z": 0.0, "radius_k": 150.0},
        {"name": "GW2", "x": -1.177, "y": 240.452, "z": 0.0, "radius_k": 60.0},
        {"name": "GW3", "x": 72.513, "y": 115.731, "z": 0.0, "radius_k": 70.0},
        {"name": "GW4", "x": 162.494, "y": 287.42, "z": 0.0, "radius_k": 80.0},
        {"name": "GW5", "x": 76.188, "y": 351.264, "z": 0.0, "radius_k": 40.0},
        {"name": "GW6", "x": 14.55, "y": 353.04, "z": 0.0, "radius_k": 40.0},
    ],
    "MarsGamma": [
        {"name": "GW1", "x": -19.308, "y": 46.387, "z": 0.0, "radius_k": 25.0},
    ],
    "Menorb": [
        {"name": "Tendrius GW", "x": 24.643, "y": -21.557, "z": 0.0, "radius_k": 5.0},
        {"name": "Skeleton GW", "x": -9.648, "y": 8.985, "z": 0.0, "radius_k": 5.0},
    ],
    "Paramis": [
        {"name": "Grav Well", "x": 23.2, "y": -136.267, "z": 0.0, "radius_k": 25.0},
    ],
}

# Sectors with a type-41 gravity well that have NO docs/sectors/json file, so the
# skill never explores them regardless (kept for provenance / completeness):
# Moto, Nebiros, Chandilar, Achenar(*), Sho, "Menorb Planet", "Test Sector".
# (*) Achenar is listed above too in case a nav file is added later.
_UNMAPPED = ("Moto", "Nebiros", "Chandilar", "Sho", "Menorb Planet", "Test Sector")

# The set of explorable sectors to REFUSE.
GRAVITY_WELL_SECTORS = frozenset(GRAVITY_WELLS)


def has_gravity_well(sector):
    """True if `sector` (a docs/sectors/json name) contains a Gravity Well."""
    return sector in GRAVITY_WELL_SECTORS


def wells(sector):
    return GRAVITY_WELLS.get(sector, [])


if __name__ == "__main__":
    import sys
    if len(sys.argv) > 1:
        s = sys.argv[1]
        if has_gravity_well(s):
            print(f"{s}: HAS GRAVITY WELL -- refuse to explore ({len(wells(s))} well(s))")
            for w in wells(s):
                print(f"    {w['name']:24} ({w['x']},{w['y']},{w['z']})  r={w['radius_k']}k")
            sys.exit(2)
        print(f"{s}: no gravity well -- safe to explore")
    else:
        for s in sorted(GRAVITY_WELL_SECTORS):
            print(f"{s:18} {len(wells(s))} well(s)")
