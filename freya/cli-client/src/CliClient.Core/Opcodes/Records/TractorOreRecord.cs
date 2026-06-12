// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya

using System.Text;

namespace N7.CliClient.Opcodes.Records;

/// <summary>
/// 0x2013 TRACTOR_ORE. Compact control packet the server emits to every
/// range-list member when a player's mined ore is tractored aboard. The client
/// never sees this opcode -- the proxy is meant to EXPAND it into a 0x0004
/// CREATE (floating-ore article) + 0x000B tractor beam + 0x001B name AUX +
/// 0x0046 position sequence (Phase AA fabrication, plans/27 §3a).
///
/// Layout is byte-identical to 0x2014 LOOT_ITEM (see LootItemRecord) -- both
/// come off the same emitter family. From Player::UseTractorBeam
/// (server/src/PlayerSkills.cpp:773-785):
///   int32  GameID            @0   GameID()           -- player
///   int32  ArticleUID        @4   article_UID
///   int32  ArticleEffectUID  @8   article_effect_UID
///   int16  BaseAsset         @12  resource-&gt;GameBaseAsset()  (m_GameBaseAsset is short)
///   uint32 ProspectTick      @14  prospect_tick
///   int16  NameLen           @18  AddDataLS length prefix
///   char   ResourceName[]    @20  (NameLen bytes, no NUL)
///   uint32 TractorTime       @..  tractor_time
///   float  TractorSpeed      @..  tractor_speed
///   float  PosX,PosY,PosZ    @..  obj-&gt;PosX/PosY/PosZ
/// </summary>
public sealed class TractorOreRecord : PacketRecord
{
    public TractorOreRecord(ReadOnlySpan<byte> payload) : base(0x2013, payload) { }

    protected override void WriteFields(StringBuilder sb)
    {
        if (Payload.Length < 20) { Flag(sb, $"TRACTOR_ORE truncated -- {Payload.Length} bytes, expected >= 20"); return; }

        FHex(sb,  0, "GameID",           ReadI32LE(Payload, 0));
        FHex(sb,  4, "ArticleUID",       ReadI32LE(Payload, 4));
        FHex(sb,  8, "ArticleEffectUID", ReadI32LE(Payload, 8));
        FDec(sb, 12, "BaseAsset",        ReadI16LE(Payload, 12));
        FHex(sb, 14, "ProspectTick",     ReadU32LE(Payload, 14));

        short nameLen = ReadI16LE(Payload, 18);
        FDec(sb, 18, "NameLen", nameLen);
        if (nameLen < 0 || 20 + nameLen > Payload.Length) { Flag(sb, $"NameLen {nameLen} overruns payload ({Payload.Length} bytes)"); return; }
        string name = Encoding.Latin1.GetString(Payload, 20, nameLen);
        FStr(sb, 20, nameLen, "ResourceName", name, required: false);

        int off = 20 + nameLen;
        if (off + 20 > Payload.Length) { Flag(sb, $"TRACTOR_ORE missing tractor/position trailer -- {Payload.Length - off} bytes left"); return; }
        FHex(sb,   off,     "TractorTime",  ReadU32LE(Payload, off));
        FFloat(sb, off + 4, "TractorSpeed", ReadF32LE(Payload, off + 4));
        float px = ReadF32LE(Payload, off + 8);
        float py = ReadF32LE(Payload, off + 12);
        float pz = ReadF32LE(Payload, off + 16);
        FBytes(sb, off + 8, 12, "Position", $"({px:0.##}, {py:0.##}, {pz:0.##})");
    }
}
