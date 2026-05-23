# 07 - Tools toolchain

The `tools/` directory contains 21 projects -- mostly C# WinForms editors for
the MySQL content database, with a handful of legacy C++ utilities. The
canonical solution is `tools/Net7Tools.sln`. It currently references
17 projects (the four legacy `.dsp` projects -- ChunkTypes, UdpDump, Unmix,
LaunchNet7-old -- and the XML Exporter, are standalone Visual C++ 6 projects
from the pre-tools era and are not in the .NET solution).

All C# projects originated as Visual Studio 2008 / .NET Framework 2.0-4.0
WinForms apps. Phase D upgrades each to `net10.0-windows` SDK-style with
`<UseWindowsForms>true</UseWindowsForms>`. The runtime is and will remain
Windows-only: WinForms does not run on Linux or macOS. Cross-platform builds
work; cross-platform execution does not. (Wine + the Windows .NET runtime
is the only Linux execution path.)

Conventions used in this doc:

- Folder paths are relative to `tools/`.
- "Type" is WinForms, console, or library.
- "Status" reflects what we know from the source pre-Phase D. The Phase D
  upgrade status table appears at the end.

## Per-tool reference

### `commontools/` -- CommonTools (shared library)

Type: library.
Purpose: shared DB connection, login dialog, enumerations, VTune helpers,
common GUI widgets, XML helpers. Every other editor depends on this.
Entry point: none -- consumed via `CommonTools.Database.DB.Instance`,
`CommonTools.Gui.Login`, etc.
Notes: this is the chokepoint to migrate from `MySql.Data` to `MySqlConnector`
or `Npgsql` in Phase D; almost everything downstream uses it.

### `chunktypes/` -- ChunkTypes (legacy C++)

Type: console (Visual C++ 6 `.dsp`).
Purpose: dumps the chunk-type tree of a Westwood 3D (`.w3d`) file to text,
for offline asset inspection.
Entry point: `ChunkTypes.cpp` -> `main` -> `ProcessFile`.
Notes: not in the .NET solution. Includes `windows.h`; not built on Linux.
Output sample preserved at `tools/chunktypes/output.txt`.

### `dataimport/` -- DataImport

Type: WinForms.
Purpose: bulk content imports into the database -- assets, skills, item
references. Reuses CommonTools' login dialog.
Entry point: `Program.cs` -> launches `DataImport` form after login.
Notes: depends on CommonTools.

### `effect-editor/SQLBind/` -- Effect Editor (SQLBind)

Type: WinForms.
Purpose: edit the `effects`, `item_effect_base`, `item_effect_container`,
and `buffs` tables.
Entry point: `SQLBind/Program.cs`.
Notes: has its own `.sln` (`SQLBind.sln`) in addition to being a project in
`Net7Tools.sln`. Loads MySQL connection from `Config.xml` next to the
binary, with `net-7.org` fallback.

### `enb-ini-parser/` -- EnB Ini Parser

Type: console.
Purpose: parse client `.ini` files (extracted from the game data) and
import into the database: BaseAsset, BuffParser, EffectsParser,
SkillParser. The current `Main` runs `new SkillParser()` and exits;
other parsers are commented out.
Entry point: `Program.cs`.
Notes: connection details hardcoded in `Main` (`net-7.org:3307`, username
`Imp`). The hardcoded credentials are a known smell; should move to config
during Phase D.

### `enbpatcher/` -- EnBPatcher

Type: WinForms.
Purpose: client-side patcher utility -- generates and applies CRC32 patches
to the client binary.
Entry point: `Program.cs` -> `Form1`.
Notes: ships `crc32.cs` for CRC computation. Standalone `.sln`.

### `faction-editor/Net7 Faction Editor.csproj` -- Net7 Faction Editor

Type: WinForms.
Purpose: edit `factions`, `faction_matrix`, `manufacturers`.
Entry point: `EntryPoint.cs` -> `N7.Program.Main`.
Notes: loads `Config.xml` for SQL credentials; fallback host is
`net-7.org:3307`.

### `itemeditor/` -- ItemEditor

