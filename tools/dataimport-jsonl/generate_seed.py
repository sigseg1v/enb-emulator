#!/usr/bin/env python3
"""Generate Postgres seed SQL for Phase Y1 (NPCs) and Y5 (items) from the
wiki-scraped JSONL reconstruct backup.

Inputs:
  NPCS_PATH    -- one JSON object per line, wiki-shaped NPC records
  ITEMS_GLOB   -- cat<N>_lvl<L>.jsonl files; <N> is the category id and
                  <L> is the item level. Same itemId may appear in
                  multiple files (rough+fine category buckets).

Output:
  db/postgres/seed_phase_y.sql -- self-contained, idempotent via
  ON CONFLICT DO NOTHING on primary keys. Loads into the `net7`
  database AFTER schema.sql.

Honest limits this seed does NOT fix:
  * Per-NPC avatar templates (starbase_npc_avatar_templates) are NOT
    in the JSONL -- avatars render as default until that table is
    populated.
  * Item type-specific tables (item_beam, item_engine, item_device,
    item_shield, item_reactor, item_missile, item_ammo, item_projectile,
    item_refine, item_manufacture) get zero rows -- the JSONL stats
    blob doesn't map to their schema columns without a translation
    table we don't have.
  * 2d_asset / 3d_asset ids are placeholder zeros -- the JSONL
    imageLink path strings would need a separate asset registry.

Room placement (room_id, npc_index): resolved at generate time by
querying the live Postgres `starbases` + `starbase_rooms` tables.
Convention used here:
  * NPCs with `sellsItems` -> Bazaar (room_type=2), falling back to
    Main Lobby (1) then Hangar (0) if 2 doesn't exist for that starbase.
  * NPCs without `sellsItems` -> Lounge (room_type=3), falling back to
    Bazaar (2), Main Lobby (1), Hangar (0).
JSONL `station` strings are matched against `starbases.name`
case/punctuation-insensitive with a small alias table for known
mismatches ("Jove's Fury" -> "Joves Fury", "Yasuragi" -> "Yasuragi
City", etc.). NPCs whose station does not resolve to a starbase
(space-nav NPCs, ships, pre-launch locations) get room_id NULL and
are simply not rendered inside any starbase -- the load loop's LEFT
JOIN tolerates this.

Vendor inventories (starbase_vendors + starbase_vender_groups +
starbase_vender_inventory) ARE seeded from the NPC `sellsItems` field:
each vendor NPC gets one synth group (GroupID == npc_Id) and one row
per item in the per-NPC inventory. Buy-only items are stored with
sell_price=-1 ("not selling"); sell-only items get buy_price=0. The
schema column convention is `sell_price` == vendor sells to player at
this price (== JSONL.buyPrice), `buy_price` == vendor buys from player
at this price (== JSONL.sellPrice).
"""
from __future__ import annotations

import argparse
import glob
import json
import os
import re
import subprocess
import sys
from collections import defaultdict
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
DEFAULT_NPCS = "/data/dev/enb-emu-data-reconstruct-backup/db/npcs/npcs.jsonl"
DEFAULT_ITEMS_GLOB = "/data/dev/enb-emu-data-reconstruct-backup/db/json/cat*_lvl*.jsonl"
DEFAULT_OUT = REPO_ROOT / "db" / "postgres" / "seed_phase_y.sql"

# Live Postgres connection for fetching starbases + starbase_rooms at
# generate time. Defaults to the docker-compose dev mapping (host port
# 5434 -> container 5432). Override via env vars (PGHOST etc.) or CLI.
DEFAULT_PG_HOST = "127.0.0.1"
DEFAULT_PG_PORT = "5434"
DEFAULT_PG_USER = "net7"
DEFAULT_PG_PASS = "net7"
DEFAULT_PG_DB = "net7"

# Aliases for JSONL `station` strings that don't exact-match starbases.name.
# Keys are normalized (see norm_name); values are the exact starbases.name
# they should resolve to.
STATION_ALIASES: dict[str, str] = {
    "joves fury": "Joves Fury",          # JSONL has "Jove's Fury"
    "yasuragi": "Yasuragi City",
    "margesi": "Margesi Station",
    "nostrand vor station": "Nostrand Vor",
    "arx emporos": "Arx Emporos ",       # schema row has trailing space
}

