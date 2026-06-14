using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace LaunchFreya.Update
{
    // Thrown for any recoverable update failure (bad path, hash mismatch,
    // download/transport error). The launcher surfaces the message and does NOT
    // apply a partial update -- it leaves the install untouched and lets the
    // next launch try again (the documented push/update race is absorbed here).
    public sealed class UpdateException : Exception
    {
        public UpdateException(string message, Exception inner = null) : base(message, inner) { }
    }

    // Orchestrates the launcher self-update: compute local hashes, ask the login
    // server, download to a staging dir, verify every hash, then atomically
    // replace -- including the running launcher (Windows rename-self) -- and
    // relaunch. Network + filesystem live here; the pure helpers are in
    // UpdateLogic so they stay unit-testable.
    public sealed class Updater
    {
        public const string LauncherFileName = "FreyaLauncher.exe";
        public const string ProxyRelativePath = "bin/FreyaProxy.exe";
        public const string PosFeedRelativePath = "bin/FreyaPosFeed.dll";
        public const string InjectRelativePath = "bin/FreyaInject.exe";
        public const string EnbmodRelativePath = "bin/enbmod.dll";
        public const string StagingDirName = "updates";
        public const string BackupSuffix = ".old";
        // The persistent mod store + per-mod version marker. See MOD-STRUCTURE.md.
        public const string ModsDirName = "mods";
        public const string ModHashFileName = "modhash";

        readonly HttpClient _http;
        readonly string _baseDir;
        readonly string _selfExePath;
        readonly Action<string> _log;

        public Updater(string baseDir, string selfExePath, Action<string> log, HttpClient http = null)
        {
            _baseDir = baseDir ?? throw new ArgumentNullException(nameof(baseDir));
            _selfExePath = selfExePath;
            _log = log ?? (_ => { });
            _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        }

        // True if the running launcher's self-exe was replaced and the caller
        // should relaunch + exit.
        public bool SelfReplaced { get; private set; }

        // Hash the local binaries the server decides on. A missing file yields
        // null -> the request sends an empty hash -> the server reports a
        // mismatch and ships that file, which is the correct recovery for a
        // partially-broken install. The MVAS pair is hashed independently (each
        // ships on its own mismatch), exactly like the proxy.
        public (string launcherHash, string proxyHash, string posFeedHash, string injectHash, string enbmodHash) ComputeLocalHashes()
        {
            string launcher = HashOrNull(_selfExePath ?? Path.Combine(_baseDir, LauncherFileName));
            string proxy = HashOrNull(Path.Combine(_baseDir, "bin", "FreyaProxy.exe"));
            string posFeed = HashOrNull(Path.Combine(_baseDir, "bin", "FreyaPosFeed.dll"));
            string inject = HashOrNull(Path.Combine(_baseDir, "bin", "FreyaInject.exe"));
            string enbmod = HashOrNull(Path.Combine(_baseDir, "bin", "enbmod.dll"));
            return (launcher, proxy, posFeed, inject, enbmod);
        }

        string HashOrNull(string path)
        {
            try
            {
                if (path != null && File.Exists(path)) return UpdateLogic.ComputeSha512(path);
            }
            catch (Exception ex)
            {
                _log($"update: could not hash {path}: {ex.Message}");
            }
            return null;
        }

        // POST the local hashes to /updateCheck. Returns the parsed reply, or
        // null on any transport/parse failure (caller treats null as
        // "server not ready" -> fail-closed, Play stays disabled).
        public async Task<UpdateCheckResponse> CheckAsync(string updateCheckUrl, CancellationToken ct = default)
        {
            var (launcher, proxy, posFeed, inject, enbmod) = ComputeLocalHashes();
            string body = UpdateLogic.BuildRequestJson(launcher ?? "", proxy ?? "", posFeed ?? "", inject ?? "", enbmod ?? "");
            _log($"checkUpdates: POST {updateCheckUrl}");
            _log($"checkUpdates: local hashes  launcher={ShortHash(launcher)}  proxy={ShortHash(proxy)}" +
                 $"  posFeed={ShortHash(posFeed)}  inject={ShortHash(inject)}  enbmod={ShortHash(enbmod)}");
            try
            {
                using var content = new StringContent(body, Encoding.UTF8, "application/json");
                using var resp = await _http.PostAsync(updateCheckUrl, content, ct).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode)
                {
                    _log($"checkUpdates: /updateCheck returned HTTP {(int)resp.StatusCode}");
                    return null;
                }
                string json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                var parsed = UpdateLogic.ParseResponse(json);
                if (parsed == null) { _log("checkUpdates: could not parse /updateCheck response"); return null; }
                int n = parsed.Files?.Count ?? 0;
                _log($"checkUpdates: server says status='{parsed.Status}'" +
                     (n > 0 ? $" ({n} file(s) to update)" : " (nothing to update)"));
                return parsed;
            }
            catch (Exception ex)
            {
                _log($"checkUpdates: /updateCheck request failed: {ex.Message}");
                return null;
            }
        }

        // First 12 hex chars of a SHA-512 (or "(none)" for a missing/unhashable
        // file) -- enough to eyeball local-vs-server agreement in the log without
        // dumping the full 128-char digest.
        static string ShortHash(string hash)
            => string.IsNullOrEmpty(hash) ? "(none)" : hash.Substring(0, Math.Min(12, hash.Length));

        // Download every file in the response to the staging dir, verify all
        // hashes, then apply. Throws UpdateException on the FIRST problem and
        // leaves the install untouched (nothing is applied until every download
        // is present and verified).
        public async Task DownloadAndApplyAsync(UpdateCheckResponse resp, Action<string> progress = null,
            CancellationToken ct = default)
        {
            progress ??= _log;
            if (resp?.Files == null || resp.Files.Count == 0)
                throw new UpdateException("Update response listed no files.");

            // 1. Resolve + path-guard every destination BEFORE touching disk.
            var planned = new (UpdateFile file, string dest, string staged)[resp.Files.Count];
            string staging = Path.Combine(_baseDir, StagingDirName);
            for (int i = 0; i < resp.Files.Count; i++)
            {
                var f = resp.Files[i];
                if (!UpdateLogic.TryResolveWithinBase(_baseDir, f.RelativePath, out string dest))
                    throw new UpdateException(
                        $"Refusing update: '{f.RelativePath}' resolves outside the install directory.");
                string staged = Path.Combine(staging, Path.GetFileName(dest));
                planned[i] = (f, dest, staged);
            }

            // 2. Fresh staging dir.
            try
            {
                if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
                Directory.CreateDirectory(staging);
            }
            catch (Exception ex)
            {
                throw new UpdateException($"Could not prepare staging directory: {ex.Message}", ex);
            }

            // 3. Download all.
            foreach (var (file, _, staged) in planned)
            {
                progress($"Downloading {Path.GetFileName(staged)}...");
                await DownloadFileAsync(file.Url, staged, ct).ConfigureAwait(false);
            }

            // 4. Verify all hashes before applying anything.
            foreach (var (file, _, staged) in planned)
            {
                string actual = UpdateLogic.ComputeSha512(staged);
                bool ok = UpdateLogic.HashesEqual(actual, file.Hash);
                _log($"verify {Path.GetFileName(staged)}: expected={ShortHash(file.Hash)} " +
                     $"got={ShortHash(actual)} -> {(ok ? "OK" : "MISMATCH")}");
                if (!ok)
                    throw new UpdateException(
                        $"Hash mismatch for {Path.GetFileName(staged)} -- update aborted, nothing changed.");
            }
            _log($"verify: all {planned.Length} file(s) validated; applying.");

            // 5. Apply. Non-self files first; the running launcher last (so a
            // failure before the self-swap never leaves a half-launched state).
            (UpdateFile file, string dest, string staged) self = default;
            bool haveSelf = false;
            foreach (var entry in planned)
            {
                if (IsSelf(entry.dest)) { self = entry; haveSelf = true; continue; }
                ApplyNonSelf(entry.staged, entry.dest);
                progress($"Updated {entry.file.RelativePath}");
            }

            if (haveSelf)
            {
                ApplySelf(self.staged, self.dest);
                SelfReplaced = true;
                progress($"Updated {self.file.RelativePath}");
            }
        }

        // Reconcile OUR enbmod Lua mods against the server's published set. For
        // each {id, hash, url} the launcher compares the hash to the local
        // <baseDir>/mods/<id>/modhash and, on a mismatch (or a missing marker),
        // downloads the zip and REPLACES the folder: wipe it, extract, then write
        // modhash LAST so the marker exists only when the extract fully succeeded
        // (a partial extract never reads as "up to date" next time).
        //
        // This is best-effort and per-mod isolated: one mod's download/extract
        // failure is logged and skipped, never aborting the others, and never
        // throwing -- a mod problem must not block Play or the binary update path.
        // After updating, prune store folders for mods we no longer publish (a
        // renamed/removed mod): a folder that carries a modhash marker but is
        // absent from the response is one of ours that went away. An id with NO
        // marker is the user's own mod and is never touched -- the safety
        // guarantee (see MOD-STRUCTURE.md).
        //
        // Returns the number of mods actually updated.
        public async Task<int> ReconcileModsAsync(UpdateCheckResponse resp,
            Action<string> progress = null, CancellationToken ct = default)
        {
            progress ??= _log;
            if (resp?.Mods == null || resp.Mods.Count == 0) return 0;

            string store = Path.Combine(_baseDir, ModsDirName);
            string staging = Path.Combine(_baseDir, StagingDirName);
            int updated = 0;

            foreach (var mod in resp.Mods)
            {
                if (mod == null) continue;
                if (!UpdateLogic.IsSafeModId(mod.Id))
                {
                    _log($"mods: skipping unsafe mod id '{mod.Id}'");
                    continue;
                }
                if (string.IsNullOrEmpty(mod.Hash) || string.IsNullOrEmpty(mod.Url))
                {
                    _log($"mods: '{mod.Id}' has no hash/url; skipping");
                    continue;
                }

                string modDir = Path.Combine(store, mod.Id);
                string hashFile = Path.Combine(modDir, ModHashFileName);
                string local = null;
                try { if (File.Exists(hashFile)) local = File.ReadAllText(hashFile).Trim(); }
                catch (Exception ex) { _log($"mods: could not read {hashFile}: {ex.Message}"); }

                if (UpdateLogic.HashesEqual(local, mod.Hash))
                    continue;   // already at this version

                string zip = Path.Combine(staging, $"{mod.Id}-{mod.Hash}.zip");
                try
                {
                    Directory.CreateDirectory(staging);
                    progress($"Downloading mod {mod.Id}...");
                    await DownloadFileAsync(mod.Url, zip, ct).ConfigureAwait(false);

                    // Replace the folder: wipe -> extract -> write marker last.
                    if (Directory.Exists(modDir)) Directory.Delete(modDir, recursive: true);
                    Directory.CreateDirectory(modDir);
                    // ZipFile guards against entries resolving outside modDir.
                    ZipFile.ExtractToDirectory(zip, modDir, overwriteFiles: true);
                    File.WriteAllText(hashFile, mod.Hash.Trim());

                    updated++;
                    progress($"Updated mod {mod.Id} ({mod.Hash})");
                }
                catch (Exception ex)
                {
                    _log($"mods: failed to update '{mod.Id}': {ex.Message}");
                    // Leave whatever was there; a half-wiped dir simply has no
                    // modhash, so the next check retries this mod.
                }
                finally
                {
                    try { if (File.Exists(zip)) File.Delete(zip); } catch { /* best-effort */ }
                }
            }

            // Prune OUR mods that the server no longer publishes (a removed or
            // renamed mod). We can tell ours from a user's own mod by the modhash
            // marker: the updater writes it on every mod we install, and a user's
            // mod never has one. So a store folder that carries a modhash marker
            // but is absent from this response is one of ours that went away ->
            // delete it. A folder WITHOUT the marker is the user's and is left
            // untouched (the safety guarantee in MOD-STRUCTURE.md).
            try
            {
                var published = new HashSet<string>(
                    resp.Mods.Where(m => m != null).Select(m => m.Id),
                    StringComparer.OrdinalIgnoreCase);
                if (Directory.Exists(store))
                {
                    foreach (var dir in Directory.GetDirectories(store))
                    {
                        var id = Path.GetFileName(dir);
                        if (published.Contains(id)) continue;
                        if (!File.Exists(Path.Combine(dir, ModHashFileName))) continue; // user's mod
                        try
                        {
                            Directory.Delete(dir, recursive: true);
                            _log($"mods: removed '{id}' (no longer published).");
                        }
                        catch (Exception ex)
                        {
                            _log($"mods: could not remove stale mod '{id}': {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex) { _log($"mods: prune of removed mods failed: {ex.Message}"); }

            if (updated > 0) _log($"mods: updated {updated} mod(s).");
            return updated;
        }

        async Task DownloadFileAsync(string url, string destPath, CancellationToken ct)
        {
            try
            {
                using var resp = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct)
                    .ConfigureAwait(false);
                resp.EnsureSuccessStatusCode();
                Directory.CreateDirectory(Path.GetDirectoryName(destPath));
                await using var fs = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None);
                await resp.Content.CopyToAsync(fs, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not UpdateException)
            {
                throw new UpdateException($"Download failed for {url}: {ex.Message}", ex);
            }
        }

        void ApplyNonSelf(string staged, string dest)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(dest));
                File.Move(staged, dest, overwrite: true);
            }
            catch (Exception ex)
            {
                throw new UpdateException($"Could not replace {dest}: {ex.Message}", ex);
            }
        }

        // Windows can rename (but not overwrite/delete) a running image. Rename
        // the live launcher aside to *.old, move the new one into place, and let
        // the freshly-started instance delete the *.old on its next launch.
        void ApplySelf(string staged, string dest)
        {
            try
            {
                string backup = dest + BackupSuffix;
                if (File.Exists(backup)) File.Delete(backup);
                if (File.Exists(dest)) File.Move(dest, backup);
                File.Move(staged, dest);
            }
            catch (Exception ex)
            {
                throw new UpdateException($"Could not replace the launcher: {ex.Message}", ex);
            }
        }

        bool IsSelf(string resolvedDest)
        {
            if (string.IsNullOrEmpty(_selfExePath)) return false;
            var cmp = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            return string.Equals(
                Path.GetFullPath(resolvedDest), Path.GetFullPath(_selfExePath), cmp);
        }

        // Remove leftovers from a previous self-replace: the *.old backup next to
        // the launcher and a stale staging dir. Best-effort; never throws.
        public void CleanupStaleArtifacts()
        {
            try
            {
                string backup = (_selfExePath ?? Path.Combine(_baseDir, LauncherFileName)) + BackupSuffix;
                if (File.Exists(backup)) File.Delete(backup);
            }
            catch (Exception ex) { _log($"update: could not remove stale backup: {ex.Message}"); }
            try
            {
                string staging = Path.Combine(_baseDir, StagingDirName);
                if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
            }
            catch (Exception ex) { _log($"update: could not remove stale staging dir: {ex.Message}"); }
        }
    }
}
