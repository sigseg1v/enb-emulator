// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Text;

namespace N7.CliClient.Opcodes.Records;

/// <summary>
/// 0x0008 SIMPLE_POSITIONAL_UPDATE. Wire (struct SimplePositionalUpdate):
///   int32 GameID; uint32 TimeStamp; float Pos[3]; float Orient[4]; float Vel[3]. = 48 bytes.
/// </summary>
public sealed class SimplePosRecord : PacketRecord
{
    public SimplePosRecord(ReadOnlySpan<byte> payload) : base(0x0008, payload) { }
    protected override void WriteFields(StringBuilder sb)
    {
        if (Payload.Length < 48) { Flag(sb, $"SIMPLE_POS truncated -- {Payload.Length} bytes, expected 48"); return; }
        int   gameId = ReadI32LE(Payload, 0);
        uint  ts     = ReadU32LE(Payload, 4);
        float px = ReadF32LE(Payload,  8), py = ReadF32LE(Payload, 12), pz = ReadF32LE(Payload, 16);
        float ow = ReadF32LE(Payload, 20), ox = ReadF32LE(Payload, 24), oy = ReadF32LE(Payload, 28), oz = ReadF32LE(Payload, 32);
        float vx = ReadF32LE(Payload, 36), vy = ReadF32LE(Payload, 40), vz = ReadF32LE(Payload, 44);
        FHex(sb,   0, "GameID",      gameId);
        FHex(sb,   4, "TimeStamp",   ts);
        FBytes(sb, 8,  12, "Position",    $"({px:0.0##}, {py:0.0##}, {pz:0.0##})");
        FBytes(sb, 20, 16, "Orientation", $"({ow:0.0##}, {ox:0.0##}, {oy:0.0##}, {oz:0.0##})");
        FBytes(sb, 36, 12, "Velocity",    $"({vx:0.0##}, {vy:0.0##}, {vz:0.0##})");
    }
}
