# missioneditor-avalonia

Avalonia port of `tools/missioneditor/`. Edits mission XML records from the `missions` table — id, key, type, name, summary, time limit, forfeitable flag; per-stage conditions, completions, rewards, and talk-tree XML.

## Status

`dotnet build` clean. Headless smoke test instantiates all 7 windows (Login, MainWindow, DlgConditions, DlgStages, DlgCompletions, DlgRewards, DlgReport).

## Run

```
dotnet run --project tools/missioneditor-avalonia/                # interactive
dotnet run --project tools/missioneditor-avalonia/ -- --smoke     # CI smoke
```

Connects to MySQL via shared `CommonTools.Database.DB`. Without a live DB the editor renders empty rows; descriptions and search lookups are wrapped in try/catch so the form stays alive.

## Layout vs. the WinForms original

- `FrmMission` + embedded `TabMission` + `TabStages` UserControls → single `MainWindow` with a 2-tab `TabControl`. Avalonia's compiled bindings don't love nested UserControl-in-TabItem patterns; collapsing them simplified the named-field plumbing and made `populateMissionTab` / `populateStagesTab` reusable.
- WinForms `ListView` with subitem columns → Avalonia `ListBox` with a `ToString()`-formatted `"Type | FormattedValue"` line. The original sortable-column behaviour isn't load-bearing for these short condition/stage/completion/reward lists.
- WinForms `MessageBox.Show` → `MsBox.Avalonia.MessageBoxManager` with `Warning` icon (validation errors) or `Question` icon (delete confirmation).
- WinForms `DlgEditXml` → reused from `commontools-avalonia` (already ported).
- WinForms `DlgSearch` → reused from `commontools-avalonia`, configured against `Net7.Tables.missions`.
- `DataConfiguration.search()` returned synchronously in WinForms; here it's `async Task<string>` because Avalonia `ShowDialog` is async. All call sites became `async void` event handlers, which is fine for UI events.

## Dropped / not yet wired

- **Talk-tree round trip.** The "Edit Talk Tree" button spawns `talktreeeditor-avalonia` via `Process.Start` (same pattern as `station-tools-avalonia`), but `talktreeeditor-avalonia` does not return its result. Re-importing the edited XML back into `m_stage` needs a temp-file or stdout-pipe contract that isn't designed yet. Status bar surfaces this: "launched talktreeeditor-avalonia (round-trip not wired)". Until then, talk-tree XML can be hand-edited via "Edit XML" on the toolbar (the whole mission XML).
- **`DlgReport` as HTML renderer.** Avalonia has no built-in `WebBrowser`, so the report is rendered as plain text in a read-only `TextBox`. The original `DlgReport` was viewer-only (no editing), so plain-text preserves the information content.
- **Cursor changes (`Cursors.WaitCursor`).** Status text already conveys progress; cursor flicker dropped.
- **Two leftover `MessageBox.Show("onConditionSelected") / ("onCompletionSelected") / ("onRewardSelected")` debug calls** from the WinForms original — clearly forgotten during development; not ported.

## DB calls

All SQL is routed through `Database/Database.cs` which already used parameterised queries (`?` placeholders), copied verbatim from the WinForms version. No new injection surface introduced; `MainWindow` does not build any SQL strings of its own.

## License

CC BY-NC-SA 3.0, inherited from the upstream Net-7 mission editor sources. See `LICENSES/Net7`.