Type: WinForms.
Purpose: edit `item_base` and all `item_*` subtype tables (ammo, beam,
device, engine, missile, projectile, reactor, shield), plus
`item_manufacture`, `item_other_req`, `item_refine`, `item_effects`.
Entry point: `EntryPoint.cs` -> `Net7_Tools.Program.Main`.
Notes: depends on CommonTools (login dialog). Subfolders: `Database/`
(connection + validation), `Editors/` (table-specific edit panels),
`Record Managers/` (per-table CRUD), `Search/` (item tree dialogs),
`Widgets/` (custom controls). No standalone `.csproj` file currently
checked in -- the project is in `Net7Tools.sln` but its `ItemEditor.csproj`
must be regenerated during Phase D.

### `launchnet7/` -- LaunchNet7 (solution containing 3 projects)

Type: WinForms (`LaunchNet7`) + console (`ExeUpdater`, `FileListCreator`).
Purpose: the client launcher -- bootstraps the game client, checks for
updates against the launcher's file list, swaps EXEs in place via
`ExeUpdater`.
Entry points:
- `LaunchNet7/Program.cs` -> `LaunchNet7.Program.Main`: main launcher
  UI.
- `ExeUpdater/Program.cs`: tiny console wrapper that waits for a target
  process to exit, then replaces its EXE with a downloaded copy. Used by
  the launcher to update itself.
- `FileListCreator/Program.cs`: console tool that walks a directory tree,
  computes CRC32 per file, writes `Files.txt`. Used to publish a manifest.
Notes: there is a `LaunchNet7 Solution.sln` inside this folder in addition
to its projects being listed in `Net7Tools.sln`.

### `launchnet7-old/` -- LaunchNet7 (old, MFC)

Type: legacy C++ MFC (Visual C++ 6 `.dsp`).
Purpose: the original launcher, pre-C# rewrite. Kept for reference.
Entry point: `LaunchNet7.cpp` (`CWinApp`-derived application class).
Notes: not in the .NET solution; not buildable on Linux; should be
archived rather than maintained. Phase D plan flags it as "likely defer,
mark as archived".

### `missioneditor/MissionEditor.csproj` -- Mission Editor

Type: WinForms.
Purpose: edit `missions.mission_XML`. Renders the mission tree
(`Nodes`, `TalkNode.cs`, `Replies.cs`) and serialises back to XML.
Entry point: `Program.cs` -> `MissionEditor.Program.Main`.
Notes: depends on CommonTools. Per-mission XML schema documented in the
`Readme.odt` (LibreOffice format, original from upstream).

### `mob-editor/N7 Mob Editor.csproj` -- N7 Mob Editor

Type: WinForms.
Purpose: edit `mob_base`, `mob_items`, `mob_spawn_group`. Per-mob
property sheets in `Props/`, GUI in `GUI/`, SQL in `Sql/`.
Entry point: `EntryPoint.cs` -> `N7.Program.Main`.
Notes: standalone `.sln` exists alongside its inclusion in `Net7Tools.sln`.

### `sector-editor/Net7 Sector Editor.csproj` -- Net7 Sector Editor

Type: WinForms.
Purpose: edit `systems`, `sectors`, `sector_objects` and subtype tables.
Three top-level windows: `SystemWindow`, `SectorWindow`, `UniverseWindow`,
with a sidebar tree (`TreeWindow.cs`). Sprite-based rendering using
`Sprites/`. Settings persisted via `Settings.cs` and `app.config`.
Entry point: `EntryPoint.cs` -> `N7.Program.Main`.
Notes: largest editor in the suite. Standalone `.sln` plus inclusion in
`Net7Tools.sln`.

### `station-tools/Station Tools.csproj` -- Station Tools

Type: WinForms.
Purpose: edit `starbases`, `starbase_rooms`, `starbase_npcs`,
`starbase_npc_avatar_templates`, `starbase_terminals`,
`starbase_vendors`, `starbase_vender_*`. Bundles a TalkTree editor
(`EditTalkTree.cs`) and an item browse dialog (`ItemBrowse.cs`).
Entry point: `Program.cs` -> `Station_Tools.Program.Main`.
Notes: ships an in-repo `MySql.Data.dll` (legacy MySQL connector;
listed in `tools/THIRD_PARTY_BINARIES.md`). Standalone `.sln` exists.

### `talktreeeditor/TalkTreeEditor.sln` -- TalkTree Editor (standalone)

