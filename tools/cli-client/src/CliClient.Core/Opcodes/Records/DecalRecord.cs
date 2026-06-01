// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Text;

namespace N7.CliClient.Opcodes.Records;

/// <summary>
/// 0x0010 DECAL. Wire (struct Decal):
///   int32 GameID; int16 DecalCount; then DecalCount * DecalItem (24B each):
///   int32 Index; int32 decal_id; float H; float S; float V; float opacity.
/// </summary>
public sealed class DecalRecord : PacketRecord
{
    public DecalRecord(ReadOnlySpan<byte> payload) : base(0x0010, payload) { }
    protected override void WriteFields(StringBuilder sb)
    {
        if (Payload.Length < 6) { Flag(sb, $"DECAL truncated -- {'{'}Payload.Length{'}'} bytes, expected >= 6"); return; }
        int   gameId = ReadI32LE(Payload, 0);
        short count  = ReadI16LE(Payload, 4);
        FHex(sb, 0, "GameID",     gameId);
        FDec(sb, 4, "DecalCount", count);
        const int ItemSize = 24;
        int expected = 6 + count * ItemSize;
        if (Payload.Length < expected) { Flag(sb, $"payload too short for {'{'}count{'}'} items -- {'{'}Payload.Length{'}'} bytes, expected {'{'}expected{'}'}"); return; }
        int off = 6;
        for (int i = 0; i < count; i++, off += ItemSize)
        {
            int   idx     = ReadI32LE(Payload, off);
            int   decalId = ReadI32LE(Payload, off + 4);
            float h       = ReadF32LE(Payload, off + 8);
            float s       = ReadF32LE(Payload, off + 12);
            float v       = ReadF32LE(Payload, off + 16);
            float opacity = ReadF32LE(Payload, off + 20);
            FHex(sb, off,      $"  [{i}] Index",   idx);
            FHex(sb, off+4,    $"  [{i}] DecalID", decalId);
            FBytes(sb, off+8,  12, $"  [{i}] H/S/V", $"{h:0.###}, {s:0.###}, {v:0.###}");
            FFloat(sb, off+20, $"  [{i}] Opacity",  opacity);
        }
    }
}
