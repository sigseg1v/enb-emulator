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
| station-tools       | 8                    |  7389 | 4 | ported (Tier 10) |
| missioneditor       | 9                    |  6364 | 4 | ported (Tier 11) |
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

### Tier 5 — first MySQL editor port (complete)

- [x] **faction-editor-avalonia** — full Avalonia port of
      `tools/faction-editor/` (factions + faction_matrix editor; original
      was a 3-form WinForms app: Login, mainFrm, About).
      Files added (13):
      - `FactionEditorAvalonia.csproj` — `net10.0`, Avalonia 11.2.3,
        DataGrid package, MsBox.Avalonia, Tmds.DBus.Protocol 0.21.3
        (forced upgrade for CVE GHSA-xrw6-gwf8-vvr9), project ref to
        `commontools-avalonia`.
      - `app.manifest`, `App.axaml{,.cs}` — Fluent theme +
        DataGrid theme include; `OnExplicitShutdown` + Login `Closed`
        handoff swap so the Login dialog can hand off to MainWindow
        without dispatcher deadlock (same pattern as
        `dataimport-avalonia`).
      - `Program.cs` — entry + `--smoke` headless test that
        instantiates Login + MainWindow + AboutBox.
      - `MainWindow.axaml{,.cs}` — DockPanel layout: menu + toolbar
        + status bar + TabControl (General Details / Faction Matrix)
        + GridSplitter + DataGrid (lifted `FactionRow` to top-level
        type so `x:DataType` compiled binding resolves cleanly).
      - `FactionRow.cs` — top-level POCO for grid binding.
      - `FactionMatrixProps.cs` — relation-entry POCO (dropped
        original PropertyGrid `[Description]` / `[Category]` /
        `[ReadOnly]` attributes since Avalonia has no PropertyGrid).
      - `AboutBox.axaml{,.cs}` — 420x240 modal.
      - `SQL/FactionsSQL.cs`, `SQL/FactionMatrixSQL.cs` — wrap
        `DB.Instance.executeQuery/executeCommand` with `?name`
        placeholders. **Silently closes SQL-injection holes that the
        original had via string concatenation through user-supplied
        name/description/PDA_text fields.**
      - `README.md` — full mapping doc.

      Dropped vs. original:
      - WinForms `PropertyGrid` for relation entries → ad-hoc panel
        (NumericUpDown + read-only TextBox + CheckBox).
        Avalonia has no PropertyGrid equivalent.
      - `Properties.Settings.Default` connection-info cache →
        commontools-avalonia's JSON settings store.
      - `dbInstance.cs` / `Database/Login.cs` → CommonTools.Database.DB
        + CommonTools.Gui.Login.

      Smoke output:
      ```
      login    OK: 290x195 "Login"
      main     OK: 720x600 "Faction Editor"
      about    OK: 420x240 "About Faction Editor"
      smoke OK: all 3 faction-editor-avalonia windows instantiated
      ```

      Registered in `tools/Net7Tools.slnx` under
      `/faction-editor-avalonia/`. Whole solution still builds
      (0 errors; the 4485 warnings are pre-existing legacy
      WinForms CA1416s in the unported projects).
      Touches: new `tools/faction-editor-avalonia/` (13 files +
      slnx entry).
      Status: complete

      **8/14 tools have Linux-native paths now.**

### Tier 6 — second MySQL editor port (complete)

- [x] **mob-editor-avalonia** — full Avalonia port of
      `tools/mob-editor/`. Original count of "3 forms" in the table above
      undercounted: the editor actually has **5 windows** (mainFrm,
      About, MobBaseAssets, ItemBaseAssets, plus the standalone Login).
      All 5 ported.
      - `mainFrm.cs` → `MainWindow.axaml{,.cs}` — 1100x760 layout:
        toolbar (New / Copy / Save / Delete / Refresh), left pane has
        the mob `DataGrid` + name-filter `TextBox`, right pane is a
        `TabControl` (General Details / Equipped / Inventory).
      - `MobBaseAssets.cs` → `MobBaseAssetsWindow.axaml{,.cs}` —
        modal asset picker: `ComboBox` of `main_cat` values + flat
        `ListBox` of "`<base_id>: <descr>  (<filename>)`". Returns
        `SelectedID`.
      - `ItemBaseAssets.cs` → `ItemBaseAssetsWindow.axaml{,.cs}` —
        modal item picker: category `ComboBox` + Level filter
        `ComboBox` + flat `ListBox`. Add inserts a `mob_items` row
        via `MobItemsSQL.insertRecord` and exposes `NewMobItem`.
      - `About.cs` → `AboutBox.axaml{,.cs}`.
      - WinForms `PropertyGrid` for per-`mob_items` row props →
        ad-hoc panel (NumericUpDown for usage / drop / qty). Avalonia
        has no PropertyGrid.
      - `DataGridView` of mobs → `DataGrid` (separate
        `Avalonia.Controls.DataGrid` package; theme pulled in via
        `App.axaml` `<StyleInclude Source="avares://Avalonia.Controls.DataGrid/Themes/Fluent.xaml" />`).
      - `ListView` w/ `ListViewGroup` headers + thumbnails → flat
        `ListBox`. The repo never shipped the `images/` tree the
        original loaded thumbnails from, and Avalonia has no native
        grouped-thumbnail `ListView`, so we collapse to a `ListBox`
        and (for the item picker) add a Level filter combo to
        recover the original's grouping affordance.
      - `Utilities/AdobeColors.cs` (**425 LOC** Win32-only
        Photoshop-style colour picker) → `HslConvert.cs` (~40 LOC).
        The `mobs` table tint is H/S/V floats; we only need round-trip
        to a `Color` for the swatch rectangle on the General tab.
      - `BackgroundWorker` → `Task.Run` + `Dispatcher.UIThread.Post`.
      - `OpenFileDialog` → `StorageProvider.OpenFilePickerAsync`.
      - `MessageBox.Show` → MsBox.Avalonia.
      - All 5 SQL wrappers (`MobsSQL`, `MobItemsSQL`, `BaseAssetSQL`,
        `ItemBaseSQL`, `FactionSql`) ported to use parameterised
        `DB.Instance.executeQuery/executeCommand` with `?name`
        placeholders. The original concatenated user-supplied `name`
        and `ai` strings into SQL — port closes those injection holes
        silently. `item_base.2d_asset` uses backticks because the
        column name starts with a digit.
      - Modal-picker windows get parameterless ctors
        (`: this(null)` / `: this(0, 0, null, null, null)`) so the
        AXAML runtime loader can find a public ctor (silences AVLN3001).
      - Top-level `MobRow.cs` POCO satisfies `x:DataType` for the
        compiled-bindings `DataGrid` (same AVLN2100 fix pattern as
        faction-editor).
      - Smoke test (`dotnet run -- --smoke`):
      ```
      login    OK: 290x195 "Login"
      main     OK: 1100x760 "Mob Editor"
      about    OK: 420x240 "About Mob Editor"
      mobAssets OK: 500x500 "Choose Mob Base Asset"
      itemAssets OK: 560x540 "Choose Item Base Asset"
      smoke OK: all 5 mob-editor-avalonia windows instantiated
      ```

      Registered in `tools/Net7Tools.slnx` under
      `/mob-editor-avalonia/`. Whole solution still builds
      (0 errors; 4486 pre-existing legacy WinForms CA1416 warnings).
      Touches: new `tools/mob-editor-avalonia/` (20 files: csproj,
      app.manifest, App.axaml{,.cs}, 5 SQL wrappers, AboutBox,
      MobRow.cs, HslConvert.cs, MobBaseAssetsWindow.axaml{,.cs},
      ItemBaseAssetsWindow.axaml{,.cs}, MainWindow.axaml{,.cs},
      Program.cs, README.md) + slnx entry.
      Status: complete

      **9/14 tools have Linux-native paths now.**

