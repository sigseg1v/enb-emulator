# Phase D — C# tools → .NET 10

Goal: upgrade every project under `tools/` from .NET Framework 4.x (old-style csproj) to .NET 10 SDK-style, targeting `net10.0-windows` with `<UseWindowsForms>true</UseWindowsForms>`. Build is cross-platform via `dotnet build`; runtime stays Windows-only.

## Per-tool checklist

For each tool below, the checkbox sequence is: (a) SDK-style csproj, (b) targets net10.0-windows, (c) `dotnet build` (with `Net7Tools.sln`) succeeds or has documented failures, (d) any obsolete API usages fixed (`ConfigurationManager`, `BinaryFormatter`, etc.), (e) DB providers swapped (`MySql.Data` → `MySqlConnector` and where it makes sense, add `Npgsql`).

- [ ] `tools/sector-editor/` (Net7 Sector Editor)
- [ ] `tools/mob-editor/` (Net7 Mob Editor)
- [ ] `tools/mission-editor/`
- [ ] `tools/faction-editor/` (Net7 Faction Editor)
- [ ] `tools/item-editor/` (ItemEditor)
- [ ] `tools/effect-editor/` (Effect Editor)
- [ ] `tools/talktree-editor/` (TalkTreeEditor)
- [ ] `tools/station-tools/`
- [ ] `tools/enb-patcher/` (EnBPatcher)
- [ ] `tools/launch-net7/` (LaunchNet7)
- [ ] `tools/launch-net7-old/` (LaunchNet7 (old)) — likely defer, mark as archived
- [ ] `tools/chunk-types/`
- [ ] `tools/w3d-parser/` — heavy P/Invoke risk; may stay on old framework
- [ ] `tools/xml-exporter/`
- [ ] `tools/unmix/`
- [ ] `tools/udp-dump/`
- [ ] `tools/common-tools/` (CommonTools)
- [ ] `tools/data-import/` (DataImport)
- [ ] `tools/enb-ini-parser/` (EnB Ini Parser)
- [ ] `tools/tools-launcher/` (ToolsLauncher)
- [ ] `tools/tools-patcher/` (ToolsPatcher)

## Solution file

- [ ] `tools/Net7Tools.sln` regenerated to reference all new csprojs (`dotnet sln add`)
- [ ] `dotnet build tools/Net7Tools.sln` runs end-to-end (failures listed in `tools/BUILD_STATUS.md`)

## Cross-cutting

- [ ] Strip per-developer files: `*.suo`, `*.user`, `obj/` from version control (handled by `.gitignore`).
- [ ] Each tool's directory README updated with build status.
- [ ] `tools/README.md` overview + runtime caveat (Windows-only at runtime).

## Verification

- `dotnet --list-sdks` shows 10.x present.
- `dotnet build tools/Net7Tools.sln 2>&1 | tee tools/BUILD_STATUS.md` runs; status doc committed.
- Proceed to Phase E without stopping.
