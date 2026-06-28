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
   - **Nav sector = `docs/sectors/json/*.jsonl`, FULL STOP.** That file set is the
     authoritative ground truth for which nav belongs to which sector. A nav is
     resolved to the sector(s) whose jsonl lists its name (matched
     alphanumeric-normalized -- drop case / punctuation / spaces, because the jsonl
     uses curly apostrophes and accents the wire names render as bare ASCII).
     **If a nav name is not in ANY sector's jsonl, that nav does not exist and is
     dropped** -- never imported off the capture's marker. This is non-negotiable:
     the in-stream sector markers are noisy around gate handoffs and the same gid
     is reused across captures, so the marker is NOT trusted for navs.
   - **Mob / resource sector assignment.** These have no name catalog, so they take
     the marker of the segment they were created in (the last `0x003A` handoff
     before their frame; markers whose id is not a real sector are dropped,
     instanced `realid*10+1` ids fold back). BUT that marker is cross-checked
     against the navs seen in the same segment (resolved via jsonl): if the marker
     sector has ZERO nav corroboration and another sector clearly dominates the
     segment's navs (>=3 navs and >3x the marker's support), the whole segment was
     mis-marked and is **relabeled** to the dominant sector -- the mobs move with
     it. This is deliberately conservative (a marker with ANY nav support is
     trusted), so legitimately mixed gate segments keep their marker. It is the
     correction for "if a nav was misattributed by believing the wrong sector, the
     mobs from that same belief were too."
   - **Dedup is two-level, because gids are session-scoped** (the same gid number is
     reused for unrelated objects across captures). WITHIN one capture a gid is
     stable, so objects are first collapsed by gid -- keeping the higher-confidence
     sector when a gid spans a relabel boundary, so the same mob seen in both the
     mis-marked and corrected segment lands in ONE sector. ACROSS captures, navs key
     on `(sector, normalized-name)` (gid-free, so a nav can never duplicate into two
     sectors) and mobs/resources on `(sector, gid)`. The kept record takes the
     **latest** position (captures ordered by timestamp, newest wins) and non-null
     fields merged in. Removal opcodes are ignored (we collect objects as they come
     into range), so an object that left and re-entered range is still one object.
   - **Exclusions:** players (`gid >= 0x40000000` or `isAvatar`), and anything that
     is not an import target (loot / corpses / decorative objects). All counted.
3. **gen_sql.py** writes the per-sector SQL following the policy below.

## Import policy

| Object | What happens |
|---|---|
| **Resources** (asteroids, clouds, ...) | Source of truth. Each captured rock is inserted as an exact-position single-rock harvestable whose `sector_objects.base_asset_id` is the **captured asteroid/gas/hulk model** (1822..1834 etc.) -- a resource is rendered straight from `base_asset_id`, so leaving it 0 would draw asset 0 (the "Old Old Terran Fighter" ship). A capture whose `baseAsset` is NOT an `assets` row with `main_cat IN ('Asteroids','Hulks')` is a mis-tagged loot/turret/derelict and is DROPPED, not spawned. Any EXISTING harvestable within 5k whose ore type matches is removed first (only that specific object). |
| **Mobs** | Source of truth. The captured mob is inserted as a 1-mob spawn at its exact position. Any EXISTING mob spawn within 5k of the SAME `base_asset_id` (the physical model at that spot) is removed first. The captured name (+asset/level) resolves to a `mob_base` template: an exact name match is reused; otherwise a NEW template is **synthesized** by cloning the nearest-level same-asset `mob_base` row and overriding name/level/asset, so the captured mob always spawns with the right model and valid stats. (`mob_base` has no hull/shield columns -- those derive from level + modifiers, which the clone inherits.) A mob is skipped only if its asset has no sibling to clone (not observed in the corpus). |
| **Navs** | Imported only if the name is listed for that sector in `docs/sectors/json/*.jsonl` (the authoritative membership source -- a nav absent from the jsonl does not exist and is dropped), AND a nav of that name does not already exist in the sector. Existing navs are never touched. gen_sql re-checks jsonl membership as a defensive gate (any drop here is a loud regression, since aggregate already enforced it). |
| **Stations / gates / planets** | **Report-only.** The capture lacks their child-row data (dock / cap-ship / stargate routing), so creating them would break them. We only print what we saw. |

Every imported **mob and resource** also gets a `sector_nav_points` row with
`nav_type = 0` (NOT a nav node -- `IsNav()` stays false, so it never shows on
radar as a nav target) carrying `signature = BASE_SIGNATURE` (7000.0). The row
is REQUIRED: the server LEFT-JOINs `sector_nav_points` onto every
`sector_object` and reads `signature` as the object's radar detection radius
(`SectorContentSQL::ProcessDefaultObjectStats` -> `ObjectManager`). With no row
the signature defaults to 0 and the object only pops in at bare scan range. 7000
is the dominant base-data value (1220/1372 mobs, 692/839 resources) and the
captures almost never carry a real per-object signature, so we match the base
convention rather than under-signature imports.

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
- A global `_purge.sql` deletes the WHOLE synth id range (children before parents)
  and runs FIRST (before `_mob_templates.sql` and the per-sector files). The
  per-sector self-delete above is sector-scoped, so a full re-apply over a
  DIFFERENT prior id mapping (the object set changed, so a gid now maps to a new
  synth id) would otherwise leave an old row squatting on an id an
  earlier-applied sector block tries to insert -- `ON CONFLICT DO NOTHING` then
  silently drops it. The up-front global purge makes every re-apply a clean
  replace across all sectors. (Observed: 246 synth rows silently dropped before
  `_purge.sql` existed.)
