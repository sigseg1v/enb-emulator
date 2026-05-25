# 07 - Tools toolchain

The `tools/` directory holds the C# editor suite for the content database
plus a few legacy C++ utilities. There are **two parallel sets** of C#
projects:

- **`<tool>-avalonia/`** — Phase L ports targeting `net10.0` + **Avalonia
  11**. Run **natively on Linux** (no WINE). These are the recommended
  binaries. The central solution that pulls them in is
  `tools/Net7Tools.slnx` (SDK-style XML solution); each port also has its
  own `.csproj` and most can be launched directly.
- **`<tool>/`** — original Phase D ports of the 2008-era WinForms code,
  targeting `net10.0-windows` with `<UseWindowsForms>true</UseWindowsForms>`.
  These cross-compile (`dotnet build tools/Net7Tools.slnx`) but their
  runtime is Windows / WINE only. Kept for reference and diffing.

Phase L is complete; per-tool status is tracked in
`plans/12-phase-l-avalonia.md`. The one exception is `tools/itemeditor/`
— it never had a `.csproj` in the upstream snapshot, so there is no
Avalonia port. See `tools/README.md` for the user-facing quickstart and
the launch recipes.

## Quickstart

The dev DB needs to be running for editors that talk to MySQL:

```sh
just init                    # boots mysql:8.0 on localhost:3307 + loads dumps
```

Then either launch the central GUI launcher…

```sh
just launch                  # toolslauncher-avalonia
```

…or jump straight to a specific editor:

```sh
just launch-mob-editor
just launch-sector-editor
just launch-mission-editor
just launch-faction-editor
just launch-effect-editor
just launch-station-tools
just launch-talktree-editor
just launch-dataimport
just launch-net7             # game client launcher (LaunchNet7)
just launch-enbpatcher       # client patcher
just launch-toolspatcher     # patcher for the editor binaries
```

`just --list` prints every recipe. Each recipe runs
`dotnet run --project tools/<name>-avalonia/`.

Editors that hit the DB pop a Login dialog on startup; default dev creds
match the docker-compose stack: host `localhost`, port `3307`, user `net7`,
password `net7`, database `net7` (or `net7_user` for the accounts schema).

## Conventions used in this doc

- Folder paths are relative to `tools/`.
- "Type" is WinForms, Avalonia, console, or library.
- "Status" reflects the Avalonia port (the recommended runtime).

## Per-tool reference

### `commontools/` + `commontools-avalonia/` — CommonTools (shared library)

Type: library.
Purpose: shared DB connection, login dialog, enumerations, common widgets,
XML helpers. Every editor depends on this.
Avalonia notes: `commontools-avalonia/` is the Avalonia 11 port; the login
dialog is Avalonia XAML. DB layer uses `MySqlConnector` (MIT-licensed,
cross-platform — `MySql.Data` was dropped during Phase L).

### `chunktypes/` — ChunkTypes (legacy C++)

Type: console (Visual C++ 6 `.dsp`).
Purpose: dumps the chunk-type tree of a Westwood 3D (`.w3d`) file to text,
for offline asset inspection.
Status: out of Phase D/L scope; not in `Net7Tools.slnx`. Windows-only.

### `dataimport/` + `dataimport-avalonia/` — DataImport

Type: Avalonia (recommended) / WinForms (legacy).
Purpose: bulk content imports into the database — assets, skills, item
references. Reuses CommonTools' login dialog.
Launch: `just launch-dataimport`.

### `effect-editor/` + `effect-editor-avalonia/` — Effect Editor

Type: Avalonia (recommended) / WinForms (legacy).
Purpose: edit the `effects`, `item_effect_base`, `item_effect_container`,
and `buffs` tables.
Launch: `just launch-effect-editor`.

### `enb-ini-parser/` — EnB Ini Parser

Type: console.
Purpose: parse client `.ini` files (extracted from the game data) and
import into the database: BaseAsset, BuffParser, EffectsParser,
SkillParser. The current `Main` runs `new SkillParser()` and exits;
other parsers are commented out.
Status: console tool, no Avalonia port needed; runs as-is on `dotnet`.

### `enbpatcher/` + `enbpatcher-avalonia/` — EnBPatcher

Type: Avalonia (recommended) / WinForms (legacy).
Purpose: client-side patcher utility — generates and applies CRC32 patches
to the client binary.
Launch: `just launch-enbpatcher`.

### `faction-editor/` + `faction-editor-avalonia/` — Faction Editor

