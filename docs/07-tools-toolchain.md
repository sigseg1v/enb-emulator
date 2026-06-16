# 07 - Tools toolchain

The `tools/` directory holds the C# editor suite for the content database
plus a few legacy C++ utilities. All user-facing C# editors are
**Avalonia 11 / .NET 10** ports (`tools/<name>-avalonia/`) that run
**natively on Linux** (no WINE). The original WinForms projects have been
removed. The central solution is `tools/FreyaTools.slnx` (SDK-style XML
solution); each port also has its own `.csproj` and can be launched directly.

Every user-facing editor has an Avalonia port, including the Item Editor
(`tools/item-editor-avalonia/`). See `tools/README.md` for the
user-facing quickstart and the launch recipes.

## Quickstart

The dev DB needs to be running for editors that talk to the database:

```sh
just init                    # boots Postgres 16 + applies the schema
```

Then either launch the central GUI launcher:

```sh
just launch                  # toolslauncher-avalonia
```

Or jump straight to a specific editor:

```sh
just launch-mob-editor
just launch-sector-editor
just launch-mission-editor
just launch-faction-editor
just launch-effect-editor
just launch-item-editor
just launch-station-tools
just launch-talktree-editor
just launch-dataimport
just launch-net7             # game client launcher (Freya)
just launch-enbpatcher       # client patcher
just launch-toolspatcher     # patcher for the editor binaries
```

`just --list` prints every recipe. Each recipe runs
`dotnet run --project tools/<name>-avalonia/`.

Editors that hit the DB pop a Login dialog on startup; default dev creds
match the docker-compose stack: host `localhost`, port `5434`, user `net7`,
password `net7`, database `net7` (or `net7_user` for the accounts schema).

## Conventions used in this doc

- Folder paths are relative to `tools/`.
- "Type" is WinForms, Avalonia, console, or library.
- "Status" reflects the Avalonia port (the recommended runtime).

## Per-tool reference

### `commontools-avalonia/` -- CommonTools (shared library)

Type: library.
Purpose: shared DB connection, login dialog, enumerations, common widgets,
XML helpers. Every editor depends on this.
Notes: the login dialog is Avalonia XAML. The DB layer talks to Postgres
via Npgsql.

### `chunktypes/` -- ChunkTypes (legacy C++)

Type: console (Visual C++ 6 `.dsp`).
Purpose: dumps the chunk-type tree of a Westwood 3D (`.w3d`) file to text,
for offline asset inspection.
Status: legacy C++ utility; not in `FreyaTools.slnx`. Windows-only.

### `dataimport-avalonia/` -- DataImport

Type: Avalonia.
Purpose: bulk content imports into the database -- assets, skills, item
references. Reuses CommonTools' login dialog.
Launch: `just launch-dataimport`.

### `effect-editor-avalonia/` -- Effect Editor

Type: Avalonia.
Purpose: edit the `effects`, `item_effect_base`, `item_effect_container`,
and `buffs` tables.
Launch: `just launch-effect-editor`.

### `enb-ini-parser/` -- EnB Ini Parser

Type: console.
Purpose: parse client `.ini` files (extracted from the game data) and
import into the database: BaseAsset, BuffParser, EffectsParser,
SkillParser. The current `Main` runs `new SkillParser()` and exits;
other parsers are commented out.
Status: console tool, no Avalonia port needed; runs as-is on `dotnet`.

### `enbpatcher-avalonia/` -- EnBPatcher

Type: Avalonia.
Purpose: client-side patcher utility -- generates and applies CRC32 patches
to the client binary.
Launch: `just launch-enbpatcher`.

### `faction-editor-avalonia/` -- Faction Editor

Type: Avalonia.
Purpose: edit `factions`, `faction_matrix`, `manufacturers`.
Launch: `just launch-faction-editor`.

### `item-editor-avalonia/` -- Item Editor

