# Phase L — Avalonia migration (C# tools cross-platform UI)

User directive: "for winforms we need it to run on linux so switch it to avalonia."

## Scope reality

The `tools/` suite has **14 csproj projects that flag `<UseWindowsForms>true</UseWindowsForms>`**, but the actual form-by-form scope is:

| Project | Real form count | LOC | Tier | Notes |
|---|---|---|---|---|
| ExeUpdater          | 0 (one MessageBox)  |   374 | 0 | Trivial: drop the MessageBox, retarget to `net10.0` console |
| w3d-parser          | 0 (library, no UI)  |  8545 | 0 | Trivial: `UseWindowsForms` is a false flag — drop it, retarget to `net10.0` |
| toolspatcher        | 1                    |   994 | 1 | POC candidate — simplest real form |
| dataimport          | 1                    |   377 | 1 | Depends on commontools |
| commontools         | 2 (Gui/DlgSearch, Gui/DlgEditXml) | 14040 | 2 | Shared library — must migrate before consumers |
| faction-editor      | 3                    |  1983 | 2 | |
| mob-editor          | 3                    |  5598 | 2 | |
| launchnet7          | 4                    |  5112 | 2 | |
| talktreeeditor      | 5                    |  1804 | 3 | |
| effect-editor       | 5                    |  3689 | 3 | |
| toolslauncher       | 7                    |  4167 | 3 | |
| station-tools       | 8                    |  7389 | 4 | |
| missioneditor       | 9                    |  6364 | 4 | |
| sector-editor       | 16                   | 19386 | 4 | Largest — custom drawing, multi-pane editor |
| **Total**           | **64 real forms**    | **79,822** | | |

(Plus there are ~24 inflated Designer.cs files in `Properties/{Resources,Settings}.Designer.cs` that are auto-generated bindings, not forms.)

## Honest per-form cost estimate

WinForms → Avalonia is **not a retarget**. The two frameworks differ at every level:

| Concept             | WinForms                              | Avalonia                                  |
|---------------------|---------------------------------------|-------------------------------------------|
| Layout markup       | Designer.cs (code generation)         | AXAML (XML markup)                        |
| Controls            | Button, Label, DataGridView, etc.     | Button, TextBlock, DataGrid (different APIs) |
| Layout system       | Anchor + Dock + Absolute pixels       | Grid + StackPanel + DockPanel (WPF-style) |
| Data binding        | BindingSource + DataBindings          | INotifyPropertyChanged + AXAML `{Binding}` |
| Resource files      | .resx                                 | Embedded AXAML resources                  |
| Custom controls     | OnPaint via System.Drawing.Graphics   | Custom Control + DrawingContext           |
| Cross-thread UI     | Control.Invoke(delegate)              | Dispatcher.UIThread.Post(Action)          |

No automated converter exists. Each form is hand-translated.

Per-form effort (estimate, based on industry experience and complexity):
- **Trivial form** (1-5 controls, no custom paint): ~half a day
- **Typical form** (5-30 controls, basic data binding): 1-2 days
- **Complex form** (DataGridView, custom paint, multi-pane): 3-5 days

**Best-case total**: 64 forms × ~1.5 days avg = ~3 months single-engineer.
**Realistic total**: 4-6 months, given the largest editors (sector-editor's 16 forms include custom 2D map-drawing canvas; mission/station editors have heavy DataGridView usage).

## Strategy

Do not silently start a months-long migration. Phase L delivers:

1. **Tier 0 quick wins** — projects mis-flagged as WinForms (no actual UI dependency). Just retarget to `net10.0`. These already run on Linux today; we just need to drop the `-windows` TFM.
2. **Avalonia POC** — migrate ONE small editor (toolspatcher) end-to-end so we have a working pattern, a real build, and a measurable per-form cost.
3. **Avalonia infrastructure** — `tools/avalonia-common/` with shared base classes (login dialog, MySQL connection, common patterns), so subsequent editors don't re-solve the same problems.
4. **Document timeline** — update master plan with realistic completion estimates for the remaining tiers.

The existing WinForms targets stay in the tree. They still build via `dotnet build` and still run under WINE on Linux. Avalonia ports live alongside as `tools/<name>-avalonia/` until they reach parity, then the originals can be archived.

## Items

### Tier 0 — drop false WinForms flags (target: this session)

- [ ] **w3d-parser**: drop `<UseWindowsForms>true</UseWindowsForms>`, retarget `net10.0-windows` → `net10.0`. It's a parser library with zero UI code. Verify `dotnet build` clean.
      Touches: `tools/w3d-parser/W3d Parser.csproj`
      Status: not started

- [ ] **ExeUpdater**: replace single `MessageBox.Show` with `Console.WriteLine`, drop `<UseWindowsForms>`, retarget to `net10.0`. Single ~6-LOC change in `Program.cs:208`.
      Touches: `tools/launchnet7/ExeUpdater/ExeUpdater.csproj`, `tools/launchnet7/ExeUpdater/Program.cs`
      Status: not started

### Tier 1 — Avalonia POC

- [ ] **Avalonia infrastructure**: add a shared `tools/avalonia-common/` library with login dialog, MySQL connection helper, and base view-model classes. Pulls `Avalonia` + `Avalonia.Desktop` + `MessageBox.Avalonia` packages.
      Status: not started

- [ ] **toolspatcher-avalonia**: full Avalonia port of `tools/toolspatcher/`. 1 main window (progress bars + file label + start timer). HTTP downloader logic stays as-is; only the UI layer changes. Verify it builds and `dotnet run` opens a window on Linux.
      Touches: new `tools/toolspatcher-avalonia/`
      Status: not started

- [ ] **Per-form-day measurement**: record actual hours spent on toolspatcher port. Use to scale estimate for the remaining 13 editors.
      Status: not started

### Tier 2+ — deferred

The remaining 12 editors (commontools, dataimport, faction-editor, mob-editor, launchnet7, talktreeeditor, effect-editor, toolslauncher, station-tools, missioneditor, sector-editor) are tracked as future Phase L sub-items. **Not in scope for this session.** With realistic ~3-6 month total for the suite, this is its own project.

For immediate Linux runnability of the editors: the WinForms binaries already run under WINE — `tools/README.md` documents this. That's the realistic interim story until Avalonia ports land.

## Decisions

- **Keep both UIs in parallel** during migration. The WinForms targets stay in the build until Avalonia ports reach parity. Don't break working code chasing a not-yet-working port.
- **Avalonia, not MAUI/UWP/Eto**. Avalonia has the closest WinForms-developer story (XAML + code-behind), best Linux X11/Wayland support, and an active community. MAUI requires macOS for development workflows (Mac Catalyst dependency), Eto.Forms is lower quality, UWP is Windows-only.
- **Use MVVM lightly**. Avalonia's idiomatic pattern is MVVM, but for porting WinForms apps (which weren't written MVVM) we keep code-behind for now — `View.axaml.cs` instead of `Form.cs` — to minimize the rewrite scope. Refactor to MVVM later if a maintainer wants to.
- **MySql.Data → keep as-is**. The package works cross-platform on .NET 10. No need to swap for MySqlConnector during this phase.
