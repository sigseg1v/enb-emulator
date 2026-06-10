// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using N7.CliClient.Logging;
using Xunit;

namespace N7.CliClient.UnitTests.Logging;

public sealed class ChatLogTests
{
    [Fact]
    public void Log_WritesOneLinePerCall_WithDefaultChannel()
    {
        var sw = new StringWriter();
        using var log = new ChatLog(sw);
        var ts = new DateTimeOffset(2026, 5, 24, 18, 30, 5, 123, TimeSpan.Zero);

        log.Log("Alice", "hello world", timestamp: ts);

        Assert.Equal(
            "2026-05-24T18:30:05.123Z [chat] Alice: hello world" + Environment.NewLine,
            sw.ToString());
    }

    [Fact]
    public void Log_UsesProvidedChannel()
    {
        var sw = new StringWriter();
        using var log = new ChatLog(sw);
        log.Log("Bob", "team go", channel: "team",
            timestamp: new DateTimeOffset(2026, 5, 24, 18, 30, 5, TimeSpan.Zero));
        Assert.Contains("[team] Bob: team go", sw.ToString());
    }

    [Fact]
    public void Log_EmptyMessage_IsAllowed()
    {
        var sw = new StringWriter();
        using var log = new ChatLog(sw);
        log.Log("Alice", "");
        Assert.Contains("Alice: ", sw.ToString());
    }

    [Fact]
    public void Log_EmptySender_Throws()
    {
        var sw = new StringWriter();
        using var log = new ChatLog(sw);
        Assert.Throws<ArgumentException>(() => log.Log("", "hi"));
    }

    [Fact]
    public void Log_NullMessage_Throws()
    {
        var sw = new StringWriter();
        using var log = new ChatLog(sw);
        Assert.Throws<ArgumentNullException>(() => log.Log("Alice", null!));
    }

    [Fact]
    public void Log_AfterDispose_Throws()
    {
        var sw = new StringWriter();
        var log = new ChatLog(sw);
        log.Dispose();
        Assert.Throws<ObjectDisposedException>(() => log.Log("Alice", "hi"));
    }

    [Fact]
    public void OpenFile_AppendsAcrossReopens()
    {
        string dir = Path.Combine(Path.GetTempPath(), "n7-chatlog-test-" + Guid.NewGuid().ToString("N"));
        string file = Path.Combine(dir, "chat.log");
        try
        {
            using (var log = ChatLog.OpenFile(file)) log.Log("Alice", "first");
            using (var log = ChatLog.OpenFile(file)) log.Log("Bob",   "second");

            string[] lines = File.ReadAllLines(file);
            Assert.Equal(2, lines.Length);
            Assert.Contains("Alice: first",  lines[0]);
            Assert.Contains("Bob: second",   lines[1]);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Log_IsThreadSafe()
    {
        var sw = new StringWriter();
        using var log = new ChatLog(sw);
        Parallel.For(0, 200, i => log.Log("U" + i, "msg " + i));
        var lines = sw.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(200, lines.Length);
    }
}