Type: Avalonia.
Purpose: edit `item_base` and all `item_*` subtype tables (ammo, beam,
device, engine, missile, projectile, reactor, shield), plus
`item_manufacture`, `item_other_req`, `item_refine`, `item_effects`.
Launch: `just launch-item-editor`.
Notes: the upstream WinForms project (`tools/itemeditor/`) had no
`.csproj` and has been removed. The Avalonia port is the only version.

### `LaunchFreya/` -- Freya (game client launcher)

Type: Avalonia.
Purpose: bootstraps the EnB client (the original Win32 binary under
WINE). On the Windows build it self-updates: it hashes its own
`FreyaLauncher.exe` + `FreyaProxy.exe`, asks the login server's
`/updateCheck` endpoint whether they are current, and swaps the EXEs in
place when a newer release is published.
Launch: `just launch-net7`.
Notes: the WinForms launcher projects (`launchnet7/` and `launchnet7-old/`)
have been removed. The Avalonia launcher is the only one.

Windows distribution: `just package-client-windows` produces
`dist/enb-client-windows/` (and a `.zip`) holding the self-contained
`FreyaLauncher.exe` + `bin/FreyaProxy.exe` + a package-only
`FreyaLauncher.cfg`. That folder IS the install -- there is no installer
program (no Inno/NSIS) and no upstream Net-7 patcher: the player extracts
the zip and runs `FreyaLauncher.exe`, which from then on keeps itself and
the bundled `FreyaProxy.exe` current via the `/updateCheck` + CloudFront
self-updater described above (Phase AN). The EnB game client itself is a
separate, pre-existing install the launcher points at via `ClientPath`;
the Freya updater delivers only the launcher + proxy, never game data.
The Linux path (`client/linux-installer/`) is unrelated and still uses the
upstream WINE installer, which remains the EnB client-data delivery there.

### `missioneditor-avalonia/` -- Mission Editor

Type: Avalonia.
Purpose: edit `missions.mission_XML`. Renders the mission tree
(`Nodes`, `TalkNode.cs`, `Replies.cs`) and serialises back to XML.
Launch: `just launch-mission-editor`.

### `mob-editor-avalonia/` -- Mob Editor

Type: Avalonia.
Purpose: edit `mob_base`, `mob_items`, `mob_spawn_group`. Per-mob
property sheets, GUI, SQL split into folders.
Launch: `just launch-mob-editor`.

### `sector-editor-avalonia/` -- Sector Editor

Type: Avalonia.
Purpose: edit `systems`, `sectors`, `sector_objects` and subtype tables.
Three top-level windows (`SystemWindow`, `SectorWindow`, `UniverseWindow`)
plus a sidebar tree (`TreeWindow`). The original Piccolo.NET dependency
was replaced by `tools/sector-editor-avalonia/PiccoloShim/` -- a shim
that maps the small Piccolo subset the editor used onto Avalonia primitives,
so we don't carry a Windows-only third-party graphics library.
Launch: `just launch-sector-editor`.

### `station-tools-avalonia/` -- Station Tools

Type: Avalonia.
Purpose: edit `starbases`, `starbase_rooms`, `starbase_npcs`,
`starbase_npc_avatar_templates`, `starbase_terminals`,
`starbase_vendors`, `starbase_vender_*`. Bundles a TalkTree editor and
an item browse dialog.
Launch: `just launch-station-tools`.

### `talktreeeditor-avalonia/` -- TalkTree Editor

Type: Avalonia.
Purpose: edit and preview NPC dialogue trees in the XML format stored in
`starbase_npcs.talk_tree_handle`.
Launch: `just launch-talktree-editor`.

### `toolslauncher-avalonia/` -- Tools Launcher

Type: Avalonia.
Purpose: a launcher menu for the other editors. The central entry point
exposed by `just launch`.
Launch: `just launch`.

### `toolspatcher-avalonia/` -- Tools Patcher

Type: Avalonia.
Purpose: in-place patcher for the editor binaries. CRC32-checks each
binary, downloads the replacement, swaps. Counterpart to `enbpatcher-avalonia/`
but for the toolchain itself.
Launch: `just launch-toolspatcher`.

