// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Text;

namespace N7.CliClient.Opcodes.Records;

/// <summary>
/// 0x0011 COLORIZATION. Wire:
///   int32 GameID; int16 ItemCount; then ItemCount colour slots, each a PAIR of
///   {int32 metal; float H,S,V} blocks -- a primary block followed by a secondary
///   block (32 bytes per slot). Retail's own colorization (capture_3.rar, the
///   frame just before Server-&gt;Client packet #373: GameID 0x00AACCEE,
///   ItemCount 4, 134-byte payload) carries 4 slots == 8 colour blocks, so the
///   counted unit is the slot/pair, not the single ColorizationItem. The server's
///   struct Colorization names the 8 blocks Hull/Profession/Wing/Engine x
///   {primary,secondary}; here we stay byte-literal and just label them
///   primary/secondary per slot.
/// </summary>
public sealed class ColorizationRecord : PacketRecord
{
    public ColorizationRecord(ReadOnlySpan<byte> payload) : base(0x0011, payload) { }
    protected override void WriteFields(StringBuilder sb)
    {
        if (Payload.Length < 6) { Flag(sb, $"COLORIZATION truncated -- {Payload.Length} bytes, expected >= 6"); return; }
        int   gameId = ReadI32LE(Payload, 0);
        short count  = ReadI16LE(Payload, 4);
        FHex(sb, 0, "GameID",    gameId);
        FDec(sb, 4, "ItemCount", count);
        const int BlockSize = 16;       // one {metal, H, S, V}
        const int SlotSize  = 32;       // primary + secondary block
        int expected = 6 + count * SlotSize;
        if (Payload.Length < expected) { Flag(sb, $"payload too short for {count} slots -- {Payload.Length} bytes, expected {expected}"); return; }
        int off = 6;
        for (int i = 0; i < count; i++, off += SlotSize)
        {
            EmitBlock(sb, off,             $"  [{i}] primary  ");
            EmitBlock(sb, off + BlockSize, $"  [{i}] secondary");
        }
        if (Payload.Length > expected)
            Flag(sb, $"{Payload.Length - expected} trailing bytes after {count} slots");
    }

    private void EmitBlock(StringBuilder sb, int off, string label)
    {
        int   metal = ReadI32LE(Payload, off);
        float h     = ReadF32LE(Payload, off + 4);
        float s     = ReadF32LE(Payload, off + 8);
        float v     = ReadF32LE(Payload, off + 12);
        FHex(sb, off,       $"{label} Metal", metal);
        FBytes(sb, off + 4, 12, $"{label} H/S/V", $"{h:0.###}, {s:0.###}, {v:0.###}");
    }
}
