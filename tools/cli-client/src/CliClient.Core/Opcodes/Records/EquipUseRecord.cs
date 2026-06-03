// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Text;

namespace N7.CliClient.Opcodes.Records;

/// <summary>
/// EQUIP_USE / UseInventoryItem (0x005D, client-&gt;server) -- the player manually
/// activates an equipped item (a fire button / device toggle). Wire (struct
/// EquipUse, 6 bytes): int32 GameID, char InvNum, char InvSlot. The consumer,
/// Player::HandleEquipUse (PlayerConnection.cpp:4556), reads ONE field --
/// m_Equip[myUse-&gt;InvSlot].ManualActivate() -- InvSlot is a raw char index into the
/// equipment array, so the part the server acts on is byte-order-independent. GameID
/// is the activated avatar/ship game id; the handler does NOT read it, and it is
/// decoded LITTLE-ENDIAN because that is the only sane id range: the captured frame's
/// GameID is 0x003ACEB4 = 3854004 little-endian (big-endian would be 0xB4CE3A00,
/// ~3 billion, outside any world id range), and it is the IDENTICAL id carried by the
/// two 0x58 SKILL_ABILITY frames immediately bracketing this packet in the same
/// 159.153.232.99:3367 session (one of them the skillability_index_44 fixture, also
/// decoded little-endian) -- same ship, same session. InvNum/InvSlot are single bytes
/// (order-independent). Source: struct EquipUse (PacketStructures.h:929),
/// Player::HandleEquipUse (PlayerConnection.cpp:4556). Pinned to capture_3.rar.
/// </summary>
public sealed class EquipUseRecord : PacketRecord
{
    public EquipUseRecord(ReadOnlySpan<byte> payload) : base(0x005D, payload) { }

    protected override void WriteFields(StringBuilder sb)
    {
        if (Payload.Length < 6) { Flag(sb, $"EQUIP_USE truncated -- {Payload.Length} bytes, expected 6"); return; }

        FHex(sb, 0, "GameID", ReadI32LE(Payload, 0),
            "(LE; activated avatar/ship id -- not consumed by HandleEquipUse; identical to the bracketing 0x58 frames in this session)");
        FDec(sb, 4, "InvNum", Payload[4],
            "(inventory page/number -- not consumed by HandleEquipUse)");
        FDec(sb, 5, "InvSlot", Payload[5],
            "(equipment slot -- HandleEquipUse calls m_Equip[InvSlot].ManualActivate())");

        if (Payload.Length > 6) Flag(sb, $"EQUIP_USE has {Payload.Length - 6} trailing bytes");
    }
}
