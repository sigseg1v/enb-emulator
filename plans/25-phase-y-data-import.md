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
  (wiki-scraped JSONL, NOT a Net-7 server dump). Importer:
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

- [!] **Y4: Sector map / system topology import** -- populate
  `sector`, `sector_link`, `system`, `nav_object`, `gate` and the
  associated coordinates / faction-region tables so warp routes and
  the in-game galaxy map match retail. Closes the `m_ObjectMgr->SendAllNavs`
  curatorial gap on the SectorLogin (space-arm) path -- not the
  station path -- but is a prerequisite for any post-Phase-K cross-
  sector navigation testing.
  **STATUS: BLOCKED -- wiki scrape is the WRONG source; runtime is
  already authoritative. See "Finding 2026-06-01" below.** The runtime
  `sector_objects` table already holds **9382** authoritative navs with
  real `base_asset_id` and runtime coordinates; `sectors` has 130 rows.
  The wiki `sectors/json/*.jsonl` is the SAME navs at 1/1000 the
  coordinate scale with no asset ids (proof: wiki "Sado Pit"
  (-152.74,-63.89) == runtime "Sado Pit" (-152740,-63860) in sector
  1076). 85% of the 2286 wiki nav names already exist by name in
  `sector_objects`. Injecting them would create wrong-position,
  asset-less duplicate navs -- divergence forbidden by the
  server-integrity rules. Real unblock needs a Net-7 server dump with
  integer asset ids + runtime coords, not the wiki scrape.

- [~] **Y5: Item catalog import** -- populate `item_base` /
  `item_categories` / `item_subcategories` with the wiki-scraped item
  catalog. Acceptance: a fresh-character's starter-loadout
  `SendItemBase` calls in `server/src/PlayerClass.cpp:1080-1083`
  resolve to a real template, not the placeholder-or-NULL most rows
  currently return.
  **STATUS: PARTIAL IMPORT LANDED 2026-05-31.** Source:
  `/data/dev/enb-emu-data-reconstruct-backup/db/json/cat*_lvl*.jsonl`
  (wiki-scraped). Same importer / output / gate as Y1. Loaded:
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
  **STATUS: BLOCKED -- wiki scrape has no `mission_XML`; runtime is
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

## Finding 2026-06-01: the wiki reconstruct backup is NOT a runtime-import source

The user pointed at `/data/dev/enb-emu-data-reconstruct-backup/db`
(items/missions/mobs/npcs/prospecting/sectors) and granted blanket
permission to "do all the import phases." Investigation
(`tools/dataimport-jsonl/analyze_wiki_coverage.py`, reproducible against
the dev Postgres) shows that for **every** category except NPCs (Y1) and
items (Y5, which had real integer itemIds and dedup), the authoritative
runtime tables are **already fully populated** and the wiki JSONL is a
name-level near-duplicate in an **incompatible form**:

| category | runtime table | runtime rows | wiki overlap by name | why wiki can't be injected |
|---|---|---|---|---|
| sectors/navs | `sector_objects` | 9382 | 85% of 2286 | /1000 coords, no asset ids |
| mobs | `mob_base` | 2042 | 77% of 1127 | no asset/faction/AI, "?" stats |
| missions | `missions` | 364 (all w/ XML) | 25% of 588 | no `mission_XML` |
| item drops | `mob_items` | 8583 | 77% of 933 mob refs | drop rates mostly "?" |
| prospecting | `sector_objects_harvestable` | 882 | n/a | no `resource_id` linkage |

The decisive proof: the wiki `sectors/json/ABA.jsonl` nav "Sado Pit" at
map coords (-152.74, -63.89) is the same authoritative runtime nav "Sado
Pit" in sector 1076 at (-152740, -63860) -- identical object, runtime
coords == wiki coords * 1000, already present with a real `base_asset_id`.

**This is exactly what the Phase Y process gate already requires**: a
PRIMARY source (MySQL/Net-7 server dump, packet capture, or first-hand
doc), NOT a wiki scrape. The Y1 note itself flags the JSONL as "wiki-
scraped, NOT a Net-7 server dump". Injecting it into the runtime tables
would create wrong-position/asset-less duplicate objects and non-runnable
missions -- divergence the CLAUDE.md server-integrity rules forbid, with
no primary-source escape hatch available.

**Decision (autonomous, overnight):** do NOT inject any of these five
categories into the runtime tables. The data is already preserved as a
structured JSONL archive in the reconstruct backup. If the user later
wants it queryable in Postgres, the right shape is opt-in `wiki_*`
REFERENCE tables the server never reads (no fidelity risk) -- but that is
preservation-only with no current consumer (the CLI is a network client,
not a DB client), so it is deferred pending an explicit "yes, build the
reference tables even though nothing reads them yet" rather than built
unsupervised. NPCs (Y1) and items (Y5) remain the only wiki categories
that were safely importable, because they carried real ids and deduped
against the authoritative roster.

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
