// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya

using System.Text;

namespace N7.CliClient.Opcodes.Records;

/// <summary>
/// 0x0052 LOUNGE_NPC. Wire (variable, up to ~10KB), all fields LE:
///   StationData (8B): int32 StationType; int32 RoomCount.
///   RoomCount * StationRooms (28B each): RoomNumber; RoomStyle; FogNear; FogFar; FogR; FogG; FogB.
///   int32 NumTerms; NumTerms * StationTerms (16B each): RoomNumber; Location; TermType; Unknown.
///   int32 NumNPCs; NumNPCs * StationNPC (265B each, fixed stride): 6 int32 header
///     (RoomNumber, Location, NPCID, BoothType, Unknown1, Unknown2 = 24B) followed by
///     a 241-byte AvatarData (avatar_first_name[20], avatar_last_name[20], then a
///     cosmetic appearance block). Lounge NPCs repurpose the name fields: first_name
///     carries the visible NPC name, last_name a dialogue/mission tag.
/// The 265-byte stride is fixed because StationNPC + AvatarData are both ATTRIB_PACKED
/// fixed-size structs that SendLoungeNPC (PlayerConnection.cpp:9721) memcpy's verbatim
/// (sizeof(StationNPC)) per NPC. Decoding the 6 ints + both names per NPC self-validates
/// the stride: a wrong size garbles every subsequent NPC's name. The remaining 201 bytes
/// of each AvatarData (colours/body fields) are byte-pinned but not field-decoded.
/// </summary>
public sealed class LoungeNpcRecord : PacketRecord
{
    public LoungeNpcRecord(ReadOnlySpan<byte> payload) : base(0x0052, payload) { }
    protected override void WriteFields(StringBuilder sb)
    {
        if (Payload.Length < 8) { Flag(sb, $"LOUNGE_NPC truncated -- {Payload.Length} bytes, expected >= 8"); return; }
        int stationType = ReadI32LE(Payload, 0);
        int roomCount = ReadI32LE(Payload, 4);
        FDec(sb, 0, "StationType", stationType);
        FDec(sb, 4, "RoomCount", roomCount);
        const int RoomSize = 28;
        int off = 8 + roomCount * RoomSize;
        for (int r = 0; r < roomCount; r++)
        {
            int roff = 8 + r * RoomSize;
            if (roff + RoomSize > Payload.Length) break;
            int roomNum = ReadI32LE(Payload, roff);
            int roomStyle = ReadI32LE(Payload, roff + 4);
            float fogNear = ReadF32LE(Payload, roff + 8);
            float fogFar = ReadF32LE(Payload, roff + 12);
            float fogR = ReadF32LE(Payload, roff + 16);
            float fogG = ReadF32LE(Payload, roff + 20);
            float fogB = ReadF32LE(Payload, roff + 24);
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
            int room = ReadI32LE(Payload, off);
            int location = ReadI32LE(Payload, off + 4);
            int termType = ReadI32LE(Payload, off + 8);
            FBytes(sb, off, TermSize, $"  Term[{t}]", $"room={room} loc={location} type={termType}");
        }
        if (off + 4 > Payload.Length) { Flag(sb, "truncated before NumNPCs"); return; }
        int numNpcs = ReadI32LE(Payload, off);
        FDec(sb, off, "NumNPCs", numNpcs); off += 4;
        const int NpcHdr = 24;          // 6 int32: Room, Location, NPCID, BoothType, Unknown1, Unknown2
        const int AvatarSize = 241;     // fixed ATTRIB_PACKED AvatarData
        const int NpcSize = NpcHdr + AvatarSize;   // 265
        const int NameLen = 20;         // avatar_first_name[20] / avatar_last_name[20]
        for (int n = 0; n < numNpcs; n++, off += NpcSize)
        {
            if (off + NpcSize > Payload.Length)
            {
                Flag(sb, $"NPC[{n}] truncated: need {off + NpcSize - Payload.Length} more bytes");
                return;
            }
            int room = ReadI32LE(Payload, off);
            int loc = ReadI32LE(Payload, off + 4);
            int npcId = ReadI32LE(Payload, off + 8);
            int booth = ReadI32LE(Payload, off + 12);
            int unk1 = ReadI32LE(Payload, off + 16);
            int unk2 = ReadI32LE(Payload, off + 20);
            int avOff = off + NpcHdr;
            string first = ReadNulString(Payload.AsSpan(avOff, NameLen));
            string last = ReadNulString(Payload.AsSpan(avOff + NameLen, NameLen));
            FBytes(sb, off, NpcHdr, $"  NPC[{n}]",
                   $"room={room} loc={loc} npcId=0x{npcId:X4} booth={booth} unk=({unk1},{unk2})");
            FBytes(sb, avOff, NameLen * 2, "    name", $"{Quote(first)} / {Quote(last)}");
            F(sb, avOff + NameLen * 2, AvatarSize - NameLen * 2, "    appearance",
              $"{AvatarSize - NameLen * 2}B", "(AvatarData cosmetic block -- not field-decoded)");
        }
    }
}
