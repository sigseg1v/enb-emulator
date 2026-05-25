# Content pipeline — editor → DB → server

How sectors, mobs, missions, and items get from a designer's editor into
the running server.

## Overview

```
┌────────────────┐    SQL    ┌────────────┐  startup load   ┌──────────────┐
│ C# editor      │ ────────► │  MySQL     │ ───────────────►│ C++ server   │
│ (Avalonia or   │           │  (3307 in  │   (per-Manager  │ (managers in │
│  WinForms,     │           │   dev)     │    Load* call)  │  global mem) │
│  tools/*)      │           │            │                 │              │
└────────────────┘           └────────────┘                 └──────────────┘
```

The server is **read-mostly** at content boundaries: editors write the
DB, the server reads it. Live reloads exist for sector content (see §4)
but are the exception. The Postgres schema is staged at
`db/postgres/schema.sql` but the runtime is still MySQL pending Phase N
Wave 3.

## 1. The editors and the tables they touch

The recommended Avalonia ports run natively on Linux; the legacy
WinForms ports are kept alongside for diff reference and Windows / WINE
use.

| Editor | Avalonia path (recommended) | Legacy WinForms path | Primary tables |
|---|---|---|---|
| Sector | `tools/sector-editor-avalonia/` | `tools/sector-editor/` | `sectors`, `systems`, `sector_objects`, `factions` |
| Mob | `tools/mob-editor-avalonia/` | `tools/mob-editor/` | `mob_base`, `mob_items`, `mob_type`, `mob_spawn_group` |
| Item | — (no upstream csproj) | `tools/itemeditor/` | `item_base` (+ category sub-tables) |
| Mission | `tools/missioneditor-avalonia/` | `tools/missioneditor/` | `missions` (mission XML lives in a column) |
| Faction | `tools/faction-editor-avalonia/` | `tools/faction-editor/` | `factions`, `faction_matrix` |
| Effect | `tools/effect-editor-avalonia/` | `tools/effect-editor/` | `item_effect_base`, `item_effects`, `item_effect_stats`, `item_effect_container`, `buffs` |
| TalkTree | `tools/talktreeeditor-avalonia/` | `tools/talktreeeditor/` | mission dialogue trees (XML-based; minimal direct SQL) |
| Station | `tools/station-tools-avalonia/` | `tools/station-tools/` | `starbases`, `starbase_vender_groups`, `starbase_vender_inventory`, `sector_objects_starbases` |

Per-table source of truth is `db/mysql/net7.sql` (and the converted
`db/postgres/schema.sql`). Editor table refs (legacy paths still hold
the original SQL templates the Avalonia ports inherit):
`tools/sector-editor/SectorsSql.cs:15`,
`tools/mob-editor/MobsSQL.cs:15`,
`tools/itemeditor/TableIO.cs:75`.

## 2. Schema, by content type

- **Sectors** — `sectors` (system FK, dimensions, backdrop,
  type), plus `sector_objects` for everything that lives in
  them (mobs, stargates, starbases, asteroids). Stargates get
  `sector_objects_stargates`; mobs get `sector_objects_mob`;
  starbases get `sector_objects_starbases`.
- **Mobs** — `mob_base` is the type definition (level, faction,
  AI script, skill 0-9 slots, base asset). `mob_items` equips
  the mob. `mob_type` is the type-name lookup. `mob_spawn_group`
  groups several mobs for spawning together.
- **Missions** — `missions` holds the mission XML in a column
  (`mission_XML`). Player progress lives in `avatar_missions` and
  `mission_objectives`, written at runtime (see §5).
- **Items** — `item_base` joined with `item_manufacturer_base`,
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
(ServerManager.cpp:488) sets `g_ResetContent`, reloads
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
  (or `/reloadsector` if the change is sector-scoped) — there's
  no live-edit feedback loop.
- The C# tools and the server both need to agree on schema. The
  eventual Postgres cutover (whenever Phase N Wave 3 completes the
  DAO migration) renames or retypes columns in places; cross-check
  `db/postgres/schema.sql` against the editors' SQL constants before
  shipping any schema change.
- Mission XML lives in a DB column, not the filesystem.
  Re-importing missions means rewriting `missions.mission_XML`.
