// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Text;

namespace N7.CliClient.Opcodes.Records;

/// <summary>
/// 0x0011 COLORIZATION. Wire shape (struct Colorization):
///   int32 GameID; int16 ItemCount; then ItemCount * ColorizationItem.
/// ColorizationItem (16 bytes): int32 metal; float H; float S; float V.
/// Size sent = ((char*)&item[ItemCount]) - ((char*)&header).
/// </summary>
public sealed class ColorizationRecord : PacketRecord
{
    public ColorizationRecord(ReadOnlySpan<byte> payload) : base(0x0011, payload) { }

    protected override void WriteFields(StringBuilder sb)
    {
        if (Payload.Length < 6)
        {
            Flag(sb, $"COLORIZATION truncated -- {Payload.Length} bytes, expected >= 6");
            return;
        }
        int gameId    = ReadI32LE(Payload, 0);
        short count   = ReadI16LE(Payload, 4);

        FieldHex(sb, "GameID",    gameId);
        FieldDec(sb, "ItemCount", count);

        const int ItemSize = 16;
        int expected = 6 + count * ItemSize;
        if (Payload.Length < expected)
        {
            Flag(sb, $"payload too short for {count} items -- {Payload.Length} bytes, expected {expected}");
            return;
        }

        int off = 6;
        for (int i = 0; i < count; i++, off += ItemSize)
        {
            int   metal = ReadI32LE(Payload, off);
            float h     = ReadF32LE(Payload, off + 4);
            float s     = ReadF32LE(Payload, off + 8);
            float v     = ReadF32LE(Payload, off + 12);
            Field(sb, $"  [{i}] Metal",   $"0x{metal:X8}  ({metal})");
            Field(sb, $"  [{i}] H/S/V",   $"{h:0.###}, {s:0.###}, {v:0.###}");
        }
    }
}
