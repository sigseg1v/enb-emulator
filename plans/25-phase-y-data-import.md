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
  same day.** Source: the external reconstruct dataset (`db/npcs/npcs.jsonl`)
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
  **STATUS: AWAITING USER APPROVAL + REFERENCE DATA.** A full file
  inventory of the reconstruct backup (2026-06-02) confirms it carries
  NO skills data file (only items/missions/mobs/npcs/prospecting/sectors
  + html/mediawiki source). So this is not a gap the current dataset can
  close -- it genuinely needs a user-supplied skill table.

- [ ] **Y3: Buff / item-effect definitions import** -- populate the
  `item_effect_data` / buff tables that drive
  `ItemBase::EquipEffect()` lookups in `server/src/Equipable.cpp:1357`.
  Closes the gap that makes our fresh-character `m_Effects.SendEffects`
  iterate an empty list while retail emits one or more `0x0009`
  ObjectEffect frames per persistent-buff slot.
  **STATUS: AWAITING USER APPROVAL + REFERENCE DATA.** Same as Y2: the
  2026-06-02 file inventory of the reconstruct backup shows no buff /
  item-effect data file present, so the current dataset cannot close
  this gap -- it needs a user-supplied effect table.

- [x] **Y4: Sector navigation-marker import** -- add the
  nav-point / hidden-nav-point markers the runtime `sector_objects`
  table is genuinely missing, after a by-name + by-position diff of the
  reconstruct dataset against the authoritative runtime. Closes part of
  the `m_ObjectMgr->SendAllNavs` curatorial gap on the SectorLogin
  (space-arm) path.
  **STATUS: IMPORT LANDED 2026-06-02. 48 markers.** Source:
  the external reconstruct dataset (`db/sectors/json/*.jsonl`).
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
  the external reconstruct dataset (`db/json/cat*_lvl*.jsonl`)
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
  **STATUS: BLOCKED -- diffed 2026-06-02, no importable row.** The diff
  WAS done this time (per the Y4 lesson), not rejected on percentages:
  the dataset `missions/missions.jsonl` has **587** distinct mission
  names; **430 are genuinely absent** from the runtime `missions` by
  name (only 157 / 26% overlap). So the gap is real and large -- but it
  is **not importable**, for a reason specific to missions and absent
  for navs: the server *executes* a mission by running its
  `mission_XML` (a `<Mission><Stage><Condition><Tree><Branch>` script
  with completion triggers + integer `mission_key`/`mission_type`). All
  364 runtime missions carry that XML; every one of the 430 absent
  dataset entries is walkthrough **prose** (giver NPC, prerequisite
  list, "reward 5000 explore XP") with NO XML, no key, no stage/
  condition/dialog structure. A row with empty `mission_XML` would make
  the job terminal advertise a mission the server cannot run (player
  accepts, nothing fires) -- a broken/divergent object the
  server-integrity rules forbid, and the XML CANNOT be synthesized from
  prose. There is also no empty column on the 157 name-matches to
  enrich: the runtime XML already carries giver + reward, and
  mission_id/name/key/type/minSecurityLevel are all populated. Contrast
  with Y4: a nav is fully functional from name + position + per-sector
  field derivation; a mission is not functional without its script.
  Real unblock needs a Net-7 mission-XML dump.
  (Note: the plan's `mission_step`/`mission_dialog`/`mission_npc_link`
  tables do not exist in `db/postgres/schema.sql`; the runtime keeps the
  whole mission in `missions.mission_XML`.)

- [!] **Y7: Mob catalog import** -- (`mobs/mobs.jsonl`).
  **BLOCKED -- diffed 2026-06-02, no importable row.** The runtime
  `mob_base` holds **2042** mob templates with real `base_asset_id`,
  `faction_id`, and AI scripts; `mob_items` holds 8583 loot rows;
  `sector_objects_mob` holds 1385 spawn rows. The diff: 1113 dataset
  mob names, **242 absent** from `mob_base` by name (21%) -- but **0 of
  the 242 carry an asset or template id**, and a `mob_base` row without
  `base_asset_id` cannot be spawned or rendered (non-functional). Many
  absent entries are placeholders (e.g. one is literally named
  `''pls update when known''`, stats=null, abilities=null). Nothing to
  enrich on the matches either (asset/faction/AI already populated).
  Real unblock needs a Net-7 mob dump with the integer asset/faction/AI.