Type: Avalonia (recommended) / WinForms (legacy).
Purpose: edit `factions`, `faction_matrix`, `manufacturers`.
Launch: `just launch-faction-editor`.

### `itemeditor/` — ItemEditor (no Avalonia port)

Type: WinForms only.
Purpose: edit `item_base` and all `item_*` subtype tables (ammo, beam,
device, engine, missile, projectile, reactor, shield), plus
`item_manufacture`, `item_other_req`, `item_refine`, `item_effects`.
Status: **NOT ported to Avalonia.** The original upstream snapshot never
shipped an `ItemEditor.csproj`, so Phase L had no baseline to port from.
Runs on Windows / WINE only via the regenerated WinForms csproj.

### `launchnet7/` + `launchnet7-avalonia/` — LaunchNet7 (game client launcher)

Type: Avalonia (recommended) / WinForms (legacy).
Purpose: bootstraps the EnB client (the original Win32 binary under
WINE), checks for updates against a manifest, swaps EXEs in place via
the `ExeUpdater` helper.
Launch: `just launch-net7`.
Notes: the legacy folder also contained `ExeUpdater/` (console, swaps the
running EXE after target exit) and `FileListCreator/` (console, walks a
directory and computes CRC32 to publish a `Files.txt` manifest). Phase L
folded the launcher itself into Avalonia; the helper consoles run as-is
on `dotnet`.

### `missioneditor/` + `missioneditor-avalonia/` — Mission Editor

Type: Avalonia (recommended) / WinForms (legacy).
Purpose: edit `missions.mission_XML`. Renders the mission tree
(`Nodes`, `TalkNode.cs`, `Replies.cs`) and serialises back to XML.
Launch: `just launch-mission-editor`.

### `mob-editor/` + `mob-editor-avalonia/` — Mob Editor

Type: Avalonia (recommended) / WinForms (legacy).
Purpose: edit `mob_base`, `mob_items`, `mob_spawn_group`. Per-mob
property sheets, GUI, SQL split into folders.
Launch: `just launch-mob-editor`.

### `sector-editor/` + `sector-editor-avalonia/` — Sector Editor

Type: Avalonia (recommended) / WinForms (legacy).
Purpose: edit `systems`, `sectors`, `sector_objects` and subtype tables.
Three top-level windows (`SystemWindow`, `SectorWindow`, `UniverseWindow`)
plus a sidebar tree (`TreeWindow`). The original Piccolo.NET dependency
was replaced in the Avalonia port by `tools/sector-editor-avalonia/PiccoloShim/`
— a shim that maps the small Piccolo subset the editor used onto
Avalonia primitives, so we don't carry a Windows-only third-party
graphics library.
Launch: `just launch-sector-editor`.

### `station-tools/` + `station-tools-avalonia/` — Station Tools

Type: Avalonia (recommended) / WinForms (legacy).
Purpose: edit `starbases`, `starbase_rooms`, `starbase_npcs`,
`starbase_npc_avatar_templates`, `starbase_terminals`,
`starbase_vendors`, `starbase_vender_*`. Bundles a TalkTree editor and
an item browse dialog.
Launch: `just launch-station-tools`.

### `talktreeeditor/` + `talktreeeditor-avalonia/` — TalkTree Editor

Type: Avalonia (recommended) / WinForms (legacy).
Purpose: edit and preview NPC dialogue trees in the XML format stored in
`starbase_npcs.talk_tree_handle`.
Launch: `just launch-talktree-editor`.

### `toolslauncher/` + `toolslauncher-avalonia/` — Tools Launcher

Type: Avalonia (recommended) / WinForms (legacy).
Purpose: a launcher menu for the other editors. The Avalonia version is
the central entry point exposed by `just launch`.
Launch: `just launch`.

### `toolspatcher/` + `toolspatcher-avalonia/` — Tools Patcher

Type: Avalonia (recommended) / WinForms (legacy).
Purpose: in-place patcher for the editor binaries. CRC32-checks each
binary, downloads the replacement, swaps. Counterpart to `enbpatcher/`
but for the toolchain itself.
Launch: `just launch-toolspatcher`.

### `udpdump/` — UdpDump (legacy C++)

Type: legacy C++ (Visual C++ 6 `.dsp`).
Purpose: decrypt and decode UDP captures from the game client, producing
`SectorContent.xml` containing parsed packet opcodes.
Status: out of Phase D/L scope; useful Phase H reference for protocol RE.
Depends on `WestwoodRSA.cpp` / `WestwoodRC4.cpp` (the same crypto now
shared via `common/include/net7/`).

### `unmix/` — Unmix (legacy C++)

