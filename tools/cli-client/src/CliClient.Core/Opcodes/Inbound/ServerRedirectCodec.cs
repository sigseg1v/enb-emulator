// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Buffers.Binary;
using System.Net;

namespace N7.CliClient.Opcodes.Inbound;

/// <summary>
/// 0x0036 SERVER_REDIRECT -- Master server tells the client which
/// sector server (IP + port) to TCP-connect to next.
/// </summary>
/// <param name="SectorId">Target sector ID.</param>
/// <param name="ServerEndPoint">IP + port of the sector server to
/// connect to.</param>
public sealed record ServerRedirect(int SectorId, IPEndPoint ServerEndPoint);

/// <summary>
/// Codec for opcode 0x0036 SERVER_REDIRECT.
/// </summary>
/// <remarks>
/// <para>
/// Wire format (mirrors <c>struct ServerRedirect</c> in
/// <c>common/include/net7/PacketStructures.h:350-356</c> as built by
/// <c>proxy/ClientToMasterServer.cpp::SendServerRedirect</c>):
/// </para>
/// <code>
///   offset  type    field        endianness
///   0       int32   sector_id    little-endian (host-order int memcpy'd on x86)
///   4       int32   ip_address   little-endian (int whose host value is the
///                                 network-order IP -- inet_ntoa(s_addr=value)
///                                 prints the right dotted form)
///   8       int16   port         little-endian (HOST order; no htons)
///   total: 10 bytes
/// </code>
/// <para>
/// All three fields are little-endian on the wire because the C++
/// server memcpy's a packed <c>struct ServerRedirect</c> directly. The
/// fields' int values are pre-byte-swapped where the source was in
/// network order (ip_address), and passed straight through where the
/// source was already host order (sector_id, port).
/// </para>
/// <para>
/// Real-server confirmation (LE on wire for all three fields):
/// <c>archive/kyp-snapshot/capturedPackets/capture_1.rar</c> frames
/// 222 / 656 / 1062 and <c>capture_2.rar</c> frame 222. Each shows a
/// destination IP / sector / port whose decoded value matches the
/// client's follow-up TCP connect only when the bytes are read LE.
/// </para>
/// </remarks>
public sealed class ServerRedirectCodec : IOpcodeCodec
{
    /// <summary>Fixed wire size of the ServerRedirect payload.</summary>
    public const int WireSize = 10;

    public OpcodeId Opcode => OpcodeId.Known.ServerRedirect;

    public object DecodeInbound(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < WireSize)
        {
            throw new InvalidDataException(
                $"ServerRedirect payload is {payload.Length} bytes, expected {WireSize}");
        }

        int sectorId = BinaryPrimitives.ReadInt32LittleEndian(payload[..4]);
        // ip_address: the wire bytes are the LE storage of a 32-bit int
        // whose VALUE, fed into a Win32 sockaddr_in.s_addr, prints the
        // right dotted-IP via inet_ntoa. inet_ntoa internally ntohl's
        // s_addr to print high-octet first; on a little-endian client
        // that gives bytes-as-read-from-wire reversed. To recover the
        // human-readable IP here we read LE -> int -> write BE bytes ->
        // hand to IPAddress (which treats input as network order). The
        // net effect is "reverse the four wire bytes."
        int ipValue = BinaryPrimitives.ReadInt32LittleEndian(payload[4..8]);
        ushort port = BinaryPrimitives.ReadUInt16LittleEndian(payload[8..10]);

        Span<byte> ipBytes = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(ipBytes, ipValue);

        var endpoint = new IPEndPoint(new IPAddress(ipBytes.ToArray()), port);
        return new ServerRedirect(sectorId, endpoint);
    }

    public byte[] EncodeOutbound(object message)
        => throw new NotSupportedException(
            "ServerRedirect is server-to-client only; the client never originates it");
}
