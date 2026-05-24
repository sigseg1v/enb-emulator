# launchnet7-avalonia

Avalonia port of `tools/launchnet7/LaunchNet7/` — the client-side launcher
that picks a server, patches the EnB client's ini files + `authlogin.dll`,
then starts `client.exe` (or `Net7Proxy.exe`).

The WinForms original is 30+ source files split across `FormMain`,
`AdvancedSettings`, `FormUpdate`, `Updateing/`, `Configuration/`,
`Patching/`, plus the `ExeUpdater` and `FileListCreator` side-projects.
This port keeps the parts that still make sense in 2026 and drops the
dead ones.

## Build

```sh
dotnet build tools/launchnet7-avalonia/LaunchNet7Avalonia.csproj
```

Targets `net10.0` (no `-windows` suffix). Runs natively on Linux/macOS;
the launched `client.exe` itself still needs WINE (the launcher just
prefixes `wine` when on non-Windows — see [`Launcher.cs`](Launcher.cs)).

## Run

```sh
dotnet tools/launchnet7-avalonia/bin/Debug/net10.0/LaunchNet7Avalonia.dll
```

### Headless smoke test

```sh
dotnet tools/launchnet7-avalonia/bin/Debug/net10.0/LaunchNet7Avalonia.dll --smoke
```

Exits 0 on success. CI should call this on every push.

## What's dropped vs. the WinForms original

| Dropped                              | Why                                                      |
|--------------------------------------|----------------------------------------------------------|
| `Updateing/Updater.cs` + `FormUpdate`| Upstream patch host `patch.net-7.org` is offline; resurrecting it is its own project. |
| `ExeUpdater` (self-update subproject)| Only meaningful on Windows; a Linux launcher updates itself via the OS package manager. |
| `FileListCreator` subproject         | Used to publish updates to the dead patch host. Useless without it. |
| `WebBrowser` patch-notes pane        | No built-in WebView in Avalonia; the URL was on the dead host anyway. |
| `BackgroundWorker`                   | Replaced by `Task.Run` + `Dispatcher.UIThread.Post`.     |
| `System.Configuration.ConfigurationSection` | Now a NuGet add-on; rewritten as an `XmlDocument` reader in [`Config/LauncherConfig.cs`](Config/LauncherConfig.cs). |
| `Properties.Settings.Default`        | Replaced by JSON-on-disk in [`Config/UserSettings.cs`](Config/UserSettings.cs). |
| `OpenFileDialog`                     | Replaced by `TopLevel.StorageProvider.OpenFilePickerAsync`. |
| `MessageBox.Show`                    | Replaced by `MsBox.Avalonia`.                            |
| kernel32 `GetPrivateProfileString` / `WritePrivateProfileString` P/Invokes | Replaced by a portable INI reader/writer in [`Patching/IniFile.cs`](Patching/IniFile.cs). |
| `Microsoft.Win32.Registry` direct call | Now gated by `#if WINDOWS_BUILD`; logs a warning on non-Windows since WINE manages its own per-prefix registry. See [`WindowsRegistryHelpers.cs`](WindowsRegistryHelpers.cs). |

## What's preserved

- The exact byte-level [`AuthLoginPatcher`](Patching/AuthLoginPatcher.cs)
  offsets (0x8328 HTTPS-flag, 0x82AD port, 0x8292 timeout).
- The patch sequence in [`Launcher.cs`](Launcher.cs): `rg_regdata` rename →
  `rg_regdata.ini` URL → `Auth.ini` URLs → `Network.ini` host across the
  11 known sections → `authlogin.dll` flags → registry (no-op on Linux).
- The `LaunchName` dispatch:
  - `NET7SP` → starts `Net7.exe`, sleeps 25 s, then `Net7Proxy.exe`.
  - `NET7MP` → starts `Net7Proxy.exe`.
  - default → starts `client.exe` directly with `-SERVER_ADDR <ip> -PROTOCOL TCP`.
- The `/L` (Local) and `/LC` (Local Cert) `Net7Proxy.exe` flags.
- The TCP probe of `host:3809` for the "ONLINE / OFFLINE" badge.

## What runs where

- The launcher UI itself is **native** on Linux (Avalonia/Skia, no WINE).
- The processes it spawns (`client.exe`, `Net7Proxy.exe`, `Net7.exe`,
  `Detours.exe`) are Win32 binaries. On non-Windows hosts the launcher
  prefixes `wine "<exe>"`; on Windows it runs them directly.
- The registry patch (`HKLM\Software\Westwood Studios\Earth and Beyond\Registration`)
  is skipped on non-Windows with a warning. WINE creates that key on
  first run anyway; if a user hits a registration-related crash, the
  workaround is `wine regedit`.

## Cost data point

- Source files: ~13 (vs. 30+ in the WinForms original; the dropped
  updater/exe-updater/file-list-creator accounts for the bulk).
- 1 main window (~8 controls) + 3 small POCO configs + 2 patchers.
- Smoke test added.

## Status

- [x] Builds clean on `net10.0` (cross-platform)
- [x] Headless smoke test passes
- [ ] Manual end-to-end test against a live server
- [ ] CI runs `--smoke` on every push