### `udpdump/` -- UdpDump (legacy C++)

Type: legacy C++ (Visual C++ 6 `.dsp`).
Purpose: decrypt and decode UDP captures from the game client, producing
`SectorContent.xml` containing parsed packet opcodes.
Status: legacy C++ utility; a useful reference for the protocol's
packet formats. Depends on `WestwoodRSA.cpp` / `WestwoodRC4.cpp` (the
same crypto now shared via `common/include/net7/`).

### `unmix/` -- Unmix (legacy C++)

Type: legacy C++ (Visual C++ 6 `.dsp`).
Purpose: extract files from a Westwood `.MIX` archive. Original author
VectoR.360, public domain (2004).
Status: legacy C++ utility; trivially portable to POSIX (small file).

### `w3d-parser/` -- W3d Parser

Type: library (C#).
Purpose: parse Westwood 3D (`.w3d`) files into a chunk tree. Pure managed
code, in principle cross-platform.
Status: not user-facing; no Avalonia port.

### `xml-exporter/` -- XML Exporter (legacy C++)

Type: legacy C++ (Visual C++ 6 `.dsp`).
Purpose: exports the item subsystem from MySQL to XML files (and the
reverse). Ships a bundled MySQL client library tree at
`tools/xml-exporter/mysql/`. Predates the Postgres cutover and still
speaks MySQL.
Status: legacy C++ utility; Windows-only.

## Content pipeline

The end-to-end flow from "designer wants to add content" to "server serves
it":

1. **Editor**: a designer opens the appropriate Avalonia editor (Sector,
   Mob, Item, Mission, Faction, Effect, Station Tools, TalkTree). The
   editor connects to Postgres via CommonTools' login dialog.
2. **Edit**: changes are committed to the live content database. The audit
   table `table_changes` records what changed, by whom, when, with full
   before/after payloads.
3. **Validation**: some editors run client-side validation (e.g.
   `item-editor-avalonia/Database/DataValidation.cs`) before writing.
4. **Database**: data lives in Postgres. The runtime schema is
   `db/postgres/schema.sql`; the `db/mysql/` dumps are the historical
   source the schema was converted from. World content (the `net7`
   database) is read-mostly; per-player state (the `net7_user` database)
   is read-write.
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

## Build status

Every user-facing editor is an Avalonia port that runs natively on
Linux. Every C# project that has a `.csproj` is SDK-style and builds on a
modern dotnet SDK; historical Phase D status lives in `tools/BUILD_STATUS.md`.

| Tool | Linux runtime |
|---|:-:|
| commontools-avalonia (shared lib) | n/a |
| dataimport-avalonia | Yes |
| effect-editor-avalonia | Yes |
| enb-ini-parser (console) | Yes |
| enbpatcher-avalonia | Yes |
| faction-editor-avalonia | Yes |
| item-editor-avalonia | Yes |
| LaunchFreya | Yes |
| missioneditor-avalonia | Yes |
| mob-editor-avalonia | Yes |
| sector-editor-avalonia | Yes |
| station-tools-avalonia | Yes |
| talktreeeditor-avalonia | Yes |
| toolslauncher-avalonia | Yes |
| toolspatcher-avalonia | Yes |
| chunktypes (legacy C++) | No |
| udpdump (legacy C++) | No |
| unmix (legacy C++) | No |
| w3d-parser (not user-facing) | n/a |
| xml-exporter (legacy C++) | No |

## Runtime requirements

For the Avalonia editors:

- Any modern Linux distro with the .NET 10 SDK or runtime installed.
  Also runs on macOS and Windows -- Avalonia is cross-platform.
- A Postgres server reachable on the network. The default dev stack runs
  `postgres:16` on `localhost:5434` via `docker-compose.yml`.

For console tools (`enb-ini-parser`): .NET 10 runtime.

For the legacy C++ utilities: a Win32 toolchain (MSYS2 + MinGW or
Visual Studio Build Tools). Trivially portable in principle, not
prioritised.
