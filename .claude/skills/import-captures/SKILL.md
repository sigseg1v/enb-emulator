# Import captured object data into the sector DB

Read accurate packet captures, work out which objects are where in each sector,
de-duplicate them, and emit per-sector SQL that imports the prospecting
resources, mobs, and missing navs. **These captures are accurate and take
priority over the current emulator data** wherever the two disagree.

This skill is pure data tooling: it never touches the server, proxy, or client.
It decodes captures offline, aggregates, and writes committed `.sql` files.

## Run it

```bash
# one-time: tell the skill where the captures + DB live
cp .claude/skills/import-captures/.env.example .claude/skills/import-captures/.env
$EDITOR .claude/skills/import-captures/.env        # set ENB_CAPS_DIR

# generate the per-sector SQL (no DB writes):
bash .claude/skills/import-captures/scripts/import.sh

# ... or also apply it to the running dev DB right now:
bash .claude/skills/import-captures/scripts/import.sh --apply
```

The generated artifacts are committed:
`db/postgres/capture_import/<sector_id>_<name>.sql` (one per sector) plus the
`db/postgres/seed_captures.sql` wrapper that `\ir`-includes them. On a fresh
`docker compose` boot, `schema-init` applies the wrapper automatically (gated, so
it runs once); the prerequisite is that the base seeds have already run.

## The pipeline (three stages)

1. **decode.sh** runs the `tools/pcap-inventory` decoder with `--json` over every
   capture into `WORK/objjson/<capture>.json`. Each JSON has the full object list
   (per-object frame, create-type, position, name, level, asset, flags) and the
   in-stream sector-marker list.
2. **aggregate.py** turns those into one de-duplicated object list **per sector**
   (`WORK/sectors/<id>.json`). The hard parts:
   - **Sector assignment.** A capture starts in the PREVIOUS sector and gates into
     the target. An object's sector is the last in-stream sector marker (`0x003A`
     handoff) seen *before* that object was created -- never the filename. Markers
     whose id is not a real sector are dropped; instanced sub-sector ids
     (`realid*10+1`, e.g. `40151`) fold back to the real id; both are validated
     against the `sectors` table.
   - **Dedup.** Key is `(sector, gid)`. The same object seen many times / across
     captures is kept once, with its **latest** position (captures ordered by
     timestamp, newest wins) and non-null fields merged in. Removal opcodes are
     ignored (we collect objects as they come into range), so an object that left
     and re-entered range is still one object.
   - **Exclusions:** players (`gid >= 0x40000000` or `isAvatar`), and anything that
     is not an import target (loot / corpses / decorative objects). All counted.
3. **gen_sql.py** writes the per-sector SQL following the policy below.

## Import policy

| Object | What happens |
|---|---|
| **Resources** (asteroids, clouds, ...) | Source of truth. Each captured rock is inserted as an exact-position single-rock harvestable. Any EXISTING harvestable within 5k whose ore type matches is removed first (only that specific object). |
| **Mobs** | Source of truth. The captured mob is inserted as a 1-mob spawn at its exact position. Any EXISTING mob spawn within 5k of the SAME `base_asset_id` (the physical model at that spot) is removed first. The captured name (+asset/level) resolves to a `mob_base` template: an exact name match is reused; otherwise a NEW template is **synthesized** by cloning the nearest-level same-asset `mob_base` row and overriding name/level/asset, so the captured mob always spawns with the right model and valid stats. (`mob_base` has no hull/shield columns -- those derive from level + modifiers, which the clone inherits.) A mob is skipped only if its asset has no sibling to clone (not observed in the corpus). |
| **Navs** | Insert only if a nav of that name does not already exist in the sector. Existing navs are never touched. |
| **Stations / gates / planets** | **Report-only.** The capture lacks their child-row data (dock / cap-ship / stargate routing), so creating them would break them. We only print what we saw. |

## How duplicates are prevented

- Synthetic `sector_object_id`s start at `1000000` (above the max existing id and
  the Phase Y synth range), assigned deterministically by `(sector_id, gid)`.
- Synthetic `mob_base.mob_id`s (clone templates) start at `900000` (above the max
  real `mob_id`, below the object range). They live in their own
  `capture_import/_mob_templates.sql`, deduped across sectors by
  `(name, asset, level)`, and that file MUST load first (the wrapper and
  `import.sh --apply` both order it ahead of the per-sector spawns). Existing
  synth rows (`id >= 900000` / `>= 1000000`) are excluded when reading the live DB
  for replacement + clone sources, so regeneration is deterministic regardless of
  whether a prior import is already applied.
- Each per-sector file first deletes **its own** prior synthetic rows
  (`sector_id = S AND sector_object_id >= 1000000`), so re-applying is a clean
  replace -- no accumulation.
- The 5k-replacement deletes target **explicit existing ids** computed against the
  live DB at generation time, so they can never over-delete (worst case: no-op).
  **Regenerate the SQL whenever the base seeds change**, or those id lists go
  stale. Run order in `schema-init` is: schema -> base seeds (incl. Phase Y) ->
  `seed_captures.sql` -> orphan-spawn cleanup -> `sync_sequences.sql` (last, so
  the synthetic ids bump the identity sequences).

## Rules baked in

- Captured data takes priority over current data; that is the whole point.
- No SQL query is built by concatenating values -- every introspection query is a
  constant; resolution and spatial matching happen in Python. The generated
  `.sql` is a script artifact (literal values, quote-doubled), which is the one
  allowed place for literal SQL text.
- Captures are never committed. `.env` and any local captures are gitignored.
