using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace LaunchFreya
{
    // One enbmod Lua mod as described by its scripts/mods/<id>/mod.json manifest.
    public sealed class ModInfo
    {
        public string Id { get; init; } = "";
        public string Name { get; init; } = "";
        public string Author { get; init; } = "";
        public string Description { get; init; } = "";
        public string Entrypoint { get; init; } = "init.lua";
        // Other mod ids this mod needs enabled to work (mod.json "dependencies").
        // The Configure Mods window flags a mod red when a dependency is disabled
        // or missing. Empty for mods with no declared dependencies.
        public IReadOnlyList<string> Dependencies { get; init; } = Array.Empty<string>();
        // Absolute path of the mod folder in the SOURCE scripts tree (used for staging).
        public string Dir { get; init; } = "";
    }

    // Discovers the available enbmod mods by reading the mod.json manifests under
    // the persistent mod store (<launcher-dir>/mods/, the same tree
    // StageClientMods stages from and the self-updater refreshes). Both the
    // launcher's staging step and the "Configure Mods" window use this so the
    // catalog and what gets staged can never drift. Lists OUR mods and the user's
    // own mods alike -- they coexist in the store.
    public static class ModCatalog
    {
        // The persistent mod store dir, or null if it doesn't exist yet. Callers
        // that want the bundled mods seeded first should call
        // ModStore.SeedFromBundle() before scanning.
        public static string ModsDir()
        {
            var mods = ModStore.Dir();
            return Directory.Exists(mods) ? mods : null;
        }

        // Read and parse every mod.json under the source mods/ dir, sorted by id.
        // Returns an empty list if the scripts tree isn't present or has no mods.
        public static List<ModInfo> Scan()
        {
            var result = new List<ModInfo>();
            var modsDir = ModsDir();
            if (modsDir == null) return result;

            foreach (var dir in Directory.GetDirectories(modsDir).OrderBy(d => Path.GetFileName(d), StringComparer.OrdinalIgnoreCase))
            {
                var manifest = Path.Combine(dir, "mod.json");
                if (!File.Exists(manifest)) continue;
                var folderName = Path.GetFileName(dir);
                try
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(manifest));
                    var root = doc.RootElement;
                    string Get(string k, string fallback) =>
                        root.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String
                            ? v.GetString() : fallback;
                    List<string> GetStrArray(string k)
                    {
                        var list = new List<string>();
                        if (root.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Array)
                            foreach (var e in v.EnumerateArray())
                                if (e.ValueKind == JsonValueKind.String)
                                    list.Add(e.GetString());
                        return list;
                    }
                    result.Add(new ModInfo
                    {
                        Id           = Get("id", folderName),
                        Name         = Get("name", folderName),
                        Author       = Get("author", "Unknown"),
                        Description  = Get("description", ""),
                        Entrypoint   = Get("entrypoint", "init.lua"),
                        Dependencies = GetStrArray("dependencies"),
                        Dir          = dir,
                    });
                }
                catch
                {
                    // Malformed manifest: surface the folder so the user can see/disable
                    // it rather than silently dropping it.
                    result.Add(new ModInfo
                    {
                        Id = folderName, Name = folderName, Author = "Unknown",
                        Description = "(mod.json could not be parsed)", Dir = dir,
                    });
                }
            }
            return result;
        }

        // A mod is enabled unless the user explicitly turned it off. A mod absent
        // from the map is on by default, so newly-added mods light up automatically.
        public static bool IsEnabled(IReadOnlyDictionary<string, bool> states, string id)
            => states == null || !states.TryGetValue(id, out var on) || on;

        // For an ENABLED mod, the declared dependencies that are not satisfied -- a
        // dep is satisfied only when it is a present mod AND currently enabled. Each
        // returned string is the dep id with a reason suffix ("foo (disabled)" /
        // "foo (missing)"). A disabled mod is not active, so it never reports unmet
        // deps (returns empty). `catalog` is the set of mods currently present.
        public static List<string> UnmetDependencies(
            IReadOnlyDictionary<string, bool> states,
            IEnumerable<ModInfo> catalog,
            ModInfo mod)
        {
            var unmet = new List<string>();
            if (mod == null || !IsEnabled(states, mod.Id)) return unmet;
            var known = new HashSet<string>(
                catalog.Select(m => m.Id), StringComparer.OrdinalIgnoreCase);
            foreach (var dep in mod.Dependencies)
            {
                if (!known.Contains(dep)) unmet.Add(dep + " (missing)");
                else if (!IsEnabled(states, dep)) unmet.Add(dep + " (disabled)");
            }
            return unmet;
        }
    }
}
