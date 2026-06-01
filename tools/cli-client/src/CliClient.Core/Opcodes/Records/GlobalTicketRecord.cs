// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Buffers.Binary;
using System.Text;

namespace N7.CliClient.Opcodes.Records;

/// <summary>
/// 0x006F GLOBAL_TICKET. Wire shape mirrors
/// <c>N7.CliClient.Opcodes.Inbound.GlobalTicketCodec</c>: 68 bytes;
/// response_code (BE), embedded MasterJoin (avatar_id BE, sector_id BE,
/// level host, 16-byte ticket string).
/// </summary>
public sealed class GlobalTicketRecord : PacketRecord
{
    public GlobalTicketRecord(ReadOnlySpan<byte> payload) : base(0x006F, payload) { }

    protected override void WriteFields(StringBuilder sb)
    {
        if (Payload.Length < 68)
        {
            Flag(sb, $"GLOBAL_TICKET truncated -- {Payload.Length} bytes, expected 68");
            return;
        }
        int responseCode = BinaryPrimitives.ReadInt32BigEndian(Payload.AsSpan(0, 4));
        int avatarId     = BinaryPrimitives.ReadInt32BigEndian(Payload.AsSpan(20, 4));
        int sectorId     = BinaryPrimitives.ReadInt32BigEndian(Payload.AsSpan(24, 4));
        int level        = BinaryPrimitives.ReadInt32LittleEndian(Payload.AsSpan(32, 4));
        string ticket    = ReadNulString(Payload.AsSpan(48, 16));

        FieldDec(sb, "ResponseCode", responseCode,
            responseCode == 0 ? "(success)" :
            responseCode == 1002 ? "(galaxy-full sentinel)" :
            responseCode == 1000 ? "(user-not-authorised)" : null);
        if (responseCode != 0)
            Flag(sb, $"non-zero ResponseCode ({responseCode}) -- client will abort the join");
        FieldHex(sb, "AvatarID", avatarId);
        if (avatarId == 0x40000000)
            Flag(sb, "AvatarID == 0x40000000 (proxy failure sentinel)");
        FieldHex(sb, "SectorID", sectorId);
        FieldDec(sb, "Level", level);
        FieldString(sb, "Ticket", ticket, requiredNonEmpty: true);
    }
}