### Tier 7 — first non-SQL editor port (complete)

- [x] **talktreeeditor-avalonia** — first editor port with **no DB
      access**. Full port of `tools/talktreeeditor/`: NPC conversation
      tree editor invoked modally from `missioneditor`/`sector-editor`
      with an XML string in/out. Preserves `SetConversation(string)` +
      `GetConversation(out string)` contract; additionally accepts XML
      as `argv[0]`.
      Touches: `tools/talktreeeditor-avalonia/` (csproj, app.manifest,
      App.axaml{,.cs}, MainWindow.axaml{,.cs}, TreeItem.cs, TalkNode.cs,
      Reply/{Branch,Trade,Flag}Control.axaml{,.cs}, Program.cs,
      README.md) + slnx entry.
      Status: complete

      **Key mechanical changes:**
      - WinForms `TreeView` mutates `TreeNode` directly; Avalonia
        binds via `HierarchicalDataTemplate`. Introduced explicit
        `TreeItem` data model (`Tag` / `Children` / `Parent` /
        `FirstChild` / `NextSibling`) — `TalkNode.parentNode` retyped
        from `TreeNode` to `TreeItem`. `TreeItem.Text` raises
        `PropertyChanged` so post-mutation refresh doesn't need
        rebuilding the tree.
      - Reply UserControls **dropped the typed back-reference** to
        the parent form. `BranchControl` exposes
        `event Action<string> GotoRequested`; `FlagControl` exposes
        `void SetStagesProvider(Func<List<CodeValue>>)`. No
        compile-time coupling to `MainWindow`.
      - 4 reply rows created programmatically in `MainWindow` ctor
        (rather than design-time `Panel` array) — captured-index
        closures wire each row's `typeCbo.SelectionChanged`.
      - `_replyTypeNames` intentionally omits `Trade` (matching the
        original `tools/talktreeeditor/Reply/Flag.cs:23`); `Trade`
        survives in `TalkNodeTypes` enum and in load/save for
        round-tripping legacy XML.
      - Reuses `commontools-avalonia`'s `DlgEditXml` for the raw-XML
        editor (no DB-related work was needed in commontools).
      - `MsBox.Avalonia.Enums.Icon` aliased to `MsBoxIcon` to avoid
        collision with `Avalonia.Controls.Window.Icon` (`WindowIcon`).
      - Compiled bindings: `TreeDataTemplate` declares
        `DataType="local:TreeItem"`.

      **No Login window** — first editor in this tier with zero DB
      surface. `App.OnFrameworkInitializationCompleted` goes straight
      to `MainWindow`.

      **`Validate()` ported verbatim** from `FrmTalkTree.cs:661`:
      unique IDs, first node id=1 + no Trade reply, non-empty text,
      branch destinations exist, `Mission_Goto_Stage` references known
      stage or -2 sentinel, all nodes reachable from node 1.

      Smoke: `dotnet run -- --smoke` instantiates `MainWindow` + all
      3 reply controls (no DB needed).

      **10/14 tools have Linux-native paths now.**

### Tier 8 — launcher port (complete)

