#!/usr/bin/env python3
"""Generate Postgres seed SQL for Phase Y4 (sector navigation markers) from
the JSONL reconstruct dataset.

What this imports
-----------------
The reconstruct `sectors/json/<Sector>.jsonl` files list navigation markers
with a `type`, `name`, and `x`/`y`/`z`. This generator imports ONLY the two
marker classes that are fully self-describing from position alone:

  * "nav-point"        -> a radar-visible navigation marker
  * "hidden-nav-point" -> a marker revealed by scanning (not on radar)

It deliberately does NOT import gates, stations, planets, moons, or generic
objects from this dataset. Those rows are relational: a gate needs a
`gate_to` destination sector and a `sector_objects_stargates` row (faction,
class restriction, security); a station needs `sector_objects_starbases`
plus the whole interior (rooms/terminals/NPCs/vendors); a planet needs
`sector_objects_planets` orbit parameters. The reconstruct dataset carries
none of that, so importing them would create non-functional objects. The
runtime DB already ships those relational objects; this seed only fills the
gap in pure positional markers.

Coordinate frame
-----------------
The reconstruct coordinates are the runtime coordinates divided by ~1000
(verified by matching same-named navs across both datasets: e.g. Lagarto
"Anomaly" is (-500.82,-123.2) in the dataset and (-500822,-123204) in the
runtime). This generator multiplies x/y/z by 1000 to land in the runtime
frame. The dataset is rounded to 2 decimals and the scale factor wobbles
(1000-1002 across navs), so imported positions are APPROXIMATE -- good
enough for a navigation marker a player flies toward or scans for, not a
byte-accurate retail value.

Derived fields
--------------
A `sector_objects` + `sector_nav_points` pair needs ~15 fields the dataset
does not provide (type/base_asset_id/nav_type/signature/is_huge/base_xp/
exploration_range/radar_range/scale/orientation/hsv/object_radius_patch).
Rather than invent flat constants, each new marker copies the representative
profile of the SAME sector's existing navs of the same visibility class
(modal categoricals, median numerics), falling back to a global profile for
that class when the sector has no example. This grounds every derived field
in real neighbouring runtime data.

Dedup
-----
A dataset marker is dropped as already-present when, within its mapped
runtime sector, either (a) a runtime object has the same normalized name, or
(b) a runtime object lies within DEDUP_EPS game units of the x1000 position.
The position test catches navs the runtime stores under a spelling variant
(the dataset "corrects" the in-game spelling "InfinitiCorp" to "Infinity",
"Outer Debris Fiels" to "Field", etc.) so we do not duplicate them.

Output
------
db/postgres/seed_phase_y_navs.sql -- self-contained, idempotent. All
sector_object_id values live at/above SYNTH_ID_FLOOR (100000) so they can
never collide with an authoritative dump; the seed DELETEs that range first
and every INSERT is ON CONFLICT DO NOTHING. Applied after seed_phase_y.sql
by the docker-compose schema-init service.
"""
from __future__ import annotations

import argparse
import glob
import json
import os
import re
import statistics
import subprocess
import sys
from collections import Counter, defaultdict
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
DEFAULT_SECTORS_GLOB = "/data/dev/enb-emu-data-reconstruct-backup/db/sectors/json/*.jsonl"
DEFAULT_OUT = REPO_ROOT / "db" / "postgres" / "seed_phase_y_navs.sql"

DEFAULT_PG_HOST = "127.0.0.1"
DEFAULT_PG_PORT = "5434"
DEFAULT_PG_USER = "net7"
DEFAULT_PG_PASS = "net7"
DEFAULT_PG_DB = "net7"

SYNTH_ID_FLOOR = 100000
COORD_SCALE = 1000.0
DEDUP_EPS = 8000.0  # game units (~8 dataset units) -- conservative: errs toward "already present"

NAV_TYPES = {"nav-point", "hidden-nav-point"}

