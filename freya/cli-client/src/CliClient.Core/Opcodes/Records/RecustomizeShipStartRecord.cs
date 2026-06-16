// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya

using System.Text;

namespace N7.CliClient.Opcodes.Records;

/// <summary>
/// 0x0081 RECUSTOMIZE_SHIP_START. Wire (struct RecustomizeShipStart,
/// PacketStructures.h:1103):
///   ShipData ship (194 bytes); int32 costs[12]; int32 PlayerID; int32 unknown[4].
///   = 262 bytes.
/// Emitter: PlayerConnection.cpp:10042. The whole struct is memcpy'd verbatim, so
/// every ShipData field and costs[] is host-order LE; PlayerID is emitted BE
/// (`rss.playerid = htonl(pkt-&gt;PlayerID)`), same convention as the avatar
/// 0x0083 packet. unknown[4] is zeroed at emit.
///
/// ShipData (PacketStructures.h:196, 194 bytes): five int32 ids (race, profession,
/// hull, wing, decal), a 26-byte NUL-terminated ship_name, a float[3] name colour,
/// then eight 17-byte ColorInfo blocks (HSV float[3] + char flat + int32 metal):
/// Hull/Profession/Wing/Engine each in a Primary+Secondary pair.
/// </summary>
public sealed class RecustomizeShipStartRecord : PacketRecord
{
    public RecustomizeShipStartRecord(ReadOnlySpan<byte> payload) : base(0x0081, payload) { }

    private const int ShipDataLen = 194;
    private const int TotalLen = 262;

    protected override void WriteFields(StringBuilder sb)
    {
        if (Payload.Length < TotalLen)
        {
            Flag(sb, $"RECUSTOMIZE_SHIP_START truncated -- {Payload.Length} bytes, expected {TotalLen}");
            return;
        }

        // -- ShipData @ 0x00 (194 bytes) --
        FDec(sb, 0x00, "ship.Race", ReadI32LE(Payload, 0x00));
        FDec(sb, 0x04, "ship.Profession", ReadI32LE(Payload, 0x04));
        FHex(sb, 0x08, "ship.Hull", ReadI32LE(Payload, 0x08));
        FHex(sb, 0x0C, "ship.Wing", ReadI32LE(Payload, 0x0C));
        FHex(sb, 0x10, "ship.Decal", ReadI32LE(Payload, 0x10));
        FStr(sb, 0x14, 26, "ship.Name", ReadNulString(Payload.AsSpan(0x14, 26)));
        FFloat(sb, 0x2E, "ship.NameColor.H", ReadF32LE(Payload, 0x2E));
        FFloat(sb, 0x32, "ship.NameColor.S", ReadF32LE(Payload, 0x32));
        FFloat(sb, 0x36, "ship.NameColor.V", ReadF32LE(Payload, 0x36));

        int off = 0x3A;
        WriteColor(sb, ref off, "ship.HullPrimary");
        WriteColor(sb, ref off, "ship.HullSecondary");
        WriteColor(sb, ref off, "ship.ProfessionPrimary");
        WriteColor(sb, ref off, "ship.ProfessionSecondary");
        WriteColor(sb, ref off, "ship.WingPrimary");
        WriteColor(sb, ref off, "ship.WingSecondary");
        WriteColor(sb, ref off, "ship.EnginePrimary");
        WriteColor(sb, ref off, "ship.EngineSecondary");
        // off is now 0xC2 (ShipDataLen).

        // -- costs[12] @ 0xC2 (host-order LE) --
        for (int i = 0; i < 12; i++)
            FDec(sb, ShipDataLen + i * 4, $"Cost[{i}]", ReadI32LE(Payload, ShipDataLen + i * 4));

        // -- PlayerID @ 0xF2 (BE -- htonl at emit) --
        FHex(sb, 0xF2, "PlayerID", ReadI32BE(Payload, 0xF2), "(BE -- htonl at emit)");

        // -- unknown[4] @ 0xF6 (zeroed) --
        for (int i = 0; i < 4; i++)
            FHex(sb, 0xF6 + i * 4, $"Unknown[{i}]", ReadI32LE(Payload, 0xF6 + i * 4));
    }

    /// <summary>ColorInfo (17 bytes): float[3] HSV; char flat; int32 metal.</summary>
    private void WriteColor(StringBuilder sb, ref int off, string name)
    {
        FFloat(sb, off, $"{name}.H", ReadF32LE(Payload, off));
        FFloat(sb, off + 4, $"{name}.S", ReadF32LE(Payload, off + 4));
        FFloat(sb, off + 8, $"{name}.V", ReadF32LE(Payload, off + 8));
        FDec(sb, off + 12, $"{name}.Flat", Payload[off + 12]);
        FDec(sb, off + 13, $"{name}.Metal", ReadI32LE(Payload, off + 13));
        off += 17;
    }
}
