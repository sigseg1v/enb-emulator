#!/usr/bin/env python3
"""Quantify how much of the wiki-scraped reconstruct backup is ALREADY
present, authoritatively, in the live Postgres runtime tables.

Why this exists: Phase Y (`plans/25-phase-y-data-import.md`) lists
sector/mission/mob/drop/prospecting "imports". This script is the
evidence that those categories must NOT be injected into the runtime
tables:

  * The runtime tables are authoritative and densely populated
    (sector_objects, mob_base, missions all carry the real Net-7
    integer asset ids / faction ids / mission XML).
  * The wiki JSONL is a name-level near-duplicate of that data in an
    INCOMPATIBLE form: nav coordinates are the runtime coordinates
    divided by ~1000 (wiki "Sado Pit" (-152.74,-63.89) == runtime
    "Sado Pit" (-152740,-63860) in sector 1076), it carries no asset
    ids, no faction ids, no mission XML, and drop rates are mostly "?".

Injecting it would create wrong-position, asset-less duplicate objects
and non-runnable missions -- divergence the CLAUDE.md server-integrity
rules forbid (the escape hatch requires a PRIMARY source; a wiki scrape
is not one, and the Phase Y process gate says so explicitly).

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
import subprocess
import sys

DEFAULT_DB_DIR = "/data/dev/enb-emu-data-reconstruct-backup/db"


def psql_names(host, port, user, password, dbname, sql) -> set[str]:
    env = {**os.environ, "PGPASSWORD": password}
    cmd = ["psql", "-h", host, "-p", port, "-U", user, "-d", dbname, "-tA", "-c", sql]
    r = subprocess.run(cmd, env=env, capture_output=True, text=True, check=True)
    return {line.strip().lower() for line in r.stdout.splitlines() if line.strip()}


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
            out.add(v.strip().lower())
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
                wiki_navs.add(n.strip().lower())
    print(f"SECTORS    {len(sector_files)} wiki files, {len(wiki_navs)} distinct nav "
          f"names; already authoritative in sector_objects: {pct(len(wiki_navs & rt_navs), len(wiki_navs))}")

    wiki_mobs = distinct_names(os.path.join(d, "mobs/mobs.jsonl"), "name")
    print(f"MOBS       {len(wiki_mobs)} distinct wiki mob names; already in mob_base: "
          f"{pct(len(wiki_mobs & rt_mobs), len(wiki_mobs))}")

    wiki_miss = distinct_names(os.path.join(d, "missions/missions.jsonl"), "name")
    print(f"MISSIONS   {len(wiki_miss)} distinct wiki mission names; already in missions "
          f"(all of which carry XML): {pct(len(wiki_miss & rt_miss), len(wiki_miss))}")

    items, mobrefs, rows = set(), set(), 0
    for o in jsonl_iter(os.path.join(d, "items/item_drops.jsonl")):
        rows += 1
        if o.get("item"):
            items.add(o["item"].strip().lower())
        for ds in (o.get("dropSources") or []):
            m = ds.get("mob")
            if m and m.strip() not in ("?", ""):
                mobrefs.add(m.strip().lower())
    print(f"ITEM_DROPS {rows} rows, {len(items)} items, {len(mobrefs)} distinct mob refs; "
          f"mob refs resolvable to mob_base: {pct(len(mobrefs & rt_mobs), len(mobrefs))} "
          f"(runtime mob_items already holds the authoritative loot rows)")

    presources, prows = set(), 0
    for o in jsonl_iter(os.path.join(d, "prospecting/prospecting.jsonl")):
        prows += 1
        if o.get("resource"):
            presources.add(o["resource"].strip().lower())
    print(f"PROSPECT   {prows} rows, {len(presources)} distinct resources "
          f"(runtime sector_objects_harvestable holds the authoritative fields)")

    print("\nVerdict: the wiki scrape is a name-level near-duplicate of authoritative")
    print("runtime data in an incompatible form (no asset/faction ids, no XML, /1000")
    print("coords). It is NOT a primary source per CLAUDE.md and MUST NOT be injected")
    print("into the runtime tables. See plans/25-phase-y-data-import.md.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
