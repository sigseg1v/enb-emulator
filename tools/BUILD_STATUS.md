# tools/ build status

Two parallel sets of projects live under `tools/`:

- **Phase L — Avalonia ports** (`tools/<name>-avalonia/`, `net10.0`).
  Recommended. Run natively on Linux. Tracked in
  `plans/12-phase-l-avalonia.md`. Status table in `tools/README.md`.
- **Phase D — legacy WinForms ports** (`tools/<name>/`,
  `net10.0-windows`). Cross-compile; runtime is Windows / WINE only.
  Build status below.

Run `dotnet build <project>` from the repo root, or
`dotnet build tools/Net7Tools.slnx` for the full solution.

## Phase D — legacy WinForms (still build clean)

| Project | Build | Warnings |
|---|---|---|
| `tools/commontools/CommonTools.csproj` | builds | 0 |
| `tools/dataimport/DataImport.csproj` | builds | 0 |
| `tools/effect-editor/SQLBind/SQLBind.csproj` | builds | 0 |
| `tools/enb-ini-parser/EnB Ini Parser.csproj` | builds | 0 |
| `tools/faction-editor/Net7 Faction Editor.csproj` | builds | 0 |
| `tools/launchnet7/ExeUpdater/ExeUpdater.csproj` | builds | 0 |
| `tools/launchnet7/FileListCreator/FileListCreator.csproj` | builds | 0 |
| `tools/launchnet7/LaunchNet7/LaunchNet7.csproj` | builds | 237 |
| `tools/missioneditor/MissionEditor.csproj` | builds | 0 |
| `tools/mob-editor/N7 Mob Editor.csproj` | builds | 0 |
| `tools/sector-editor/Net7 Sector Editor.csproj` | builds | 0 |
| `tools/station-tools/Station Tools.csproj` | builds | 1 |
| `tools/talktreeeditor/TalkTreeEditor/TalkTreeEditor.csproj` | builds | 0 |
| `tools/toolslauncher/ToolsLauncher/ToolsLauncher.csproj` | builds | 196 |
| `tools/toolspatcher/ToolsPatcher.csproj` | builds | 0 |
| `tools/w3d-parser/W3d Parser.csproj` | builds | 0 |

`tools/itemeditor/` has no `.csproj` in the upstream snapshot, so it is
in neither matrix. Phase L did not produce an Avalonia port for the same
reason.
