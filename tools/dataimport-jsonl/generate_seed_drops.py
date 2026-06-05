#!/usr/bin/env python3
"""Generate Postgres seed SQL for Phase Y8 (mob loot/drop table) from the JSONL
reconstruct dataset, plus a CSV of the links that cannot be imported.

What this imports
-----------------
The reconstruct dataset records, by NAME, which mob drops which item -- in two
places that we union:

  * mobs/mobs.jsonl       -> each mob's `drops[].item`
  * items/item_drops.jsonl-> each item's `dropSources[].mob`

Each (mob, item) name pair is a drop link. We resolve both ends against the
live runtime (`mob_base.name`, `item_base.name`, normalized) and import ONLY
the links that:

  1. resolve on BOTH ends to runtime ids, AND
  2. are absent from `mob_items` for every (mob_id, item_id) id-combination of
     that name pair (so we never duplicate an existing drop, and never add an
     asymmetric variant the runtime deliberately omitted).

A mob name that maps to several `mob_base` rows is a combat-level variant of
the same creature; the drop applies to every variant (minus ones already
present). Every gap item name resolves to exactly one `item_base` id in this
dataset (measured: 0 same-name tiers), so item resolution is unambiguous.

Generated fields (NOT retail values -- see CLAUDE.md content-fidelity note)
---------------------------------------------------------------------------
The reconstruct dataset carries `dropRatesAvailable: false` everywhere: it
knows the link exists but never the rate. The four `mob_items` payload columns
are therefore GENERATED, grounded in the runtime distribution of the existing
8450 rows so synthetic rows are shape-identical to the most common real ones:

  * drop_chance  = 10.0  -- a common real value, just below the runtime mean
                            (~13); the loader truncates it to unsigned char, so
                            10.0 stores as the integer 10. A single documented
                            constant is deliberately chosen over a fabricated
                            per-row formula: there is no rarity signal in the
                            source, and false precision is worse than honest
                            uniformity. Trivially re-tunable here.
  * usage_chance = 0      -- modal runtime value.
  * type         = 1      -- modal runtime value (unused by the server at
                            runtime; matched for data shape only).
  * qty          = 1      -- modal runtime value.

These rates are placeholders approved for generation by the project owner; they
are reversible (every synth row lives at id >= SYNTH_ID_FLOOR and the seed
DELETEs that range before re-inserting) and clearly labelled.

Excluded links
--------------
  * mob name in SUSPECT_NAMES ("unknown", "various", ...): the reconstruct
    source did not know the mob. These collide with real `mob_base` rows
    literally named "Unknown" (placeholder content), so importing them would
    fabricate drops onto unrelated rows. Dropped.
  * either end unresolved against the runtime: written to the CSV instead of
    the SQL -- these need new mob_base / item_base rows (Phase Y6/Y7), which
    this positional-only backfill cannot synthesize.

Output
------
  * db/postgres/seed_phase_y_drops.sql  -- idempotent; all mob_items.id values
    >= SYNTH_ID_FLOOR (100000); DELETEs that range first; applied after
    seed_phase_y_navs.sql by the docker-compose schema-init service.
  * tools/dataimport-jsonl/phase_y8_unresolvable_drops.csv -- the links that
    could not be imported, with the reason and whichever end DID resolve, for
    the owner to fill out.
"""
from __future__ import annotations

import argparse
import csv
import json
import os
import re
import subprocess
import sys
from collections import Counter, defaultdict
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
RECONSTRUCT_DB = os.environ.get("ENB_RECONSTRUCT_DB", "reconstruct-data")
DEFAULT_DB = os.path.join(RECONSTRUCT_DB, "db")
DEFAULT_OUT = REPO_ROOT / "db" / "postgres" / "seed_phase_y_drops.sql"
DEFAULT_CSV = Path(__file__).resolve().parent / "phase_y8_unresolvable_drops.csv"

DEFAULT_PG_HOST = "127.0.0.1"
DEFAULT_PG_PORT = "5434"
DEFAULT_PG_USER = "net7"
DEFAULT_PG_PASS = "net7"
DEFAULT_PG_DB = "net7"

SYNTH_ID_FLOOR = 100000

# Generated payload values (see module docstring).
GEN_DROP_CHANCE = 10.0
GEN_USAGE_CHANCE = 0
GEN_TYPE = 1
GEN_QTY = 1