Type: WinForms.
Purpose: edit and preview NPC dialogue trees in the XML format stored in
`starbase_npcs.talk_tree_handle`. `Program.cs` initialises with a sample
conversation in code.
Entry point: `TalkTreeEditor/Program.cs`.
Notes: standalone, also referenced from Station Tools. Smaller than the
Station Tools version.

### `toolslauncher/ToolsLauncher.csproj` -- Tools Launcher

Type: WinForms.
Purpose: a launcher menu for the other editors. Loads `Config.xml` for
SQL credentials, presents a list of editors to launch.
Entry point: `ToolsLauncher/Program.cs`.

### `toolspatcher/ToolsPatcher.csproj` -- Tools Patcher

Type: WinForms.
Purpose: in-place patcher for the editor binaries. CRC32-checks each EXE,
downloads the replacement, swaps. Counterpart to `enbpatcher/` but for the
toolchain rather than the game client.
Entry point: `Program.cs` -> `Form1`.
Notes: standalone `.sln` plus inclusion in `Net7Tools.sln`.

### `udpdump/` -- UdpDump (legacy C++)

Type: legacy C++ (Visual C++ 6 `.dsp`).
Purpose: decrypt and decode UDP captures from the game client, producing
`SectorContent.xml` containing parsed packet opcodes (0x04 Create,
0x30 ActivateRenderState, 0x09 ObjectEffect, etc.; opcode list at top of
`UdpDump.cpp`).
Entry point: `UdpDump.cpp` -> `DoFile`.
Notes: depends on `WestwoodRSA.cpp`/`WestwoodRC4.cpp` for the client
crypto. Links `cryptlib` (Crypto++) and `wsock32`. Windows-only as
written. Useful Phase H reference for protocol RE.

### `unmix/` -- Unmix (legacy C++)

Type: legacy C++ (Visual C++ 6 `.dsp`).
Purpose: extract all files from a Westwood `.MIX` archive. Original
author VectoR.360, public domain (2004).
Entry point: `unmix.cpp` -> `main`.
Notes: uses `WIN32_LEAN_AND_MEAN` + `windows.h`; trivially portable to
POSIX (small file). Not in `Net7Tools.sln`.

### `w3d-parser/W3d Parser.csproj` -- W3d Parser

