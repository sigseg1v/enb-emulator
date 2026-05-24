// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System;
using System.IO;
using System.Text.Json;

namespace SectorEditorAvalonia.Utilities
{
    // Replaces WinForms Properties.Settings.Default. The original tool
    // persisted KeepVisualizationSetting (bool) and zoomSelection (int)
    // through .settings infrastructure; we keep the same two keys but
    // serialise to a small JSON file beside the binary so the Avalonia
    // port works the same on Linux as the WinForms tool did on Windows.
    public static class EditorSettings
    {
        public static bool KeepVisualization { get; set; } = false;
        public static int ZoomSelection { get; set; } = 0;

        private static string PathName =>
            Path.Combine(AppContext.BaseDirectory, "sector-editor-settings.json");

        static EditorSettings() => Load();

        public static void Load()
        {
            try
            {
                if (!File.Exists(PathName)) return;
                using var fs = File.OpenRead(PathName);
                var doc = JsonDocument.Parse(fs);
                if (doc.RootElement.TryGetProperty("keepVisualization", out var v1))
                    KeepVisualization = v1.GetBoolean();
                if (doc.RootElement.TryGetProperty("zoomSelection", out var v2))
                    ZoomSelection = v2.GetInt32();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("[EditorSettings] load failed: " + ex.Message);
            }
        }

        public static void Save()
        {
            try
            {
                using var fs = File.Create(PathName);
                using var w = new Utf8JsonWriter(fs, new JsonWriterOptions { Indented = true });
                w.WriteStartObject();
                w.WriteBoolean("keepVisualization", KeepVisualization);
                w.WriteNumber("zoomSelection", ZoomSelection);
                w.WriteEndObject();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("[EditorSettings] save failed: " + ex.Message);
            }
        }
    }
}
