// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Text;

namespace N7.CliClient.Opcodes.Records;

/// <summary>
/// 0x0040 CONSTANT_POSITIONAL_UPDATE.
/// Wire layout (struct ConstantPositionalUpdate):
///   int32 GameID; float Pos[3]; float Orient[4]; = 4 + 12 + 16 = 32 bytes
/// </summary>
public sealed class ConstantPosRecord : PacketRecord
{
    public ConstantPosRecord(ReadOnlySpan<byte> payload) : base(0x0040, payload) { }

    protected override void WriteFields(StringBuilder sb)
    {
        if (Payload.Length < 32)
        {
            Flag(sb, $"CONSTANT_POS truncated -- {Payload.Length} bytes, expected 32");
            return;
        }
        int gameId = ReadI32LE(Payload, 0);
        float px = ReadF32LE(Payload, 4);
        float py = ReadF32LE(Payload, 8);
        float pz = ReadF32LE(Payload, 12);
        float ow = ReadF32LE(Payload, 16);
        float ox = ReadF32LE(Payload, 20);
        float oy = ReadF32LE(Payload, 24);
        float oz = ReadF32LE(Payload, 28);

        FieldHex(sb, "GameID", gameId);
        Field(sb, "Position", $"({px:0.0##}, {py:0.0##}, {pz:0.0##})");
        Field(sb, "Orientation", $"({ow:0.0##}, {ox:0.0##}, {oy:0.0##}, {oz:0.0##})");
    }
}
