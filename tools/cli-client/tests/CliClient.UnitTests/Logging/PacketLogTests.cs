// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Text.Json;
using N7.CliClient.Logging;
using N7.CliClient.Net;
using Xunit;

namespace N7.CliClient.UnitTests.Logging;

public sealed class PacketLogTests
{
    private static Packet MakePacket(ushort opcode, byte[] payload)
        => Packet.ForOpcode(opcode, payload);

    [Fact]
    public void Log_WritesOneNdjsonLinePerCall()
    {
        var sw = new StringWriter();
        using var log = new PacketLog(sw);

        log.Log(PacketDirection.Outbound, MakePacket(0x0035, new byte[] { 1, 2, 3 }));
        log.Log(PacketDirection.Inbound,  MakePacket(0x0036, new byte[] { 4 }));

        string[] lines = sw.ToString()
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.TrimEnd('\r'))
            .ToArray();

        Assert.Equal(2, lines.Length);
        foreach (var line in lines)
        {
            // Each line must parse as a JSON object.
            using var doc = JsonDocument.Parse(line);
            Assert.Equal(JsonValueKind.Object, doc.RootElement.ValueKind);
        }
    }

    [Fact]
    public void Log_PopulatesAllRequiredFields()
    {
        var sw = new StringWriter();
        using var log = new PacketLog(sw);
        var fixedTs = new DateTimeOffset(2026, 5, 24, 18, 30, 0, TimeSpan.Zero);

        log.Log(
            PacketDirection.Outbound,
            MakePacket(0x0035, new byte[] { 0xCA, 0xFE }),
            decoded: null,
            timestamp: fixedTs);

        using var doc = JsonDocument.Parse(sw.ToString().Trim());
        var root = doc.RootElement;

        Assert.Equal("2026-05-24T18:30:00.0000000+00:00", root.GetProperty("ts").GetString());
        Assert.Equal("outbound",                          root.GetProperty("direction").GetString());
        Assert.Equal("0x0035",                            root.GetProperty("opcode_hex").GetString());
        Assert.Equal("MasterJoin",                        root.GetProperty("opcode_name").GetString());
        Assert.Equal(2,                                   root.GetProperty("length").GetInt32());
        Assert.Equal("cafe",                              root.GetProperty("payload_hex").GetString());
        Assert.False(root.TryGetProperty("decoded", out _));
    }

    [Fact]
    public void Log_OmitsOpcodeName_WhenUnknown()
    {
        var sw = new StringWriter();
        using var log = new PacketLog(sw);
        log.Log(PacketDirection.Inbound, MakePacket(0xDEAD, Array.Empty<byte>()));

        using var doc = JsonDocument.Parse(sw.ToString().Trim());
        Assert.False(doc.RootElement.TryGetProperty("opcode_name", out _));
        Assert.Equal("0xDEAD", doc.RootElement.GetProperty("opcode_hex").GetString());
    }

    [Fact]
    public void Log_IncludesDecodedField_WhenProvided()
    {
        var sw = new StringWriter();
        using var log = new PacketLog(sw);

        log.Log(
            PacketDirection.Inbound,
            MakePacket(0x0036, new byte[] { 0xAA }),
            decoded: new { sector = 42, host = "127.0.0.1" });

        using var doc = JsonDocument.Parse(sw.ToString().Trim());
        var decoded = doc.RootElement.GetProperty("decoded");
        Assert.Equal(42, decoded.GetProperty("sector").GetInt32());
        Assert.Equal("127.0.0.1", decoded.GetProperty("host").GetString());
    }

    [Fact]
    public void Log_EmptyPayload_ProducesEmptyHex()
    {
        var sw = new StringWriter();
        using var log = new PacketLog(sw);
        log.Log(PacketDirection.Outbound, MakePacket(0x0000, Array.Empty<byte>()));

        using var doc = JsonDocument.Parse(sw.ToString().Trim());
        Assert.Equal("", doc.RootElement.GetProperty("payload_hex").GetString());
        Assert.Equal(0, doc.RootElement.GetProperty("length").GetInt32());
    }

    [Fact]
    public void Log_AfterDispose_Throws()
    {
        var sw = new StringWriter();
        var log = new PacketLog(sw);
        log.Dispose();

        Assert.Throws<ObjectDisposedException>(
            () => log.Log(PacketDirection.Inbound, MakePacket(0, Array.Empty<byte>())));
    }

    [Fact]
    public void OpenFile_CreatesDirectory_AndAppends()
    {
        string dir = Path.Combine(Path.GetTempPath(), "n7-packetlog-test-" + Guid.NewGuid().ToString("N"));
        string file = Path.Combine(dir, "sub", "packets.ndjson");
        try
        {
            using (var log = PacketLog.OpenFile(file))
            {
                log.Log(PacketDirection.Outbound, MakePacket(0x0035, new byte[] { 1 }));
            }
            using (var log = PacketLog.OpenFile(file))
            {
                log.Log(PacketDirection.Inbound, MakePacket(0x0036, new byte[] { 2 }));
            }

            string[] lines = File.ReadAllLines(file);
            Assert.Equal(2, lines.Length);
            using var d1 = JsonDocument.Parse(lines[0]);
            using var d2 = JsonDocument.Parse(lines[1]);
            Assert.Equal("outbound", d1.RootElement.GetProperty("direction").GetString());
            Assert.Equal("inbound",  d2.RootElement.GetProperty("direction").GetString());
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Log_FlushesOnEveryCall()
    {
        // The underlying StreamWriter is wrapped with AutoFlush=false but
        // Log() must explicitly flush — otherwise a crashing client would
        // lose the last few packets, defeating the point of a packet log.
        string file = Path.Combine(Path.GetTempPath(), $"n7-packetlog-flush-{Guid.NewGuid():N}.ndjson");
        try
        {
            using var log = PacketLog.OpenFile(file);
            log.Log(PacketDirection.Outbound, MakePacket(0x0035, new byte[] { 1, 2 }));

            // Read while log is still open — succeeds because FileShare.Read.
            string contents = File.ReadAllText(file);
            using var doc = JsonDocument.Parse(contents.Trim());
            Assert.Equal("0x0035", doc.RootElement.GetProperty("opcode_hex").GetString());
        }
        finally
        {
            if (File.Exists(file)) File.Delete(file);
        }
    }

    [Fact]
    public void Log_IsThreadSafe()
    {
        var sw = new StringWriter();
        using var log = new PacketLog(sw);

        Parallel.For(0, 200, i =>
        {
            log.Log(
                i % 2 == 0 ? PacketDirection.Inbound : PacketDirection.Outbound,
                MakePacket((ushort)(0x1000 + (i & 0xFF)), new byte[] { (byte)i }));
        });

        string[] lines = sw.ToString()
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.TrimEnd('\r'))
            .ToArray();

        Assert.Equal(200, lines.Length);
        foreach (var line in lines)
        {
            using var doc = JsonDocument.Parse(line);
            Assert.Equal(JsonValueKind.Object, doc.RootElement.ValueKind);
        }
    }
}
