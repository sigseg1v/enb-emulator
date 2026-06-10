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
        // PB-2: inject the in-client MVAS position-feed DLL (FreyaPosFeed.dll) into
        // client.exe so thruster flight stays in sync with the server. ON by
        // default so the shipped Windows package (which writes no settings.json)
        // injects the feed -- without it MVAS movement does not work online. The
        // injection is harmless when the artifacts are absent (a checkout that did
        // not build the DLL just logs a warning and launches plain), and the feed
        // itself sends nothing until the engine read is provided
        // (client/detours/ClientEngineOffsets.local.h, baked into the DLL at build
        // time, never committed). This is a NEW mechanism -- not the historical
        // ClientDetours debug DLL -- hence its own name.
        public bool UsePositionFeed { get; set; } = true;
        public string LastEmulatorName { get; set; } = "";
        public string LastServerName { get; set; } = "";
        public string AuthenticationPort { get; set; } = "";
        public int FormMainPositionX { get; set; } = -1;
        public int FormMainPositionY { get; set; } = -1;

        // Settings-file schema version, for one-shot migrations on load. MUST
        // default to 0: a field absent from an older JSON file falls back to this
        // initializer, so a pre-migration file reads as version 0 and triggers the
        // migration below. A fresh install (no file) also starts at 0 and is
        // migrated to the current version on first load. Bump CurrentSettingsVersion
        // and add a case when a new migration is needed.
        public int SettingsVersion { get; set; } = 0;
        public const int CurrentSettingsVersion = 1;

        static readonly string SettingsPath =
            Path.Combine(AppContext.BaseDirectory, "FreyaLauncher.settings.json");

        public static UserSettings Load()
        {
            UserSettings s = null;
            try
            {
                if (File.Exists(SettingsPath))
                {
                    var json = File.ReadAllText(SettingsPath);
                    s = JsonSerializer.Deserialize<UserSettings>(json);
                }
            }
            catch { /* corrupted settings → start fresh */ }
            s ??= new UserSettings();

            // Migration to v1: the MVAS position feed shipped OFF by default and
            // every client that launched before this change persisted
            // UsePositionFeed=false, which leaves online MVAS movement broken. There
            // is no UI that sets it false, so a persisted false is only ever the old
            // default -- flip it on. Idempotent: once v1 is saved this never re-runs,
            // so a future explicit user toggle survives.
            if (s.SettingsVersion < 1)
            {
                s.UsePositionFeed = true;
                s.SettingsVersion = CurrentSettingsVersion;
                s.Save();
            }

            return s;
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
