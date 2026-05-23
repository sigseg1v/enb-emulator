# Phase D — C# tools → .NET 10

Goal: upgrade every C# project under `tools/` from .NET Framework 4.x (old-style csproj) to .NET 10 SDK-style, targeting `net10.0-windows` with `<UseWindowsForms>true</UseWindowsForms>`. Build is cross-platform via `dotnet build`; runtime stays Windows-only.

## Outcome

**All 16 csproj projects build cleanly under .NET 10 SDK 10.0.107.**
`dotnet build tools/Net7Tools.slnx` returns 0, 437 warnings (mostly CA1416 — Windows-only WinForms API usage from a `net10.0-windows` TFM, which is fine since runtime is Windows-only).

Per-tool status in `tools/BUILD_STATUS.md`. Conversion driver is `tools/convert_csproj.py`. Solution-wide settings are in `tools/Directory.Build.props`.

## Per-tool checklist

For each tool below, the checkbox sequence is: (a) SDK-style csproj, (b) targets net10.0-windows, (c) `dotnet build` succeeds, (d) any obsolete API usages fixed, (e) DB providers swapped (`MySql.Data` 5.x → 9.x NuGet).

- [x] `tools/commontools/` (CommonTools shared library)
- [x] `tools/sector-editor/` (Net7 Sector Editor)
      Notes: SDK glob would have picked up `Design/`, bare `Sprites/*.cs` (SdlDotNet drafts), `Sql/Helpers.cs` (duplicate `SQLData`), `Sql/MobConvertSQL.cs` (references removed enum). Per-project `<Compile Remove>` added. Created lowercase `images/` symlinks to `Images/` so the Resources.resx (which references lowercase paths) resolves on Linux. `licenses.licx` excluded (LC task unsupported under .NET Core MSBuild).
- [x] `tools/mob-editor/` (Net7 Mob Editor)
- [x] `tools/missioneditor/`
      Notes: ProjectReference to talktreeeditor restored with corrected case (`../talktreeeditor/TalkTreeEditor/`).
- [x] `tools/faction-editor/` (Net7 Faction Editor)
- [x] `tools/effect-editor/SQLBind/` (Effect Editor SQLBind library)
- [x] `tools/talktreeeditor/TalkTreeEditor/`
- [x] `tools/station-tools/`
- [x] `tools/launchnet7/LaunchNet7/`
      Notes: `FormUpdate.cs` AreDetailsVisible property got `[DesignerSerializationVisibility(Hidden)]` to suppress WFO1000 (which is hard-error in current SDK).
- [x] `tools/launchnet7/ExeUpdater/`
- [x] `tools/launchnet7/FileListCreator/`
- [x] `tools/toolslauncher/ToolsLauncher/`
      Notes: SmartIrc4Net references resolved via HintPath into `tools/commontools/Libs/release/Meebey.SmartIrc4net.dll`. `System.Diagnostics.Debug.WriteLine` qualified (using directive was missing). `Struct Data/FileLinkList.cs` kept (original csproj used this; root-level `FileLinkList.cs` is a duplicate, removed via Compile Remove). Same `[DesignerSerializationVisibility(Hidden)]` patch as launchnet7.
- [x] `tools/toolspatcher/`
- [x] `tools/w3d-parser/`
      Notes: `Chunks/TexturesChunke.cs` (typo'd duplicate of `TexturesChunk.cs`) excluded via Compile Remove.
- [x] `tools/dataimport/`
- [x] `tools/enb-ini-parser/` (console)
      Notes: Pilot conversion. Console app (no WinForms); targets `net10.0`. Vendored `lib/IniFiles.dll` referenced via HintPath.

### Deferred (no csproj at all in upstream — needs SDK-style csproj written from scratch)

- [!] `tools/itemeditor/`
      Reason: Upstream tada-o never shipped a csproj; only loose `.cs` + `app.config` + forms. Needs a new SDK-style csproj written manually. Defer to Phase D continuation.
- [!] `tools/enbpatcher/`
      Reason: Same as itemeditor — has `EnBPatcher.sln` but no `.csproj`. Needs SDK-style csproj written manually.

### Not in scope (C++ tools, not C#)

These were originally listed as Phase D items but are actually pre-2010 C++ utilities with `.dsp` files (Visual Studio 6 era). They need a separate phase entirely:

- [!] `tools/chunktypes/`     — C++
- [!] `tools/unmix/`          — C++
- [!] `tools/udpdump/`        — C++ (uses Westwood RSA, same code as udpdump in server/src/mysql/)
- [!] `tools/xml-exporter/`   — C++
- [!] `tools/launchnet7-old/` — C++ MFC

## Solution file

- [x] `tools/Net7Tools.slnx` (modern slnx format) regenerated to reference all 16 new csprojs (`dotnet sln add`).
- [x] `dotnet build tools/Net7Tools.slnx` runs end-to-end with 0 errors. Per-project status in `tools/BUILD_STATUS.md`.

## Cross-cutting

- [x] `*.suo`, `*.user`, `obj/`, `bin/` already in `.gitignore`.
- [x] `tools/README.md` rewritten with build status, per-tool table, and the Phase D conversion patterns.
- [x] Per-project Notes captured above (above each checkbox).
- [x] `tools/convert_csproj.py` committed — bulk converter for the legacy csproj format. Re-runnable; preserves the `*.csproj.old` backup.
- [x] `tools/Directory.Build.props` committed — solution-wide build settings.

## Verification

- `dotnet --list-sdks` → 10.0.107 present.
- `dotnet build tools/Net7Tools.slnx` → `Build succeeded. 437 Warning(s) 0 Error(s)`.
- `tools/BUILD_STATUS.md` committed with per-project status.
- Proceed to Phase E.
