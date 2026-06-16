// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya

using System.Text;

namespace N7.CliClient.Opcodes.Records;

/// <summary>
/// 0x0011 COLORIZATION. Wire:
///   int32 GameID; int16 ItemCount; then a run of 16-byte colour blocks
///   {int32 metal; float H,S,V}. The blocks pair up into slots: a primary
///   block followed by a secondary block (32 bytes per slot).
///
/// ItemCount's UNIT is not consistent across sources, so we never drive the
/// loop off it. Retail counts SLOTS: capture_3.rar (and capture_1/2) emit
/// ItemCount=4 for a 134-byte payload == 8 blocks, 90/90 frames -- so the
/// retail client reads count*32B pairs. The tada-o server
/// (PlayerClass.cpp SendShipColorization) instead counts flat blocks,
/// writing ItemCount=8 for the same 8-block body. To render frames from
/// either source we derive the block count from the payload length
/// ((len-6)/16) and only annotate ItemCount with how it lines up.
/// </summary>
public sealed class ColorizationRecord : PacketRecord
{
    public ColorizationRecord(ReadOnlySpan<byte> payload) : base(0x0011, payload) { }
    protected override void WriteFields(StringBuilder sb)
    {
        if (Payload.Length < 6) { Flag(sb, $"COLORIZATION truncated -- {Payload.Length} bytes, expected >= 6"); return; }
        int gameId = ReadI32LE(Payload, 0);
        short count = ReadI16LE(Payload, 4);
        FHex(sb, 0, "GameID", gameId);

        const int BlockSize = 16;       // one {metal, H, S, V}
        int bodyLen = Payload.Length - 6;
        int blocks = bodyLen / BlockSize;
        string note = count == blocks / 2 ? "slots, 2 blocks each (retail convention)"
                    : count == blocks ? "flat blocks (tada-o server convention)"
                    : $"does not match {blocks} blocks present";
        FDec(sb, 4, "ItemCount", count, note);

        int off = 6;
        for (int b = 0; off + BlockSize <= Payload.Length; b++, off += BlockSize)
            EmitBlock(sb, off, $"  [{b / 2}] {(b % 2 == 0 ? "primary  " : "secondary")}");

        int trailing = Payload.Length - off;
        if (trailing > 0) Flag(sb, $"{trailing} trailing bytes -- not a whole {BlockSize}B colour block");
    }

    private void EmitBlock(StringBuilder sb, int off, string label)
    {
        int metal = ReadI32LE(Payload, off);
        float h = ReadF32LE(Payload, off + 4);
        float s = ReadF32LE(Payload, off + 8);
        float v = ReadF32LE(Payload, off + 12);
        FHex(sb, off, $"{label} Metal", metal);
        FBytes(sb, off + 4, 12, $"{label} H/S/V", $"{h:0.###}, {s:0.###}, {v:0.###}");
    }
}
