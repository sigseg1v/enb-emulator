// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya

using System.Text;

namespace N7.CliClient.Opcodes.Records;

/// <summary>
/// SKILL_ABILITY (0x0058, client->server) -- the player activates a skill/ability
/// (the ability hotbar). Wire (struct SkillUse, 12 bytes): int32 GameID, int32
/// Action, int32 AbilityIndex. ALL LITTLE-ENDIAN. The consumer, Player::
/// HandleSkillAbility (PlayerAbilitys.cpp:23), reads Action-&gt;AbilityIndex directly
/// and uses it as an index into m_AbilityList[0..MAX_ABILITY_IDS) (138) with NO
/// ntohl -- so AbilityIndex is host-order little-endian, and the capture proves it:
/// across the session's seven 0x58 frames AbilityIndex reads {44, 123, 131}
/// little-endian, all inside [0,138), whereas big-endian would give 0x2C000000 /
/// 0x7B000000 / 0x83000000 -- all far past the bound, so every frame would fall to
/// the handler's "not yet working" rejection. GameID is the caster's ship/avatar
/// game id (a small sane id little-endian: 8708585 / 3854004 / 4045835 / 4050589;
/// big-endian would be negative/billions); the handler doesn't read it. The struct's
/// middle Action field is 0 in all seven captured frames and is not read by the
/// handler, so it is shown little-endian-by-convention and labelled unused rather
/// than flagged. Source: struct SkillUse (PacketStructures.h:994), Player::
/// HandleSkillAbility (PlayerAbilitys.cpp:23), MAX_ABILITY_IDS (PlayerSkills.h:275).
/// Pinned to capture_3.rar.
/// </summary>
public sealed class SkillAbilityRecord : PacketRecord
{
    private const int MaxAbilityIds = 138;

    public SkillAbilityRecord(ReadOnlySpan<byte> payload) : base(0x0058, payload) { }

    protected override void WriteFields(StringBuilder sb)
    {
        if (Payload.Length < 12) { Flag(sb, $"SKILL_ABILITY truncated -- {Payload.Length} bytes, expected 12"); return; }

        FHex(sb, 0, "GameID", ReadI32LE(Payload, 0),
            "(LE; caster ship/avatar game id -- not read by HandleSkillAbility)");
        FDec(sb, 4, "Action", ReadI32LE(Payload, 4),
            "(LE; unused by HandleSkillAbility -- 0 in all captured frames)");

        int abilityIndex = ReadI32LE(Payload, 8);
        FDec(sb, 8, "AbilityIndex", abilityIndex,
            $"(LE; index into m_AbilityList[0..{MaxAbilityIds}) -- HandleSkillAbility reads this directly, no ntohl)");
        if (abilityIndex < 0 || abilityIndex >= MaxAbilityIds)
            Flag(sb, $"AbilityIndex {abilityIndex} out of range [0,{MaxAbilityIds}) -- HandleSkillAbility would reject it");

        if (Payload.Length > 12) Flag(sb, $"SKILL_ABILITY has {Payload.Length - 12} trailing bytes");
    }
}
