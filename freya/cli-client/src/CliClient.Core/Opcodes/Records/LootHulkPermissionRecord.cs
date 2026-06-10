// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Text;

namespace N7.CliClient.Opcodes.Records;

/// <summary>
/// 0x008C LOOT_HULK_PERMISSION. Server grants the client permission to loot a
/// destroyed object's hulk. 4 bytes:
///   int32  HulkId  @0   (game id of the lootable hulk)
/// </summary>
public sealed class LootHulkPermissionRecord : PacketRecord
{
    public LootHulkPermissionRecord(ReadOnlySpan<byte> payload) : base(0x008C, payload) { }

    protected override void WriteFields(StringBuilder sb)
    {
        if (Payload.Length < 4) { Flag(sb, $"LOOT_HULK_PERMISSION truncated -- {Payload.Length} bytes, expected 4"); return; }
        FHex(sb, 0, "HulkId", ReadI32LE(Payload, 0));
    }
}
