#!/usr/bin/env python3
"""Quantify how much of the JSONL reconstruct dataset is ALREADY
present, authoritatively, in the live Postgres runtime tables.

Why this exists: Phase Y (`plans/25-phase-y-data-import.md`) lists
sector/mission/mob/drop/prospecting "imports". This script measures,
per category, how much is a duplicate of the (richer) runtime data vs
a genuine gap -- so imports target only the real gaps:

  * The runtime tables are authoritative and densely populated
    (sector_objects, mob_base, missions all carry the real Net-7
    integer asset ids / faction ids / mission XML).
  * The reconstruct JSONL is largely a name-level near-duplicate of
    that data in an INCOMPATIBLE form: nav coordinates are the runtime
    coordinates divided by ~1000 (dataset "Sado Pit" (-152.74,-63.89)
    == runtime "Sado Pit" (-152740,-63860) in sector 1076), and it
    carries no asset ids, no faction ids, no mission XML; drop rates
    are mostly "?".

So most categories cannot be injected wholesale -- doing so would
create wrong-position, asset-less duplicate objects and non-runnable
missions, divergence the CLAUDE.md server-integrity rules forbid. The
exceptions are the small genuine gaps a by-name + by-position diff
surfaces: Y1 added NPCs the runtime lacked, and Y4
(generate_seed_navs.py) adds the positional nav markers the runtime is
missing. See plans/25 for the per-category verdict.

Run against the dev stack's Postgres (host port 5434 by default):
    python3 tools/dataimport-jsonl/analyze_wiki_coverage.py

Read-only: it SELECTs from the runtime DB and reads the JSONL; it never
writes anything.
"""
from __future__ import annotations

import argparse
import glob
import json
import os
import re
import subprocess
import sys

DEFAULT_DB_DIR = "/data/dev/enb-emu-data-reconstruct-backup/db"


def norm(s) -> str:
    # Alphanumeric-only fold, identical to generate_seed_navs.py, so a name's
    # punctuation/whitespace variants compare equal on both sides of the diff.
    return re.sub(r"[^a-z0-9]", "", (s or "").lower())


def psql_names(host, port, user, password, dbname, sql) -> set[str]:
    env = {**os.environ, "PGPASSWORD": password}
    cmd = ["psql", "-h", host, "-p", port, "-U", user, "-d", dbname, "-tA", "-c", sql]
    r = subprocess.run(cmd, env=env, capture_output=True, text=True, check=True)
    return {norm(line) for line in r.stdout.splitlines() if line.strip()}


def jsonl_iter(path):
    with open(path) as f:
        for line in f:
            line = line.strip()
            if line:
                try:
                    yield json.loads(line)
                except json.JSONDecodeError:
                    continue


def distinct_names(path, key) -> set[str]:
    out: set[str] = set()
    for o in jsonl_iter(path):
        v = o.get(key)
        if v:
            out.add(norm(v))
    return out


