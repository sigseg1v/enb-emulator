// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Buffers.Binary;

namespace N7.CliClient.Opcodes.Outbound;

/// <summary>
/// Inventory container ids used by <see cref="InventoryMoveMessage"/>. These are
/// the values the server's Player::HandleInventoryMove switches on
/// (server/src/PlayerConnection.cpp:2505). A move is described as
/// (FromInv, FromSlot) -> (ToInv, ToSlot); the same opcode carries cargo
/// rearrange, equip, vault transfer, vendor buy/sell and jettison.
/// </summary>
public enum InventoryContainer
{
    /// <summary>Ship cargo hold.</summary>
    Cargo = 1,
    /// <summary>Equipped slots (weapons, devices, reactor, ...).</summary>
    Equip = 2,
    /// <summary>Station secure vault.</summary>
    Vault = 3,
    /// <summary>Vendor. As ToInv this SELLS the source item; as FromInv it BUYS
    /// the vendor-stock item into the target container.</summary>
    Vendor = 4,
    /// <summary>Loot source: the targeted husk's loot bin. Used only as FromInv;
    /// the server loots <c>FromSlot</c> of the currently-targeted husk
    /// (server/src/PlayerConnection.cpp:3076, <c>LootItem</c>). The real client
    /// sends ToInv=0, ToSlot=-1, Num=-1 with it.</summary>
    Loot = 6,
    /// <summary>Jettison into space ("spacing" an item).</summary>
    Space = 11,
    /// <summary>Manufacture / refinery input (only valid in manufacture mode).</summary>
    Manufacture = 12,
}

/// <summary>
/// 0x0027 INVENTORY_MOVE (client -> server). Moves <see cref="Num"/> units from
/// (<see cref="FromInv"/>, <see cref="FromSlot"/>) to (<see cref="ToInv"/>,
/// <see cref="ToSlot"/>). A <see cref="ToSlot"/> of -1 asks the server to pick
/// the first free slot (used for vault deposits and vendor sells).
/// </summary>
public sealed record InventoryMoveMessage(
    int GameId,
    int FromInv,
    int FromSlot,
    int ToInv,
    int ToSlot,
    int Num)
{
    /// <summary>Convenience ctor taking <see cref="InventoryContainer"/> enums.</summary>
    public InventoryMoveMessage(
        int gameId,
        InventoryContainer fromInv,
        int fromSlot,
        InventoryContainer toInv,
        int toSlot,
        int num)
        : this(gameId, (int)fromInv, fromSlot, (int)toInv, toSlot, num) { }
}

/// <summary>
/// Codec for opcode 0x0027 INVENTORY_MOVE (client -> server).
/// </summary>
/// <remarks>
/// Wire layout is <c>struct InvMove</c> (common/include/net7/PacketStructures.h:243),
/// 24 bytes, EVERY field big-endian: the server reads each one through
/// <c>ntohl</c> in Player::HandleInventoryMove, so the Win32 client sends network
/// byte order. Field order on the wire: GameID, FromInv, FromSlot, ToInv, ToSlot,
/// Num.
/// <para>
/// Byte-pinned against the live reference server capture
/// VendorInvEco frame #20 (cargo slot 28 -> vault, first free) and the vendor
/// buy/sell frames in the same capture. The inbound half is
/// <see cref="N7.CliClient.Opcodes.Records.InventoryMoveRecord"/>.
/// </para>
/// </remarks>
public sealed class InventoryMoveCodec : IOpcodeCodec
{
    /// <summary>Fixed wire size of struct InvMove.</summary>
    public const int Size = 24;

    public OpcodeId Opcode => OpcodeId.Known.InventoryMove;

    public object DecodeInbound(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < Size)
        {
            throw new InvalidDataException(
                $"InventoryMove payload is {payload.Length} bytes, expected {Size}");
        }

        return new InventoryMoveMessage(
            GameId:   BinaryPrimitives.ReadInt32BigEndian(payload[0..4]),
            FromInv:  BinaryPrimitives.ReadInt32BigEndian(payload[4..8]),
            FromSlot: BinaryPrimitives.ReadInt32BigEndian(payload[8..12]),
            ToInv:    BinaryPrimitives.ReadInt32BigEndian(payload[12..16]),
            ToSlot:   BinaryPrimitives.ReadInt32BigEndian(payload[16..20]),
            Num:      BinaryPrimitives.ReadInt32BigEndian(payload[20..24]));
    }

    public byte[] EncodeOutbound(object message)
    {
        if (message is not InventoryMoveMessage move)
            throw new ArgumentException(
                $"expected InventoryMoveMessage, got {message?.GetType().Name ?? "null"}",
                nameof(message));

        byte[] buf = new byte[Size];
        Span<byte> span = buf;
        BinaryPrimitives.WriteInt32BigEndian(span[0..4],   move.GameId);
        BinaryPrimitives.WriteInt32BigEndian(span[4..8],   move.FromInv);
        BinaryPrimitives.WriteInt32BigEndian(span[8..12],  move.FromSlot);
        BinaryPrimitives.WriteInt32BigEndian(span[12..16], move.ToInv);
        BinaryPrimitives.WriteInt32BigEndian(span[16..20], move.ToSlot);
        BinaryPrimitives.WriteInt32BigEndian(span[20..24], move.Num);
        return buf;
    }
}