Type: legacy C++ (Visual C++ 6 `.dsp`).
Purpose: extract files from a Westwood `.MIX` archive. Original author
VectoR.360, public domain (2004).
Status: out of Phase D/L scope; trivially portable to POSIX (small file).

### `w3d-parser/` — W3d Parser

Type: library (C#).
Purpose: parse Westwood 3D (`.w3d`) files into a chunk tree. Pure managed
code, in principle cross-platform.
Status: not user-facing; not in the Avalonia migration scope.

### `xml-exporter/` — XML Exporter (legacy C++)

Type: legacy C++ (Visual C++ 6 `.dsp`).
Purpose: exports the item subsystem from MySQL to XML files (and the
reverse). Ships a bundled MySQL client library tree at
`tools/xml-exporter/mysql/`.
Status: out of Phase D/L scope; Windows-only.

## Content pipeline

The end-to-end flow from "designer wants to add content" to "server serves
it":

1. **Editor**: a designer opens the appropriate Avalonia editor (Sector,
   Mob, Item, Mission, Faction, Effect, Station Tools, TalkTree). The
   editor connects to MySQL via CommonTools' login dialog.
2. **Edit**: changes are committed to the live content database. The audit
   table `table_changes` records what changed, by whom, when, with full
   before/after payloads.
3. **Validation**: some editors run client-side validation (e.g.
   `itemeditor/Database/DataValidation.cs`) before writing.
4. **Database**: data lives in MySQL. The Postgres conversion is staged at
   `db/postgres/schema.sql` for future cutover but is not the runtime
   target today. World content (`net7.sql`) is read-mostly; per-player
   state (`net7_user.sql`) is read-write.
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
This is convenient and dangerous — there is no schema migration story
beyond `versions` and `table_changes`.

## Build status

Phase L (`plans/12-phase-l-avalonia.md`) is complete: every user-facing
editor has an Avalonia port that runs on Linux. Phase D
(`plans/04-phase-d-csharp-tools.md`) is also closed — every C# project
that had a `.csproj` was upgraded to SDK-style `net10.0-windows` and
builds on a modern dotnet SDK; per-tool diff status lives in
`tools/BUILD_STATUS.md`.

The matrix below is the runtime story (not the build story — every C#
project below builds via `dotnet build tools/Net7Tools.slnx`):

| Tool | Avalonia? | Linux runtime |
|---|:-:|:-:|
| commontools | ✅ shared lib | n/a |
| dataimport | ✅ | ✅ |
| effect-editor | ✅ | ✅ |
| enb-ini-parser | n/a (console) | ✅ |
| enbpatcher | ✅ | ✅ |
| faction-editor | ✅ | ✅ |
| itemeditor | ❌ (no upstream csproj) | ❌ WinForms only |
| launchnet7 | ✅ | ✅ |
| missioneditor | ✅ | ✅ |
| mob-editor | ✅ | ✅ |
| sector-editor | ✅ | ✅ |
| station-tools | ✅ | ✅ |
| talktreeeditor | ✅ | ✅ |
| toolslauncher | ✅ | ✅ |
| toolspatcher | ✅ | ✅ |
| chunktypes | ❌ (legacy C++) | ❌ |
| udpdump | ❌ (legacy C++) | ❌ |
| unmix | ❌ (legacy C++) | ❌ |
| w3d-parser | n/a (not user-facing) | n/a |
| xml-exporter | ❌ (legacy C++) | ❌ |

## Runtime requirements

For the Avalonia editors (the recommended path):

- Any modern Linux distro with the .NET 10 SDK or runtime installed.
  Also runs on macOS and Windows — Avalonia is cross-platform.
- A MySQL or MySQL-compatible server reachable on the network. The
  default dev stack runs `mysql:8.0` on `localhost:3307` via
  `docker-compose.yml`.

For the legacy WinForms editors (`tools/<name>/` without `-avalonia`):

- Windows 10 or 11, or WINE 9+ with the .NET 10 Desktop Runtime inside
  the WINE prefix.
- The .NET 10 Desktop Runtime (Microsoft-supplied; not the cross-platform
  ASP.NET runtime).
- Same MySQL/PostgreSQL connectivity as above.

For console tools (`enb-ini-parser`, `FileListCreator`, `ExeUpdater`):
.NET 10 runtime. Some reference `System.Windows.Forms` and remain
Windows-only as written.

For the legacy C++ utilities: a Win32 toolchain (MSYS2 + MinGW or
Visual Studio Build Tools). Trivially portable in principle, not
prioritised.
