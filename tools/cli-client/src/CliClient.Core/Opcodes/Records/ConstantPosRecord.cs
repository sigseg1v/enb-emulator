// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Text;

namespace N7.CliClient.Opcodes.Records;

/// <summary>
/// 0x0040 CONSTANT_POSITIONAL_UPDATE. Wire (struct ConstantPositionalUpdate):
///   int32 GameID; float Pos[3]; float Orient[4]. = 32 bytes.
/// </summary>
public sealed class ConstantPosRecord : PacketRecord
{
    public ConstantPosRecord(ReadOnlySpan<byte> payload) : base(0x0040, payload) { }
    protected override void WriteFields(StringBuilder sb)
    {
        if (Payload.Length < 32) { Flag(sb, $"CONSTANT_POS truncated -- {Payload.Length} bytes, expected 32"); return; }
        int   gameId = ReadI32LE(Payload, 0);
        float px = ReadF32LE(Payload,  4), py = ReadF32LE(Payload,  8), pz = ReadF32LE(Payload, 12);
        float ow = ReadF32LE(Payload, 16), ox = ReadF32LE(Payload, 20), oy = ReadF32LE(Payload, 24), oz = ReadF32LE(Payload, 28);
        FHex(sb,   0, "GameID",      gameId);
        FBytes(sb, 4,  12, "Position",    $"({px:0.0##}, {py:0.0##}, {pz:0.0##})");
        FBytes(sb, 16, 16, "Orientation", $"({ow:0.0##}, {ox:0.0##}, {oy:0.0##}, {oz:0.0##})");
    }
}
