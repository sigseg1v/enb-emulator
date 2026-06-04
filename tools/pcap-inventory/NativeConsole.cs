// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace N7.Tools.PcapInventory;

/// <summary>
/// Win32 console interop used only to detect a drag-and-drop / double-click
/// launch (where this process owns a freshly-created console window that would
/// vanish the instant <c>main</c> returns), so the tool can hold the window open
/// for the user. No-op on non-Windows.
/// </summary>
internal static class NativeConsole
{
    /// <summary>
    /// Fills <paramref name="processList"/> with the PIDs attached to the
    /// current console and returns the count. A console created just for this
    /// process (double-click / drag-drop from Explorer) has exactly one
    /// attached process; a console inherited from a parent shell has two or
    /// more.
    /// </summary>
    [SupportedOSPlatform("windows")]
    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern uint GetConsoleProcessList(uint[] processList, uint processCount);
}
