# 06 - Database schema

Source-of-truth dump: `db/mysql/net7.sql` (71 tables, world/content data) and
`db/mysql/net7_user.sql` (42 tables, account/character/guild/galaxy data).

Both dumps are mysqldump output from the tada-o snapshot (2010). They use
MySQL idioms (`int(N) unsigned`, `AUTO_INCREMENT`, backticked identifiers,
`ENGINE=InnoDB`, mixed `latin1`/`utf8` charsets) that do not parse cleanly in
Postgres. Phase C of the modernization plan converts them; see
`plans/03-phase-c-postgres.md` and (when produced) `db/postgres/schema.sql`.

This document covers the 71 tables in `net7.sql`. The 42 tables in
`net7_user.sql` are summarised at the end.

## Conventions in this doc

- "PK" = primary key.
- "FK -> X.y" means an explicit `FOREIGN KEY` constraint to table `X`,
  column `y`. Implicit foreign keys (column named `foo_id` but no FK
  declared) are noted where obvious.
- "cols" is the column count.
- Tables with `_100` suffixed columns store the value at quality 100; the
  effective in-game value is `column * (quality / 100.0)`.
- "Editor" cross-references the C# tool under `tools/` that primarily edits
  the table. "(none)" means it is read-only seed data with no dedicated
  editor.

## Group: world geometry

The galaxy is a tree: `systems` -> `sectors` -> `sector_objects` (plus
type-specific specialisation tables).

### `systems`

Star systems. The galaxy is a list of systems.

- PK: `system_id`
- cols: 9
- Key columns: `name`, `galaxy_x/y/z` (position in galaxy view),
  `color_r/g/b` (rendering tint), `notes`.
- Referenced by: `sectors.system_id`.
- Editor: Sector Editor (`tools/sector-editor/`).

### `sectors`

Individual playable space sectors. Each sector is a 3D box (`x_min`/`max`
etc.) belonging to one system.

- PK: `sector_id`
- cols: ~35
- Key columns: `name`, `system_id` (FK -> `systems.system_id`), bounding
  box `x_min/y_min/z_min/x_max/y_max/z_max`, `grid_x/y/z`, `fog_near/far`,
  `backdrop_asset`, `sector_ip_addr` (the IP the sector server listens on),
  `challenge_rating`, `sector_type`, `Radius Value` (note the literal
  space and capitalisation -- preserved from upstream).
- Referenced by: `sector_objects.sector_id`, `sector_nav_points.sector_id`.
- Editor: Sector Editor.

### `sector_allocation`

Maps a sector to the GM username allowed to allocate / edit it. Looks like
edit-control metadata for the live editor.

- PK: `id`
- cols: 3
- Key columns: `sector_id`, `username`.
- Editor: Sector Editor (admin view).

### `sector_objects`

The base table for everything physically present in a sector: planets,
stargates, starbases, asteroids, mobs, turrets, navpoints. The
`type` column discriminates between subtype tables.

- PK: `sector_object_id`
- cols: ~21
- Key columns: `base_asset_id`, position (`position_x/y/z`),
  orientation (`orientation_u/v/w/z`), `type`, `sector_id` (KEY,
  no explicit FK), `gate_to`, `appears_in_radar`, `radar_range`,
  `sound_effect_id`, `sound_effect_range`, `h/s/v/scale`.
- Subtype tables: `sector_objects_planets`, `sector_objects_starbases`,
  `sector_objects_stargates`, `sector_objects_harvestable`,
  `sector_objects_mob`, `sector_objects_turrets`, `sector_nav_points`.
- Editor: Sector Editor.

### `sector_objects_planets`

Planet-specific data attached to a `sector_objects` row.

- PK: `planet_id` (FK -> `sector_objects.sector_object_id`)
- cols: 9
- Key columns: `orbit_id`, `orbit_dist/angle/rate`,
  `rotate_angle/rate`, `tilt_angle`, `is_landable`.
- Editor: Sector Editor.

### `sector_objects_starbases`

Starbase-specific data on a `sector_objects` row.

- PK: `starbase_id` (FK -> `sector_objects.sector_object_id`)
- cols: 3
- Key columns: `capShip` (capital ship base?), `dockable`.
- Editor: Sector Editor (placement) + Station Tools (interior).