# Sector-file stems whose runtime sector name is not a plain de-camel of the
# stem. Resolved by inspecting the live `sectors` table. Anything not here is
# matched by de-camelcasing the stem; mappings are confidence-gated below.
SECTOR_ALIAS = {
    "ABA": "Asteroid Belt Alpha", "ABB": "Asteroid Belt Beta", "ABG": "Asteroid Belt Gamma",
    "MarsAlpha": "Asteroid Belt Alpha", "MarsBeta": "Asteroid Belt Beta", "MarsGamma": "Asteroid Belt Gamma",
    "Altair3": "Altair III", "Equatorial": "Equatorial Earth", "HighEarth": "High Earth",
    "Pluto": "Pluto and Charon", "Ceres": "Ceres/Thule", "Mazzaroth": "Mazzaroth Maelstrom",
    "Mondara": "Mondara Maelstrom", "Nifleheim": "Nifleheim Cloud", "Todesengel": "der Todesengel",
}

# Sector-file stems excluded outright. Planet*/Moon* surface files either
# duplicate an existing runtime planet-surface sector or have no runtime
# counterpart; FishBowl/MoonRisco have no runtime sector at all. Adding a
# whole sector is out of scope for a positional-marker backfill.
EXCLUDE_STEMS_PREFIX = ("Planet", "Moon")
EXCLUDE_STEMS = {"FishBowl"}


def norm(s: str | None) -> str:
    return re.sub(r"[^a-z0-9]", "", (s or "").lower())


def decamel(s: str) -> str:
    s = re.sub(r"(?<=[a-z])(?=[A-Z])", " ", s)
    s = re.sub(r"(?<=[A-Za-z])(?=[0-9])", " ", s)
    return s


def clean_name(s: str) -> str:
    # Normalize apostrophe artifacts to ASCII; collapse whitespace.
    s = s.replace("´", "'").replace("’", "'").replace("‘", "'")
    return re.sub(r"\s+", " ", s).strip()


def core_name(s: str | None) -> str:
    # Strip the runtime "[asset NNNN]" annotation and a leading
    # "Wreckage of the " / "Wreck of the " so a dataset nav named
    # "Teoyaomqui Maru" dedups against the runtime nav
    # "Wreckage of the Teoyaomqui Maru [asset 1268]" (same object). This is
    # equality after stripping, NOT substring -- "Above Hadean" stays distinct
    # from "Hadean", "Menorb's Back" from "Menorb".
    s = re.sub(r"\s*\[asset\s+\d+\]\s*$", "", s or "", flags=re.I)
    s = re.sub(r"^wreck(age)? of the ", "", s.strip(), flags=re.I)
    return s.strip()


def sql_str(s: str | None) -> str:
    if s is None:
        return "NULL"
    return "'" + s.replace("'", "''") + "'"


def run_psql(host, port, user, password, dbname, sql: str) -> list[list[str]]:
    env = dict(os.environ, PGPASSWORD=password)
    out = subprocess.check_output(
        ["psql", "-h", host, "-p", port, "-U", user, "-d", dbname, "-tA", "-F", "\t", "-c", sql],
        env=env, text=True,
    )
    rows = []
    for line in out.splitlines():
        if line.strip() == "":
            continue
        rows.append(line.split("\t"))
    return rows


def mode(vals):
    vals = [v for v in vals if v is not None]
    if not vals:
        return None
    return Counter(vals).most_common(1)[0][0]


def med(vals):
    vals = [v for v in vals if v is not None]
    if not vals:
        return None
    return statistics.median(vals)


class Profile:
    """Representative field set for a nav class, derived from real rows."""
    __slots__ = ("type", "base_asset_id", "nav_type", "is_huge", "appears_in_radar",
                 "orient", "radar_range", "scale", "h", "s", "v",
                 "signature", "base_xp", "exploration_range", "object_radius_patch", "n")

    def __init__(self, rows):
        self.n = len(rows)
        self.type = mode([r["type"] for r in rows])
        self.base_asset_id = mode([r["base_asset_id"] for r in rows])
        self.nav_type = mode([r["nav_type"] for r in rows])
        self.is_huge = mode([r["is_huge"] for r in rows])
        self.appears_in_radar = mode([r["appears_in_radar"] for r in rows])
        self.orient = mode([r["orient"] for r in rows]) or (0.0, 0.0, 0.0, 1.0)
        self.radar_range = med([r["radar_range"] for r in rows])
        self.scale = med([r["scale"] for r in rows])
        self.h = med([r["h"] for r in rows])
        self.s = med([r["s"] for r in rows])
        self.v = med([r["v"] for r in rows])
        self.signature = med([r["signature"] for r in rows])
        self.base_xp = med([r["base_xp"] for r in rows])
        self.exploration_range = med([r["exploration_range"] for r in rows])
        self.object_radius_patch = med([r["object_radius_patch"] for r in rows])


