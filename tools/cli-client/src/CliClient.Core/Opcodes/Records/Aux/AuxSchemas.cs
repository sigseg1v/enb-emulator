// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

namespace N7.CliClient.Opcodes.Records.Aux;

using static AuxKind;

/// <summary>
/// AuxBase-structure schemas, ported field-for-field from the server's
/// per-class BuildPacket() methods under server/src/AuxClasses/. Field order
/// is the serialisation order (ascending flagNum); flagNum/sizeof(Flags) and
/// wire kinds are taken straight from each class's BuildPacket + SetData.
/// </summary>
public static class AuxSchemas
{
    private static AuxField F(int flag, string name, AuxKind kind, AuxSchema? s = null)
        => new(flag, name, kind, s);

    // ── inventory item (AuxItem.cpp, Flags[2]) ────────────────────────────────
    public static readonly AuxSchema Item = new("Item", 2, new[]
    {
        F(0, "ItemTemplateID", S32),
        F(1, "StackCount",     U32),
        F(2, "Price",          U64),
        F(3, "AveCost",        F32),
        F(4, "Structure",      F32),
        F(5, "Quality",        F32),
        F(6, "InstanceInfo",                 Str),
        F(7, "ActivatedEffectInstanceInfo",  Str),
        F(8, "EquipEffectInstanceInfo",      Str),
        F(9, "BuilderName",                  Str),
    });

    public static readonly AuxSchema SecureInv   = new("SecureInv",   13, 96,  Item);
    public static readonly AuxSchema VendorInv   = new("VendorInv",   17, 128, Item);
    public static readonly AuxSchema RewardInv   = new("RewardInv",   1,  2,   Item);
    public static readonly AuxSchema OverflowInv = new("OverflowInv", 2,  8,   Item);

    // ── skills (AuxSkill.cpp, Flags[2]) ───────────────────────────────────────
    public static readonly AuxSchema Skill = new("Skill", 2, new[]
    {
        F(0, "Level",              U32),
        F(1, "RecycleTime",        U32),
        F(2, "LastActivationTime", U32),
        F(3, "Availability",       Avail4),
        F(4, "QuestOnlyLevel",     U32),
        F(5, "MaxSkillLevel",      U32),
        F(6, "Affiliation",        Str),
    });
    public static readonly AuxSchema Skills = new("Skills", 9, 64, Skill);

    // ── skill abilities (AuxSkillAbility.cpp, Flags[2]) ───────────────────────
    public static readonly AuxSchema SkillAbility = new("SkillAbility", 2, new[]
    {
        F(0, "HasAbility",                   U8),
        F(1, "Usage",                        U32),
        F(2, "Targets",                      U32),
        F(3, "Range",                        U32),
        F(4, "Radius",                       U32),
        F(5, "Validity",                     U32),
        F(6, "CanBeUsedWhileIncapacitated",  U8),
    });
    public static readonly AuxSchema SkillAbilities = new("SkillAbilities", 22, 170, SkillAbility);

    // ── missions (AuxMissionStage.cpp Flags[1]; AuxMission.cpp Flags[3]) ──────
    public static readonly AuxSchema MissionStage = new("MissionStage", 1, new[]
    {
        F(0, "Text",    Str),
        F(1, "IsTimed", U8),
    });
    public static readonly AuxSchema MissionStages = new("MissionStages", 3, 20, MissionStage);

    public static readonly AuxSchema Mission = new("Mission", 3, new[]
    {
        F(0,  "ID",                  U32),
        F(1,  "Name",                Str),
        F(2,  "Summary",             Str),
        F(3,  "Reward",              Str),
        F(4,  "FailureConsequence",  Str),
        F(5,  "IssuingFaction",      Str),
        F(6,  "IsTimed",             Bool),
        F(7,  "ExpirationTime",      U32),
        F(8,  "StartTime",           U32),
        F(9,  "IsForfeitable",       Bool),
        F(10, "IsCompleted",         Bool),
        F(11, "IsFailed",            Bool),
        F(12, "IsExpired",           Bool),
        F(13, "IsFullyVisible",      Bool),
        F(14, "StageCount",          U32),
        F(15, "StageNum",            U32),
        F(16, "Stages",              Nested, MissionStages),
        F(17, "StageExpirationTime", U32),
        F(18, "HasGivenNewMissionMessage", Bool),
    });
    public static readonly AuxSchema Missions = new("Missions", 2, 12, Mission);

