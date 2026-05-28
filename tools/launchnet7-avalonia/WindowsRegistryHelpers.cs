using System.Runtime.Versioning;
#if WINDOWS_BUILD
using Microsoft.Win32;
#endif

namespace LaunchNet7Avalonia
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
        [SupportedOSPlatform("windows")]
        public static void EnsureRegistered()
        {
#if WINDOWS_BUILD
            const string regKey  = "HKEY_LOCAL_MACHINE\\Software\\Westwood Studios\\Earth and Beyond\\Registration";
            const string authKey = "HKEY_LOCAL_MACHINE\\Software\\EACom\\AuthAuth";

            var currentRegistered = (int?)Registry.GetValue(regKey, "Registered", 0);
            if (currentRegistered != 1)
                Registry.SetValue(regKey, "Registered", 1, RegistryValueKind.DWord);

            // AuthLoginServer is HARDCODED to "localhost". authlogin.dll
            // always dials the in-process LocalAuthRelay on loopback, which
            // re-wraps the call as TLS to the actual upstream. See
            // Launcher.PatchRegistry for the WINE-side equivalent and rationale.
            var currentServer = (string)Registry.GetValue(authKey, "AuthLoginServer", null);
            if (!string.Equals(currentServer, "localhost", System.StringComparison.OrdinalIgnoreCase))
                Registry.SetValue(authKey, "AuthLoginServer", "localhost", RegistryValueKind.String);

            var currentBase = (string)Registry.GetValue(authKey, "AuthLoginBaseService", null);
            if (!string.Equals(currentBase, "AuthLogin", System.StringComparison.Ordinal))
                Registry.SetValue(authKey, "AuthLoginBaseService", "AuthLogin", RegistryValueKind.String);
#else
            throw new System.PlatformNotSupportedException(
                "Built without the Microsoft.Win32.Registry package; rebuild with WINDOWS_BUILD if you need this code path.");
#endif
        }
    }
}
