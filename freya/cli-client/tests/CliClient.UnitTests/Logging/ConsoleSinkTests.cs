// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using N7.CliClient.Logging;
using N7.CliClient.Net;
using Xunit;

namespace N7.CliClient.UnitTests.Logging;

public sealed class ConsoleSinkTests
{
    [Fact]
    public void Packet_FormatsKnownOpcode_WithName()
    {
        var sw = new StringWriter();
        var sink = new ConsoleSink(sw);
        sink.Packet(
            PacketDirection.Outbound,
            Packet.ForOpcode(0x0035, new byte[] { 0xAB, 0xCD }),
            timestamp: new DateTimeOffset(2026, 5, 24, 18, 30, 5, 123, TimeSpan.Zero));

        string line = sw.ToString().TrimEnd();
        Assert.Contains("18:30:05.123", line);
        Assert.Contains("0x0035", line);
        Assert.Contains("MasterJoin", line);
        Assert.Contains("(2 bytes)", line);
        Assert.Contains("abcd", line);
    }

    [Fact]
    public void Packet_UnknownOpcode_StillRenders_WithQuestionMarkName()
    {
        var sw = new StringWriter();
        var sink = new ConsoleSink(sw);
        sink.Packet(PacketDirection.Inbound, Packet.ForOpcode(0xDEAD, Array.Empty<byte>()));

        string line = sw.ToString();
        Assert.Contains("0xDEAD", line);
        Assert.Contains(" ? ", line);
    }

    [Fact]
    public void Packet_LongPayload_IsTruncated()
    {
        var sw = new StringWriter();
        var sink = new ConsoleSink(sw);
        byte[] payload = new byte[64];
        for (int i = 0; i < payload.Length; i++) payload[i] = (byte)i;

        sink.Packet(PacketDirection.Inbound, Packet.ForOpcode(0x0036, payload));

        string line = sw.ToString();
        // First 32 bytes hexified = 64 hex chars; then ellipsis.
        Assert.Contains("…", line);
        // Should NOT contain the byte at offset 33 (0x21) in a way that
        // proves the full payload is in the line — but checking absence
        // of the last byte is most direct:
        Assert.DoesNotContain("3f", line); // 0x3f = byte 63, well past truncation
    }

    [Fact]
    public void Chat_FormatsWithChannel()
    {
        var sw = new StringWriter();
        var sink = new ConsoleSink(sw);
        sink.Chat("Alice", "hello",
            timestamp: new DateTimeOffset(2026, 5, 24, 18, 30, 5, TimeSpan.Zero));
        Assert.Contains("[chat] Alice: hello", sw.ToString());
        Assert.Contains("[18:30:05]", sw.ToString());
    }

    [Fact]
    public void Info_FormatsWithTimestamp()
    {
        var sw = new StringWriter();
        var sink = new ConsoleSink(sw);
        sink.Info("connected to global");
        string line = sw.ToString();
        Assert.Contains("connected to global", line);
        // Crude timestamp check: at least starts with '['
        Assert.StartsWith("[", line);
    }

    [Fact]
    public void Packet_OutboundUsesRightArrow_InboundUsesLeft()
    {
        var sw = new StringWriter();
        var sink = new ConsoleSink(sw);
        sink.Packet(PacketDirection.Outbound, Packet.ForOpcode(0x0035, Array.Empty<byte>()));
        sink.Packet(PacketDirection.Inbound,  Packet.ForOpcode(0x0036, Array.Empty<byte>()));
        string output = sw.ToString();
        Assert.Contains("→", output);
        Assert.Contains("←", output);
    }
}
