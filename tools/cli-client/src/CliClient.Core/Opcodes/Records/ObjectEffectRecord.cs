// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Text;

namespace N7.CliClient.Opcodes.Records;

/// <summary>
/// 0x0009 OBJECT_EFFECT. Wire (struct ObjectEffect, variable):
///   uint8 Bitmask; int32 GameID; int16 EffectDescID; conditional fields per bit:
///   0x01 int32 EffectID; 0x02 uint32 TimeStamp; 0x04 int16 Duration;
///   0x08 float Scale; 0x10/0x20/0x40 float HSVShift[3]. Min 7 bytes.
/// </summary>
public sealed class ObjectEffectRecord : PacketRecord
{
    public ObjectEffectRecord(ReadOnlySpan<byte> payload) : base(0x0009, payload) { }
    protected override void WriteFields(StringBuilder sb)
    {
        if (Payload.Length < 7) { Flag(sb, $"OBJECT_EFFECT truncated -- {Payload.Length} bytes, expected >= 7"); return; }
        byte  bitmask     = Payload[0];
        int   gameId      = ReadI32LE(Payload, 1);
        short effectDescId = ReadI16LE(Payload, 5);
        FHex(sb, 0, "Bitmask",      bitmask);
        FHex(sb, 1, "GameID",       gameId);
        FHex(sb, 5, "EffectDescID", (ushort)effectDescId);
        int off = 7;
        if ((bitmask & 0x01) != 0) { if (off + 4 > Payload.Length) { Flag(sb, "truncated before EffectID"); return; }   FHex(sb, off, "EffectID",    ReadI32LE(Payload, off)); off += 4; }
        if ((bitmask & 0x02) != 0) { if (off + 4 > Payload.Length) { Flag(sb, "truncated before TimeStamp"); return; }  FHex(sb, off, "TimeStamp",   ReadU32LE(Payload, off)); off += 4; }
        if ((bitmask & 0x04) != 0) { if (off + 2 > Payload.Length) { Flag(sb, "truncated before Duration"); return; }   FDec(sb, off, "Duration",    ReadI16LE(Payload, off)); off += 2; }
        if ((bitmask & 0x08) != 0) { if (off + 4 > Payload.Length) { Flag(sb, "truncated before Scale"); return; }      FFloat(sb, off, "Scale",      ReadF32LE(Payload, off)); off += 4; }
        if ((bitmask & 0x10) != 0) { if (off + 4 > Payload.Length) { Flag(sb, "truncated before HSVShift[0]"); return; }FFloat(sb, off, "HSVShift[0]",ReadF32LE(Payload, off)); off += 4; }
        if ((bitmask & 0x20) != 0) { if (off + 4 > Payload.Length) { Flag(sb, "truncated before HSVShift[1]"); return; }FFloat(sb, off, "HSVShift[1]",ReadF32LE(Payload, off)); off += 4; }
        if ((bitmask & 0x40) != 0) { if (off + 4 > Payload.Length) { Flag(sb, "truncated before HSVShift[2]"); return; }FFloat(sb, off, "HSVShift[2]",ReadF32LE(Payload, off)); off += 4; }

        // Live net-7 (2026) capture sends an unconditional EffectID right after
        // EffectDescID even when bitmask bit0 is clear; tada-o's SendEffect gates
        // it on bit0 (and the older capture set always had bit0 set, hiding this).
        // Decode any trailing 4 bytes so the frame is fully accounted for.
        if (off + 4 == Payload.Length)
            FHex(sb, off, "EffectID", ReadU32LE(Payload, off), "(unconditional in live net7 capture)");
    }
}
