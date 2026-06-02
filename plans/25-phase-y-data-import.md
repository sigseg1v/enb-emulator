# Phase Y — Reference-data import (NPCs / skills / buffs / sector maps / items / quests)

Goal: close the **curatorial** gap between our server and retail. The
Phase K byte-diff work has reduced the StationLogin handshake stream
to a 73-frame ItemBase deficit + a handful of per-character-state
gaps (see `tests/integration/CliClient.IntegrationTests/Verification/DockHandshakeFriendship7Tests.cs`).
That residual deficit is **almost entirely DB seed**: starbase 73's
lounge NPC roster, equipped-item EquipEffect rows, ability/skill
definitions, sector route topology, mission/quest tables.

None of these tasks are server-code changes. Each is a one-shot
DB-seed import from a primary-source data dump.

## Process

**Every task in this phase is GATED on the user providing an
up-to-date reference dump for the corresponding data category.**
Do not start any task in this phase without:

1. Explicit user approval to begin the task, AND
2. A pointer to an up-to-date reference: a MySQL dump file path,
   a CSV from a Net-7 admin export, a packet capture with the
   relevant authoritative rows, a docs/RTF spec from the kyp-snapshot,
   or equivalent primary source per the CLAUDE.md server-integrity
   rules.

The kyp-snapshot under `archive/kyp-snapshot/` contains an OLDER
reference set; do not assume it is current. The user has stated they
will provide newer references when each task is approved.

## Items

- [~] **Y1: NPC roster import** -- populate `starbase_npcs`,
  `factions`, `manufacturers`, and the vendor tables
  (`starbase_vendors` / `starbase_vender_groups` /
  `starbase_vender_inventory`) with the full Net-7-era NPC roster.
  Primary use: closes the 73-frame ItemBase deficit on the Friendship
  7 dock handshake (the missing rows are the lounge NPC inventory and
  avatar/ship descriptors that Friendship 7's `LoungeNPC_45151.dat`
  references). Acceptance: `DockHandshakeFriendship7Tests` ItemBase
  count climbs from 4 toward 77.
  **STATUS: PARTIAL IMPORT LANDED 2026-05-31, room placement added
  same day.** Source: `/data/dev/enb-emu-data-reconstruct-backup/db/npcs/npcs.jsonl`
  (JSONL reconstruct dataset, NOT a Net-7 server dump). Importer:
  `tools/dataimport-jsonl/generate_seed.py`. Output:
  `db/postgres/seed_phase_y.sql` (applied at boot via the
  `schema-init` service, gated on `starbase_npcs.npc_Id = 100000`;
  idempotent on re-apply via top-of-seed `DELETE WHERE id >= 100000`
  for every synth-touched table).
  **Dedup**: importer queries live PG for pre-existing
  `(first_name, last_name, starbase_id)` and SKIPS any wiki NPC that
  matches a schema.sql roster entry at the same starbase (no NPC
  row, no vendor row, no inventory emitted). The wiki JSONL largely
  redescribes NPCs already in `schema.sql`, so most rows dedup out.
  **Station resolution**: JSONL `station` string fuzzy-matched
  against `starbases.name` (52/65 exact + 5 manual aliases = 57/65
  resolved; 8 unresolved stations are NPCs whose starbase genuinely
  does not exist in `starbases` -- Porvenir Mons, Chavez Capital
  Ship, Warship Genesis, etc.).
  **Room placement**: each resolved starbase has a 4-room layout
  (Hangar=0, Main Lobby=1, Bazaar=2, Lounge=3). NPCs with
  `sellsItems` go to Bazaar with fallback chain (2,1,0,3); others
  go to Lounge (3,2,1,0). `npc_index` is a per-room ordinal so
  multiple NPCs share a room cleanly. Rows whose station did NOT
  resolve are emitted as "ghost" NPCs with `room_id=NULL`
  (StationLoader's `WHERE room_id=?` filters them out, so they sit
  in DB harmlessly until starbase wiring lands).
  **Final synth counts (post-dedup)**: 156 NPCs synth-inserted (83
  placed in real rooms, 73 ghosted with NULL room_id); 349 of the
  original 505 wiki NPCs were SKIPPED as schema duplicates. 28
  factions, 50 manufacturers, 26 `starbase_vendors` + 26
  `starbase_vender_groups` + 508 `starbase_vender_inventory` rows.
  `vendor_id == npc_Id == GroupID` by construction so the
  `StationLoader.cpp:333` left-join works. tradeType mapping:
  `Buy/Sell` -> both prices; `Sell` -> buy_price=0; `Buy` ->
  sell_price=-1 (server treats -1 as "not for sale"). Pre-existing
  schema vendor data: 556 vendors / 8608 inventory rows untouched.
  **Caveats** -- this does NOT yet close the 4-vs-77 byte-deficit:
    * 73 ghost NPCs (un-resolved-station rows) carry no room_id
      and won't be enumerated until starbase wiring lands.
    * No per-NPC avatar template rows (would need
      `starbase_npc_avatar_templates` seed; JSONL has no avatar IDs).
  Follow-up to actually close the byte-deficit: either (a) source
  per-NPC avatar template IDs or (b) restore the missing 8 starbases
  in `starbases` so the ghost NPCs can be placed.

