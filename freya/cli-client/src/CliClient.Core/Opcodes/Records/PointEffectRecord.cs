// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Text;

namespace N7.CliClient.Opcodes.Records;

/// <summary>
/// 0x000A POINT_EFFECT. Spawns a one-shot effect at a fixed world point (no
/// parent object). Fixed 40-byte payload, all LE:
///   [00] int32 ObjectID   -- sector-assigned effect object id
///   [04] uint32 TimeStamp  -- server tick + 200ms
///   [08] float  X
///   [0C] float  Y
///   [10] float  Z
///   [14] int16  Duration  -- 0 in observed frames
///   [16] int16  EffectID  -- the visual effect id
///   [18] float  Scale     -- 129.982 * caller-scale
///   [1C] float  HSV[0]
///   [20] float  HSV[1]
///   [24] float  HSV[2]
/// Source: Player::PointEffect (server/src/PlayerConnection.cpp:1018), which
/// builds the buffer field-by-field at exactly these offsets.
/// </summary>
public sealed class PointEffectRecord : PacketRecord
{
    public PointEffectRecord(ReadOnlySpan<byte> payload) : base(0x000A, payload) { }
    protected override void WriteFields(StringBuilder sb)
    {
        if (Payload.Length < 40) { Flag(sb, $"POINT_EFFECT truncated -- {Payload.Length} bytes, expected 40"); return; }
        FHex(sb, 0, "ObjectID",  ReadI32LE(Payload, 0));
        FHex(sb, 4, "TimeStamp", ReadU32LE(Payload, 4));
        FBytes(sb, 8, 12, "Position",
               $"({ReadF32LE(Payload, 8):0.0##}, {ReadF32LE(Payload, 12):0.0##}, {ReadF32LE(Payload, 16):0.0##})");
        FDec(sb, 20, "Duration", ReadI16LE(Payload, 20));
        FDec(sb, 22, "EffectID", ReadI16LE(Payload, 22));
        FFloat(sb, 24, "Scale", ReadF32LE(Payload, 24));
        FBytes(sb, 28, 12, "HSVShift",
               $"({ReadF32LE(Payload, 28):0.0##}, {ReadF32LE(Payload, 32):0.0##}, {ReadF32LE(Payload, 36):0.0##})");
        if (Payload.Length > 40) Flag(sb, $"POINT_EFFECT has {Payload.Length - 40} trailing bytes");
    }
}