### `sector_objects_stargates`

Stargate-specific data on a `sector_objects` row. Stargates can be
faction-locked, security-level locked, or class-restricted.

- PK: `stargate_id` (FK -> `sector_objects.sector_object_id`)
- cols: 4
- Key columns: `classSpecific`, `minSecurityLevel`,
  `faction_id`.
- Editor: Sector Editor.

### `sector_objects_turrets`

Turret-as-object data.

- PK: `turret_id`
- cols: 3
- Key columns: `turret_mob_id` (the mob template the turret behaves as),
  `treat_as_mob`.
- Editor: Sector Editor.

### `sector_objects_harvestable`

Harvestable resource fields (asteroid belts, gas clouds) as
sector objects.

- PK: implicit on `resource_id` (FK -> `sector_objects.sector_object_id`)
- cols: 8
- Key columns: `level`, `field`, `res_count`, `spawn_radius`,
  `pop_rock_chance`, `max_field_radius`, `respawn_timer`.
- Editor: Sector Editor.

### `sector_objects_harvestable_oretypes`

Per-field additional ore drops.

- PK: none (KEY on `resource_id, additional_ore_item_id`)
- cols: 3
- Key columns: `resource_id`, `additional_ore_item_id`, `frequency`.
- Editor: Sector Editor.

### `sector_objects_harvestable_restypes`

Per-field resource type list.

- PK: `id`
- cols: 3
- Key columns: `group_id`, `type`.
- Editor: Sector Editor.

### `sector_objects_mob`

Mob-spawn-as-object data: hooks a mob spawn into a sector location.

- PK: `mob_id` (FK -> `sector_objects.sector_object_id`)
- cols: 6
- Key columns: `mob_count` (group size), `mob_spawn_radius`,
  `respawn_time`, `delayed_spawn`, `group_aggro`.
- Editor: Sector Editor + Mob Editor.

### `sector_nav_points`

Navigation points attached to a sector object.

- PK: `sector_object_id` (FK)
- cols: 8
- Key columns: `nav_type`, `signature`, `is_huge`, `sector_id`,
  `base_xp`, `exploration_range`, `object_radius_patch`.
- Editor: Sector Editor.

### `base_ore_list`

Per-sector base ore frequencies.

- PK: none (KEY on `sector_id, item_id`)
- cols: 4
- Key columns: `item_id`, `name`, `sector_id`, `frequency`.
- Editor: Sector Editor.

### `asteroid_content_selection`

Maps asteroid type to the item subcategories that can drop from it.

- PK: none (KEY on `asteroid_type, item_subcat_id`)
- cols: 3
- Key columns: `asteroid_type`, `item_subcat_id`, `item_subcat_desc`.
- Editor: (none) -- mostly seed data.

## Group: assets

The `assets` table is the bridge between numeric IDs and on-disk media
files (3D models, textures, sounds). It is referenced by name from many
other tables via columns named `*_asset_id`.

### `assets`

Asset registry. One row per visual/audio resource.

- PK: `base_id`
- cols: 6
- Key columns: `descr`, `main_cat`, `sub_cat`, `filename`, `rslid`.
- Referenced by: `effects.base_asset_id`, `sectors.backdrop_asset`,
  `mob_base.base_asset_id`, many `*_asset` columns elsewhere.
- Editor: (none, populated by Data Import).

## Group: mobs

The mob system is split: `mob_base` is the template, `mob_spawn_group`
groups templates into encounter sets, `mob_items` defines drops, and
`sector_objects_mob` hooks a group into a sector.

### `mob_base`

The template for one kind of mob. Includes stats, AI, up to 10 skill IDs,
and per-mob shield/damage/range modifiers.

- PK: `mob_id`
- cols: 30
- Key columns: `name`, `level`, `intelligence`, `bravery`, `type`,
  `faction_id`, `base_asset_id`, `altruism`, `aggressiveness`, `ai`,
  `h/s/v/scale`, `skill0..skill9`, `skillchance`, `skillcooldown`,
  `shield_modifier`, `damage_modifier`, `range_modifier`.
- Editor: Mob Editor (`tools/mob-editor/`).

### `mob_items`

