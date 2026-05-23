# tools/ — C# editor suite

This directory holds the 21 C# editors and helper utilities used to
build / inspect / modify Net-7 game data. They were originally
authored against Visual Studio 2008, .NET Framework 2.0-3.5, WinForms.
Phase D migrates them to **.NET 10** SDK-style csproj targeting
`net10.0-windows` with `<UseWindowsForms>true</UseWindowsForms>`.

## Build (cross-platform)

You can _build_ the suite anywhere the .NET SDK runs:

```sh
dotnet build tools/Net7Tools.sln
```

On Linux you need .NET SDK 10.0 (`apt install dotnet-sdk-10.0` or via
`dotnet-install.sh`). The build produces Windows-only binaries.

**Before Phase D is complete**, `dotnet build` will fail because the
csproj files are still in the old MSBuild 2008 format. CI has this job
under `continue-on-error: true` until the migration lands.

## Run (Windows-only at runtime)

The editors are WinForms apps and require a Windows environment to run.
Options on Linux:

- Run the published binaries under WINE + the WINE-bundled .NET runtime.
  (Works for some editors; UI fidelity varies.)
- Run a Windows VM and shell-in.
- Use a Windows workstation.

Headless / cross-platform variants for the data-pipeline tools
(`dataimport`, `xml-exporter`, `chunktypes`, `enb-ini-parser`,
`udpdump`, `unmix`, `w3d-parser`) are tracked as a Phase D-or-later
deliverable — those don't structurally need WinForms.

## The 21 tools

| Tool | Purpose |
|---|---|
| `chunktypes`       | Inspect / dump CHUNK file format types |
| `commontools`      | Shared library used by the editors |
| `dataimport`       | Bulk import of game data into the DB |
| `effect-editor`    | Visual / particle effect authoring |
| `enb-ini-parser`   | Parse the client `.ini` config files |
| `enbpatcher`       | Apply client patches |
| `faction-editor`   | NPC faction relationships |
| `itemeditor`       | Item / inventory data |
| `launchnet7`       | Game launcher (current) |
| `launchnet7-old`   | Game launcher (legacy, retained for reference) |
| `missioneditor`    | Mission / quest authoring |
| `mob-editor`       | Mob (NPC) data |
| `sector-editor`    | Sector / map authoring |
| `station-tools`    | Station authoring |
| `talktreeeditor`   | NPC dialog tree authoring |
| `toolslauncher`    | Front-end that launches the other editors |
| `toolspatcher`     | Patches the tools themselves |
| `udpdump`          | Capture / inspect game UDP traffic |
| `unmix`            | Unpack `.MIX` archive files |
| `w3d-parser`       | Parse Westwood 3D model files |
| `xml-exporter`     | Export DB tables to XML |

## Vendored binaries

See `tools/THIRD_PARTY_BINARIES.md` for the list of vendored DLLs (some
editors ship .NET assemblies we don't have source for; the file records
provenance and license info for each).

## Phase D notes

The migration plan is in `plans/04-phase-d-csharp-tools.md`. High-level:

1. Convert each `.csproj` to SDK-style (`<Project Sdk="Microsoft.NET.Sdk">`).
2. Set `<TargetFramework>net10.0-windows</TargetFramework>` and
   `<UseWindowsForms>true</UseWindowsForms>`.
3. Replace AssemblyInfo.cs duplication with SDK-managed attributes.
4. Confirm `dotnet build tools/Net7Tools.sln` succeeds on Linux.
5. Confirm a published Windows binary runs in a Windows VM for at
   least the launcher and one editor.
