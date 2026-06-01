// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Text;

namespace N7.CliClient.Opcodes.Records;

/// <summary>
/// 0x0009 OBJECT_EFFECT. Wire shape (struct ObjectEffect, variable):
///   uint8 Bitmask; int32 GameID; int16 EffectDescID;
///   then conditionally (per bitmask bit):
///     bit 0x01: int32  EffectID
///     bit 0x02: uint32 TimeStamp
///     bit 0x04: int16  Duration
///     bit 0x08: float  Scale
///     bit 0x10: float  HSVShift[0]
///     bit 0x20: float  HSVShift[1]
///     bit 0x40: float  HSVShift[2]
/// Minimum 7 bytes (Bitmask + GameID + EffectDescID).
/// </summary>
public sealed class ObjectEffectRecord : PacketRecord
{
    public ObjectEffectRecord(ReadOnlySpan<byte> payload) : base(0x0009, payload) { }

    protected override void WriteFields(StringBuilder sb)
    {
        if (Payload.Length < 7)
        {
            Flag(sb, $"OBJECT_EFFECT truncated -- {Payload.Length} bytes, expected >= 7");
            return;
        }
        byte bitmask      = Payload[0];
        int gameId        = ReadI32LE(Payload, 1);
        short effectDescId = ReadI16LE(Payload, 5);

        FieldHex(sb, "Bitmask",      bitmask);
        FieldHex(sb, "GameID",       gameId);
        FieldHex(sb, "EffectDescID", (ushort)effectDescId);

        int off = 7;
        if ((bitmask & 0x01) != 0)
        {
            if (off + 4 > Payload.Length) { Flag(sb, "truncated before EffectID"); return; }
            FieldHex(sb, "EffectID", ReadI32LE(Payload, off));
            off += 4;
        }
        if ((bitmask & 0x02) != 0)
        {
            if (off + 4 > Payload.Length) { Flag(sb, "truncated before TimeStamp"); return; }
            FieldHex(sb, "TimeStamp", ReadU32LE(Payload, off));
            off += 4;
        }
        if ((bitmask & 0x04) != 0)
        {
            if (off + 2 > Payload.Length) { Flag(sb, "truncated before Duration"); return; }
            FieldDec(sb, "Duration", ReadI16LE(Payload, off));
            off += 2;
        }
        if ((bitmask & 0x08) != 0)
        {
            if (off + 4 > Payload.Length) { Flag(sb, "truncated before Scale"); return; }
            FieldFloat(sb, "Scale", ReadF32LE(Payload, off));
            off += 4;
        }
        if ((bitmask & 0x10) != 0)
        {
            if (off + 4 > Payload.Length) { Flag(sb, "truncated before HSVShift[0]"); return; }
            FieldFloat(sb, "HSVShift[0]", ReadF32LE(Payload, off));
            off += 4;
        }
        if ((bitmask & 0x20) != 0)
        {
            if (off + 4 > Payload.Length) { Flag(sb, "truncated before HSVShift[1]"); return; }
            FieldFloat(sb, "HSVShift[1]", ReadF32LE(Payload, off));
            off += 4;
        }
        if ((bitmask & 0x40) != 0)
        {
            if (off + 4 > Payload.Length) { Flag(sb, "truncated before HSVShift[2]"); return; }
            FieldFloat(sb, "HSVShift[2]", ReadF32LE(Payload, off));
        }
    }
}
