# tools/ — C# editor suite

The Net-7 game-data editors. Each editor reads/writes the live MySQL
database (or game-asset files); together they're what content authors
used to add abilities, mobs, missions, sectors, factions, dialog trees,
particle effects, vendor inventories, etc.

All user-facing editors are **Avalonia 11 / .NET 10** ports that run
**natively on Linux** (no WINE). The original WinForms projects have
been removed; the Avalonia ports are the only versions.

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
just launch-net7              # game client launcher (Freya)
just launch-enbpatcher        # client patcher
just launch-toolspatcher      # patcher for the editors themselves
```

`just --list` shows them all. Each recipe just runs
`dotnet run --project tools/<name>/`. First run rebuilds on
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
`plans/12-phase-l.md`. Current state:

| Tool                  | Talks to DB? | Notes |
|---|:-:|---|
| `commontools`     | n/a | Login dialog + DB layer used by the others |
| `dataimport`      | ✅ | Bulk-load game data |
| `effect-editor`   | ✅ | Particle / stat effects |
| `enbpatcher`      |    | Client binary patcher |
| `faction-editor`  | ✅ | NPC faction matrix |
| `LaunchFreya`              |    | Game client launcher |
| `missioneditor`   | ✅ | Mission / quest authoring |
| `mob-editor`      | ✅ | Mob (NPC) data |
| `sector-editor`   | ✅ | Sector / map authoring (Piccolo-on-Avalonia canvas) |
| `station-tools`   | ✅ | Station / vendor / NPC authoring |
| `talktreeeditor`  |    | NPC dialog trees (XML in/out) |
| `toolslauncher`   |    | The central GUI launcher |
| `toolspatcher`    |    | Patcher for the tools themselves |
| `item-editor`     | ✅ | Item editor (the upstream WinForms project had no csproj) |
| `chunktypes`               |    | 2010-era C++ utility (`.dsp`) -- not in scope |
| `udpdump`                  |    | 2010-era C++ utility (`.dsp`) -- not in scope |
| `unmix`                    |    | 2010-era C++ utility (`.dsp`) -- not in scope |
| `w3d-parser`               |    | 2010-era C# utility -- not user-facing |
| `xml-exporter`             |    | 2010-era C++ utility (`.dsp`) -- not in scope |

## Building everything (without running)

```sh
# Build all Avalonia ports (any * project picks up the rest via solution refs).
dotnet build tools/toolslauncher/
```

Or build the full solution:

```sh
dotnet build tools/FreyaTools.slnx
```

## Vendored binaries

See `tools/THIRD_PARTY_BINARIES.md` for notes on third-party DLLs from
the original WinForms projects. The Piccolo.NET dependency was replaced in
the Avalonia sector editor by a shim under
`tools/sector-editor/PiccoloShim/`.
