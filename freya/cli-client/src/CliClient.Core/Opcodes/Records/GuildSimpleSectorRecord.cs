// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya

using System.Text;

namespace N7.CliClient.Opcodes.Records;

/// <summary>
/// 0x00CC GUILD_SIMPLE_SECTOR_CLIENT. Wire (PlayerGuild.cpp:823
/// SendGuildSimpleSectorClient):
///   int32 Type (LE); AddDataLS OptionalParam (uint16 len LE + len raw bytes).
/// </summary>
public sealed class GuildSimpleSectorRecord : PacketRecord
{
    public GuildSimpleSectorRecord(ReadOnlySpan<byte> payload) : base(0x00CC, payload) { }
    protected override void WriteFields(StringBuilder sb)
    {
        if (Payload.Length < 4) { Flag(sb, $"GUILD_SIMPLE_SECTOR truncated -- {Payload.Length} bytes, expected >= 4"); return; }
        FDec(sb, 0, "Type", ReadI32LE(Payload, 0));
        int off = 4;
        TryReadAddDataLS(sb, ref off, "OptionalParam");
    }
}
