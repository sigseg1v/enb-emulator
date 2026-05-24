using System;
using System.Runtime.InteropServices;
using System.Text;

namespace LaunchNet7Avalonia
{
    // Original LauncherUtility.GetShortPathName called the kernel32
    // GetShortPathNameW Win32 API. On Windows we keep that behaviour
    // because the EnB client and Net7Proxy were observed to choke on
    // long paths with spaces. On non-Windows there is no 8.3 alias to
    // resolve to — callers should pass the path through unchanged.
    internal static class ShortPath
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int GetShortPathNameW(
            [MarshalAs(UnmanagedType.LPWStr)] string lpszLongPath,
            [MarshalAs(UnmanagedType.LPWStr)] StringBuilder lpszShortPath,
            int cchBuffer);

        public static string Get(string longPath)
        {
            if (string.IsNullOrEmpty(longPath)) return longPath;
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return longPath;
            var sb = new StringBuilder(longPath.Length + 1);
            int n = GetShortPathNameW(longPath, sb, sb.Capacity);
            if (n == 0) return longPath;
            if (n > sb.Capacity)
            {
                sb = new StringBuilder(n);
                n = GetShortPathNameW(longPath, sb, sb.Capacity);
                if (n == 0) return longPath;
            }
            return sb.ToString();
        }
    }
}
