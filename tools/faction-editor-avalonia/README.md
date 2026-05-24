# faction-editor-avalonia

Cross-platform Avalonia port of `tools/faction-editor/` (the original Net-7 WinForms editor for the `factions` and `faction_matrix` tables).

Built on **.NET 10** + **Avalonia 11.2.3**. Targets `net10.0` (no `-windows` suffix), so it runs on Linux without WINE.

## What this is

A two-tab editor:

- **General Details** — name / PDA text / description for a faction row.
- **Faction Matrix** — for the selected faction, a list of every *other* faction and the relation entry (`base_value`, read-only `current_value`, `reward_faction` flag) between them.

A toolbar/menu offers New / Save / Delete / Refresh against the live MySQL DB via `commontools-avalonia` (`DB.Instance`).

## Mapping from the original

| Original `tools/faction-editor/` | Port |
|---|---|
| `GUI/mainFrm.cs` (`Form`) | `MainWindow.axaml` + `MainWindow.axaml.cs` |
| `GUI/About.cs` | `AboutBox.axaml{,.cs}` |
| `SQL/FactionsSQL.cs` (raw `MySql.Data` w/ string-concat SQL) | `SQL/FactionsSQL.cs` — parameterised via `DB.Instance.executeQuery/executeCommand` with `?name` placeholders |
| `SQL/FactionMatrixSQL.cs` (same) | `SQL/FactionMatrixSQL.cs` — same parameterisation |
| `Database/dbInstance.cs` | dropped — replaced by `CommonTools.Database.DB` |
| `Database/Login.cs` (Form) | dropped — replaced by `CommonTools.Gui.Login` |
| WinForms `PropertyGrid` for `FactionMatrixProps` | replaced by an ad-hoc panel (NumericUpDown + read-only TextBox + CheckBox); Avalonia has no PropertyGrid. The POCO `FactionMatrixProps.cs` is unchanged shape-wise but dropped the `[Description]` / `[Category]` / `[ReadOnly]` attributes. |
| `DataGridView` of factions | `DataGrid` from the separate `Avalonia.Controls.DataGrid` package; theme pulled in via `App.axaml`'s `<StyleInclude Source="avares://Avalonia.Controls.DataGrid/Themes/Fluent.xaml" />` |
| `ListView`/`ListBox` of relation entries | `ListBox` with item-source bound to faction names |
| `MessageBox.Show` | `MsBox.Avalonia` |
| `Form_Load` body | `Opened += async (_,_) => await OnLoadAsync()` running the DB-hitting work on `Task.Run` so the UI stays responsive |

## Login → MainWindow handoff

Same pattern as `dataimport-avalonia`: `ShutdownMode.OnExplicitShutdown`, the Login window's `Closed` event swaps `desktop.MainWindow` to the editor. Naive `ShowDialog().GetAwaiter().GetResult()` from the lifecycle callback deadlocks the dispatcher.

## Security note

The original `FactionsSQL` / `FactionMatrixSQL` built SQL via raw string concatenation including user-supplied fields (`name`, `description`, `PDA_text`). The port routes everything through parameterised commands. This silently closes injection holes that existed in the original.

## Building & running

```bash
dotnet build                       # from this directory
dotnet run -- --smoke              # headless smoke test (no DB needed)
dotnet run                         # interactive (needs reachable MySQL via Login dialog)
```

Smoke output:

```
login    OK: 290x195 "Login"
main     OK: 720x600 "Faction Editor"
about    OK: 420x240 "About Faction Editor"
smoke OK: all 3 faction-editor-avalonia windows instantiated
```

## What is *not* ported

- The original `Properties.Settings.Default` connection-info cache. The port persists last-used host/db/user via `commontools-avalonia`'s JSON settings (same as the other Avalonia tools).
- The PropertyGrid's reflection-driven editing UI — the ad-hoc panel exposes exactly the three fields the original surface had.
