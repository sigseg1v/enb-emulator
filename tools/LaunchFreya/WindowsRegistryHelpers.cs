using System.Runtime.Versioning;
#if WINDOWS_BUILD
using Microsoft.Win32;
#endif

namespace LaunchFreya
{
    // Wrapper around the Westwood\Earth and Beyond\Registration HKLM
    // key. Compiled out on non-Windows builds. On a stock .NET 10
    // install Microsoft.Win32.Registry is a NuGet package
    // (Microsoft.Win32.Registry) that throws PlatformNotSupportedException
    // on non-Windows; we go further and not even reference the symbol
    // unless the build pulls it in explicitly via WINDOWS_BUILD. The
    // production tool calls Launcher.PatchRegistry which already checks
    // RuntimeInformation.IsOSPlatform(OSPlatform.Windows) before
    // entering, so on a generic net10.0 build of this tool, this method
    // throws (and the caller catches) — which is the right behaviour
    // anyway: a registry call on the wrong OS is a real bug.
    internal static class WindowsRegistryHelpers
    {
        // The EnB client is a 32-bit process, so its RegOpenKeyExA(HKLM,
        // "Software\Westwood Studios\...") is WOW64-redirected to
        // HKLM\Software\Wow6432Node\Westwood Studios\... . This launcher is a
        // 64-bit .NET process, so the string-based Registry.SetValue would write
        // the 64-bit view the client never reads. Write BOTH views (like the WINE
        // path in Launcher.PatchRegistry) so the value lands where the client
        // reads it regardless of either side's bitness. HKLM\Software subkey path
        // only (no hive prefix).
        const string RenderSubKey       = "Software\\Westwood Studios\\Earth and Beyond\\Render";
        const string RegistrationSubKey = "Software\\Westwood Studios\\Earth and Beyond\\Registration";
        const string AuthAuthSubKey     = "Software\\EACom\\AuthAuth";

        [SupportedOSPlatform("windows")]
        public static void EnsureRegistered()
        {
#if WINDOWS_BUILD
            SetHklmValueBothViews(RegistrationSubKey, "Registered", 1, RegistryValueKind.DWord);

            // AuthLoginServer is HARDCODED to "localhost". authlogin.dll
            // always dials the in-process LocalAuthRelay on loopback, which
            // re-wraps the call as TLS to the actual upstream. See
            // Launcher.PatchRegistry for the WINE-side equivalent and rationale.
            SetHklmValueBothViews(AuthAuthSubKey, "AuthLoginServer", "localhost", RegistryValueKind.String);
            SetHklmValueBothViews(AuthAuthSubKey, "AuthLoginBaseService", "AuthLogin", RegistryValueKind.String);
#else
            throw new System.PlatformNotSupportedException(
                "Built without the Microsoft.Win32.Registry package; rebuild with WINDOWS_BUILD if you need this code path.");
#endif
        }

        // BA-4: write the EnB client's render resolution + windowed flag to the
        // Render key. windowed maps to RenderDeviceWindowed (1 = windowed,
        // 0 = fullscreen).
        [SupportedOSPlatform("windows")]
        public static void SetDisplay(int width, int height, bool windowed)
        {
#if WINDOWS_BUILD
            SetHklmValueBothViews(RenderSubKey, "RenderDeviceWidth", width, RegistryValueKind.DWord);
            SetHklmValueBothViews(RenderSubKey, "RenderDeviceHeight", height, RegistryValueKind.DWord);
            SetHklmValueBothViews(RenderSubKey, "RenderDeviceWindowed", windowed ? 1 : 0, RegistryValueKind.DWord);
#else
            throw new System.PlatformNotSupportedException(
                "Built without the Microsoft.Win32.Registry package; rebuild with WINDOWS_BUILD if you need this code path.");
#endif
        }

#if WINDOWS_BUILD
        // Write one HKLM value into BOTH the 32-bit (Wow6432Node) and 64-bit
        // registry views, so a value is correct no matter which bitness reads it.
        [SupportedOSPlatform("windows")]
        static void SetHklmValueBothViews(string subKey, string name, object value, RegistryValueKind kind)
        {
            foreach (var view in new[] { RegistryView.Registry32, RegistryView.Registry64 })
            {
                using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                using var key = baseKey.CreateSubKey(subKey, writable: true);
                key.SetValue(name, value, kind);
            }
        }
#endif
    }
}
