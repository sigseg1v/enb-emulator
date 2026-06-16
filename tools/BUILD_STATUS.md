# tools/ build status

All user-facing editors are **Phase L Avalonia ports** (`tools/<name>-avalonia/`,
`net10.0`). They run natively on Linux. Tracked in
`plans/12-phase-l-avalonia.md`. Status table in `tools/README.md`.

The original WinForms projects (`tools/<name>/`, `net10.0-windows`) have
been removed. This file is kept as a historical record of the Phase D
port status at the time of removal.

Run `dotnet build <project>` from the repo root, or
`dotnet build tools/FreyaTools.slnx` for the full solution.

## Phase D -- legacy WinForms (historical reference, projects removed)

| Project | Build (at time of removal) | Warnings |
|---|---|---|
| `tools/commontools/CommonTools.csproj` | built | 0 |
| `tools/dataimport/DataImport.csproj` | built | 0 |
| `tools/effect-editor/SQLBind/SQLBind.csproj` | built | 0 |
| `tools/enb-ini-parser/EnB Ini Parser.csproj` | built | 0 |
| `tools/faction-editor/Net7 Faction Editor.csproj` | built | 0 |
| `tools/launchnet7/ExeUpdater/ExeUpdater.csproj` | built | 0 |
| `tools/launchnet7/FileListCreator/FileListCreator.csproj` | built | 0 |
| `tools/launchnet7/LaunchNet7/LaunchNet7.csproj` | built | 237 |
| `tools/missioneditor/MissionEditor.csproj` | built | 0 |
| `tools/mob-editor/N7 Mob Editor.csproj` | built | 0 |
| `tools/sector-editor/Net7 Sector Editor.csproj` | built | 0 |
| `tools/station-tools/Station Tools.csproj` | built | 1 |
| `tools/talktreeeditor/TalkTreeEditor/TalkTreeEditor.csproj` | built | 0 |
| `tools/toolslauncher/ToolsLauncher/ToolsLauncher.csproj` | built | 196 |
| `tools/toolspatcher/ToolsPatcher.csproj` | built | 0 |
| `tools/w3d-parser/W3d Parser.csproj` | built | 0 |

`tools/itemeditor/` had no `.csproj` in the upstream snapshot, so it was
in neither matrix. Phase L ported the item editor as `tools/item-editor-avalonia/`.
