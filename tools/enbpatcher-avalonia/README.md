# enbpatcher-avalonia

Avalonia port of `tools/enbpatcher/` — patches the EnB game client.
Previously had no csproj and didn't build at all; this is the only
buildable version.

Patterned after `tools/toolspatcher-avalonia/` (same upstream author
wrote both, very similar shape). Differences:

| | enbpatcher | toolspatcher |
|---|---|---|
| URL                | http://patch.net-7.org/      | http://toolspatch.net-7.org/ |
| Self-name          | EnBPatcher.exe                | ToolsPatcher.exe              |
| Launcher target    | LaunchNet7.exe (hardcoded)    | configurable via args[0]      |
| Game dir           | c:\\net7\\bin                  | c:\\net7\\tools                |

## Build / run / smoke

```sh
dotnet build tools/enbpatcher-avalonia/EnbPatcherAvalonia.csproj
dotnet tools/enbpatcher-avalonia/bin/Debug/net10.0/EnbPatcherAvalonia.dll
dotnet tools/enbpatcher-avalonia/bin/Debug/net10.0/EnbPatcherAvalonia.dll --smoke   # headless test
```

## Cost data point

Second Avalonia port: ~30 minutes once toolspatcher-avalonia was the
template. The Avalonia plumbing (csproj + App.axaml + Program.cs +
smoke test) was a copy-rename-substitute pass; MainWindow.axaml.cs was
the only real translation work.

This confirms the per-form cost drops dramatically once the first port
exists. For the simpler patcher-class tools the cost is mostly
constants-and-substitution work. Editor-class tools (DataGridView, tabs,
DB binding) will still be the heavy lift.