# Reconstruct mob names that are placeholders, not real creatures. These also
# match `mob_base` rows literally named "Unknown", so they must be excluded.
SUSPECT_NAMES = re.compile(r"^(unknown|various|many|several|n/?a|none|tbd|\?+|misc|all|any)$", re.I)


def norm(s):
    return re.sub(r"[^a-z0-9]", "", (s or "").lower())


def run_psql(host, port, user, password, dbname, sql):
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


def jl(path):
    with open(path) as f:
        for line in f:
            line = line.strip()
            if not line:
                continue
            try:
                yield json.loads(line)
            except json.JSONDecodeError:
                pass


def main():
    ap = argparse.ArgumentParser(description="Generate Phase Y8 mob-drop seed SQL + unresolvable CSV.")
    ap.add_argument("--db", default=DEFAULT_DB, help="reconstruct db/ root")
    ap.add_argument("--out", default=str(DEFAULT_OUT))
    ap.add_argument("--csv", default=str(DEFAULT_CSV))
    ap.add_argument("--pg-host", default=os.environ.get("PGHOST", DEFAULT_PG_HOST))
    ap.add_argument("--pg-port", default=os.environ.get("PGPORT", DEFAULT_PG_PORT))
    ap.add_argument("--pg-user", default=os.environ.get("PGUSER", DEFAULT_PG_USER))
    ap.add_argument("--pg-password", default=os.environ.get("PGPASSWORD", DEFAULT_PG_PASS))
    ap.add_argument("--pg-db", default=os.environ.get("PGDATABASE", DEFAULT_PG_DB))
    args = ap.parse_args()

    pg = (args.pg_host, args.pg_port, args.pg_user, args.pg_password, args.pg_db)

    # --- runtime name -> id sets, and existing drop pairs ---
    mob_by_name = defaultdict(set)
    for mid, name in run_psql(*pg, "SELECT mob_id, name FROM mob_base;"):
        mob_by_name[norm(name)].add(int(mid))
    item_by_name = defaultdict(set)
    for iid, name in run_psql(*pg, "SELECT id, name FROM item_base;"):
        item_by_name[norm(name)].add(int(iid))
    existing = set()
    for mid, iid in run_psql(*pg, "SELECT mob_id, item_base_id FROM mob_items;"):
        existing.add((int(mid), int(iid)))

    # --- reconstruct drop links (mob name, item name) from both sources ---
    links = {}  # (norm_mob, norm_item) -> (raw_mob, raw_item)  -- first raw spelling wins
    for o in jl(f"{args.db}/mobs/mobs.jsonl"):
        mn = o.get("name")
        if not mn:
            continue
        for d in (o.get("drops") or []):
            it = d.get("item")
            if it:
                links.setdefault((norm(mn), norm(it)), (mn, it))
    for o in jl(f"{args.db}/items/item_drops.jsonl"):
        it = o.get("item")
        if not it:
            continue
        for ds in (o.get("dropSources") or []):
            mb = ds.get("mob")
            if mb:
                links.setdefault((norm(mb), norm(it)), (mb, it))

    # --- resolve & classify ---
    emit = []        # (mob_id, item_id, raw_mob, raw_item)
    emitted_pairs = set()
    unresolved = []  # (raw_mob, raw_item, reason, mob_ids, item_ids)
    suspect_skipped = present_skipped = 0
    multi_mob_links = 0

    for (nm, ni), (rmob, ritem) in sorted(links.items()):
        if SUSPECT_NAMES.match((rmob or "").strip()) or SUSPECT_NAMES.match((ritem or "").strip()):
            suspect_skipped += 1
            continue
        mres = mob_by_name.get(nm)
        ires = item_by_name.get(ni)
        if not mres and not ires:
            unresolved.append((rmob, ritem, "both_missing", "", ""))
            continue
        if not mres:
            unresolved.append((rmob, ritem, "mob_missing", "", ",".join(map(str, sorted(ires)))))
            continue
        if not ires:
            unresolved.append((rmob, ritem, "item_missing", ",".join(map(str, sorted(mres))), ""))
            continue
        # both ends resolve -- conservative: skip the whole link if ANY id combo
        # is already in mob_items (the runtime already knows this drop).
        if any((m, i) in existing for m in mres for i in ires):
            present_skipped += 1
            continue
        iid = min(ires)  # unambiguous: 0 same-name item tiers in the gap set
        if len(mres) > 1:
            multi_mob_links += 1
        for m in sorted(mres):
            if (m, iid) in existing or (m, iid) in emitted_pairs:
                continue
            emitted_pairs.add((m, iid))
            emit.append((m, iid, rmob, ritem))

    # --- emit SQL ---
    out = Path(args.out)
    out.parent.mkdir(parents=True, exist_ok=True)
    with out.open("w") as f:
        w = lambda s: f.write(s + "\n")
        w("-- Generated by tools/dataimport-jsonl/generate_seed_drops.py.")
        w("-- Do not edit by hand. Re-run the script to regenerate.")
        w(f"-- Source: {args.db}/mobs/mobs.jsonl + items/item_drops.jsonl")
        w("-- Phase Y8: mob loot/drop links absent from the runtime mob_items table,")
        w("-- where both the mob and the item resolve by name to a runtime id.")
        w("--")
        w("-- The payload columns (drop_chance/usage_chance/type/qty) are GENERATED")
        w("-- placeholders, NOT retail values: the reconstruct source records the")
        w("-- existence of each drop link but never its rate (dropRatesAvailable is")
        w("-- false throughout). Values are grounded in the runtime distribution -- see")
        w("-- the generator docstring. Every row's id is >= 100000 so it can never")
        w("-- collide with an authoritative dump and is trivially reversible.")
        w("-- All inserts use ON CONFLICT DO NOTHING so re-applying is safe.")
        w("")
        w("BEGIN;")
        w("")
        w("-- Idempotent cleanup of synth rows from any prior revision.")
        w(f"DELETE FROM mob_items WHERE id >= {SYNTH_ID_FLOOR};")
        w("")
        next_id = SYNTH_ID_FLOOR
        for mob_id, item_id, rmob, ritem in emit:
            comment = f"  -- {rmob} -> {ritem}".replace("\n", " ")
            w(
                "INSERT INTO mob_items (id, mob_id, item_base_id, usage_chance, drop_chance, type, qty) VALUES ("
                f"{next_id}, {mob_id}, {item_id}, {GEN_USAGE_CHANCE}, {GEN_DROP_CHANCE!r}, {GEN_TYPE}, {GEN_QTY}) "
                "ON CONFLICT (id) DO NOTHING;" + comment
            )
            next_id += 1
        w("")
        w("COMMIT;")

    # --- emit CSV of unresolvable links (dedup by reason + normalized names) ---
    csv_path = Path(args.csv)
    seen_csv = set()
    reason_counts = Counter()
    with csv_path.open("w", newline="") as cf:
        wr = csv.writer(cf)
        wr.writerow(["reason", "mob_name", "item_name", "resolved_mob_ids", "resolved_item_ids", "note"])
        NOTE = {
            "mob_missing": "mob name not in mob_base -- needs a Phase Y6 mob_base row",
            "item_missing": "item name not in item_base -- needs a Phase Y7 item_base row",
            "both_missing": "neither name resolves -- needs both mob_base and item_base rows",
        }
        for rmob, ritem, reason, mob_ids, item_ids in sorted(unresolved):
            key = (reason, norm(rmob), norm(ritem))
            if key in seen_csv:
                continue
            seen_csv.add(key)
            reason_counts[reason] += 1
            wr.writerow([reason, rmob, ritem, mob_ids, item_ids, NOTE[reason]])

    # --- summary ---
    e = lambda s: print(s, file=sys.stderr)
    e(f"wrote {out}")
    e(f"  reconstruct distinct drop links:        {len(links)}")
    e(f"  skipped (suspect mob/item name):        {suspect_skipped}")
    e(f"  skipped (already in mob_items):         {present_skipped}")
    e(f"  unresolvable (-> CSV):                  {sum(reason_counts.values())} {dict(reason_counts)}")
    e(f"  importable gap links w/ multi-id mob:   {multi_mob_links}")
    e(f"  NEW mob_items rows emitted:             {len(emit)} (id {SYNTH_ID_FLOOR}..{next_id-1})")
    e(f"wrote {csv_path} ({sum(reason_counts.values())} rows)")


if __name__ == "__main__":
    main()
