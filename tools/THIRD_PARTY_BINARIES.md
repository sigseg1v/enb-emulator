# C# tool third-party DLLs (vendored)

Each editor under `tools/<name>/` ships with the third-party DLLs it depends on, vendored under `Libs/`, `lib/`, or `SQLBind/`. Phase D (upgrade to .NET 10) replaces these with NuGet packages where possible, but they are kept in-tree so the legacy projects still load in Visual Studio without manual package restore.

| DLL | Used by | What it is | Phase D replacement |
|---|---|---|---|
| `MySql.Data.dll` | station-tools, effect-editor, commontools | MySQL ADO.NET provider (Oracle's; GPL-licensed) | Replace with `MySqlConnector` (MIT) and/or `Npgsql` (PostgreSQL.NET) |
| `log4net.dll` | commontools | log4net 1.x | Replace with `Microsoft.Extensions.Logging` |
| `SandDock.dll` | commontools | Divelements docking control suite (legacy WinForms) | Drop (use built-in TabControl) or keep if it can be NuGet-restored |
| `UMD.HCIL.Piccolo.dll`, `UMD.HCIL.PiccoloX.dll` | sector-editor, commontools | Piccolo.NET 2D graphics scene-graph (CIL port of Piccolo Java) | NuGet: `Piccolo.NET` |
| `Meebey.SmartIrc4net.dll` | commontools | IRC client library (used somewhere — server chat?) | Drop if unused, else NuGet |
| `WeifenLuo.WinFormsUI.Docking.dll` | possible | DockPanel Suite | NuGet: `DockPanelSuite` |

## Source availability

All of the above are independently open-source / freely redistributable. We don't have to keep the DLLs once we move to NuGet — they're kept now only so the pre-Phase-D project state still builds.

## Per-tool notes

- `tools/launchnet7/` and `tools/launchnet7-old/`: launcher utilities. May reference WebView2 or other binaries. To check during Phase D.
- `tools/w3d-parser/`: parses Westwood's W3D model format. P/Invoke risk; may stay on the old framework if the upgrade is non-trivial.
- `tools/unmix/`: decompiles Westwood `.mix` archives. Often a single .cs file plus a CLI.

## Build outputs to NEVER commit

`bin/` and `obj/` are gitignored except where overridden. If a build artefact slips into a re-included path, drop it.
