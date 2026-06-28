# Phase AY -- Prospecting-driven ore/sector assignment + missing resource fields

Owner ask (2026-06-27): "for applying which resources spawn in which asteroid,
use the files in `docs/prospecting`. Also generate resource fields that those
files indicate if we don't have them in the db, use sql files for this."
("asteroid" = any prospectable node type, not just rock asteroids.)

Two parts, both pure data tooling (no server/proxy/client wire change, so no CV
gate). Generator: `tools/prospecting-import/gen_prospecting.py` (Freya/MIT,
self-contained Python, reads `docs/prospecting/*.jsonl` + introspects the live DB
read-only, emits committed SQL). Source data committed under `docs/prospecting/`.

## How the server actually selects ore (why Part A is correct)

`ItemBaseManager::GetOreTemplate(level, obj_type, sector_id, field)` builds a
candidate pool = `base_ore_list` items for that sector whose `item.sub_category`
is in `asteroid_content_selection[obj_type]` AND `item.level == node level`, then
picks one uniformly (the `frequency` column is loaded but UNUSED). `base_ore_list`
is SECTOR-LEVEL (no nodeType/level columns): an item is reachable iff its
`sub_category` is in some ACS set. So mapping "which resource spawns in which
sector" == inserting the right `(item_id, sector_id)` rows into `base_ore_list`.

`nodeType -> ACS category`: Rock->2, Crystal->4, Hydrocarbon->3, Glowing->1,
Gas->7, Hulk->10. (Inorganic Hulks legitimately yield manufactured goods/ammo
sub-cats, not raw rock -- that is correct EnB behaviour, not a bug.)

## Part A -- base_ore_list (which resources spawn in which sector)

- Drives from `docs/prospecting/prospecting.jsonl` (3545 records). For each unique
  `(resource, sector)`: resolve resource name -> `item_base.id` (alias
  `aluminium->aluminum`), sector name -> `sectors.id` (normalised).
- ADDITIVE ONLY: each row inserted with `INSERT ... SELECT ... WHERE NOT EXISTS`
  (the table has no unique key, so NOT EXISTS is the idempotency guard). Never
  prunes existing rows. `frequency = 0` (unused).
- Skips items whose `sub_category` is not an ore sub-cat in any ACS set (they
  could never be selected) and reports them; reports unresolved resource names.
- Result: **859 additions across 45 sectors** (2426 already present, 244 not an
  ore sub-cat, 16 unresolved resource names: `DA6 Volatile Slugs`, `Harrier`).
- Does NOT modify `item_base.sub_category` or `asteroid_content_selection` -- that
  would be a server-logic change needing primary-source proof.

## Part B -- resource fields the data indicates but the DB lacked

- Drives from `docs/prospecting/prospect_fields.jsonl` (145 records). For each
  `(sector, nav)` resolve the nav to a position (robust normalisation: exact ->
  slash-split -> annotation-stripped -> difflib fuzzy >= 0.80 against ALL named
  objects in the sector, not just type-37). If a harvestable FIELD already exists
  within 8000 units of the nav, skip (DB already has it).
- For each missing one, emit one invisible field container (`sector_objects`
  type 38, `base_asset_id = 0`) + `sector_objects_harvestable`
  (`field=0, res_count=10, max_field_radius=8000, pop_rock_chance=3`) + one
  `sector_objects_harvestable_restypes` child per node-type model
  (Rock->1825, Crystal->1831, Glowing->1822, Hydrocarbon->1828, Gas->1834,
  Hulk->1131; all verified present in the Asteroids/Hulks asset sets).
- REACHABILITY FILTER (the load-bearing correctness step): only emit a restype
  child whose ACS sub-cats x node level are actually present in `base_ore_list`
  for that sector (Part A's reachable set). A field with no spawnable child is
  dropped entirely -- otherwise it would render an empty/immutable container.
- Result: **25 field containers across 21 navs, 70 restype children, 0 empty
  drops**. 93 navs already have a field; 25 report-only navs unresolved (gates,
  planets, compound/sub-sector names -- left alone honestly, NOT renamed off the
  prospecting data, which has as many typos as the DB).

## Synth id range + idempotency

- Fields use synthetic ids `>= 3000000` (clear of capture import at 1000000 and
  turrets at 2000000). `fields.sql` purges its own synth range first, then
  re-inserts -- a clean replace on every apply.
- `assert_pristine()` in the generator refuses to regenerate against a DB that
  already has prospecting applied (Part A is a diff over the base; it must see the
  base WITHOUT its own prior additions, same trap as the capture import).
- `ENB_SKIP_PROSPECTING_SEED=1` schema-init escape hatch brings up a
  base+captures DB without the prospecting layer to regenerate against.

## Wiring

- `db/postgres/seed_prospecting.sql` wrapper `\ir`s `prospecting/base_ore_list.sql`
  + `prospecting/fields.sql`; applied by a gated `schema-init` block AFTER the
  capture import and BEFORE `sync_sequences` (so synth ids advance the
  identity sequences). Gate var `ENB_SKIP_PROSPECTING_SEED` declared in the
  service `environment:` block (mirrors `ENB_SKIP_CAPTURE_SEED`).

## Checklist

- [x] AY-1 Generator built (`tools/prospecting-import/gen_prospecting.py`).
- [x] AY-2 Part A: 859 base_ore_list additions, NOT EXISTS-guarded, reported.
- [x] AY-3 Part B: 25 fields / 70 restypes, reachability-filtered, 0 empty.
- [x] AY-4 Nav resolution corrected (owner flag "we should have a record for
  every nav"): brittle exact-lowercase type-37 match replaced with normalised +
  fuzzy match over all object types; 25 genuine report-only remainders.
- [x] AY-5 Synth range >=3000000, pristine guard, schema-init gate + env
  passthrough.
- [x] AY-6 Regenerated against a pristine base+captures stack; applied to the
  live dev DB (25 fields, 70 restypes, 0 empty, 18106 ore rows).
- [x] AY-7 docs/prospecting source data + generator + SQL committed.
- [ ] AY-8 Real-client confirmation that the new fields render + mine (owner,
  async; no wire change so this is a content check, not a CV gate).
