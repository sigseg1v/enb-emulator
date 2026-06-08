using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace LaunchFreya.Update
{
    // Wire contract for the login server's /updateCheck endpoint (Phase AN).
    // This is NOT the EnB game protocol -- it is a plain HTTPS/JSON side channel
    // only the launcher speaks. The real client never calls it.

    // Request body the launcher POSTs: the two SHA-512 hex hashes it computed of
    // its own FreyaLauncher.exe and the FreyaProxy.exe it ships. The cfg has no
    // hash here on purpose -- it is bound to the launcher and ships whenever the
    // launcher EXE mismatches (owner decision 2026-06-07).
    public sealed class UpdateCheckRequest
    {
        [JsonPropertyName("launcherHash")] public string LauncherHash { get; set; }
        [JsonPropertyName("proxyHash")]    public string ProxyHash    { get; set; }
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

    public sealed class UpdateCheckResponse
    {
        // "UP_TO_DATE" or "UPDATE_NEEDED".
        [JsonPropertyName("status")] public string Status { get; set; }
        [JsonPropertyName("files")]  public List<UpdateFile> Files { get; set; }
    }

    public static class UpdateStatus
    {
        public const string UpToDate     = "UP_TO_DATE";
        public const string UpdateNeeded = "UPDATE_NEEDED";
    }
}
