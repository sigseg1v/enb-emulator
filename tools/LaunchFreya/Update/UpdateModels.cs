using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace LaunchFreya.Update
{
    // Wire contract for the login server's /updateCheck endpoint (Phase AN).
    // This is NOT the EnB game protocol -- it is a plain HTTPS/JSON side channel
    // only the launcher speaks. The real client never calls it.

    // Request body the launcher POSTs: the SHA-512 hex hashes it computed of its
    // own FreyaLauncher.exe, the FreyaProxy.exe it ships, and the MVAS injection
    // pair (FreyaPosFeed.dll + FreyaInject.exe). Each independently-hashed binary
    // is shipped on its own mismatch, exactly like the proxy -- so an
    // MVAS-feed-only patch reaches existing installs without a launcher bump.
    // The cfg has no hash here on purpose -- it is bound to the launcher and
    // ships whenever the launcher EXE mismatches (owner decision 2026-06-07).
    public sealed class UpdateCheckRequest
    {
        [JsonPropertyName("launcherHash")] public string LauncherHash { get; set; }
        [JsonPropertyName("proxyHash")]    public string ProxyHash    { get; set; }
        [JsonPropertyName("posFeedHash")]  public string PosFeedHash  { get; set; }
        [JsonPropertyName("injectHash")]   public string InjectHash   { get; set; }
        // enbmod.dll -- the optional Lua mod runtime. Hashed and shipped
        // INDEPENDENTLY, exactly like the MVAS pair: it rides with the launcher
        // set but a runtime-only patch reaches existing installs on its own
        // mismatch. The Lua SCRIPTS are deliberately NOT hashed here -- they are
        // user-editable mod content (init.lua hot-reloads) and the self-updater
        // would clobber local edits; they ship as a one-time seed in the zip.
        [JsonPropertyName("enbmodHash")]   public string EnbmodHash   { get; set; }
        // GalaxyMap.dat -- the proxy's cached galaxy-map node data. Hashed and
        // shipped INDEPENDENTLY, exactly like the MVAS pair and enbmod runtime:
        // the WINE proxy serves it to the client for the in-game galaxy map and
        // has no docker mount to source it from, so it must ride to existing
        // installs on its own mismatch.
        [JsonPropertyName("galaxyMapHash")] public string GalaxyMapHash { get; set; }
    }

    public sealed class UpdateFile
    {
        // Install-relative destination (e.g. "FreyaLauncher.exe",
        // "FreyaLauncher.cfg", "bin/FreyaProxy.exe"). MUST resolve within the
        // launcher install dir or below -- enforced by the path-traversal guard
        // before anything is written.
        [JsonPropertyName("relativePath")] public string RelativePath { get; set; }
        [JsonPropertyName("url")]          public string Url          { get; set; }
        [JsonPropertyName("hash")]         public string Hash         { get; set; }
    }

    // One published enbmod Lua mod the server vouches for. The launcher compares
    // Hash to the local ./mods/<Id>/modhash and downloads Url (a <id>-<hash>.zip)
    // only on a mismatch. An Id never in this list is a user's own mod and is
    // never touched. See freya/client-injection/enbmod/MOD-STRUCTURE.md.
    public sealed class ModUpdate
    {
        [JsonPropertyName("id")]   public string Id   { get; set; }
        [JsonPropertyName("hash")] public string Hash { get; set; }
        [JsonPropertyName("url")]  public string Url  { get; set; }
    }

    // One operator-supplied game-data patch the server vouches for. Unlike a
    // file/mod, a patch is an EXECUTABLE the launcher runs against the EnB game
    // install (e.g. enb-patch.exe). The launcher records applied patches in a
    // patchlevel.txt at the EnB install root (one hash per line) and only runs a
    // patch whose hash is absent. Name is a single safe path segment; it is both
    // the URL segment and the staged file name. See deploy/do/README.md
    // "Operator-provided client patch (enb-patch.exe)".
    public sealed class PatchUpdate
    {
        [JsonPropertyName("name")] public string Name { get; set; }
        [JsonPropertyName("hash")] public string Hash { get; set; }
        [JsonPropertyName("url")]  public string Url  { get; set; }
    }

    public sealed class UpdateCheckResponse
    {
        // "UP_TO_DATE" or "UPDATE_NEEDED".
        [JsonPropertyName("status")] public string Status { get; set; }
        [JsonPropertyName("files")]  public List<UpdateFile> Files { get; set; }
        // The authoritative set of OUR Lua mods (id + hash + zip url). Orthogonal
        // to Status: present in both replies, since mod updates do not gate Play.
        [JsonPropertyName("mods")]   public List<ModUpdate> Mods { get; set; }
        // The operator-supplied game-data patches (name + hash + exe url). Like
        // mods, orthogonal to Status -- present in both replies and applied
        // against the EnB game install, not the launcher dir. See PatchUpdate.
        [JsonPropertyName("patches")] public List<PatchUpdate> Patches { get; set; }
    }

    public static class UpdateStatus
    {
        public const string UpToDate     = "UP_TO_DATE";
        public const string UpdateNeeded = "UPDATE_NEEDED";
    }
}