Loot/equipment table per mob. `type` is the slot or drop-type;
`drop_chance` is a float percent.

- PK: `id`
- cols: 7
- Key columns: `mob_id`, `item_base_id`, `usage_chance`, `drop_chance`,
  `type`, `qty`.
- Editor: Mob Editor.

### `mob_spawn_group`

Groups one or more mob templates into a named spawn group with a deterministic
within-group index.

- PK: `id`
- cols: 4
- Key columns: `spawn_group_id`, `mob_id`, `group_index`.
- Editor: Mob Editor.

### `mob_type`

Lookup: mob type id to name (e.g. NPC, hostile, friendly).

- PK: `Value`
- cols: 2
- Key columns: `Value`, `Name`.
- Editor: (none).

### `mob_agressiveness`

Lookup: aggressiveness id to name. (Note the original spelling
"agressiveness" -- preserved from upstream.)

- PK: `id`
- cols: 2
- Editor: (none).

## Group: avatars / character templates

These are the **template** rows -- one per Race+Profession combination --
not per-player data. Per-player rows live in `net7_user.sql`.

### `avatar_base`

Per-(Race, Profession) base ship stats: shield, reactor, engine, weapon,
hull asset, starting sector, etc.

- PK: none declared (logically Race+Profession)
- cols: 15
- Key columns: `Race`, `Profession`, `base_shield`, `base_reactor`,
  `base_engine`, `base_weapon`, `base_hull_asset`,
  `base_profession_asset`, `base_wing_asset`, `base_engine_asset`,
  `starting_sector`, `base_faction`, `base_scan_range`,
  `base_signature`, `base_speed`.
- Editor: (none -- seed data; touched by Item Editor for cross-refs).

### `hulls`

Per-(Race, Profession, upgrade_level) hull capacity numbers.

- PK: none (logically Race+Profession+upgrade_level)
- cols: 7
- Key columns: `Race`, `Profession`, `upgrade_level`, `hull_points`,
  `weapon_slots`, `device_slots`, `cargo_slots`.
- Editor: (none).

## Group: items

The item subsystem is the largest in the schema. `item_base` is the
template; per-type tables (`item_ammo`, `item_beam`, `item_shield`,
`item_reactor`, `item_engine`, `item_device`, `item_missile`,
`item_projectile`) extend it.

### `item_base`

Template row for every item. Categories are referenced numerically into
`item_categories`/`item_subcategories`/`item_type`.

- PK: `id`
- cols: ~34
- Key columns: `level`, `category`, `sub_category`, `type`, `max_stack`,
  `name`, `description`, `manufacturer` (FK ->
  `item_manufacturer_base.id`), `2d_asset`, `3d_asset`, `no_trade`,
  `no_store`, `no_destroy`, `no_manu`, `unique`, `item_base_id`
  (self-FK for variants), `effect_id` (FK -> `effects.effect_id`),
  `price`, `man_cost*` (manufacture cost fields), pricing modifiers,
  `quality_mod`.
- Referenced by: every `item_*` subtype table.
- Editor: Item Editor (`tools/itemeditor/`).

### `item_categories`, `item_subcategories`, `item_type`

Three flat lookup tables for the (category, subcategory, type) tuple
that classifies every item.

- `item_categories`: PK `id`, 2 cols (`id`, `category`).
- `item_subcategories`: PK `id`, 2 cols (`id`, `subcategory`).
- `item_type`: PK `id`, 2 cols (`id`, `name`).
- Editor: Item Editor (read-only).

### `item_cat_subcat_type`

Tuple mapping that constrains valid (category, subcategory, type)
combinations.

- PK: none
- cols: 3
- Editor: Item Editor.

### `item_ammo`

Ammunition stats per item.

- PK: `item_id` (FK -> `item_base.id`)
- cols: 7
- Key columns: `ammo_type_id`, `damage_type`, `fire_effect`,
  `maneuv_100`, `damage_100`, `range_100`.
- Editor: Item Editor.

### `item_ammo_type`

Ammunition type lookup.

- PK: `id`
- cols: 3
- Key columns: `sub_category`, `name`.
- Editor: Item Editor.

### `item_beam`

Beam-weapon stats.

