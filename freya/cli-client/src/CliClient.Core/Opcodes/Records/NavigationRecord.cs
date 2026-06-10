// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Text;

namespace N7.CliClient.Opcodes.Records;

/// <summary>
/// 0x0099 NAVIGATION. Wire (14 bytes, PACKED):
///   int32 GameID; float Signature; uint8 PlayerHasVisited; int32 NavType; uint8 IsHuge.
/// </summary>
public sealed class NavigationRecord : PacketRecord
{
    public NavigationRecord(ReadOnlySpan<byte> payload) : base(0x0099, payload) { }

    protected override void WriteFields(StringBuilder sb)
    {
        if (Payload.Length < 14) { Flag(sb, $"NAVIGATION truncated -- {Payload.Length} bytes, expected 14"); return; }
        int   gameId          = ReadI32LE(Payload, 0);
        float signature       = ReadF32LE(Payload, 4);
        byte  playerHasVisited = Payload[8];
        int   navType         = ReadI32LE(Payload, 9);
        byte  isHuge          = Payload[13];

        FHex(sb,   0, "GameID",           gameId);
        FFloat(sb, 4, "Signature",        signature);
        FDec(sb,   8, "PlayerHasVisited", playerHasVisited);
        FDec(sb,   9, "NavType",          navType);
        FDec(sb,  13, "IsHuge",           isHuge);
    }
}
