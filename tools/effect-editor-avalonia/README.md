# effect-editor-avalonia

Cross-platform Avalonia port of `tools/effect-editor/SQLBind/` — the Net-7 particle/stat effect editor that writes to `item_effect_base`, `item_effects`, and `item_effect_container`.

Built on **.NET 10** + **Avalonia 11.2.3** + **MsBox.Avalonia 3.2.0** + the shared `commontools-avalonia` library. Targets `net10.0` (no `-windows` suffix), so it runs on Linux without WINE.

## What this is

A 5-window editor:

- **Main** (740×475) — load/save a single effect (`item_effect_base` row). Edit Type, Name, Description, Tooltip, friend/enemy/group targeting flags, RequireT flag, buff association, visual-effect id, 3 variable stat/type slots, and 2 constant stat/type/value slots.
- **Effect Search** (640×420) — pick or delete an effect by id/description/tooltip.
- **Edit Items** (680×580) — attach up to 5 `item_effects` rows to a single `item_base` row, plus its `Item_effect_container` (recharge/energy/range for activatable items). Includes a live `%valueN.Mf%` printf-style tooltip preview.
- **Item Browse** (620×340) — search the item catalog by name/level/type and pick one for the Edit Items dialog.
- **Login** — shared from `commontools-avalonia` (`CommonTools.Gui.Login`).

## Mapping from the original

| Original `tools/effect-editor/SQLBind/` | Port |
|---|---|
| `Program.cs` (loads `Config.xml`, shows `Login`, then `Form1`) | `App.axaml.cs` (Login → MainWindow swap, faction-editor pattern) + `Program.cs` (`--smoke` flag) |
| `Form1.cs` / `Form1.Designer.cs` (main effect editor, ~600 LOC) | `MainWindow.axaml{,.cs}` |
| `EffectSearch.cs` / `.designer.cs` | `EffectSearchWindow.axaml{,.cs}` |
| `EditItem.cs` / `.designer.cs` (~700 LOC, 5 hand-laid-out effect rows) | `EditItemWindow.axaml{,.cs}` (5 rows constructed programmatically) |
| `ItemBrowse.cs` / `.designer.cs` | `ItemBrowseWindow.axaml{,.cs}` |
| `CodeValue.cs` (combo formatter) | `CodeValue.cs` — verbatim port |
| `EffectComboHandel.cs` (sic) | folded into `MainWindow.FillStats` / `FillBuffs` / `FillVarTypes` |
| `Login.cs` + `Config.xml` roundtrip | dropped — uses `commontools-avalonia`'s shared `Login` (persists creds via `LoginData`) |
| `SQLDataBase.cs` (private MySQL wrapper, sprintf-style SQL) | dropped — uses `commontools-avalonia`'s parameterised `DB.Instance.executeQuery/executeCommand` |
| MySql.Data 6.x reference | replaced by `MySqlConnector` (via `commontools-avalonia`) |
| `WinForms.MessageBox.Show` | `MsBox.Avalonia.MessageBoxManager.GetMessageBoxStandard` |
| `DataGridView` (effect/item search) | Avalonia `DataGrid` with `AutoGenerateColumns=True` |
| `Form1`'s custom version check against `versions` table | dropped — the editor was a Windows-only desktop binary with a `versions` table that no current deployment uses |

## What this port adds

- **Every SQL statement is parameterised** through `commontools-avalonia`'s `DB.Instance.executeQuery(sql, paramKeys[], paramVals[])`. The original built SQL by string concatenation (`"WHERE name LIKE '%" + textbox.Text + "%'"`, etc.) — roughly 20 textbook SQL-injection holes are silently closed by this port. Wire semantics are identical because all values go through MySqlConnector's placeholder binder.
- **`(none)` sentinel row at combo index 0** in the per-effect dropdowns in Edit Items, so resetting an effect slot back to "(none)" deterministically maps to a DELETE on save (mirrors the original's "if combo was set and is now empty, delete" intent — but explicit instead of relying on `SelectedIndex==-1` ambiguity).
- **Headless `--smoke` test** instantiates all 5 windows under `Avalonia.Headless` (no display required). Wired into the project for CI.
- **DB-touching combo loads are wrapped in try/catch in the ctor** so the smoke test can verify the AXAML compiles and the controls wire up without a running MySQL.

## What this port preserves verbatim

- **Flag1 / Flag2 bit encoding** — `TFriend << 4`, `TEnemy << 5`, `TGroupM << 6`; `RequireT` → `flag2 |= 1` when checked, `flag2 |= 1 << 1` when unchecked (the original's surprising "both bits" inverted encoding is kept exactly).
- **`NewEffect` defaults** — the same `'none'/'none'/'none'`, `NO_STAT/BUFF_NONE`, zero-everywhere row the original inserts.
- **Variable-type combo content** — `Not Used (0)`, `Increase Value (1)`, `Increase Percent (2)`, `Decrease Value (3)`, `Decrease Percent (4)`, `Duration (5)` — matches the original 1:1.
- **Item-type combo ordering in Item Browse** — the `_itemTypes` array prepends `(any)` so `SelectedIndex - 1` still maps to the database `type` column value the original computed.
- **`%valueN.Mf%` printf parser in Edit Items** — the loop shape from `EditItem.DisplayString` is preserved exactly (uses an "f%" sentinel string search rather than a regex). This is the one piece of editor logic the user-facing tooltip preview depends on.
- **Container row CRUD** — `Item_effect_container` rows are still INSERTed when missing and UPDATEed when present, keyed by `(ItemID, EffectContainerID, EquipEffect=1)`.

## Building & running

```bash
dotnet build                  # from this directory
dotnet run -- --smoke         # headless smoke test
dotnet run                    # interactive editor (needs MySQL with the Net7 schema)
```

Smoke output:

```
login    OK: 290x195 "Login"
main     OK: 740x475 "Effect Editor"
search   OK: 640x420 "Effect Search"
browse   OK: 620x340 "Item Browse"
edit     OK: 680x580 "Edit Items"
smoke OK: all 5 effect-editor-avalonia windows instantiated
```

## License

CC BY-NC-SA 3.0 — see project root `LICENSES/Net7`. Original Net-7 Entertainment headers in ported source files are preserved unchanged.
