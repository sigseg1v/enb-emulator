using System.IO;
using System.Text.Json;
using LaunchFreya.Update;
using Xunit;

namespace LaunchFreya.Tests
{
    // Unit tests for the launcher self-updater's pure logic (Phase AN). The
    // path-traversal guard (TryResolveWithinBase) is the highest-risk item in
    // the phase -- a server-supplied relativePath that escaped the install dir
    // would let /updateCheck overwrite arbitrary files -- so it is tested
    // exhaustively.
    public class UpdateLogicTests
    {
        // A rooted base dir that need not exist on disk; Path.GetFullPath does
        // not require existence. Using the temp root keeps it valid on every OS.
        static string BaseDir => Path.Combine(Path.GetTempPath(), "freya-install-base");

        // ---- TryResolveWithinBase: accepts files inside the base ----

        [Theory]
        [InlineData("FreyaLauncher.exe")]
        [InlineData("FreyaLauncher.cfg")]
        [InlineData("bin/FreyaProxy.exe")]
        [InlineData("bin/sub/deep.dat")]
        [InlineData("./FreyaLauncher.exe")]      // normalizes to the same file
        [InlineData("bin/../FreyaLauncher.exe")] // dot-dot that stays inside
        public void Resolve_AcceptsPathsInsideBase(string rel)
        {
            bool ok = UpdateLogic.TryResolveWithinBase(BaseDir, rel, out string resolved);
            Assert.True(ok);
            Assert.NotNull(resolved);
            string baseFull = Path.GetFullPath(BaseDir) + Path.DirectorySeparatorChar;
            Assert.StartsWith(baseFull, resolved, System.StringComparison.Ordinal);
        }

        // ---- TryResolveWithinBase: rejects every escape ----

        [Theory]
        [InlineData("../evil.exe")]                 // parent
        [InlineData("../../etc/passwd")]            // multi-level parent
        [InlineData("bin/../../evil.exe")]          // dot-dot that escapes
        [InlineData("bin/../../../evil")]           // deep escape
        [InlineData("C:foo")]                       // rooted-but-relative (drive-relative)
        [InlineData("")]                            // empty
        [InlineData("   ")]                         // whitespace
        public void Resolve_RejectsEscapes(string rel)
        {
            bool ok = UpdateLogic.TryResolveWithinBase(BaseDir, rel, out string resolved);
            Assert.False(ok);
            Assert.Null(resolved);
        }

        [Fact]
        public void Resolve_RejectsNull()
        {
            Assert.False(UpdateLogic.TryResolveWithinBase(BaseDir, null, out var r));
            Assert.Null(r);
            Assert.False(UpdateLogic.TryResolveWithinBase(null, "x", out var r2));
            Assert.Null(r2);
        }

