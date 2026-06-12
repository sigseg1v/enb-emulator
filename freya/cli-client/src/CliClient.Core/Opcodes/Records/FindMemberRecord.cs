// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya

using System.Text;

namespace N7.CliClient.Opcodes.Records;

/// <summary>
/// 0x0053 FIND_MEMBER. Wire (struct FindMember, PacketStructures.h:1085):
///   int32 count (LE);
///   count x { int32 GameID; int32 Level; int32 Race; int32 Profession } -- all BIG-ENDIAN.
/// Total = 4 + count*16 bytes. Emitter Player::SendFindMember
/// (PlayerConnection.cpp:11228); the per-item fields are filled via ntohl()
/// (PlayerManager.cpp:1292) so they are reversed on the wire, but `count`
/// itself is host-order LE (PlayerManager.cpp:1284).
/// </summary>
public sealed class FindMemberRecord : PacketRecord
{
    public FindMemberRecord(ReadOnlySpan<byte> payload) : base(0x0053, payload) { }
    protected override void WriteFields(StringBuilder sb)
    {
        if (Payload.Length < 4) { Flag(sb, $"FIND_MEMBER truncated -- {Payload.Length} bytes, expected >= 4"); return; }
        int count = ReadI32LE(Payload, 0);
        FDec(sb, 0, "Count", count);
        if (count < 0 || 4 + count * 16 > Payload.Length)
        {
            Flag(sb, $"FIND_MEMBER count={count} needs {4 + count * 16L} bytes, have {Payload.Length}");
            return;
        }
        for (int i = 0; i < count; i++)
        {
            int off = 4 + i * 16;
            FHex(sb, off,      $"[{i}].GameID",     ReadI32BE(Payload, off),      "(BE)");
            FDec(sb, off + 4,  $"[{i}].Level",      ReadI32BE(Payload, off + 4),  "(BE)");
            FDec(sb, off + 8,  $"[{i}].Race",       ReadI32BE(Payload, off + 8),  "(BE)");
            FDec(sb, off + 12, $"[{i}].Profession", ReadI32BE(Payload, off + 12), "(BE)");
        }
    }
}