- [x] **Y8: Item-drop table import** -- (`mobs/mobs.jsonl` +
  `items/item_drops.jsonl`). **IMPORTER BUILT 2026-06-02; 2582 mob_items
  rows generated. The earlier "BLOCKED -- no importable row" verdict
  below was WRONG and is superseded.** The prior pass declared the
  dataset non-importable because **0 of 2415** dropSource rows carry a
  numeric `drop_chance` -- a field-presence heuristic. That is the exact
  same mistake the Y9 entry and this file's Y4 lesson warn against: it
  measured a *field's presence* instead of running the actual *(mob,item)
  link diff* against the runtime. The drop RATE was never the gap; the
  drop LINK is. The owner explicitly approved generating the rate, which
  removes the only real objection.
  - **The diff.** Union the drop links from both sources -- mobs.jsonl
    `drops[].item` (3479) + item_drops.jsonl `dropSources[].mob` (3917)
    = **3706 distinct (mob,item) name pairs**. Resolve each end against
    `mob_base.name` / `item_base.name` (normalized). 3159 resolve on both
    ends; of those **1061 already exist** in `mob_items` (any id-combo)
    and **2089 are a genuine gap**. After excluding 232 links whose mob
    name is a placeholder ("unknown"/"various"/... -- these collide with
    real `mob_base` rows literally named "Unknown") and fanning the 208
    multi-CL-variant mobs across their ids, **2582 mob_items rows** are
    emitted (every gap item name resolves to exactly one item id -- 0
    same-name tiers, so item resolution is unambiguous).
  - **Generated payload.** The 4 payload columns are GENERATED, grounded
    in the runtime distribution of the existing 8450 rows so synth rows
    are shape-identical to the most common real ones: `drop_chance`=10.0
    (a common real value just under the ~13 runtime mean; the loader
    truncates to unsigned char -> stores 10), `usage_chance`=0, `type`=1
    (unused by the server at runtime), `qty`=1 (all modal). A single
    documented constant beats a fabricated per-row formula: there is no
    rarity signal in the source and false precision is worse than honest
    uniformity. Re-tunable in one place in the generator.
  - **Runtime reality (verified, not assumed).** `mob_items` IS loaded
    live: `MOBContent::LoadMOBContent` (`server/src/MOBDatabaseSQL.cpp`,
    `USE_MYSQL_SECTOR` path) runs `SELECT * FROM mob_items` through the
    libpqxx shim into `MOBData::m_Loot` (struct `LootNode`). Two
    consumers: the GM `DisplayLoot` debug print, and the weapon-loadout
    selector (`MOBClass.cpp:281`) which picks the mob's actual weapon/
    ammo/engine from the loot list by `item_base_id` SubCategory. Adding
    equip-category loot to a mob can therefore influence its combat
    loadout -- but the owner confirmed mobs canonically drop their
    weapons + gear, so the entries are content-correct. No husk drop-roll
    keyed on `drop_chance` was found; the rate is currently inert at
    runtime (loaded, printed, not yet rolled).
  - Importer: `tools/dataimport-jsonl/generate_seed_drops.py` ->
    `db/postgres/seed_phase_y_drops.sql` (synth ids >= 100000, gated
    DELETE-then-INSERT, ON CONFLICT DO NOTHING, idempotent). Wired into
    docker-compose schema-init (gated on `mob_items.id=100000`). APPLIED
    + verified live: 2582 rows, all joins resolve. Re-running the
    generator against the post-apply DB emits 0 (gap closed; idempotent).
  - **551 links could NOT be imported** (500 mob name not in `mob_base`,
    30 item name not in `item_base`, 21 neither) -> written to
    `tools/dataimport-jsonl/phase_y8_unresolvable_drops.csv` for the
    owner. These need new mob_base/item_base rows (Y6/Y7 territory),
    deferred to **Y10** (owner-confirmation-gated; do not auto-start).
  - See decisions-log "Phase Y8 UNBLOCKED" for the correction record.

