# tools/ — C# editor suite

This directory holds the C# editors and helper utilities used to build /
inspect / modify Net-7 game data. They were originally authored against
Visual Studio 2008, .NET Framework 2.0–3.5, WinForms. Phase D migrated
them to **.NET 10** SDK-style csproj targeting `net10.0-windows` with
`<UseWindowsForms>true</UseWindowsForms>`.

## Build (cross-platform)

```sh
dotnet build tools/Net7Tools.slnx
```

On Linux you need .NET SDK 10.0 (`apt install dotnet-sdk-10.0` or via
`dotnet-install.sh`). The build produces Windows-only binaries.

All 16 C# projects build cleanly under Phase D. Build warnings
(currently ~437) are mostly `CA1416` (Windows-only WinForms APIs called
from a `net10.0-windows` TFM — which is fine; the binaries are
Windows-only at runtime). The full per-project status is in
`BUILD_STATUS.md`.

## Run (Windows-only at runtime)

The editors are WinForms apps and require a Windows environment to run.
Options on Linux:

- Run the published binaries under WINE + the WINE-bundled .NET runtime.
  (Works for some editors; UI fidelity varies.)
- Run a Windows VM and shell-in.
- Use a Windows workstation.

The data-pipeline tools (`dataimport`, `xml-exporter`, `chunktypes`,
`enb-ini-parser`, `udpdump`, `unmix`, `w3d-parser`) don't structurally
need WinForms; future work could retarget them to `net10.0`.

## The tools

### C# projects (in Net7Tools.slnx)

| Project | Purpose |
|---|---|
| `commontools`      | Shared library used by the editors |
| `dataimport`       | Bulk import of game data into the DB |
| `effect-editor` (SQLBind) | Visual / particle effect authoring |
| `enb-ini-parser`   | Parse the client `.ini` config files (console) |
| `faction-editor`   | NPC faction relationships |
| `launchnet7/ExeUpdater` | Launcher's exe-replacement helper |
| `launchnet7/FileListCreator` | Manifest generator for the launcher |
| `launchnet7/LaunchNet7` | Game launcher (current) |
| `missioneditor`    | Mission / quest authoring |
| `mob-editor`       | Mob (NPC) data |
| `sector-editor`    | Sector / map authoring |
| `station-tools`    | Station authoring |
| `talktreeeditor`   | NPC dialog tree authoring |
| `toolslauncher`    | Front-end that launches the other editors |
| `toolspatcher`     | Patches the tools themselves |
| `w3d-parser`       | Parse Westwood 3D model files |

### C# tools without csproj (deferred)

These have C# source but the original repo never shipped a csproj.
They would need a new SDK-style csproj written from scratch:

- `enbpatcher` — apply client patches (single Form1 + crc32)
- `itemeditor` — item / inventory data editor (has app.config + forms)

### C++ tools (not part of Phase D)

These are misnamed as "tools" but are actually pre-2010 C++ utilities
with `.dsp` (VS6) project files. They are not part of the .NET 10
migration — modernizing them would be a separate phase:

- `chunktypes`     — Inspect / dump CHUNK file format types
- `launchnet7-old` — Legacy MFC launcher
- `udpdump`        — Capture / inspect game UDP traffic (uses Westwood RSA)
- `unmix`          — Unpack `.MIX` archive files
- `xml-exporter`   — Export DB tables to XML

## Phase D translation patterns

The conversion was driven by `tools/convert_csproj.py`. Patterns:

- Old-style csproj (`<Project ToolsVersion="3.5" ...>`) → SDK-style
  (`<Project Sdk="Microsoft.NET.Sdk">`).
- `TargetFrameworkVersion=v3.5` → `TargetFramework=net10.0-windows`
  (WinForms apps) or `net10.0` (console).
- `<OutputType>WinExe</OutputType>` implies `<UseWindowsForms>true</UseWindowsForms>`.
- Stripped framework refs (`System`, `System.Core`, `System.Xml`,
  `System.Data`, `System.Windows.Forms`, `System.Drawing`,
  `System.Deployment`, `System.Design`, `System.Drawing.Design`) — the
  SDK provides them via `<UseWindowsForms>`.
- `<Reference Include="MySql.Data, Version=5.2.5.0, ...">` →
  `<PackageReference Include="MySql.Data" Version="9.4.0" />` (Oracle's
  current NuGet, drop-in API).
- Vendored DLL refs (`Meebey.SmartIrc4net`, `SandDock`,
  `UMD.HCIL.Piccolo`, `LaMarvin.Windows.Forms.ColorPicker`, `log4net`)
  rewritten as `<Reference>` with HintPath into
  `tools/commontools/Libs/release/`.
- `<ProjectReference>` paths case-corrected for case-sensitive
  filesystems (`..\CommonTools\` → `../commontools/`) and slashes
  normalised to forward.
- The old `<Compile Include="...">` lists were dropped in favour of the
  SDK's automatic globbing. Where the SDK glob picked up files the
  original csproj never compiled (legacy `Design/`, vendored
  `Libs/SmartIrc4Net/`, duplicate-class `Enumerations.cs`, typo
  `TexturesChunke.cs`, etc.), per-project `<Compile Remove>` rules were
  added — see the individual csproj files.

### Solution-wide settings: `Directory.Build.props`

- `EnableWindowsTargeting=true` lets `net10.0-windows` build on Linux.
- A large `NoWarn` list silences the categories of warning that 2008-era
  C# triggers under modern Roslyn (CS0108 hides-by-name, CS0414/19/68/69
  unused, CS0612/18 obsolete, CA1416 Windows-only APIs, SYSLIB* obsolete).
- `WarningsNotAsErrors` demotes WFO1000/WFO2001 (WinForms designer
  analyzer) from error to warning where the analyzer fires on existing
  code patterns.
- `RuntimeIdentifiers=win-x64;win-x86` — runtime targets.
- `DefaultItemExcludes` excludes vendored library sources (`Libs/**`)
  and the `*.old` backup csprojs left next to the converted ones.

## Vendored binaries

See `tools/THIRD_PARTY_BINARIES.md` for the list of vendored DLLs (some
editors ship .NET assemblies we don't have source for; the file records
provenance and license info for each).

## Old csproj backups

The original `*.csproj` files are kept beside the new ones as
`*.csproj.old` for reference / diff. Once the migration has soaked in
production they can be deleted.
