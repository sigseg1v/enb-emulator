# Phase AX -- Captured object import into the sector DB (per-sector SQL)

Owner ask (2026-06-23): build a skill + scripts that read the accurate packet
captures (the survey corpus from Phase AW), work out which objects are where in
each sector, de-duplicate them, and emit per-sector SQL that imports prospecting
resources, mobs, and missing navs. **These captures are accurate and take
priority over the current emulator data** wherever the two disagree. (We do NOT
disclose where the captures come from -- only that they are accurate captures
that take priority.)

Skill lives at `.claude/skills/import-captures/` (Freya/MIT). Pure offline data
tooling: it never touches the server, proxy, or client. Captures are NEVER
committed -- `.env` and any local captures are gitignored.

## Owner rules (verbatim intent, all baked into the pipeline)

- Read the pcap, compute which objects are where, **dedup on GID** (same thing
  seen twice = one object).
- **Ignore "remove" opcodes** -- we fly around and objects go in/out of range;
  collect all objects as they come into range and keep their LATEST position.
- Import positions + data for **prospecting resources** (asteroids, clouds, ...)
  and **mobs**.
- **Do NOT import players. Do NOT import enemy corpses or floating temp loot**
  (tractor-in drops).
- **Navs: import only if the nav does not already exist** in the sector.
- For **mobs and resources**, captures are source of truth: if other data exists
  nearby (within 5k) of the SAME specific object, remove just that and replace
  with the captured info. No duplicate data at the end.
- One SQL file per sector (or multiple per sector if needed); no duplicates at
  the end.
- **GREAT CARE on sector assignment**: captures start in the PREVIOUS sector and
  gate in; the data we collect is AFTER the gate. Never write data to the wrong
  sector.
- Stations MAY be updated only if we have FULL data with no missing fields
  (otherwise we would break them).

## The pipeline (`.claude/skills/import-captures/scripts/`)

1. **decode.sh** -- runs the `tools/pcap-inventory` decoder `--json` over every
   capture into `WORK/objjson/<capture>.json` (full per-object list: frame,
   create-type, position, name, level, asset, flags + in-stream sector markers).
2. **aggregate.py** -- one de-duplicated object list PER SECTOR
   (`WORK/sectors/<id>.json`):
   - **Sector assignment**: an object's sector is the last in-stream `0x003A`
     handoff marker BEFORE its frame (never the filename). Markers whose id is
     not a real sector are dropped; instanced sub-sector ids (`realid*10+1`,
     e.g. `40151`) fold to the real id; both validated against the `sectors`
     table. A marker-LESS capture has no gate crossing in it, so it is a single
     sector resolved from its filename prefix (validated against the DB);
     gate-named prefixes that are not sectors resolve to nothing and are dropped.
   - **Dedup**: key `(sector, gid)`. Same object across frames/captures kept
     once, LATEST position wins (captures ordered by timestamp), non-null fields
     merged. Removal opcodes ignored upstream (decoder only tracks creates).
   - **Exclusions** (counted): players (`gid >= 0x40000000` or `isAvatar`),
     loot/corpses/decorative objects, no-position objects.
3. **gen_sql.py** -- writes per-sector SQL + the wrapper (policy below).

`import.sh` orchestrates decode -> aggregate -> gen_sql; `--apply` pipes each
per-sector file directly into psql.

## Import policy

| Object | What happens |
|---|---|
| **Resources** | Source of truth. Each captured rock = exact-position single-rock harvestable (res_count=1, spawn_radius=0). Any EXISTING harvestable within 5k of the SAME ore type removed first (only that specific object). |
| **Mobs** | Source of truth. Captured mob inserted as a 1-mob spawn at exact position. Any EXISTING spawn within 5k of the SAME `base_asset_id` removed first. Name (+asset/level) resolves to a `mob_base` template by exact match, else a NEW template is SYNTHESIZED by cloning the nearest-level same-asset row (mob_id >= 900000). No mob is dropped. |
| **Navs** | Insert only if a nav of that name does not already exist in the sector. Existing navs never touched. |
| **Stations / gates / planets** | **Report-only.** Captures lack their child-row data (dock / cap-ship / stargate routing) so creating them would break them. |