- [x] **toolslauncher-avalonia** — Avalonia port of `tools/toolslauncher/ToolsLauncher/`,
      the "launch pad" giving one-click access to all the editors plus the
      LaunchNet7 client launcher. Standalone (no commontools dep, no DB).
      Touches: `tools/toolslauncher-avalonia/` (csproj, app.manifest,
      App.axaml{,.cs}, MainWindow.axaml{,.cs}, SettingsWindow.axaml{,.cs},
      Settings.cs, EditorLauncher.cs, Program.cs, README.md) + slnx entry.
      Status: complete

      **Subsystems dropped** (all pointed at dead infrastructure — the
      `launchnet7-avalonia` Tier 5 precedent established that dead-endpoint
      subsystems get dropped, not ported to no useful target):
      - `GUI/IRCMessenger.cs` + `GUI/PrivateMessage.cs` + `GUI/Login.cs`
        (IRC auth) + `Meebey.SmartIrc4Net` dep — hardcoded
        `eservices.dyndns.org:6667 #test`. dyndns.org's free service shut
        down 2014; the placeholder channel name reveals this was never
        production-grade.
      - `GUI/FtpWindow.cs` + `Struct Data/FtpAddy.cs` — used
        `System.Windows.Forms.WebBrowser` (IE-based, no Avalonia analogue)
        against hardcoded credentials for `net-7.org` FTP (dead).
      - `Updateing/*` + `GUI/FormUpdate.cs` + `Resources/ExeUpdater.exe`
        + `Cryptography/Crc32*.cs` — pointed at `toolspatch.net-7.org`,
        sibling of the dead `patch.net-7.org` already dropped from
        launchnet7-avalonia.
      - `Helpers.cs SQLData` + `Properties/Settings.{settings,Designer.cs}`
        — only used by Login/Updater; replaced by JSON `Settings.cs`.
      - `AssemblyFileInfo.cs`, `WebPath.cs` — only used by Updater.
      - System tray `NotifyIcon` — Avalonia has `TrayIcon` but it's
        finicky under `--smoke` and requires libnotify on Linux; can be
        added later.
      - 6 large editor icon images — buttons use text labels.

      **Key mechanical changes:**
      - **Editor buttons spawn the Avalonia projects via
        `dotnet run --project <csproj>`**, not `Process.Start("<editor>.exe")`.
        `EditorLauncher.cs` resolves the editor's `.csproj` relative to
        the launcher binary (walks up 8 dirs looking for sibling
        `launchnet7-avalonia/` as canary) or uses the configured
        `EditorsCheckoutRoot`. More portable than the original; works
        on Linux without WINE.
      - **`Ported` flag per editor.** `_editors` list in `MainWindow`
        tags each editor with a bool; non-ported entries render as
        `Foo Editor  (not yet ported)` with `IsEnabled=false`. Currently
        true for: mob, faction, talktree (matching what Phase L has
        landed). Flip flags as more Tier-7+ ports merge.
      - **JSON-backed settings** at
        `~/.config/Net7Tools/toolslauncher-avalonia.json` (Linux) /
        `%APPDATA%\Net7Tools\toolslauncher-avalonia.json` (Windows)
        replace `Properties.Settings.Default`. Two keys: `LaunchNet7Path`
        (optional dir containing published `LaunchNet7Avalonia`) and
        `EditorsCheckoutRoot` (optional `tools/` dir).
      - `FolderBrowserDialog` → Avalonia `StorageProvider.OpenFolderPickerAsync`.
      - `MessageBox.Show` status pop-ups → status `TextBlock` at the
        bottom of `MainWindow`. Less intrusive for transient state.
      - `ContextMenuStrip` (right-click on tray icon) → File menu only.

      **No Login window** — no DB surface at all, so
      `App.OnFrameworkInitializationCompleted` goes straight to
      `MainWindow`.

      **Avalonia Grid gotcha:** `Grid` does NOT support
      `ColumnSpacing="N"` (unlike WPF Grid in modern XAML / unlike
      Avalonia `StackPanel.Spacing`). Used spacer column
      `ColumnDefinitions="*,6,Auto"` instead.

      **AVLN3001 fix:** `SettingsWindow` has parameterless
      `public SettingsWindow() : this(new Settings()) { }` delegating
      to the real `SettingsWindow(Settings)` ctor for the XAML toolchain.

      Smoke: `dotnet run -- --smoke` instantiates `MainWindow` (260×380)
      + `SettingsWindow` (500×220). No DB, no network.

      **11/14 tools have Linux-native paths now.**

### Tier 9 — particle effect editor port (complete)