- PK: `item_id`
- cols: 9
- Key columns: `rest_prof`, `rest_race`, `damage_type`, `fire_effect`,
  `damage_100`, `range_100`, `energy_100`, `reload_100`.
- Editor: Item Editor.

### `item_device`

Device stats (toggleable equipment).

- PK: `item_id`
- cols: 5
- Key columns: `rest_prof`, `rest_race`, `energy_100`, `range_100`.
- Editor: Item Editor.

### `item_engine`

Engine stats: warp, thrust, signature, energy drain.

- PK: `item_id`
- cols: 9
- Key columns: `rest_prof`, `rest_race`, `warp`, `warp_drain_100`,
  `thrust_100`, `signature_100`, `energy_100`, `range_100`.
- Editor: Item Editor.

### `item_manufacture`

Recipe: up to 6 component item IDs plus a difficulty.

- PK: `item_id`
- cols: 8
- Key columns: `comp_1..comp_6`, `difficulty`.
- Editor: Item Editor.

### `item_manufacture_difficulty`

Difficulty lookup.

- PK: `id`
- cols: 2
- Editor: (none).

### `item_manufacturer_base`

Manufacturer lookup (referenced by `item_base.manufacturer`). Note this
is distinct from the `manufacturers` table in the factions group, which
is the in-fiction company.

- PK: `id`
- cols: 2
- Editor: (none).

### `item_missile`

Missile stats.

- PK: `item_id`
- cols: 8
- Key columns: `rest_prof`, `rest_race`, `ammo`, `ammo_per_shot`,
  `energy_100`, `reload_100`, `ammo_type_id`.
- Editor: Item Editor.

### `item_other_req`

Skill / level requirements for using an item.

- PK: `item_id`
- cols: 9
- Key columns: `overall_lvl`, `combat_lvl`, `explore_lvl`, `trade_level`,
  `other_skill`, `over_skill_lvl`, `energy_drain`, `shield_drain`.
- Editor: Item Editor.

### `item_projectile`

Projectile-weapon stats.

- PK: `item_id`
- cols: 9
- Key columns: `rest_prof`, `rest_race`, `ammo`, `ammo_per_shot`,
  `range_100`, `energy_100`, `reload_100`, `ammo_type_id`.
- Editor: Item Editor.

### `item_reactor`

Reactor stats.

- PK: `item_id`
- cols: 7
- Key columns: `rest_prof`, `rest_race`, `cap_100`, `recharge_100`,
  `energy_100`, `range_100`.
- Editor: Item Editor.

### `item_shield`

Shield stats.

- PK: `item_id`
- cols: 7
- Key columns: `rest_prof`, `rest_race`, `cap_100`, `recharge_100`,
  `energy_100`, `range_100`.
- Editor: Item Editor.

### `item_refine`

Refinement target mapping: item id to refined-into item id.

- PK: `item_id`
- cols: 2
- Editor: Item Editor.

### `item_verification_status`

Lookup for content-QA verification states.

- PK: `id`
- cols: 2
- Editor: (none).

## Group: effects (item-attached buffs/abilities)

The effects subsystem is a small graph: an `effects` row is the "what
happens visually"; `item_effect_base` is the "what happens
mechanically"; `item_effect_container` and `item_effects` join the two
to items.

### `effects`

Visual/audio effect: linked-list of effect classes, base asset ID,
sound file.

- PK: `effect_id`
- cols: 7
- Key columns: `effect_class`, `description`, `start_link_id`,
  `next_link_id`, `base_asset_id` (FK to `assets.base_id` by
  convention), `sound_fx_file`.
- Referenced by: `item_base.effect_id` (with explicit FK constraint).
- Editor: Effect Editor (`tools/effect-editor/`).

### `item_effect_base`

Mechanical effect template. Encodes up to two constant stat-modifiers
and three variable ones, plus flags, a buff name, a visual hookup.

- PK: `EffectID`
- cols: 25
- Key columns: `EffectType` (0=equipable, 1=activatable), `Name`,
  `Description`, `Tooltip`, `flag1/2`, `Constant1Value/Stat/Type`,
  `Constant2Value/Stat/Type`, `Var1/2/3Stat/Type`, `Buff_Name`,
  `VisualEffect`, `Var1/2/3_mod`, `O2OEffect`.
