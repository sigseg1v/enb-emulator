// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// New code; project default license (LICENSES/enb-emulator).

using System.Buffers.Binary;
using System.Net;

namespace N7.CliClient.Opcodes.Inbound;

/// <summary>
/// 0x0036 SERVER_REDIRECT — Master server tells the client which
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
/// <c>common/include/net7/PacketStructures.h:298-304</c> as built by
/// <c>proxy/ClientToMasterServer.cpp::SendServerRedirect</c>):
/// </para>
/// <code>
///   offset  type    field        endianness
///   0       int32   sector_id    big-endian  (server uses ntohl-of-host-value)
///   4       int32   ip_address   big-endian  (same; conveniently network byte order)
///   8       int16   port         little-endian (HOST order; no htons)
///   total: 10 bytes
/// </code>
/// <para>
/// The port field is a known asymmetry — every other multi-byte field
/// in this struct is big-endian, but the C++ code does NOT call
/// <c>htons</c> on port. So on the wire, port stays in x86 little-endian.
/// If we ever see a big-endian server we'll need to revisit this, but
/// for now matching the C++ byte-for-byte is the only thing that works.
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

        int sectorId = BinaryPrimitives.ReadInt32BigEndian(payload[..4]);
        int ipNetOrder = BinaryPrimitives.ReadInt32BigEndian(payload[4..8]);
        ushort port = BinaryPrimitives.ReadUInt16LittleEndian(payload[8..10]);

        // ipNetOrder is the raw 32-bit address read in network byte
        // order (same order IPAddress(byte[]) expects, which is also
        // network order). Pulling it back to bytes preserves that.
        Span<byte> ipBytes = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(ipBytes, ipNetOrder);

        var endpoint = new IPEndPoint(new IPAddress(ipBytes.ToArray()), port);
        return new ServerRedirect(sectorId, endpoint);
    }

    public byte[] EncodeOutbound(object message)
        => throw new NotSupportedException(
            "ServerRedirect is server-to-client only; the client never originates it");
}
