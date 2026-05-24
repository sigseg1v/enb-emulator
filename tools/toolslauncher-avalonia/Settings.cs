using System;
using System.IO;
using System.Text.Json;

namespace ToolsLauncherAvalonia
{
    // Replacement for the original WinForms Properties.Settings.Default
    // mechanism. Persists to a JSON file in the per-user app-data dir;
    // this is portable across Windows/Linux/macOS unlike the WinForms
    // user.config flow.
    public sealed class Settings
    {
        public string LaunchNet7Path { get; set; } = "";
        public string EditorsCheckoutRoot { get; set; } = "";

        public static string FilePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Net7Tools",
            "toolslauncher-avalonia.json");

        public static Settings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                    return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings();
            }
            catch { /* fall through to defaults */ }
            return new Settings();
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { /* best-effort */ }
        }
    }
}
