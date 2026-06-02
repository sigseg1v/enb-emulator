// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Text;

namespace N7.CliClient.Opcodes.Records;

/// <summary>
/// 0x000B OBJECT_TO_OBJECT_EFFECT. A visual/effect link between two objects
/// (e.g. a weapon beam, a buff). Variable + bitmask-conditional.
///
/// Fixed header:
///   uint16 Bitmask        @0
///   int32  GameID         @2
///   int32  TargetID       @6
///   uint16 EffectDescID   @10
///   char   Message[]      @12   (null-terminated)
/// Then, ONLY when the matching bit is set, in this exact order -- matching the
/// server emitter Object::SendObjectToObjectEffect (server/src/ObjectClass.cpp
/// lines 870-925), which is the authoritative wire layout (and differs from the
/// PacketStructures.h comment, which is stale):
///   bit0  0x001  int32  EffectID
///   bit1  0x002  uint32 TimeStamp
///   bit2  0x004  int16  Duration
///   bit3  0x008  int32  OutsideTargetRadius   (emitted as AddData((long)) = 4B)
///   bit4  0x010  (nothing emitted)
///   bit5  0x020  (nothing emitted)
///   bit6  0x040  float  TargetOffset[3]
///   bit7  0x080  float  Scale
///   bit8  0x100  float  HSVShift[0]
///   bit9  0x200  float  HSVShift[1]
///   bit10 0x400  float  HSVShift[2]
///   bit11 0x800  float  Speedup
/// Validated byte-exact against the live retail capture: bitmask 0x0007 (35B,
/// weapon-fire with EffectID+TimeStamp+Duration) and 0x0807 (27B, same three
/// plus the Speedup field) both consume to the last byte.
/// </summary>
public sealed class ObjectToObjectEffectRecord : PacketRecord
{
    public ObjectToObjectEffectRecord(ReadOnlySpan<byte> payload) : base(0x000B, payload) { }

    protected override void WriteFields(StringBuilder sb)
    {
        if (Payload.Length < 12) { Flag(sb, $"OBJECT_TO_OBJECT_EFFECT truncated -- {Payload.Length} bytes, expected >= 12"); return; }

        ushort bitmask = ReadU16LE(Payload, 0);
        FHex(sb, 0, "Bitmask",      bitmask, DecodeBits(bitmask));
        FHex(sb, 2, "GameID",       ReadI32LE(Payload, 2));
        FHex(sb, 6, "TargetID",     ReadI32LE(Payload, 6));
        FHex(sb, 10, "EffectDescID", ReadU16LE(Payload, 10));

        int msgEnd = IndexOfNul(12);
        if (msgEnd < 0) { Flag(sb, "OBJECT_TO_OBJECT_EFFECT Message has no null terminator"); return; }
        FStr(sb, 12, msgEnd - 12, "Message", Encoding.Latin1.GetString(Payload, 12, msgEnd - 12), required: false);
        Mark(msgEnd, 1);
        int off = msgEnd + 1;

        if (Bit(bitmask, 0) && Need(sb, ref off, 4, "EffectID"))            FHex(sb, off - 4, "EffectID", ReadI32LE(Payload, off - 4));
        if (Bit(bitmask, 1) && Need(sb, ref off, 4, "TimeStamp"))           FHex(sb, off - 4, "TimeStamp", ReadU32LE(Payload, off - 4));
        if (Bit(bitmask, 2) && Need(sb, ref off, 2, "Duration"))            FDec(sb, off - 2, "Duration", ReadI16LE(Payload, off - 2));
        if (Bit(bitmask, 3) && Need(sb, ref off, 4, "OutsideTargetRadius")) FDec(sb, off - 4, "OutsideTargetRadius", ReadI32LE(Payload, off - 4));
        // bits 4 (0x10) and 5 (0x20) emit nothing on the wire.
        if (Bit(bitmask, 6) && Need(sb, ref off, 12, "TargetOffset"))
        {
            int b = off - 12;
            FBytes(sb, b, 12, "TargetOffset", $"({ReadF32LE(Payload, b):0.##}, {ReadF32LE(Payload, b + 4):0.##}, {ReadF32LE(Payload, b + 8):0.##})");
        }
        if (Bit(bitmask, 7)  && Need(sb, ref off, 4, "Scale"))     FFloat(sb, off - 4, "Scale", ReadF32LE(Payload, off - 4));
        if (Bit(bitmask, 8)  && Need(sb, ref off, 4, "HSVShift0")) FFloat(sb, off - 4, "HSVShift0", ReadF32LE(Payload, off - 4));
        if (Bit(bitmask, 9)  && Need(sb, ref off, 4, "HSVShift1")) FFloat(sb, off - 4, "HSVShift1", ReadF32LE(Payload, off - 4));
        if (Bit(bitmask, 10) && Need(sb, ref off, 4, "HSVShift2")) FFloat(sb, off - 4, "HSVShift2", ReadF32LE(Payload, off - 4));
        if (Bit(bitmask, 11) && Need(sb, ref off, 4, "Speedup"))   FFloat(sb, off - 4, "Speedup", ReadF32LE(Payload, off - 4));
    }

    private static bool Bit(ushort mask, int n) => (mask & (1 << n)) != 0;

    private bool Need(StringBuilder sb, ref int off, int len, string field)
    {
        if (off + len > Payload.Length) { Flag(sb, $"{field} runs past payload (need {len}B at {off:X4}, have {Payload.Length - off})"); return false; }
        off += len;
        return true;
    }

    private static string DecodeBits(ushort m)
    {
        string[] names = { "EffectID", "TimeStamp", "Duration", "OutRadius", "unused4", "unused5", "TargetOffset", "Scale", "HSV0", "HSV1", "HSV2", "Speedup" };
        var parts = new List<string>();
        for (int i = 0; i < names.Length; i++)
            if ((m & (1 << i)) != 0) parts.Add(names[i]);
        return parts.Count > 0 ? string.Join("|", parts) : "-";
    }

    private int IndexOfNul(int from)
    {
        for (int i = from; i < Payload.Length; i++)
            if (Payload[i] == 0) return i;
        return -1;
    }
}
