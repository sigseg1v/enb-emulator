// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya

namespace N7.CliClient.Opcodes.Records.Aux;

using static AuxKind;

/// <summary>
/// AuxBase-structure schemas, ported field-for-field from the server's
/// per-class BuildPacket() / BuildExtendedPacket() under server/src/AuxClasses/.
/// Field order is the serialisation order (ascending flagNum); flag-bitmap
/// widths are derived from FlagCount (= field/slot count). Booleans serialise
/// as one byte (AddData&lt;bool&gt; / char()).
/// </summary>
public static class AuxSchemas
{
    private static AuxField F(int flag, string name, AuxKind kind, AuxSchema? s = null)
        => new(flag, name, kind, s);

    // ═══ shared leaf: inventory item (AuxItem.cpp) ════════════════════════════
    public static readonly AuxSchema Item = new("Item", new[]
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

    // ═══ player tree (AuxPlayerIndex.cpp + nested) ════════════════════════════
    public static readonly AuxSchema SecureInv = new("SecureInv", 96, Item);
    public static readonly AuxSchema VendorInv = new("VendorInv", 128, Item);
    public static readonly AuxSchema RewardInv = new("RewardInv", 2, Item);
    public static readonly AuxSchema OverflowInv = new("OverflowInv", 8, Item);

    public static readonly AuxSchema Skill = new("Skill", new[]
    {
        F(0, "Level", U32), F(1, "RecycleTime", U32), F(2, "LastActivationTime", U32),
        F(3, "Availability", Avail4), F(4, "QuestOnlyLevel", U32), F(5, "MaxSkillLevel", U32),
        F(6, "Affiliation", Str),
    });
    public static readonly AuxSchema Skills = new("Skills", 64, Skill);

    public static readonly AuxSchema SkillAbility = new("SkillAbility", new[]
    {
        F(0, "HasAbility", U8), F(1, "Usage", U32), F(2, "Targets", U32), F(3, "Range", U32),
        F(4, "Radius", U32), F(5, "Validity", U32), F(6, "CanBeUsedWhileIncapacitated", U8),
    });
    public static readonly AuxSchema SkillAbilities = new("SkillAbilities", 170, SkillAbility);

    public static readonly AuxSchema MissionStage = new("MissionStage", new[]
    {
        F(0, "Text", Str), F(1, "IsTimed", U8),
    });
    public static readonly AuxSchema MissionStages = new("MissionStages", 20, MissionStage);

    public static readonly AuxSchema Mission = new("Mission", new[]
    {
        F(0,  "ID", U32), F(1, "Name", Str), F(2, "Summary", Str), F(3, "Reward", Str),
        F(4,  "FailureConsequence", Str), F(5, "IssuingFaction", Str), F(6, "IsTimed", Bool),
        F(7,  "ExpirationTime", U32), F(8, "StartTime", U32), F(9, "IsForfeitable", Bool),
        F(10, "IsCompleted", Bool), F(11, "IsFailed", Bool), F(12, "IsExpired", Bool),
        F(13, "IsFullyVisible", Bool), F(14, "StageCount", U32), F(15, "StageNum", U32),
        F(16, "Stages", Nested, MissionStages), F(17, "StageExpirationTime", U32),
        F(18, "HasGivenNewMissionMessage", Bool),
    });
    public static readonly AuxSchema Missions = new("Missions", 12, Mission);

    public static readonly AuxSchema Faction = new("Faction", new[]
    {
        F(0, "Name", Str), F(1, "Reaction", F32), F(2, "Order", U32),
    });
    public static readonly AuxSchema Factions = new("Factions", 32, Faction);
    public static readonly AuxSchema Reputation = new("Reputation", new[]
    {
        F(0, "Factions", Nested, Factions), F(1, "Affiliation", Str),
    });

    public static readonly AuxSchema RPGInfo = new("RPGInfo", new[]
    {
        F(0,  "Race", U32), F(1, "Profession", U32), F(2, "Skills", Nested, Skills),
        F(3,  "Abilities", Nested, SkillAbilities), F(4, "SkillPoints", U32),
        F(5,  "TotalSkillPoints", U32), F(6, "CombatXP", F32), F(7, "CombatLevel", U32),
        F(8,  "TradeXP", F32), F(9, "TradeLevel", U32), F(10, "ExploreXP", F32),
        F(11, "ExploreLevel", U32), F(12, "HullUpgradeLevel", U32),
        F(13, "SkillPowerUpStartTime", U32), F(14, "SkillPowerUpEndTime", U32),
        F(15, "SkillPowerUpAbilityNumber", S32),
    });

    public static readonly AuxSchema GroupMember = new("GroupMember", new[]
    {
        F(0, "Name", Str), F(1, "GameID", U32), F(2, "Formation", U32), F(3, "Position", U32),
    });
    public static readonly AuxSchema GroupMembers = new("GroupMembers", 5, GroupMember);
    public static readonly AuxSchema GroupInfo = new("GroupInfo", new[]
    {
        F(0,  "IsGroupLeader", U8), F(1, "LookingForGroup", U8), F(2, "AllowGroupInvite", U8),
        F(3,  "ShowNonCombatantActivities", U8), F(4, "ForceAutoSplit", U8),
        F(5,  "RestrictedLootingRights", U8), F(6, "AutoReleaseLootingRights", U8),
        F(7,  "FormationName", Str), F(8, "Formation", U32), F(9, "Position", U32),
        F(10, "Members", Nested, GroupMembers),
    });

    public static readonly AuxSchema PlayerIndex = new("PlayerIndex", new[]
    {
        F(0,  "Credits", U64), F(1, "XPDebt", U32), F(2, "SecureInv", Nested, SecureInv),
        F(3,  "VendorInv", Nested, VendorInv), F(4, "RewardInv", Nested, RewardInv),
        F(5,  "OverflowInv", Nested, OverflowInv), F(6, "RPGInfo", Nested, RPGInfo),
        F(7,  "CommunityEventFlags", Str), F(8, "MusicID", U32),
        new AuxField(9, "Missions", Nested, Missions, extSuppressed: true),
        F(10, "Reputation", Nested, Reputation), F(11, "PIPAvatarID", U32),
        F(12, "RegistrationStarbase", Str), F(13, "RegistrationStarbaseSector", Str),
        F(14, "SectorName", Str), F(15, "SectorNum", U32), F(16, "ClientSendUITriggers", U32),
        F(17, "GroupInfo", Nested, GroupInfo),
    }, hasHeader: true);

    // ═══ ship tree (AuxShipIndex.cpp + nested) ════════════════════════════════
    public static readonly AuxSchema Percent = new("Percent", new[]
    {
        F(0, "EndTime", U32), F(1, "ChangePerTick", F32), F(2, "StartValue", F32),
    });
    public static readonly AuxSchema Shake = new("Shake", new[]
    {
        F(0, "ForceX", F32), F(1, "ForceY", F32), F(2, "ForceZ", F32), F(3, "Damage", F32),
    });
    public static readonly AuxSchema QuadrantDamage = new("QuadrantDamage", new[]
    {
        F(0, "Slot1", F32), F(1, "Slot2", F32), F(2, "Slot3", F32), F(3, "Slot4", F32),
    });
    public static readonly AuxSchema Stats = new("Stats", new[]
    {
        F(0, "Defence", S32), F(1, "MissleDefence", S32), F(2, "Speed", S32), F(3, "WarpSpeed", S32),
        F(4, "WarpPowerLevel", S32), F(5, "TurnRate", S32), F(6, "ScanRange", S32),
        F(7, "Visibility", S32), F(8, "ResistImpact", S32), F(9, "ResistExplosion", S32),
        F(10, "ResistPlasma", S32), F(11, "ResistEnergy", S32), F(12, "ResistEMP", S32),
        F(13, "ResistChemical", S32), F(14, "ResistPsionic", S32),
    });

    public static readonly AuxSchema Effect = new("Effect", new[]
    {
        F(0, "Range", F32), F(1, "Usage", U32), F(2, "Targets", U32), F(3, "Validity", U32),
    });
    public static readonly AuxSchema EquipItem = new("EquipItem", new[]
    {
        F(0, "ItemTemplateID", S32), F(1, "StackCount", U32), F(2, "Price", U64), F(3, "AveCost", F32),
        F(4, "Structure", F32), F(5, "Quality", F32), F(6, "InstanceInfo", Str),
        F(7, "ActivatedEffectInstanceInfo", Str), F(8, "EquipEffectInstanceInfo", Str),
        F(9, "BuilderName", Str), F(10, "ReadyTime", U32), F(11, "TargetRange", F32),
        F(12, "ItemState", U32), F(13, "Effect", Nested, Effect),
    });
    public static readonly AuxSchema EquipInv = new("EquipInv", 20, EquipItem);

    public static readonly AuxSchema Element = new("Element", new[]
    {
        F(0, "SourceEntity", Str), F(1, "SourceObject", Str), F(2, "Magnitude", U32),
        F(3, "IsActive", U8), F(4, "ExpirationTime", U32),
    });
    public static readonly AuxSchema Elements = new("Elements", 4, Element);
    public static readonly AuxSchema Buff = new("Buff", new[]
    {
        F(0, "BuffType", Str), F(1, "ScrubTypeName", Str), F(2, "IsPermanent", U8),
        F(3, "BuffRemovalTime", U32), F(4, "Elements", Nested, Elements),
    });
    public static readonly AuxSchema Buffs = new("Buffs", 16, Buff);

    public static readonly AuxSchema Attachment = new("Attachment", new[]
    {
        F(0, "BoneName", Str), F(1, "Type", U32), F(2, "Asset", U32), F(3, "DataStr", Str),
    });
    public static readonly AuxSchema Attachments = new("Attachments", 18, Attachment);
    public static readonly AuxSchema Lego = new("Lego", new[]
    {
        F(0, "Scale", F32), F(1, "Attachments", Nested, Attachments),
    });

    public static readonly AuxSchema Inventory40 = new("Inventory40", 40, Item);
    public static readonly AuxSchema Inventory20 = new("Inventory20", 20, Item);
    public static readonly AuxSchema Inventory6 = new("Inventory6", 6, Item);
    public static readonly AuxSchema Inventory1 = new("Inventory1", 1, Item);

    // AuxMounts: container of 20, each present slot emits 12 inline bytes (Mount3).
    public static readonly AuxSchema MountsCont = MakeMounts();
    private static AuxSchema MakeMounts()
    {
        // model the container's slots as Mount3 inline fields.
        var slots = new AuxField[20];
        for (int i = 0; i < 20; i++) slots[i] = new AuxField(i, $"Mount[{i}]", AuxKind.Mount3);
        return new AuxSchema("Mounts", slots);
    }
    public static readonly AuxSchema MountBoneNames = MakeMountBoneNames();
    private static AuxSchema MakeMountBoneNames()
    {
        var slots = new AuxField[20];
        for (int i = 0; i < 20; i++) slots[i] = new AuxField(i, $"Bone[{i}]", AuxKind.Str);
        return new AuxSchema("MountBoneNames", slots);
    }

    public static readonly AuxSchema ShipInv = new("ShipInv", new[]
    {
        F(0, "CargoSpace", U32), F(1, "EquipMountModel", Str), F(2, "Mounts", Nested, MountsCont),
        F(3, "MountBones", Nested, MountBoneNames), F(4, "FutureWeapons", U32),
        F(5, "FutureDevices", U32), F(6, "CargoInv", Nested, Inventory40),
        F(7, "EquipInv", Nested, EquipInv), F(8, "AmmoInv", Nested, Inventory20),
        F(9, "HullInv", Nested, Inventory20), F(10, "TradeInv", Nested, Inventory6),
    });

    public static readonly AuxSchema Damage = new("Damage", System.Array.Empty<AuxField>()); // no-op (commented out server-side)

    public static readonly AuxSchema ShipIndex = new("ShipIndex", new[]
    {
        F(0,  "Name", Str), F(1, "Owner", Str), F(2, "Title", Str), F(3, "Rank", Str),
        F(4,  "Energy", Nested, Percent), F(5, "MaxEnergy", F32), F(6, "Shield", Nested, Percent),
        F(7,  "MaxShield", F32), F(8, "HullPoints", F32), F(9, "MaxHullPoints", F32),
        F(10, "MaxTiltRate", F32), F(11, "MaxTurnRate", F32), F(12, "MaxTiltAngle", F32),
        F(13, "MaxSpeed", F32), F(14, "MinSpeed", F32), F(15, "Acceleration", F32),
        F(16, "LockSpeed", U8), F(17, "LockOrient", U8), F(18, "AutoLevel", U8),
        F(19, "IsCloaked", U8), F(20, "IsCountermeasureActive", U8), F(21, "IsIncapacitated", U8),
        F(22, "IsOrganic", U8), F(23, "IsInPVP", U8), F(24, "IsAutoFollowing", U8),
        F(25, "IsRescueBeaconActive", U8), F(26, "CombatLevel", U32), F(27, "TargetGameID", U32),
        F(28, "TargetThreat", Str), F(29, "TargetThreatSound", Str), F(30, "TargetThreatLevel", U32),
        F(31, "PrivateWarpState", U32), F(32, "GlobalWarpState", U32), F(33, "WarpAvailable", U32),
        F(34, "WarpTriggerTime", U32), F(35, "Shake", Nested, Shake), F(36, "Inventory", Nested, ShipInv),
        F(37, "QuadrantDamage", Nested, QuadrantDamage), F(38, "DamageSpot", Empty, Damage),
        F(39, "DamageLine", Empty, Damage), F(40, "DamageBlotch", Empty, Damage),
        F(41, "Lego", Nested, Lego), F(42, "Buffs", Nested, Buffs), F(43, "TradeMoney", U64),
        F(44, "BaseStats", Nested, Stats), F(45, "CurrentStats", Nested, Stats),
        F(46, "EngineThrustState", U32), F(47, "EngineTrailType", U32), F(48, "GuildName", Str),
        F(49, "GuildRank", U32), F(50, "GuildRankName", Str), F(51, "SameGuildTagColor", Float3),
        F(52, "OtherGuildTagColor", Float3), F(53, "InterruptAbilityName", Str),
        F(54, "InterruptState", U32), F(55, "InterruptActivationTime", U32),
        F(56, "InterruptProgress", F32), F(57, "FactionIdentifier", Str),
    }, hasHeader: true);

    // ═══ harvestable resource node (AuxHarvestable.cpp, Flags[1]) ═════════════
    public static readonly AuxSchema Harvestable = new("Harvestable", new[]
    {
        F(0, "Name", Str), F(1, "CargoInv", Nested, Inventory40),
        F(2, "PercentFull", F32), F(3, "TechLevel", U32),
    }, hasHeader: true);

    // ═══ manufacturing tree (AuxManufacturingIndex.cpp + nested) ══════════════
    public static readonly AuxSchema SubCategory = new("SubCategory", new[]
    {
        F(0, "Name", Str), F(1, "SubCategoryID", U32), F(2, "IsVisible", U8),
    });
    public static readonly AuxSchema SubCategories = new("SubCategories", 5, SubCategory);
    public static readonly AuxSchema Category = new("Category", new[]
    {
        F(0, "Name", Str), F(1, "SubCategories", Nested, SubCategories), F(2, "CategoryID", U32),
    });
    public static readonly AuxSchema Categories = new("Categories", 5, Category);
    public static readonly AuxSchema PrimaryCategory = new("PrimaryCategory", new[]
    {
        F(0, "Name", Str), F(1, "Categories", Nested, Categories),
    });
    public static readonly AuxSchema PrimaryCategories = new("PrimaryCategories", 2, PrimaryCategory);

    public static readonly AuxSchema KnownFormula = new("KnownFormula", new[]
    {
        F(0, "ItemName", Str), F(1, "ItemID", U32), F(2, "TechLevel", U32),
    });
    public static readonly AuxSchema KnownFormulas = new("KnownFormulas", 500, KnownFormula);
    public static readonly AuxSchema PreviousAttempts = new("PreviousAttempts", 16, KnownFormula);

    // flagNums per AuxManufacturingIndex::BuildPacket + Reset()/SetData Init() ids.
    public static readonly AuxSchema ManufacturingIndex = new("ManufacturingIndex", new[]
    {
        F(0,  "Name", Str), F(1, "Mode", U32), F(2, "Validity", U32), F(3, "FailureMessage", Str),
        F(4,  "Difficulty", U32), F(5, "Target", Nested, Inventory1),
        F(6,  "Components", Nested, Inventory6), F(7, "Override", Nested, Inventory1),
        F(8,  "NegotiatedCost", U64), F(9, "BaseCost", U64),
        F(10, "PrimaryCategories", Nested, PrimaryCategories), F(11, "KnownFormulas", Nested, KnownFormulas),
        F(12, "PreviousAttempts", Nested, PreviousAttempts), F(13, "CurrentItemCat", U32),
        F(14, "SuccessProbability", F32), F(15, "CriticalSuccessProbability", F32),
        F(16, "ExpectedQuality", F32), F(17, "MinimumQuality", F32), F(18, "MaximumQuality", F32),
        F(19, "AdditionalIterations", U32), F(20, "TechFilterBitField", U32),
    }, hasHeader: true);
}
