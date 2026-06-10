// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Text;

namespace N7.CliClient.Opcodes.Records;

/// <summary>
/// 0x0004 CREATE. Wire (struct Create): int32 GameID; float Scale; short BaseAsset; char Type; float HSV[3]. = 23 bytes.
/// </summary>
public sealed class CreateRecord : PacketRecord
{
    public CreateRecord(ReadOnlySpan<byte> payload) : base(0x0004, payload) { }
    protected override void WriteFields(StringBuilder sb)
    {
        if (Payload.Length < 23) { Flag(sb, $"CREATE truncated -- {Payload.Length} bytes, expected 23+"); return; }
        int   gameId = ReadI32LE(Payload, 0);
        float scale  = ReadF32LE(Payload, 4);
        short asset  = ReadI16LE(Payload, 8);
        byte  type   = Payload[10];
        float h      = ReadF32LE(Payload, 11);
        float s      = ReadF32LE(Payload, 15);
        float v      = ReadF32LE(Payload, 19);
        FHex(sb, 0, "GameID",    gameId);
        FlagSuspicious(sb, "GameID", gameId);
        FFloat(sb, 4, "Scale",   scale);
        FHex(sb, 8, "BaseAsset", (ushort)asset);
        FDec(sb, 10, "Type",     type);
        FBytes(sb, 11, 12, "HSV", $"({h:0.0##}, {s:0.0##}, {v:0.0##})");
    }
}
