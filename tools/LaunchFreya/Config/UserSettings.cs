using System;
using System.IO;
using System.Text.Json;

namespace LaunchFreya.Config
{
    // Cross-platform replacement for the original
    // Properties.Settings.Default (which depends on
    // System.Configuration.ApplicationSettingsBase, a .NET Framework
    // pattern). Persists to JSON next to the executable.
    public sealed class UserSettings
    {
        public string ClientPath { get; set; } = "";
        // PB-2: inject the in-client MVAS position-feed DLL (Net7PosFeed.dll) into
        // client.exe so thruster flight stays in sync with the server. Off by
        // default; the feed itself is inert until the owner fills its engine read
        // (client/detours/ClientEngineOffsets.local.h). This is a NEW mechanism --
        // not the historical ClientDetours debug DLL -- hence its own name.
        public bool UsePositionFeed { get; set; } = false;
        public string LastEmulatorName { get; set; } = "";
        public string LastServerName { get; set; } = "";
        public string AuthenticationPort { get; set; } = "";
        public int FormMainPositionX { get; set; } = -1;
        public int FormMainPositionY { get; set; } = -1;

        static readonly string SettingsPath =
            Path.Combine(AppContext.BaseDirectory, "FreyaLauncher.settings.json");

        public static UserSettings Load()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    var json = File.ReadAllText(SettingsPath);
                    return JsonSerializer.Deserialize<UserSettings>(json) ?? new UserSettings();
                }
            }
            catch { /* corrupted settings → start fresh */ }
            return new UserSettings();
        }

        public void Save()
        {
            try
            {
                var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SettingsPath, json);
            }
            catch { /* best-effort */ }
        }
    }
}
