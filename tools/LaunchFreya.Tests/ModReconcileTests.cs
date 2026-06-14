using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LaunchFreya.Update;
using Xunit;

namespace LaunchFreya.Tests
{
    // Tests the launcher-side mod update apply path (Updater.ReconcileModsAsync):
    // download a <id>-<hash>.zip, REPLACE the store folder, write the modhash
    // marker, and -- the load-bearing safety property -- NEVER touch a mod whose
    // id the server did not vouch for (a user's own mod). See MOD-STRUCTURE.md.
    public class ModReconcileTests
    {
        // Serves a fixed url->bytes map; everything else 404s.
        sealed class MapHandler : HttpMessageHandler
        {
            readonly Dictionary<string, byte[]> _map;
            public int Hits;
            public MapHandler(Dictionary<string, byte[]> map) { _map = map; }
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
            {
                if (_map.TryGetValue(req.RequestUri.ToString(), out var b))
                {
                    Hits++;
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    { Content = new ByteArrayContent(b) });
                }
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }
        }

        static byte[] MakeZip(params (string name, string content)[] files)
        {
            using var ms = new MemoryStream();
            using (var za = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
                foreach (var (name, content) in files)
                {
                    var e = za.CreateEntry(name);
                    using var s = e.Open();
                    var bytes = Encoding.UTF8.GetBytes(content);
                    s.Write(bytes, 0, bytes.Length);
                }
            return ms.ToArray();
        }

        static string FreshBase()
        {
            string dir = Path.Combine(Path.GetTempPath(), "freya-modtest-" + Path.GetRandomFileName());
            Directory.CreateDirectory(dir);
            return dir;
        }

        static (Updater updater, MapHandler handler) MakeUpdater(string baseDir, Dictionary<string, byte[]> map)
        {
            var handler = new MapHandler(map);
            var http = new HttpClient(handler);
            var updater = new Updater(baseDir, selfExePath: null, log: _ => { }, http: http);
            return (updater, handler);
        }

        [Fact]
        public async Task FreshInstall_DownloadsExtractsAndWritesModhash()
        {
            string baseDir = FreshBase();
            try
            {
                string url = "https://dl/mods/player-hud-abc1234567.zip";
                var map = new Dictionary<string, byte[]>
                {
                    [url] = MakeZip(("mod.json", "{\"id\":\"player-hud\"}"), ("freya_ui.lua", "-- ui"))
                };
                var (updater, _) = MakeUpdater(baseDir, map);
                var resp = new UpdateCheckResponse
                {
                    Status = UpdateStatus.UpToDate,
                    Mods = new List<ModUpdate>
                    {
                        new ModUpdate { Id = "player-hud", Hash = "abc1234567", Url = url }
                    }
                };

                int n = await updater.ReconcileModsAsync(resp);

                Assert.Equal(1, n);
                string modDir = Path.Combine(baseDir, "mods", "player-hud");
                Assert.True(File.Exists(Path.Combine(modDir, "mod.json")));
                Assert.True(File.Exists(Path.Combine(modDir, "freya_ui.lua")));
                Assert.Equal("abc1234567", File.ReadAllText(Path.Combine(modDir, "modhash")).Trim());
            }
            finally { Directory.Delete(baseDir, true); }
        }

        [Fact]
        public async Task SecondRunSameHash_IsNoOp()
        {
            string baseDir = FreshBase();
            try
            {
                string url = "https://dl/mods/player-hud-abc1234567.zip";
                var map = new Dictionary<string, byte[]>
                {
                    [url] = MakeZip(("mod.json", "{\"id\":\"player-hud\"}"))
                };
                var (updater, handler) = MakeUpdater(baseDir, map);
                var resp = new UpdateCheckResponse
                {
                    Mods = new List<ModUpdate> { new ModUpdate { Id = "player-hud", Hash = "abc1234567", Url = url } }
                };

                Assert.Equal(1, await updater.ReconcileModsAsync(resp));
                int hitsAfterFirst = handler.Hits;
                Assert.Equal(0, await updater.ReconcileModsAsync(resp));
                Assert.Equal(hitsAfterFirst, handler.Hits);   // no second download
            }
            finally { Directory.Delete(baseDir, true); }
        }

        [Fact]
        public async Task HashMismatch_ReplacesFolderContents()
        {
            string baseDir = FreshBase();
            try
            {
                // Pre-existing v1 in the store with a stale file + old marker.
                string modDir = Path.Combine(baseDir, "mods", "player-hud");
                Directory.CreateDirectory(modDir);
                File.WriteAllText(Path.Combine(modDir, "old.lua"), "-- stale");
                File.WriteAllText(Path.Combine(modDir, "modhash"), "0000000000");

                string url = "https://dl/mods/player-hud-newhash999.zip";
                var map = new Dictionary<string, byte[]>
                {
                    [url] = MakeZip(("mod.json", "{}"), ("new.lua", "-- new"))
                };
                var (updater, _) = MakeUpdater(baseDir, map);
                var resp = new UpdateCheckResponse
                {
                    Mods = new List<ModUpdate> { new ModUpdate { Id = "player-hud", Hash = "newhash999", Url = url } }
                };

                Assert.Equal(1, await updater.ReconcileModsAsync(resp));
                Assert.False(File.Exists(Path.Combine(modDir, "old.lua")));   // wiped
                Assert.True(File.Exists(Path.Combine(modDir, "new.lua")));    // extracted
                Assert.Equal("newhash999", File.ReadAllText(Path.Combine(modDir, "modhash")).Trim());
            }
            finally { Directory.Delete(baseDir, true); }
        }

        [Fact]
        public async Task UserMod_NotInResponse_IsNeverTouched()
        {
            string baseDir = FreshBase();
            try
            {
                // A user's own mod: unknown id, no modhash. The server response
                // names a DIFFERENT mod. The user's folder must be left intact.
                string userMod = Path.Combine(baseDir, "mods", "my-cool-mod");
                Directory.CreateDirectory(userMod);
                File.WriteAllText(Path.Combine(userMod, "mod.json"), "{\"id\":\"my-cool-mod\"}");
                File.WriteAllText(Path.Combine(userMod, "secret.lua"), "-- do not touch");

                string url = "https://dl/mods/player-hud-abc1234567.zip";
                var map = new Dictionary<string, byte[]> { [url] = MakeZip(("mod.json", "{}")) };
                var (updater, _) = MakeUpdater(baseDir, map);
                var resp = new UpdateCheckResponse
                {
                    Mods = new List<ModUpdate> { new ModUpdate { Id = "player-hud", Hash = "abc1234567", Url = url } }
                };

                await updater.ReconcileModsAsync(resp);

                Assert.True(File.Exists(Path.Combine(userMod, "secret.lua")));
                Assert.Equal("-- do not touch", File.ReadAllText(Path.Combine(userMod, "secret.lua")));
                Assert.False(File.Exists(Path.Combine(userMod, "modhash")));   // still no marker
            }
            finally { Directory.Delete(baseDir, true); }
        }

        [Fact]
        public async Task OurMod_NotInResponse_IsPruned()
        {
            string baseDir = FreshBase();
            try
            {
                // One of OUR mods (carries a modhash marker) that the server no
                // longer publishes -- a renamed/removed mod. It must be pruned.
                string oldMod = Path.Combine(baseDir, "mods", "player-hud");
                Directory.CreateDirectory(oldMod);
                File.WriteAllText(Path.Combine(oldMod, "freya_ui.lua"), "-- old");
                File.WriteAllText(Path.Combine(oldMod, "modhash"), "deadbeef00");

                // A user's own mod (no marker) that is ALSO absent from the
                // response must survive the prune.
                string userMod = Path.Combine(baseDir, "mods", "my-cool-mod");
                Directory.CreateDirectory(userMod);
                File.WriteAllText(Path.Combine(userMod, "secret.lua"), "-- keep");

                string url = "https://dl/mods/freya-hud-abc1234567.zip";
                var map = new Dictionary<string, byte[]> { [url] = MakeZip(("mod.json", "{}")) };
                var (updater, _) = MakeUpdater(baseDir, map);
                var resp = new UpdateCheckResponse
                {
                    Mods = new List<ModUpdate> { new ModUpdate { Id = "freya-hud", Hash = "abc1234567", Url = url } }
                };

                await updater.ReconcileModsAsync(resp);

                Assert.False(Directory.Exists(oldMod));                       // ours, gone -> pruned
                Assert.True(File.Exists(Path.Combine(userMod, "secret.lua"))); // user's -> kept
                Assert.True(Directory.Exists(Path.Combine(baseDir, "mods", "freya-hud"))); // new -> present
            }
            finally { Directory.Delete(baseDir, true); }
        }

        [Fact]
        public async Task UnsafeModId_IsSkipped()
        {
            string baseDir = FreshBase();
            try
            {
                var (updater, handler) = MakeUpdater(baseDir, new Dictionary<string, byte[]>());
                var resp = new UpdateCheckResponse
                {
                    Mods = new List<ModUpdate>
                    {
                        new ModUpdate { Id = "../escape", Hash = "x", Url = "https://dl/evil.zip" }
                    }
                };

                Assert.Equal(0, await updater.ReconcileModsAsync(resp));
                Assert.Equal(0, handler.Hits);   // never even attempted a download
            }
            finally { Directory.Delete(baseDir, true); }
        }
    }
}
