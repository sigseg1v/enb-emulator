# toolspatcher-avalonia

Avalonia port of `tools/toolspatcher/`. **Phase L proof-of-concept** — the
first WinForms→Avalonia migration in the editor suite. Validates that a
small editor can be ported cleanly and run natively on Linux without
WINE.

## What it does

Self-updating patcher for the editor suite. Downloads `Version.txt` from
the (legacy) `toolspatch.net-7.org` host, compares to local `Version.txt`,
fetches the `Files.txt` manifest, CRC32-checks every entry, downloads
mismatches, then launches the LaunchNet7 exe.

The HTTP protocol and CRC32 logic match the original byte-for-byte. The
UI was rewritten in AXAML.

## Build

```sh
dotnet build tools/toolspatcher-avalonia/ToolsPatcherAvalonia.csproj
```

Targets `net10.0` (no `-windows` suffix) so it builds and runs on Linux,
macOS, and Windows. No WINE required.

## Run

```sh
dotnet tools/toolspatcher-avalonia/bin/Debug/net10.0/ToolsPatcherAvalonia.dll [LauncherExe]
```

`LauncherExe` is the launcher executable to start once patching
completes; defaults to `LaunchNet7.exe` to match the WinForms behaviour.

### Headless smoke test

```sh
dotnet tools/toolspatcher-avalonia/bin/Debug/net10.0/ToolsPatcherAvalonia.dll --smoke
```

Uses `Avalonia.Headless` to instantiate `App` + `MainWindow` without a
display. Exits 0 on success, prints `smoke OK: window 573x363 title="…"`.
This is what CI and `just test` should run.

## What changed vs. the WinForms original

| WinForms                              | Avalonia                                  |
|---------------------------------------|-------------------------------------------|
| `Form` + `Form1.Designer.cs`          | `Window` + `MainWindow.axaml`             |
| Absolute pixel positions              | `Grid` + `StackPanel` layouts             |
| `WebClient` (obsolete in .NET 6+)     | `HttpClient`                              |
| `Control.Invoke(delegate)`            | `Dispatcher.UIThread.Post(Action)`        |
| `Thread.Abort` (gone in .NET 5+)      | `CancellationToken` cooperative cancel    |
| `WebBrowser` showing patch notes URL  | `TextBox` placeholder (host is dead anyway; Avalonia has no built-in WebView) |
| `MessageBox.Show`                     | `MsBox.Avalonia` `ShowWindowDialogAsync`  |

The WinForms `Form1` used 4 `delegate`-typed callbacks to marshal
progress updates to the UI thread. The Avalonia version replaces these
with three `Post...` helper methods that wrap `Dispatcher.UIThread.Post`.

## Cost data point

Time to port (from "start" to "smoke test passing", inclusive of
plumbing Avalonia + writing a headless test):

- 1 form (10 controls, 1 timer, ~450 LOC of logic): ~2 hours.

This is a clean small case. Bigger editors with `DataGridView`,
multi-pane layouts, custom paint, and `PropertyGrid` will be
considerably more — see [`plans/12-phase-l-avalonia.md`](../../plans/12-phase-l-avalonia.md)
for the full suite estimate.

## Status

- [x] Builds clean on `net10.0` (cross-platform)
- [x] Headless smoke test passes
- [ ] Manual end-to-end test against a live patch server (the original
      `toolspatch.net-7.org` is gone; would need a stand-in)
- [ ] CI runs `--smoke` on every push
