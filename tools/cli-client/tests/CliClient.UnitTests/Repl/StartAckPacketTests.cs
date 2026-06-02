// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Buffers.Binary;
using N7.CliClient.Net;
using N7.CliClient.Opcodes;
using N7.CliClient.Repl;
using Xunit;

namespace N7.CliClient.UnitTests.Repl;

/// <summary>
/// Byte-pins the 0x0006 START_ACK frame the sector handshake sends in reply
/// to 0x0005 START. This is load-bearing: the proxy turns START_ACK into
/// 0x3004 PLAYER_SHIP_SENT, which is what flips the server-side avatar to
/// InSpace=true. Without that, UpdatePlayerVisibilityList early-returns and
/// the client never sees other players or mobs (sector chat still works --
/// it keys off the sector player-list, not InSpace). If the frame shape
/// drifts, the proxy stops recognising it and in-space login silently breaks.
/// </summary>
public sealed class StartAckPacketTests
{
    [Fact]
    public void BuildStartAckPacket_HasOpcode0006()
    {
        var pkt = SectorEnterDriver.BuildStartAckPacket(0x6F);
        Assert.Equal(OpcodeId.Known.StartAck.Value, pkt.Header.Opcode);
        Assert.Equal((ushort)0x0006, pkt.Header.Opcode);
    }

    [Fact]
    public void BuildStartAckPacket_PayloadIsFourByteStartId()
    {
        var pkt = SectorEnterDriver.BuildStartAckPacket(0x6F);
        Assert.Equal(4, pkt.Payload.Length);
        Assert.Equal(0x6F, BinaryPrimitives.ReadInt32LittleEndian(pkt.Payload.Span));
        // total frame = 4-byte header + 4-byte payload
        Assert.Equal((ushort)8, pkt.Header.Size);
    }

    [Theory]
    [InlineData(0x0000006F, new byte[] { 0x08, 0x00, 0x06, 0x00, 0x6F, 0x00, 0x00, 0x00 })]
    [InlineData(0x00000074, new byte[] { 0x08, 0x00, 0x06, 0x00, 0x74, 0x00, 0x00, 0x00 })]
    [InlineData(0x00000000, new byte[] { 0x08, 0x00, 0x06, 0x00, 0x00, 0x00, 0x00, 0x00 })]
    [InlineData(0x12345678, new byte[] { 0x08, 0x00, 0x06, 0x00, 0x78, 0x56, 0x34, 0x12 })]
    public void BuildStartAckPacket_WireBytesArePinned(int startId, byte[] expected)
    {
        var wire = SectorEnterDriver.BuildStartAckPacket(startId).ToWireBytes();
        Assert.Equal(expected, wire);
    }

    [Fact]
    public void BuildStartAckPacket_EchoesStartIdFromStartFrame()
    {
        // The id we send back must be exactly the one the server put in the
        // 0x0005 START frame (PlayerConnection::SendStart writes it as int32 LE);
        // a round-trip through the wire must preserve it bit-for-bit.
        const int startId = unchecked((int)0xDEADBEEF);
        var wire = SectorEnterDriver.BuildStartAckPacket(startId).ToWireBytes();
        int echoed = BinaryPrimitives.ReadInt32LittleEndian(wire.AsSpan(PacketHeader.WireSize, 4));
        Assert.Equal(startId, echoed);
    }
}
