using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

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
                    if (Directory.Exists(dst))
                    {
#if DEBUG
                        // Dev iteration: in a dev build the bundle (the repo's
                        // scripts/) is the source of truth, so refresh our OWN
                        // bundled mods into the store on every launch. Without
                        // this the store SHADOWS the bundle forever (seeding only
                        // fills gaps), so a Lua edit to one of our mods never
                        // reaches the game -- you would have to hand-delete the
                        // store copy. This loop only ever iterates ids present in
                        // the bundle (ours), so a user's own mod (an id we never
                        // ship) is never touched. Release builds keep gap-only
                        // seeding: there the online self-updater (modhash) owns
                        // refresh and re-copying the shipped bundle would regress
                        // a newer updater-fetched mod.
                        try
                        {
                            Directory.Delete(dst, recursive: true);
                            CopyDir(src, dst);
                            MarkOurs(dst);
                            log($"mods: refreshed '{id}' in the store from the bundle (dev build)");
                        }
                        catch (Exception ex)
                        {
                            log($"mods: could not refresh '{id}' from the bundle: {ex.Message}");
                        }
#endif
                        continue;   // present: gap-filled (release) or refreshed (debug)
                    }
                    CopyDir(src, dst);
#if DEBUG
                    MarkOurs(dst);
#endif
                    log($"mods: seeded '{id}' into the store from the bundle");
                }

#if DEBUG
                // Dev iteration: the bundle is the complete set of OUR mods, so
                // prune store folders that are ours (carry the marker we write
                // above) but no longer exist in the bundle -- i.e. a mod we
                // renamed or deleted. A folder WITHOUT the marker is the dev's own
                // scratch mod and is left alone. Release builds rely on the online
                // updater's manifest-driven prune instead.
                var bundleIds = new HashSet<string>(
                    Directory.GetDirectories(bundleMods).Select(Path.GetFileName),
                    StringComparer.OrdinalIgnoreCase);
                foreach (var dir in Directory.GetDirectories(store))
                {
                    var id = Path.GetFileName(dir);
                    if (bundleIds.Contains(id)) continue;
                    if (!File.Exists(Path.Combine(dir, ModHashFileName))) continue; // dev's own
                    try
                    {
                        Directory.Delete(dir, recursive: true);
                        log($"mods: removed '{id}' from the store (no longer in the bundle)");
                    }
                    catch (Exception ex)
                    {
                        log($"mods: could not remove stale '{id}': {ex.Message}");
                    }
                }
#endif
            }
            catch (Exception ex) { log($"mods: seeding from bundle failed: {ex.Message}"); }
        }

#if DEBUG
        // Tag a dev-seeded folder as ours so the dev prune can tell it apart from
        // the dev's own scratch mods. The sentinel value never matches a real
        // published hash, so if this build ever goes online the updater simply
        // re-fetches the mod -- harmless in dev.
        static void MarkOurs(string dir)
        {
            try { File.WriteAllText(Path.Combine(dir, ModHashFileName), "dev"); }
            catch { /* best-effort: prune just won't see this folder as ours */ }
        }
#endif

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
