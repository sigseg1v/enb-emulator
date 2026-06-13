using System;
using System.IO;

namespace LaunchFreya
{
    // The persistent, user-facing mod store: <launcher-dir>/mods/<id>/.
    // It holds BOTH our distributed mods (each carrying a 'modhash' marker that
    // the self-updater refreshes) and the user's own mods (no marker, an id we
    // never publish, so the updater never touches them). This is the
    // AUTHORITATIVE source the launcher stages into the game and the Configure
    // Mods UI lists. See freya/client-injection/enbmod/MOD-STRUCTURE.md.
    public static class ModStore
    {
        public const string ModHashFileName = "modhash";
        const string ModsDirName = "mods";

        // <launcher-dir>/mods (next to FreyaLauncher.exe).
        public static string Dir() => Path.Combine(AppContext.BaseDirectory, ModsDirName);

        // Seed the store from the bundled mods (bin/scripts/mods/<id>) shipped in
        // the package, copying any mod id NOT already present. This gives a fresh
        // / offline / dev install a working set of our mods with no network round
        // trip; once online, the self-updater refreshes them from CloudFront (an
        // absent modhash reads as a mismatch -> a single re-download). Seeding
        // only fills gaps -- it never overwrites an existing store folder, so a
        // user's edits and the updater's results are both preserved.
        public static void SeedFromBundle(Action<string> log = null)
        {
            log ??= _ => { };
            try
            {
                var scripts = Launcher.LocateScriptsDir();
                if (string.IsNullOrEmpty(scripts)) return;
                var bundleMods = Path.Combine(scripts, "mods");
                if (!Directory.Exists(bundleMods)) return;

                var store = Dir();
                Directory.CreateDirectory(store);
                foreach (var src in Directory.GetDirectories(bundleMods))
                {
                    var id = Path.GetFileName(src);
                    var dst = Path.Combine(store, id);
                    if (Directory.Exists(dst)) continue;   // already present; leave it
                    CopyDir(src, dst);
                    log($"mods: seeded '{id}' into the store from the bundle");
                }
            }
            catch (Exception ex) { log($"mods: seeding from bundle failed: {ex.Message}"); }
        }

        static void CopyDir(string src, string dst)
        {
            Directory.CreateDirectory(dst);
            foreach (var f in Directory.GetFiles(src))
                File.Copy(f, Path.Combine(dst, Path.GetFileName(f)), overwrite: true);
            foreach (var d in Directory.GetDirectories(src))
                CopyDir(d, Path.Combine(dst, Path.GetFileName(d)));
        }
    }
}
