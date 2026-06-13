// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya

using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;

namespace N7.CliClient.IntegrationTests.Net;

/// <summary>
/// Test-side producer for the proxy's MVAS position feed -- the exact transport
/// the in-client position hook (freya/client-injection/FreyaPosFeed.dll) uses.
/// It sends a fixed 40-byte <c>FreyaClientPosDatagram</c>
/// (freya/client-injection/ClientPositionShared.h) to the proxy's loopback
/// intake port (<c>FREYA_CLIENT_POS_PORT</c> 3807, published by docker-compose
/// as 127.0.0.1:3807), and the proxy
/// (<c>UDPClient::ReadClientShipPosition</c> -> <c>SendPositionIfChanged</c>)
/// re-emits it to the server as 0x1004 MVAS_SEND_POSITION FROM THE PROXY'S OWN
/// socket.
///
/// <para>
/// Why this and not a direct <c>SectorUdpClient</c> MVAS feed: a datagram sent
/// straight to the server's MVAS port 3806 arrives from the docker-gateway IP,
/// and <c>HandleMVASPosReturn</c> (UDP_MVAS.cpp) calls <c>SetPlayerPortIP</c>
/// UNCONDITIONALLY -- overwriting the player's <c>m_Player_IPAddr</c> to the
/// gateway. Subsequent sector opcodes relayed THROUGH the proxy (proxy IP) then
/// fail the server's anti-spoof guard (<c>PlayerIPAddr() == source_addr</c>,
/// UDP_Client.cpp) and are dropped. Routing position through the proxy's 3807
/// intake instead keeps the 0x1004 source == the proxy == the avatar's
/// registered IP, so the guard holds and the player's whole sector stream stays
/// coherent -- exactly as it does for the real Win32 client. No server change,
/// no weakened guard.
/// </para>
/// </summary>
public sealed class ProxyPositionFeed : IDisposable
{
    // freya/client-injection/ClientPositionShared.h
    private const int   PosPort = 3807;          // FREYA_CLIENT_POS_PORT
    private const uint  Magic   = 0x4E37504Fu;   // FREYA_CLIENT_POS_MAGIC ('N7PO')

    private readonly UdpClient _udp;
    private readonly IPEndPoint _dst;
    private uint _seq;

    /// <param name="host">Host the proxy's 3807 intake is published on
    /// (ServerFixture.SectorHost == 127.0.0.1).</param>
    public ProxyPositionFeed(string host)
    {
        _udp = new UdpClient();
        _dst = new IPEndPoint(IPAddress.Parse(host), PosPort);
    }

    /// <summary>
    /// Send one position sample. The proxy keeps the latest valid datagram and
    /// only re-emits 0x1004 to the server when the position actually CHANGES, so
    /// callers must vary <paramref name="pos"/> each tick to keep the feed live.
    /// </summary>
    public void Send((float X, float Y, float Z) pos, (float X, float Y, float Z) heading, int sectorId)
    {
        // 40-byte FreyaClientPosDatagram, little-endian (x86 LE wire struct):
        //   u32 magic; u32 seq; f32 position[3]; f32 heading[3]; u32 sector_id; u32 valid;
        var buf = new byte[40];
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(0, 4), Magic);
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(4, 4), ++_seq);
        BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(8, 4),  pos.X);
        BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(12, 4), pos.Y);
        BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(16, 4), pos.Z);
        BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(20, 4), heading.X);
        BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(24, 4), heading.Y);
        BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(28, 4), heading.Z);
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(32, 4), (uint)sectorId);
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(36, 4), 1u); // valid = live in-space sample
        _udp.Send(buf, buf.Length, _dst);
    }

    public void Dispose() => _udp.Dispose();
}
