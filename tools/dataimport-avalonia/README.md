# dataimport-avalonia

Avalonia port of `tools/dataimport/`. Linux-native build of the
"insert rows from a delimited file into a Net7 table" admin tool.

Depends on `commontools-avalonia` for the Login dialog +
`DB.Instance.importValues` + `Enumeration.AddSortedByName<T>`.

## What it does

Single-window helper:

1. Pick a `Net7.Tables` enum value from the ComboBox.
2. Browse to a delimited values file.
3. Click Import — pushes the rows through
   `CommonTools.Database.DB.Instance.importValues(table, path)`.

Login runs first; if the user cancels, the app exits.

## Build

```sh
dotnet build tools/dataimport-avalonia/DataImportAvalonia.csproj
```

Targets `net10.0` (no `-windows` suffix) — cross-platform, runs natively
on Linux without WINE.

## Run

```sh
dotnet tools/dataimport-avalonia/bin/Debug/net10.0/DataImportAvalonia.dll
```

The Login dialog appears first. On success, the Data Import window opens.

### Headless smoke test

```sh
dotnet tools/dataimport-avalonia/bin/Debug/net10.0/DataImportAvalonia.dll --smoke
```

Constructs the main window under `Avalonia.Headless` (no display
required). Exits 0 on success, prints `smoke OK: window 486x283 title="Data Import …"`.

## What changed vs. the WinForms original

| WinForms                          | Avalonia                              |
|-----------------------------------|---------------------------------------|
| `Form` + `Designer.cs`            | `Window` + `MainWindow.axaml`         |
| Absolute pixel positions          | `Grid` + `StackPanel` layouts         |
| `OpenFileDialog.ShowDialog()`     | `StorageProvider.OpenFilePickerAsync` |
| `login.ShowDialog()` blocks       | Login Closed handler swaps MainWindow |
| `Application.Run(form)`           | `OnExplicitShutdown` + manual chain   |

The Login → MainWindow handoff uses `ShutdownMode.OnExplicitShutdown`
in `App.OnFrameworkInitializationCompleted` rather than trying to
`.GetAwaiter().GetResult()` an async ShowDialog from the lifecycle
callback (which would deadlock the dispatcher).

## Status

- [x] Builds clean on `net10.0` (cross-platform)
- [x] Headless smoke test passes
- [ ] Manual end-to-end import test against a live Net7 database
- [ ] CI runs `--smoke` on every push