- [ ] **Y2: Skill definitions import** -- populate the `skill` /
  `skill_data` / class-skill-map tables so `SkillsList()` at
  `server/src/PlayerClass.cpp:362` returns the full per-class skill
  unlock matrix instead of the bare defaults a fresh test character
  sees today. Touches: SkillsList wire-frame contents at zone-in,
  the `0x001B` AuxData ship-stats frame that includes derived skill
  bonuses, and the per-equip skill-gated install checks.
  **STATUS: AWAITING USER APPROVAL + REFERENCE DATA.**

- [ ] **Y3: Buff / item-effect definitions import** -- populate the
  `item_effect_data` / buff tables that drive
  `ItemBase::EquipEffect()` lookups in `server/src/Equipable.cpp:1357`.
  Closes the gap that makes our fresh-character `m_Effects.SendEffects`
  iterate an empty list while retail emits one or more `0x0009`
  ObjectEffect frames per persistent-buff slot.
  **STATUS: AWAITING USER APPROVAL + REFERENCE DATA.**

- [x] **Y4: Sector navigation-marker import** -- add the
  nav-point / hidden-nav-point markers the runtime `sector_objects`
  table is genuinely missing, after a by-name + by-position diff of the
  reconstruct dataset against the authoritative runtime. Closes part of
  the `m_ObjectMgr->SendAllNavs` curatorial gap on the SectorLogin
  (space-arm) path.
  **STATUS: IMPORT LANDED 2026-06-02. 48 markers.** Source:
  `/data/dev/enb-emu-data-reconstruct-backup/db/sectors/json/*.jsonl`.
  Importer: `tools/dataimport-jsonl/generate_seed_navs.py`. Output:
  `db/postgres/seed_phase_y_navs.sql` (applied at boot by the
  `schema-init` service after `seed_phase_y.sql`, gated on
  `sector_objects.sector_object_id = 100000`; idempotent via
  top-of-seed `DELETE WHERE sector_object_id >= 100000` on
  `sector_nav_points` then `sector_objects`, and per-row
  `ON CONFLICT (sector_object_id) DO NOTHING`).
  **Why only 51 of ~2286 dataset nav names**: the bulk are already
  authoritative in `sector_objects` (9382 rows). The diff drops, in
  order: (1) only `nav-point`/`hidden-nav-point` types are eligible --
  gates, stations, planets, moons are excluded because they need
  relational rows the dataset lacks (`gate_to` +
  `sector_objects_stargates` faction/class/security;
  `sector_objects_starbases` + interior; `sector_objects_planets`
  orbit) and importing them would create non-functional broken objects;
  (2) `Planet*`/`Moon*` stems are excluded -- they either duplicate an
  existing surface sector or are unmappable (e.g. the dataset's 29
  "Planet Inverness" records already exist 1:1 as runtime sector 4093);
  (3) name-dedup against the resolved sector's existing nav names
  (1659 skipped) -- this normalizes both sides through `core_name()`,
  which strips the runtime `[asset NNNN]` annotation and a
  "Wreckage of the" prefix so e.g. dataset "Teoyaomqui Maru" dedups
  against runtime "Wreckage of the Teoyaomqui Maru [asset 1268]"
  (equality after stripping, NOT substring, so "Above Hadean" stays
  distinct from "Hadean"); (4) position-dedup against existing nav
  positions at the /1000 scale (134 skipped -- catches renamed dupes
  like "Neptune Nav 3" == runtime "Nav 3"). What survives is 48 markers
  in sectors the runtime has but whose specific markers were absent.
  **Field derivation**: the dataset gives only name + xyz +
  visible/hidden. The runtime nav model needs ~15 more fields
  (`type`, `base_asset_id`, `nav_type`, `is_huge`, `radar_range`,
  `scale`, `h/s/v`, orientation quat, `signature`, `base_xp`,
  `exploration_range`, `object_radius_patch`). These are DERIVED from
  the same sector's existing navs of the same visibility class (modal
  categoricals, median numerics; global fallback if the sector has no
  navs of that class). `appears_in_radar`/`nav_type` are set from the
  visible/hidden flag (visible->1/varies, hidden->0/0).
  **Caveats (honest)**: (a) positions are the dataset's /1000 coords
  multiplied by 1000 back to the runtime frame, so they are
  APPROXIMATE (the scale wobbles 1000-1002 and the dataset rounds to 2
  decimals) -- NOT byte-accurate retail values; (b) the ~15 derived
  fields are statistical fills, not authoritative per-marker values.
  These markers are functional navigation aids, not a fidelity claim;
  a Net-7 server dump with real coords + asset ids would supersede
  them (the seed's synth-id range makes that a clean replacement).
  Synth ids 100000..100047.

- [~] **Y5: Item catalog import** -- populate `item_base` /
  `item_categories` / `item_subcategories` with the reconstruct item
  catalog. Acceptance: a fresh-character's starter-loadout
  `SendItemBase` calls in `server/src/PlayerClass.cpp:1080-1083`
  resolve to a real template, not the placeholder-or-NULL most rows
  currently return.
  **STATUS: PARTIAL IMPORT LANDED 2026-05-31.** Source:
  `/data/dev/enb-emu-data-reconstruct-backup/db/json/cat*_lvl*.jsonl`
  (JSONL reconstruct dataset). Same importer / output / gate as Y1. Loaded:
  5003 unique `item_base` rows (real itemId range, no synth offset
  here because itemIds are <10k and won't collide with future
  authoritative imports). 12 `item_categories` rows (rough buckets,
  cat <100), 38 `item_subcategories` rows (fine buckets, cat 100-211).
  Category / subcategory IDs come straight from the cat<N> filename
  prefix. Item-row category column = rough bucket; sub_category
  column = fine bucket. Caveats:
    * Item type-specific tables get ZERO rows (item_beam, item_engine,
      item_device, item_shield, item_reactor, item_missile, item_ammo,
      item_projectile, item_refine, item_manufacture). The JSONL
      `stats` blob doesn't map to their schema columns without a
      per-type translation table that doesn't exist in the JSONL.
    * `2d_asset` / `3d_asset` ids are placeholder zeros. The JSONL
      has `imageLink` path strings but no integer asset registry.
    * `type` column is 0 for every row (the JSONL `itemType` is a
      free-text string, not an `item_type.id` integer; no mapping
      table).
    * `effect_id` is 0; the JSONL doesn't carry buff/effect ids.
  Without item-type-specific rows, ItemBase frames the server emits
  will still be incomplete and the 4-vs-77 byte-deficit cannot fully
  close. Follow-up: extend the importer with a `itemType` ->
  `item_type.id` map and per-type stat field translators, OR
  source a real Net-7 dump that already has the integer ids.

- [!] **Y6: Mission / quest catalog import** -- populate the `missions`
  table so mission terminals (`0x0011` `Hijackee` interactions,
  job-terminal flows) return real missions instead of empty lists.
  Acceptance: the in-game job terminal in any starbase returns at
  least one available mission for a fresh character at level-
  appropriate range.
  **STATUS: BLOCKED -- reconstruct dataset has no `mission_XML`; runtime is
  already authoritative. See "Finding 2026-06-01" below.** The runtime
  `missions` table already holds **364** missions and **all 364 carry a
  non-empty `mission_XML`** -- the script the server actually executes.
  The wiki `missions/missions.jsonl` is human walkthrough prose (giver,
  reward text, narrative steps) with NO XML and no mission_key. A
  name-only mission row would make the job terminal advertise a mission
  the server cannot run. Real unblock needs the Net-7 mission XML dump.
  (Note: the plan's `mission_step`/`mission_dialog`/`mission_npc_link`
  tables do not exist in `db/postgres/schema.sql`; the runtime keeps the
  whole mission in `missions.mission_XML`.)

- [!] **Y7: Mob catalog import** -- (was on the user's data list:
  `mobs/mobs.jsonl`). **BLOCKED -- runtime already authoritative.** The
  runtime `mob_base` holds **2042** mob templates with real
  `base_asset_id`, `faction_id`, and AI scripts (carrying original
  Byakhee/Skeletor 2009 dev annotations); `mob_items` holds **8583**
  authoritative loot rows; `sector_objects_mob` holds 1385 spawn rows.
  77% of the 1127 wiki mob names already exist by name in `mob_base`.
  The wiki rows have no asset/faction/AI and mostly "?" stats. Injecting
  would create asset-less mobs. Real unblock needs a Net-7 mob dump.

- [!] **Y8: Item-drop table import** -- (was on the user's data list:
  `items/item_drops.jsonl`). **BLOCKED -- runtime already authoritative.**
  `mob_items` (mob_id -> item_base_id, drop_chance, qty) already holds
  **8583** loot rows. The wiki maps item-name -> mob-name with drop
  rates that are mostly "?" (no numeric drop_chance). Of 933 distinct
  wiki mob refs, 720 resolve to `mob_base` by name -- i.e. the data is
  largely a name-level restatement of rows that already exist with real
  drop chances. Injecting fabricated drop_chance values onto authoritative
  mobs would corrupt loot tables. Real unblock needs a Net-7 drop dump.

- [!] **Y9: Prospecting / resource import** -- (was on the user's data
  list: `prospecting/prospecting.jsonl` + `prospect_fields.jsonl`).
  **BLOCKED -- runtime already authoritative.** `sector_objects_harvestable`
  (+ `_oretypes` / `_restypes`) holds **882** harvestable fields keyed by
  `resource_id` (a sector_object_id); `item_base` holds the ore/resource
  items (from Y5). The wiki gives resource-name -> nav distribution with
  no `resource_id` linkage to the runtime fields. Injecting would need
  fabricated ids and would not connect to the authoritative spawn fields.
  Real unblock needs a Net-7 harvestable dump.

## Finding 2026-06-01..02: per-category diff of the reconstruct dataset

The user pointed at `/data/dev/enb-emu-data-reconstruct-backup/db`
(items/missions/mobs/npcs/prospecting/sectors) and asked for a real
diff -- not a wholesale insert -- to find and import the genuine gaps.
Investigation (`tools/dataimport-jsonl/analyze_wiki_coverage.py`,
reproducible against the dev Postgres) shows the authoritative runtime
tables are densely populated for every category, and the reconstruct
JSONL is largely a name-level near-duplicate in an **incompatible
form** (no asset/faction ids, no mission XML, /1000 coords). The
decisive scale proof: dataset `sectors/json/ABA.jsonl` nav "Sado Pit"
at (-152.74, -63.89) is the runtime nav "Sado Pit" in sector 1076 at
(-152740, -63860) -- runtime coords == dataset coords * 1000.

A by-name + by-position diff per category yields:

| category | runtime table | runtime rows | verdict |
|---|---|---|---|
| sectors/navs | `sector_objects` | 9382 | **48-marker gap imported (Y4)** after type-filter + name/position dedup |
| npcs | `starbase_npcs` | -- | **156-NPC gap imported (Y1)** after roster dedup |
| items | `item_base` | -- | **5003-row catalog imported (Y5)**, real itemIds |
| mobs | `mob_base` | 2042 | no importable gap -- no asset/faction/AI, "?" stats |
| missions | `missions` | 364 (all w/ XML) | no importable gap -- dataset has no `mission_XML` |
| item drops | `mob_items` | 8583 | no importable gap -- drop rates mostly "?" |
| prospecting | `sector_objects_harvestable` | 882 | no importable gap -- no `resource_id` linkage |

**What was importable** (Y1 NPCs, Y4 navs, Y5 items): a real diff
surfaced rows the runtime genuinely lacked, and each carried enough to
build a valid runtime row (NPCs deduped against the roster; navs got
derived per-sector fields; items had real integer itemIds). All three
landed via `generate_seed*.py` -> `db/postgres/seed_phase_y*.sql`,
gated and idempotent, applied by the `schema-init` service.

**What was NOT importable** (mobs, missions, item-drops, prospecting):
the diff found mostly name-level restatements of rows that already
exist authoritatively, plus a tail of new names that cannot be turned
into valid runtime rows because the dataset lacks the load-bearing
fields -- a mob with no asset/faction/AI, a mission with no XML the
server can run, a drop row with a "?" chance, a resource with no
`resource_id` linkage to the spawn fields. Injecting those would
create broken/divergent objects, which the CLAUDE.md server-integrity
rules forbid absent a primary-source escape hatch. These categories
stay blocked pending a Net-7 server dump with the real ids/XML. The
data remains preserved as the structured JSONL archive in the
reconstruct backup.

## Tracking notes

- Each Y-task above is one DB-seed import, NOT a code change. The
  Postgres schema in `db/postgres/` should already accommodate the
  rows; if it does not, that is an in-scope schema gap to add to the
  corresponding Y-task.
- After each Y-task lands, re-run `DockHandshakeFriendship7Tests`
  and any other Phase K byte-diff regression tests to see how much
  of the histogram deficit closed; record the before/after numbers
  in the task's "Notes" line.
- Do NOT batch Y-tasks together: each is independently testable
  against a specific byte-diff metric, and batching loses the
  attribution of which import fixed which frame count.
