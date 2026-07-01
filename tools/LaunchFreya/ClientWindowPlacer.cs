using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace LaunchFreya
{
    // Optional post-launch client-window placement (the launcher's WindowX/WindowY
    // setting). The launcher spawns the client (directly, or through the injector,
    // or under WINE) and does NOT block on it, so the window we want to move does
    // not exist yet when Launch() returns. This helper snapshots the pre-existing
    // client windows, then -- on a background task -- waits for a NEW one to appear
    // and moves it to (x, y). Best-effort: every failure is logged, never thrown,
    // so a placement problem can never break the launch itself.
    //
    // Cross-platform, matching how the launcher runs the client:
    //   - Windows: find the new client PROCESS and move its MainWindowHandle with
    //     user32!SetWindowPos.
    //   - Linux (WINE): find the new X11 window by WM_CLASS and move it with xdotool
    //     (the client renders through WINE's X11 driver). If xdotool is not
    //     installed the placement is skipped with a clear log line.
    //
    // Multibox-safe: the "which window is ours" ambiguity (several client.exe
    // windows can be open at once) is resolved by diffing against a snapshot taken
    // immediately BEFORE the client is started -- we only ever move a window/process
    // that was not already there.
    static class ClientWindowPlacer
    {
        // How long to keep looking for the freshly-launched window before giving up.
        static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);
        static readonly TimeSpan Poll = TimeSpan.FromMilliseconds(500);

        static bool OnWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

        // The X11 WM_CLASS / Windows process name the client presents. Both derive
        // from the client exe file name (e.g. "client.exe" -> class "client.exe",
        // process "client").
        static string ExeFileName(string clientPath) =>
            string.IsNullOrEmpty(clientPath) ? "client.exe" : Path.GetFileName(clientPath);

        // Snapshot the client windows/processes that exist right now, so the placer
        // can tell the one we are about to launch apart from any already open.
        public static object Snapshot(string clientPath)
        {
            try
            {
                if (OnWindows)
                    return new HashSet<int>(ClientProcessIds(ExeFileName(clientPath)));
                return new HashSet<string>(XdotoolWindowIds(ExeFileName(clientPath)));
            }
            catch { return OnWindows ? (object)new HashSet<int>() : new HashSet<string>(); }
        }

        // Kick off the wait-then-move on a background task. Returns immediately.
        public static void PlaceAsync(string clientPath, object snapshot, int x, int y, Action<string> log)
        {
            log ??= _ => { };
            log($"Window placement: waiting for the client window to move it to ({x}, {y}).");
            _ = Task.Run(() =>
            {
                try
                {
                    if (OnWindows)
                        PlaceWindows(ExeFileName(clientPath), snapshot as HashSet<int>, x, y, log);
                    else
                        PlaceLinux(ExeFileName(clientPath), snapshot as HashSet<string>, x, y, log);
                }
                catch (Exception ex)
                {
                    log("Window placement failed: " + ex.Message);
                }
            });
        }

        // ---- Windows ----------------------------------------------------------

        static void PlaceWindows(string exeName, HashSet<int> before, int x, int y, Action<string> log)
        {
            before ??= new HashSet<int>();
            string procName = Path.GetFileNameWithoutExtension(exeName);
            var deadline = DateTime.UtcNow + Timeout;
            while (DateTime.UtcNow < deadline)
            {
                foreach (int pid in ClientProcessIds(exeName).Where(p => !before.Contains(p)))
                {
                    IntPtr hwnd;
                    try
                    {
                        using var p = Process.GetProcessById(pid);
                        p.Refresh();
                        hwnd = p.MainWindowHandle;
                    }
                    catch { continue; }   // process may have exited between listing and open
                    if (hwnd != IntPtr.Zero)
                    {
                        if (SetWindowPos(hwnd, IntPtr.Zero, x, y, 0, 0,
                                         SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE))
                            log($"Window placement: moved '{procName}' (pid {pid}) to ({x}, {y}).");
                        else
                            log($"Window placement: SetWindowPos failed (win32 {Marshal.GetLastWin32Error()}).");
                        return;
                    }
                }
                System.Threading.Thread.Sleep(Poll);
            }
            log($"Window placement: gave up after {Timeout.TotalSeconds:0}s -- no new '{procName}' window appeared.");
        }

        static IEnumerable<int> ClientProcessIds(string exeName)
        {
            string procName = Path.GetFileNameWithoutExtension(exeName);
            Process[] procs;
            try { procs = Process.GetProcessesByName(procName); }
            catch { yield break; }
            foreach (var p in procs)
            {
                int id;
                try { id = p.Id; } catch { p.Dispose(); continue; }
                p.Dispose();
                yield return id;
            }
        }

        const uint SWP_NOSIZE = 0x0001, SWP_NOZORDER = 0x0004, SWP_NOACTIVATE = 0x0010;

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
                                        int X, int Y, int cx, int cy, uint uFlags);

        // ---- Linux / WINE -----------------------------------------------------

        static void PlaceLinux(string exeName, HashSet<string> before, int x, int y, Action<string> log)
        {
            before ??= new HashSet<string>();
            if (!ToolExists("xdotool"))
            {
                log("Window placement: xdotool not found on PATH -- cannot move the WINE window; skipping.");
                return;
            }
            var deadline = DateTime.UtcNow + Timeout;
            while (DateTime.UtcNow < deadline)
            {
                var fresh = XdotoolWindowIds(exeName).Where(id => !before.Contains(id)).ToList();
                if (fresh.Count > 0)
                {
                    string win = fresh[0];
                    // windowmove uses X11 root coords, matching the SetWindowPos side.
                    var (code, _, err) = Run("xdotool", $"windowmove {win} {x} {y}");
                    if (code == 0)
                        log($"Window placement: moved WINE window {win} ('{exeName}') to ({x}, {y}).");
                    else
                        log($"Window placement: xdotool windowmove failed: {err.Trim()}");
                    return;
                }
                System.Threading.Thread.Sleep(Poll);
            }
            log($"Window placement: gave up after {Timeout.TotalSeconds:0}s -- no new '{exeName}' WINE window appeared.");
        }

        static IEnumerable<string> XdotoolWindowIds(string wmClass)
        {
            var (code, outp, _) = Run("xdotool", $"search --class {wmClass}");
            if (code != 0 || string.IsNullOrWhiteSpace(outp)) yield break;
            foreach (var line in outp.Split('\n'))
            {
                var id = line.Trim();
                if (id.Length > 0) yield return id;
            }
        }

        static bool ToolExists(string tool)
        {
            try { return Run("/usr/bin/env", $"which {tool}").code == 0; }
            catch { return false; }
        }

        static (int code, string stdout, string stderr) Run(string file, string args)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = file,
                    Arguments = args,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                };
                using var p = Process.Start(psi);
                if (p == null) return (-1, "", "could not start " + file);
                string o = p.StandardOutput.ReadToEnd();
                string e = p.StandardError.ReadToEnd();
                p.WaitForExit(5000);
                return (p.HasExited ? p.ExitCode : -1, o, e);
            }
            catch (Exception ex) { return (-1, "", ex.Message); }
        }
    }
}
