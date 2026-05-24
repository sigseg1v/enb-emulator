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

- [x] **avalonia-common shared library**: now realized as
      `tools/commontools-avalonia/` (see Tier 2 below). The shared
      pieces — Login window, DlgSearch, DlgSearchCriteria, DlgEditXml,
      DB layer, Xml helpers, Enumeration, TableButtonHandler — all
      live there. Subsequent editor ports `<ProjectReference>` this
      instead of `tools/commontools/`.
      Status: complete

### Tier 1.5 — second patcher port (no-deps)

- [x] **enbpatcher-avalonia**: ported `tools/enbpatcher/` (which had no
      csproj and didn't build at all upstream). Same shape as
      toolspatcher — same author wrote both. Differences: URL
      (patch.net-7.org), self-name (EnBPatcher.exe), launcher
      (LaunchNet7.exe), game dir (c:\net7\bin). Smoke test passes:
      ```
      smoke OK: window 573x363 title="E&B Patcher"
      ```
      Registered in tools/Net7Tools.slnx.
      Touches: new `tools/enbpatcher-avalonia/`
      Status: complete

- [x] **Confirm copy-template velocity**: second port took ~30 min
      vs. the first port's ~2 hr. Plumbing is now boilerplate;
      MainWindow.axaml.cs translation is the only real work for
      similarly-shaped tools. Per-form estimate for the
      simple-patcher class drops to **~30 min each**. Editor-class
      tools still TBD until first DB-aware editor is ported.
      Status: complete

### Tier 2 — shared library port (complete)

- [x] **commontools-avalonia**: full Avalonia port of `tools/commontools/`.
      New project at `tools/commontools-avalonia/` targeting `net10.0`
      (cross-platform, no `-windows` suffix). Library output
      `CommonToolsAvalonia.dll` with `RootNamespace=CommonTools` so
      downstream consumers can swap a ProjectReference and keep `using`
      lines unchanged.

      Ported pieces:
      - **Login** (Window) — same public API (`isValid()`,
        `updateVersion()`, `LoginData` static) so consumers don't
        change. Reads/writes `Config.xml` next to the entry assembly.
      - **DlgEditXml** (Window) — simple textbox + ok/cancel.
      - **DlgSearchCriteria** (Window) — field/comparator/pattern
        dialog with the "% wildcard" tip popup.
      - **DlgSearch** (Window) — search criteria list + result table.
        WinForms `ListView` replaced with Avalonia `DataGrid`
        (`Avalonia.Controls.DataGrid` package; columns added at
        runtime from `Net7.Tables` enum; `DataTable.DefaultView`
        bound directly). `ListViewColumnSorter` dropped — DataGrid's
        built-in sort handles it.
      - **DB / DBErrorReporter** — extracted MessageBox calls behind
        a `DBErrorReporter.Show = Action<string, string>` sink. The
        DB layer no longer depends on any UI framework. `Login`'s
        ctor installs an Avalonia MsBox sink; library hosts (and
        the smoke test) can install a console sink instead.
      - **Database/, Xml/, Singleton.cs, Enumeration.cs,
        TableButtonHandler.cs, Gui/SearchCriteria.cs** — copied
        verbatim or ported to Avalonia ComboBox/ListBox APIs.

      Build: `dotnet build` clean (0 warnings, 0 errors).
      Test: `tools/commontools-avalonia/test/` is a headless
      Avalonia smoke test (`Avalonia.Headless` 11.2.3) that
      instantiates each of the 4 windows without a display and
      verifies the DBErrorReporter sink rerouted by Login's ctor
      fires. Output:
      ```
      login    OK: 290x195 "Login"
      editxml  OK: 600x450 "Edit XML"
      crit     OK: 490x160 "Search Criteria"
      search   OK: 640x500 "Search"
      smoke OK: all 4 commontools-avalonia windows instantiated
      ```
      Registered in `tools/Net7Tools.slnx`. Whole solution still builds.
      Touches: new `tools/commontools-avalonia/` (4 axaml + .cs,
      11 .cs ports, csproj, test/ subproject).
      Status: complete

      **Landmines hit:**
      - Avalonia SDK auto-includes `*.axaml` as `AvaloniaResource`.
        Adding an explicit `<AvaloniaResource Include="**/*.axaml" />`
        produces `AVLN2002: Duplicate x:Class`. Don't.
      - `test/` lives under the library project dir, so SDK's default
        `**/*.cs` glob sweeps `test/Program.cs` into the library. Fix
        with `<Compile Remove="test/**" />` (+ `None`,
        `EmbeddedResource`, `AvaloniaResource` removes).
      - `void InitializeComponent() => AvaloniaXamlLoader.Load(this);`
        as a hand-rolled override is a silent landmine: Avalonia's
        XAML compiler *generates* `InitializeComponent` which is what
        wires up x:Name fields. A hand-rolled one shadows the generated
        version → x:Name fields stay null → ctor NREs at the first
        field access. Don't write one. Smoke test caught it.

### Tier 3 — first commontools-avalonia consumer (complete)

- [x] **dataimport-avalonia** — 1 form, depends on commontools-avalonia.
  - Ports `tools/dataimport/` (377 LOC) to a Linux-native build.
  - Layout: 7-control form (Table ComboBox, File TextBox + Browse,
    Import/Close) rewritten as `Grid` + `StackPanel`.
  - `OpenFileDialog` → `StorageProvider.OpenFilePickerAsync`.
  - Login → MainWindow handoff uses `ShutdownMode.OnExplicitShutdown`
    in `App.OnFrameworkInitializationCompleted`, swapping `MainWindow`
    on the Login `Closed` event. The naive
    `login.ShowDialog(null).GetAwaiter().GetResult()` from the lifecycle
    callback would deadlock the dispatcher — caught during design.
  - 4 files + slnx entry; `dotnet build` clean (0 warn/0 err);
    `--smoke` headless test passes (`window 486x283 title="Data Import v1.0 Build 0.0"`).
  - **6/14 tools have Linux-native paths now.**

### Tier 4 — client-side launcher port (complete)

- [x] **launchnet7-avalonia** — full Avalonia port of
      `tools/launchnet7/LaunchNet7/`. Self-contained — does NOT depend
      on commontools-avalonia (the launcher has its own config story,
      no MySQL, no DB Search dialog).

      Ported:
      - `FormMain.cs` → `MainWindow.axaml{,.cs}` — server picker
        (`ServerConfig` + `HostConfig` ComboBoxes), client EXE
        path + Browse, authentication port + secure checkbox, misc
        ClientDetours/LocalCert checkboxes, status line + log pane,
        Advanced/Play/Quit. TCP probe of `host:3809` (proxy port)
        for the ONLINE/OFFLINE badge runs on a `Task.Run` with
        `Dispatcher.UIThread.InvokeAsync` for UI updates.
      - `Configuration/*` (8 files of
        `System.Configuration.ConfigurationSection` boilerplate) →
        `Config/LauncherConfig.cs` + `Config/ServerConfig.cs` —
        thin `XmlDocument` reader, POCOs.
      - `Properties.Settings.Default` → `Config/UserSettings.cs` —
        JSON-on-disk beside the .dll.
      - `IniUtility.cs` (P/Invokes kernel32 `GetPrivateProfileString`
        / `WritePrivateProfileString`) → `Patching/IniFile.cs` —
        portable text-mode reader/writer that mirrors the kernel32
        contract (returns null when key is absent).
      - `Patching/AuthLoginPatcher.cs` + `AuthPatcherInfo.cs` —
        verbatim port (0x8328 HTTPS flag, 0x82AD port, 0x8292
        timeout). `fs.Read` upgraded to `fs.ReadExactly` (CA2022).
      - `Launcher.cs` — orchestrates the patch sequence
        (rg_regdata rename → rg_regdata.ini URL → Auth.ini URLs →
        Network.ini host across the 11 known sections →
        authlogin.dll flags → registry) and dispatches by
        `LaunchName`: NET7SP → Net7Server then Net7Proxy after 25s
        sleep; NET7MP → Net7Proxy; default → client.exe with
        `-SERVER_ADDR <ip> -PROTOCOL TCP`.
      - `LauncherUtility.GetShortPathName` (kernel32 P/Invoke) →
        `ShortPath.cs` — Windows-only Win32 call; passthrough on
        non-Windows.
      - `Microsoft.Win32.Registry` direct use → `WindowsRegistryHelpers.cs`
        gated by `#if WINDOWS_BUILD`; `Launcher.PatchRegistry()`
        no-ops on non-Windows with a warning (WINE manages its own
        per-prefix registry).
      - On non-Windows, all Win32 .exe spawns are wrapped via
        `wine "<exe>"` (`WinExe()` helper checks
        `RuntimeInformation.IsOSPlatform(OSPlatform.Windows)`).

      Dropped (documented in `tools/launchnet7-avalonia/README.md`):
      - `Updateing/Updater.cs` + `FormUpdate.cs` — upstream patch host
        (patch.net-7.org) is offline; resurrecting it is its own task.
      - `ExeUpdater` (self-update subproject) — only meaningful on
        Windows; a Linux launcher updates via the OS package manager.
        Note: ExeUpdater was already retargeted to `net10.0` console
        in Tier 0.
      - `FileListCreator` subproject — only useful for publishing
        updates to the dead patch host.
      - `WebBrowser` patch-notes pane — no built-in WebView in
        Avalonia, host is dead anyway.
      - `BackgroundWorker` → `Task.Run` + `Dispatcher.UIThread.Post`.
      - `OpenFileDialog` → `StorageProvider.OpenFilePickerAsync`.
      - `MessageBox.Show` → MsBox.Avalonia.

      Build: `dotnet build` clean (0 warn / 0 err) after fixing two
      CA2022 inexact-read warnings in AuthLoginPatcher.
      Test: `--smoke` headless passes:
      ```
      smoke OK: window 640x520 title="LaunchNet7"
      ```
      `LaunchNet7.cfg` is copied to output via
      `<None CopyToOutputDirectory="PreserveNewest" />` so the smoke
      test sees the same XML the production deployment will.

      Registered in `tools/Net7Tools.slnx` under
      `/launchnet7-avalonia/`. Whole solution still builds.
      Touches: new `tools/launchnet7-avalonia/` (13 files +
      `LaunchNet7.cfg` copy + slnx entry).
      Status: complete

      **7/14 tools have Linux-native paths now.**

### Future tier ordering (remaining 8 tools — deferred until session focus returns to Phase L)

Recommended order:

1. **faction-editor-avalonia** — 3 forms, MySQL. ~1-2 days (depends on
   commontools-avalonia).

2. **mob-editor-avalonia** — 3 forms, MySQL, larger LOC. ~2 days
   (depends on commontools-avalonia).

3. **talktreeeditor-avalonia** — 5 forms, depends on
   commontools-avalonia. ~2-3 days.

4. **toolslauncher-avalonia** — 6 forms incl. IRC client + FTP window.
   ~3-5 days (IRC integration via Meebey.SmartIrc4Net is the wildcard).

5. **effect-editor-avalonia** (SQLBind) — 5 forms, particle effects.
   ~3 days.

6. **station-tools-avalonia** — 8 forms. ~4-5 days.

7. **missioneditor-avalonia** — 9 forms incl. tree view. Depends on
   commontools-avalonia. ~5 days.

8. **sector-editor-avalonia** — 16 forms, custom map canvas
   (System.Drawing.Graphics → Avalonia DrawingContext is the major
   work). ~2-3 weeks.

### Tier 2+ — deferred

The remaining 8 editors (faction-editor, mob-editor, talktreeeditor, effect-editor, toolslauncher, station-tools, missioneditor, sector-editor) are tracked as future Phase L sub-items. With realistic ~3-6 month total for the suite, this is its own project — but per the user directive "do all plans / dont stop at phase boundaries," subsequent invocations should keep grinding through them.

For immediate Linux runnability of the editors: the WinForms binaries already run under WINE — `tools/README.md` documents this. That's the realistic interim story until Avalonia ports land.

## Decisions

- **Keep both UIs in parallel** during migration. The WinForms targets stay in the build until Avalonia ports reach parity. Don't break working code chasing a not-yet-working port.
- **Avalonia, not MAUI/UWP/Eto**. Avalonia has the closest WinForms-developer story (XAML + code-behind), best Linux X11/Wayland support, and an active community. MAUI requires macOS for development workflows (Mac Catalyst dependency), Eto.Forms is lower quality, UWP is Windows-only.
- **Use MVVM lightly**. Avalonia's idiomatic pattern is MVVM, but for porting WinForms apps (which weren't written MVVM) we keep code-behind for now — `View.axaml.cs` instead of `Form.cs` — to minimize the rewrite scope. Refactor to MVVM later if a maintainer wants to.
- **MySql.Data → keep as-is**. The package works cross-platform on .NET 10. No need to swap for MySqlConnector during this phase.
