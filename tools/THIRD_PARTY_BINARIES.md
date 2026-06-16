# C# tool third-party DLLs (historical reference)

The original WinForms editor projects under `tools/<name>/` shipped
vendored third-party DLLs. Those projects have been removed; the Avalonia
ports (`tools/<name>-avalonia/`) replaced them with NuGet packages or
dropped the dependency. This file is kept as a historical record of what
was vendored and why.

| DLL | Was used by | What it is | Avalonia replacement |
|---|---|---|---|
| `MySql.Data.dll` | station-tools, effect-editor, commontools | MySQL ADO.NET provider (Oracle; GPL-licensed) | `Npgsql` (PostgreSQL, MIT) via NuGet |
| `log4net.dll` | commontools | log4net 1.x | `Microsoft.Extensions.Logging` via NuGet |
| `SandDock.dll` | commontools | Divelements docking control suite (legacy WinForms) | Dropped (Avalonia has built-in docking primitives) |
| `UMD.HCIL.Piccolo.dll`, `UMD.HCIL.PiccoloX.dll` | sector-editor, commontools | Piccolo.NET 2D graphics scene-graph (CIL port of Piccolo Java) | Replaced by `tools/sector-editor-avalonia/PiccoloShim/` -- a shim against Avalonia primitives |
| `Meebey.SmartIrc4net.dll` | commontools | IRC client library | Dropped |
| `WeifenLuo.WinFormsUI.Docking.dll` | (various) | DockPanel Suite | Dropped |

## Source availability

All of the above are independently open-source / freely redistributable.

## Per-tool notes

- `tools/launchnet7/` and `tools/launchnet7-old/`: the WinForms launcher
  utilities. Superseded by `tools/LaunchFreya/` (Avalonia). Removed.
- `tools/w3d-parser/`: C# parser for Westwood W3D model format. Not user-facing;
  no Avalonia port was produced.
- `tools/unmix/`: decompiles Westwood `.mix` archives. Legacy C++ utility; kept
  under `tools/unmix/` as historical reference (not built by `FreyaTools.slnx`).
