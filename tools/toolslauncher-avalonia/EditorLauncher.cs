using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace ToolsLauncherAvalonia
{
    // Spawns an editor by Avalonia project name. The original
    // tools/toolslauncher/ launched .exe files in the working directory
    // via ProcessStartInfo. In our checkout the editors are sibling
    // projects under `tools/<name>-avalonia/`, so we resolve the editor's
    // csproj relative to this launcher's binary, then invoke
    // `dotnet run --project <path>`.
    //
    // If `Settings.EditorsCheckoutRoot` is set, it overrides the resolved
    // path — useful when the launcher is published standalone and the
    // editor sources live elsewhere.
    public static class EditorLauncher
    {
        public static (bool Ok, string Detail) Launch(string editorAvaloniaName, Settings settings)
        {
            string toolsRoot = ResolveToolsRoot(settings);
            string projDir   = Path.Combine(toolsRoot, editorAvaloniaName);
            if (!Directory.Exists(projDir))
                return (false, $"Editor project not found: {projDir}");

            string csproj = FindCsproj(projDir);
            if (csproj == null)
                return (false, $"No .csproj in {projDir}");

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName               = "dotnet",
                    WorkingDirectory       = projDir,
                    UseShellExecute        = false,
                    RedirectStandardOutput = false,
                    RedirectStandardError  = false,
                };
                psi.ArgumentList.Add("run");
                psi.ArgumentList.Add("--project");
                psi.ArgumentList.Add(csproj);
                Process.Start(psi);
                return (true, $"started {Path.GetFileName(csproj)}");
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        // Launches LaunchNet7Avalonia from settings.LaunchNet7Path, or
        // falls back to the sibling launchnet7-avalonia project. On
        // non-Windows the in-tree `dotnet run` works directly; on Windows
        // a published binary in LaunchNet7Path is preferred if set.
        public static (bool Ok, string Detail) LaunchNet7(Settings settings)
        {
            if (!string.IsNullOrWhiteSpace(settings.LaunchNet7Path))
            {
                // User pointed at an explicit binary directory.
                string exe = Path.Combine(settings.LaunchNet7Path, "LaunchNet7Avalonia");
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) exe += ".exe";
                if (File.Exists(exe))
                {
                    try { Process.Start(new ProcessStartInfo { FileName = exe, WorkingDirectory = settings.LaunchNet7Path, UseShellExecute = false }); return (true, exe); }
                    catch (Exception ex) { return (false, ex.Message); }
                }
                // Fall through to in-tree fallback if the configured path
                // is stale.
            }
            return Launch("launchnet7-avalonia", settings);
        }

        static string ResolveToolsRoot(Settings settings)
        {
            if (!string.IsNullOrWhiteSpace(settings.EditorsCheckoutRoot)
                && Directory.Exists(settings.EditorsCheckoutRoot))
            {
                return settings.EditorsCheckoutRoot;
            }
            // Walk up from the entry-assembly directory until we find a
            // sibling launchnet7-avalonia (canary).
            string dir = Path.GetDirectoryName(Assembly.GetEntryAssembly()?.Location ?? "");
            for (int i = 0; i < 8 && dir != null; i++)
            {
                if (Directory.Exists(Path.Combine(dir, "launchnet7-avalonia")))
                    return dir;
                dir = Path.GetDirectoryName(dir);
            }
            return Directory.GetCurrentDirectory();
        }

        static string FindCsproj(string dir)
        {
            foreach (var f in Directory.EnumerateFiles(dir, "*.csproj")) return f;
            return null;
        }
    }
}