# Room type ids per starbase_room_type table:
#   0 = Hanger (starport), 1 = Main Lobby, 2 = Bazaar (marketplace),
#   3 = Lounge.
# Fallback chains: NPCs with sellsItems prefer Bazaar; everyone else
# prefers Lounge. If the preferred room doesn't exist on that starbase,
# walk the chain.
ROOM_FALLBACK_VENDOR = (2, 1, 0, 3)
ROOM_FALLBACK_NONVENDOR = (3, 2, 1, 0)


def norm_name(s: str) -> str:
    """Lowercase + strip whitespace + strip apostrophes for fuzzy match."""
    return (s or "").strip().lower().replace("'", "").replace("’", "")

# ID-space partition: synth rows live above this floor so they cannot
# collide with any future authoritative import that uses the real
# Net-7 numeric IDs (which were all <= 4 digits historically).
SYNTH_ID_FLOOR = 100_000

# Item category / sub_category IDs come straight from the cat<N>
# filename prefix, so they stay in their original sub-100 / 100-211
# numeric ranges. Manufacturers and factions are name-keyed in the
# wiki data so we assign synth IDs above SYNTH_ID_FLOOR for them.


def sql_str(s: str | None) -> str:
    """Single-quote-escape a Python string for Postgres."""
    if s is None:
        return "NULL"
    return "'" + s.replace("'", "''") + "'"


def sql_int(v) -> str:
    if v is None:
        return "NULL"
    if isinstance(v, bool):
        return "1" if v else "0"
    return str(int(v))


def split_name(full: str) -> tuple[str, str]:
    """Split NPC name into (first, last) for the starbase_npcs columns
    that cap at varchar(19) each. Long single names are truncated; names
    with >2 tokens have everything after the first joined into last_name.
    """
    full = (full or "").strip()
    if not full:
        return ("", "")
    parts = full.split(None, 1)
    if len(parts) == 1:
        return (parts[0][:19], "")
    return (parts[0][:19], parts[1][:19])


def load_npcs(path: str):
    with open(path) as f:
        for line in f:
            line = line.strip()
            if line:
                yield json.loads(line)


def fetch_rooms(host: str, port: str, user: str, password: str, dbname: str):
    """Query the live Postgres for the starbases + starbase_rooms tables
    and the current max npc_index per room. Returns:
        starbase_by_norm: dict[normalized_name] -> starbase_id
        rooms_by_starbase: dict[starbase_id] -> dict[room_type] -> room_id
        room_next_index: dict[room_id] -> next free npc_index (max+1)
    Shells out to `psql -tA`; psycopg2 isn't installed in this dev env.
    """
    env = {**os.environ, "PGPASSWORD": password}
    base = ["psql", "-h", host, "-p", port, "-U", user, "-d", dbname, "-tA", "-F", "\t", "-c"]

    def run(sql: str) -> list[list[str]]:
        r = subprocess.run(base + [sql], env=env, capture_output=True, text=True, check=True)
        return [line.split("\t") for line in r.stdout.strip().split("\n") if line]

    starbase_by_norm: dict[str, int] = {}
    for row in run('SELECT starbase_id, name FROM starbases WHERE name IS NOT NULL;'):
        sid, name = int(row[0]), row[1]
        starbase_by_norm[norm_name(name)] = sid
        # Also key by exact-stripped form so the alias map can target
        # "Arx Emporos " (trailing space) precisely.
        starbase_by_norm[name.strip().lower().replace("'", "")] = sid

    rooms_by_starbase: dict[int, dict[int, int]] = defaultdict(dict)
    for row in run('SELECT starbase_id, type, room_id FROM starbase_rooms WHERE starbase_id IS NOT NULL AND type IS NOT NULL;'):
        sid, rtype, rid = int(row[0]), int(row[1]), int(row[2])
        rooms_by_starbase[sid][rtype] = rid

    room_next_index: dict[int, int] = {}
    rows = run('SELECT room_id, COALESCE(MAX(npc_index), -1) + 1 AS next_idx FROM starbase_npcs WHERE room_id IS NOT NULL AND "npc_Id" < 100000 GROUP BY room_id;')
    for row in rows:
        room_next_index[int(row[0])] = int(row[1])

    # Pre-existing NPCs indexed by (lowered first, lowered last, starbase_id).
    # Used downstream to suppress synth NPC room placement when the wiki
    # JSONL would otherwise duplicate an NPC who already lives at that
    # starbase under their own avatar template + inventory row.
    existing_names: set[tuple[str, str, int]] = set()
    rows = run(
        'SELECT LOWER(COALESCE(n.first_name, \'\')), LOWER(COALESCE(n.last_name, \'\')), r.starbase_id '
        'FROM starbase_npcs n JOIN starbase_rooms r ON n.room_id = r.room_id '
        'WHERE n."npc_Id" < 100000 AND r.starbase_id IS NOT NULL;'
    )
    for row in rows:
        if len(row) >= 3:
            existing_names.add((row[0], row[1], int(row[2])))

    return starbase_by_norm, dict(rooms_by_starbase), room_next_index, existing_names


