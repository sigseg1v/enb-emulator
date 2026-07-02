using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LaunchFreya.Config
{
    // Cross-platform replacement for the original
    // Properties.Settings.Default (which depends on
    // System.Configuration.ApplicationSettingsBase, a .NET Framework
    // pattern). Persists to JSON next to the executable.
    //
    // Profiles (BA-3): the launcher keeps a set of named profiles, each its own
    // JSON file in the settings dir. The "Default" profile IS the baseline file
    // (FreyaLauncher.settings.json) -- it is special and cannot be deleted, and
    // it is NOT renamed to a "Default" file. Every other profile is a sibling
    // FreyaLauncher.profile.<sanitized>.settings.json. The launcher-global keys
    // (window position, schema version, and the active-profile pointer) live in
    // the baseline file and are authoritative there regardless of which profile
    // is active; the per-profile keys are whatever the active profile's file
    // holds.
    public sealed class UserSettings
    {
        // ---- per-profile keys (saved into the active profile's file) ----
        public string ClientPath { get; set; } = "";
        // PB-2: inject the in-client MVAS position-feed DLL (FreyaPosFeed.dll) into
        // client.exe so thruster flight stays in sync with the server. ON by
        // default so the shipped Windows package (which writes no settings.json)
        // injects the feed -- without it MVAS movement does not work online. The
        // injection is harmless when the artifacts are absent (a checkout that did
        // not build the DLL just logs a warning and launches plain). The engine
        // read is in freya/client-injection/ClientEngineOffsets.h, compiled into
        // the DLL. This is a NEW mechanism -- not the historical ClientDetours
        // debug DLL -- hence its own name.
        public bool UsePositionFeed { get; set; } = true;
        // Inject the enbmod Lua mod runtime (enbmod.dll) into client.exe, enabling
        // client-side UI mods written in Lua without patching the game. OFF by
        // default: experimental and unverified against the live client, so it ships
        // dark and is opt-in via the "Enable Lua Mods" checkbox (or this key in
        // FreyaLauncher.settings.json). When off, enbmod.dll is neither staged nor injected. A new
        // field absent from an older settings file reads as false, which is the
        // intended default, so no migration is needed.
        public bool UseClientMods { get; set; } = false;
        // Per-mod enable/disable state for the enbmod Lua mods, keyed by mod id
        // (the scripts/mods/<id> folder name). A mod absent from this map is
        // treated as ENABLED -- so a freshly added mod is on by default and the
        // map only needs to record explicit user opt-outs. The "Configure Mods"
        // window writes this; StageClientMods stages only the enabled mods, so a
        // disabled mod is neither copied next to the client nor loaded by init.lua.
        public Dictionary<string, bool> ModStates { get; set; } = new();
        public string LastEmulatorName { get; set; } = "";
        public string LastServerName { get; set; } = "";
        public string AuthenticationPort { get; set; } = "";

        // BA-4: client display settings applied to the EnB registry before launch.
        // Resolution is "WxH" (e.g. "1280x960"); empty means "leave the registry
        // as-is". Fullscreen=false maps to RenderDeviceWindowed=1.
        public string Resolution { get; set; } = "";
        public bool Fullscreen { get; set; } = false;

        // Whether the mouse cursor stays confined to the client window while the
        // game holds focus. The game itself confines the pointer (ClipCursor), so
        // ON is the native behaviour and the default; OFF injects enbmod's
        // ClipCursor hook (FREYA_LOCK_MOUSE=0) to free the pointer -- handy for
        // multiboxing so it can reach the other client's window. Persisted
        // per-profile like the other display settings.
        public bool LockMouseToWindow { get; set; } = true;

        // BA-5: optional auto-login credentials. AccountName + CharacterName are
        // persisted; the password is NEVER written to disk (it is held only in the
        // text box for the session and passed to the launch env).
        public string AccountName { get; set; } = "";
        public string CharacterName { get; set; } = "";

        // Optional client-window placement. When both are set the launcher waits for
        // the client window to appear after launch and moves it to (WindowX, WindowY);
        // when either is null the client window is left wherever the game opens it (the
        // stock behaviour). Per-profile so each multibox profile can pin its own screen
        // slot. Nullable (not a -1 sentinel) because a negative coordinate is a valid
        // position on a monitor left of the primary.
        public int? WindowX { get; set; }
        public int? WindowY { get; set; }

        // Display name of THIS profile. The baseline file carries "Default".
        public string Name { get; set; } = DefaultProfileName;

        // ---- launcher-global keys (authoritative in the baseline file) ----
        public int FormMainPositionX { get; set; } = -1;
        public int FormMainPositionY { get; set; } = -1;
        // Name of the profile to load on startup. Only meaningful in the baseline
        // file; ignored in per-profile files.
        public string ActiveProfile { get; set; } = DefaultProfileName;

        // Settings-file schema version, for one-shot migrations on load. MUST
        // default to 0: a field absent from an older JSON file falls back to this
        // initializer, so a pre-migration file reads as version 0 and triggers the
        // migration below. A fresh install (no file) also starts at 0 and is
        // migrated to the current version on first load. Bump CurrentSettingsVersion
        // and add a case when a new migration is needed.
        public int SettingsVersion { get; set; } = 0;
        public const int CurrentSettingsVersion = 1;

        public const string DefaultProfileName = "Default";

        static readonly string SettingsDir = AppContext.BaseDirectory;
        static readonly string BaselinePath =
            Path.Combine(SettingsDir, "FreyaLauncher.settings.json");
        const string ProfilePrefix = "FreyaLauncher.profile.";
        const string ProfileSuffix = ".settings.json";

        [JsonIgnore]
        public bool IsDefaultProfile =>
            string.Equals(Name, DefaultProfileName, StringComparison.OrdinalIgnoreCase);

        // Map a profile display name to its file. Default -> the baseline file;
        // any other name -> a sanitized profile sibling.
        public static string PathFor(string profileName)
        {
            if (string.IsNullOrWhiteSpace(profileName) ||
                string.Equals(profileName, DefaultProfileName, StringComparison.OrdinalIgnoreCase))
                return BaselinePath;
            return Path.Combine(SettingsDir, ProfilePrefix + Sanitize(profileName) + ProfileSuffix);
        }

        static string Sanitize(string name)
        {
            var sb = new StringBuilder();
            foreach (char c in name.Trim())
                sb.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '_');
            var s = sb.ToString().Trim('_');
            return string.IsNullOrEmpty(s) ? "profile" : s;
        }

        // Display names of all profiles: "Default" first, then every named profile
        // file found in the settings dir (by the Name stored inside each file).
        public static List<string> ListProfiles()
        {
            var names = new List<string> { DefaultProfileName };
            try
            {
                foreach (var f in Directory.EnumerateFiles(SettingsDir, ProfilePrefix + "*" + ProfileSuffix))
                {
                    var ps = ReadFile(f);
                    var nm = ps?.Name;
                    if (!string.IsNullOrWhiteSpace(nm) &&
                        !names.Contains(nm, StringComparer.OrdinalIgnoreCase))
                        names.Add(nm);
                }
            }
            catch { /* best-effort listing */ }
            return names;
        }

        static UserSettings ReadFile(string path)
        {
            try
            {
                if (File.Exists(path))
                    return JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(path));
            }
            catch { /* corrupted -> null */ }
            return null;
        }

        // Load the active profile (per the baseline pointer), folding the
        // launcher-global keys in from the baseline file.
        public static UserSettings Load()
        {
            var baseline = ReadFile(BaselinePath) ?? new UserSettings();
            string active = baseline.ActiveProfile;
            return LoadProfile(active, baseline);
        }

        // Load a specific profile by display name. Globals (window pos, version,
        // active pointer) always come from the baseline file so they stay coherent
        // no matter which profile's file is the body.
        public static UserSettings LoadProfile(string displayName, UserSettings baseline = null)
        {
            baseline ??= ReadFile(BaselinePath) ?? new UserSettings();

            UserSettings s;
            if (string.IsNullOrWhiteSpace(displayName) ||
                string.Equals(displayName, DefaultProfileName, StringComparison.OrdinalIgnoreCase))
            {
                s = baseline;
                s.Name = DefaultProfileName;
            }
            else
            {
                s = ReadFile(PathFor(displayName));
                if (s == null)
                {
                    // pointer dangles -> fall back to Default
                    s = baseline;
                    s.Name = DefaultProfileName;
                }
                else
                {
                    s.Name = displayName;
                    // globals are authoritative in baseline
                    s.FormMainPositionX = baseline.FormMainPositionX;
                    s.FormMainPositionY = baseline.FormMainPositionY;
                    s.SettingsVersion = baseline.SettingsVersion;
                }
            }
            s.ActiveProfile = s.Name;

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
            WriteFile(PathFor(Name), this);

            // Keep the baseline's launcher-global keys coherent even when a named
            // profile is active (window position + active pointer are global).
            if (!IsDefaultProfile)
            {
                var baseline = ReadFile(BaselinePath) ?? new UserSettings();
                baseline.FormMainPositionX = FormMainPositionX;
                baseline.FormMainPositionY = FormMainPositionY;
                baseline.SettingsVersion = SettingsVersion;
                baseline.ActiveProfile = Name;
                WriteFile(BaselinePath, baseline);
            }
        }

        static void WriteFile(string path, UserSettings s)
        {
            try
            {
                var json = JsonSerializer.Serialize(s, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(path, json);
            }
            catch { /* best-effort */ }
        }

        // Create a new profile by copying the current in-memory settings into a new
        // file under the new name. Returns the created (loaded) settings, or null if
        // the name is invalid/taken. The caller becomes "on" the new profile.
        public UserSettings CreateProfile(string newName)
        {
            newName = (newName ?? "").Trim();
            if (newName.Length == 0 ||
                string.Equals(newName, DefaultProfileName, StringComparison.OrdinalIgnoreCase))
                return null;
            if (ListProfiles().Contains(newName, StringComparer.OrdinalIgnoreCase))
                return null;

            // copy every per-profile field from the current settings
            var copy = (UserSettings)MemberwiseClone();
            copy.ModStates = new Dictionary<string, bool>(ModStates);
            copy.Name = newName;
            WriteFile(PathFor(newName), copy);

            // point the baseline at the new profile so it is active next load
            copy.SetActive();
            return copy;
        }

        // Delete a named profile's file. Default cannot be deleted. Returns the
        // display name to switch to afterward (always Default).
        public static string DeleteProfile(string displayName)
        {
            if (string.IsNullOrWhiteSpace(displayName) ||
                string.Equals(displayName, DefaultProfileName, StringComparison.OrdinalIgnoreCase))
                return DefaultProfileName; // never delete Default
            try
            {
                var path = PathFor(displayName);
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch { /* best-effort */ }

            var baseline = ReadFile(BaselinePath) ?? new UserSettings();
            baseline.ActiveProfile = DefaultProfileName;
            WriteFile(BaselinePath, baseline);
            return DefaultProfileName;
        }

        // Persist this profile as the active one (updates the baseline pointer).
        public void SetActive()
        {
            var baseline = IsDefaultProfile ? this : (ReadFile(BaselinePath) ?? new UserSettings());
            baseline.ActiveProfile = Name;
            WriteFile(BaselinePath, baseline);
        }
    }
}
