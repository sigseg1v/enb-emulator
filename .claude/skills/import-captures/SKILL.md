---
name: import-captures
description: Decode and aggregate Earth & Beyond packet captures into deduplicated per-sector SQL imports. Use when importing captured nav, mob, resource, or sector object data; do not use for live server or client changes.
---

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
   - **Objects created BEFORE the first sector marker are recovered, not dropped.**
     A capture opens mid-flight in the sector we were already in, and objects can
     arrive in range before the first in-stream handoff. Their sector is determined
     three ways, most-authoritative first: (a) navs resolve by NAME via the jsonl
     regardless of markers, so a pre-marker nav is placed correctly with no guess;
     (b) a pre-marker mob/resource segment is provisionally tagged with the end
     sector of the immediately-preceding capture in the same play session (the
     sector we were flying when this capture began), chained only when the two
     captures are within `SESSION_GAP_SECS` (30 min) by their `...T......Z`
     filename timestamps, then subject to the SAME nav-corroboration relabel as any
     other segment; (c) if a pre-marker segment has NO navs to corroborate AND no
     same-session predecessor to chain from, it is still dropped -- attributing it
     would be inventing a location. This never trusts the capture filename for a
     mob/resource (only for a genuinely marker-less capture whose name resolves).
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

## Capture-mob loot (`gen_loot.py` -> `seed_capture_loot.sql`)

The import synthesizes a `mob_base` template (`mob_id >= 900000`) for every
captured mob whose exact name is not already in `mob_base`. Those clones get the
right model + stats but **no `mob_items`** -- nothing copies loot onto them, so
they would drop nothing when killed. `gen_loot.py` fills that gap as an
**additive** layer on top of the import (it does NOT regenerate the import, and
it NEVER touches base loot -- only the synth range). Two tiers, most-authoritative
first:

- **(b) Reconstruct-by-name.** If the synth mob's name matches a mob in the
  reconstruct dataset (`ENB_RECONSTRUCT_DIR/db/mobs/mobs.jsonl`) that lists drop
  items, and those item names resolve to `item_base` rows, emit those items.
  Reconstruct rarely carries real per-item rates, so the items get an EVEN split
  summing to exactly 100 (the server throttles over-tier/over-priced drops
  itself, so an even table is safe; exactly 100 avoids the server's ">100%" loot
  warning). `usage_chance`/`type` are inert in the drop roll -> 0/0, `qty` 1.
- **(a) Sibling inheritance (fallback).** No name match -> copy the loot of the
  nearest-level same-model (`base_asset_id`) BASE mob that actually has loot,
  mirroring how `gen_sql.py` picks the clone source. A copied table that sums
  >100 (a base-data quirk) is proportionally rescaled to exactly 100 so it does
  not trip the server warning (drop behaviour is identical -- the server would
  scale it by 100/total anyway). A synth mob whose model has no loot-bearing
  sibling gets no loot (correct -- e.g. a pure turret).

- **`item_base.id` is the loot key** (`mob_items.item_base_id` -> `item_base.id`,
  NOT the empty `item_base.item_base_id` column). Every emitted id is validated
  to exist.
- Synth templates come from the running net7 DB (`mob_id >= 900000`); if the DB
  has none (generating against a pristine base DB before the import is applied),
  the committed `_mob_templates.sql` is parsed instead -- so `gen_loot.py` does
  not depend on whether the import has been applied yet.
- `seed_capture_loot.sql` deletes its own synth-range `mob_items` first
  (`mob_id >= 900000`), so re-applying is a clean replace.
- `schema-init` applies it after `seed_captures.sql`, **gated on the SAME
  `ENB_SKIP_CAPTURE_SEED`** (the synth templates it references do not exist in a
  pristine base DB); otherwise applied on every boot.

Set `ENB_RECONSTRUCT_DIR` in `.env` to enable tier (b); without it every synth
mob falls back to tier (a). Regenerate with `import.sh` (runs `gen_loot.py` after
`gen_turrets.py`), or directly:
`ENB_RECONSTRUCT_DIR=... python3 .claude/skills/import-captures/scripts/gen_loot.py`.

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