## Duplicate prevention / idempotency

- Synthetic `sector_object_id`s start at `1000000` (above max existing id + the
  Phase Y synth range), assigned deterministically by `(sector_id, gid)`.
- A global `_purge.sql` clears the WHOLE synth id range (children before parents)
  and runs FIRST, before `_mob_templates.sql` and the per-sector files. The
  per-sector self-delete is sector-scoped, so a full re-apply over a DIFFERENT
  prior id mapping left an old row squatting on an id an earlier sector block
  tried to insert -> `ON CONFLICT DO NOTHING` silently dropped it (246 rows lost
  before the purge existed). The up-front global purge makes every re-apply clean.
- Each per-sector file ALSO deletes its own prior synth rows (kept for single-file
  manual apply) -> re-apply = clean replace.
- 5k-replacement deletes explicit existing-id lists computed against the live DB
  at generation time (can never over-delete; worst case no-op). **REGENERATE ONLY
  AGAINST A PRISTINE BASE DB** (base + Phase Y, `seed_captures` NOT applied): the
  replace blocks delete base rows the import itself removes, so regenerating
  against a captures-applied DB makes them come back EMPTY and the duplicates
  resurrect. `gen_sql.py` reads the committed replace-target ids and REFUSES if
  any are already deleted. Pristine DB: `docker compose down -v` then
  `ENB_SKIP_CAPTURE_SEED=1 docker compose up -d schema-init`.
- `schema-init` run order: schema -> base seeds (incl. Phase Y) ->
  `seed_captures.sql` (gated, runs once; skipped when `ENB_SKIP_CAPTURE_SEED=1`)
  -> orphan-spawn cleanup -> `sync_sequences.sql` (last). docker-compose.yml
  carries the gated apply block.

## Items

- [x] **AX-1** decoder `--json` mode (markers + per-object fields) in
  `tools/pcap-inventory/Program.cs` (uncommitted).
- [x] **AX-2** skill scaffold: `SKILL.md`, `.env.example`, `.gitignore`, `.env`
  (gitignored, machine-local), `lib.sh`.
- [x] **AX-3** `decode.sh` -- decode every capture to JSON (skip up-to-date).
- [x] **AX-4** `aggregate.py` -- per-sector dedup + DB-authoritative sector
  assignment + exclusions.
- [x] **AX-5** `gen_sql.py` -- per-sector SQL (nav-if-absent, resource/mob
  delete-within-5k-then-insert, report-only stations/gates/planets).