- FKs: `Constant1Stat`, `Constant2Stat`, `Var1Stat`, `Var2Stat`,
  `Var3Stat` -> `item_effect_stats.Stat_Name`.
- Editor: Effect Editor.

### `item_effect_container`

Per-item activation envelope: cooldown, range, energy use, modifiers.

- PK: `EffectContainerID`
- cols: 9
- Key columns: `ItemID`, `EquipEffect`, `RechargeTime`, `Unknown2`,
  `_Range`, `Unknown4`, `EnergyUse`, `Energy_mod`.
- Editor: Effect Editor + Item Editor.

### `item_effect_stats`

Lookup of stat names (`HULL_STRENGTH`, etc.) referenced from
`item_effect_base` and `buffs`.

- PK: `Stat_Name`
- cols: 2
- Editor: (none).

### `item_effects`

Per-item per-effect variable values. Joins one item to one
`item_effect_base`, supplying the three variable values.

- PK: `ItemEffectID`
- cols: 6
- Key columns: `ItemID`, `item_effect_base_id` (FK ->
  `item_effect_base.EffectID`), `Var1Data`, `Var2Data`, `Var3Data`.
- Editor: Item Editor.

### `buffs`

Player-facing buff descriptors. Each buff references a stat (must exist
in `item_effect_stats`) and an effect (must exist in `effects`).

- PK: `buff_id`
- cols: 9
- Key columns: `buff_name`, `StatName` (FK ->
  `item_effect_stats.Stat_Name`), `StatType`, `EffectID` (FK ->
  `effects.effect_id`), `EffectLength`, `tooltip`, `description`,
  `is_good_buff`.
- Editor: Effect Editor.

### `damage_types`

Lookup: damage type id to label.

- PK: `id`
- cols: 2
- Editor: (none).

## Group: skills / abilities

Skill trees gate ability unlocks. Per-(profession, skill_id) max levels
live on `skills`.

### `skills`

Skill template plus per-profession max-level columns. Profession spelling
note: "sentinal" (sic), "tradesman" -- preserved verbatim.

- PK: `skill_id`
- cols: 14
- Key columns: `name`, `description`, `is_activated`, `category`,
  `warrior_max_level`, `sentinal_max_level`, `privateer_max_level`,
  `defender_max_level`, `explorer_max_level`, `seeker_max_level`,
  `enforcer_max_level`, `scout_max_level`, `tradesman_max_level`.
- Editor: (none -- usually edited via DataImport or direct SQL).

### `skill_levels`

Per-skill, per-level descriptive text.

- PK: `skill_level_id`
- cols: 4
- Key columns: `skill_id`, `level`, `description`.
- Editor: (none).

### `skill_abilities`

Player-unlockable abilities granted by skill levels.

- PK: `ability_id`
- cols: 6
- Key columns: `skill_id`, `min_level`, `description`,
  `activation_cost`, `name`.
- Editor: (none -- generated by DataImport from server side).

### `level_xp`

XP cost table per level, split by combat / explore / trade.

- PK: `level`
- cols: 4
- Key columns: `trade_xp`, `explore_xp`, `combat_xp`.
- Editor: (none -- seed data).

## Group: factions

### `factions`

Faction definitions.

- PK: `faction_id`
- cols: 6
- Key columns: `name`, `description`, `player_PDA`, `PDA_text`,
  `faction_gain_sound`.
- Referenced by: `manufacturers.faction_id`, `mob_base.faction_id`
  (implicit), `sector_objects_stargates.faction_id`,
  `starbase_npcs.faction_id`, `starbases.faction_id`.
- Editor: Faction Editor (`tools/faction-editor/`).

### `faction_matrix`

Faction-to-faction relationship matrix: how much faction A gains
or loses when something happens to faction B.

- PK: `id`
- cols: 6
- Key columns: `faction_id`, `faction_entry_id`, `base_value`,
  `current_value`, `reward_faction`.
- Editor: Faction Editor.

### `manufacturers`

In-fiction manufacturer companies (Terran, Jenquai, etc., aligned to
factions). Separate from `item_manufacturer_base`, which is the item-
template join.

- PK: `manufacturer_id`
- cols: 4
- Key columns: `name`, `slogan`, `faction_id` (FK -> `factions.faction_id`).
- Editor: Faction Editor.

