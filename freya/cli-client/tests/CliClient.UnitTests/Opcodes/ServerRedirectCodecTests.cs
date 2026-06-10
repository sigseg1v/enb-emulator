// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Net;
using N7.CliClient.Opcodes;
using N7.CliClient.Opcodes.Inbound;
using Xunit;

namespace N7.CliClient.UnitTests.Opcodes;

public sealed class ServerRedirectCodecTests
{
    [Fact]
    public void Opcode_Is_0x0036()
    {
        var codec = new ServerRedirectCodec();
        Assert.Equal(OpcodeId.Known.ServerRedirect, codec.Opcode);
        Assert.Equal(0x0036, codec.Opcode.Value);
    }

    /// <summary>
    /// Synthetic round-trip: sector_id, ip_address, and port all read as
    /// little-endian on the wire (the proxy memcpy's a packed struct
    /// whose ints are already in the form the client expects).
    /// </summary>
    [Fact]
    public void Decode_ParsesAllFieldsLittleEndian()
    {
        // sector_id = 0x12345678, ip = 10.0.0.1, port = 3500.
        // Bytes laid out as the wire memcpy would store them on x86 LE.
        byte[] payload = new byte[]
        {
            0x78, 0x56, 0x34, 0x12,             // sector_id LE -> 0x12345678
            0x01, 0x00, 0x00, 0x0A,             // ip LE int 0x0A000001 -> 10.0.0.1
            0xAC, 0x0D,                         // port LE -> 0x0DAC = 3500
        };

        var codec = new ServerRedirectCodec();
        var result = (ServerRedirect)codec.DecodeInbound(payload);

        Assert.Equal(0x12345678, result.SectorId);
        Assert.Equal(IPAddress.Parse("10.0.0.1"), result.ServerEndPoint.Address);
        Assert.Equal(3500, result.ServerEndPoint.Port);
    }

    /// <summary>
    /// Real packet capture from
    /// archive/kyp-snapshot/capturedPackets/capture_2.rar frame 222
    /// (retail Net-7 master server, 2006). The exact 10 payload bytes
    /// the client decoded into "connect to 159.153.232.97:3501 for
    /// sector 10601 (Aragoth)". Pins our codec against ground truth.
    /// </summary>
    [Fact]
    public void Decode_MatchesRetailCapture_Capture2Frame222()
    {
        byte[] payload = new byte[]
        {
            0x69, 0x29, 0x00, 0x00,             // sector_id LE = 0x2969 = 10601 Aragoth
            0x61, 0xE8, 0x99, 0x9F,             // ip LE int 0x9F99E861 -> 159.153.232.97
            0xAD, 0x0D,                         // port LE = 0x0DAD = 3501
        };

        var codec = new ServerRedirectCodec();
        var result = (ServerRedirect)codec.DecodeInbound(payload);

        Assert.Equal(10601, result.SectorId);
        Assert.Equal(IPAddress.Parse("159.153.232.97"), result.ServerEndPoint.Address);
        Assert.Equal(3501, result.ServerEndPoint.Port);
    }

    /// <summary>
    /// Local-proxy round-trip: when the proxy is bound on 127.0.0.1,
    /// its m_IpAddress is the inet_addr() value 0x0100007F (network
    /// order). SendServerRedirect runs that through ntohl() and the
    /// memcpy puts the post-swap LE-int bytes 01 00 00 7F on the wire.
    /// The codec must round-trip that to 127.0.0.1 so the launcher
    /// actually connects to the proxy's own sector port.
    /// </summary>
    [Fact]
    public void Decode_HandlesProxyLocalRedirect()
    {
        byte[] payload = new byte[]
        {
            0x01, 0x00, 0x00, 0x00,             // sector_id LE = 1 (Earth)
            0x01, 0x00, 0x00, 0x7F,             // ip LE int 0x7F000001 -> 127.0.0.1
            0xAC, 0x0D,                         // port LE = 3500
        };

        var codec = new ServerRedirectCodec();
        var result = (ServerRedirect)codec.DecodeInbound(payload);

        Assert.Equal(1, result.SectorId);
        Assert.Equal(IPAddress.Loopback, result.ServerEndPoint.Address);
        Assert.Equal(3500, result.ServerEndPoint.Port);
    }

    [Fact]
    public void Decode_ShortPayload_Throws()
    {
        var codec = new ServerRedirectCodec();
        Assert.Throws<InvalidDataException>(
            () => codec.DecodeInbound(new byte[ServerRedirectCodec.WireSize - 1]));
    }

    [Fact]
    public void EncodeOutbound_Throws_ClientNeverSends()
    {
        var codec = new ServerRedirectCodec();
        Assert.Throws<NotSupportedException>(() => codec.EncodeOutbound(new object()));
    }
}