Type: library (C#).
Purpose: parse Westwood 3D (`.w3d`) files into a chunk tree. Used to
inspect/extract mesh, animation, and aggregate data. Per-chunk parsers
in `Chunks/` (`AabTreeChunk`, `AggregateChunk`, `AnimationChunk`, etc.).
Entry point: `w3d.cs` -> `WestWood3D.w3d` constructor; no console main.
Notes: pure managed code; cross-platform-portable in principle (no
WinForms). Phase D plan flags "heavy P/Invoke risk; may stay on old
framework" -- needs an inventory pass to confirm.

### `xml-exporter/` -- XML Exporter (legacy C++)

Type: legacy C++ (Visual C++ 6 `.dsp`).
Purpose: exports the item subsystem from MySQL to XML files (and the
reverse). Reads `Config.cfg` for two MySQL connections (a "Dase" source
DB and the destination `net7` DB) and runs the import.
Entry point: `Exporter.cpp` -> `main` -> `ItemBaseImporter`.
Notes: ships a bundled MySQL client library tree at
`tools/xml-exporter/mysql/`. Windows-only.

## Content pipeline

The end-to-end flow from "designer wants to add content" to "server serves
it":

1. **Editor**: a designer opens the appropriate WinForms editor (Sector,
   Mob, Item, Mission, Faction, Effect, Station Tools, TalkTree). The
   editor connects to MySQL via CommonTools' login dialog.
2. **Edit**: changes are committed to the live content database. The audit
   table `table_changes` records what changed, by whom, when, with full
   before/after payloads.
3. **Validation**: some editors run client-side validation (e.g.
   `itemeditor/Database/DataValidation.cs`) before writing.
4. **Database**: data lives in MySQL (today) / Postgres (after Phase C). The
   schema is documented in `06-database-schema.md`. World content
   (`net7.sql`) is read-mostly; per-player state (`net7_user.sql`) is
   read-write.
5. **Server load**: the C++ server reads content tables on startup and on
   sector-server bring-up; `AssetDatabaseSQL.cpp` is the chokepoint.
6. **Asset files on disk**: the `assets` table maps base IDs to filenames
   (`.mix`, `.w3d`, sound files) that ship with the client; the server
   sends asset IDs over the wire and the client resolves them locally.
7. **Bulk imports**: when content is sourced from extracted client `.ini`
   files (BaseAsset, effects, buffs, skills), `enb-ini-parser` and
   `dataimport` populate the corresponding tables.

There is no source-of-truth distinction between "editor view of an entity"
and "server view of an entity": both speak directly to the same database.
This is convenient and dangerous -- there is no schema migration story
beyond `versions` and `table_changes`.

## .NET 10 build status

Phase D (`plans/04-phase-d-csharp-tools.md`) tracks the per-tool upgrade.
Pre-upgrade, every C# project is a 2008-vintage non-SDK `.csproj` targeting
either .NET Framework 2.0 or 3.5. Building any of them on a modern dotnet
SDK requires:

1. Convert each `.csproj` to SDK-style (`<Project Sdk="Microsoft.NET.Sdk">`).
2. Set `<TargetFramework>net10.0-windows</TargetFramework>`,
   `<UseWindowsForms>true</UseWindowsForms>`.
3. Migrate any `app.config` ConfigurationManager calls to
   `Microsoft.Extensions.Configuration` or remove them.
4. Replace `MySql.Data` package references with `MySqlConnector` (active
   maintenance, MIT-licensed); for Postgres-aware editors add `Npgsql`.
5. Strip per-developer `.suo`, `.user`, `obj/`, `bin/` (already covered by
   `.gitignore`).
6. Replace removed APIs: `BinaryFormatter` is gone in .NET 10; XML
   serialisation is the drop-in replacement for the few editors using it.
7. Regenerate `Net7Tools.sln` via `dotnet sln add tools/<name>/<name>.csproj`.

The four legacy C++ projects (ChunkTypes, UdpDump, Unmix, LaunchNet7-old)
plus XML Exporter are not in scope for Phase D. They stay on their VC6
`.dsp` files unless someone explicitly ports them. The cleanest path for
UdpDump is to port it to a standalone CMake target alongside the server
during Phase H protocol work.

A live build-status table will appear in `tools/BUILD_STATUS.md` once
Phase D starts. Until then, treat every entry as "unverified, not built
on .NET 10". The expected matrix is:

| Tool | Phase D expected |
|---|---|
| commontools | upgrade -- prerequisite for all editors |
| dataimport | upgrade -- depends on commontools |
| effect-editor (SQLBind) | upgrade |
| enb-ini-parser | upgrade (console; trivial) |
| enbpatcher | upgrade |
| faction-editor | upgrade |
| itemeditor | upgrade (no .csproj checked in; regenerate) |
| launchnet7 (3 projects) | upgrade |
| launchnet7-old | archive |
| missioneditor | upgrade |
| mob-editor | upgrade |
| sector-editor | upgrade |
| station-tools | upgrade |
| talktreeeditor | upgrade |
| toolslauncher | upgrade |
| toolspatcher | upgrade |
| w3d-parser | upgrade or defer (P/Invoke audit pending) |
| chunktypes | leave as VC6 (not in solution) |
| udpdump | port to CMake during Phase H |
| unmix | port to CMake (trivial) or leave |
| xml-exporter | leave as VC6 / archive |

## Runtime requirements

For all WinForms editors after Phase D:

- Windows 10 or 11, or Windows Server 2019+, or Wine 9+ with the
  .NET 10 Desktop Runtime installed inside the Wine prefix.
- .NET 10 Desktop Runtime (Microsoft-supplied; not the cross-platform
  ASP.NET runtime).
- MySQL or PostgreSQL accessible on the network (the editors are
  GUI clients on top of the DB).

For console tools (enb-ini-parser, FileListCreator, ExeUpdater) after
Phase D: .NET 10 runtime, Windows-only as written (they reference
`System.Windows.Forms` or Win32-specific behaviour in places).

For the legacy C++ utilities: a Win32 toolchain (MSYS2 + MinGW or
Visual Studio Build Tools). Trivially portable in principle, not
prioritised.
