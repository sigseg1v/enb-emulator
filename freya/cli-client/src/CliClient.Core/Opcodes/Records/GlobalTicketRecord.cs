// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya

using System.Text;

namespace N7.CliClient.Opcodes.Records;

using System.Buffers.Binary;
/// <summary>
/// 0x006F GLOBAL_TICKET. Wire (68 bytes): response_code BE int32 at 0;
/// AvatarID BE int32 at 20; SectorID BE int32 at 24; Level LE int32 at 32;
/// Ticket NUL-string at 48 (16 bytes).
/// </summary>
public sealed class GlobalTicketRecord : PacketRecord
{
    public GlobalTicketRecord(ReadOnlySpan<byte> payload) : base(0x006F, payload) { }
    protected override void WriteFields(StringBuilder sb)
    {
        if (Payload.Length < 68) { Flag(sb, $"GLOBAL_TICKET truncated -- {Payload.Length} bytes, expected 68"); return; }
        int    responseCode = BinaryPrimitives.ReadInt32BigEndian(Payload.AsSpan(0, 4));
        int    avatarId     = BinaryPrimitives.ReadInt32BigEndian(Payload.AsSpan(20, 4));
        int    sectorId     = BinaryPrimitives.ReadInt32BigEndian(Payload.AsSpan(24, 4));
        int    level        = BinaryPrimitives.ReadInt32LittleEndian(Payload.AsSpan(32, 4));
        string ticket       = ReadNulString(Payload.AsSpan(48, 16));
        FDec(sb, 0,  "ResponseCode", responseCode,
            responseCode == 0    ? "(success)" :
            responseCode == 1002 ? "(galaxy-full)" :
            responseCode == 1000 ? "(not-authorised)" : null);
        if (responseCode != 0) Flag(sb, $"non-zero ResponseCode ({responseCode}) -- client will abort the join");
        Mark(4, 16);   // 16 bytes between ResponseCode and AvatarID are unknown/padding
        FHex(sb, 20, "AvatarID",     avatarId);
        if (avatarId == 0x40000000) Flag(sb, "AvatarID == 0x40000000 (proxy failure sentinel)");
        FHex(sb, 24, "SectorID",     sectorId);
        Mark(28, 4);   // 4 bytes between SectorID and Level are unknown
        FDec(sb, 32, "Level",        level);
        Mark(36, 12);  // 12 bytes between Level and Ticket are unknown
        FStr(sb, 48, 16, "Ticket",   ticket, required: true);
    }
}
