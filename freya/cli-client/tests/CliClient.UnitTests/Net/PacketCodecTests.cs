// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using N7.CliClient.Net;
using Xunit;

namespace N7.CliClient.UnitTests.Net;

public sealed class PacketCodecTests
{
    [Fact]
    public void Header_RoundTrips_LittleEndian()
    {
        var header = new PacketHeader(Size: 0x1234, Opcode: 0x5678);
        byte[] buf = new byte[PacketHeader.WireSize];

        header.Write(buf);

        Assert.Equal(0x34, buf[0]);
        Assert.Equal(0x12, buf[1]);
        Assert.Equal(0x78, buf[2]);
        Assert.Equal(0x56, buf[3]);

        var decoded = PacketHeader.Read(buf);
        Assert.Equal(header, decoded);
    }

    [Fact]
    public void Header_PayloadLength_IsSizeMinusWireSize()
    {
        var header = new PacketHeader(Size: 100, Opcode: 0x0035);
        Assert.Equal(96, header.PayloadLength);
    }

    [Fact]
    public void Header_Read_ThrowsOnShortBuffer()
    {
        Assert.Throws<ArgumentException>(() => PacketHeader.Read(new byte[3]));
    }

    [Fact]
    public void Header_Write_ThrowsOnShortBuffer()
    {
        var header = new PacketHeader(Size: 4, Opcode: 0);
        Assert.Throws<ArgumentException>(() => header.Write(new byte[3]));
    }

    [Fact]
    public void Packet_ForOpcode_ComputesTotalSize()
    {
        byte[] payload = new byte[] { 1, 2, 3, 4, 5 };
        var packet = Packet.ForOpcode(opcode: 0x0035, payload);

        Assert.Equal(9, packet.Header.Size);
        Assert.Equal(0x0035, packet.Header.Opcode);
        Assert.Equal(5, packet.Header.PayloadLength);
        Assert.True(payload.AsSpan().SequenceEqual(packet.Payload.Span));
    }

    [Fact]
    public void Packet_ForOpcode_AcceptsEmptyPayload()
    {
        var packet = Packet.ForOpcode(opcode: 0x0000, ReadOnlyMemory<byte>.Empty);

        Assert.Equal(PacketHeader.WireSize, packet.Header.Size);
        Assert.Equal(0, packet.Header.PayloadLength);
        Assert.Equal(0, packet.Payload.Length);
    }

    [Fact]
    public void Packet_ToWireBytes_RoundTrips()
    {
        byte[] payload = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };
        var packet = Packet.ForOpcode(opcode: 0x0036, payload);

        byte[] wire = packet.ToWireBytes();

        Assert.Equal(PacketHeader.WireSize + payload.Length, wire.Length);

        var decodedHeader = PacketHeader.Read(wire);
        Assert.Equal(packet.Header, decodedHeader);

        var decodedPayload = wire.AsSpan(PacketHeader.WireSize);
        Assert.True(payload.AsSpan().SequenceEqual(decodedPayload));
    }
}
