# toolslauncher-avalonia

Cross-platform Avalonia port of `tools/toolslauncher/ToolsLauncher/` — the Net-7 "launch pad" that gives one-click access to all the editors plus the LaunchNet7 client launcher.

Built on **.NET 10** + **Avalonia 11.2.3**. Targets `net10.0` (no `-windows` suffix), so it runs on Linux without WINE.

## What this is

A small (260×380) window with one button per editor and a "Launch Net7" button at the bottom. Editor buttons spawn the sibling Avalonia projects from the current checkout via `dotnet run --project ../<name>-avalonia/`. Editors that haven't been ported yet appear disabled with `(not yet ported)` suffixed.

A Settings dialog (File → Settings...) configures:

- `LaunchNet7Path` — explicit directory containing a published `LaunchNet7Avalonia[.exe]`. Leave blank to fall back to `dotnet run --project ../launchnet7-avalonia/`.
- `EditorsCheckoutRoot` — the `tools/` directory. Leave blank to auto-resolve (walks up from the launcher's binary looking for a sibling `launchnet7-avalonia/`).

Settings persist to `~/.config/Net7Tools/toolslauncher-avalonia.json` on Linux and `%APPDATA%\Net7Tools\toolslauncher-avalonia.json` on Windows (via `Environment.SpecialFolder.ApplicationData`).

## Mapping from the original

| Original `tools/toolslauncher/ToolsLauncher/` | Port |
|---|---|
| `GUI/ToolsLauncher.cs` (`Form` with 6 large image buttons, MenuStrip, ContextMenuStrip, NotifyIcon) | `MainWindow.axaml{,.cs}` — buttons generated from a list, simple menu, no tray icon (see "Dropped" below) |
| `GUI/SettingsFrm.cs` + `Properties/Settings.settings` | `SettingsWindow.axaml{,.cs}` + `Settings.cs` (JSON-backed) |
| `Program.cs` (loads `Config.xml` for MySQL creds, shows `Login`) | `Program.cs` (no DB, no Login) + `EditorLauncher.cs` |
| `Struct Data/FileLinkList.cs` | inlined as a tuple list in `MainWindow.axaml.cs` |
| `MessageBox.Show(...)` | status line at the bottom of MainWindow |

## What this port drops

These subsystems all depend on infrastructure that has been dead for years. Per the launchnet7-avalonia precedent (Tier 5), they are dropped rather than ported to no useful endpoint.

| Dropped | Why |
|---|---|
| `GUI/IRCMessenger.cs`, `GUI/PrivateMessage.cs`, `GUI/Login.cs` (IRC), `Meebey.SmartIrc4Net` dep | Hardcoded IRC server `eservices.dyndns.org:6667` channel `#test`. dyndns.org's free service shut down in 2014, and the placeholder channel name betrays this was never production. |
| `GUI/FtpWindow.cs`, `Struct Data/FtpAddy.cs` | Used `System.Windows.Forms.WebBrowser` (IE-based, no Avalonia analogue) against hardcoded credentials for `net-7.org` FTP, which is dead. |
| `Updateing/*` (Updater, UpdateItem, UpdateCheckResult, UpdateCheckStatus, UpdateItemCollection, VersionCompareMode), `GUI/FormUpdate.cs`, `Resources/ExeUpdater.exe` | Pointed at `toolspatch.net-7.org` — sibling of the dead `patch.net-7.org` already dropped from launchnet7-avalonia. |
| `Cryptography/Crc32*.cs` | Only used by Updater. |
| `Helpers.cs` `SQLData` static | Only used by the IRC Login. |
| `Properties/Settings.settings` / `Properties/Settings.Designer.cs` | WinForms `user.config` flow; replaced by JSON in `Settings.cs`. |
| `AssemblyFileInfo.cs`, `WebPath.cs` | Only used by Updater. |
| System tray icon (`NotifyIcon`) | Avalonia has `TrayIcon` but it requires a libnotify-style daemon on Linux and is finicky under `--smoke`. Not included in this first port; can be added by setting `TrayIcon.Icons` in `App.OnFrameworkInitializationCompleted`. |
| 6 large editor icon images (`Resources/*.ico` / `.png`) | Buttons use text labels; the icons are not load-bearing. Drop-in Assets/ if/when desired. |

## What this port adds

- **Editor buttons spawn the Avalonia projects, not `.exe` files.** `EditorLauncher.cs` resolves the editor's `.csproj` relative to the launcher's binary (or the configured `EditorsCheckoutRoot`) and runs `dotnet run --project <path>`. This is more portable than the original's `Process.Start("<editor>.exe")` and works on Linux without WINE.
- **Greys out editors that haven't been ported yet.** Walks the `_editors` list with a `Ported` flag; non-ported entries render as `Foo Editor  (not yet ported)` with `IsEnabled=false`. Update the list as each Tier-7+ port lands.
- **JSON-backed settings** rather than the WinForms `user.config` flow — survives roaming profile quirks and works identically on Linux.

## Editor list (current)

| Label | Project | Ported |
|---|---|:-:|
| Effect Editor   | `effect-editor-avalonia`     |   |
| Item Editor     | `item-editor-avalonia`       |   |
| Mission Editor  | `missioneditor-avalonia`     |   |
| Mob Editor      | `mob-editor-avalonia`        | ✅ |
| Sector Editor   | `sector-editor-avalonia`     |   |
| Station Tools   | `station-tools-avalonia`     |   |
| Faction Editor  | `faction-editor-avalonia`    | ✅ |
| Talk Tree       | `talktreeeditor-avalonia`    | ✅ |

When new editors land, flip their `Ported` flag in `MainWindow.axaml.cs`.

## Building & running

```bash
dotnet build                  # from this directory
dotnet run -- --smoke         # headless smoke test
dotnet run                    # interactive launcher
```

Smoke output:

```
main     OK: 260x380 "Net7 Tools Launch Pad"
settings OK: 500x220 "Tools Launcher Settings"
smoke OK: all 2 toolslauncher-avalonia windows instantiated
```

## License

CC BY-NC-SA 3.0 — see project root `LICENSES/Net7`. Original Net-7 Entertainment headers in ported source files are preserved unchanged.