    // ── reputation (AuxFaction.cpp Flags[1]; AuxReputation.cpp Flags[1]) ──────
    public static readonly AuxSchema Faction = new("Faction", 1, new[]
    {
        F(0, "Name",     Str),
        F(1, "Reaction", F32),
        F(2, "Order",    U32),
    });
    public static readonly AuxSchema Factions = new("Factions", 5, 32, Faction);
    public static readonly AuxSchema Reputation = new("Reputation", 1, new[]
    {
        F(0, "Factions",    Nested, Factions),
        F(1, "Affiliation", Str),
    });

    // ── RPG info (AuxRPGInfo.cpp, Flags[3]) ───────────────────────────────────
    public static readonly AuxSchema RPGInfo = new("RPGInfo", 3, new[]
    {
        F(0,  "Race",                      U32),
        F(1,  "Profession",                U32),
        F(2,  "Skills",                    Nested, Skills),
        F(3,  "Abilities",                 Nested, SkillAbilities),
        F(4,  "SkillPoints",               U32),
        F(5,  "TotalSkillPoints",          U32),
        F(6,  "CombatXP",                  F32),
        F(7,  "CombatLevel",               U32),
        F(8,  "TradeXP",                   F32),
        F(9,  "TradeLevel",                U32),
        F(10, "ExploreXP",                 F32),
        F(11, "ExploreLevel",              U32),
        F(12, "HullUpgradeLevel",          U32),
        F(13, "SkillPowerUpStartTime",     U32),
        F(14, "SkillPowerUpEndTime",       U32),
        F(15, "SkillPowerUpAbilityNumber", S32),
    });

    // ── group (AuxGroupMember.cpp Flags[1]; AuxGroupInfo.cpp Flags[2]) ────────
    public static readonly AuxSchema GroupMember = new("GroupMember", 1, new[]
    {
        F(0, "Name",      Str),
        F(1, "GameID",    U32),
        F(2, "Formation", U32),
        F(3, "Position",  U32),
    });
    public static readonly AuxSchema GroupMembers = new("GroupMembers", 2, 5, GroupMember);
    public static readonly AuxSchema GroupInfo = new("GroupInfo", 2, new[]
    {
        F(0,  "IsGroupLeader",               U8),
        F(1,  "LookingForGroup",             U8),
        F(2,  "AllowGroupInvite",            U8),
        F(3,  "ShowNonCombatantActivities",  U8),
        F(4,  "ForceAutoSplit",              U8),
        F(5,  "RestrictedLootingRights",     U8),
        F(6,  "AutoReleaseLootingRights",    U8),
        F(7,  "FormationName",               Str),
        F(8,  "Formation",                   U32),
        F(9,  "Position",                    U32),
        F(10, "Members",                     Nested, GroupMembers),
    });

    // ── AuxPlayerIndex (top-level, Flags[3], GameID always 0) ─────────────────
    public static readonly AuxSchema PlayerIndex = new("PlayerIndex", 3, new[]
    {
        F(0,  "Credits",                    U64),
        F(1,  "XPDebt",                     U32),
        F(2,  "SecureInv",                  Nested, SecureInv),
        F(3,  "VendorInv",                  Nested, VendorInv),
        F(4,  "RewardInv",                  Nested, RewardInv),
        F(5,  "OverflowInv",                Nested, OverflowInv),
        F(6,  "RPGInfo",                    Nested, RPGInfo),
        F(7,  "CommunityEventFlags",        Str),
        F(8,  "MusicID",                    U32),
        F(9,  "Missions",                   Nested, Missions),
        F(10, "Reputation",                 Nested, Reputation),
        F(11, "PIPAvatarID",                U32),
        F(12, "RegistrationStarbase",       Str),
        F(13, "RegistrationStarbaseSector", Str),
        F(14, "SectorName",                 Str),
        F(15, "SectorNum",                  U32),
        F(16, "ClientSendUITriggers",       U32),
        F(17, "GroupInfo",                  Nested, GroupInfo),
    }, hasHeader: true);
}
