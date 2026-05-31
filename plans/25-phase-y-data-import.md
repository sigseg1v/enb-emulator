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

- [ ] **Y4: Sector map / system topology import** -- populate
  `sector`, `sector_link`, `system`, `nav_object`, `gate` and the
  associated coordinates / faction-region tables so warp routes and
  the in-game galaxy map match retail. Closes the `m_ObjectMgr->SendAllNavs`
  curatorial gap on the SectorLogin (space-arm) path -- not the
  station path -- but is a prerequisite for any post-Phase-K cross-
  sector navigation testing.
  **STATUS: AWAITING USER APPROVAL + REFERENCE DATA.**

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

- [ ] **Y6: Mission / quest catalog import** -- populate
  `mission`, `mission_step`, `mission_dialog`, `mission_npc_link`
  tables so mission terminals (`0x0011` `Hijackee` interactions,
  job-terminal flows) return real missions instead of empty lists.
  Acceptance: the in-game job terminal in any starbase returns at
  least one available mission for a fresh character at level-
  appropriate range.
  **STATUS: AWAITING USER APPROVAL + REFERENCE DATA.**

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
