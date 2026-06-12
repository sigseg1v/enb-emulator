// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya

using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;

namespace N7.CliClient.Net;

/// <summary>
/// Minimal MVAS (Move-Assist) UDP position emitter.
/// </summary>
/// <remarks>
/// <para>
/// The retail position channel is a plaintext UDP datagram wrapped in an
/// <c>EnbUdpHeader</c>. In the live stack the Net7 proxy emits it -- it scrapes
/// the Win32 client's engine position out of process memory
/// (<c>proxy/UDPProxyMVAS.cpp</c>) and relays it to the MVAS server port. A
/// headless client has no engine, so it computes and feeds position itself.
/// </para>
/// <para>
/// Wire format is byte-identical to the proxy/server emitter -- the 12-byte
/// header <c>{ short size; short opcode; int32 player_id; int32 sequence }</c>
/// from <c>common/include/net7/PacketStructures.h</c> followed by the payload
/// (<c>proxy/UDPClient_linux.cpp</c> SendResponse, <c>server/src/UDPConnection.cpp</c>
/// SendOpcode). <c>size</c> is the total datagram length. No encryption (the
/// server reads raw floats in <c>UDP_MVAS.cpp</c> HandleMVASPosReturn). This is
/// NOT a server change: the server keys the update on the <c>player_id</c> in
/// the header, exactly as it does for the proxy's datagrams.
/// </para>
/// </remarks>
public sealed class MvasClient : IDisposable
{
    /// <summary>MVAS_LOGIN_PORT (common/include/net7/Ports.h).</summary>
    public const int DefaultPort = 3806;

    private const int HeaderSize = 12;                // sizeof(EnbUdpHeader)
    private const ushort SendPositionOpcode = 0x1004; // ENB_OPCODE_1004_MVAS_SEND_POSITION_C_S
    private const ushort CommsAliveOpcode = 0x3005;   // ENB_OPCODE_3005_PLAYER_COMMS_ALIVE

    private readonly Socket _sock;
    private readonly IPEndPoint _ep;
    private int _sequence;

    public MvasClient(string host, int port = DefaultPort)
    {
        ArgumentException.ThrowIfNullOrEmpty(host);
        IPAddress ip = IPAddress.TryParse(host, out var parsed)
            ? parsed
            : Dns.GetHostAddresses(host).First(a => a.AddressFamily == AddressFamily.InterNetwork);
        _ep = new IPEndPoint(ip, port);
        _sock = new Socket(_ep.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
    }

    /// <summary>
    /// Build (without sending) one MVAS_SEND_POSITION datagram. Position-only
    /// is a 12-byte payload; with a heading it is 24 bytes (the server reads
    /// the heading only when the datagram size exceeds 28 -- i.e. payload 24).
    /// </summary>
    public static byte[] BuildDatagram(
        int playerId, int sequence, float x, float y, float z,
        (float X, float Y, float Z)? heading = null)
    {
        int payloadLen = heading is null ? 12 : 24;
        byte[] dg = new byte[HeaderSize + payloadLen];
        BinaryPrimitives.WriteInt16LittleEndian(dg.AsSpan(0, 2), (short)(HeaderSize + payloadLen));
        BinaryPrimitives.WriteUInt16LittleEndian(dg.AsSpan(2, 2), SendPositionOpcode);
        BinaryPrimitives.WriteInt32LittleEndian(dg.AsSpan(4, 4), playerId);
        BinaryPrimitives.WriteInt32LittleEndian(dg.AsSpan(8, 4), sequence);
        BinaryPrimitives.WriteSingleLittleEndian(dg.AsSpan(12, 4), x);
        BinaryPrimitives.WriteSingleLittleEndian(dg.AsSpan(16, 4), y);
        BinaryPrimitives.WriteSingleLittleEndian(dg.AsSpan(20, 4), z);
        if (heading is { } h)
        {
            BinaryPrimitives.WriteSingleLittleEndian(dg.AsSpan(24, 4), h.X);
            BinaryPrimitives.WriteSingleLittleEndian(dg.AsSpan(28, 4), h.Y);
            BinaryPrimitives.WriteSingleLittleEndian(dg.AsSpan(32, 4), h.Z);
        }
        return dg;
    }

    /// <summary>Send one position (and optional heading) update.</summary>
    public void SendPosition(int playerId, float x, float y, float z,
        (float X, float Y, float Z)? heading = null)
    {
        byte[] dg = BuildDatagram(
            playerId, Interlocked.Increment(ref _sequence), x, y, z, heading);
        _sock.SendTo(dg, _ep);
    }

    /// <summary>
    /// Build (without sending) one COMMS_ALIVE keepalive datagram. This is the
    /// 12-byte <c>EnbUdpHeader</c> with NO payload (<c>size</c> = 12): the
    /// retail client pings the MVAS port with this on a periodic timer so the
    /// server keeps the avatar in-world while it is idle. The server keys the
    /// refresh on the header's <c>player_id</c> alone -- it reads no body
    /// (proxy/UDPProxyToClient_linux.cpp SendCommsAlive emits NULL/0;
    /// server/src/UDP_MVAS.cpp HandleKeepCommsAlive reads only hdr->player_id
    /// and calls SetLastAccessTime).
    /// </summary>
    public static byte[] BuildCommsAlive(int playerId, int sequence)
    {
        byte[] dg = new byte[HeaderSize];
        BinaryPrimitives.WriteInt16LittleEndian(dg.AsSpan(0, 2), HeaderSize);
        BinaryPrimitives.WriteUInt16LittleEndian(dg.AsSpan(2, 2), CommsAliveOpcode);
        BinaryPrimitives.WriteInt32LittleEndian(dg.AsSpan(4, 4), playerId);
        BinaryPrimitives.WriteInt32LittleEndian(dg.AsSpan(8, 4), sequence);
        return dg;
    }

    /// <summary>Send one COMMS_ALIVE keepalive ping for the player.</summary>
    public void SendCommsAlive(int playerId)
    {
        byte[] dg = BuildCommsAlive(playerId, Interlocked.Increment(ref _sequence));
        _sock.SendTo(dg, _ep);
    }

    public void Dispose() => _sock.Dispose();
}