## Group: starbases

Starbases are the in-station UI: rooms, NPCs, vendors, terminals.

### `starbases`

Starbase definitions.

- PK: `starbase_id`
- cols: 11
- Key columns: `sector_id`, `name`, `type`, `is_active`, `description`,
  `welcome_message`, `target_sector_object`, `faction_id` (FK ->
  `factions.faction_id`), `starbase_sector_id`, `challenge_rating`.
- Referenced by: `starbase_rooms.starbase_id`.
- Editor: Station Tools (`tools/station-tools/`) + Sector Editor for
  placement.

### `starbase_rooms`

Interior rooms.

- PK: `room_id`
- cols: 10
- Key columns: `type`, `style`, `fog_near/far`, `fog_red/green/blue`,
  `description`, `starbase_id` (FK -> `starbases.starbase_id`).
- Referenced by: `starbase_npcs.room_id`, `starbase_terminals.room_id`.
- Editor: Station Tools.

### `starbase_room_type`

Lookup: room type id to label.

- PK: `room_type`
- cols: 2
- Editor: (none).

### `starbase_npcs`

In-room NPCs. The `talk_tree_handle` column is a long XML string
referencing TalkTree content.

- PK: `npc_Id`
- cols: 9
- Key columns: `first_name`, `last_name`, `location`, `faction_id` (FK),
  `description`, `talk_tree_handle`, `room_id` (FK -> `starbase_rooms`),
  `npc_index`.
- Extra FK: `npc_Id` -> `starbase_npc_avatar_templates.avatar_template_id`
  and `starbase_vendors.vendor_id` (an NPC is also-a-vendor and
  also-an-avatar template).
- Editor: Station Tools + TalkTree Editor.

### `starbase_npc_avatar_templates`

Avatar appearance templates for NPCs: race, gender, hair, clothing,
colours, body weights. Long flat record.

- PK: `avatar_template_id`
- cols: 64
- Key columns: `avatar_type`, `avatar_version`, `race`, `profession`,
  `gender`, `mood_type`, `personality`, body/head feature numbers,
  color RGB triples for hair/beard/eyes/skin/shirt1/shirt2/pants1/pants2,
  body_weight 0..4, head_weight 0..4.
- Editor: Station Tools.

### `starbase_terminals`

Interactive terminals in a room.

- PK: `terminal_id`
- cols: 8
- Key columns: `location`, `type`, `attribute`, `description`,
  `room_id` (FK), `terminal_index`, `terminal_level`.
- Editor: Station Tools.

### `starbase_vendors`

Vendor NPC config. Joins back to `starbase_npcs` via `vendor_id`.

- PK: `vendor_id`
- cols: 4
- Key columns: `level`, `booth_type`, `groupid`.
- Editor: Station Tools.

### `starbase_vender_groups`

Vendor group (sell/buy multiplier table; "vender" sic, preserved).

- PK: `GroupID`
- cols: 5
- Key columns: `GroupName`, `SellMultiplyer`, `BuyMultiplyer`,
  `BuyOnlyList`.
- Editor: Station Tools.

### `starbase_vender_inventory`

Vendor inventory rows.

- PK: `id`
- cols: 6
- Key columns: `groupid`, `itemid`, `sell_price`, `buy_price`, `quanity`
  (sic).
- Editor: Station Tools.

## Group: missions

### `missions`

Missions are stored as XML blobs.

- PK: `mission_id`
- cols: 6
- Key columns: `mission_XML` (text), `mission_name`, `mission_key`,
  `mission_type`, `mission_minSecurityLevel`.
- Editor: Mission Editor (`tools/missioneditor/`).

The mission XML structure includes branches, replies, and rewards;
parsed by the Mission Editor and reauthored on save.

## Group: audit / utility

### `table_changes`

Audit log of editor-driven changes. Big varchar payloads for
`new_values` and `old_values`.

- PK: `id`
- cols: 7
- Key columns: `tablename`, `modification`, `username`, `modified`,
  `new_values`, `old_values`.
- Editor: written by all editors via Common Tools.

### `test_trigger`

Smoke-test target for editor triggers ("test1", "test2"). Not gameplay
data.

