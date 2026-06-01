// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Text;

namespace N7.CliClient.Opcodes.Records;

/// <summary>
/// 0x00B2 NAME_DECAL. Wire layout (struct NameDecal):
///   int32 GameID; char Name[32]; float RGB[3]; = 4 + 32 + 12 = 48 bytes
/// </summary>
public sealed class NameDecalRecord : PacketRecord
{
    public NameDecalRecord(ReadOnlySpan<byte> payload) : base(0x00B2, payload) { }

    protected override void WriteFields(StringBuilder sb)
    {
        if (Payload.Length < 48)
        {
            Flag(sb, $"NAME_DECAL truncated -- {Payload.Length} bytes, expected 48");
            return;
        }
        int gameId = ReadI32LE(Payload, 0);
        string name = ReadNulString(Payload.AsSpan(4, 32));
        float r = ReadF32LE(Payload, 36);
        float g = ReadF32LE(Payload, 40);
        float b = ReadF32LE(Payload, 44);

        FieldHex(sb, "GameID", gameId);
        FieldString(sb, "Name", name, requiredNonEmpty: true);
        Field(sb, "RGB", $"({r:0.0##}, {g:0.0##}, {b:0.0##})");
    }
}