- The 5k-replacement deletes target **explicit existing ids** computed against the
  live DB at generation time, so they can never over-delete (worst case: no-op).

  **REGENERATE ONLY AGAINST A PRISTINE BASE DB** (base + Phase Y seeds, with
  `seed_captures.sql` NOT yet applied). This is a hard precondition: the replace
  blocks delete base-seed rows the import itself removes, so on a DB where the
  capture import already ran those base rows are gone, the replace queries return
  nothing, and the replace blocks regenerate EMPTY -- silently resurrecting the
  duplicates. `gen_sql.py` enforces this: it reads the committed replace-target
  ids and **refuses** (non-zero exit) if any are already deleted. To get a
  pristine DB:

  ```bash
  docker compose down -v
  ENB_SKIP_CAPTURE_SEED=1 docker compose up -d schema-init   # base+Phase Y only
  bash .claude/skills/import-captures/scripts/import.sh        # regenerate
  bash .claude/skills/import-captures/scripts/import.sh --apply # (optional) apply now
  ```

  Note this also means `import.sh --apply` is a ONE-SHOT against a pristine DB:
  re-running it (which regenerates first) will hit the guard and refuse, because
  the first apply made the DB non-pristine. Re-apply the committed SQL via a fresh
  boot or by piping `seed_captures.sql` into psql -- do not regenerate to re-apply.

  Run order in `schema-init` is: schema -> base seeds (incl. Phase Y) ->
  `seed_captures.sql` (gated; skipped when `ENB_SKIP_CAPTURE_SEED=1`) ->
  orphan-spawn cleanup -> `sync_sequences.sql` (last, so the synthetic ids bump
  the identity sequences).

## Baseline guardian turrets (`gen_turrets.py` -> `seed_turrets.sql`)

The captures show guardian turrets ringing gates and starbases ("Gate Guardian
Turret" / "Starbase Guardian Turret", model asset 14). A single fly-through only
catches the 1-3 turrets nearest the flight path, and only in the handful of
sectors we flew; the base data also carries ~129 real turrets, but only near some
gates/starbases. The retail server placed a small ring around EVERY gate and
starbase. `gen_turrets.py` **fills the gaps** as **baseline content** (not an
optional mod):

- It reads every stargate (`sector_objects.type = 11`) and starbase (`type = 12`)
  with a known position from the running net7 DB and, for each that does NOT
  already have a real turret within 6k, places an evenly-spaced ring of guardian
  turrets (3 per gate, 4 per starbase) elevated above the anchor plane. The ring
  radius/elevation give a 3D distance (~1980) squarely inside the captured
  1428-2500 band. Anchors that already have a turret keep their authentic base
  placement (the ring is gap-fill, so the two never double up).
- Each turret matches the existing base-data turret shape exactly (e.g.
  sector_object 1733): a single `sector_objects` row with **`type = 42`**
  (OT_MOB / stationary turret -- the server's `SectorContentSQL` turret branch
  reads the model straight from **`base_asset_id = 14`**), `scale = 1`,
  `radar_range = 5000`, plus one `sector_nav_points` row (nav_type 1, signature
  20000, exploration_range 3000). The display name ("Gate"/"Starbase Guardian
  Turret") lives on `sector_objects.name`. A type-42 turret is self-contained: it
  writes **no** `sector_objects_mob` and **no** `mob_spawn_group` row. (A type-0
  mob SPAWN with `base_asset_id 0` -- the original mistaken shape -- loads as an
  OT_MOBSPAWN with no model and is invisible; the self-delete below still purges
  any such stale rows.)
- Synthetic ids live in their OWN range (`sector_object_id >= 2000000`), clear of
  the capture import (1000000) and Phase Y. `seed_turrets.sql` deletes its own
  range first (children before parents), so re-applying is a clean replace.
- `schema-init` applies `seed_turrets.sql` **unconditionally** on every boot
  (after the capture block, before the orphan cleanup, before `sync_sequences`).
  It is independent of `ENB_SKIP_CAPTURE_SEED`: it touches neither base rows nor
  the capture import, so it is present even when regenerating against a pristine
  base DB.
- A running sector thread reads its objects once on cold-start and does not
  re-poll the DB, so applying `seed_turrets.sql` to a live DB only shows up after
  the sector reloads (re-enter the sector, or restart the server). A fresh
  `docker compose` boot has them from the first load.

Regenerate it with `import.sh` (it runs `gen_turrets.py` after `gen_sql.py`), or
directly: `python3 .claude/skills/import-captures/scripts/gen_turrets.py`.

## Rules baked in

- Captured data takes priority over current data; that is the whole point.
- **`docs/sectors/json/*.jsonl` is the sole authority for nav-to-sector
  membership.** A nav not in there does not exist; never attribute a nav to a
  sector off the capture's in-stream marker. (Mobs/resources have no such catalog,
  so they fall back to the marker-segment sector, conservatively relabeled by the
  jsonl-resolved navs in that same segment.)
- No SQL query is built by concatenating values -- every introspection query is a
  constant; resolution and spatial matching happen in Python. The generated
  `.sql` is a script artifact (literal values, quote-doubled), which is the one
  allowed place for literal SQL text.
- Captures are never committed. `.env` and any local captures are gitignored.