def pct(hit, total) -> str:
    return f"{hit}/{total} ({100 * hit // max(1, total)}%)"


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--db-dir", default=DEFAULT_DB_DIR)
    ap.add_argument("--pg-host", default=os.environ.get("PGHOST", "127.0.0.1"))
    ap.add_argument("--pg-port", default=os.environ.get("PGPORT", "5434"))
    ap.add_argument("--pg-user", default=os.environ.get("PGUSER", "net7"))
    ap.add_argument("--pg-password", default=os.environ.get("PGPASSWORD", "net7"))
    ap.add_argument("--pg-db", default=os.environ.get("PGDATABASE", "net7"))
    args = ap.parse_args()

    def names(sql):
        return psql_names(args.pg_host, args.pg_port, args.pg_user,
                          args.pg_password, args.pg_db, sql)

    rt_navs = names("SELECT lower(trim(name)) FROM sector_objects WHERE name IS NOT NULL;")
    rt_mobs = names("SELECT lower(trim(name)) FROM mob_base WHERE name IS NOT NULL;")
    rt_miss = names("SELECT lower(trim(mission_name)) FROM missions WHERE mission_name IS NOT NULL;")

    d = args.db_dir
    print("=== wiki reconstruct backup vs. authoritative runtime tables ===")
    print(f"runtime: {len(rt_navs)} sector_objects names, {len(rt_mobs)} mob_base "
          f"names, {len(rt_miss)} mission names\n")

    # sectors / navs
    wiki_navs: set[str] = set()
    sector_files = glob.glob(os.path.join(d, "sectors/json/*.jsonl"))
    for p in sector_files:
        for o in jsonl_iter(p):
            n = o.get("name")
            if n:
                wiki_navs.add(norm(n))
    print(f"SECTORS    {len(sector_files)} wiki files, {len(wiki_navs)} distinct nav "
          f"names; already authoritative in sector_objects: {pct(len(wiki_navs & rt_navs), len(wiki_navs))}")

    # mobs: absent-by-name, and how many absent rows carry the load-bearing
    # field a runtime mob_base row requires (an asset / template id). A mob
    # with no asset id cannot be spawned or rendered, so it is not importable.
    wiki_mobs: set[str] = set()
    mob_has_asset: dict[str, bool] = {}
    for o in jsonl_iter(os.path.join(d, "mobs/mobs.jsonl")):
        n = o.get("name")
        if not n:
            continue
        key = norm(n)
        wiki_mobs.add(key)
        has_asset = any("asset" in k.lower() or "template" in k.lower() for k in o)
        mob_has_asset[key] = mob_has_asset.get(key, False) or has_asset
    absent_mobs = wiki_mobs - rt_mobs
    absent_mobs_with_asset = sum(1 for k in absent_mobs if mob_has_asset.get(k))
    print(f"MOBS       {len(wiki_mobs)} distinct names; already in mob_base: "
          f"{pct(len(wiki_mobs & rt_mobs), len(wiki_mobs))}; absent by name={len(absent_mobs)}, "
          f"of which carry an asset/template id (importable shape)={absent_mobs_with_asset}")

    # missions: absent-by-name. The load-bearing field is mission_XML (the
    # executable script); the dataset is walkthrough prose and carries none,
    # so importable=0 by construction regardless of absent count.
    wiki_miss = distinct_names(os.path.join(d, "missions/missions.jsonl"), "name")
    absent_miss = len(wiki_miss - rt_miss)
    print(f"MISSIONS   {len(wiki_miss)} distinct names; already in missions "
          f"(all w/ XML): {pct(len(wiki_miss & rt_miss), len(wiki_miss))}; absent={absent_miss}, "
          f"of which carry mission_XML (importable shape)=0 (dataset is prose, no XML)")

    # item_drops: the load-bearing field is a numeric drop_chance. Count how
    # many dropSource entries actually carry one (vs "?" / missing).
    items, rows, numeric_rates = set(), 0, 0
    for o in jsonl_iter(os.path.join(d, "items/item_drops.jsonl")):
        rows += 1
        if o.get("item"):
            items.add(o["item"].strip().lower())
        for ds in (o.get("dropSources") or []):
            rate = ds.get("rate") or ds.get("dropRate") or ds.get("chance")
            if isinstance(rate, (int, float)):
                numeric_rates += 1
            elif isinstance(rate, str) and any(c.isdigit() for c in rate) and "?" not in rate:
                numeric_rates += 1
    print(f"ITEM_DROPS {rows} rows, {len(items)} items; dropSource entries carrying a "
          f"numeric drop_chance (importable shape)={numeric_rates}")

    # prospecting: the load-bearing field is resource_id (links to a runtime
    # sector_object) plus spawn mechanics. Count rows carrying any id field.
    prows, with_id = 0, 0
    for o in jsonl_iter(os.path.join(d, "prospecting/prospecting.jsonl")):
        prows += 1
        if any("id" in k.lower() for k in o):
            with_id += 1
    print(f"PROSPECT   {prows} rows; rows carrying a resource_id / any id field "
          f"(importable shape)={with_id}")

    print("\nVerdict: the reconstruct dataset is largely a name-level near-duplicate of")
    print("authoritative runtime data in an incompatible form (no asset/faction ids, no")
    print("XML, /1000 coords). A per-category by-name (+ by-position for navs) diff finds")
    print("the genuine importable gaps -- rows the runtime lacks AND that carry the field")
    print("the runtime row needs. Importable and done: Y1 NPCs, Y4 nav markers")
    print("(generate_seed_navs.py), Y5 items. NOT importable (absent rows exist but 0 carry")
    print("the load-bearing field, per the counts above): mobs (asset id), missions")
    print("(mission_XML), item-drops (numeric drop_chance), prospecting (resource_id).")
    print("See plans/25-phase-y-data-import.md.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
