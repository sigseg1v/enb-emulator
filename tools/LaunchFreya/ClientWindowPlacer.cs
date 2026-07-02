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
    // Two hard-won rules shape the implementation:
    //
    //  1. Only the GAME window counts. The client puts up other windows first --
    //     WINE creates invisible helper windows with the same WM_CLASS, and the
    //     EULA dialog (506x362) precedes the game window. Matching "first new
    //     window" moved one of those and left the game window untouched. So the
    //     placer only accepts a VISIBLE window at least MinWidth x MinHeight
    //     (the game renders 640x480 minimum).
    //
    //  2. One move does not stick. The client re-centers its window when it sets
    //     the D3D display mode (and WINE/the WM can shuffle it during map/unmap),
    //     so a single early windowmove is silently undone. The placer therefore
    //     ENFORCES the position: move, read back where the window actually
    //     settled (the WM's frame can offset the observed position from the
    //     requested coords, so the read-back -- not (x, y) -- is the reference),
    //     and re-move whenever the window drifts from that settled position.
    //     It exits once the position has been stable for StableExit, or at the
    //     enforcement deadline.
    //
    // Cross-platform, matching how the launcher runs the client:
    //   - Windows: user32 EnumWindows/GetWindowRect/SetWindowPos on the new
    //     client process's windows.
    //   - Linux (WINE): xdotool search/getwindowgeometry/windowmove (the client
    //     renders through WINE's X11 driver). If xdotool is not installed the
    //     placement is skipped with a clear log line.
    //
    // Multibox-safe: the "which window is ours" ambiguity (several client.exe
    // windows can be open at once) is resolved by diffing against a snapshot taken
    // immediately BEFORE the client is started -- we only ever move a window/process
    // that was not already there.
    static class ClientWindowPlacer
    {
        // How long to keep looking for the freshly-launched game window. Generous:
        // the user may sit on the EULA dialog before the game window exists.
        static readonly TimeSpan AppearTimeout = TimeSpan.FromSeconds(180);
        // After the first successful move, keep re-asserting the position for this
        // long (the D3D mode-set re-center lands within the first seconds, but a
        // slow login can stretch it).
        static readonly TimeSpan EnforceFor = TimeSpan.FromSeconds(60);
        // Exit the enforcement loop early once the window has sat at the settled
        // position for this long.
        static readonly TimeSpan StableExit = TimeSpan.FromSeconds(10);
        static readonly TimeSpan Poll = TimeSpan.FromMilliseconds(500);

        // The smallest window that can be the actual game render window. Filters
        // out WINE's invisible zero/tiny helper windows and the 506x362 EULA
        // dialog.
        const int MinWidth = 640, MinHeight = 480;

        static bool OnWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

        // The X11 WM_CLASS / Windows process name the client presents. Both derive
        // from the client exe file name (e.g. "client.exe" -> class "client.exe",
        // process "client").
        static string ExeFileName(string clientPath) =>
            string.IsNullOrEmpty(clientPath) ? "client.exe" : Path.GetFileName(clientPath);

        // Snapshot the client windows/processes that exist right now, so the placer
        // can tell the one we are about to launch apart from any already open.
        // Snapshots ALL matching window ids (not just game-sized ones): a window
        // that pre-exists in any form is never ours to move.
        public static object Snapshot(string clientPath)
        {
            try
            {
                if (OnWindows)
                    return new HashSet<int>(ClientProcessIds(ExeFileName(clientPath)));
                return new HashSet<string>(XdotoolWindowIds(ExeFileName(clientPath), onlyVisible: false));
            }
            catch { return OnWindows ? (object)new HashSet<int>() : new HashSet<string>(); }
        }

        // Kick off the wait-then-move on a background task. Returns immediately.
        public static void PlaceAsync(string clientPath, object snapshot, int x, int y, Action<string> log)
        {
            log ??= _ => { };
            log($"Window placement: waiting for the client game window to move it to ({x}, {y}).");
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

            // Stage 1: wait for a NEW client process with a visible game-sized window.
            IntPtr hwnd = IntPtr.Zero;
            int ownerPid = 0;
            var appearDeadline = DateTime.UtcNow + AppearTimeout;
            while (DateTime.UtcNow < appearDeadline && hwnd == IntPtr.Zero)
            {
                foreach (int pid in ClientProcessIds(exeName).Where(p => !before.Contains(p)))
                {
                    IntPtr found = FindGameWindow(pid);
                    if (found != IntPtr.Zero) { hwnd = found; ownerPid = pid; break; }
                }
                if (hwnd == IntPtr.Zero) System.Threading.Thread.Sleep(Poll);
            }
            if (hwnd == IntPtr.Zero)
            {
                log($"Window placement: gave up after {AppearTimeout.TotalSeconds:0}s -- no new '{procName}' game window (>= {MinWidth}x{MinHeight}, visible) appeared.");
                return;
            }

            Enforce(
                move: () => SetWindowPos(hwnd, IntPtr.Zero, x, y, 0, 0,
                                         SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE),
                observe: () => GetWindowRect(hwnd, out var r)
                    ? ((int, int)?)(r.Left, r.Top) : null,
                what: $"'{procName}' (pid {ownerPid})", x: x, y: y, log: log);
        }

        // The process's largest visible window of game size, or zero. Not
        // MainWindowHandle: that can be the EULA dialog or a splash.
        static IntPtr FindGameWindow(int pid)
        {
            IntPtr best = IntPtr.Zero;
            long bestArea = 0;
            EnumWindows((h, _) =>
            {
                GetWindowThreadProcessId(h, out uint winPid);
                if (winPid != (uint)pid || !IsWindowVisible(h)) return true;
                if (!GetWindowRect(h, out var r)) return true;
                long w = r.Right - r.Left, ht = r.Bottom - r.Top;
                if (w < MinWidth || ht < MinHeight) return true;
                if (w * ht > bestArea) { bestArea = w * ht; best = h; }
                return true;
            }, IntPtr.Zero);
            return best;
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

        [StructLayout(LayoutKind.Sequential)]
        struct RECT { public int Left, Top, Right, Bottom; }

        delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
                                        int X, int Y, int cx, int cy, uint uFlags);
        [DllImport("user32.dll")]
        static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
        [DllImport("user32.dll")]
        static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll")]
        static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
        [DllImport("user32.dll")]
        static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        // ---- Linux / WINE -----------------------------------------------------

        static void PlaceLinux(string exeName, HashSet<string> before, int x, int y, Action<string> log)
        {
            before ??= new HashSet<string>();
            if (!ToolExists("xdotool"))
            {
                log("Window placement: xdotool not found on PATH -- cannot move the WINE window; skipping.");
                return;
            }

            // Stage 1: wait for a NEW visible game-sized window. --onlyvisible is
            // essential -- a bare `xdotool search --class` also returns WINE's
            // invisible helper windows and the placer used to move one of those.
            string win = null;
            var appearDeadline = DateTime.UtcNow + AppearTimeout;
            while (DateTime.UtcNow < appearDeadline && win == null)
            {
                win = XdotoolWindowIds(exeName, onlyVisible: true)
                    .Where(id => !before.Contains(id))
                    .FirstOrDefault(id =>
                    {
                        var g = XdotoolGeometry(id);
                        return g.HasValue && g.Value.w >= MinWidth && g.Value.h >= MinHeight;
                    });
                if (win == null) System.Threading.Thread.Sleep(Poll);
            }
            if (win == null)
            {
                log($"Window placement: gave up after {AppearTimeout.TotalSeconds:0}s -- no new '{exeName}' game window (>= {MinWidth}x{MinHeight}, visible) appeared.");
                return;
            }

            Enforce(
                // windowmove uses X11 root coords, matching the SetWindowPos side.
                move: () => Run("xdotool", $"windowmove {win} {x} {y}").code == 0,
                observe: () =>
                {
                    var g = XdotoolGeometry(win);
                    return g.HasValue ? ((int, int)?)(g.Value.x, g.Value.y) : null;
                },
                what: $"WINE window {win} ('{exeName}')", x: x, y: y, log: log);
        }

        static IEnumerable<string> XdotoolWindowIds(string wmClass, bool onlyVisible)
        {
            string vis = onlyVisible ? "--onlyvisible " : "";
            var (code, outp, _) = Run("xdotool", $"search {vis}--class {wmClass}");
            if (code != 0 || string.IsNullOrWhiteSpace(outp)) yield break;
            foreach (var line in outp.Split('\n'))
            {
                var id = line.Trim();
                if (id.Length > 0) yield return id;
            }
        }

        // X, Y, WIDTH, HEIGHT of a window from `getwindowgeometry --shell`, or null
        // if the window is gone / the output is unparseable.
        static (int x, int y, int w, int h)? XdotoolGeometry(string win)
        {
            var (code, outp, _) = Run("xdotool", $"getwindowgeometry --shell {win}");
            if (code != 0) return null;
            int x = 0, y = 0, w = 0, h = 0; bool gx = false, gy = false, gw = false, gh = false;
            foreach (var line in outp.Split('\n'))
            {
                var kv = line.Trim().Split('=');
                if (kv.Length != 2 || !int.TryParse(kv[1], out int v)) continue;
                switch (kv[0])
                {
                    case "X": x = v; gx = true; break;
                    case "Y": y = v; gy = true; break;
                    case "WIDTH": w = v; gw = true; break;
                    case "HEIGHT": h = v; gh = true; break;
                }
            }
            return gx && gy && gw && gh ? (x, y, w, h) : ((int, int, int, int)?)null;
        }

        // ---- shared enforcement loop -------------------------------------------

        // Move the window, learn where it actually settled (the WM frame offsets
        // the observed position from the requested coords), then keep watching:
        // any drift from the settled position means the game/WM moved it, so move
        // it back and re-learn. Stop once stable for StableExit or at the deadline.
        static void Enforce(Func<bool> move, Func<(int x, int y)?> observe,
                            string what, int x, int y, Action<string> log)
        {
            if (!move())
            {
                log($"Window placement: initial move of {what} to ({x}, {y}) failed.");
                return;
            }
            log($"Window placement: moved {what} to ({x}, {y}); holding position...");

            (int x, int y)? settled = null;
            int moves = 1;
            var enforceDeadline = DateTime.UtcNow + EnforceFor;
            var stableSince = DateTime.UtcNow;
            while (DateTime.UtcNow < enforceDeadline)
            {
                System.Threading.Thread.Sleep(Poll);
                var pos = observe();
                if (pos == null)
                {
                    log($"Window placement: {what} disappeared while holding position.");
                    return;
                }
                if (settled == null)
                {
                    settled = pos;           // first read-back after a move
                    stableSince = DateTime.UtcNow;
                }
                else if (pos.Value != settled.Value)
                {
                    // The game re-centered on D3D mode-set (or the WM shuffled
                    // it). Re-assert and re-learn the settled position.
                    if (!move())
                    {
                        log($"Window placement: re-move of {what} failed; leaving it at ({pos.Value.x}, {pos.Value.y}).");
                        return;
                    }
                    moves++;
                    settled = null;
                    stableSince = DateTime.UtcNow;
                }
                else if (DateTime.UtcNow - stableSince >= StableExit)
                {
                    log($"Window placement: {what} settled at ({settled.Value.x}, {settled.Value.y}) after {moves} move(s).");
                    return;
                }
            }
            log($"Window placement: enforcement window over for {what} ({moves} move(s)); leaving it as-is.");
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
