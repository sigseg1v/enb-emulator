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

### Tier 0 — drop false WinForms flags (complete)

- [x] **w3d-parser**: dropped `<UseWindowsForms>`, retargeted `net10.0-windows` → `net10.0`, added explicit `System.Drawing.Common` package ref. Builds clean.
      Touches: `tools/w3d-parser/W3d Parser.csproj`
      Commit: 2108e67

- [x] **ExeUpdater**: dropped single `MessageBox.Show` in `Program.cs:208` for `Console.WriteLine`, dropped `<UseWindowsForms>`, retargeted `OutputType=WinExe`→`Exe` and `net10.0-windows`→`net10.0`. Runs natively on Linux:
      ```
      $ dotnet tools/launchnet7/ExeUpdater/bin/Debug/net10.0/ExeUpdater.dll
      === ExeUpdater - Information ===
      ExeUpdater
      Description: A lightweight executable for replaceing and restarting...
      ```
      Touches: `tools/launchnet7/ExeUpdater/ExeUpdater.csproj`, `tools/launchnet7/ExeUpdater/Program.cs`
      Commit: 2108e67

### Tier 1 — Avalonia POC

- [x] **toolspatcher-avalonia**: full Avalonia port of `tools/toolspatcher/`.
      New project at `tools/toolspatcher-avalonia/` targeting `net10.0`
      (no `-windows` suffix). 1 main window with progress bars + labels,
      MAXML layout, code-behind for the patcher state machine.
      - WinForms `WebClient` → `HttpClient`
      - `Control.Invoke` → `Dispatcher.UIThread.Post`
      - `Thread.Abort` → `CancellationToken`
      - `WebBrowser` patch-notes pane → placeholder `TextBox`
        (`toolspatch.net-7.org` host is dead anyway; Avalonia has no
        built-in WebView and adding one isn't worth it for this tool)
      - `MessageBox.Show` → MsBox.Avalonia
      Registered in `tools/Net7Tools.slnx`; whole solution still builds.
      Touches: `tools/toolspatcher-avalonia/{ToolsPatcherAvalonia.csproj,Program.cs,App.axaml*,MainWindow.axaml*,Crc32.cs,README.md,app.manifest}`
      Status: complete

- [x] **Headless smoke test**: `--smoke` arg uses `Avalonia.Headless` to
      instantiate `App` + `MainWindow` without a display. Verifies AXAML
      parses + window class loads. Output:
      ```
      smoke OK: window 573x363 title="E&B Tools Patcher"
      ```
      Will hook into CI in a separate commit.
      Status: complete

- [x] **Per-form-day measurement**: toolspatcher (1 simple form, ~450
      LOC of logic + ~10 controls + 1 timer + HTTP/file IO) took ~2
      hours including Avalonia plumbing setup, smoke test, README. The
      Avalonia plumbing is now solved — subsequent ports start from a
      working template. Rough per-form estimate forward:
      - **simple form** (≤15 controls, no grid, no custom paint): ~1 hour
      - **typical editor form** (DataGridView, tabs, MySQL binding): half a day
      - **sector-editor-class** (custom map canvas, multi-pane): ~3 days
      Suite estimate refines to **~3 months** single-engineer at the
      lower end, **5-6 months** likely with debug + parity verification.
      Status: complete

- [ ] **avalonia-common shared library**: extract login dialog, MySQL
      helper, and progress-update patterns from `commontools` into a new
      `tools/avalonia-common/` library so subsequent ports don't re-solve
      them. **Deferred** — first do another small editor (dataimport)
      to see what's actually shared before extracting prematurely.
      Status: deferred

### Tier 2+ — deferred

The remaining 12 editors (commontools, dataimport, faction-editor, mob-editor, launchnet7, talktreeeditor, effect-editor, toolslauncher, station-tools, missioneditor, sector-editor) are tracked as future Phase L sub-items. **Not in scope for this session.** With realistic ~3-6 month total for the suite, this is its own project.

For immediate Linux runnability of the editors: the WinForms binaries already run under WINE — `tools/README.md` documents this. That's the realistic interim story until Avalonia ports land.

## Decisions

- **Keep both UIs in parallel** during migration. The WinForms targets stay in the build until Avalonia ports reach parity. Don't break working code chasing a not-yet-working port.
- **Avalonia, not MAUI/UWP/Eto**. Avalonia has the closest WinForms-developer story (XAML + code-behind), best Linux X11/Wayland support, and an active community. MAUI requires macOS for development workflows (Mac Catalyst dependency), Eto.Forms is lower quality, UWP is Windows-only.
- **Use MVVM lightly**. Avalonia's idiomatic pattern is MVVM, but for porting WinForms apps (which weren't written MVVM) we keep code-behind for now — `View.axaml.cs` instead of `Form.cs` — to minimize the rewrite scope. Refactor to MVVM later if a maintainer wants to.
- **MySql.Data → keep as-is**. The package works cross-platform on .NET 10. No need to swap for MySqlConnector during this phase.