- PK: `index`
- cols: 5
- Editor: (none -- diagnostic).

### `tirgger_save`

Companion to `test_trigger`. (Note: "tirgger" sic, preserved.)

- PK: `index`
- cols: 3
- Editor: (none -- diagnostic).

### `versions`

Per-component version pins ("EName" is editor name).

- PK: `EName`
- cols: 2
- Editor: written by Tools Patcher / each editor on startup.

## Summary of net7.sql

71 tables, grouped above:

- World geometry (16): `systems`, `sectors`, `sector_allocation`,
  `sector_objects`, `sector_objects_planets`,
  `sector_objects_starbases`, `sector_objects_stargates`,
  `sector_objects_turrets`, `sector_objects_harvestable`,
  `sector_objects_harvestable_oretypes`,
  `sector_objects_harvestable_restypes`, `sector_objects_mob`,
  `sector_nav_points`, `base_ore_list`, `asteroid_content_selection`,
  plus the assets table.
- Assets (1): `assets`.
- Mobs (5): `mob_base`, `mob_items`, `mob_spawn_group`, `mob_type`,
  `mob_agressiveness`.
- Avatar templates (2): `avatar_base`, `hulls`.
- Items (18): `item_base`, `item_categories`, `item_subcategories`,
  `item_type`, `item_cat_subcat_type`, `item_ammo`, `item_ammo_type`,
  `item_beam`, `item_device`, `item_engine`, `item_manufacture`,
  `item_manufacture_difficulty`, `item_manufacturer_base`,
  `item_missile`, `item_other_req`, `item_projectile`, `item_reactor`,
  `item_shield`, `item_refine`, `item_verification_status`.
- Effects/buffs (6): `effects`, `item_effect_base`,
  `item_effect_container`, `item_effect_stats`, `item_effects`,
  `buffs`, `damage_types`.
- Skills (4): `skills`, `skill_levels`, `skill_abilities`, `level_xp`.
- Factions (3): `factions`, `faction_matrix`, `manufacturers`.
- Starbases (9): `starbases`, `starbase_rooms`, `starbase_room_type`,
  `starbase_npcs`, `starbase_npc_avatar_templates`,
  `starbase_terminals`, `starbase_vendors`, `starbase_vender_groups`,
  `starbase_vender_inventory`.
- Missions (1): `missions`.
- Audit/utility (4): `table_changes`, `test_trigger`, `tirgger_save`,
  `versions`.

(The counts above add to slightly more than 71 because a handful of
tables are referenced in multiple groups; the canonical count is
71 distinct `CREATE TABLE` statements in `db/mysql/net7.sql`.)

## net7_user.sql (account / character / per-player data)

Loaded separately. 42 tables. This dump holds the live state -- accounts,
characters, inventories, guild membership, position, mission progress --
that gets created and mutated at runtime. The world tables in `net7.sql`
are read-mostly content; `net7_user.sql` is read-write live data.

Tables (alphabetical, names preserved verbatim from the dump):

- `account_avatar_forumname`
- `account_infractions`
- `account_status_levels`
- `accounts`
- `avatar_ammo`
- `avatar_base`
- `avatar_data`
- `avatar_equipment`
- `avatar_exploration`
- `avatar_faction_level`
- `avatar_gm_items`
- `avatar_info`
- `avatar_inventory_items`
- `avatar_level_info`
- `avatar_mission_progress`
- `avatar_position`
- `avatar_recipes`
- `avatar_skill_levels`
- `avatar_trade_items`
- `avatar_vault_items`
- `cbasset`
- `faction_data`
- `factions`
- `forbidden_names`
- `friends_lists`
- `galaxy`
- `guild_members`
- `guild_ranks`
- `guilds`
- `hulls`
- `ignore_lists`
- `local_respawn_time`
- `missions_completed`
- `professions`
- `races`
- `server_local_field_respawn_times`
- `ship_data`
- `ship_info`
- `skill_list`
- `ssl_deny_list`
- `status_levels`
- `warning_levels`

Notable overlap with `net7.sql`: `avatar_base`, `hulls`, `factions`
exist in both dumps. The user-database versions are the live per-player
records; the content-database versions are templates. The schema does
not enforce that distinction, so be careful when comparing.

