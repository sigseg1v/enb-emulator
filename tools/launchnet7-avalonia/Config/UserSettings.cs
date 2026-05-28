using System;
using System.IO;
using System.Text.Json;

namespace LaunchNet7Avalonia.Config
{
    // Cross-platform replacement for the original
    // Properties.Settings.Default (which depends on
    // System.Configuration.ApplicationSettingsBase, a .NET Framework
    // pattern). Persists to JSON next to the executable.
    public sealed class UserSettings
    {
        public string ClientPath { get; set; } = "";
        public string LastEmulatorName { get; set; } = "";
        public string LastServerName { get; set; } = "";
        public string AuthenticationPort { get; set; } = "";
        public int FormMainPositionX { get; set; } = -1;
        public int FormMainPositionY { get; set; } = -1;

        static readonly string SettingsPath =
            Path.Combine(AppContext.BaseDirectory, "LaunchNet7.settings.json");

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
