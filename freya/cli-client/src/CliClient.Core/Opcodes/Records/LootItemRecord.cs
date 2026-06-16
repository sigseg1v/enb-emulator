// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya

using System.Text;

namespace N7.CliClient.Opcodes.Records;

/// <summary>
/// 0x2014 LOOT_ITEM. A lootable item floating in space (tractorable). Variable,
/// per PlayerSkills.cpp:1141-1156:
///   int32  GameID            @0
///   int32  ArticleUID        @4
///   int32  ArticleEffectUID  @8
///   int16  BaseAsset         @12
///   uint32 LootTick          @14
///   int16  NameLen           @18   (LS-prefixed name)
///   char   ItemName[NameLen] @20
///   uint32 TractorTime       @..
///   float  TractorSpeed      @..
///   float  PosX,PosY,PosZ    @..
/// </summary>
public sealed class LootItemRecord : PacketRecord
{
    public LootItemRecord(ReadOnlySpan<byte> payload) : base(0x2014, payload) { }

    protected override void WriteFields(StringBuilder sb)
    {
        if (Payload.Length < 20) { Flag(sb, $"LOOT_ITEM truncated -- {Payload.Length} bytes, expected >= 20"); return; }

        FHex(sb, 0, "GameID", ReadI32LE(Payload, 0));
        FHex(sb, 4, "ArticleUID", ReadI32LE(Payload, 4));
        FHex(sb, 8, "ArticleEffectUID", ReadI32LE(Payload, 8));
        FDec(sb, 12, "BaseAsset", ReadI16LE(Payload, 12));
        FHex(sb, 14, "LootTick", ReadU32LE(Payload, 14));

        short nameLen = ReadI16LE(Payload, 18);
        FDec(sb, 18, "NameLen", nameLen);
        if (nameLen < 0 || 20 + nameLen > Payload.Length) { Flag(sb, $"NameLen {nameLen} overruns payload ({Payload.Length} bytes)"); return; }
        string name = Encoding.Latin1.GetString(Payload, 20, nameLen);
        FStr(sb, 20, nameLen, "ItemName", name, required: false);

        int off = 20 + nameLen;
        if (off + 20 > Payload.Length) { Flag(sb, $"LOOT_ITEM missing tractor/position trailer -- {Payload.Length - off} bytes left"); return; }
        FHex(sb, off, "TractorTime", ReadU32LE(Payload, off));
        FFloat(sb, off + 4, "TractorSpeed", ReadF32LE(Payload, off + 4));
        float px = ReadF32LE(Payload, off + 8);
        float py = ReadF32LE(Payload, off + 12);
        float pz = ReadF32LE(Payload, off + 16);
        FBytes(sb, off + 8, 12, "Position", $"({px:0.##}, {py:0.##}, {pz:0.##})");
    }
}
