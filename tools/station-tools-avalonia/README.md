# station-tools-avalonia

Cross-platform Avalonia port of `tools/station-tools/` — the Net-7 starbase / room / terminal / NPC / vender editor. Writes to `starbase_objects`, `starbase_rooms`, `starbase_terminals`, `starbase_npc`, `starbase_npc_avatar_templates`, `starbase_vender_groups`, `starbase_vender_inventory`, and `starbase_vendors`.

Built on **.NET 10** + **Avalonia 11.2.3** + **MsBox.Avalonia 3.2.0** + the shared `commontools-avalonia` library. Targets `net10.0` (no `-windows` suffix), so it runs on Linux without WINE.

## What this is

A 4-window editor:

- **Main** (1240×720) — station combo + Load / New / Reload / Save All toolbar; a `TreeView` on the left mirroring the starbase hierarchy (Station → Room → Terminals folder + NPC folder → leaf nodes); a 5-tab editor on the right (Station / Room / Terminal / NPC / Venders). The TreeView's right-click `ContextFlyout` exposes Add Room / Add Terminal / Add NPC / Delete.
- **Item Browse** (700×500) — filter the catalog by name / level / type and either return one row (Browse) or many (Multi-Item Add to vender group).
- **Find Object** (560×420) — modal starbase picker (used when re-targeting an NPC to a different starbase).
- **Login** — shared from `commontools-avalonia` (`CommonTools.Gui.Login`).

The Venders tab is an embedded `VenderTabControl` UserControl rather than a separate window, matching the original's `tabPage2.Controls.Add(venderTab)` placement.

## Mapping from the original

| Original `tools/station-tools/` | Port |
|---|---|
| `Program.cs` (loads `Config.xml`, shows `Login`, then `Main`) | `App.axaml.cs` (Login → MainWindow swap) + `Program.cs` (`--smoke` flag) |
| `Main.cs` / `Main.Designer.cs` (~1900 LOC, 5 tab pages) | `MainWindow.axaml{,.cs}` |
| `VenderTab.cs` / `VenderTab.Designer.cs` (~700 LOC) | `VenderTabControl.axaml{,.cs}` |
| `ItemBrowse.cs` / `.designer.cs` | `ItemBrowseWindow.axaml{,.cs}` |
| `FindObject.cs` / `.designer.cs` | `FindObjectWindow.axaml{,.cs}` |
| `LoadAvatar.cs` (binary parser) | `LoadAvatar.cs` — verbatim port; byte layout identical (offset 44 header, int32 fields, float[3] colour triples, float[5] body weights) |
| `Helpers.cs` (`Utility`, `SQLData`) | `Utility` folded inline where used; `SQLData.ConnStr` dropped — uses `commontools-avalonia`'s `LoginData.ConnStr` |
| `EditTalkTree.cs` + the entire embedded `TalkTreeEditor/` subdirectory | dropped — launches the standalone `talktreeeditor-avalonia` via `dotnet run --project ../talktreeeditor-avalonia/` |
| `Login.cs` + `Config.xml` roundtrip | dropped — uses `commontools-avalonia`'s shared `Login` (persists creds via `LoginData`) |
| `SQLDataBase.cs` (private MySQL wrapper, sprintf-style SQL) | dropped — uses `commontools-avalonia`'s parameterised `DB.Instance.executeQuery/executeCommand` |
| `StationSQL.cs` (empty stub in original) | dropped (was already dead in the WinForms tree) |
| MySql.Data 6.x reference | replaced by `MySqlConnector` (via `commontools-avalonia`) |
| `WinForms.MessageBox.Show` | `MsBox.Avalonia.MessageBoxManager.GetMessageBoxStandard` |
| `TreeView` (System.Windows.Forms) with icon images per node | `Avalonia.Controls.TreeView` with text labels (icon GIFs/ICOs dropped — see below) |
| `DataGridView` (vender inventory) | Avalonia `DataGrid` with `AutoGenerateColumns=True` bound to `DataTable.DefaultView` |
| `DisplayStation` preview composite (rendered station thumbnail in main editor) | dropped — visual-only, no functional impact |
| Drag-drop of `.bin` avatar files onto the NPC tab | dropped — replaced by an "Add Avatar..." button that opens `StorageProvider.OpenFilePickerAsync` |
| Runtime icon loading from `/ico/*.gif|.ico` | dropped — TreeView nodes use plain text labels |

## What this port adds

- **Every SQL statement is parameterised** through `commontools-avalonia`'s `DB.Instance.executeQuery(sql, paramKeys[], paramVals[])`. The original built SQL via string concatenation throughout `Main.cs` and `VenderTab.cs`; in particular, `ItemBrowse`'s `LIKE '%" + textbox + "%'` is the textbook injection hole. All ~40 call sites are now parameterised.
- **Headless `--smoke` test** instantiates Login + MainWindow + ItemBrowseWindow + FindObjectWindow under `Avalonia.Headless`. Wired into the project for CI.
- **DB-touching loads in the ctor are wrapped in try/catch** (both `MainWindow` and `VenderTabControl`) so the smoke test runs without a MySQL backend.
- **Avatar file loading via `StorageProvider`** (`OpenFilePickerAsync`) — replaces both the original `OpenFileDialog` and the drag-drop handler. Avatar bytes are still hex-encoded and `REPLACE INTO starbase_npc_avatar_templates` exactly as the original did.

## What this port preserves verbatim

- **`LoadAvatar` byte layout** — header skipped 44 bytes, then `avatarType:int32`, `avaterVersion:byte` (sic, typo from original), `race/profession/gender/moodType:int32`, color triples as `float[3]`, body weights as `float[5]`. Round-trip-compatible with avatar files captured by the original tool.
- **Tree hierarchy** — Station root, then per-Room node, each with a `"Terminals"` folder and a `"NPC's"` folder (apostrophe preserved) as children, then the leaf terminal/NPC nodes inside.
- **Vender group / inventory schema usage** — `OnAddGroup` / `OnUpdateName` write `(GroupName, SellMultiplyer, BuyMultiplyer, BuyOnlyList)` (note `SellMultiplyer` / `BuyMultiplyer` typos preserved — they're column names). `OnDeleteGroup` cascades the three deletes the original performed: `starbase_vender_groups` + `starbase_vender_inventory` + un-setting `starbase_vendors.groupid = -1`.
- **`OnNewItem` default row** — `INSERT INTO starbase_vender_inventory ... VALUES (@g, '0', '0', '0', '0')` matches the original literal defaults (item id 0, sell 0, buy 0, qty 0).
- **`OnUnlimitedChanged` toggle** — checking "Unlimited" disables the quantity textbox and forces it to `-1`; unchecking resets it to `0`. Same intent and same sentinel as the WinForms original.
- **Terminal type / NPC booth-type / station type / room type combo content** — same string lists, same orderings as `Main.Designer.cs`.

## Building & running

```bash
dotnet build                  # from this directory
dotnet run -- --smoke         # headless smoke test
dotnet run                    # interactive editor (needs MySQL with the Net7 schema)
```

Smoke output:

```
login    OK: 290x195 "Login"
main     OK: 1240x720 "Station Tools"
browse   OK: 700x500 "Item Browse"
find     OK: 560x420 "Find Object"
smoke OK: all 4 station-tools-avalonia windows instantiated
```

## License

CC BY-NC-SA 3.0 — see project root `LICENSES/Net7`. Original Net-7 Entertainment headers in ported source files are preserved unchanged.
