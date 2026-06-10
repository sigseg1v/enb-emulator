using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LaunchFreya.Update
{
    // Pure / IO-light helpers for the launcher self-updater (Phase AN). Kept
    // free of UI and network so they can be unit-tested in isolation
    // (LaunchFreya.Tests). The orchestration that does the actual HTTP +
    // file replacement lives in Updater.cs.
    public static class UpdateLogic
    {
        // Lowercase hex SHA-512 of a file's bytes. This is the exact form both
        // sides compare and the manifest publishes.
        public static string ComputeSha512(string filePath)
        {
            using var stream = File.OpenRead(filePath);
            using var sha = SHA512.Create();
            byte[] hash = sha.ComputeHash(stream);
            return ToHex(hash);
        }

        public static string ToHex(byte[] bytes)
        {
            var sb = new StringBuilder(bytes.Length * 2);
            foreach (byte b in bytes) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }

        public static bool HashesEqual(string a, string b)
            => !string.IsNullOrEmpty(a) && !string.IsNullOrEmpty(b)
               && string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

        // Path-traversal guard (SECURITY -- the highest-risk item in this phase).
        // A relativePath from the /updateCheck response must resolve to a file
        // INSIDE baseDir (the launcher install dir) or a subdirectory of it --
        // never a parent, never an absolute/rooted path, never a drive change.
        // Returns false (and leaves resolved null) on any violation; the caller
        // aborts the WHOLE update on a single bad entry.
        public static bool TryResolveWithinBase(string baseDir, string relativePath, out string resolved)
        {
            resolved = null;
            if (string.IsNullOrWhiteSpace(baseDir) || string.IsNullOrWhiteSpace(relativePath))
                return false;

            // Reject anything rooted: absolute POSIX paths, Windows drive paths
            // (C:\...), and UNC paths. Path.IsPathRooted catches "/x", "C:\x",
            // "\\server\share"; also reject a bare "C:foo" (rooted-but-relative).
            if (Path.IsPathRooted(relativePath)) return false;
            if (relativePath.Length >= 2 && relativePath[1] == ':') return false;

            string baseFull = Path.GetFullPath(baseDir);
            // Normalize the base to end with a single separator so the prefix
            // check below cannot match a sibling dir that merely shares a prefix
            // (e.g. base "/app" must not accept "/app-evil/x").
            string baseWithSep = baseFull.EndsWith(Path.DirectorySeparatorChar)
                ? baseFull
                : baseFull + Path.DirectorySeparatorChar;

            string combined;
            try
            {
                combined = Path.GetFullPath(Path.Combine(baseFull, relativePath));
            }
            catch
            {
                return false; // malformed path characters etc.
            }

            // The resolved file must live strictly under the base dir. (It must
            // not BE the base dir itself, and not escape via "..".)
            if (!combined.StartsWith(baseWithSep, StringComparison.Ordinal))
                return false;

            resolved = combined;
            return true;
        }

        public static string BuildRequestJson(string launcherHash, string proxyHash,
            string posFeedHash, string injectHash, string enbmodHash)
        {
            var req = new UpdateCheckRequest
            {
                LauncherHash = launcherHash,
                ProxyHash    = proxyHash,
                PosFeedHash  = posFeedHash,
                InjectHash   = injectHash,
                EnbmodHash   = enbmodHash,
            };
            return JsonSerializer.Serialize(req);
        }

        // Tolerant parse of the /updateCheck reply. Throws nothing on a
        // structurally-valid-but-unexpected body; returns null only when the
        // JSON itself is unparseable, so the caller can treat that as "could not
        // determine status" (fail-closed -> server treated as not-ready).
        public static UpdateCheckResponse ParseResponse(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try
            {
                return JsonSerializer.Deserialize<UpdateCheckResponse>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }
}
