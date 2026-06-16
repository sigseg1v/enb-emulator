# Content pipeline -- editor to DB to server

How sectors, mobs, missions, and items get from a designer's editor into
the running server.

## Overview

```
+----------------+    SQL    +------------+  startup load   +--------------+
| C# editor      | -------->  |  Postgres  | --------------> | C++ server   |
| (Avalonia,     |           |  (net7     |   (per-Manager  | (managers in |
|  tools/*       |           |   content  |    Load* call)  |  global mem) |
|  -avalonia)    |           |   DB)      |                 |              |
+----------------+           +------------+                 +--------------+
```

The server is **read-mostly** at content boundaries: editors write the
DB, the server reads it. Live reloads exist for sector content (see §4)
but are the exception. Content lives in the `net7` Postgres database
(schema at `db/postgres/schema.sql`); the `db/mysql/` dumps are the
historical upstream source the Postgres schema was converted from, not a
runtime store.

## 1. The editors and the tables they touch

The Avalonia editors run natively on Linux. The original WinForms projects
have been removed.

| Editor | Avalonia path | Primary tables |
|---|---|---|
| Sector | `tools/sector-editor-avalonia/` | `sectors`, `systems`, `sector_objects`, `factions` |
| Mob | `tools/mob-editor-avalonia/` | `mob_base`, `mob_items`, `mob_type`, `mob_spawn_group` |
| Item | `tools/item-editor-avalonia/` | `item_base` (+ category sub-tables) |
| Mission | `tools/missioneditor-avalonia/` | `missions` (mission XML lives in a column) |
| Faction | `tools/faction-editor-avalonia/` | `factions`, `faction_matrix` |
| Effect | `tools/effect-editor-avalonia/` | `item_effect_base`, `item_effects`, `item_effect_stats`, `item_effect_container`, `buffs` |
| TalkTree | `tools/talktreeeditor-avalonia/` | mission dialogue trees (XML-based; minimal direct SQL) |
| Station | `tools/station-tools-avalonia/` | `starbases`, `starbase_vender_groups`, `starbase_vender_inventory`, `sector_objects_starbases` |

Per-table source of truth is the live `net7` schema
(`db/postgres/schema.sql`; the historical `db/mysql/net7.sql` dump is
where it was converted from). Editor table refs for reference:
`tools/sector-editor-avalonia/Sql/SectorsSql.cs`,
`tools/mob-editor-avalonia/Sql/MobsSQL.cs`,
`tools/item-editor-avalonia/Database/TableIO.cs`.

## 2. Schema, by content type

- **Sectors** -- `sectors` (system FK, dimensions, backdrop,
  type), plus `sector_objects` for everything that lives in
  them (mobs, stargates, starbases, asteroids). Stargates get
  `sector_objects_stargates`; mobs get `sector_objects_mob`;
  starbases get `sector_objects_starbases`.
- **Mobs** -- `mob_base` is the type definition (level, faction,
  AI script, skill 0-9 slots, base asset). `mob_items` equips
  the mob. `mob_type` is the type-name lookup. `mob_spawn_group`
  groups several mobs for spawning together.
- **Missions** -- `missions` holds the mission XML in a column
  (`mission_XML`). Player progress lives in `avatar_missions` and
  `mission_objectives`, written at runtime (see §5).
- **Items** -- `item_base` joined with `item_manufacturer_base`,
  with category-specific sub-tables for beams, engines, ammo,
  etc. Effects/buffs are normalized into the `item_effect_*`
  family.

## 3. Server load path

The server loads everything once at startup from
`ServerManager.cpp`'s init sequence (ServerManager.cpp:203,
ServerManager.cpp:212). Each content domain has a `*SQL.cpp`
that does the actual queries:

| Domain | Loader | Query | Runtime container |
|---|---|---|---|
| Sectors | `SectorContentSQL.cpp:73` `ParseSectorContent()` | `SELECT * FROM sectors` (line 105) | `g_ServerMgr->m_SectorContent.m_SectorList` (map by sector_id) |
| Mobs | `MOBDatabaseSQL.cpp:48` `LoadMOBContent()` | `SELECT * FROM mob_base` (line 70) | `g_ServerMgr->MOBList().m_MOB` (map by mob_id) |
| Missions | `MissionDatabaseSQL.cpp:74` `LoadMissionContent()` | `SELECT * FROM missions` (line 93), mission_XML is parsed per row | `g_ServerMgr->m_Missions.m_Missions` (map of `MissionTree`) |
| Items | `ItemBaseSQL.cpp:102` `LoadItemBase()` | `SELECT * FROM item_base INNER JOIN item_manufacturer_base` (line 103) | `g_ItemBaseMgr->m_ItemDB` (dense array indexed by item_id) |

Sub-tables (mob items, sector objects, item effects) load via
follow-up queries inside the same loaders.

## 4. Reloads

Most content is **load-once at boot.** Restart the server to
pick up new mobs or items.

The exception is sectors: `ServerManager::ReloadSectorObjects()`
(ServerManager.cpp:584) sets `g_ResetContent`, reloads
`m_MOBList`, and re-parses one sector's objects via
`m_SectorContent.LoadSectorContent(sector_id)`. Triggered by
GM command (see `docs/11-gm-commands.md`).

## 5. Runtime state vs. content state

Two different lifecycles, important to keep straight:

- **Content tables** (`sectors`, `mob_base`, `item_base`,
  `missions`) are designer-authored, edited via the C#
  tools, loaded into memory at server start.
- **Player state tables** (`accounts`, `avatars`,
  `avatar_inventory`, `avatar_missions`, `avatar_skills`,
  `guilds`, `guild_members`, ...) are runtime data, written
  by the server via the DAO layer when players act.

A new piece of content (a new mob type) lands as a `mob_base`
row + an `sector_objects` + `sector_objects_mob` row pair to
place it. Player kills, loot pickups, etc. write to
`avatar_*` tables.

## 6. Practical implications

- After editing content in a tool, **restart the server**
  (or `/reloadsector` if the change is sector-scoped) -- there's
  no live-edit feedback loop.
- The C# tools and the server both need to agree on schema. When a
  schema change renames or retypes a column, cross-check
  `db/postgres/schema.sql` against the editors' SQL constants before
  shipping it.
- Mission XML lives in a DB column, not the filesystem.
  Re-importing missions means rewriting `missions.mission_XML`.
