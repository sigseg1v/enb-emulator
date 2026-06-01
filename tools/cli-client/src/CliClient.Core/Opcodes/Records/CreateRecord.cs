// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Text;

namespace N7.CliClient.Opcodes.Records;

/// <summary>
/// 0x0004 CREATE -- spawn a server-side object.
/// Wire layout (struct Create in common/include/net7/PacketStructures.h):
///   int32 GameID; float Scale; short BaseAsset; char Type; float HSV[3]; = 23 bytes
/// </summary>
public sealed class CreateRecord : PacketRecord
{
    public CreateRecord(ReadOnlySpan<byte> payload) : base(0x0004, payload) { }

    protected override void WriteFields(StringBuilder sb)
    {
        if (Payload.Length < 23)
        {
            Flag(sb, $"CREATE truncated -- {Payload.Length} bytes, expected 23+");
            return;
        }
        int gameId    = ReadI32LE(Payload, 0);
        float scale   = ReadF32LE(Payload, 4);
        short asset   = ReadI16LE(Payload, 8);
        byte type     = Payload[10];
        float h       = ReadF32LE(Payload, 11);
        float s       = ReadF32LE(Payload, 15);
        float v       = ReadF32LE(Payload, 19);

        FieldHex(sb, "GameID", gameId);
        FlagSuspicious(sb, "GameID", gameId);
        FieldFloat(sb, "Scale", scale);
        FieldHex(sb, "BaseAsset", (ushort)asset);
        FieldDec(sb, "Type", type);
        Field(sb, "HSV", $"({h:0.0##}, {s:0.0##}, {v:0.0##})");
    }
}
