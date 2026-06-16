using System;
using System.IO;
using System.Linq;

namespace LaunchFreya
{
    /// <summary>
    /// Log-file location + read helpers split out of <c>MainWindow</c> (Phase AT-5)
    /// so the filesystem logic is unit-testable against a temp dir without an
    /// Avalonia window. Pure filesystem access, no UI, no state.
    /// </summary>
    public static class LauncherLogFiles
    {
        /// <summary>
        /// Wrap a path-resolver in a snapshot reader: each call resolves the path,
        /// then reads the whole file (shared read, tolerant of a writer holding it).
        /// Returns null when there is no file to read; a parenthesised error string
        /// when the file exists but could not be read.
        /// </summary>
        public static Func<string> ReadLogFile(Func<string> resolve) => () =>
        {
            string path;
            try { path = resolve(); } catch { path = null; }
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            try
            {
                using var fs = new FileStream(
                    path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var sr = new StreamReader(fs);
                return sr.ReadToEnd();
            }
            catch (Exception ex)
            {
                return "(could not read " + Path.GetFileName(path) + ": " + ex.Message + ")";
            }
        };

        /// <summary>Return <paramref name="path"/> if it exists, else null.</summary>
        public static string ExistingFile(string path)
            => File.Exists(path) ? path : null;

        /// <summary>
        /// Newest file matching <paramref name="pattern"/> in <paramref name="dir"/>
        /// by last-write time. The proxy's daily log is _YYYY_MM_DD.log, so "most
        /// recent" == newest mtime.
        /// </summary>
        public static string NewestLogFile(string dir, string pattern)
        {
            try
            {
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return null;
                return new DirectoryInfo(dir).GetFiles(pattern)
                    .OrderByDescending(f => f.LastWriteTimeUtc)
                    .Select(f => f.FullName)
                    .FirstOrDefault();
            }
            catch { return null; }
        }

        /// <summary>
        /// The EnB client has no standard text log; probe the names it might use
        /// and report none rather than mis-grabbing an unrelated *.log (enbmod's
        /// log lives in the same folder).
        /// </summary>
        public static string FindClientLog(string clientDir)
        {
            if (string.IsNullOrEmpty(clientDir)) return null;
            foreach (var name in new[] { "client.log", "Net7.log", "clientlog.txt", "eb.log" })
            {
                var p = Path.Combine(clientDir, name);
                if (File.Exists(p)) return p;
            }
            return null;
        }
    }
}
