// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Text;

namespace N7.CliClient.Opcodes.Records;

/// <summary>
/// 0x0028 INVENTORY_SORT (client->server). Wire (struct InvSort, 21 bytes):
/// int32 ID; int32 TargetInv; int32 Sort1; int32 Sort2; int32 Sort3; u8 Reverse.
/// The five int32s are all big-endian -- Player::HandleInventorySort reads each
/// through ntohl (the uniform-BE convention shared with 0x27 InventoryMove);
/// Reverse is a single trailing byte (no byte order). TargetInv selects the
/// container (1 = cargo, 3 = vault). Sort1..Sort3 are up to three sort keys
/// applied in order by InvSortFunc (1 = name, 5 = category, 10 = value; 4/8 are
/// secondary/tertiary keys the server treats as no-ops). Reverse != 0 inverts
/// the comparison.
/// Source: struct InvSort (PacketStructures.h:253), Player::HandleInventorySort
/// + InvSortFunc (PlayerConnection.cpp:3285/3249). Pinned to capture_3.rar
/// (Client->Server).
/// </summary>
public sealed class InventorySortRecord : PacketRecord
{
    public InventorySortRecord(ReadOnlySpan<byte> payload) : base(0x0028, payload) { }

    protected override void WriteFields(StringBuilder sb)
    {
        if (Payload.Length < 21) { Flag(sb, $"INVENTORY_SORT truncated -- {Payload.Length} bytes, expected 21"); return; }

        FHex(sb,  0, "ID",        ReadI32BE(Payload,  0), "(BE -- ntohl at parse)");
        FDec(sb,  4, "TargetInv", ReadI32BE(Payload,  4), TargetInvNote(ReadI32BE(Payload, 4)));
        FDec(sb,  8, "Sort1",     ReadI32BE(Payload,  8), SortKeyNote(ReadI32BE(Payload,  8)));
        FDec(sb, 12, "Sort2",     ReadI32BE(Payload, 12), SortKeyNote(ReadI32BE(Payload, 12)));
        FDec(sb, 16, "Sort3",     ReadI32BE(Payload, 16), SortKeyNote(ReadI32BE(Payload, 16)));
        FDec(sb, 20, "Reverse",   Payload[20], Payload[20] != 0 ? "(reverse order)" : "(ascending)");
    }

    private static string TargetInvNote(int inv) => inv switch
    {
        1 => "(BE; cargo)",
        3 => "(BE; vault)",
        _ => "(BE; container)",
    };

    private static string SortKeyNote(int key) => key switch
    {
        1  => "(BE; sort by name)",
        5  => "(BE; sort by category)",
        10 => "(BE; sort by value)",
        4  => "(BE; secondary key -- no-op)",
        8  => "(BE; tertiary key -- no-op)",
        _  => "(BE; sort key)",
    };
}
