// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya

using System.Text;

namespace N7.CliClient.Opcodes.Records;

/// <summary>
/// 0x0027 INVENTORY_MOVE (client->server). Wire (struct InvMove, 24 bytes,
/// BIG-ENDIAN): int32 GameID; int32 FromInv; int32 FromSlot; int32 ToInv;
/// int32 ToSlot; int32 Num.
/// EVERY field is big-endian on the wire: Player::HandleInventoryMove reads each
/// one through ntohl (Inventory->GameID, ->FromInv, ->FromSlot, ->ToInv,
/// ->ToSlot, ->Num), so the client sends network byte order and the server
/// byte-swaps to host. This is the same BE convention as 0x5A VerbRequest's
/// SubjectID/ObjectID, applied uniformly here (no LE field). FromInv/ToInv select
/// the container (the handler switches FromInv: 1 = cargo, 2 = equip, ...);
/// ToSlot/Num == -1 are sentinels the client sends for "unspecified".
/// Source: struct InvMove (PacketStructures.h), Player::HandleInventoryMove
/// (PlayerConnection.cpp). Pinned to capture_3.rar (Client->Server).
/// </summary>
public sealed class InventoryMoveRecord : PacketRecord
{
    public InventoryMoveRecord(ReadOnlySpan<byte> payload) : base(0x0027, payload) { }

    protected override void WriteFields(StringBuilder sb)
    {
        if (Payload.Length < 24) { Flag(sb, $"INVENTORY_MOVE truncated -- {Payload.Length} bytes, expected 24"); return; }
        FHex(sb,  0, "GameID",   ReadI32BE(Payload,  0), "(BE -- ntohl at parse)");
        FDec(sb,  4, "FromInv",  ReadI32BE(Payload,  4), "(BE; 1=cargo 2=equip ...)");
        FDec(sb,  8, "FromSlot", ReadI32BE(Payload,  8), "(BE)");
        FDec(sb, 12, "ToInv",    ReadI32BE(Payload, 12), "(BE)");
        FDec(sb, 16, "ToSlot",   ReadI32BE(Payload, 16), "(BE)");
        FDec(sb, 20, "Num",      ReadI32BE(Payload, 20), "(BE)");
    }
}
