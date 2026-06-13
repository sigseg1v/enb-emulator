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
        // Absolute path of the mod folder in the SOURCE scripts tree (used for staging).
        public string Dir { get; init; } = "";
    }

    // Discovers the available enbmod mods by reading the mod.json manifests under
    // the source scripts/mods/ tree (the same tree StageClientMods copies from).
    // Both the launcher's staging step and the "Configure Mods" window use this so
    // the catalog and what gets staged can never drift.
    public static class ModCatalog
    {
        // The mods/ subfolder of the located source scripts dir, or null if the
        // scripts tree isn't present (unbuilt checkout).
        public static string ModsDir()
        {
            var scripts = Launcher.LocateScriptsDir();
            if (string.IsNullOrEmpty(scripts)) return null;
            var mods = Path.Combine(scripts, "mods");
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
                    result.Add(new ModInfo
                    {
                        Id          = Get("id", folderName),
                        Name        = Get("name", folderName),
                        Author      = Get("author", "Unknown"),
                        Description = Get("description", ""),
                        Entrypoint  = Get("entrypoint", "init.lua"),
                        Dir         = dir,
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
    }
}
