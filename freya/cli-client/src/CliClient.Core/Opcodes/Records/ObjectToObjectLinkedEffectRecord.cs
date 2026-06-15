// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya

using System.Text;

namespace N7.CliClient.Opcodes.Records;

/// <summary>
/// 0x000E OBJECT_TO_OBJECT_LINKED_EFFECT. A duration-linked effect that travels
/// from a source object to a target object (e.g. a beam weapon impact). Fixed
/// 58-byte payload, all LE, serialised field-by-field (no struct memcpy):
///   [00] int32 ObjectID    -- sector-assigned effect object id
///   [04] uint32 TimeStamp   -- server tick
///   [08] int32 SourceID     -- GameID of the effect source
///   [0C] int8  Spacer       -- unused, 0
///   [0D] int32 TargetID     -- GameID of the effect target
///   [11] int16 LinkedEffectDescID  -- effect played for the duration of the link
///   [13] int16 EffectDescID         -- impact effect
///   [15] float TargetOffset[3]      -- x/y/z offset from the target hit zone (server leaves 0)
///   [21] int32 Unknown      -- unused, 0
///   [25] int8  OutsideTargetRadius  -- 1
///   [26] float Scale        -- 1.0
///   [2A] float HSVShift[3]
///   [36] float Speedup      -- link travel-speed multiplier
/// </summary>
public sealed class ObjectToObjectLinkedEffectRecord : PacketRecord
{
    public ObjectToObjectLinkedEffectRecord(ReadOnlySpan<byte> payload) : base(0x000E, payload) { }
    protected override void WriteFields(StringBuilder sb)
    {
        if (Payload.Length < 58) { Flag(sb, $"OBJECT_TO_OBJECT_LINKED_EFFECT truncated -- {Payload.Length} bytes, expected 58"); return; }
        FHex(sb, 0, "ObjectID",  ReadI32LE(Payload, 0));
        FHex(sb, 4, "TimeStamp", ReadU32LE(Payload, 4));
        FHex(sb, 8, "SourceID",  ReadI32LE(Payload, 8));
        FDec(sb, 12, "Spacer",   Payload[12]);
        FHex(sb, 13, "TargetID", ReadI32LE(Payload, 13));
        FDec(sb, 17, "LinkedEffectDescID", ReadI16LE(Payload, 17));
        FDec(sb, 19, "EffectDescID",       ReadI16LE(Payload, 19));
        FBytes(sb, 21, 12, "TargetOffset",
               $"({ReadF32LE(Payload, 21):0.0##}, {ReadF32LE(Payload, 25):0.0##}, {ReadF32LE(Payload, 29):0.0##})");
        FHex(sb, 33, "Unknown", ReadI32LE(Payload, 33));
        FDec(sb, 37, "OutsideTargetRadius", Payload[37]);
        FFloat(sb, 38, "Scale", ReadF32LE(Payload, 38));
        FBytes(sb, 42, 12, "HSVShift",
               $"({ReadF32LE(Payload, 42):0.0##}, {ReadF32LE(Payload, 46):0.0##}, {ReadF32LE(Payload, 50):0.0##})");
        FFloat(sb, 54, "Speedup", ReadF32LE(Payload, 54));
        if (Payload.Length > 58) Flag(sb, $"OBJECT_TO_OBJECT_LINKED_EFFECT has {Payload.Length - 58} trailing bytes");
    }
}
