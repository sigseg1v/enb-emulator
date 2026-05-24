// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// New code; project default license (LICENSES/enb-emulator).

using System.Buffers.Binary;
using System.Text;

namespace N7.CliClient.Opcodes.Outbound;

/// <summary>
/// 0x0035 MASTER_JOIN — the first packet a client sends to the master
/// server after the RSA+RC4 handshake on the master TCP port. It tells
/// the server which avatar wants in, which sector, and supplies the
/// 20-byte auth ticket from /AuthLogin.
/// </summary>
/// <remarks>
/// Mirrors <c>struct MasterJoin</c> in
/// <c>common/include/net7/PacketStructures.h:262-282</c>. All 11 int32
/// fields are wire-big-endian (server uses <c>ntohl</c> to read them).
/// The 20-byte ticket is ASCII bytes, copied verbatim — no byte-order
/// concept.
/// </remarks>
public sealed record MasterJoinRequest(
    int Unknown1,
    int Unknown2,
    int Unknown3,
    int AvatarIdMsb,
    int AvatarIdLsb,
    int ToSectorId,
    int FromSectorId,
    int PlayerLevel,
    int Unknown8,
    int Unknown9,
    int Unknown10,
    string Ticket);

/// <summary>
/// Codec for opcode 0x0035 MASTER_JOIN (client → master).
/// </summary>
public sealed class MasterJoinCodec : IOpcodeCodec
{
    /// <summary>Fixed wire size of the MasterJoin payload.</summary>
    public const int WireSize = 64;

    /// <summary>Length of the ticket field in bytes.</summary>
    public const int TicketLength = 20;

    public OpcodeId Opcode => OpcodeId.Known.MasterJoin;

    public object DecodeInbound(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < WireSize)
        {
            throw new InvalidDataException(
                $"MasterJoin payload is {payload.Length} bytes, expected {WireSize}");
        }

        int u1 = BinaryPrimitives.ReadInt32BigEndian(payload[0..4]);
        int u2 = BinaryPrimitives.ReadInt32BigEndian(payload[4..8]);
        int u3 = BinaryPrimitives.ReadInt32BigEndian(payload[8..12]);
        int avatarMsb = BinaryPrimitives.ReadInt32BigEndian(payload[12..16]);
        int avatarLsb = BinaryPrimitives.ReadInt32BigEndian(payload[16..20]);
        int toSector = BinaryPrimitives.ReadInt32BigEndian(payload[20..24]);
        int fromSector = BinaryPrimitives.ReadInt32BigEndian(payload[24..28]);
        int playerLevel = BinaryPrimitives.ReadInt32BigEndian(payload[28..32]);
        int u8 = BinaryPrimitives.ReadInt32BigEndian(payload[32..36]);
        int u9 = BinaryPrimitives.ReadInt32BigEndian(payload[36..40]);
        int u10 = BinaryPrimitives.ReadInt32BigEndian(payload[40..44]);

        ReadOnlySpan<byte> ticketBytes = payload.Slice(44, TicketLength);
        int nul = ticketBytes.IndexOf((byte)0);
        if (nul < 0) nul = ticketBytes.Length;
        string ticket = Encoding.ASCII.GetString(ticketBytes[..nul]);

        return new MasterJoinRequest(
            u1, u2, u3, avatarMsb, avatarLsb,
            toSector, fromSector, playerLevel,
            u8, u9, u10, ticket);
    }

    public byte[] EncodeOutbound(object message)
    {
        if (message is not MasterJoinRequest req)
            throw new ArgumentException(
                $"expected MasterJoinRequest, got {message?.GetType().Name ?? "null"}",
                nameof(message));

        byte[] buf = new byte[WireSize];
        Span<byte> span = buf;

        BinaryPrimitives.WriteInt32BigEndian(span[0..4], req.Unknown1);
        BinaryPrimitives.WriteInt32BigEndian(span[4..8], req.Unknown2);
        BinaryPrimitives.WriteInt32BigEndian(span[8..12], req.Unknown3);
        BinaryPrimitives.WriteInt32BigEndian(span[12..16], req.AvatarIdMsb);
        BinaryPrimitives.WriteInt32BigEndian(span[16..20], req.AvatarIdLsb);
        BinaryPrimitives.WriteInt32BigEndian(span[20..24], req.ToSectorId);
        BinaryPrimitives.WriteInt32BigEndian(span[24..28], req.FromSectorId);
        BinaryPrimitives.WriteInt32BigEndian(span[28..32], req.PlayerLevel);
        BinaryPrimitives.WriteInt32BigEndian(span[32..36], req.Unknown8);
        BinaryPrimitives.WriteInt32BigEndian(span[36..40], req.Unknown9);
        BinaryPrimitives.WriteInt32BigEndian(span[40..44], req.Unknown10);

        // Ticket: ASCII, zero-padded to 20 bytes.
        Span<byte> ticketSpan = span.Slice(44, TicketLength);
        ticketSpan.Clear();
        int copied = Encoding.ASCII.GetBytes(req.Ticket.AsSpan(), ticketSpan);
        if (copied > TicketLength)
        {
            throw new ArgumentException(
                $"ticket is {copied} bytes (or more), max {TicketLength}",
                nameof(message));
        }

        return buf;
    }
}
