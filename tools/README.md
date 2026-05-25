# tools/ — C# editor suite

The Net-7 game-data editors. Each editor reads/writes the live MySQL
database (or game-asset files); together they're what content authors
used to add abilities, mobs, missions, sectors, factions, dialog trees,
particle effects, vendor inventories, etc.

There are **two parallel sets** of projects in this directory:

- **`<tool>-avalonia/`** — Phase L ports targeting `net10.0` + **Avalonia 11**.
  These run **natively on Linux** (no WINE) and are the recommended path.
- **`<tool>/`** — original Phase D ports of the 2008-era WinForms code, targeting
  `net10.0-windows`. Cross-compiles, but the binaries only run on Windows
  or under WINE. Kept for reference / diff during the migration.

If you're not sure which one to use, **use the Avalonia version**.

## Quickstart

Get the dev DB up first (editors that talk to the DB need it):

```sh
just init           # boots mysql + loads dumps  (~30s)
```

Then either launch the central launcher GUI…

```sh
just launch         # button-per-editor menu
```

…or jump straight to a specific editor:

```sh
just launch-mob-editor
just launch-faction-editor
just launch-sector-editor
just launch-mission-editor
just launch-talktree-editor
just launch-station-tools
just launch-effect-editor
just launch-dataimport
just launch-net7              # game client launcher (LaunchNet7)
just launch-enbpatcher        # client patcher
just launch-toolspatcher      # patcher for the editors themselves
```

`just --list` shows them all. Each recipe just runs
`dotnet run --project tools/<name>-avalonia/`. First run rebuilds on
demand; subsequent runs start in a few seconds.

Editors that talk to MySQL pop a Login dialog on startup. For the dev
stack the defaults are:

| Field | Value |
|---|---|
| Host | `localhost` |
| Port | `3307` |
| User | `net7` |
| Password | `net7` |
| Database | `net7` (or `net7_user` for the accounts schema) |

## Editor status (Phase L — complete)

Phase L closed with 13 editor ports landed; per-tool status:
`plans/12-phase-l-avalonia.md`. Current state:

| Tool                  | Avalonia? | Talks to DB? | Notes |
|---|:-:|:-:|---|
| `commontools`         | ✅ shared lib | n/a | Login dialog + DB layer used by the others |
| `dataimport`          | ✅ | ✅ | Bulk-load game data |
| `effect-editor`       | ✅ | ✅ | Particle / stat effects |
| `enbpatcher`          | ✅ |    | Client binary patcher |
| `faction-editor`      | ✅ | ✅ | NPC faction matrix |
| `launchnet7`          | ✅ |    | Game client launcher |
| `mission-editor`      | ✅ | ✅ | Mission / quest authoring |
| `mob-editor`          | ✅ | ✅ | Mob (NPC) data |
| `sector-editor`       | ✅ | ✅ | Sector / map authoring (Piccolo-on-Avalonia canvas) |
| `station-tools`       | ✅ | ✅ | Station / vendor / NPC authoring |
| `talktreeeditor`      | ✅ |    | NPC dialog trees (XML in/out) |
| `toolslauncher`       | ✅ |    | The central GUI launcher |
| `toolspatcher`        | ✅ |    | Patcher for the tools themselves |
| `item-editor`         | ❌ | ✅ | Original `tools/itemeditor/` never had a csproj |
| `chunktypes`          | ❌ |    | 2010-era C++ utility (`.dsp`) — out of Phase D/L scope |
| `udpdump`             | ❌ |    | 2010-era C++ utility (`.dsp`) — out of Phase D/L scope |
| `unmix`               | ❌ |    | 2010-era C++ utility (`.dsp`) — out of Phase D/L scope |
| `w3d-parser`          | ❌ |    | 2010-era C# utility — not user-facing |
| `xml-exporter`        | ❌ |    | 2010-era C++ utility (`.dsp`) — out of Phase D/L scope |

## Building everything (without running)

```sh
# Build the Avalonia ports (any *-avalonia project picks up the rest via solution refs).
dotnet build tools/toolslauncher-avalonia/

# Build the legacy WinForms tools (cross-compiles; binaries are Windows-only).
just build-tools
```

## Legacy WinForms route (not recommended on Linux)

The `tools/<name>/` projects (without `-avalonia`) were the Phase D
attempt to retarget the 2008-era WinForms code to `net10.0-windows`.
They build cross-platform (`dotnet build tools/Net7Tools.slnx`) but
their runtime story on Linux is "WINE + .NET runtime" — varying UI
fidelity, no real integration. Phase L superseded them with the
Avalonia ports above. See `BUILD_STATUS.md` for the Phase D status
table.

If you're running these on Windows or under WINE, point them at the
same `localhost:3307` MySQL the Avalonia editors use.

## Vendored binaries

See `tools/THIRD_PARTY_BINARIES.md` for the list of vendored DLLs the
WinForms ports reference (Piccolo, SandDock, SmartIrc4Net, etc.). The
Avalonia ports drop most of these — the Piccolo dep was rewritten as a
shim against Avalonia primitives under `tools/sector-editor-avalonia/PiccoloShim/`.

## Old csproj backups

The original `*.csproj` files under `tools/<name>/` are kept beside the
new ones as `*.csproj.old` for reference / diff. Safe to delete once
the Phase D path is fully retired (currently kept because some content
authors still run Windows).
