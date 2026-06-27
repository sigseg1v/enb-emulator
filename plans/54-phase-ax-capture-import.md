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

## Notes

- Mobs are NO LONGER skipped. The 8 names previously absent from `mob_base`
  (Craxel, Resource Hound, Resource Hunter, The Wrangler, Rahu the Cultivator,
  Starbase Guardian Turret, Hadean Hijacker, Bardon Nesrith) now import via
  synthesized clone templates (mob_id >= 900000) -- see AX-5b. The clone is a
  COMPLETE same-asset `mob_base` row, so it does NOT hit the Default-mob crash
  (that comes from a missing/incomplete template, not a present one).
- Final aggregate exclusions: before_first_marker 469, player 47,
  capture_unresolved_no_marker 4, drop_loot_or_deco 263, no_position 1.
- No wire change, no server/proxy/login change -> no `plans/29` CV entry. This is
  DB content tooling only.
- The import was applied to the running DEV DB during AX-9 validation (idempotent;
  reproduced by schema-init on a fresh boot). `docker compose down -v` + reboot
  reverts it.