- [x] **Y9: Prospecting / resource import** --
  (`prospecting/prospecting.jsonl`). **IMPORTER BUILT 2026-06-02; 529
  nodes generated. The earlier "BLOCKED -- no importable row" finding
  below was WRONG and is superseded.** The prior pass concluded the
  dataset was non-importable because its rows carry no numeric
  `resource_id`. That reasoning was the same Y4-era mistake the rest of
  this file warns against: it diffed on *ids* instead of *names*. A
  harvestable node is constructable from name + position + per-sector
  mechanics derivation -- exactly the path that made Y4 navs importable.
  Concretely: `sector_objects_harvestable.resource_id` is a
  `sector_objects.sector_object_id` of **type 38** (the field object),
  not a separate key (verified against the server's own join in
  `server/src/SectorContentSQL.cpp`: `sector_objects.sector_object_id =
  sector_objects_harvestable.resource_id`). `field` is a 0-5 field-type
  enum (NOT a nav id). The dataset's `resource` names resolve to ore
  `item_id`s via `base_ore_list`/`item_base` (339/342 = 99%), and its
  `(sector, nav)` pairs resolve to runtime nav-point `sector_objects`
  (93% of triples) which supply the field POSITION + `sector_id`.
  Importer: `tools/dataimport-jsonl/generate_seed_prospecting.py` ->
  `db/postgres/seed_phase_y_prospecting.sql`. Each gap node is emitted
  as a type-38 `sector_objects` row at the matched nav's coords + a
  `sector_objects_harvestable` row (resource_id == node id; res_count
  1-5 per project intent; mechanics columns copied from same-level
  existing fields) + 1-5 `oretypes` rows (resource->item, frequency =
  dataset prob/weight/density apportioned, top-N=res_count) + restypes
  rock-asset rows (copied from a same-level node). Synth ids >= 200000
  (see the wiring note below), gated DELETE-then-INSERT, ON CONFLICT DO
  NOTHING, idempotent. Field-level dedup drops 136 candidates coincident
  with an existing type-38 field. Result: **529 nodes, 2537 oretype
  links, 1339 restype rows.** Honest caveats: oretype `frequency` is
  currently INERT (the server picks ore uniformly --
  `ItemBaseManager.cpp ~244` has a "TODO: add frequency weightings");
  positions are the matched nav-point's, so a field sits exactly on the
  nav rather than at the retail offset; rock-asset `type` ids and the 4
  mechanics columns are same-level copies, not dataset-attested.
  `prospect_fields.jsonl` (145 rows) was inspected but adds nothing the
  per-nav `navDistribution` in prospecting.jsonl does not already carry.

  **WIRED 2026-06-03.** Two bugs were found before wiring and both fixed
  in the generator:
  1. **sector_object_id collided with Y4 navs.** Both Y4 navs (type=37)
     and Y9 fields (type=38) live in `sector_objects` with the same
     single-column PK, and both allocated from 100000 -- Y4 owns
     [100000, 100048), so Y9's first 48 ids would have been silently
     dropped by ON CONFLICT (and their harvestable children mis-attached
     to Y4 nav objects). Fixed: Y9 now allocates from
     `PROSPECTING_ID_BASE = 200000`; read-filters and the type=38-scoped
     DELETE stay at SYNTH_ID_FLOOR=100000 (they correctly span both
     bands). Verified range: sector_object_id 200000..200528, zero ids
     in the Y4 band.
  2. **ON CONFLICT target with no backing constraint.** The
     `sector_objects_harvestable` INSERT used `ON CONFLICT (resource_id)`
     but that table has NO unique/PK constraint, so Postgres rejected the
     whole seed with "no unique or exclusion constraint matching the ON
     CONFLICT specification". Fixed: bare `ON CONFLICT DO NOTHING`
     (idempotency is via the DELETE; degrades to a real guard if a
     unique constraint on resource_id is ever added).
  Verified by applying the regenerated seed in a transaction against the
  live net7 (with real Y4 data present) and rolling back: 529 nodes /
  529 harvestable / 2537 oretypes / 1339 restypes all land, FK-clean, DB
  untouched after rollback. Wired into `docker-compose.yml` schema-init
  after the Y8 block, gated on a Y9-specific canary (type=38
  sector_object at 200000). Applies automatically on the next fresh
  bring-up; row counts to be confirmed there.