        [Fact]
        public void Resolve_RejectsAbsolutePaths()
        {
            // An OS-appropriate absolute path must never be accepted.
            string abs = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "elsewhere.exe"));
            Assert.False(UpdateLogic.TryResolveWithinBase(BaseDir, abs, out var r));
            Assert.Null(r);
        }

        [Fact]
        public void Resolve_RejectsSiblingThatSharesPrefix()
        {
            // base "/tmp/freya-install-base" must not accept a sibling
            // "/tmp/freya-install-base-evil/x" -- the trailing-separator
            // normalization in the guard is what stops the prefix match.
            string baseFull = Path.GetFullPath(BaseDir);
            string sibling = "../" + Path.GetFileName(baseFull) + "-evil/x";
            Assert.False(UpdateLogic.TryResolveWithinBase(BaseDir, sibling, out var r));
            Assert.Null(r);
        }

        [Fact]
        public void Resolve_RejectsBaseDirItself()
        {
            // "." resolves to the base dir, which is not a file under it.
            Assert.False(UpdateLogic.TryResolveWithinBase(BaseDir, ".", out var r));
            Assert.Null(r);
        }

        // ---- ComputeSha512 ----

        [Fact]
        public void Sha512_OfEmptyFile_MatchesKnownVector()
        {
            string tmp = Path.GetTempFileName();
            try
            {
                File.WriteAllBytes(tmp, System.Array.Empty<byte>());
                string hash = UpdateLogic.ComputeSha512(tmp);
                Assert.Equal(
                    "cf83e1357eefb8bdf1542850d66d8007d620e4050b5715dc83f4a921d36ce9ce" +
                    "47d0d13c5d85f2b0ff8318d2877eec2f63b931bd47417a81a538327af927da3e",
                    hash);
            }
            finally { File.Delete(tmp); }
        }

        [Fact]
        public void Sha512_IsLowercaseHex()
        {
            string tmp = Path.GetTempFileName();
            try
            {
                File.WriteAllText(tmp, "freya");
                string hash = UpdateLogic.ComputeSha512(tmp);
                Assert.Equal(128, hash.Length);
                Assert.Equal(hash.ToLowerInvariant(), hash);
            }
            finally { File.Delete(tmp); }
        }

        // ---- HashesEqual ----

        [Theory]
        [InlineData("abcDEF", "ABCdef", true)]   // case-insensitive
        [InlineData("  abc  ", "abc", true)]     // trimmed
        [InlineData("abc", "abd", false)]
        [InlineData("", "abc", false)]           // empty never matches
        [InlineData("abc", "", false)]
        [InlineData(null, "abc", false)]
        [InlineData("abc", null, false)]
        public void HashesEqual_Cases(string a, string b, bool expected)
            => Assert.Equal(expected, UpdateLogic.HashesEqual(a, b));

        // ---- BuildRequestJson + ParseResponse round-trip ----

        [Fact]
        public void BuildRequestJson_EmitsContractFieldNames()
        {
            string json = UpdateLogic.BuildRequestJson("LAUNCH", "PROXY", "POSFEED", "INJECT", "ENBMOD");
            using var doc = JsonDocument.Parse(json);
            Assert.Equal("LAUNCH", doc.RootElement.GetProperty("launcherHash").GetString());
            Assert.Equal("PROXY", doc.RootElement.GetProperty("proxyHash").GetString());
            Assert.Equal("POSFEED", doc.RootElement.GetProperty("posFeedHash").GetString());
            Assert.Equal("INJECT", doc.RootElement.GetProperty("injectHash").GetString());
            Assert.Equal("ENBMOD", doc.RootElement.GetProperty("enbmodHash").GetString());
        }

        [Fact]
        public void ParseResponse_ReadsUpToDate()
        {
            var resp = UpdateLogic.ParseResponse("{\"status\":\"UP_TO_DATE\"}");
            Assert.NotNull(resp);
            Assert.Equal(UpdateStatus.UpToDate, resp.Status);
            Assert.True(resp.Files == null || resp.Files.Count == 0);
        }

        [Fact]
        public void ParseResponse_ReadsUpdateNeededWithFiles()
        {
            string json = "{\"status\":\"UPDATE_NEEDED\",\"files\":[" +
                "{\"relativePath\":\"FreyaLauncher.exe\",\"url\":\"https://dl/x\",\"hash\":\"AA\"}," +
                "{\"relativePath\":\"bin/FreyaProxy.exe\",\"url\":\"https://dl/y\",\"hash\":\"BB\"}]}";
            var resp = UpdateLogic.ParseResponse(json);
            Assert.NotNull(resp);
            Assert.Equal(UpdateStatus.UpdateNeeded, resp.Status);
            Assert.Equal(2, resp.Files.Count);
            Assert.Equal("FreyaLauncher.exe", resp.Files[0].RelativePath);
            Assert.Equal("https://dl/x", resp.Files[0].Url);
            Assert.Equal("AA", resp.Files[0].Hash);
        }

        [Fact]
        public void ParseResponse_IsCaseInsensitiveOnFieldNames()
        {
            var resp = UpdateLogic.ParseResponse("{\"Status\":\"UP_TO_DATE\"}");
            Assert.NotNull(resp);
            Assert.Equal(UpdateStatus.UpToDate, resp.Status);
        }

        [Theory]
        [InlineData("not json")]
        [InlineData("{")]
        [InlineData("")]
        [InlineData("   ")]
        public void ParseResponse_ReturnsNullOnUnparseable(string json)
            => Assert.Null(UpdateLogic.ParseResponse(json));
    }
}