def resolve_starbase(station: str,
                     starbase_by_norm: dict[str, int]) -> int | None:
    """Map JSONL `station` string -> starbase_id, or None."""
    s = (station or "").strip()
    if not s or s in ("N/A", "Space"):
        return None
    n = norm_name(s)
    if n in ("in space", "na", "space"):
        return None
    alias = STATION_ALIASES.get(n)
    if alias is not None:
        sid = starbase_by_norm.get(norm_name(alias))
        if sid is not None:
            return sid
    return starbase_by_norm.get(n)


def resolve_room(starbase_id: int | None, has_sells_items: bool,
                 rooms_by_starbase: dict[int, dict[int, int]]) -> int | None:
    """Map (starbase_id, sellsItems-flag) -> room_id, or None."""
    if starbase_id is None:
        return None
    rooms = rooms_by_starbase.get(starbase_id, {})
    if not rooms:
        return None
    chain = ROOM_FALLBACK_VENDOR if has_sells_items else ROOM_FALLBACK_NONVENDOR
    for rtype in chain:
        if rtype in rooms:
            return rooms[rtype]
    return None


def load_items(glob_pat: str):
    """Yield (cat, level, item_obj) per row. The same itemId may appear
    twice (rough + fine cat); de-dupe is the caller's job."""
    cat_re = re.compile(r".*cat(\d+)_lvl(\d+)\.jsonl$")
    for path in sorted(glob.glob(glob_pat)):
        m = cat_re.match(path)
        if not m:
            continue
        cat = int(m.group(1))
        lvl = int(m.group(2))
        with open(path) as f:
            for line in f:
                line = line.strip()
                if line:
                    yield cat, lvl, json.loads(line)


