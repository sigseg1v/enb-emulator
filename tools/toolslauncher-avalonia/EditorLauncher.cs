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

        // Launches the interactive CLI client via `just play-cli`. Unlike the
        // editors and LaunchNet7 (GUI apps), the CLI is a REPL that needs a
        // TTY, so it is spawned inside a terminal emulator. `just play-cli`
        // brings up the shared docker stack without bouncing it, starts a
        // dedicated CLI proxy, then drops into the REPL.
        public static (bool Ok, string Detail) LaunchNet7Cli(Settings settings)
        {
            string repoRoot = ResolveRepoRoot(settings);
            if (repoRoot == null)
                return (false, "justfile not found walking up from launcher / tools root");

            const string runCmd = "just play-cli";
            try
            {
                ProcessStartInfo psi;
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    psi = new ProcessStartInfo { FileName = "cmd.exe", WorkingDirectory = repoRoot, UseShellExecute = false };
                    psi.ArgumentList.Add("/c");
                    psi.ArgumentList.Add("start");
                    psi.ArgumentList.Add("");          // start's window-title arg
                    psi.ArgumentList.Add("cmd.exe");
                    psi.ArgumentList.Add("/k");
                    psi.ArgumentList.Add(runCmd);
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    psi = new ProcessStartInfo { FileName = "osascript", WorkingDirectory = repoRoot, UseShellExecute = false };
                    psi.ArgumentList.Add("-e");
                    psi.ArgumentList.Add($"tell application \"Terminal\" to do script \"cd '{repoRoot}' && {runCmd}\"");
                }
                else
                {
                    var term = FindLinuxTerminal();
                    if (term == null)
                        return (false, "no terminal emulator found (install gnome-terminal, konsole, or xterm)");
                    // Keep the shell open after the REPL exits so the final
                    // output stays readable.
                    string script = $"{runCmd}; echo; echo '[CLI exited -- press enter to close]'; read";
                    psi = new ProcessStartInfo { FileName = term.Value.Bin, WorkingDirectory = repoRoot, UseShellExecute = false };
                    psi.ArgumentList.Add(term.Value.DashDash ? "--" : "-e");
                    psi.ArgumentList.Add("bash");
                    psi.ArgumentList.Add("-c");
                    psi.ArgumentList.Add(script);
                }
                Process.Start(psi);
                return (true, runCmd);
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        // Repo root == directory containing the justfile. Walk up from the
        // tools root (which holds launchnet7-avalonia) and then from the
        // launcher binary as a fallback.
        static string ResolveRepoRoot(Settings settings)
        {
            foreach (string start in new[] {
                         ResolveToolsRoot(settings),
                         Path.GetDirectoryName(Assembly.GetEntryAssembly()?.Location ?? ""),
                     })
            {
                string dir = start;
                for (int i = 0; i < 10 && !string.IsNullOrEmpty(dir); i++)
                {
                    if (File.Exists(Path.Combine(dir, "justfile"))) return dir;
                    dir = Path.GetDirectoryName(dir);
                }
            }
            return null;
        }

        // Returns (binary, usesDashDash) for the first available terminal
        // emulator. gnome-terminal introduces the command with `--`; the
        // others use `-e`.
        static (string Bin, bool DashDash)? FindLinuxTerminal()
        {
            var candidates = new (string Bin, bool DashDash)[]
            {
                ("x-terminal-emulator", false),
                ("gnome-terminal",      true),
                ("konsole",             false),
                ("xfce4-terminal",      false),
                ("xterm",               false),
            };
            string pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
            string[] dirs = pathEnv.Split(Path.PathSeparator);
            foreach (var c in candidates)
                foreach (var d in dirs)
                {
                    if (string.IsNullOrEmpty(d)) continue;
                    try { if (File.Exists(Path.Combine(d, c.Bin))) return c; }
                    catch { /* malformed PATH entry */ }
                }
            return null;
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