def load_profiles(rows_raw):
    """rows_raw: list of dicts keyed by sector_name + the nav fields.
    Returns (per_sector[sector_name][class] -> Profile, global[class] -> Profile)."""
    by_sector_class = defaultdict(lambda: defaultdict(list))
    global_class = defaultdict(list)
    for r in rows_raw:
        cls = "visible" if r["appears_in_radar"] == 1 else "hidden"
        by_sector_class[r["sector"]][cls].append(r)
        global_class[cls].append(r)
    per_sector = {}
    for sec, classes in by_sector_class.items():
        per_sector[sec] = {c: Profile(rs) for c, rs in classes.items()}
    glob = {c: Profile(rs) for c, rs in global_class.items()}
    return per_sector, glob


def main():
    ap = argparse.ArgumentParser(description="Generate Phase Y4 sector-nav seed SQL.")
    ap.add_argument("--sectors-glob", default=DEFAULT_SECTORS_GLOB)
    ap.add_argument("--out", default=str(DEFAULT_OUT))
    ap.add_argument("--pg-host", default=os.environ.get("PGHOST", DEFAULT_PG_HOST))
    ap.add_argument("--pg-port", default=os.environ.get("PGPORT", DEFAULT_PG_PORT))
    ap.add_argument("--pg-user", default=os.environ.get("PGUSER", DEFAULT_PG_USER))
    ap.add_argument("--pg-password", default=os.environ.get("PGPASSWORD", DEFAULT_PG_PASS))
    ap.add_argument("--pg-db", default=os.environ.get("PGDATABASE", DEFAULT_PG_DB))
    args = ap.parse_args()

    pg = (args.pg_host, args.pg_port, args.pg_user, args.pg_password, args.pg_db)

    # --- sector name -> id ---
    sector_id = {}
    for name, sid in run_psql(*pg, "select name, sector_id from sectors;"):
        sector_id[name] = int(sid)
    sid_by_norm = {norm(n): n for n in sector_id}

    # --- existing nav names + positions per sector (for dedup) ---
    rt_names = defaultdict(set)        # sector_name -> {normname}
    rt_pos = defaultdict(list)         # sector_name -> [(x,y)]
    for sname, oname, x, y in run_psql(
        *pg,
        "select s.name, o.name, o.position_x, o.position_y "
        "from sector_objects o join sectors s on s.sector_id=o.sector_id "
        f"where o.name is not null and o.name <> '' and o.sector_object_id < {SYNTH_ID_FLOOR};",
    ):
        rt_names[sname].add(norm(oname))
        rt_names[sname].add(norm(core_name(oname)))
        try:
            rt_pos[sname].append((float(x), float(y)))
        except (TypeError, ValueError):
            pass

    # --- per-sector nav profiles ---
    prof_rows = []
    for row in run_psql(
        *pg,
        "select s.name, o.type, o.base_asset_id, o.appears_in_radar, coalesce(o.radar_range,0), "
        "coalesce(o.scale,1), coalesce(o.h,0), coalesce(o.s,0), coalesce(o.v,0), "
        "coalesce(o.orientation_u,0), coalesce(o.orientation_v,0), coalesce(o.orientation_w,0), coalesce(o.orientation_z,1), "
        "coalesce(np.nav_type,0), coalesce(np.signature,0), coalesce(np.is_huge,0), "
        "coalesce(np.base_xp,0), coalesce(np.exploration_range,0), coalesce(np.object_radius_patch,0) "
        "from sector_nav_points np join sector_objects o on o.sector_object_id=np.sector_object_id "
        f"join sectors s on s.sector_id=o.sector_id where o.sector_object_id < {SYNTH_ID_FLOOR};",
    ):
        prof_rows.append({
            "sector": row[0], "type": int(row[1]) if row[1] else None,
            "base_asset_id": int(row[2]) if row[2] else None,
            "appears_in_radar": int(row[3]) if row[3] else 0,
            "radar_range": float(row[4]), "scale": float(row[5]),
            "h": float(row[6]), "s": float(row[7]), "v": float(row[8]),
            "orient": (float(row[9]), float(row[10]), float(row[11]), float(row[12])),
            "nav_type": int(row[13]), "signature": float(row[14]), "is_huge": int(row[15]),
            "base_xp": int(row[16]), "exploration_range": float(row[17]),
            "object_radius_patch": float(row[18]),
        })
    per_sector_prof, global_prof = load_profiles(prof_rows)

    # --- resolve sector-file stem -> runtime sector name (confidence-gated) ---
    def resolve_sector(stem, recnames):
        # An exact normalized stem<->sector-name match is certainly the right
        # sector; trust it unconditionally (low name-overlap then signals a
        # genuine gap, not a mismap). Only alias/de-camel matches are gated on
        # name overlap, since those could resolve to the wrong sector.
        if norm(stem) in sid_by_norm:
            return sid_by_norm[norm(stem)], 1.0
        cands = []
        if stem in SECTOR_ALIAS:
            cands.append(SECTOR_ALIAS[stem])
        cands.append(decamel(stem))
        best, best_ov = None, -1
        for c in cands:
            nk = norm(c)
            if nk in sid_by_norm:
                rt = sid_by_norm[nk]
                ov = len(recnames & rt_names.get(rt, set()))
                if ov > best_ov:
                    best, best_ov = rt, ov
        if best is None:
            return None, 0.0
        rate = best_ov / max(1, len(recnames))
        return best, rate

    new_navs = []      # (sector_name, sector_id, type_str, name, x, y, z)
    skipped_name = skipped_pos = 0
    excluded_stems = []
    lowconf_stems = []
    mapped_sectors = set()

    for path in sorted(glob.glob(args.sectors_glob)):
        stem = os.path.basename(path)[:-6]
        if stem.startswith(EXCLUDE_STEMS_PREFIX) or stem in EXCLUDE_STEMS:
            excluded_stems.append(stem)
            continue
        recs = [json.loads(l) for l in open(path)]
        recnames = {norm(r["name"]) for r in recs if r.get("name")}
        rt_sector, rate = resolve_sector(stem, recnames)
        if rt_sector is None:
            lowconf_stems.append((stem, None, 0.0))
            continue
        if rate < 0.30:
            lowconf_stems.append((stem, rt_sector, rate))
            continue
        mapped_sectors.add(stem)
        sid = sector_id[rt_sector]
        names_here = rt_names.get(rt_sector, set())
        pos_here = rt_pos.get(rt_sector, [])
        for r in recs:
            if r.get("type") not in NAV_TYPES:
                continue
            nm = r.get("name")
            if not nm or r.get("x") is None:
                continue
            if norm(nm) in names_here or norm(core_name(nm)) in names_here:
                skipped_name += 1
                continue
            cx, cy = r["x"] * COORD_SCALE, r["y"] * COORD_SCALE
            cz = (r.get("z") or 0.0) * COORD_SCALE
            if any((px - cx) ** 2 + (py - cy) ** 2 < DEDUP_EPS * DEDUP_EPS for px, py in pos_here):
                skipped_pos += 1
                continue
            new_navs.append((rt_sector, sid, r["type"], clean_name(nm), cx, cy, cz))

    # --- emit ---
    out = Path(args.out)
    out.parent.mkdir(parents=True, exist_ok=True)
    f = out.open("w")
    w = lambda s: f.write(s + "\n")

    w("-- Generated by tools/dataimport-jsonl/generate_seed_navs.py.")
    w("-- Do not edit by hand. Re-run the script to regenerate.")
    w(f"-- Source: {args.sectors_glob}")
    w("-- Phase Y4: sector navigation markers (nav-point / hidden-nav-point) that")
    w("-- are absent from the runtime DB by both name and position. Positions are")
    w("-- the dataset coordinates x1000 (runtime frame); see the module docstring")
    w("-- for the coordinate-frame and field-derivation notes. All ids >= 100000.")
    w("-- All inserts use ON CONFLICT DO NOTHING so re-applying is safe.")
    w("")
    w("BEGIN;")
    w("")
    w("-- Idempotent cleanup of synth rows from any prior revision. Child")
    w("-- (sector_nav_points) before parent (sector_objects).")
    w(f"DELETE FROM sector_nav_points WHERE sector_object_id >= {SYNTH_ID_FLOOR};")
    w(f"DELETE FROM sector_objects   WHERE sector_object_id >= {SYNTH_ID_FLOOR};")
    w("")

    def fnum(v, default):
        return repr(float(v)) if v is not None else repr(float(default))

    def inum(v, default):
        return str(int(v)) if v is not None else str(int(default))

    next_id = SYNTH_ID_FLOOR
    emitted = 0
    by_sector_count = Counter()
    for rt_sector, sid, type_str, name, x, y, z in new_navs:
        cls = "visible" if type_str == "nav-point" else "hidden"
        prof = (per_sector_prof.get(rt_sector, {}).get(cls)
                or global_prof.get(cls)
                or global_prof.get("visible"))
        oid = next_id
        next_id += 1
        emitted += 1
        by_sector_count[rt_sector] += 1
        ou, ov, ow, oz = prof.orient
        w(
            "INSERT INTO sector_objects (sector_object_id, base_asset_id, h, s, v, type, scale, "
            "position_x, position_y, position_z, orientation_u, orientation_v, orientation_w, orientation_z, "
            "name, appears_in_radar, radar_range, sector_id, gate_to, sound_effect_id, sound_effect_range) VALUES ("
            f"{oid}, {inum(prof.base_asset_id, 0)}, {fnum(prof.h, 0)}, {fnum(prof.s, 0)}, {fnum(prof.v, 0)}, "
            f"{inum(prof.type, 0)}, {fnum(prof.scale, 1)}, {repr(x)}, {repr(y)}, {repr(z)}, "
            f"{repr(ou)}, {repr(ov)}, {repr(ow)}, {repr(oz)}, {sql_str(name)}, "
            f"{inum(prof.appears_in_radar, 0)}, {fnum(prof.radar_range, 0)}, {sid}, NULL, NULL, NULL) "
            "ON CONFLICT (sector_object_id) DO NOTHING;"
        )
        w(
            "INSERT INTO sector_nav_points (sector_object_id, nav_type, signature, is_huge, sector_id, "
            "base_xp, exploration_range, object_radius_patch) VALUES ("
            f"{oid}, {inum(prof.nav_type, 0)}, {fnum(prof.signature, 0)}, {inum(prof.is_huge, 0)}, {sid}, "
            f"{inum(prof.base_xp, 0)}, {fnum(prof.exploration_range, 3000)}, {fnum(prof.object_radius_patch, 0)}) "
            "ON CONFLICT (sector_object_id) DO NOTHING;"
        )
    w("")
    w("COMMIT;")
    f.close()

    # --- summary ---
    e = lambda s: print(s, file=sys.stderr)
    e(f"wrote {out}")
    e(f"  sectors mapped (>=0.30 name overlap): {len(mapped_sectors)}")
    e(f"  excluded stems (Planet*/Moon*/no-counterpart): {len(excluded_stems)} -> {sorted(excluded_stems)}")
    if lowconf_stems:
        e(f"  low-confidence / unmapped stems: {len(lowconf_stems)} -> {[(s, round(r,2)) for s,_,r in lowconf_stems]}")
    e(f"  markers skipped (already present by name): {skipped_name}")
    e(f"  markers skipped (already present by position): {skipped_pos}")
    e(f"  NEW markers emitted: {emitted} (sector_object_id {SYNTH_ID_FLOOR}..{next_id-1})")
    e(f"  top sectors: {by_sector_count.most_common(10)}")


if __name__ == "__main__":
    main()