- [ ] **Y10: Owner-confirmation-gated import of the unresolvable drop
  links.** **DO NOT AUTO-START -- this phase only runs when the owner
  explicitly populates the missing rows and says go.** The Y8 import
  closed every drop link whose mob AND item already exist in the
  runtime. The remaining **551 links** (in
  `tools/dataimport-jsonl/phase_y8_unresolvable_drops.csv`) reference a
  mob (500), an item (30), or both (21) that the runtime has no row for.
  They cannot be synthesized from positional/name data alone -- they
  need real `mob_base` (Y6/Y7-class) and `item_base` (Y7-class) rows
  with their own asset ids, factions, stats, categories, etc., which
  the reconstruct dataset does not carry in importable form. The CSV is
  the worklist: each row gives `reason`, `mob_name`, `item_name`, and
  whichever end DID resolve (`resolved_mob_ids` / `resolved_item_ids`).
  **Process:** the owner fills in the missing mob/item definitions (or
  confirms each is intentionally out of scope); only then does a
  follow-up importer create those base rows and the drop links. The
  agent ASKS for this input and waits -- it never fabricates mob_base /
  item_base rows on its own (that was the failure mode the original Y8
  "build-then-reject" pass fell into). The CSV's `''pls update when
  known''`-style junk mob names should be dropped, not filled.

## Finding 2026-06-01..02: per-category diff of the reconstruct dataset

The user pointed at the external reconstruct dataset
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

| category | runtime table | runtime rows | absent-by-name | verdict |
|---|---|---|---|---|
| sectors/navs | `sector_objects` | 9382 | -- | **48-marker gap imported (Y4)** after type-filter + name/position dedup |
| npcs | `starbase_npcs` | -- | -- | **156-NPC gap imported (Y1)** after roster dedup |
| items | `item_base` | -- | -- | **5003-row catalog imported (Y5)**, real itemIds |
| mobs | `mob_base` | 2042 | 242 / 1113 | no importable row -- 0 of the 242 carry an asset/template id (unspawnable) |
| missions | `missions` | 364 (all w/ XML) | 430 / 587 | no importable row -- 0 carry `mission_XML` (the executable script; cannot synthesize from prose) |
| item drops | `mob_items` | 8450 | -- | **2582-row gap imported (Y8, 2026-06-02)** -- the "no importable row" verdict was a field-presence mistake (it measured `drop_chance` presence, not the (mob,item) link diff); the drop LINK is the gap, the RATE is owner-approved generated. 551 links whose mob/item lacks a runtime row -> CSV, deferred to Y10 |
| prospecting | `sector_objects_harvestable` | 882 | -- | **529-node gap imported + WIRED (Y9, 2026-06-03)** -- the "no resource_id" verdict was an id-diff mistake; resource+nav NAMES resolve (99%/93%) and a node is constructable from name+position+per-sector mechanics, exactly like Y4 navs. Two pre-wiring bugs fixed: id collision with Y4 (now 200000+) and an ON CONFLICT target with no backing constraint |

**What was importable** (Y1 NPCs, Y4 navs, Y5 items): a real diff
surfaced rows the runtime genuinely lacked, and each carried enough to
build a valid runtime row (NPCs deduped against the roster; navs got
derived per-sector fields; items had real integer itemIds). All three
landed via `generate_seed*.py` -> `db/postgres/seed_phase_y*.sql`,
gated and idempotent, applied by the `schema-init` service.

**What was importable on a second look** (Y8 drops, Y9 prospecting):
both were initially rejected with a *field-presence* heuristic -- "0 of
N rows carry the load-bearing field, therefore nothing is importable" --
and both verdicts were WRONG for the same reason. The load-bearing field
(`drop_chance` for Y8, `resource_id` for Y9) was never the gap. The gap
is the LINK (which mob drops which item; which resource spawns at which
field), and the link resolves by name + position to runtime ids exactly
like Y4 navs. Y8: 2582 (mob,item) links importable, rate owner-approved
generated. Y9: 529 nodes constructable from name + per-sector mechanics,
now generated, verified-by-rollback against live net7, and wired into
schema-init (after fixing an id collision with Y4 and a bad ON CONFLICT
target -- see the Y9 entry above). The lesson, now stated three times in
this file (Y4, Y9, Y8): **run the finest-key diff before declaring "no
gap" -- never substitute a field-presence count for it.**

**What remains NOT importable** (mobs, missions, and the Y8/Y9 tails
whose endpoints don't exist yet): a mob needs `base_asset_id` to be
spawnable (0 of 242 absent mobs have one), a mission needs `mission_XML`
to run (0 of 430 absent missions have it; it cannot be synthesized from
walkthrough prose), and the 551 Y8 drop links + the prospecting tail
that reference a mob/item the runtime has no row for cannot be
materialized without first creating those base rows. Those base rows
need real asset/template ids the reconstruct dataset does not carry, so
they stay blocked pending a Net-7 server dump -- tracked as **Y6/Y7**
(mob/mission catalog) and **Y10** (owner-confirmation-gated drop tail).
Injecting them with fabricated ids would create broken/divergent
objects, which the CLAUDE.md server-integrity rules forbid absent a
primary-source escape hatch. The data remains preserved as the
structured JSONL archive in the reconstruct backup.

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
