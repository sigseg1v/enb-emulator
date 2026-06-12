// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya

using System.Buffers.Binary;

namespace N7.CliClient.Opcodes.Outbound;

/// <summary>
/// 0x005D EQUIP_USE / UseInventoryItem (client -&gt; server). The player manually
/// activates an equipped item -- the fire button on a weapon, the toggle on a
/// device. Wire layout is <c>struct EquipUse</c>
/// (common/include/net7/PacketStructures.h:929), 6 bytes:
/// <code>
///   int32_t GameID;   // activated avatar/ship id
///   char    InvNum;   // inventory page/number
///   char    InvSlot;  // equipment slot index
/// </code>
/// The server's Player::HandleEquipUse (server/src/PlayerConnection.cpp:4556)
/// reads ONLY <see cref="InvSlot"/> -- <c>m_Equip[InvSlot].ManualActivate()</c>.
/// GameID and InvNum are not consumed.
/// </summary>
/// <remarks>
/// <para>
/// <b>Byte order.</b> Unlike 0x0027 INVENTORY_MOVE (every field big-endian via
/// <c>ntohl</c>), HandleEquipUse casts the struct directly with NO byte-order
/// normalisation. <see cref="InvNum"/>/<see cref="InvSlot"/> are single bytes
/// (order-independent). <see cref="GameID"/> is LITTLE-endian on the wire: the
/// captured frames carry it as the host-order id (the same id the bracketing
/// 0x58 SKILL_ABILITY frames carry, also little-endian), and big-endian would
/// put it outside any world id range.
/// </para>
/// <para>
/// Byte-pinned against the live reference server captures:
/// KillLoot2 dg #3 (GameID 0x4003992A LE, InvNum=2, InvSlot=3 -- fire the weapon
/// in equip slot 3) and SkillTrainingHostileDevice2 dg #44 (same GameID/InvNum,
/// InvSlot=11 -- activate the device in equip slot 11). The inbound half is
/// <see cref="N7.CliClient.Opcodes.Records.EquipUseRecord"/>.
/// </para>
/// </remarks>
public sealed record EquipUseMessage(int GameId, byte InvNum, byte InvSlot)
{
    /// <summary>The equip-page number the retail client sends for an in-ship
    /// equipment slot. Captured value is 2 across every observed frame.</summary>
    public const byte DefaultInvNum = 2;

    /// <summary>Activate the item in <paramref name="invSlot"/> with the
    /// captured default inventory page (<see cref="DefaultInvNum"/>).</summary>
    public EquipUseMessage(int gameId, byte invSlot) : this(gameId, DefaultInvNum, invSlot) { }
}

/// <summary>Codec for opcode 0x005D EQUIP_USE (client -&gt; server).</summary>
public sealed class EquipUseCodec : IOpcodeCodec
{
    /// <summary>Fixed wire size of struct EquipUse.</summary>
    public const int Size = 6;

    public OpcodeId Opcode => OpcodeId.Known.EquipUse;

    public object DecodeInbound(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < Size)
        {
            throw new InvalidDataException(
                $"EquipUse payload is {payload.Length} bytes, expected {Size}");
        }

        return new EquipUseMessage(
            GameId:  BinaryPrimitives.ReadInt32LittleEndian(payload[0..4]),
            InvNum:  payload[4],
            InvSlot: payload[5]);
    }

    public byte[] EncodeOutbound(object message)
    {
        if (message is not EquipUseMessage use)
            throw new ArgumentException(
                $"expected EquipUseMessage, got {message?.GetType().Name ?? "null"}",
                nameof(message));

        byte[] buf = new byte[Size];
        BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(0, 4), use.GameId);
        buf[4] = use.InvNum;
        buf[5] = use.InvSlot;
        return buf;
    }
}
