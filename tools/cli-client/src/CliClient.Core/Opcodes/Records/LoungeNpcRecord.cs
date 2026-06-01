// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Text;

namespace N7.CliClient.Opcodes.Records;

/// <summary>
/// 0x0052 LOUNGE_NPC. Wire (variable, up to ~10KB):
///   StationData (8B): int32 StationType; int32 RoomCount.
///   RoomCount * StationRooms (28B each): RoomNumber; RoomStyle; FogNear; FogFar; FogR; FogG; FogB.
///   int32 NumTerms; NumTerms * StationTerms (16B each): RoomNumber; Location; TermType; Unknown.
///   int32 NumNPCs; NumNPCs * StationNPC (~269B each, not iterated -- count reported).
/// Source: PlayerConnection.cpp SendStationLounge().
/// </summary>
public sealed class LoungeNpcRecord : PacketRecord
{
    public LoungeNpcRecord(ReadOnlySpan<byte> payload) : base(0x0052, payload) { }
    protected override void WriteFields(StringBuilder sb)
    {
        if (Payload.Length < 8) { Flag(sb, $"LOUNGE_NPC truncated -- {Payload.Length} bytes, expected >= 8"); return; }
        int stationType = ReadI32LE(Payload, 0);
        int roomCount   = ReadI32LE(Payload, 4);
        FDec(sb, 0, "StationType", stationType);
        FDec(sb, 4, "RoomCount",   roomCount);
        const int RoomSize = 28;
        int off = 8 + roomCount * RoomSize;
        for (int r = 0; r < roomCount; r++)
        {
            int roff = 8 + r * RoomSize;
            if (roff + RoomSize > Payload.Length) break;
            int   roomNum   = ReadI32LE(Payload, roff);
            int   roomStyle = ReadI32LE(Payload, roff + 4);
            float fogNear   = ReadF32LE(Payload, roff + 8);
            float fogFar    = ReadF32LE(Payload, roff + 12);
            float fogR      = ReadF32LE(Payload, roff + 16);
            float fogG      = ReadF32LE(Payload, roff + 20);
            float fogB      = ReadF32LE(Payload, roff + 24);
            FBytes(sb, roff, RoomSize, $"  Room[{r}]", $"num={roomNum} style={roomStyle} fog=({fogNear:0.##},{fogFar:0.##}) rgb=({fogR:0.##},{fogG:0.##},{fogB:0.##})");
        }
        if (off + 4 > Payload.Length) { Flag(sb, "truncated before NumTerms"); return; }
        int numTerms = ReadI32LE(Payload, off);
        FDec(sb, off, "NumTerms", numTerms); off += 4;
        const int TermSize = 16;
        int termsEnd = off + numTerms * TermSize;
        if (termsEnd > Payload.Length) { Flag(sb, $"truncated: need {termsEnd - Payload.Length} more bytes for terms"); return; }
        for (int t = 0; t < numTerms; t++, off += TermSize)
        {
            int room     = ReadI32LE(Payload, off);
            int location = ReadI32LE(Payload, off + 4);
            int termType = ReadI32LE(Payload, off + 8);
            FBytes(sb, off, TermSize, $"  Term[{t}]", $"room={room} loc={location} type={termType}");
        }
        if (off + 4 > Payload.Length) { Flag(sb, "truncated before NumNPCs"); return; }
        int numNpcs   = ReadI32LE(Payload, off);
        FDec(sb, off, "NumNPCs", numNpcs); off += 4;
        int remaining = Payload.Length - off;
        F(sb, off, remaining, "NPC data bytes", remaining.ToString(), "(AvatarData embedded -- not iterated)");
    }
}
