// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Buffers.Binary;
using N7.CliClient.Net;
using Xunit;

namespace N7.CliClient.UnitTests.Net;

public sealed class MvasClientTests
{
    // EnbUdpHeader (common/include/net7/PacketStructures.h):
    //   short size; short opcode; int32 player_id; int32 packet_sequence  (12 bytes)
    // size = total datagram length; payload = pos[3] (+ heading[3] when present).
    // Server reads heading only when datagram size > 28 (UDP_MVAS.cpp).

    [Fact]
    public void BuildDatagram_PositionOnly_Is24BytesByteExact()
    {
        byte[] dg = MvasClient.BuildDatagram(
            playerId: 0x40000029, sequence: 7, x: 1.5f, y: -2.0f, z: 3.25f);

        Assert.Equal(24, dg.Length);                                       // 12 header + 12 payload
        Assert.Equal(24, BinaryPrimitives.ReadInt16LittleEndian(dg.AsSpan(0, 2)));  // size = total
        Assert.Equal(0x1004, BinaryPrimitives.ReadUInt16LittleEndian(dg.AsSpan(2, 2)));
        Assert.Equal(0x40000029, BinaryPrimitives.ReadInt32LittleEndian(dg.AsSpan(4, 4)));
        Assert.Equal(7, BinaryPrimitives.ReadInt32LittleEndian(dg.AsSpan(8, 4)));
        Assert.Equal(1.5f, BinaryPrimitives.ReadSingleLittleEndian(dg.AsSpan(12, 4)));
        Assert.Equal(-2.0f, BinaryPrimitives.ReadSingleLittleEndian(dg.AsSpan(16, 4)));
        Assert.Equal(3.25f, BinaryPrimitives.ReadSingleLittleEndian(dg.AsSpan(20, 4)));

        // Size 24 is NOT > 28, so the server treats this as position-only.
        Assert.False(BinaryPrimitives.ReadInt16LittleEndian(dg.AsSpan(0, 2)) > 28);
    }

    [Fact]
    public void BuildDatagram_WithHeading_Is36Bytes_AndCrossesServerHeadingThreshold()
    {
        byte[] dg = MvasClient.BuildDatagram(
            playerId: 1, sequence: 1, x: 0, y: 0, z: 0, heading: (0.1f, 0.2f, 0.3f));

        Assert.Equal(36, dg.Length);                                       // 12 + 24
        Assert.Equal(36, BinaryPrimitives.ReadInt16LittleEndian(dg.AsSpan(0, 2)));
        Assert.True(BinaryPrimitives.ReadInt16LittleEndian(dg.AsSpan(0, 2)) > 28); // server reads heading
        Assert.Equal(0.1f, BinaryPrimitives.ReadSingleLittleEndian(dg.AsSpan(24, 4)));
        Assert.Equal(0.2f, BinaryPrimitives.ReadSingleLittleEndian(dg.AsSpan(28, 4)));
        Assert.Equal(0.3f, BinaryPrimitives.ReadSingleLittleEndian(dg.AsSpan(32, 4)));
    }
}
