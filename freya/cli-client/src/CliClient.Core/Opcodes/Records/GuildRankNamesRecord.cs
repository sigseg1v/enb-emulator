// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya

using System.Text;

namespace N7.CliClient.Opcodes.Records;

/// <summary>
/// 0x00D3 GUILD_RANK_NAMES_SECTOR. Wire (PlayerGuild.cpp:690
/// HandleGuildRankNamesRequest):
///   int32 Count (BE, always 10);
///   Count x { AddDataLS RankName (uint16 len LE + len bytes); int32 Index (BE, = i+1) }.
/// Count and the per-rank Index are emitted via htonl() -> BIG-ENDIAN; the
/// AddDataLS length prefixes remain little-endian.
/// </summary>
public sealed class GuildRankNamesRecord : PacketRecord
{
    public GuildRankNamesRecord(ReadOnlySpan<byte> payload) : base(0x00D3, payload) { }
    protected override void WriteFields(StringBuilder sb)
    {
        if (Payload.Length < 4) { Flag(sb, $"GUILD_RANK_NAMES truncated -- {Payload.Length} bytes, expected >= 4"); return; }
        int count = ReadI32BE(Payload, 0);
        FDec(sb, 0, "Count", count, "(BE)");
        if (count < 0 || count > 64) { Flag(sb, $"GUILD_RANK_NAMES implausible count={count}"); return; }
        int off = 4;
        for (int i = 0; i < count; i++)
        {
            if (!TryReadAddDataLS(sb, ref off, $"[{i}].RankName")) return;
            if (off + 4 > Payload.Length)
            {
                Flag(sb, $"GUILD_RANK_NAMES: [{i}].Index needs 4 bytes at offset {off}, only {Payload.Length - off} remain");
                return;
            }
            FDec(sb, off, $"[{i}].Index", ReadI32BE(Payload, off), "(BE)");
            off += 4;
        }
    }
}
