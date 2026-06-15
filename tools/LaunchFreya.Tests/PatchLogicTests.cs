using System;
using System.IO;
using System.Threading.Tasks;
using LaunchFreya.Update;
using Xunit;

namespace LaunchFreya.Tests
{
    // Unit tests for the operator-supplied game-data patch logic (enb-patch.exe).
    // Covers the patchlevel.txt model the launcher uses to decide whether a patch
    // has already been applied, the safe-name guard (a patch name becomes a URL
    // segment + staged file name), and the /updateCheck "patches" array parse.
    // The actual execution + auth.ini special case live in Updater.cs (IO/process)
    // and are exercised by the integration suite, not here.
    public class PatchLogicTests
    {
        // ---- ParsePatchLevel ----

        [Fact]
        public void ParsePatchLevel_ReadsOneHashPerLine()
        {
            var hashes = UpdateLogic.ParsePatchLevel("aaa\nbbb\nccc\n");
            Assert.Equal(new[] { "aaa", "bbb", "ccc" }, hashes);
        }

        [Fact]
        public void ParsePatchLevel_IgnoresBlankLinesAndWhitespace_AndLowercases()
        {
            var hashes = UpdateLogic.ParsePatchLevel("  AAA  \n\n\t\nBbB\r\n");
            Assert.Equal(new[] { "aaa", "bbb" }, hashes);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void ParsePatchLevel_EmptyYieldsNoHashes(string content)
            => Assert.Empty(UpdateLogic.ParsePatchLevel(content));

        // ---- IsPatchApplied ----

        [Fact]
        public void IsPatchApplied_MatchesCaseInsensitively()
        {
            var applied = UpdateLogic.ParsePatchLevel("abc123\n");
            Assert.True(UpdateLogic.IsPatchApplied(applied, "ABC123"));
            Assert.False(UpdateLogic.IsPatchApplied(applied, "deadbeef"));
        }

        [Fact]
        public void IsPatchApplied_FalseForEmptyHashOrList()
        {
            Assert.False(UpdateLogic.IsPatchApplied(null, "abc"));
            Assert.False(UpdateLogic.IsPatchApplied(new[] { "abc" }, ""));
            Assert.False(UpdateLogic.IsPatchApplied(new[] { "abc" }, null));
        }

        // ---- AppendPatchHash ----

        [Fact]
        public void AppendPatchHash_AddsToEmpty()
        {
            string body = UpdateLogic.AppendPatchHash("", "abc");
            Assert.Equal(new[] { "abc" }, UpdateLogic.ParsePatchLevel(body));
            Assert.EndsWith("\n", body);
        }

        [Fact]
        public void AppendPatchHash_AppendsToExisting()
        {
            string body = UpdateLogic.AppendPatchHash("aaa\nbbb\n", "ccc");
            Assert.Equal(new[] { "aaa", "bbb", "ccc" }, UpdateLogic.ParsePatchLevel(body));
        }

        [Fact]
        public void AppendPatchHash_IsIdempotent()
        {
            string once = UpdateLogic.AppendPatchHash("aaa\n", "BBB");
            string twice = UpdateLogic.AppendPatchHash(once, "bbb");
            Assert.Equal(once, twice);
            Assert.Single(UpdateLogic.ParsePatchLevel(twice), h => h == "bbb");
        }

        [Fact]
        public void AppendPatchHash_EmptyHashLeavesContentEffectivelyUnchanged()
        {
            string body = UpdateLogic.AppendPatchHash("aaa\n", "   ");
            Assert.Equal(new[] { "aaa" }, UpdateLogic.ParsePatchLevel(body));
        }

        // ---- IsSafePatchName ----

        [Theory]
        [InlineData("enb-patch.exe")]
        [InlineData("patch2.exe")]
        [InlineData("a.b_c-1.exe")]
        public void IsSafePatchName_AcceptsSimpleNames(string name)
            => Assert.True(UpdateLogic.IsSafePatchName(name));

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        [InlineData("..")]
        [InlineData("dir/enb-patch.exe")]   // path separator
        [InlineData("dir\\enb-patch.exe")]  // windows separator
        [InlineData("../evil.exe")]         // traversal
        [InlineData("enb patch.exe")]       // space
        public void IsSafePatchName_RejectsUnsafe(string name)
            => Assert.False(UpdateLogic.IsSafePatchName(name));

        // ---- ParseResponse reads the patches array ----

        [Fact]
        public void ParseResponse_ReadsPatchesArray()
        {
            string json = "{\"status\":\"UP_TO_DATE\",\"patches\":[" +
                "{\"name\":\"enb-patch.exe\",\"hash\":\"deadbeef00\",\"url\":\"https://dl/patches/enb-patch.exe\"}]}";
            var resp = UpdateLogic.ParseResponse(json);
            Assert.NotNull(resp);
            Assert.Single(resp.Patches);
            Assert.Equal("enb-patch.exe", resp.Patches[0].Name);
            Assert.Equal("deadbeef00", resp.Patches[0].Hash);
            Assert.Equal("https://dl/patches/enb-patch.exe", resp.Patches[0].Url);
        }

        [Fact]
        public void ParseResponse_NoPatchesFieldYieldsNullList()
        {
            var resp = UpdateLogic.ParseResponse("{\"status\":\"UP_TO_DATE\"}");
            Assert.NotNull(resp);
            Assert.True(resp.Patches == null || resp.Patches.Count == 0);
        }

        // ---- ReconcileLocalPatchAsync (play-local detection branches) ----
        // The Applied branch runs the real enb-patch.exe and is exercised manually
        // / by the integration suite; here we pin the non-executing decisions.

        static Updater NewUpdater(out string baseDir)
        {
            baseDir = Directory.CreateTempSubdirectory("freya-localpatch-").FullName;
            return new Updater(baseDir, selfExePath: null, log: _ => { });
        }

        [Fact]
        public async Task ReconcileLocalPatch_AuthIniPresent_IsAlreadyPatched()
        {
            var updater = NewUpdater(out _);
            string install = Directory.CreateTempSubdirectory("freya-install-").FullName;
            string authIni = Path.Combine(install, "Data", "client", "ini", "auth.ini");
            Directory.CreateDirectory(Path.GetDirectoryName(authIni));
            File.WriteAllText(authIni, "patched");

            var result = await updater.ReconcileLocalPatchAsync(install, localPatchExePath: null);
            Assert.Equal(Updater.LocalPatchResult.AlreadyPatched, result);
        }

        [Fact]
        public async Task ReconcileLocalPatch_UnpatchedAndNoExe_IsMissingPatch()
        {
            var updater = NewUpdater(out _);
            string install = Directory.CreateTempSubdirectory("freya-install-").FullName;

            // No auth.ini and no on-disk patch to apply -> tell the user to obtain it.
            Assert.Equal(Updater.LocalPatchResult.MissingPatch,
                await updater.ReconcileLocalPatchAsync(install, localPatchExePath: null));
            Assert.Equal(Updater.LocalPatchResult.MissingPatch,
                await updater.ReconcileLocalPatchAsync(install,
                    Path.Combine(install, "does-not-exist.exe")));
        }

        [Fact]
        public async Task ReconcileLocalPatch_MissingInstallDir_IsFailed()
        {
            var updater = NewUpdater(out _);
            string missing = Path.Combine(Path.GetTempPath(), "freya-no-such-" + Guid.NewGuid().ToString("N"));
            var result = await updater.ReconcileLocalPatchAsync(missing, localPatchExePath: null);
            Assert.Equal(Updater.LocalPatchResult.Failed, result);
        }
    }
}
