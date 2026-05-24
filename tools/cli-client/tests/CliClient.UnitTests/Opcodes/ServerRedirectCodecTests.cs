// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// New code; project default license (LICENSES/enb-emulator).

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

    [Fact]
    public void Decode_ParsesSectorIdBigEndian()
    {
        // sector_id = 0x12345678, ip_address = 10.0.0.1 (0x0A000001), port = 3500 (0x0DAC)
        byte[] payload = new byte[]
        {
            0x12, 0x34, 0x56, 0x78,             // sector_id (big-endian)
            0x0A, 0x00, 0x00, 0x01,             // ip_address (big-endian = network order)
            0xAC, 0x0D                          // port (LITTLE-endian — see codec comment)
        };

        var codec = new ServerRedirectCodec();
        var result = (ServerRedirect)codec.DecodeInbound(payload);

        Assert.Equal(0x12345678, result.SectorId);
        Assert.Equal(IPAddress.Parse("10.0.0.1"), result.ServerEndPoint.Address);
        Assert.Equal(3500, result.ServerEndPoint.Port);
    }

    [Fact]
    public void Decode_HandlesProxyLocalRedirect()
    {
        // Real-world ServerRedirect from proxy/ClientToMasterServer.cpp:
        // proxy redirects to its own m_IpAddress + PROXY_LOCAL_TCP_PORT.
        // sector_id = 1 (Earth sector), ip = 127.0.0.1, port = 3500.
        byte[] payload = new byte[]
        {
            0x00, 0x00, 0x00, 0x01,             // sector 1
            0x7F, 0x00, 0x00, 0x01,             // 127.0.0.1
            0xAC, 0x0D                          // 3500 little-endian
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
