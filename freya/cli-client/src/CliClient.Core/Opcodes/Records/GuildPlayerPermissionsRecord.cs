// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya

using System.Text;

namespace N7.CliClient.Opcodes.Records;

/// <summary>
/// 0x00D2 GUILD_PLAYER_PERMISSIONS. Wire (PlayerGuild.cpp:785, 4x AddData(htonl(...))):
///   int32 Permission; int32 MaxPromote; int32 MaxRemove; int32 MinDemote.
///   = 16 bytes, ALL BIG-ENDIAN.
/// </summary>
public sealed class GuildPlayerPermissionsRecord : PacketRecord
{
    public GuildPlayerPermissionsRecord(ReadOnlySpan<byte> payload) : base(0x00D2, payload) { }
    protected override void WriteFields(StringBuilder sb)
    {
        if (Payload.Length < 16) { Flag(sb, $"GUILD_PLAYER_PERMISSIONS truncated -- {Payload.Length} bytes, expected 16"); return; }
        FHex(sb, 0, "Permission", ReadI32BE(Payload, 0), "(BE -- htonl at emit)");
        FDec(sb, 4, "MaxPromote", ReadI32BE(Payload, 4), "(BE)");
        FDec(sb, 8, "MaxRemove", ReadI32BE(Payload, 8), "(BE)");
        FDec(sb, 12, "MinDemote", ReadI32BE(Payload, 12), "(BE)");
    }
}