- [x] **effect-editor-avalonia** — Avalonia port of
      `tools/effect-editor/SQLBind/`, the 5-form editor for
      `item_effect_base`, `item_effects`, and `item_effect_container`.
      Touches: `tools/effect-editor-avalonia/` (csproj, app.manifest,
      App.axaml{,.cs}, MainWindow.axaml{,.cs}, EffectSearchWindow.axaml{,.cs},
      EditItemWindow.axaml{,.cs}, ItemBrowseWindow.axaml{,.cs}, CodeValue.cs,
      Program.cs, README.md) + slnx entry + `toolslauncher-avalonia`
      `_editors` flag flipped from `false` → `true`.
      Status: complete

      **Mapping** (5 forms + login + program → 5 windows + program):
      - `Program.cs` (Config.xml roundtrip + custom Login) → `App.axaml.cs`
        (faction-editor's Login→MainWindow swap pattern) + `Program.cs`
        with `--smoke` flag. Custom `Login.cs` with version check against
        a `versions` table dropped — uses the shared
        `commontools-avalonia` `CommonTools.Gui.Login`, which persists
        creds via `LoginData`.
      - `Form1.cs` + `.Designer.cs` (~600 LOC main editor) →
        `MainWindow.axaml{,.cs}` — 740×475, EffectType combo,
        Name/Description/Tooltip, 3 var stat/type slots, 2 constant
        stat/type/value slots, friend/enemy/group/RequireT flag
        checkboxes, Buff dropdown, VisualEffect, Save / New /
        Edit Items buttons.
      - `EffectSearch.cs` + `.designer.cs` →
        `EffectSearchWindow.axaml{,.cs}`.
      - `EditItem.cs` + `.designer.cs` (~700 LOC, 5 hand-laid-out
        effect rows) → `EditItemWindow.axaml{,.cs}` with 5 rows
        constructed programmatically via a `MakeRow(int)` helper
        (one `EffectSlot` object per row, holding combo + 3 var
        textboxes + computed-string TextBlock).
      - `ItemBrowse.cs` + `.designer.cs` →
        `ItemBrowseWindow.axaml{,.cs}`.
      - `CodeValue.cs` (formats combo entries as `"Label (N)"`) →
        verbatim port.
      - `EffectComboHandel.cs` (sic) → folded into `MainWindow`'s
        `FillStats` / `FillBuffs` / `FillVarTypes` static helpers.
      - `SQLDataBase.cs` (private MySQL wrapper, all sprintf-style SQL)
        → dropped. Uses `commontools-avalonia`'s `DB.Instance`.

      **Key mechanical changes:**
      - **All SQL parameterised.** Every `SELECT/INSERT/UPDATE/DELETE`
        goes through `DB.Instance.executeQuery(sql, paramKeys[],
        paramVals[])` / `executeCommand(...)`. The original built SQL
        by string concatenation throughout (`"WHERE name LIKE '%" +
        textbox.Text + "%'"`, etc.) — roughly 20 textbook SQL-injection
        holes silently closed.
      - **`(none)` sentinel at combo index 0** in the per-effect-slot
        combos in `EditItemWindow`. The original relied on
        `SelectedIndex == -1` to mean "delete"; we add an explicit
        `(none)` row so `SelectedIndex == 0` deterministically maps to
        DELETE on save.
      - **DB-touching combo loads in ctor wrapped in try/catch** so
        the `--smoke` test can validate AXAML compile + control wiring
        without a running MySQL. Same defensive pattern as
        mob-editor-avalonia.

      **Preserved verbatim** (these are the bits users would notice
      regressing):
      - `Flag1` bit-packing — `TFriend << 4`, `TEnemy << 5`,
        `TGroupM << 6`.
      - `Flag2 RequireT` encoding — sets bit 0 when checked, sets
        bit 1 when unchecked (the original's surprising "both bits"
        inversion is kept exactly).
      - `NewEffect` defaults — `'none'/'none'/'none'`, `NO_STAT`,
        `BUFF_NONE`, zeros everywhere.
      - Variable-type combo content — `Not Used (0)`,
        `Increase Value (1)`, `Increase Percent (2)`,
        `Decrease Value (3)`, `Decrease Percent (4)`, `Duration (5)`.
      - Item-type combo ordering in `ItemBrowseWindow` — `_itemTypes`
        prepends `(any)` so `SelectedIndex - 1` still maps to the DB
        `type` column value the original computed.
      - `%valueN.Mf%` printf parser in `EditItemWindow.Render(int)` —
        the quirky `for`-loop shape from `EditItem.DisplayString` is
        preserved exactly (uses an `"f%"` sentinel string-search
        rather than a regex). The user-facing in-editor tooltip
        preview depends on it.

      Smoke: `dotnet run -- --smoke` instantiates `Login` (290×195) +
      `MainWindow` (740×475) + `EffectSearchWindow` (640×420) +
      `ItemBrowseWindow` (620×340) + `EditItemWindow` (680×580). All
      5 print OK, no DB required.

      **12/14 tools have Linux-native paths now.**

### Tier 10 — station / room / terminal / NPC / vender editor port (complete)

- [x] **station-tools-avalonia** — Avalonia port of `tools/station-tools/`,
      the 8-form starbase editor for `starbase_objects`, `starbase_rooms`,
      `starbase_terminals`, `starbase_npc`, `starbase_npc_avatar_templates`,
      `starbase_vender_groups`, `starbase_vender_inventory`, and
      `starbase_vendors`.
      Touches: `tools/station-tools-avalonia/` (csproj, app.manifest,
      App.axaml{,.cs}, MainWindow.axaml{,.cs} — 5-tab editor with
      `TreeView` on the left, ItemBrowseWindow.axaml{,.cs},
      FindObjectWindow.axaml{,.cs}, VenderTabControl.axaml{,.cs} —
      embedded UserControl in the 5th tab, LoadAvatar.cs, Program.cs,
      README.md) + slnx entry + `toolslauncher-avalonia` `_editors` flag
      flipped from `false` → `true`.
      Status: complete

      **Mapping** (8 WinForms forms + helpers → 4 windows + 1 user
      control + helpers):
      - `Program.cs` (Config.xml + Login) → `App.axaml.cs`
        (Login → MainWindow swap) + `Program.cs` with `--smoke`.
      - `Main.cs` + `.Designer.cs` (~1900 LOC, 5 tab pages) →
        `MainWindow.axaml{,.cs}`. Top toolbar (Station combo + Load /
        New / Reload / Save All); split layout with `TreeView` on the
        left (Station → Room → Terminals + NPC's folders → leaves) and
        a 5-tab editor on the right (Station / Room / Terminal / NPC /
        Venders).
      - `VenderTab.cs` + `.Designer.cs` (~700 LOC) →
        `VenderTabControl.axaml{,.cs}` — embedded UserControl placed
        directly in the 5th `<TabItem>`, matching the original's
        `tabPage2.Controls.Add` placement.
      - `ItemBrowse.cs` + `.designer.cs` →
        `ItemBrowseWindow.axaml{,.cs}` — name / level / type filters,
        DataGrid bound to `DataTable.DefaultView`,
        `SetMultiSelect(bool)` toggles selection mode for use from both
        Browse (single-pick) and Multi-Item Add (multi-pick).
      - `FindObject.cs` + `.designer.cs` →
        `FindObjectWindow.axaml{,.cs}` — modal starbase picker.
      - `LoadAvatar.cs` (binary parser, not a form) → verbatim port.
        Byte layout preserved: offset 44 header, then `avatarType:int32`,
        `avaterVersion:byte` (typo from original kept — it's a
        wire-format field, renaming would break round-trip), int32
        race/profession/gender/moodType, float[3] colour triples,
        float[5] body weights. Round-trip-compatible with avatar files
        captured by the original tool.
      - `Helpers.cs` (`Utility`, `SQLData`) — `Utility` folded inline;
        `SQLData.ConnStr` dropped (uses `LoginData.ConnStr`).
      - `EditTalkTree.cs` + the entire embedded `TalkTreeEditor/`
        subdirectory → dropped. The NPC tab's "Edit Talk Tree" button
        now spawns the standalone `talktreeeditor-avalonia` project via
        `Process.Start("dotnet", "run --project ../talktreeeditor-avalonia/")`.
      - `StationSQL.cs` (empty stub in original) → dropped (was already
        dead in the WinForms tree).
      - Custom `Login.cs` + `Config.xml` → dropped; uses
        `commontools-avalonia`'s shared `Login`.
      - `SQLDataBase.cs` (private MySQL wrapper) → dropped. Uses
        `DB.Instance.executeQuery/executeCommand`.

      **Key mechanical changes:**
      - **All ~40 SQL call sites parameterised** through
        `commontools-avalonia`'s `DB.Instance`. The original built SQL
        via string concatenation throughout `Main.cs` and
        `VenderTab.cs`. In particular, `ItemBrowse`'s
        `WHERE name LIKE '%" + textbox.Text + "%'` was a textbook
        injection hole — silently closed.
      - **`DataGridView` → `Avalonia.Controls.DataGrid`** bound to
        `DataTable.DefaultView` with `AutoGenerateColumns=True`;
        row access via `(DataRowView)c_ItemLists.SelectedItem`.
      - **`System.Windows.Forms.TreeView` → `Avalonia.Controls.TreeView`**
        with a `HierarchicalDataTemplate<TreeNodeVM>` set in code (via
        `FuncTreeDataTemplate`). Right-click `ContextFlyout` exposes
        Add Room / Add Terminal / Add NPC / Delete instead of the
        original's strip-menu.
      - **Avatar file ingress** — original used both an `OpenFileDialog`
        and a drag-drop handler. Avalonia port uses
        `StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { ... })`
        from an "Add Avatar..." button. Drag-drop dropped. Avatar bytes
        are still hex-encoded and `REPLACE INTO
        starbase_npc_avatar_templates` exactly as the original did.
      - **`MessageBox.Show` → `MsBox.Avalonia`** via the standard
        `using MsBoxIcon = MsBox.Avalonia.Enums.Icon;` alias to avoid
        collision with Avalonia `WindowIcon`.
      - **DB-touching ctor calls wrapped in try/catch** in both
        `MainWindow` and `VenderTabControl` so the smoke test runs
        without MySQL.

      **Preserved verbatim:**
      - `LoadAvatar` binary layout — see Mapping above.
      - Tree hierarchy — Station root, per-Room node, each with a
        `"Terminals"` folder and a `"NPC's"` folder (apostrophe
        preserved — matches original `Main.cs` `LoadStationTree`) as
        children, then leaves inside.
      - Vender schema usage — `starbase_vender_groups` columns
        `SellMultiplyer` / `BuyMultiplyer` typos preserved (they're
        actual column names in the schema dump).
      - `OnDeleteGroup` cascade — three deletes:
        `starbase_vender_groups` + `starbase_vender_inventory` + the
        un-set `UPDATE starbase_vendors SET groupid = -1` the original
        performs, in that order.
      - `OnNewItem` default row literals — `(@g, '0', '0', '0', '0')`.
      - `OnUnlimitedChanged` toggle — disables quantity textbox and
        forces it to `-1` when checked, resets it to `0` when
        unchecked.
      - Terminal type / NPC booth-type / station type / room type combo
        content — same string lists and orderings as `Main.Designer.cs`.

      **What was dropped** (visual-only or non-functional):
      - `DisplayStation` preview composite (renders a stylised station
        thumbnail in the main editor). Visual-only.
      - Runtime icon loading from `/ico/*.gif|.ico` for TreeView nodes
        — Avalonia TreeView shows plain text labels.
      - Drag-drop of `.bin` avatar files (file picker covers it).

      Smoke: `dotnet run -- --smoke` instantiates `Login` (290×195) +
      `MainWindow` (1240×720) + `ItemBrowseWindow` (700×500) +
      `FindObjectWindow` (560×420). All 4 print OK, no DB required.

      **13/14 tools have Linux-native paths now.**

### Tier 11 — mission / stage / condition / completion / reward editor port (complete)

- [x] **missioneditor-avalonia** — Avalonia port of `tools/missioneditor/`,
      the 9-form editor for `missions` (XML-blob storage of mission
      definitions, with per-stage completions, rewards, and talk-tree
      sub-trees).
      Touches: `tools/missioneditor-avalonia/` (csproj, app.manifest,
      App.axaml{,.cs}, MainWindow.axaml{,.cs} — 2-tab editor,
      DlgConditionsWindow.axaml{,.cs}, DlgStagesWindow.axaml{,.cs},
      DlgCompletionsWindow.axaml{,.cs}, DlgRewardsWindow.axaml{,.cs},
      DlgReportWindow.axaml{,.cs}, Nodes/{Mission,Stage,Condition,
      Completion,Reward,TalkTree}.cs, Database/{Database,
      DataConfiguration}.cs, Program.cs, README.md) + slnx entry +
      `toolslauncher-avalonia` `_editors` flag flipped from `false` →
      `true`.
      Status: complete

      **Mapping** (9 WinForms forms → 6 Windows + helpers):
      - `Program.cs` (Config.xml + Login) → `App.axaml.cs`
        (Login → MainWindow swap) + `Program.cs` with `--smoke`.
      - `FrmMission.cs` + `.Designer.cs` (~343 LOC parent form) +
        `TabMission.cs` + `.Designer.cs` (~337 LOC, embedded
        UserControl for the Mission tab) + `TabStages.cs` +
        `.Designer.cs` (~329 LOC, embedded UserControl for the Stages
        tab) → collapsed into single `MainWindow.axaml{,.cs}` with a
        2-tab `TabControl`. Avalonia compiled bindings don't love
        nested UserControl-in-TabItem patterns; collapsing simplified
        the named-field plumbing and made `populateMissionTab` /
        `populateStagesTab` reusable.
      - `DlgConditions.cs` + `.Designer.cs` →
        `DlgConditionsWindow.axaml{,.cs}` (Type/Value/Code/Search/
        Description/Amount + OK/Cancel; switch covers Overall_Level,
        Combat_Level, Explore_Level, Trade_Level, Hull_Level,
        Faction_Required, Item_Required, Profession, Race,
        Mission_Required).
      - `DlgCompletions.cs` + `.Designer.cs` →
        `DlgCompletionsWindow.axaml{,.cs}` (Type/Value combo+text/
        Data combo+text/two Search buttons + OK/Cancel; full switch
        covering all 15 `CompletionType` values).
      - `DlgRewards.cs` + `.Designer.cs` →
        `DlgRewardsWindow.axaml{,.cs}` (Type/Code/Search/Amount +
        OK/Cancel; 10 `RewardType` cases).
      - `DlgStages.cs` + `.Designer.cs` →
        `DlgStagesWindow.axaml{,.cs}` (read-only ID + Description +
        OK/Cancel).
      - `DlgReport.cs` + `.Designer.cs` →
        `DlgReportWindow.axaml{,.cs}` (read-only TextBox; HTML rendered
        as plain text since Avalonia has no built-in `WebBrowser`).
      - `DlgEditXml` — reused from `commontools-avalonia` (already
        ported), invoked from the toolbar "Edit XML" button.
      - `DlgSearch` — reused from `commontools-avalonia`, configured
        against `Net7.Tables.missions`.
      - `Nodes/{Mission,Stage,Condition,Completion,Reward,TalkTree}.cs`
        — ~1300 LOC of data model + XML serialisation, ported verbatim
        with `System.Windows.Forms.MessageBox.Show` swapped for
        `Console.Error.WriteLine` (deep data-model code can't pop UI
        dialogs without a parent Window reference).
      - `Database/Database.cs` — verbatim port (~220 LOC). All SQL
        already parameterised with `?` placeholders in the upstream;
        only the namespace + Nodes import changed.
      - `Database/DataConfiguration.cs` — ported with one signature
        change: `static String search(DataType)` → `static async
        Task<String> search(DataType, Window owner)` because Avalonia
        `ShowDialog` is async. `getDescription()` wrapped in try/catch
        returning the id on DB failure so the editor renders rows
        even when DB descriptions fail to resolve.
      - `TalkNode.cs` / `Replies.cs` — original talk-tree GUI model
        not ported. Talk-tree editing delegated to the standalone
        `talktreeeditor-avalonia` project via `Process.Start` (same
        pattern as `station-tools-avalonia`).

      **Key mechanical changes:**
      - **WinForms `ListView` with subitem columns →
        `Avalonia.Controls.ListBox`** with a `ToString()`-formatted
        `"Type | FormattedValue"` line on per-condition/stage/
        completion/reward `*Row` VM wrappers. The original sortable-
        column behaviour isn't load-bearing for these short lists.
      - **`MessageBox.Show("...") + Yes/No`** for delete confirmation →
        `MessageBoxManager.GetMessageBoxStandard(... ButtonEnum.YesNo,
        MsBoxIcon.Question).ShowWindowDialogAsync(this)` returning
        `ButtonResult.Yes`.
      - **`AvaloniaXamlLoader.Load(this)`** in dialog ctors → fixed to
        `InitializeComponent()`. The former is the legacy reflective
        loader (used by `App.axaml.cs` where no fields are referenced);
        the latter is the compiled-XAML method that wires the
        `x:Name`'d field accessors. Caught by the smoke test: every
        dialog crashed with `NullReferenceException` on `c_TypeCbo` at
        first run because the field wasn't populated.
      - **`async void` event handlers** for every handler that awaits a
        `ShowDialog` (`OnConditionAdd`, `OnConditionEdit`,
        `OnCompletionAdd`, etc.). Fine for UI events.
      - **DB-touching ctor calls wrapped in try/catch** so `--smoke`
        runs without MySQL — `DataConfiguration.init()` and
        `DlgSearch.configure(Tables.missions)` are both guarded.

      **What was dropped** (debug-leftovers or framework-incompatible):
      - `MessageBox.Show("onConditionSelected")` /
        `MessageBox.Show("onCompletionSelected")` /
        `MessageBox.Show("onRewardSelected")` — three obvious
        development-leftover debug calls in `TabMission.cs:128` and
        `TabStages.cs:155,225`. Not ported.
      - `Cursor = Cursors.WaitCursor` — Avalonia handles cursor
        differently; status text already conveys progress.
      - Embedded `TalkTreeEditor.FrmTalkTree` — replaced by
        `Process.Start("dotnet run --project ../talktreeeditor-avalonia/")`.
      - **`DlgReport` as HTML renderer** — rendered as plain text in a
        read-only `TextBox` since Avalonia has no built-in
        `WebBrowser`. Original was viewer-only, so plain-text
        preserves the information content.

      **Round-trip limitation (documented):**
      - `talktreeeditor-avalonia` accepts XML via args[0] but does not
        return the edited XML. Re-importing into `m_stage` needs a
        temp-file or stdout-pipe contract that isn't designed yet.
        Status bar surfaces this: "launched talktreeeditor-avalonia
        (round-trip not wired)". Until then, talk-tree XML can be
        hand-edited via the toolbar "Edit XML" on the whole mission
        XML.

      Smoke: `dotnet run -- --smoke` instantiates `Login` (290×195) +
      `MainWindow` (900×700) + `DlgConditionsWindow` (500×260) +
      `DlgStagesWindow` (420×200) + `DlgCompletionsWindow` (540×320) +
      `DlgRewardsWindow` (500×260) + `DlgReportWindow` (720×600). All
      7 print OK, no DB required.

      **14/15 tools have Linux-native paths now** (only sector-editor
      remains).

### Tier 12 — sector-editor-avalonia (in progress — sub-tiered)

Discovery on first read: the original sector editor is **not just a
large editor with a custom canvas** — it depends on **Piccolo2D**
(`UMD.HCIL.Piccolo.dll`), a defunct .NET 2.0-era 2D scene-graph library
(University of Maryland HCIL, BSD). 149 Piccolo references thread through
`SectorWindow.cs` / `SystemWindow.cs` / `GUI/mainFrm.cs` / all 9 sprite
classes. There is no Avalonia port of Piccolo2D. Trying to do this in a
single commit produces either an unreviewable diff or a broken half-port,
so Tier 12 is split:

- [x] **Tier 12a — project scaffold + window shell**
      Status: done (commit 91ea17c)
      Touches: `tools/sector-editor-avalonia/` (csproj, App.axaml{,cs},
      app.manifest, Program.cs with `--smoke`, `Windows/MainWindow.{axaml,axaml.cs}`,
      README, `tools/Net7Tools.slnx`)
      Notes: `dotnet build` clean, `--smoke` instantiates shared
      `commontools-avalonia` Login + MainWindow shell (TreeView left,
      tabbed canvas centre, properties right). No data layer, no
      dialogs, no Piccolo shim, no sprites yet.

- [x] **Tier 12b — Sql/ layer ported through `commontools-avalonia` DB**
      Status: done
      Touches: `tools/sector-editor-avalonia/Sql/` (Database.cs facade,
      BaseAssetSQL, FactionSql, MobsSQL, Navs, SectorObjects,
      SectorObjectsSql, Sectors, SectorsSql, Systems, SystemsSql)
      Notes: every `UPDATE … SET col='"+r["col"]+"'` site in the
      WinForms editor now rides a `?col` placeholder routed through
      `CommonTools.Database.DB.Instance.executeCommand(query, paramNames,
      paramValues)`. Dropped two files: `Helpers.cs` (SQLData → replaced
      by commontools `LoginData.ConnStr`) and `MobConvertSQL.cs` (one-shot
      migration script targeting a defunct `tmp_enbemulator` database —
      dead code, not carried). `Database.executeQuery(DatabaseName, …)`
      facade kept as the entry point so the Tier 12e sprite port reads
      as a near-1:1 translation; second DatabaseName value rejected
      with a loud throw to surface any forgotten call site. Build clean,
      smoke green.

- [x] **Tier 12c — Props/ POCOs + Utilities scaffolding**
      Status: done
      Touches: `tools/sector-editor-avalonia/Props/` (BaseProps,
      SystemProps, SectorProps, MobProps, PlanetProps, StargateProps,
      StarbaseProps, HarvestableProps), `tools/sector-editor-avalonia/
      Utilities/` (HE_GlobalVars, QuaternionCalc)
      Notes: Props ported with [Category]/[Description]/[ReadOnly]/
      [DefaultProperty]/[Browsable] preserved; all WinForms-only
      [Editor()]/[TypeConverter()] attributes dropped because Avalonia
      has no PropertyGrid equivalent (the Tier 12e UI will need a
      bespoke editor pane). Color uses System.Drawing.Color
      (cross-platform via System.Drawing.Primitives on .NET 10).
      HarvestableProps: renamed `private String field` →
      `field_value` to avoid C# 14's contextual `field` accessor
      keyword. HE_GlobalVars: only the const string tables were
      lifted (the WinForms StringConverter subclasses go away with
      PropertyGrid). QuaternionCalc: pure-math port verbatim.
      **15 dialogs descoped to Tier 12e** (NewSystem, NewSector,
      NewSectorObject, NewSectorObjectType, NewFrm, OptionsGui,
      SoundEffects, HarvestableResTypes, MobGroup, Destination,
      frmContrast, BaseAssets, Settings, AboutBox1, AboutBox2) —
      they depend on mainFrm static-globals, Sprites, and Piccolo
      shim being in place; porting them now would produce untested
      broken scaffolds. Build clean, smoke green.

- [x] **Tier 12d — Piccolo shim on Avalonia primitives**
      Status: done (2026-05-24). Phase S/T have landed; resumed and shipped.
      Touches: `tools/sector-editor-avalonia/PiccoloShim/` — 11 files:
      `Graphics.cs` (Pen/Brush/SolidBrush/Brushes/DashStyle/StringAlignment
      data types), `PNode.cs` (+ `PLayer` subclass), `PPath.cs`, `PImage.cs`,
      `PText.cs`, `PCamera.cs`, `PCanvas.cs`, `PPanEventHandler.cs`,
      `PDragEventHandler.cs`, `MouseWheelZoomController.cs`,
      `PInputEventArgs.cs` (+ `PInputEventHandler` delegate),
      `PiccoloSmoke.cs`.
      Notes:
      - `PCanvas` is an Avalonia `Control` (not Canvas+ScrollViewer) that
        renders its `Layer` (a `PLayer`) into a `DrawingContext` after
        applying `Matrix.CreateTranslation × Matrix.CreateScale` from
        `Camera`. Pointer events pick top-down through the scene graph and
        capture the hit node for drag.
      - `PNode` is a plain class (not an Avalonia Control) — children
        render recursively via `RenderTree(ctx)`. This is closer to
        Piccolo's actual semantics than wrapping every node as a Control
        and avoids per-node visual overhead in scenes with thousands of
        mobs.
      - **Critical design call:** the shim deliberately ships its own
        `Pen` / `Brush` / `SolidBrush` / `Brushes` / `DashStyle` /
        `StringAlignment` types in `Graphics.cs` instead of reusing
        `System.Drawing.*`. `System.Drawing.Pen` and friends pull in
        libgdiplus at runtime on Linux — that would push a libgdiplus
        install dep onto every Linux user and defeat the whole point of
        the Avalonia port. The shim types mirror the System.Drawing
        surface the sprite code touches so the Tier 12e port stays
        drop-in (`new Pen(Color.Red, 3.0f)`, `Brushes.White`,
        `DashStyle.Dash`, `StringAlignment.Center` all compile unchanged).
      - `System.Drawing.Color`, `PointF`, `RectangleF`, `Point`, `Size` are
        pure data structs in `System.Drawing.Primitives` (part of the BCL,
        no GDI+ dep) — those we still consume.
      - `PImage` accepts an Avalonia `Bitmap` directly (or a path / stream)
        instead of wrapping `System.Drawing.Image`. The Tier 12e port will
        load sprite PNGs as `Bitmap` rather than `Image.FromFile`.
      - `PText` uses Avalonia `FormattedText` with Inter typeface (the
        font already shipped via `Avalonia.Fonts.Inter`).
      - `MouseWheelZoomController` is a ctor-compat shim only — the
        actual wheel handling lives in `PCanvas.OnPointerWheelChanged`.
        Kept the type so original sector-editor code that does
        `new MouseWheelZoomController(canvas.Camera)` compiles unchanged.
      - `PiccoloSmoke.Run()` exercises every API the sprites consume:
        builds canvas+layer+image+sub-circles+label, asserts child
        counts/parent/GetChild/TranslateBy/PickTopDown(hit and miss)/
        event firing/Camera Pan+ZoomBy/RemoveChild/RemoveAllChildren/
        MouseWheelZoomController ctor/PDragEventHandler drag delta math.
        Wired into `Program.RunSmoke()` so `--smoke` returns non-zero on
        any failure.
      - Type-ambiguity gotchas resolved with aliases (Avalonia.Media and
        System.Drawing both define `Brush`, `Pen`, `Point`, `Brushes` —
        the shim uses its own namespace's types and aliases Avalonia's
        where needed for rendering).
      - **Honest caveat:** the shim is verified by `PiccoloSmoke` (passes
        green) but NOT yet exercised by real sprite code. Tier 12e port
        may reveal API gaps that need backfilling — likely candidates:
        bounds-rect math, hit-test on rotated nodes, picking through
        ChildrenPickable=false containers (already implemented but
        edge cases). Backfill happens during 12e.
      Smoke: `dotnet run -- --smoke` → `piccolo: ok`. Build: 0 warnings,
      0 errors.

- [ ] **Tier 12e — Sprites + windows**
      Status: deferred — depends on Tier 12d. Same rationale: pivoted
      to Phase S for higher leverage. Resume after Phase S/T.
      Touches: `tools/sector-editor-avalonia/Sprites/` (Mob, Planet,
      Stargate, Starbase, Decoration, Harvestable, Sector,
      SectorBounds; one Sprite per type plus a paired "data" class),
      `Windows/SectorWindow.cs`, `Windows/SystemWindow.cs`,
      `Windows/UniverseWindow.cs`, `Windows/TreeWindow.cs`, and the
      wire-up in `Windows/MainWindow.axaml.cs`
      Notes: mechanical port against the Tier 12d shim. The fiddly
      pieces: QuaternionCalc (port verbatim), AdobeColors (replace
      with `commontools-avalonia`'s HslConvert per mob-editor
      precedent — Win32-only the original uses), drag-drop of object
      types onto the canvas (Avalonia `DragDrop` API, not WinForms
      `DoDragDrop`), and the `mainFrm.selected*` static-globals
      anti-pattern (refactor or preserve, decision per-call-site).

The WinForms binary continues to build and runs under WINE on Linux
(`tools/README.md`). The Avalonia port is the end state; WINE is the
interim story until Tier 12e lands.

## Decisions

- **Keep both UIs in parallel** during migration. The WinForms targets stay in the build until Avalonia ports reach parity. Don't break working code chasing a not-yet-working port.
- **Avalonia, not MAUI/UWP/Eto**. Avalonia has the closest WinForms-developer story (XAML + code-behind), best Linux X11/Wayland support, and an active community. MAUI requires macOS for development workflows (Mac Catalyst dependency), Eto.Forms is lower quality, UWP is Windows-only.
- **Use MVVM lightly**. Avalonia's idiomatic pattern is MVVM, but for porting WinForms apps (which weren't written MVVM) we keep code-behind for now — `View.axaml.cs` instead of `Form.cs` — to minimize the rewrite scope. Refactor to MVVM later if a maintainer wants to.
- **MySql.Data → keep as-is**. The package works cross-platform on .NET 10. No need to swap for MySqlConnector during this phase.