def main():
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--npcs", default=DEFAULT_NPCS)
    ap.add_argument("--items-glob", default=DEFAULT_ITEMS_GLOB)
    ap.add_argument("--out", default=str(DEFAULT_OUT))
    ap.add_argument("--pg-host", default=os.environ.get("PGHOST", DEFAULT_PG_HOST))
    ap.add_argument("--pg-port", default=os.environ.get("PGPORT", DEFAULT_PG_PORT))
    ap.add_argument("--pg-user", default=os.environ.get("PGUSER", DEFAULT_PG_USER))
    ap.add_argument("--pg-password", default=os.environ.get("PGPASSWORD", DEFAULT_PG_PASS))
    ap.add_argument("--pg-db", default=os.environ.get("PGDATABASE", DEFAULT_PG_DB))
    args = ap.parse_args()

    # Fetch room placement data from the live DB (the source of truth
    # is schema.sql, but parsing 200+ multi-tuple INSERTs is fragile;
    # the live DB already has them loaded).
    starbase_by_norm, rooms_by_starbase, room_next_index, existing_names = fetch_rooms(
        args.pg_host, args.pg_port, args.pg_user, args.pg_password, args.pg_db
    )
    print(f"  fetched {len(starbase_by_norm)//2} starbases, "
          f"{sum(len(v) for v in rooms_by_starbase.values())} rooms, "
          f"{len(existing_names)} pre-existing NPC name+starbase keys",
          file=sys.stderr)

    # -------- Pass 1: mine lookup keys --------
    item_rows: dict[int, dict] = {}  # itemId -> chosen-best row
    item_cats: dict[int, list[tuple[int, int]]] = defaultdict(list)
    manufacturers: set[str] = set()
    cat_ids: set[int] = set()
    factions: set[str] = set()
    # name lookups for vendor inventory: itemName -> itemId. displayName
    # is the user-facing form ("Quark Spark MLS"); raw `name` may carry a
    # level suffix ("Quark Spark MLS (L8)"). Both are kept; vendor JSONL
    # uses the displayName form.
    name_to_id: dict[str, int] = {}
    disp_to_id: dict[str, int] = {}

    for cat, lvl, o in load_items(args.items_glob):
        iid = int(o["itemId"])
        item_cats[iid].append((cat, lvl))
        # Prefer the row from the FINER (higher) cat number as canonical.
        # Same name+description across duplicates per inspection.
        prev = item_rows.get(iid)
        if prev is None or cat > prev["__cat"]:
            o["__cat"] = cat
            o["__lvl"] = lvl
            item_rows[iid] = o
        if o.get("manufacturer"):
            manufacturers.add(o["manufacturer"])
        cat_ids.add(cat)
        nm = (o.get("name") or "").strip()
        dn = (o.get("displayName") or "").strip()
        if nm:
            name_to_id.setdefault(nm, iid)
        if dn:
            disp_to_id.setdefault(dn, iid)

    npc_rows = list(load_npcs(args.npcs))
    for o in npc_rows:
        if o.get("faction"):
            factions.add(o["faction"])

    # Assign synth IDs deterministically (sorted by name).
    faction_id = {name: SYNTH_ID_FLOOR + i for i, name in enumerate(sorted(factions))}
    manu_id = {name: SYNTH_ID_FLOOR + i for i, name in enumerate(sorted(manufacturers))}

    # Categories: split the cat namespace into rough (<100) and fine
    # (>=100). Item rows get category=rough_cat, sub_category=fine_cat
    # when both are present. If only one bucket exists for an itemId,
    # use it for whichever range it falls into and leave the other 0.
    item_pair: dict[int, tuple[int, int]] = {}
    for iid, buckets in item_cats.items():
        rough = next((c for c, _ in buckets if c < 100), 0)
        fine = next((c for c, _ in buckets if c >= 100), 0)
        item_pair[iid] = (rough, fine)

    # Per-cat representative name = the most common itemType across the
    # items in that cat. Good enough for the lookup table's `category`
    # / `subcategory` name column.
    cat_name: dict[int, str] = {}
    cat_type_counts: dict[int, dict[str, int]] = defaultdict(lambda: defaultdict(int))
    for iid, o in item_rows.items():
        it = o.get("itemType") or ""
        for c, _ in item_cats[iid]:
            cat_type_counts[c][it] += 1
    for c, counts in cat_type_counts.items():
        best = max(counts.items(), key=lambda kv: kv[1])[0]
        cat_name[c] = best or f"cat{c}"

    # -------- Pass 2: emit SQL --------
    out = Path(args.out)
    out.parent.mkdir(parents=True, exist_ok=True)
    f = out.open("w")
    w = lambda s: f.write(s + "\n")

    w("-- Generated by tools/dataimport-jsonl/generate_seed.py.")
    w("-- Do not edit by hand. Re-run the script to regenerate.")
    w("-- Sources:")
    w(f"--   NPCs:  {args.npcs}")
    w(f"--   Items: {args.items_glob}")
    w("-- All inserts use ON CONFLICT DO NOTHING so re-applying is safe.")
    w("")
    w("BEGIN;")
    w("")
    # Idempotent cleanup of every synth row this script could have written
    # in any prior revision. Reapplying always converges to the current
    # emit (rows for placed/ghosted NPCs; nothing for skipped duplicates).
    # Order matters: child rows before parents to honor implicit FKs.
    # `manufacturers` was an earlier-revision miss-target (server reads
    # item_manufacturer_base instead); the row count is small so we
    # delete from both.
    w(f"DELETE FROM starbase_vender_inventory WHERE id >= {SYNTH_ID_FLOOR};")
    w(f"DELETE FROM starbase_vendors WHERE vendor_id >= {SYNTH_ID_FLOOR};")
    w(f"DELETE FROM starbase_vender_groups WHERE \"GroupID\" >= {SYNTH_ID_FLOOR};")
    w(f"DELETE FROM starbase_npcs WHERE \"npc_Id\" >= {SYNTH_ID_FLOOR};")
    w(f"DELETE FROM manufacturers WHERE manufacturer_id >= {SYNTH_ID_FLOOR};")
    w(f"DELETE FROM item_manufacturer_base WHERE id >= {SYNTH_ID_FLOOR};")
    w(f"DELETE FROM factions WHERE faction_id >= {SYNTH_ID_FLOOR};")
    w("")

    # ---- factions (synth from JSONL faction strings) ----
    w("-- factions: derived from NPC `faction` strings. IDs >= 100000.")
    for name in sorted(factions):
        fid = faction_id[name]
        w(
            f"INSERT INTO factions (faction_id, name, description, \"player_PDA\", \"PDA_text\") "
            f"VALUES ({fid}, {sql_str(name)}, {sql_str(name)}, 1, '') "
            f"ON CONFLICT (faction_id) DO NOTHING;"
        )
    w("")

    # ---- item_manufacturer_base (the table the server actually queries) ----
    # NOTE: the schema has TWO manufacturer tables. The server queries
    # `item_manufacturer_base` (see server/src/ItemBaseSQL.cpp's
    # "Inner Join item_manufacturer_base"). `manufacturers` is an
    # editor-suite lookup that the runtime ignores; we skip it.
    w("-- item_manufacturer_base: derived from item `manufacturer` strings. IDs >= 100000.")
    for name in sorted(manufacturers):
        mid = manu_id[name]
        w(
            f"INSERT INTO item_manufacturer_base (id, name) "
            f"VALUES ({mid}, {sql_str(name[:45])}) "
            f"ON CONFLICT (id) DO NOTHING;"
        )
    w("")

    # ---- item_categories + item_subcategories ----
    w("-- item_categories: rough buckets, cat ids 0-99 from JSONL filenames.")
    for c in sorted(cat_ids):
        if c < 100:
            w(
                f"INSERT INTO item_categories (id, category) "
                f"VALUES ({c}, {sql_str(cat_name[c])}) "
                f"ON CONFLICT (id) DO NOTHING;"
            )
    w("")
    w("-- item_subcategories: fine buckets, cat ids 100-299 from JSONL filenames.")
    for c in sorted(cat_ids):
        if c >= 100:
            # subcategory column caps at varchar(40)
            w(
                f"INSERT INTO item_subcategories (id, subcategory) "
                f"VALUES ({c}, {sql_str(cat_name[c][:40])}) "
                f"ON CONFLICT (id) DO NOTHING;"
            )
    w("")

    # ---- item_base ----
    w(f"-- item_base: {len(item_rows)} unique itemIds.")
    cols = [
        "id", "level", "category", "sub_category", "type", "max_stack",
        "name", "description", "manufacturer", "\"2d_asset\"", "\"3d_asset\"",
        "no_trade", "no_store", "no_destroy", "no_manu", "\"unique\"",
        "custom_flag", "status", "effect_id", "price",
        "man_cost_base", "man_cost_mod", "man_cost", "man_dif", "ana_dif",
        "dis_dif", "price_tweak", "selling_price", "buying_price",
        "sell_mod", "buy_mod", "quality_mod",
    ]
    w(f"-- columns: {', '.join(cols)}")
    for iid in sorted(item_rows):
        o = item_rows[iid]
        rough, fine = item_pair[iid]
        name = (o.get("displayName") or o.get("name") or "")[:64]
        desc = o.get("description") or ""
        mfr = o.get("manufacturer")
        mfr_id = manu_id[mfr] if mfr in manu_id else 0
        no_trade = 1 if o.get("notTradable") else 0
        no_store = 1 if o.get("notStorable") else 0
        no_destroy = 1 if o.get("notDestroyable") else 0
        no_manu = 1 if o.get("notManufacturable") else 0
        unique = 1 if o.get("unique") else 0
        max_stack = int(o.get("stackSize") or 1)
        price = int(o.get("sellPrice") or o.get("buyPrice") or 0)
        sell = int(o.get("sellPrice") or 0)
        buy = int(o.get("buyPrice") or 0)
        man_cost = int(o.get("manufactureCost") or 0)
        lvl = int(o.get("level") or o.get("__lvl") or 1)
        vals = [
            sql_int(iid), sql_int(lvl), sql_int(rough), sql_int(fine),
            sql_int(0),  # type -- not in JSONL
            sql_int(max_stack),
            sql_str(name), sql_str(desc), sql_int(mfr_id),
            sql_int(0), sql_int(0),  # 2d/3d asset placeholders
            sql_int(no_trade), sql_int(no_store), sql_int(no_destroy),
            sql_int(no_manu), sql_int(unique),
            sql_int(0), sql_int(0), sql_int(0),  # custom_flag, status, effect_id
            sql_int(price),
            sql_int(0), sql_int(1), sql_int(man_cost),  # man_cost_base, mod, cost
            sql_int(0), sql_int(0), sql_int(0),  # man_dif, ana_dif, dis_dif
            sql_int(1),  # price_tweak
            sql_int(sell), sql_int(buy),
            sql_int(1), sql_int(1),  # sell_mod, buy_mod
            "1",  # quality_mod (double)
        ]
        w(f"INSERT INTO item_base ({', '.join(cols)}) VALUES ({', '.join(vals)}) ON CONFLICT (id) DO NOTHING;")
    w("")

    # ---- starbase_npcs ----
    # Resolve room placement up front. Three outcomes per JSONL row:
    #   PLACED      -> insert synth NPC + room_id + npc_index. Net-new
    #                  NPC that doesn't collide with the pre-existing roster.
    #   SKIPPED     -> JSONL NPC duplicates a pre-existing one at the same
    #                  starbase (same first+last name). Don't insert
    #                  ANYTHING -- not the NPC row, not the vendor row,
    #                  not the inventory rows. The pre-existing schema row
    #                  keeps its own avatar template and inventory.
    #   GHOSTED     -> station doesn't resolve to a starbase (Porvenir Mons,
    #                  Chavez Capital Ship, ...). Insert the NPC row with
    #                  room_id=NULL so it sits in the table but never gets
    #                  rendered. Vendor/inventory rows are still emitted so
    #                  the data is preserved for future room assignment.
    PLACED, SKIPPED, GHOSTED = 0, 1, 2
    npc_state: list[int] = []
    npc_placement: list[tuple[int | None, int | None]] = []
    placed = skipped = ghosted = 0
    unplaced_stations: dict[str, int] = defaultdict(int)
    for o in npc_rows:
        station = o.get("station") or ""
        has_inv = bool(o.get("sellsItems"))
        sb_id = resolve_starbase(station, starbase_by_norm)
        first, last = split_name(o.get("name") or "")
        key = (first.lower(), last.lower(), sb_id) if sb_id is not None else None
        if sb_id is not None and key in existing_names:
            npc_state.append(SKIPPED)
            npc_placement.append((None, None))
            skipped += 1
            continue
        rid = resolve_room(sb_id, has_inv, rooms_by_starbase)
        if rid is None:
            npc_state.append(GHOSTED)
            npc_placement.append((None, None))
            ghosted += 1
            if station and station.strip() not in ("N/A", "Space", "in space"):
                unplaced_stations[station.strip()] += 1
        else:
            idx = room_next_index.get(rid, 0)
            room_next_index[rid] = idx + 1
            npc_state.append(PLACED)
            npc_placement.append((rid, idx))
            placed += 1
    print(f"  npc placement: {placed} placed, {skipped} skipped (dupes), "
          f"{ghosted} ghosted (no matching starbase)",
          file=sys.stderr)
    if unplaced_stations:
        print(f"  unresolved stations ({len(unplaced_stations)}):", file=sys.stderr)
        for s, n in sorted(unplaced_stations.items(), key=lambda x: -x[1]):
            print(f"    {n:4d}  {s!r}", file=sys.stderr)

    w(f"-- starbase_npcs: {len(npc_rows)} JSONL NPCs (synth npc_Id >= {SYNTH_ID_FLOOR}). "
      f"{placed} placed, {skipped} skipped (pre-existing dupes), "
      f"{ghosted} ghosted (no matching starbase).")
    w("-- Missing per-NPC: avatar template (starbase_npc_avatar_templates).")
    for i, o in enumerate(npc_rows):
        if npc_state[i] == SKIPPED:
            continue
        nid = SYNTH_ID_FLOOR + i
        first, last = split_name(o.get("name") or "")
        fid = faction_id.get(o.get("faction") or "")
        descr = o.get("service") or o.get("location") or ""
        descr = descr[:255]
        # Use the station string as a coarse "talk_tree_handle" tag --
        # nothing reads this in the current server but it preserves the
        # wiki context for future translation work.
        tth = (o.get("station") or "")[:64000]
        rid, idx = npc_placement[i]
        # ON CONFLICT DO UPDATE for room_id/npc_index so that an earlier
        # apply (without room placement) gets back-filled when the seed
        # is re-applied. Other columns stay frozen on the first apply.
        w(
            f"INSERT INTO starbase_npcs (\"npc_Id\", first_name, last_name, faction_id, description, talk_tree_handle, room_id, npc_index) "
            f"VALUES ({nid}, {sql_str(first)}, {sql_str(last)}, {sql_int(fid)}, {sql_str(descr)}, {sql_str(tth)}, "
            f"{sql_int(rid)}, {sql_int(idx)}) "
            f"ON CONFLICT (\"npc_Id\") DO UPDATE SET room_id = EXCLUDED.room_id, npc_index = EXCLUDED.npc_index;"
        )
    w("")

    # ---- starbase_vender_groups + starbase_vendors + starbase_vender_inventory ----
    # One synth group per vendor NPC. GroupID == npc_Id, vendor_id ==
    # npc_Id (StationLoader.cpp joins on starbase_npcs.npc_Id =
    # starbase_vendors.vendor_id). For each sellsItems entry: look up
    # itemId via displayName / name; skip unmatched names. tradeType
    # convention:
    #   Buy/Sell -> sell_price=buyPrice, buy_price=sellPrice
    #   Sell     -> sell_price=buyPrice, buy_price=0
    #   Buy      -> sell_price=-1 (not for sale), buy_price=sellPrice
    # quanity=-1 means infinite stock (server convention).
    vendor_group_rows: list[tuple[int, str]] = []
    vendor_rows: list[tuple[int, int, int]] = []  # vendor_id, level, groupid
    inv_rows: list[tuple[int, int, int, int]] = []  # groupid, itemid, sell_price, buy_price
    inv_matched = inv_unmatched = vendor_count = 0
    for i, o in enumerate(npc_rows):
        # Skip vendors whose NPC row was deduped against a pre-existing
        # schema NPC. The pre-existing vendor row (with its own groupid
        # + inventory) keeps the slot at this room+name. We don't try to
        # merge the wiki inventory into the pre-existing groupid because
        # that would conflate two distinct authoritative sources and
        # change vendor behavior in subtle ways.
        if npc_state[i] == SKIPPED:
            continue
        items = o.get("sellsItems") or []
        if not items:
            continue
        nid = SYNTH_ID_FLOOR + i
        # de-dupe within a vendor by itemId; keep first occurrence
        seen: dict[int, tuple[int, int]] = {}
        max_lvl = 0
        for it in items:
            nm = (it.get("item") or "").strip()
            iid = disp_to_id.get(nm) or name_to_id.get(nm)
            if not iid:
                inv_unmatched += 1
                continue
            tt = (it.get("tradeType") or "").strip()
            bp = int(it.get("buyPrice") or 0)   # player buys at this price
            sp = int(it.get("sellPrice") or 0)  # player sells at this price
            if tt == "Buy":
                sell_price = -1
                buy_price = sp
            elif tt == "Sell":
                sell_price = bp
                buy_price = 0
            else:  # Buy/Sell or unknown -> both
                sell_price = bp
                buy_price = sp
            seen.setdefault(iid, (sell_price, buy_price))
            max_lvl = max(max_lvl, int(it.get("level") or 0))
            inv_matched += 1
        if not seen:
            continue
        vendor_count += 1
        first, last = split_name(o.get("name") or "")
        gname = f"{first} {last}".strip()[:50] or f"NPC {nid}"
        vendor_group_rows.append((nid, gname))
        vendor_rows.append((nid, max_lvl or 1, nid))
        for iid, (sell_p, buy_p) in seen.items():
            inv_rows.append((nid, iid, sell_p, buy_p))

    w(f"-- starbase_vender_groups: {len(vendor_group_rows)} groups (one per vendor NPC).")
    for gid, gname in vendor_group_rows:
        w(
            f"INSERT INTO starbase_vender_groups (\"GroupID\", \"GroupName\", \"SellMultiplyer\", \"BuyMultiplyer\", \"BuyOnlyList\") "
            f"VALUES ({gid}, {sql_str(gname)}, 1.0, 1.0, 0) "
            f"ON CONFLICT (\"GroupID\") DO NOTHING;"
        )
    w("")

    w(f"-- starbase_vendors: {len(vendor_rows)} rows. vendor_id == npc_Id.")
    for vid, lvl, gid in vendor_rows:
        w(
            f"INSERT INTO starbase_vendors (vendor_id, level, booth_type, groupid) "
            f"VALUES ({vid}, {sql_int(lvl)}, -1, {gid}) "
            f"ON CONFLICT (vendor_id) DO NOTHING;"
        )
    w("")

    w(f"-- starbase_vender_inventory: {len(inv_rows)} rows ({inv_matched} matched, {inv_unmatched} unmatched item names).")
    w("-- Explicit ids in the 100000+ synth range; schema.sql ships rows with")
    w("-- ids 1-11269 and the IDENTITY sequence is not advanced, so auto-assign")
    w("-- would collide with them. ON CONFLICT (id) DO NOTHING for idempotency.")
    for i, (gid, iid, sell_p, buy_p) in enumerate(inv_rows):
        rid = SYNTH_ID_FLOOR + i
        w(
            f"INSERT INTO starbase_vender_inventory (id, groupid, itemid, sell_price, buy_price, quanity) "
            f"VALUES ({rid}, {gid}, {iid}, {sell_p}, {buy_p}, -1) "
            f"ON CONFLICT (id) DO NOTHING;"
        )
    w("")

    w("COMMIT;")
    f.close()

    # -------- Summary --------
    print(f"wrote {out}", file=sys.stderr)
    print(f"  factions: {len(factions)} (synth IDs {SYNTH_ID_FLOOR}+)", file=sys.stderr)
    print(f"  item_manufacturer_base: {len(manufacturers)} (synth IDs {SYNTH_ID_FLOOR}+)", file=sys.stderr)
    print(f"  item_categories: {sum(1 for c in cat_ids if c<100)}", file=sys.stderr)
    print(f"  item_subcategories: {sum(1 for c in cat_ids if c>=100)}", file=sys.stderr)
    print(f"  item_base: {len(item_rows)} unique itemIds", file=sys.stderr)
    print(f"  starbase_npcs: {len(npc_rows)}", file=sys.stderr)
    print(f"  starbase_vendors: {vendor_count} (npc_Id == vendor_id == GroupID)", file=sys.stderr)
    print(f"  starbase_vender_inventory: {len(inv_rows)} rows ({inv_matched} matched, {inv_unmatched} unmatched)", file=sys.stderr)


if __name__ == "__main__":
    main()
