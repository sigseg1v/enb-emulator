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
| **Mobs** | Source of truth. Name (+asset/level) resolves to a `mob_base` template; captured mob inserted as a 1-mob spawn at exact position. Any EXISTING spawn within 5k of the SAME template removed first. Unresolved names skipped + reported (cannot synth a mob_base safely). |
| **Navs** | Insert only if a nav of that name does not already exist in the sector. Existing navs never touched. |
| **Stations / gates / planets** | **Report-only.** Captures lack their child-row data (dock / cap-ship / stargate routing) so creating them would break them. |

## Duplicate prevention / idempotency

- Synthetic `sector_object_id`s start at `1000000` (above max existing id + the
  Phase Y synth range), assigned deterministically by `(sector_id, gid)`.
- Each per-sector file first deletes ITS OWN prior synth rows
  (`sector_id = S AND sector_object_id >= 1000000`) -> re-apply = clean replace.
- 5k-replacement deletes explicit existing-id lists computed against the live DB
  at generation time (can never over-delete; worst case no-op). **Regenerate the
  SQL whenever base seeds change**, or those id lists go stale.
- `schema-init` run order: schema -> base seeds (incl. Phase Y) ->
  `seed_captures.sql` (gated, runs once) -> orphan-spawn cleanup ->
  `sync_sequences.sql` (last). docker-compose.yml carries the gated apply block.

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
- [x] **AX-6** `import.sh` orchestrator (+ `--apply`).
- [x] **AX-7** generated artifacts: `db/postgres/capture_import/<id>_<name>.sql`
  (12 sectors, 1204 objects) + `db/postgres/seed_captures.sql` wrapper.
- [x] **AX-8** gated `schema-init` wiring in docker-compose.yml (applies the
  wrapper once on a fresh boot, after base seeds).
- [x] **AX-9** validated end-to-end: all 12 files execute clean + idempotently;
  `\ir` wrapper applied to dev DB (per-sector counts 1020=14, 1910=190,
  1920=204, 1925=183, 2005=222, 2010=8, 4015=70, 4025=97, 4030=7, 4120=65,
  4515=40, 4520=104); loader join works (sector 1925 = 223 rows); zero orphan
  `mob_spawn_group` rows (all templates resolve to `mob_base`).
- [ ] **AX-10** commit (held for owner -- decoder + skill + generated SQL +
  docker-compose). Captures stay OUT of git.

## Notes

- Mob names genuinely absent from `mob_base` (correctly skipped + logged, no
  close variant exists): Craxel, Resource Hound, Resource Hunter, The Wrangler,
  Rahu the Cultivator, Starbase Guardian Turret, Hadean Hijacker, Bardon Nesrith
  (~52 mob instances total). Synthesizing a `mob_base` would risk the documented
  Default-mob crash, so they are deliberately not imported.
- Final aggregate exclusions: before_first_marker 469, player 47,
  capture_unresolved_no_marker 4, drop_loot_or_deco 263, no_position 1.
- No wire change, no server/proxy/login change -> no `plans/29` CV entry. This is
  DB content tooling only.
- The import was applied to the running DEV DB during AX-9 validation (idempotent;
  reproduced by schema-init on a fresh boot). `just nuke-pg` reverts it.