- [x] **AX-5b** mob `mob_base` synthesis (owner ask 2026-06-27: "for missing mob
  base it should be there ... fill all rows"). Previously ~44 captured mob
  instances across 8 names were skipped because no exact `mob_base` name match
  existed (Craxel, Resource Hound/Hunter, The Wrangler, Rahu the Cultivator,
  Starbase Guardian Turret, Hadean Hijacker, Bardon Nesrith). Verified the names
  are fully parsed (not truncated) and confirmed real via the mediawiki enemy
  scrape at `/data/dev/net7-db-scraper`. Fix: captured name -> exact `mob_base`
  match, else SYNTHESIZE a clone of the nearest-level same-asset template
  (`base_asset_id` is the model; `mob_base` has no hull/shield cols so a clone is
  complete + valid -- no Default-mob crash since the row exists). Synth templates
  `mob_id >= 900000` in `_mob_templates.sql` (loads first), deduped across sectors
  by (name,asset,level). Mob 5k-replacement switched from template-match to
  asset-match (consistent with resource ore-match). Result: 0 unresolved mobs,
  all 8 names now spawn (43 spawn rows), 0 orphan `mob_spawn_group`, idempotent.
  The decomp at `/data/dev/enb-emulator-decomp` was NOT needed (asset clone is
  sufficient).
- [x] **AX-6** `import.sh` orchestrator (+ `--apply`).
- [x] **AX-7** generated artifacts: `db/postgres/capture_import/<id>_<name>.sql`
  (12 sectors, 1247 objects) + `db/postgres/capture_import/_mob_templates.sql`
  (10 synth `mob_base` clones) + `db/postgres/seed_captures.sql` wrapper
  (templates `\ir`'d first).
- [x] **AX-8** gated `schema-init` wiring in docker-compose.yml (applies the
  wrapper once on a fresh boot, after base seeds).
- [x] **AX-9** validated end-to-end: all 12 files + `_mob_templates.sql` execute
  clean + idempotently; `\ir` wrapper applied to dev DB; loader join works; zero
  orphan `mob_spawn_group` rows (every spawn resolves to a `mob_base`, real or
  synth); 10 synth templates, 1247 objects, 43 synth spawn rows; second apply is a
  no-op (templates=10, objs=1247, synth_spawns=43, orphans=0).
- [x] **AX-10** commit (decoder + skill + generated SQL + docker-compose +
  plans). Captures stay OUT of git.
- [x] **AX-11** `_purge.sql` global synth-range purge (runs first in the wrapper
  and in `import.sh --apply`). Fixes 246 silently-dropped synth rows on a
  cross-mapping re-apply (per-sector self-deletes are sector-scoped; an old row
  squatting on a reassigned id blocked the insert via ON CONFLICT DO NOTHING).
- [x] **AX-12** replace-block determinism fix + pristine-DB guard. HEAD
  (f42fe3cf) shipped a latent bug: its per-sector files had ZERO 5k-replace
  blocks, so seed_captures left ~25 base-seed duplicate mobs/resources in 8
  sectors (e.g. base "Scuttle Pupa Spawn"/"Relentless Drone Spawn" coexisting
  with the captured same-asset spawn). Root cause: the replace blocks key on base
  rows the import itself deletes, so regenerating against a captures-applied DB
  produces empty replaces. Fixes: (1) `gen_sql.py` reads the committed
  replace-target ids and REFUSES to regenerate if any are already deleted
  (non-pristine DB); (2) new `ENB_SKIP_CAPTURE_SEED=1` schema-init escape hatch
  brings up a pristine base+Phase-Y DB so the skill can recompute the replaces.
  Regenerated against pristine -> 25 replace blocks across 8 sectors restored
  (1020 +2, 1910 +1, 1920 +4, 1925 +4, 2005 +5, 4025 +2, 4515 +4, 4520 +3).
  Verified: fresh normal boot now removes all base dups, 1247 synth objects, 10
  synth templates, 0 orphans, no schema-init errors; guard refuses on a
  non-pristine DB with the replace blocks intact.
- [x] **AX-13** mob 5k-replacement keyed on the RESOLVED template asset, not the
  raw captured asset. In-game/DB verification (1020 High Earth) found ONE
  surviving duplicate the AX-12 replaces missed: base spawn 4728 "Scuttle Larva
  Spawn" (mob_id 1253, model asset 1180) sat 369u from the 4 captured "Scuttle
  Larva". The capture records that creature's model as asset 1050, but our
  `mob_base` carries 1180; `resolve_mob` matches the template by NAME, so keying
  `near_ids` on the raw captured asset (1050) never matched base 4728's model
  (1180) and the co-located base spawn survived. Siblings Scuttle Pupa
  (asset 1181) / Relentless Drone (asset 1456) replaced fine because their
  captured asset agreed with the DB. Fix: new `template_asset(mob_id, captured)`
  returns the resolved template's `mob_base` asset (real template) or the
  captured asset (synthesized clone, absent from `mob_asset`); the mob loop keys
  replacement on it. Regenerated against pristine -> only 1020 changed
  (replace 2 -> 3, +4728); all other sectors byte-identical. Post-apply
  same-mob_id base-vs-synth dupe query returns ZERO across all 12 sectors;
  base 4728 gone; 1247 synth objects intact. Resource side cross-checked too
  (base harvestable within 5k of a synth harvestable sharing an ore type):
  ZERO across all 12 sectors -- the ore-type replace path was already correct,
  but verified rather than assumed. 4727 (a distinct larva spawn 5994u away,
  just outside the 5k radius) correctly RETAINED -- the fix is precise, not
  over-aggressive. CI green for 2bd2a770. In-game (CLI client): undock->space
  works and the server renders captured mob spawns into scanner range with
  unique names; exhaustive per-nav flight across all 12 sectors was NOT done
  (single-client CLI move/warp primitives make it impractical and it would only
  weakly sample what the DB cross-check proves exactly).
- [x] **AX-14** nav misattribution fix: navs are now resolved AUTHORITATIVELY from
  `docs/sectors/json/*.jsonl`, not the capture's in-stream sector marker. Root
  cause: the old `(sector, gid)` dedup key let the SAME nav live in two sectors
  when one gid appeared in two captures under conflicting markers -- 19 cross-sector
  duplicate nav names (audited). Reused gids across captures (gid 100017 =
  "Nav Arduinne 2" in one capture, a different nav in another) also meant a global
  gid-only key would wrongly collapse unrelated objects. Fix in `aggregate.py`:
  (1) a nav's sector is the jsonl-listed sector for its alphanumeric-normalized
  name; a nav absent from every jsonl is DROPPED (61 such junk/variant navs
  excluded as `nav_not_in_jsonl`); (2) two-level dedup -- collapse by gid WITHIN a
  capture (session-stable, keeps the higher-confidence sector across a relabel
  boundary), then globally key navs on `(sector, normalized-name)` and
  mobs/resources on `(sector, gid)`. `gen_sql.py` gained a defensive jsonl
  membership gate on nav emission (`n_nav_notjsonl`; never fires now that aggregate
  enforces it). The constraint is documented in `SKILL.md` (pipeline step 2, Navs
  policy row, Rules-baked-in). Verified post-apply against pristine: 161 imported
  navs, ZERO violate jsonl membership, ZERO cross-sector imported-nav dups; the one
  shared name "Arena Entry Point" is genuinely in both Slayton and Glenn jsonl
  (correct ground truth, not a leak).
- [x] **AX-15** mob/resource misattribution fix (same captures, same belief).
  "If a nav was misattributed by believing the wrong sector, the mobs were too."
  `aggregate.py` now relabels a whole marker-segment to the jsonl-corroborated
  sector when the marker has ZERO nav support and another sector dominates
  (>=3 navs, >3x the marker). Four segments relabeled: the Glenn capture's
  instanced segment (marked New Edinburgh, 27 Glenn navs) and three
  New-Edinburgh/Inverness captures mis-marked as the gated-from sector. The
  conservative threshold leaves legitimately-mixed gate segments (e.g. the
  Menorb<->Ishuan capture, where the marker has its own nav support) on their
  marker. Mob/resource totals restored to full scale (381 mobs / 839 resources);
  regenerated + applied clean (1244 synth rows, no SQL errors).

- [x] **AX-16** baseline guardian turrets on every gate + starbase. The captures
  show guardian turrets ringing gates/starbases (asset 14, level 66, template
  `mob_base` 573), but a single fly-through only catches the 1-3 nearest the path,
  and only in flown sectors. New `gen_turrets.py` extrapolates the captured ring to
  the whole galaxy as BASELINE content (owner: "don't consider these extra mods,
  build them in as baseline"). It reads every `sector_objects.type IN (11,12)` with
  a position and rings each with evenly-spaced turrets (3/gate, 4/starbase) at a 3D
  distance (~1980) inside the captured 1428-2500 band, elevated above the anchor
  plane. Each is the standard 4-row mob spawn (parent + `sector_objects_mob` +
  `mob_spawn_group` + `sector_nav_points`) backed by real `mob_base` 573; the
  display name lives on `sector_objects.name` so one template backs both
  "Gate"/"Starbase Guardian Turret". Synthetic ids in their own range
  (`>= 2000000`), self-deleting first so re-apply is a clean replace; generator
  aborts if template 573 is missing (Default-mob crash guard). Output committed as
  `db/postgres/seed_turrets.sql`, wired into `schema-init` UNCONDITIONALLY (after
  the capture block, before orphan cleanup + `sync_sequences`), independent of
  `ENB_SKIP_CAPTURE_SEED`. Documented in `SKILL.md`; `import.sh` runs
  `gen_turrets.py` after `gen_sql.py`.
- [x] **AX-16b** turret shape fix -- the first cut was INVISIBLE in-client.
  Reported live: no turrets at the High Earth Tau Ceti gate. Root cause: the
  generator modelled turrets as type-0 mob SPAWNS (`sector_objects.type=0`,
  `base_asset_id=0`, + `sector_objects_mob` + `mob_spawn_group` -> template 573).
  The server (`SectorContentSQL.cpp`) loads type 0 as `OT_MOBSPAWN`, and even the
  turret branch reads the MODEL straight from `sector_objects.base_asset_id` --
  which was 0, so no model rendered. (The `turret_mob_id` promotion path the
  server also checks does not apply: that column does not exist in our Postgres
  schema.) The base data's real turrets are **type 42** with **base_asset_id 14**
  (the guardian-turret model), `scale 1`, one `sector_nav_points` row, and NO
  mob/spawn rows (e.g. sector_object 1733). Rewrote `gen_turrets.py` to emit that
  exact shape, dropped the `mob_base` 573 dependency, and added gap-fill dedup:
  skip any gate/starbase that already has a real type-42 turret within 6k so the
  ring never doubles the authentic placements. Regenerated: 716 turrets (168
  gates x3 + 53 starbases x4), 87 anchors skipped. Applied clean to the live DB.
  NOTE: a running sector thread loads objects once on cold-start, so a live apply
  only appears after the sector reloads (re-enter / server restart); a fresh boot
  has them immediately. Real-client visibility tracked as a CV check (the new
  shape matches the existing in-game turrets, which render).
- [x] **AX-17** imported mob/resource radar signature fix. Two related defects in
  `gen_sql.py`: (1) imported mobs got a `sector_nav_points` row with
  `signature = 1000`, well below the dominant base-data value of 7000 (1220/1372
  mobs), so imports were under-detectable; (2) imported RESOURCES got NO
  `sector_nav_points` row at all, so their signature defaulted to 0 (the server
  LEFT-JOINs that table onto every object and reads `signature` as the radar
  detection radius -- `SectorContentSQL::ProcessDefaultObjectStats` ->
  `ObjectManager`). Added `BASE_SIGNATURE = 7000.0`; mobs now use it and
  resources get their own `nav_type 0` nav-point row at 7000. Regenerated all 11
  per-sector files against a pristine throwaway DB (381 mob nav-points re-emitted
  at 7000, 839 new resource nav-points added; verified all 1220 import nav-points
  now read 7000, no schema.sql drift). Applied surgically to the live dev DB
  (UPDATE 381 + INSERT 839). `SKILL.md` import-policy section documents the
  nav-point/signature contract. DB content only -- no wire/server change, no
  `plans/29` CV entry.
- [x] **AX-18** imported-resource ship-model fix (owner-reported: "all these
  asteroids you generated in Inverness have the model of a ship"). Root cause:
  `emit_sector_object` hard-coded `base_asset_id = 0` for every row. A type-38
  resource is rendered straight from `base_asset_id`, and asset 0 is the "Old Old
  Terran Fighter" ship -- so all 839 imported rocks/gas/gems drew as ships. (Mob
  spawns were unaffected: a type-0 spawn takes its model from its `mob_base`
  template, not `base_asset_id`.) Fix: `emit_sector_object` takes a `base_asset`
  arg; resources now pass the captured asteroid/gas/hulk model (1822..1834 etc.,
  same value already written into `..._restypes.type`). Also added a guard --
  `DB.resource_assets` (assets `main_cat IN ('Asteroids','Hulks')`); a capture
  whose `baseAsset` is not such a model is a mis-tagged loot/turret/derelict and
  is now DROPPED rather than spawned (3 dropped: Monster Brain 1321, Terran
  Missile Turret 14, Derelict Alien Ship 1271 -> resource total 839 -> 836).
  Regenerated all per-sector files against a pristine throwaway DB and applied the
  corrected wrapper to the live dev DB (verified: 0 rows with base_asset 0 in the
  capture range, 836 resources now carry models 1822..1834/hulk, 0 junk assets).
  `SKILL.md` Resources policy updated. DB content only -- no wire/server change,
  no `plans/29` CV entry. NOTE: the server loads sector content at sector boot, so
  a player already in a fixed sector must re-enter it (gate out/in or relog) to
  see the corrected models.

- [x] **AX-19** pre-first-marker object recovery (owner: my earlier "that data
  is unrecoverable without a re-fly" claim was WRONG -- "bullshit... you are just
  lazy"). A capture opens mid-flight in the sector we were already in, so objects
  arriving in range before the first in-stream handoff were being DROPPED
  (`before_first_marker`, ~469-706 objects across the corpus). They are fully
  attributable without any re-fly: (a) navs already resolve by NAME via the jsonl
  independent of markers; (b) a pre-marker mob/resource segment inherits the END
  sector of the immediately-preceding capture in the same play session (the sector
  we were flying when this capture began), chained only within `SESSION_GAP_SECS`
  (30 min) by the `...T......Z` filename timestamps, then run through the SAME
  nav-corroboration relabel as any other segment; (c) a pre-marker segment with no
  navs AND no same-session predecessor still drops (attributing it would invent a
  location). `aggregate.py` implements the chain + `cap_epoch()` timestamp parse.
  Validated: Swooping Eagle 052139Z gates SwoopingEagle->Ishuan mid-capture; its
  133 pre-marker resources correctly attribute to SwoopingEagle via the chain from
  predecessor 051559Z (not to Ishuan); Freya pre-marker objects correctly relabel
  to Ishuan (8 nav votes). Net effect: 10 per-sector files regenerated, import
  total 1868 -> 2045 sector_objects (+177: +152 harvestable, +25 mob spawns),
  Swooping Eagle 54 -> 212. Regenerated against an isolated throwaway pristine base
  (`-p freya-import`, `ENB_SKIP_CAPTURE_SEED=1`, host port 5459) and applied the 10
  files + `_purge`/`_mob_templates` directly to the live dev DB with
  `ON_ERROR_STOP=1` (every file exited 0, live counts match the throwaway regen
  exactly: 2045/1396/623). `SKILL.md` sector-assignment section updated. DB content
  + skill tooling only -- no wire/server/proxy change, no `plans/29` CV entry.

- [x] **AX-20** finish the AX-19 recovery properly (owner: my "244 unrecoverable"
  claim was still "bullshit... lazy" -- partition every capture by gate and
  attribute EVERY segment). Two fixes to the aggregate/gen_sql pipeline:
  - **Pre-marker OWN-nav anchoring.** AX-19 only anchored a pre-first-marker
    mob/resource window when a same-session predecessor existed (the chain). But
    the pre-marker window's OWN navs resolve by name independent of any marker, so
    they identify the starting sector directly even with no chained predecessor.
    New `premarker_nav_sector()` votes the pre-window's resolvable nav names and
    anchors the window to the dominant sector when `top_votes >= 3` and it
    outvotes the rest combined. Recovers **134 of the 244** before-first-marker
    drops: Freya 192742Z's 89 pre-marker objects -> Ishuan (8 nav votes vs 1/1/1),
    SwoopingEagle 180612Z's 45 -> Swooping Eagle (29 votes, unanimous). The
    remaining **110 are genuinely anchorless**: two Yokan captures (61 + 41) whose
    pre-marker windows contain ZERO navs and have no same-session predecessor, plus
    8 lone singletons likewise nav-less -- attributing them WOULD be inventing a
    location, so they still drop. This is now a proven floor, not a lazy default.
  - **jsonl filename->sector alias.** `load_nav_jsonl` matched a nav catalog file
    to a sector by normalizing its basename against the DB sector name, so
    abbreviations (`ABA`->Asteroid Belt Alpha, `ABB`->...Beta, `ABG`->...Gamma) and
    divergent spellings (`Pluto`->Pluto and Charon, `Ceres`->Ceres/Thule,
    `Nifleheim`->Nifleheim Cloud, `Todesengel`->der Todesengel, ...) were silently
    unmatched -- those sectors' navs never loaded, so nav-corroboration could not
    relabel their segments. Added a module-level `JSONL_SECTOR_ALIAS` map
    (single source of truth) consulted on match miss, imported into `gen_sql.py`
    (which carried its own buggy copy of the matcher) to prevent drift.
    `nav_not_in_jsonl` 181 -> 128. Consequence: the `ABA__...154834Z` capture is a
    GM-`/wormhole`-noisy belt survey whose spurious in-stream markers
    (Ganymede/Ishuan/Jupiter/Saturn/Akerons/Pluto/Uranus/Neptune...) were the ONLY
    reason Jupiter/Saturn/Akerons ever got captured objects. With the belt nav
    catalogs now loaded, nav-corroboration correctly re-attributes those segments
    to Asteroid Belt Alpha and Beta (the three belt catalogs share ZERO nav names,
    so the vote is unambiguous). Net: `1070_Jupiter.sql`, `1071_Saturn.sql`,
    `1075_Akerons_Gate.sql` DELETED (their only rows were the misattribution), new
    `1077_Asteroid_Belt_Beta.sql` added, 1076 (ABA) updated.
  - **Reconciled against the live DB after `down -v` + pristine-base regen + apply**
    (2029 synth `sector_objects`: 1374 resources, 628 mobs, 27 net-new navs, 17
    clone templates): mobs aggregate 628 == live 628 exactly; resources aggregate
    1609 minus the AX-18 non-Asteroid/Hulk asset drops == 1374 live harvestable;
    navs insert-if-absent so only 27 are net-new over the base seeds. 20 per-sector
    files touched (16 modified, 3 deleted, 1 new) + wrapper. DB content + skill
    tooling only -- no wire/server/proxy change, no `plans/29` CV entry.

## Notes

- Mobs are NO LONGER skipped. The 8 names previously absent from `mob_base`
  (Craxel, Resource Hound, Resource Hunter, The Wrangler, Rahu the Cultivator,
  Starbase Guardian Turret, Hadean Hijacker, Bardon Nesrith) now import via
  synthesized clone templates (mob_id >= 900000) -- see AX-5b. The clone is a
  COMPLETE same-asset `mob_base` row, so it does NOT hit the Default-mob crash
  (that comes from a missing/incomplete template, not a present one).
- Final aggregate exclusions (after AX-20 recovery): before_first_marker 110
  (was 469-706 pre-AX-19, 244 after AX-19, now 110 after AX-20 own-nav anchoring --
  these remaining are nav-LESS pre-marker segments with no same-session
  predecessor: two Yokan captures with 0 navs, 61 + 41 objects, plus 8 nav-less
  singletons; attributing them would be guessing a location), player 47,
  capture_unresolved_no_marker 4, drop_loot_or_deco 263, no_position 1.
- No wire change, no server/proxy/login change -> no `plans/29` CV entry. This is
  DB content tooling only.
- The import was applied to the running DEV DB during AX-9 validation (idempotent;
  reproduced by schema-init on a fresh boot). `docker compose down -v` + reboot
  reverts it.