Account flow: `accounts` -> `avatar_info` (per-character) ->
`avatar_data`/`avatar_position`/`avatar_equipment`/`avatar_inventory_items`/
`avatar_mission_progress` (per-character state) -> per-character skill,
faction, recipe, vault, trade tables.

Guild flow: `guilds` -> `guild_ranks` -> `guild_members`.

## Editor-to-table cross reference

| Editor | Primary tables it edits |
|---|---|
| Sector Editor (`tools/sector-editor/`) | `systems`, `sectors`, `sector_objects` and all `sector_objects_*` subtype tables, `sector_nav_points`, `sector_allocation`, `base_ore_list` |
| Mob Editor (`tools/mob-editor/`) | `mob_base`, `mob_items`, `mob_spawn_group`, plus references to `sector_objects_mob` |
| Item Editor (`tools/itemeditor/`) | `item_base`, all `item_*` subtype tables (`item_ammo`, `item_beam`, `item_engine`, `item_shield`, `item_reactor`, `item_device`, `item_missile`, `item_projectile`, `item_manufacture`, `item_other_req`, `item_refine`, `item_effects`) |
| Effect Editor (`tools/effect-editor/`) | `effects`, `item_effect_base`, `item_effect_container`, `item_effect_stats`, `buffs` |
| Faction Editor (`tools/faction-editor/`) | `factions`, `faction_matrix`, `manufacturers` |
| Station Tools (`tools/station-tools/`) | `starbases`, `starbase_rooms`, `starbase_npcs`, `starbase_npc_avatar_templates`, `starbase_terminals`, `starbase_vendors`, `starbase_vender_groups`, `starbase_vender_inventory` |
| TalkTree Editor (`tools/talktreeeditor/`) | NPC `talk_tree_handle` XML, stored inside `starbase_npcs.talk_tree_handle` |
| Mission Editor (`tools/missioneditor/`) | `missions` |
| Data Import (`tools/dataimport/`) | Bulk seed loads -- `assets`, `skills`, `skill_levels`, `skill_abilities`, ammunition/category lookups |
| EnB Ini Parser (`tools/enb-ini-parser/`) | Imports `BaseAsset`, `effects`, `buffs`, `skill_abilities` from extracted client `.ini` files |

## Postgres migration notes

Phase C (`plans/03-phase-c-postgres.md`) defines the conversion rules. The
high-level transformations the conversion script needs to apply:

- Drop `ENGINE=InnoDB`, `DEFAULT CHARSET=...`, `COLLATE ...`.
- Backtick to double-quote: `` `name` `` -> `"name"`.
- `int(N) unsigned` -> `bigint` (safe upper bound for unsigned ranges);
  other `int(N)` -> `integer`.
- `tinyint(1)` -> `boolean` for clear flags, otherwise `smallint`.
- `AUTO_INCREMENT` -> `GENERATED ALWAYS AS IDENTITY`.
- `datetime` -> `timestamp`.
- Inline `KEY ...(cols)` -> separate `CREATE INDEX` statements after the
  table definition.
- `\\0` -> `E'\\x00'` in INSERT rows (for binary-shaped values in
  `net7_user.sql`).

Known oddities to watch for:

- Column name with a literal space: `sectors`.`Radius Value`. Either quote
  in Postgres or rename.
- Misspelled identifiers preserved: `mob_agressiveness` table,
  `tirgger_save` table, `sentinal_max_level` column,
  `SellMultiplyer`/`BuyMultiplyer` columns, `quanity` column. The
  conversion does not fix these (changing them would break every C++ and
  C# call site). Rename in a follow-up if/when the code is comprehensively
  refactored.
- `binary(1)` on `item_effect_container.EquipEffect`: keep as `bytea` or
  convert to `boolean` -- needs caller-side audit.
- `int(11) unsigned zerofill` on `starbase_rooms.fog_*`: zerofill is
  display-only in MySQL; drop in Postgres conversion.
- `double(11,4)` (faction values): becomes `double precision` in Postgres;
  precision/scale specifiers are ignored.
- `float(N,M)` columns: becomes `real`; precision specifier ignored.

See `db/postgres/README.md` (created during Phase C) for the residual
manual-fix list.
