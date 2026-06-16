using System;
using System.IO;
using LaunchFreya;
using Xunit;

namespace LaunchFreya.Tests
{
    // Unit tests for the log-file location/read helpers extracted from MainWindow
    // (Phase AT-5). Filesystem-only, exercised against a per-test temp directory.
    public class LauncherLogFilesTests : IDisposable
    {
        readonly string _dir;

        public LauncherLogFilesTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "launchfreya-logtests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_dir, recursive: true); } catch { /* best-effort */ }
        }

        // ---- ExistingFile ----

        [Fact]
        public void ExistingFile_ReturnsPathWhenPresent()
        {
            var p = Path.Combine(_dir, "there.log");
            File.WriteAllText(p, "x");
            Assert.Equal(p, LauncherLogFiles.ExistingFile(p));
        }

        [Fact]
        public void ExistingFile_ReturnsNullWhenAbsent()
        {
            Assert.Null(LauncherLogFiles.ExistingFile(Path.Combine(_dir, "nope.log")));
        }

        // ---- NewestLogFile ----

        [Fact]
        public void NewestLogFile_PicksNewestByWriteTime()
        {
            var older = Path.Combine(_dir, "a_2020_01_01.log");
            var newer = Path.Combine(_dir, "a_2020_01_02.log");
            File.WriteAllText(older, "old");
            File.WriteAllText(newer, "new");
            File.SetLastWriteTimeUtc(older, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            File.SetLastWriteTimeUtc(newer, new DateTime(2020, 1, 2, 0, 0, 0, DateTimeKind.Utc));
            Assert.Equal(newer, LauncherLogFiles.NewestLogFile(_dir, "*.log"));
        }

        [Fact]
        public void NewestLogFile_ReturnsNullForMissingDirOrNoMatch()
        {
            Assert.Null(LauncherLogFiles.NewestLogFile(Path.Combine(_dir, "missing"), "*.log"));
            Assert.Null(LauncherLogFiles.NewestLogFile(_dir, "*.nomatch"));
            Assert.Null(LauncherLogFiles.NewestLogFile(null, "*.log"));
        }

        // ---- FindClientLog ----

        [Fact]
        public void FindClientLog_FindsKnownClientLogName()
        {
            var p = Path.Combine(_dir, "Net7.log");
            File.WriteAllText(p, "log");
            Assert.Equal(p, LauncherLogFiles.FindClientLog(_dir));
        }

        [Fact]
        public void FindClientLog_IgnoresUnrelatedLog()
        {
            File.WriteAllText(Path.Combine(_dir, "enbmod.log"), "not the client log");
            Assert.Null(LauncherLogFiles.FindClientLog(_dir));
        }

        [Fact]
        public void FindClientLog_NullForEmptyDir()
        {
            Assert.Null(LauncherLogFiles.FindClientLog(null));
            Assert.Null(LauncherLogFiles.FindClientLog(""));
        }

        // ---- ReadLogFile ----

        [Fact]
        public void ReadLogFile_ReadsContentsWhilePathExists()
        {
            var p = Path.Combine(_dir, "read.log");
            File.WriteAllText(p, "hello world");
            Assert.Equal("hello world", LauncherLogFiles.ReadLogFile(() => p)());
        }

        [Fact]
        public void ReadLogFile_NullWhenResolverReturnsMissing()
        {
            Assert.Null(LauncherLogFiles.ReadLogFile(() => Path.Combine(_dir, "gone.log"))());
            Assert.Null(LauncherLogFiles.ReadLogFile(() => null)());
        }

        [Fact]
        public void ReadLogFile_SwallowsResolverThrow()
        {
            Assert.Null(LauncherLogFiles.ReadLogFile(() => throw new InvalidOperationException())());
        }
    }
}
