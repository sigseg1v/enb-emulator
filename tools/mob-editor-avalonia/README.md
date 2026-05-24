# mob-editor-avalonia

Cross-platform Avalonia port of `tools/mob-editor/` (the original Net-7 WinForms editor for the `mobs` and `mob_items` tables).

Built on **.NET 10** + **Avalonia 11.2.3**. Targets `net10.0` (no `-windows` suffix), so it runs on Linux without WINE.

## What this is

A three-tab editor for one mob row at a time:

- **General Details** — name, level, type, faction, base asset (with a modal asset picker), scale, AI hint, and an HSL tint (H/S/V floats stored on the mob row) shown live as a colour swatch.
- **Equipped** — list of `mob_items` rows with `type=0` for the selected mob plus an Add-via-modal item picker; per-row usage chance / drop chance / quantity edits.
- **Inventory** — same UI as Equipped but for `type=1` rows.

A toolbar/menu exposes New / Copy / Save / Delete / Refresh against the live MySQL DB through `commontools-avalonia` (`DB.Instance`).

## Mapping from the original

| Original `tools/mob-editor/` | Port |
|---|---|
| `GUI/mainFrm.cs` (`Form`) | `MainWindow.axaml` + `MainWindow.axaml.cs` |
| `GUI/About.cs` | `AboutBox.axaml{,.cs}` |
| `GUI/MobBaseAssets.cs` (modal asset picker, thumbnailed `ListView` grouped by `main_cat`) | `MobBaseAssetsWindow.axaml{,.cs}` — flat `ListBox` of "`<base_id>: <descr>  (<filename>)`" |
| `GUI/ItemBaseAssets.cs` (modal item picker, thumbnailed `ListView` grouped by `level`) | `ItemBaseAssetsWindow.axaml{,.cs}` — flat `ListBox` + a Level filter `ComboBox` |
| `SQL/MobsSQL.cs` (raw `MySql.Data` w/ string-concat SQL) | `SQL/MobsSQL.cs` — parameterised via `DB.Instance.executeQuery/executeCommand` |
| `SQL/MobItemsSQL.cs` (same) | `SQL/MobItemsSQL.cs` — same parameterisation |
| `SQL/BaseAssetSQL.cs` | `SQL/BaseAssetSQL.cs` |
| `SQL/ItemBaseSQL.cs` | `SQL/ItemBaseSQL.cs` — note backticks on `2d_asset` (column name starts with a digit) |
| `SQL/FactionSql.cs` | `SQL/FactionSql.cs` |
| `Database/dbInstance.cs` | dropped — replaced by `CommonTools.Database.DB` |
| `Database/Login.cs` (Form) | dropped — replaced by `CommonTools.Gui.Login` |
| `Utilities/AdobeColors.cs` (425 LOC Photoshop-style picker, Win32-only) | `HslConvert.cs` — ~40 LOC HSL↔RGB just for the swatch |
| WinForms `PropertyGrid` for per-`mob_items` row props | replaced by an ad-hoc panel (NumericUpDown for usage / drop / qty) |
| `DataGridView` of mobs | `DataGrid` from `Avalonia.Controls.DataGrid` (theme pulled in via `App.axaml`'s `<StyleInclude Source="avares://Avalonia.Controls.DataGrid/Themes/Fluent.xaml" />`) |
| `ListView` grouped by category (mob assets) / level (item assets) with thumbnails | flat `ListBox` — see "Thumbnails dropped" below |
| `MessageBox.Show` | `MsBox.Avalonia` |
| `BackgroundWorker` | `Task.Run` + `Dispatcher.UIThread.Post` |
| `OpenFileDialog` | `StorageProvider.OpenFilePickerAsync` |
| `Form_Load` body | `Opened += async (_,_) => await OnLoadAsync()` running the DB-hitting work on `Task.Run` so the UI stays responsive |

## Login → MainWindow handoff

Same pattern as the other Avalonia tools: `ShutdownMode.OnExplicitShutdown`, the Login window's `Closed` event swaps `desktop.MainWindow` to the editor. Naive `ShowDialog().GetAwaiter().GetResult()` from the lifecycle callback deadlocks the dispatcher.

## Thumbnails dropped

The original asset / item pickers used `ListView` with `LargeIcon` view and `ListViewGroup` headers, pulling thumbnails from an `images/` tree that lives next to the editor binary. Our repo never shipped that `images/` tree, and Avalonia has no native grouped-thumbnail `ListView`, so the port presents a flat `ListBox` of human-readable strings plus (in the item picker) a Level filter `ComboBox` that recovers the original's "group by level" affordance. The asset picker simply repopulates its `ListBox` whenever the category changes.

## Security note

The original `MobsSQL` / `MobItemsSQL` built SQL via raw string concatenation including user-supplied fields (`name`, `ai`, etc.). The port routes everything through parameterised commands. This silently closes injection holes that existed in the original.

## Building & running

```bash
dotnet build                       # from this directory
dotnet run -- --smoke              # headless smoke test (no DB needed)
dotnet run                         # interactive (needs reachable MySQL via Login dialog)
```

Smoke output:

```
login    OK: 290x195 "Login"
main     OK: 1100x760 "Mob Editor"
about    OK: 420x240 "About Mob Editor"
mobAssets OK: 500x500 "Choose Mob Base Asset"
itemAssets OK: 560x540 "Choose Item Base Asset"
smoke OK: all 5 mob-editor-avalonia windows instantiated
```

## What is *not* ported

- The thumbnailed grouped `ListView` UX for the asset / item pickers (see "Thumbnails dropped").
- The Photoshop-style colour picker. The swatch on the General tab is read-only-ish: edit the H/S/V numeric fields to retint; we do not expose a click-to-pick wheel.
- The original `Properties.Settings.Default` connection-info cache. The port persists last-used host/db/user via `commontools-avalonia`'s JSON settings (same as the other Avalonia tools).
- The PropertyGrid's reflection-driven editing UI — the ad-hoc panel exposes exactly the three fields the original surface had per `mob_items` row.
