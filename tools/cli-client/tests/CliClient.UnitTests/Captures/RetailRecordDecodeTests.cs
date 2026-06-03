// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using N7.CliClient.Logging;
using N7.CliClient.Opcodes.Records;
using Xunit;

namespace N7.CliClient.UnitTests.Captures;

/// <summary>
/// Drives the <see cref="PacketRecord"/> decode path (the REPL <c>dump</c>
/// view) against verbatim retail bytes from capture_3.rar, the same way we
/// validated the item codec. Where <see cref="RetailCaptureTests"/> pins the
/// round-trip <em>codecs</em>, this pins the read-only <em>records</em>:
/// AuxDataRecord, ItemBaseRecord, GalaxyMapRecord, MessageStringRecord,
/// RelationshipRecord, AvatarDescriptionRecord, ServerHandoffRecord.
///
/// Every frame in <c>capture3-records.txt</c> is complete -- the capture
/// dump's "Length = N bytes" equals payload+4 (and Aux BodyLen == payload-6),
/// so a divergence here is our decoder, never a truncated fixture. The
/// methodology: feed real bytes in, assert the decoded field VALUES (not the
/// raw ASCII gutter -- decoded strings are quoted, the gutter is not).
/// </summary>
public sealed class RetailRecordDecodeTests
{
    private static readonly IReadOnlyDictionary<string, CaptureFixture> Frames =
        CaptureFixture.Load("capture3-records.txt");

    public RetailRecordDecodeTests()
    {
        // Decode to plain text so the content assertions see no ANSI codes.
        AnsiPalette.Enabled = false;
    }

    private static string Dump(string name)
    {
        var f = Frames[name];
        return PacketRecord.Resolve((ushort)f.Opcode, f.Payload).DumpToString();
    }

    private static AuxDataRecord.AuxSummary AuxSummary(string name)
    {
        var f = Frames[name];
        var s = AuxDataRecord.TryExtractSummary(f.Payload);
        Assert.NotNull(s);
        return s!.Value;
    }

    [Fact]
    public void Fixture_Loads_AllNinetyEightFrames()
    {
        Assert.Equal(98, Frames.Count);
        Assert.Equal(0x25, Frames["itembase_sand"].Opcode);
        Assert.Equal(98, Frames["itembase_sand"].Payload.Length);
        Assert.Equal(0x25, Frames["itembase_terminal_controller_v9"].Opcode);
        Assert.Equal(456, Frames["itembase_terminal_controller_v9"].Payload.Length);
        Assert.Equal(0x25, Frames["itembase_ward_of_muck"].Opcode);
        Assert.Equal(458, Frames["itembase_ward_of_muck"].Payload.Length);
        Assert.Equal(0x34, Frames["clientsettime_roundtrip"].Opcode);
        Assert.Equal(12, Frames["clientsettime_roundtrip"].Payload.Length);
        Assert.Equal(0x1B, Frames["aux_ship_turret"].Opcode);
        Assert.Equal(0x1B, Frames["aux_abilityvar_cloak_disable"].Opcode);
        Assert.Equal(35, Frames["aux_abilityvar_cloak_disable"].Payload.Length);
        Assert.Equal(0x1B, Frames["aux_abilityvar_float_value"].Opcode);
        Assert.Equal(43, Frames["aux_abilityvar_float_value"].Payload.Length);
        Assert.Equal(0x11, Frames["colorization_default"].Opcode);
        Assert.Equal(134, Frames["colorization_default"].Payload.Length);
        Assert.Equal(0x97, Frames["galaxymap_system_aragoth"].Opcode);
        Assert.Equal(0x61, Frames["avatardesc_ace"].Opcode);
        Assert.Equal(260, Frames["avatardesc_ace"].Payload.Length);
        Assert.Equal(0x3A, Frames["serverhandoff_friendship7_to_glenn"].Opcode);
        Assert.Equal(115, Frames["serverhandoff_friendship7_to_glenn"].Payload.Length);
        Assert.Equal(0x3A, Frames["serverhandoff_glenn_to_asteroidbelt"].Opcode);
        Assert.Equal(135, Frames["serverhandoff_glenn_to_asteroidbelt"].Payload.Length);
        Assert.Equal(0x36, Frames["serverredirect_to_glenn"].Opcode);
        Assert.Equal(10, Frames["serverredirect_to_glenn"].Payload.Length);
        Assert.Equal(0x36, Frames["serverredirect_to_asteroidbelt"].Opcode);
        Assert.Equal(10, Frames["serverredirect_to_asteroidbelt"].Payload.Length);
        Assert.Equal(0x3E, Frames["advpos_minimal_bitmask0"].Opcode);
        Assert.Equal(42, Frames["advpos_minimal_bitmask0"].Payload.Length);
        Assert.Equal(0x3E, Frames["advpos_full_bitmask01ff"].Opcode);
        Assert.Equal(98, Frames["advpos_full_bitmask01ff"].Payload.Length);
        Assert.Equal(0xA5, Frames["chatevent_join_general"].Opcode);
        Assert.Equal(37, Frames["chatevent_join_general"].Payload.Length);
        Assert.Equal(0xA5, Frames["chatevent_market_wtb"].Opcode);
        Assert.Equal(187, Frames["chatevent_market_wtb"].Payload.Length);
        Assert.Equal(0x04, Frames["create_friendship_06ee13de"].Opcode);
        Assert.Equal(23, Frames["create_friendship_06ee13de"].Payload.Length);
        Assert.Equal(0x04, Frames["create_asset_ffff_type39"].Opcode);
        Assert.Equal(23, Frames["create_asset_ffff_type39"].Payload.Length);
        Assert.Equal(0x04, Frames["create_scaled_quarter"].Opcode);
        Assert.Equal(23, Frames["create_scaled_quarter"].Payload.Length);
        Assert.Equal(0x19, Frames["settarget_clear_basic"].Opcode);
        Assert.Equal(8, Frames["settarget_clear_basic"].Payload.Length);
        Assert.Equal(0x19, Frames["settarget_clear_gameid0"].Opcode);
        Assert.Equal(8, Frames["settarget_clear_gameid0"].Payload.Length);
        Assert.Equal(0x07, Frames["remove_obj_355a56"].Opcode);
        Assert.Equal(4, Frames["remove_obj_355a56"].Payload.Length);
        Assert.Equal(0x07, Frames["remove_obj_163d32"].Opcode);
        Assert.Equal(4, Frames["remove_obj_163d32"].Payload.Length);
        Assert.Equal(0x09, Frames["objeffect_obj_06ee13de"].Opcode);
        Assert.Equal(15, Frames["objeffect_obj_06ee13de"].Payload.Length);
        Assert.Equal(0x09, Frames["objeffect_obj_3ad922"].Opcode);
        Assert.Equal(15, Frames["objeffect_obj_3ad922"].Payload.Length);
        Assert.Equal(0x0F, Frames["removeeffect_3b0295"].Opcode);
        Assert.Equal(4, Frames["removeeffect_3b0295"].Payload.Length);
        Assert.Equal(0x0F, Frames["removeeffect_3b0329"].Opcode);
        Assert.Equal(4, Frames["removeeffect_3b0329"].Payload.Length);
        Assert.Equal(0x40, Frames["constpos_origin_06ee13f7"].Opcode);
        Assert.Equal(32, Frames["constpos_origin_06ee13f7"].Payload.Length);
        Assert.Equal(0x40, Frames["constpos_realpos_86"].Opcode);
        Assert.Equal(32, Frames["constpos_realpos_86"].Payload.Length);
        Assert.Equal(0x40, Frames["constpos_oriented_94"].Opcode);
        Assert.Equal(32, Frames["constpos_oriented_94"].Payload.Length);
        Assert.Equal(0x5C, Frames["verb_obj450_dis_toofar"].Opcode);
        Assert.Equal(16, Frames["verb_obj450_dis_toofar"].Payload.Length);
        Assert.Equal(0x5C, Frames["verb_obj450_both_passes"].Opcode);
        Assert.Equal(20, Frames["verb_obj450_both_passes"].Payload.Length);
        Assert.Equal(0x92, Frames["camera_msg4_obj450"].Opcode);
        Assert.Equal(8, Frames["camera_msg4_obj450"].Payload.Length);
        Assert.Equal(0x92, Frames["camera_msg3_obj3a1ec7"].Opcode);
        Assert.Equal(8, Frames["camera_msg3_obj3a1ec7"].Payload.Length);
        Assert.Equal(0x99, Frames["nav_obj_c5_type1"].Opcode);
        Assert.Equal(14, Frames["nav_obj_c5_type1"].Payload.Length);
        Assert.Equal(0x99, Frames["nav_obj_94_type2"].Opcode);
        Assert.Equal(14, Frames["nav_obj_94_type2"].Payload.Length);
        Assert.Equal(0x10, Frames["decal_obj_06ee13de"].Opcode);
        Assert.Equal(54, Frames["decal_obj_06ee13de"].Payload.Length);
        Assert.Equal(0x10, Frames["decal_obj_3ad922"].Opcode);
        Assert.Equal(54, Frames["decal_obj_3ad922"].Payload.Length);
        Assert.Equal(0xB2, Frames["namedecal_revenge_jenquai"].Opcode);
        Assert.Equal(48, Frames["namedecal_revenge_jenquai"].Payload.Length);
        Assert.Equal(0xB2, Frames["namedecal_blitzer_colored"].Opcode);
        Assert.Equal(48, Frames["namedecal_blitzer_colored"].Payload.Length);
        Assert.Equal(0xB4, Frames["subparts_obj_06ee13de"].Opcode);
        Assert.Equal(54, Frames["subparts_obj_06ee13de"].Payload.Length);
        Assert.Equal(0x9C, Frames["warpindex_one"].Opcode);
        Assert.Equal(4, Frames["warpindex_one"].Payload.Length);
        Assert.Equal(0x9C, Frames["warpindex_none"].Opcode);
        Assert.Equal(4, Frames["warpindex_none"].Payload.Length);
        Assert.Equal(0x05, Frames["start_avatarid_10069"].Opcode);
        Assert.Equal(4, Frames["start_avatarid_10069"].Payload.Length);
        Assert.Equal(0x05, Frames["start_avatarid_3126"].Opcode);
        Assert.Equal(4, Frames["start_avatarid_3126"].Payload.Length);
        Assert.Equal(0x08, Frames["simplepos_docked_obj_cb"].Opcode);
        Assert.Equal(48, Frames["simplepos_docked_obj_cb"].Payload.Length);
        Assert.Equal(0x08, Frames["simplepos_oriented_obj_6c"].Opcode);
        Assert.Equal(48, Frames["simplepos_oriented_obj_6c"].Payload.Length);
        Assert.Equal(0x37, Frames["clientavatar_your_ship_06ee13de"].Opcode);
        Assert.Equal(4, Frames["clientavatar_your_ship_06ee13de"].Payload.Length);
        Assert.Equal(0x37, Frames["clientavatar_npc_3b027d"].Opcode);
        Assert.Equal(4, Frames["clientavatar_npc_3b027d"].Payload.Length);
        Assert.Equal(0x3C, Frames["clienttype_space_zero"].Opcode);
        Assert.Equal(4, Frames["clienttype_space_zero"].Payload.Length);
        Assert.Equal(0x3F, Frames["planetpos_static_cc"].Opcode);
        Assert.Equal(48, Frames["planetpos_static_cc"].Payload.Length);
        Assert.Equal(0x3F, Frames["planetpos_static_76"].Opcode);
        Assert.Equal(48, Frames["planetpos_static_76"].Payload.Length);
        Assert.Equal(0x42, Frames["serverparams_glenn_4515"].Opcode);
        Assert.Equal(70, Frames["serverparams_glenn_4515"].Payload.Length);
        Assert.Equal(0x42, Frames["serverparams_asteroidbelt_1077"].Opcode);
        Assert.Equal(70, Frames["serverparams_asteroidbelt_1077"].Payload.Length);
        Assert.Equal(0x47, Frames["clientship_your_ship_06ee13de"].Opcode);
        Assert.Equal(4, Frames["clientship_your_ship_06ee13de"].Payload.Length);
        Assert.Equal(0x47, Frames["clientship_npc_3b027d"].Opcode);
        Assert.Equal(4, Frames["clientship_npc_3b027d"].Payload.Length);
        Assert.Equal(0x4F, Frames["starbaseset_b05f_action0"].Opcode);
        Assert.Equal(6, Frames["starbaseset_b05f_action0"].Payload.Length);
        Assert.Equal(0x4F, Frames["starbaseset_b05f_action1"].Opcode);
        Assert.Equal(6, Frames["starbaseset_b05f_action1"].Payload.Length);
        Assert.Equal(0x7F, Frames["manufactureset_manulab_tagged"].Opcode);
        Assert.Equal(4, Frames["manufactureset_manulab_tagged"].Payload.Length);
        Assert.Equal(0x7F, Frames["manufactureset_reset_zero"].Opcode);
        Assert.Equal(4, Frames["manufactureset_reset_zero"].Payload.Length);
        Assert.Equal(0x52, Frames["loungenpc_friendship7_full"].Opcode);
        Assert.Equal(3400, Frames["loungenpc_friendship7_full"].Payload.Length);
        Assert.Equal(0x0A, Frames["pointeffect_satellite_7392"].Opcode);
        Assert.Equal(40, Frames["pointeffect_satellite_7392"].Payload.Length);
        Assert.Equal(0x0A, Frames["pointeffect_satellite_7637"].Opcode);
        Assert.Equal(40, Frames["pointeffect_satellite_7637"].Payload.Length);
        Assert.Equal(0x0E, Frames["linkedeffect_e1_33_e2_3_speed1"].Opcode);
        Assert.Equal(58, Frames["linkedeffect_e1_33_e2_3_speed1"].Payload.Length);
        Assert.Equal(0x0E, Frames["linkedeffect_e1_33_e2_3_speed2"].Opcode);
        Assert.Equal(58, Frames["linkedeffect_e1_33_e2_3_speed2"].Payload.Length);
    }

    // ── ItemBase 0x25 ────────────────────────────────────────────────────────
    // The decoder that "got items working" -- prove it on two real ore items.

    [Fact]
    public void ItemBase_Sand_DecodesFully()
    {
        string d = Dump("itembase_sand");

        Assert.Contains("ItemTemplateID", d);
        Assert.Contains("0x00000167", d);          // 359, BE on the wire
        Assert.Contains("Category          = -1", d);   // ore: category/subcat sentinel -1
        Assert.Contains("ItemType          = 13", d);
        Assert.Contains("\"Level 1\"", d);          // Field[0] Item Type (Subdesc)
        Assert.Contains("Name", d);
        Assert.Contains("\"Sand\"", d);
        Assert.Contains("\"Refines to: Silicon\"", d);
        Assert.Contains("MaxStack", d);
        Assert.Contains("300", d);
        Assert.Contains("NO_MANUFACTURE", d);       // Flags 0x80
        Assert.Contains("TechLevel", d);

        // A complete item must decode with no truncation flag and no large
        // trailing gap -- the whole payload is accounted for.
        Assert.DoesNotContain("truncated", d);
        Assert.DoesNotContain("AUX_DATA truncated", d);
    }

    [Fact]
    public void ItemBase_OxiumOre_DecodesNameDescriptionAndEconomy()
    {
        string d = Dump("itembase_oxium_ore");

        Assert.Contains("0x000004FB", d);           // 1275
        Assert.Contains("\"Oxium Ore\"", d);
        Assert.Contains("\"Refines to: Oxium\"", d);
        Assert.Contains("\"Level 6\"", d);
        Assert.Contains("TechLevel", d);
        Assert.Contains("Cost", d);
        Assert.Contains("754", d);                  // tradein cost
        Assert.DoesNotContain("truncated", d);
    }

    // The AddDataLS string prefix is a plain u16 LE byte count. The ore items
    // above only ever exercise the low byte (their strings are < 256 bytes, so
    // the high byte is always 0x00). This item's 320-byte Description forces the
    // high byte: its prefix is 40 01 == 0x0140. A low-byte-only reading (the
    // earlier "printable count + format code" model) truncated it to 64 chars
    // and then desynced the Manufacturer field. Pin the WHOLE description so a
    // regression that drops the high byte fails the build.
    [Fact]
    public void ItemBase_TerminalControllerV9_DecodesFull320ByteDescription()
    {
        string d = Dump("itembase_terminal_controller_v9");

        Assert.Contains("0x00001DC4", d);                       // 7620, BE on the wire
        Assert.Contains("\"Terminal Controller v9.0\"", d);

        // The full 320-byte description, byte-pinned start to end. The closing
        // "after one use." only appears if all 320 bytes were read -- a 64-char
        // truncation would stop at "...origin. This devic".
        Assert.Contains(
            "\"Illicit technology of unknown origin. This device, when inserted " +
            "into a compatible terminal control interface, refines the inner " +
            "workings of the terminal to assist your requested operation. " +
            "Unfortunately, terminal security systems will detect unauthorized " +
            "modifications upon initiation and fry this device after one use.\"",
            d);

        // Manufacturer is the field that desynced under the old model -- here it
        // is present and empty, which only parses if the description ended on the
        // right byte.
        Assert.Contains("Manufacturer      = \"\"", d);

        // Mixed field types in the variable header decode too: a string field,
        // a float field (Terminal Override Skill+ == 1/3), an int field.
        Assert.Contains("\"Level 9 Manufacture Override\"", d);
        Assert.Contains("Terminal Override Skill+", d);
        Assert.Contains("0.333", d);
        Assert.Contains("NO_MANUFACTURE", d);                   // Flags 0x80

        // Whole 456-byte payload accounted for: no truncation flag, no gap.
        Assert.DoesNotContain("truncated", d);
        Assert.DoesNotContain("???", d);
    }

    // The ore and terminal-controller items above all carry zero effects, so the
    // ReadEffect path -- the most intricate part of the decoder -- had no
    // capture-pinned coverage. This Device carries two equippable effects. Pin
    // the whole substructure: per-effect Name/Description/Tooltip strings, the
    // BE int32 DescVarCount, the BE-float DescVar bit pattern (45 aa a0 00 decodes
    // to 5460.0, 41 dc 00 00 to 27.5 -- a low/big-endian slip would yield garbage),
    // and both LE int32 effect flags. A desync anywhere in effect 0 would corrupt
    // every field after it, so pinning effect 1's strings and the trailing economy
    // block transitively proves effect 0 consumed exactly the right number of bytes.
    [Fact]
    public void ItemBase_WardOfMuck_DecodesBothEquippableEffects()
    {
        string d = Dump("itembase_ward_of_muck");

        Assert.Contains("0x00001A6D", d);                       // 6765, BE on the wire
        Assert.Contains("Category          = 11  (Device)", d);

        // Field 0x1E has no authoritative name -- it stays the honest '????'
        // placeholder. Pinning it guards against a future change inventing a name
        // (the ItemBase fabrication trap) AND against dropping the field entirely.
        Assert.Contains("Field[2].ID     = 30  ????", d);
        Assert.Contains("Field[2].Value  = 2750", d);

        // Effect counts: zero activatable, two equippable.
        Assert.Contains("ActEffects.Count  = 0", d);
        Assert.Contains("EqEffects.Count   = 2", d);

        // Effect 0 -- all three AddDataLS strings, the DescVar, and both flags.
        Assert.Contains("\"Increase Shield Capacity 5000 Item P\"", d);
        Assert.Contains("\"Increase Shield Capacity (Equip)\"", d);
        Assert.Contains("\"+%value0.0f% Shield Capacity when equipped.\"", d);
        Assert.Contains("EqEffect[0].DescVarCount= 1", d);
        Assert.Contains("EqEffect[0].DescVar[0]= 5460", d);     // BE float 45 aa a0 00
        Assert.Contains("EqEffect[0].Flag1 = 0x00000002", d);
        Assert.Contains("EqEffect[0].Flag2 = 0x00000001", d);

        // Effect 1 -- only reachable if effect 0 consumed exactly to its boundary.
        Assert.Contains("\"Energy Resistance Item P\"", d);
        Assert.Contains("\"Deflect Energy (Equip)\"", d);
        Assert.Contains("\"+%value2.0f% Energy Deflect when equipped.\"", d);
        Assert.Contains("EqEffect[1].DescVar[0]= 27.5", d);     // BE float 41 dc 00 00
        Assert.Contains("EqEffect[1].Flag1 = 0x00000002", d);

        // The 16-byte EqEffects filler block follows both effects.
        Assert.Contains("EqEffects.Filler[0]= 0", d);
        Assert.Contains("EqEffects.Filler[3]= 0", d);

        // Trailing economy + identity block -- only on the right byte if every
        // preceding variable-length field consumed correctly.
        Assert.Contains("Cost              = 156000", d);
        Assert.Contains("Flags             = 0x00000084  (132)  (UNIQUE | NO_MANUFACTURE)", d);
        Assert.Contains("\"Ward of Muck\"", d);
        Assert.Contains("\"This filthy device offers a good deal of protection.\"", d);
        Assert.Contains("Manufacturer      = \"\"", d);

        // The whole 458-byte payload is accounted for: no truncation flag and no
        // auto-rendered undecoded-byte gap ('(NB)'). The '????' field-name
        // placeholder above is deliberate and is NOT such a gap.
        Assert.DoesNotContain("truncated", d);
        Assert.DoesNotContain("(NB)", d);
    }

    // ── Aux 0x1B ShipIndex ───────────────────────────────────────────────────
    // The world-model summary path AND the dump path, on real mob ships.

    [Fact]
    public void Aux_ShipTurret_SummaryAndDump_AgreeOnNameAndLevel()
    {
        var sum = AuxSummary("aux_ship_turret");
        Assert.Equal(0x00000A6Fu, sum.GameId);
        Assert.Equal("Starbase Guardian Turret", sum.Name);
        Assert.Equal(66u, sum.CombatLevel);
        Assert.Null(sum.MaxSpeed);                  // a turret announces no MaxSpeed

        string d = Dump("aux_ship_turret");
        Assert.Contains("ShipIndex", d);
        Assert.Contains("\"Starbase Guardian Turret\"", d);
        Assert.Contains("CombatLevel", d);
        Assert.Contains("66", d);
        Assert.Contains("HullPoints", d);
        Assert.Contains("329412", d);               // hull == max hull
    }

    [Fact]
    public void Aux_ShipCruiser_SummaryAndDump_AgreeOnNameAndLevel()
    {
        var sum = AuxSummary("aux_ship_cruiser");
        Assert.Equal(0x00000B02u, sum.GameId);
        Assert.Equal("Shinwa Patrol Cruiser", sum.Name);
        Assert.Equal(25u, sum.CombatLevel);

        string d = Dump("aux_ship_cruiser");
        Assert.Contains("ShipIndex", d);
        Assert.Contains("\"Shinwa Patrol Cruiser\"", d);
        Assert.Contains("CombatLevel", d);
        Assert.Contains("25", d);
    }

    // ── Aux 0x1B Harvestable ─────────────────────────────────────────────────
    // Same opcode, different schema -- the registry picks Harvestable here.

    [Fact]
    public void Aux_HarvestableGate_DecodesNavName_NoCombatLevel()
    {
        var sum = AuxSummary("aux_harvestable_gate");
        Assert.Equal(0x00000A24u, sum.GameId);
        Assert.Equal("Sector Gate to Jupiter", sum.Name);
        Assert.Null(sum.CombatLevel);

        string d = Dump("aux_harvestable_gate");
        Assert.Contains("Harvestable", d);
        Assert.Contains("\"Sector Gate to Jupiter\"", d);
    }

    [Fact]
    public void Aux_HarvestableHalon_DecodesResourceName()
    {
        var sum = AuxSummary("aux_harvestable_halon");
        Assert.Equal(0x0084E261u, sum.GameId);
        Assert.Equal("Halon", sum.Name);

        string d = Dump("aux_harvestable_halon");
        Assert.Contains("Harvestable", d);
        Assert.Contains("\"Halon\"", d);
    }

    // ── Aux 0x1B ability-var update (version byte 0) ─────────────────────────
    // A different 0x1B sub-protocol from the AuxBase object-indexes above: the
    // player-variable ability-state list (Player::SendProspectAUX). A version
    // byte of 0 is the discriminator -- every AuxBase Build*Packet writes 1, so
    // these are never a top-level index. The body is a flat (abilityID,value)
    // array, NOT a flag-walked struct, so the schema registry must not claim it.

    [Fact]
    public void Aux_AbilityVar_CloakDisable_DecodesEveryEntry()
    {
        string d = Dump("aux_abilityvar_cloak_disable");

        Assert.Contains("AbilityVarUpdate", d);
        Assert.Contains("GameID            = 0x00000000", d);
        Assert.Contains("(self / player-var)", d);
        Assert.Contains("Version           = 0", d);
        Assert.Contains("not a top-level AuxIndex build", d);
        Assert.Contains("Count             = 2", d);
        // Both cloak-ability slots, byte-pinned id AND value.
        Assert.Contains("[0] AbilityID     = 0x00000C15", d);   // 3093
        Assert.Contains("[0] Value         = 0x00000100  (256)", d);
        Assert.Contains("[1] AbilityID     = 0x00000CF5", d);   // 3317
        Assert.Contains("[1] Value         = 0x00000100  (256)", d);
        Assert.Contains("Trailing          = 00 00 00 00 00 00 00 00", d);
        // A clean, fully-consumed decode: no schema-walk gap, no flag, no
        // mis-classification as a top-level index.
        Assert.DoesNotContain("???", d);
        Assert.DoesNotContain("[!]", d);
        Assert.DoesNotContain("AuxType           = ShipIndex", d);
        Assert.DoesNotContain("PARTIAL", d);
    }

    [Fact]
    public void Aux_AbilityVar_FloatValue_GlossesTheFloatBitPattern()
    {
        string d = Dump("aux_abilityvar_float_value");

        Assert.Contains("AbilityVarUpdate", d);
        Assert.Contains("Count             = 3", d);
        Assert.Contains("[0] AbilityID     = 0x00000D75", d);   // 3445
        Assert.Contains("[0] Value         = 0x00000100  (256)", d);
        Assert.Contains("[1] AbilityID     = 0x00000E35", d);   // 3637
        Assert.Contains("[2] AbilityID     = 0x00001161", d);   // 4449
        // The third value's bit pattern is a finite float -- shown as hex with
        // an f32 gloss. 0x3F51B879 == 0.819...
        Assert.Contains("[2] Value         = 0x3F51B879", d);
        Assert.Contains("(f32 0.819)", d);
        Assert.Contains("Trailing          = 00 00 00 00 00 00 00 00", d);
        Assert.DoesNotContain("???", d);
        Assert.DoesNotContain("[!]", d);
    }

    [Fact]
    public void Aux_AbilityVar_IsNotMisreadAsTopLevelIndex()
    {
        // The registry's schema-walk path is gated on version byte == 1. Prove a
        // version-0 frame never produces an AuxType index match (which would mean
        // a wrong schema happened to consume the bytes) -- it must take the
        // dedicated ability-var path instead.
        var sum = AuxDataRecord.TryExtractSummary(Frames["aux_abilityvar_cloak_disable"].Payload);
        Assert.Null(sum);   // not a summarisable object index
    }

    // ── GalaxyMap 0x97 ───────────────────────────────────────────────────────

    [Fact]
    public void GalaxyMap_DecodesSystemSectorStation()
    {
        string d = Dump("galaxymap_sol_io");

        Assert.Contains("PlayerID", d);
        Assert.Contains("0x0000141E", d);           // 5150
        Assert.Contains("\"Sol\"", d);
        Assert.Contains("\"Io\"", d);
        Assert.Contains("\"Nishino Research Facility\"", d);
        Assert.Contains("map update", d);           // Type 4
    }

    // 0x97 is multiplexed on a leading Type. Our server only emits Type 4, but
    // retail uses 3/5/6/7/8/9 for nav/map detail -- the record must dispatch and
    // surface the embedded (verifiable) name instead of mis-reading the Type-4
    // string layout over them.

    [Fact]
    public void GalaxyMap_Type5_DecodesStarSystemName()
    {
        string d = Dump("galaxymap_system_aragoth");

        Assert.Contains("Type", d);
        Assert.Contains("nav/map-detail subtype", d);
        Assert.Contains("\"Aragoth\"", d);
        // Must NOT pretend the Type-4 layout applies.
        Assert.DoesNotContain("map update", d);
        Assert.DoesNotContain("PlayerID", d);
        Assert.DoesNotContain("expected 375", d);
    }

    [Fact]
    public void GalaxyMap_Type9_DecodesSectorName()
    {
        string d = Dump("galaxymap_sector_earth");

        Assert.Contains("nav/map-detail subtype", d);
        Assert.Contains("\"Earth\"", d);
        Assert.DoesNotContain("PlayerID", d);
    }

    // ── MessageString 0x1D ───────────────────────────────────────────────────

    [Fact]
    public void MessageString_DecodesDockingBanner()
    {
        string d = Dump("messagestring_docking");

        Assert.Contains("Length", d);
        Assert.Contains("78", d);                   // strlen+1
        Assert.Contains("Color", d);
        Assert.Contains("top panel green", d);      // color 5
        Assert.Contains(
            "\"Now docking at Nishino Research Facility, training grounds of the Sha'ha'dem.\"",
            d);
    }

    // ── ClientSetTime 0x34 ───────────────────────────────────────────────────
    // The server processes the time-sync request in nonzero ticks, so retail
    // sends ServerSent = ServerReceived + 1. The only real anomaly is the clock
    // running backwards (ServerSent < ServerReceived); a positive latency is
    // normal and must NOT raise a flag.

    [Fact]
    public void ClientSetTime_PositiveServerLatency_IsNotFlagged()
    {
        string d = Dump("clientsettime_roundtrip");

        Assert.Contains("ClientSent        = 0x00101ED0", d);
        Assert.Contains("ServerReceived    = 0x257E789D", d);   // 629045405
        Assert.Contains("ServerSent        = 0x257E789E", d);   // 629045406, +1
        Assert.Contains("+1 tick server latency", d);
        // A well-formed +1 round-trip must not be flagged as anomalous.
        Assert.DoesNotContain("[!]", d);
        Assert.DoesNotContain("backwards", d);
    }

    [Fact]
    public void ClientSetTime_BackwardsClock_IsFlagged()
    {
        // Synthesise the genuine anomaly: ServerSent one tick BEFORE
        // ServerReceived. The fixture's two server stamps are adjacent, so
        // swapping them produces a backwards-clock frame.
        var f = Frames["clientsettime_roundtrip"];
        byte[] p = (byte[])f.Payload.Clone();
        // serverReceived @4..7, serverSent @8..11 -- swap so sent < received.
        for (int i = 0; i < 4; i++) (p[4 + i], p[8 + i]) = (p[8 + i], p[4 + i]);
        string d = PacketRecord.Resolve((ushort)f.Opcode, p).DumpToString();

        Assert.Contains("[!]", d);
        Assert.Contains("server clock ran backwards", d);
    }

    // ── Relationship 0x89 ────────────────────────────────────────────────────

    [Fact]
    public void Relationship_DecodesObjectIdReactionAttacking()
    {
        string d = Dump("relationship_aaccee");

        Assert.Contains("ObjectID", d);
        Assert.Contains("0x00AACCEE", d);
        Assert.Contains("Reaction          = 2", d);
        Assert.Contains("IsAttacking", d);
        Assert.Contains("false", d);
    }

    // ── Colorization 0x11 ────────────────────────────────────────────────────
    // The counted unit is a slot (primary+secondary pair, 32B), NOT a single
    // colour block. Retail sends ItemCount=4 for a 134-byte body == 8 blocks;
    // decoding count*16 would have left 64 bytes (the Wing+Engine pairs)
    // silently undecoded.

    [Fact]
    public void Colorization_DecodesFourSlotsAsPrimarySecondaryPairs()
    {
        string d = Dump("colorization_default");

        Assert.Contains("GameID", d);
        Assert.Contains("0x00AACCEE", d);
        Assert.Contains("ItemCount", d);
        // All four slots present, each with a primary and a secondary block.
        Assert.Contains("[0] primary", d);
        Assert.Contains("[0] secondary", d);
        Assert.Contains("[3] primary", d);
        Assert.Contains("[3] secondary", d);
        // This default ship is uncoloured: every block is HSV 1, 1, 1.
        Assert.Contains("1, 1, 1", d);
        // The whole 134-byte payload is consumed -- no undecoded gap (the old
        // count*16 reading left "??? (64B)" here), no overrun, no short-read.
        Assert.DoesNotContain("???", d);
        Assert.DoesNotContain("trailing bytes", d);
        Assert.DoesNotContain("too short", d);
    }

    [Fact]
    public void Colorization_ServerCountConvention_DecodesSameEightBlocks()
    {
        // Our server (PlayerClass.cpp) writes ItemCount=8 for the SAME 8-block,
        // 134-byte body where retail writes 4. The decoder derives the block
        // count from the payload length, so a live count=8 frame must decode to
        // the identical four slots -- otherwise the CLI would mis-read every
        // colorization our own server sends.
        var f = Frames["colorization_default"];
        byte[] p = (byte[])f.Payload.Clone();
        Assert.Equal(4, p[4]);            // retail count
        p[4] = 8;                         // tada-o server count, same body
        string d = PacketRecord.Resolve((ushort)f.Opcode, p).DumpToString();

        Assert.Contains("ItemCount", d);
        Assert.Contains("flat blocks", d);   // note recognises the server convention
        Assert.Contains("[0] primary", d);
        Assert.Contains("[3] secondary", d);
        Assert.DoesNotContain("???", d);
        Assert.DoesNotContain("trailing bytes", d);
    }

    // ── AvatarDescription 0x61 ───────────────────────────────────────────────

    [Fact]
    public void AvatarDescription_DecodesNameRaceProfessionAppearance()
    {
        string d = Dump("avatardesc_ace");

        Assert.Contains("AvatarID", d);
        Assert.Contains("0x0084E1E9", d);
        Assert.Contains("FirstName         = \"Ace\"", d);
        Assert.Contains("LastName          = \"\"", d);
        Assert.Contains("Race", d);
        Assert.Contains("Profession", d);
        Assert.Contains("hair_color", d);           // appearance block decoded
        Assert.Contains("skin_color", d);
        Assert.DoesNotContain("truncated", d);
    }

    // ── ServerHandoff 0x3A ───────────────────────────────────────────────────
    // The sector-transition redirect. Two byte-order traps live here and both
    // have shipped real crashes: ToSectorID/FromSectorID are ntohl'd at emit so
    // they are BIG-endian on the wire (every other MasterJoin field is host LE),
    // and the 20-byte Ticket is opaque binary that must render losslessly. These
    // are real retail handoffs, so the embedded sector/system names double as a
    // sanity check that the field offsets are right.

    [Fact]
    public void ServerHandoff_Friendship7ToGlenn_DecodesHandoffWithBinaryTicket()
    {
        string d = Dump("serverhandoff_friendship7_to_glenn");

        // ToSectorID/FromSectorID are BE on the wire (00 00 11 A3 -> 4515). A
        // host-LE misread would yield 0xA3110000 -- the exact ServerRedirect
        // byte-order class of bug. FromSectorID is genuinely 0 here.
        Assert.Contains("ToSectorID        = 4515  (BE -- ntohl at emit)", d);
        Assert.Contains("FromSectorID      = 0  (BE -- ntohl at emit)", d);
        Assert.Contains("PlayerLevel       = 0", d);

        // The 20-byte Ticket is binary on the wire (retail filled it with random
        // bytes). It must render as lossless hex with NO ASCII gloss -- the gloss
        // only appears when every byte is printable-or-NUL, which this is not.
        Assert.Contains(
            "Ticket            = d0 0f a7 14 12 5f 3f 1f aa 48 da 9e ca 0d 64 04 7b 08 c8 e7",
            d);
        Assert.DoesNotContain("Ticket            = d0 0f a7 14 12 5f 3f 1f aa 48 da 9e ca 0d 64 04 7b 08 c8 e7  \"", d);

        // The four-AddString tail, including the EMPTY FromSystem -- a bare 00 00
        // length prefix. The decoder must consume it as a zero-length string, not
        // skip it (which would shift ToSector/ToSystem by two bytes).
        Assert.Contains("FromSector        = \"Friendship 7 Recreation Port\"", d);
        Assert.Contains("FromSystem        = \"\"", d);
        Assert.Contains("ToSector          = \"Glenn\"", d);
        Assert.Contains("ToSystem          = \"Beta Hydri\"", d);

        // Whole 115-byte payload consumed: no overrun flag, no short-read.
        Assert.DoesNotContain("[!]", d);
        Assert.DoesNotContain("overruns", d);
        Assert.DoesNotContain("truncated", d);
    }

    [Fact]
    public void ServerHandoff_GlennToAsteroidBelt_DecodesFullyPopulatedQuartet()
    {
        string d = Dump("serverhandoff_glenn_to_asteroidbelt");

        Assert.Contains("ToSectorID        = 1077  (BE -- ntohl at emit)", d);   // 0x0435 BE
        Assert.Contains("FromSectorID      = 0  (BE -- ntohl at emit)", d);

        // Distinct binary ticket from the other frame -- proves the field is read
        // per-frame, not a constant.
        Assert.Contains(
            "Ticket            = 28 fa 82 03 19 2a aa e3 1b f3 4e bb 9c 78 8d 27 33 d1 2d 4f",
            d);

        // All four AddStrings non-empty (the complement to the empty-FromSystem
        // frame). The parenthesised FromSector also proves '(' / ')' survive the
        // ASCII string read intact.
        Assert.Contains("FromSector        = \"Glenn Sector (Beta Hydri System)\"", d);
        Assert.Contains("FromSystem        = \"Beta Hydri\"", d);
        Assert.Contains("ToSector          = \"Asteroid Belt Beta\"", d);
        Assert.Contains("ToSystem          = \"Sol\"", d);

        Assert.DoesNotContain("[!]", d);
        Assert.DoesNotContain("overruns", d);
        Assert.DoesNotContain("truncated", d);
    }

    [Fact]
    public void ServerHandoff_PrintableTicket_GetsAsciiGloss()
    {
        // Our own server fills the ticket with printable "username-rand" ASCII
        // (AccountManager.cpp StationAuthEx), unlike retail's random bytes. When
        // every ticket byte is printable-or-NUL the decoder appends a quoted ASCII
        // gloss so our server's tickets stay human-readable. Synthesise that case
        // by overwriting the binary ticket in the capture frame with ASCII.
        var f = Frames["serverhandoff_friendship7_to_glenn"];
        byte[] p = (byte[])f.Payload.Clone();
        byte[] ascii = System.Text.Encoding.ASCII.GetBytes("playername-4242");
        for (int i = 0; i < 20; i++) p[44 + i] = i < ascii.Length ? ascii[i] : (byte)0;
        string d = PacketRecord.Resolve((ushort)f.Opcode, p).DumpToString();

        // Hex stays lossless (the trailing NUL pad is shown), and the gloss drops
        // the NULs to read back the clean name.
        Assert.Contains(
            "Ticket            = 70 6c 61 79 65 72 6e 61 6d 65 2d 34 32 34 32 00 00 00 00 00  \"playername-4242\"",
            d);
        // The surrounding fields are untouched by the ticket edit.
        Assert.Contains("ToSector          = \"Glenn\"", d);
    }

    // ── ServerRedirect 0x36 ──────────────────────────────────────────────────
    // The Client_Redirect that immediately follows a Server_Handoff. This is THE
    // packet whose ntohl byte-order bug shipped a real client crash (the proxy
    // ServerRedirect crash). Two traps live here, both pinned below:
    //   * SectorID is host LITTLE-endian (the OPPOSITE of ServerHandoff's BE
    //     ToSectorID -- the same logical sector, two encodings).
    //   * IP is network byte order; the decoder recovers the dotted quad.

    [Fact]
    public void ServerRedirect_ToGlenn_DecodesSectorIpPort()
    {
        string d = Dump("serverredirect_to_glenn");

        Assert.Contains("SectorID          = 0x000011A3  (4515)", d);   // A3 11 00 00 LE
        Assert.Contains("IP                = 159.153.232.99", d);       // 63 E8 99 9F net-order
        Assert.Contains("Port              = 3500", d);                 // AC 0D LE, standard port
        Assert.DoesNotContain("[!]", d);
        Assert.DoesNotContain("uninitialised", d);
    }

    [Fact]
    public void ServerRedirect_ToAsteroidBelt_DecodesNonStandardPort()
    {
        string d = Dump("serverredirect_to_asteroidbelt");

        Assert.Contains("SectorID          = 0x00000435  (1077)", d);   // 35 04 00 00 LE
        Assert.Contains("IP                = 159.153.232.35", d);
        Assert.Contains("Port              = 3503", d);                 // AF 0D LE -- not 3500
        Assert.DoesNotContain("[!]", d);
    }

    [Fact]
    public void ServerRedirect_SectorId_IsLittleEndian_OppositeOfHandoff()
    {
        // The regression lock for the byte-order trap. ServerRedirect's SectorID
        // is host LE (payload[0..3]); the paired ServerHandoff's ToSectorID is BE
        // (payload[20..23], ntohl at emit). They name the SAME sector, so reading
        // each with its OWN convention must yield the identical id. If a future
        // change "unifies" the two readings to one endianness, exactly one of
        // these decodes flips to a garbage sector and this test breaks -- which is
        // precisely the failure that crashed the client.
        var redirect = Frames["serverredirect_to_glenn"].Payload;
        var handoff  = Frames["serverhandoff_friendship7_to_glenn"].Payload;
        int redirectSector = redirect[0] | redirect[1] << 8 | redirect[2] << 16 | redirect[3] << 24; // LE
        int handoffSector  = handoff[23] | handoff[22] << 8 | handoff[21] << 16 | handoff[20] << 24;  // BE
        Assert.Equal(4515, redirectSector);
        Assert.Equal(4515, handoffSector);
        Assert.Equal(handoffSector, redirectSector);

        // And the same pairing for the second transition (sector 1077).
        var redirect2 = Frames["serverredirect_to_asteroidbelt"].Payload;
        var handoff2  = Frames["serverhandoff_glenn_to_asteroidbelt"].Payload;
        int r2 = redirect2[0] | redirect2[1] << 8 | redirect2[2] << 16 | redirect2[3] << 24;
        int h2 = handoff2[23] | handoff2[22] << 8 | handoff2[21] << 16 | handoff2[20] << 24;
        Assert.Equal(1077, r2);
        Assert.Equal(h2, r2);
    }

    // ── AdvancedPositionalUpdate 0x3E ────────────────────────────────────────
    // The single most common packet in the game (>40k frames in the corpus). The
    // wire layout is a 9-bit Bitmask followed by 10 mandatory slots and up to 13
    // conditional floats whose presence -- and therefore the byte OFFSET of every
    // later field -- is gated bit by bit. That conditional offset math is the
    // whole bug surface, so pin both the empty-bitmask base case and the
    // all-bits-set maximal case.

    [Fact]
    public void AdvPos_MinimalBitmask0_DecodesTheTenMandatorySlots()
    {
        string d = Dump("advpos_minimal_bitmask0");

        Assert.Contains("Bitmask           = 0x0000", d);
        Assert.Contains("GameID            = 0x06EE13DE", d);
        Assert.Contains("TimeStamp         = 0x256F41AC", d);
        Assert.Contains("Position          = 79389.33, -18882.79, 7.041", d);
        Assert.Contains("Orientation       = -0, 0, -0.23, 0.973", d);
        Assert.Contains("MovementID        = 0x00000004", d);

        // Bitmask 0 means NONE of the conditional fields are present -- the
        // decoder must stop after MovementID, not read past it.
        Assert.DoesNotContain("CurrentSpeed", d);
        Assert.DoesNotContain("ImpartedVelocity", d);
        Assert.DoesNotContain("UpdatePeriod", d);
        // All 42 bytes accounted for, no undecoded gap.
        Assert.DoesNotContain("(NB)", d);
        Assert.DoesNotContain("truncated", d);
    }

    [Fact]
    public void AdvPos_FullBitmask01FF_DecodesEveryConditionalField()
    {
        string d = Dump("advpos_full_bitmask01ff");

        Assert.Contains("Bitmask           = 0x01FF", d);
        Assert.Contains("GameID            = 0x003A567F", d);
        Assert.Contains("Position          = -100599.2, -6780.533, -3125.321", d);
        Assert.Contains("MovementID        = 0x00000B49", d);

        // Every conditional field, in wire order. The distinctive (non-zero)
        // values are pinned exactly; a wrong offset for any earlier bit would
        // shift these and change the float.
        Assert.Contains("CurrentSpeed      = 0.112", d);
        Assert.Contains("SetSpeed          = 0.149", d);
        Assert.Contains("Acceleration", d);
        Assert.Contains("RotY", d);
        Assert.Contains("DesiredY          = -0.058", d);
        Assert.Contains("RotZ", d);
        Assert.Contains("DesiredZ          = 2.283", d);
        // The 0x0080 block: ImpartedVelocity[3] + Spin + Roll + Pitch.
        Assert.Contains("ImpartedVelocity  = 0.003, -0.004, 0", d);
        Assert.Contains("ImpartedSpin", d);
        Assert.Contains("ImpartedRoll", d);
        Assert.Contains("ImpartedPitch", d);

        // UpdatePeriod is the LAST field (bit 0x0100). It only lands on the right
        // byte if all 13 preceding conditional floats consumed exactly 4 bytes
        // each -- it is the canary for the whole conditional layout.
        Assert.Contains("UpdatePeriod      = 0x00000B54", d);

        // Whole 98-byte payload consumed: no undecoded gap, no truncation flag.
        Assert.DoesNotContain("(NB)", d);
        Assert.DoesNotContain("truncated", d);
    }

    // ── ClientChatEvent 0xA5 ─────────────────────────────────────────────────
    // Five AddDataLS strings preceded by two int32s. The decoder must handle the
    // server's dual-LastName quirk (LastName is emitted twice -- the Rank slot
    // between them is commented out server-side) or every field after it shifts.

    [Fact]
    public void ChatEvent_JoinGeneral_DecodesDualLastNameAndChannel()
    {
        string d = Dump("chatevent_join_general");

        Assert.Contains("Type              = 0x00000007", d);
        // The dual LastName -- both slots carry the same name. If a decoder read
        // a rank between them, Channel would desync.
        Assert.Contains("LastName          = \"Ace\"", d);
        Assert.Contains("LastName2         = \"Ace\"", d);
        Assert.Contains("OtherPlayer       = \"\"", d);
        Assert.Contains("Channel           = \"General\"", d);
        Assert.Contains("Message           = \"\"", d);
        Assert.Contains("Trailing          = 0x00000000", d);
        Assert.DoesNotContain("overruns", d);
        Assert.DoesNotContain("(NB)", d);
    }

    [Fact]
    public void ChatEvent_MarketWtb_DecodesFullBroadcastMessage()
    {
        string d = Dump("chatevent_market_wtb");

        Assert.Contains("Type              = 0x00000003", d);
        Assert.Contains("LastName          = \"Scrabble\"", d);
        Assert.Contains("Channel           = \"Market\"", d);
        // The whole 141-char message, byte-pinned. The closing "need." only
        // appears if the AddDataLS length prefix (8D 00 == 141) was read as a
        // full u16; a low-byte-only read would land Trailing mid-message.
        Assert.Contains(
            "Message           = \"WTB Warp Kazas, any level, PM or looted.  If anybody " +
            "can build them up to level 2 I've got a couple of the gemstones and that's " +
            "all I'd need.\"",
            d);
        Assert.Contains("Trailing          = 0x00000000", d);
        Assert.DoesNotContain("overruns", d);
        Assert.DoesNotContain("(NB)", d);
    }

    [Fact]
    public void ChatEvent_TryExtract_PullsSenderChannelMessage()
    {
        // The reader-facing helper (used by the REPL chat view) must agree with
        // the structural dump: sender from LastName, channel, message.
        var ev = ClientChatEventRecord.TryExtract(Frames["chatevent_market_wtb"].Payload);
        Assert.NotNull(ev);
        Assert.Equal(3, ev!.Value.Type);
        Assert.Equal("Scrabble", ev.Value.Sender);
        Assert.Equal("Market", ev.Value.Channel);
        Assert.StartsWith("WTB Warp Kazas", ev.Value.Message);
        Assert.EndsWith("all I'd need.", ev.Value.Message);
        Assert.Equal("", ev.Value.OtherPlayer);
    }

    // ── Create 0x04 ──────────────────────────────────────────────────────────
    // Spawn an object: int32 GameID, float Scale, u16 BaseAsset, byte Type, then
    // three floats of HSV tint. 23 bytes, fixed. The fields are small and fixed,
    // so the point of pinning is the type discipline -- Scale as a float (not an
    // int), BaseAsset as an unsigned short (0xFFFF reads 65535, not -1), and the
    // HSV triple landing on the last 12 bytes.

    [Fact]
    public void Create_Friendship_DecodesGameIdScaleAssetType()
    {
        string d = Dump("create_friendship_06ee13de");

        Assert.Contains("GameID            = 0x06EE13DE", d);
        Assert.Contains("Scale             = 1.0", d);
        Assert.Contains("BaseAsset         = 0x0650  (1616)", d);
        Assert.Contains("Type              = 1", d);
        Assert.Contains("HSV               = (0.0, 0.0, 0.0)", d);
        // All 23 bytes consumed -- no trailing gap.
        Assert.DoesNotContain("(NB)", d);
        Assert.DoesNotContain("truncated", d);
    }

    [Fact]
    public void Create_AssetSentinel_ReadsBaseAssetUnsigned()
    {
        string d = Dump("create_asset_ffff_type39");

        Assert.Contains("GameID            = 0x06EE13F7", d);
        // 0xFFFF must read as 65535 (unsigned short), NOT -1. A signed read here
        // would mislabel the asset id and break the asset lookup.
        Assert.Contains("BaseAsset         = 0xFFFF  (65535)", d);
        Assert.DoesNotContain("(-1)", d);
        Assert.Contains("Type              = 39", d);
        Assert.DoesNotContain("(NB)", d);
    }

    [Fact]
    public void Create_ScaledQuarter_DecodesFractionalScale()
    {
        string d = Dump("create_scaled_quarter");

        Assert.Contains("GameID            = 0x00000086", d);
        // Scale is a real float: 0x3E800000 == 0.25, not the 1.0 every other
        // sample carries. Proves the slot is read as f32, not skipped as a const.
        Assert.Contains("Scale             = 0.25", d);
        Assert.Contains("BaseAsset         = 0x0614  (1556)", d);
        Assert.Contains("Type              = 37", d);
        Assert.DoesNotContain("(NB)", d);
    }

    [Fact]
    public void Create_And_AdvPos_AgreeOnGameId()
    {
        // Cross-opcode validation in the same capture: object 0x06EE13DE is first
        // spawned by Create (Packet #370) and then position-updated by
        // AdvancedPositionalUpdate (Packet #372). Both decoders must read the same
        // GameID from the same 4 little-endian bytes -- if either had the wrong
        // offset or endianness, the ids would not match and the two packets could
        // never be correlated into one object's lifecycle.
        var create = PacketRecord.Resolve(
            (ushort)0x04, Frames["create_friendship_06ee13de"].Payload).DumpToString();
        var advpos = PacketRecord.Resolve(
            (ushort)0x3E, Frames["advpos_minimal_bitmask0"].Payload).DumpToString();
        Assert.Contains("GameID            = 0x06EE13DE", create);
        Assert.Contains("GameID            = 0x06EE13DE", advpos);
    }

    // ── SetTarget 0x19 ───────────────────────────────────────────────────────
    // int32 GameID, int32 TargetID, both little-endian, no byte-swap. TargetID
    // 0xFFFFFFFF is the "no target" sentinel. Every 0x19 frame in the corpus
    // carries it (this capture's player only deselects, never selects), so the
    // real frames pin the clear path and a synthetic frame pins the live-target
    // path -- the gloss must appear for the sentinel and ONLY the sentinel.

    [Fact]
    public void SetTarget_Clear_DecodesNoTargetSentinel()
    {
        string d = Dump("settarget_clear_basic");

        Assert.Contains("GameID            = 0x000001C2", d);
        Assert.Contains("TargetID          = 0xFFFFFFFF  (-1)  (no target)", d);
        Assert.DoesNotContain("(NB)", d);
        Assert.DoesNotContain("truncated", d);
    }

    [Fact]
    public void SetTarget_GameIdZero_IsNotSpecialCased()
    {
        // A zero GameID is a legitimate value and must decode as 0x00000000 with
        // no flag -- guards against a decoder that treats 0 as "missing".
        string d = Dump("settarget_clear_gameid0");

        Assert.Contains("GameID            = 0x00000000", d);
        Assert.Contains("TargetID          = 0xFFFFFFFF  (-1)  (no target)", d);
        Assert.DoesNotContain("[!]", d);
        Assert.DoesNotContain("(NB)", d);
    }

    [Fact]
    public void SetTarget_LiveTarget_HasNoNoTargetGloss()
    {
        // No corpus frame selects a real target, so synthesise one: keep the
        // real GameID, set TargetID to a real object id (0x005A0499). The
        // "(no target)" gloss must NOT appear -- it is reserved for 0xFFFFFFFF.
        var f = Frames["settarget_clear_basic"];
        byte[] p = (byte[])f.Payload.Clone();
        p[4] = 0x99; p[5] = 0x04; p[6] = 0x5A; p[7] = 0x00;   // TargetID = 0x005A0499 LE
        string d = PacketRecord.Resolve((ushort)f.Opcode, p).DumpToString();

        Assert.Contains("TargetID          = 0x005A0499", d);
        Assert.DoesNotContain("(no target)", d);
        Assert.DoesNotContain("(NB)", d);
    }

    // ── Remove 0x07 ──────────────────────────────────────────────────────────
    // Despawn an object. The whole 4-byte body is one little-endian GameID.

    [Fact]
    public void Remove_DecodesGameIdLittleEndian()
    {
        string d = Dump("remove_obj_355a56");

        // Wire bytes 56 5a 35 00 -> 0x00355A56. A big-endian read would yield
        // 0x565A3500 and despawn the wrong object.
        Assert.Contains("GameID            = 0x00355A56", d);
        Assert.DoesNotContain("(NB)", d);
        Assert.DoesNotContain("truncated", d);
    }

    [Fact]
    public void Remove_SecondSample_DecodesDistinctGameId()
    {
        string d = Dump("remove_obj_163d32");

        Assert.Contains("GameID            = 0x00163D32", d);
        Assert.DoesNotContain("(NB)", d);
    }

    // ── ObjectEffect 0x09 ────────────────────────────────────────────────────
    // byte Bitmask, int32 GameID, u16 EffectDescID, then conditional fields per
    // bit: 0x01 EffectID, 0x02 TimeStamp, 0x04 Duration, 0x08 Scale,
    // 0x10/0x20/0x40 HSVShift[3]. Like AdvPos, the bitmask gates the byte offset
    // of every later field. Every corpus frame is bitmask 0x03, so the real
    // frames pin that path and a synthetic full-bitmask frame locks the rest.

    [Fact]
    public void ObjectEffect_Bitmask03_DecodesEffectIdAndTimeStamp()
    {
        string d = Dump("objeffect_obj_06ee13de");

        Assert.Contains("Bitmask           = 0x03", d);
        Assert.Contains("GameID            = 0x06EE13DE", d);
        Assert.Contains("EffectDescID      = 0x00D8  (216)", d);
        Assert.Contains("EffectID          = 0x06EE13F6", d);
        Assert.Contains("TimeStamp         = 0x256F4148", d);
        // Bits 0x04..0x40 are clear, so nothing past TimeStamp is read.
        Assert.DoesNotContain("Duration", d);
        Assert.DoesNotContain("Scale", d);
        Assert.DoesNotContain("HSVShift", d);
        // All 15 bytes consumed.
        Assert.DoesNotContain("(NB)", d);
        Assert.DoesNotContain("truncated", d);
    }

    [Fact]
    public void ObjectEffect_SecondObject_DecodesDistinctFields()
    {
        string d = Dump("objeffect_obj_3ad922");

        Assert.Contains("GameID            = 0x003AD922", d);
        Assert.Contains("EffectDescID      = 0x0017  (23)", d);
        Assert.Contains("EffectID          = 0x003B0204", d);
        Assert.Contains("TimeStamp         = 0x256FAE44", d);
        Assert.DoesNotContain("(NB)", d);
    }

    [Fact]
    public void ObjectEffect_FullBitmask_DecodesEveryConditionalField()
    {
        // No corpus frame sets bits 0x04..0x40, so synthesise one with bitmask
        // 0x7F (all bits). Each conditional field only lands on the right byte if
        // every earlier field consumed exactly its width -- Duration is a u16 (2
        // bytes), the others 4. A wrong width anywhere shifts the rest.
        byte[] p = Convert.FromHexString(
            "7f" + "44332211" + "5500" + "ddccbbaa" + "78563412" +
            "6400" + "00000040" + "0000003f" + "000000bf" + "0000803e");
        string d = PacketRecord.Resolve((ushort)0x09, p).DumpToString();

        Assert.Contains("Bitmask           = 0x7F", d);
        Assert.Contains("GameID            = 0x11223344", d);
        Assert.Contains("EffectDescID      = 0x0055  (85)", d);
        Assert.Contains("EffectID          = 0xAABBCCDD", d);
        Assert.Contains("TimeStamp         = 0x12345678", d);
        Assert.Contains("Duration          = 100", d);   // u16, the only 2-byte conditional
        Assert.Contains("Scale             = 2.0", d);
        Assert.Contains("HSVShift[0]       = 0.5", d);
        Assert.Contains("HSVShift[1]       = -0.5", d);
        Assert.Contains("HSVShift[2]       = 0.25", d);
        Assert.DoesNotContain("(NB)", d);
        Assert.DoesNotContain("truncated", d);
    }

    // ── RemoveEffect 0x0F ────────────────────────────────────────────────────
    // A single LE int32 EffectID -- the inverse of ObjectEffect.

    [Fact]
    public void RemoveEffect_DecodesEffectId()
    {
        string d = Dump("removeeffect_3b0295");

        Assert.Contains("EffectID          = 0x003B0295", d);
        Assert.DoesNotContain("(NB)", d);
        Assert.DoesNotContain("truncated", d);
    }

    [Fact]
    public void RemoveEffect_SecondSample_DecodesDistinctEffectId()
    {
        string d = Dump("removeeffect_3b0329");

        Assert.Contains("EffectID          = 0x003B0329", d);
        Assert.DoesNotContain("(NB)", d);
    }

    // ── ConstantPos 0x40 ─────────────────────────────────────────────────────
    // int32 GameID, float Pos[3], float Orient[4] (w-last quaternion). 32 bytes,
    // fixed. The trap is the quaternion ordering: the identity rotation is
    // (0,0,0,1) with the 1 on the LAST float, so a frame full of zeros except a
    // trailing 1.0 proves w is read last, not first.

    [Fact]
    public void ConstantPos_Origin_DecodesIdentityQuaternion()
    {
        string d = Dump("constpos_origin_06ee13f7");

        Assert.Contains("GameID            = 0x06EE13F7", d);
        Assert.Contains("Position          = (0.0, 0.0, 0.0)", d);
        // Identity quaternion: w=1 lands on the LAST orient float.
        Assert.Contains("Orientation       = (0.0, 0.0, 0.0, 1.0)", d);
        Assert.DoesNotContain("(NB)", d);
        Assert.DoesNotContain("truncated", d);
    }

    [Fact]
    public void ConstantPos_RealPosition_DecodesThreeDistinctFloats()
    {
        string d = Dump("constpos_realpos_86");

        Assert.Contains("GameID            = 0x00000086", d);
        // Three distinct position floats incl. a negative -- a swapped axis or a
        // wrong float width would change these.
        Assert.Contains("Position          = (59999.6, -37883.5, -500.0)", d);
        Assert.Contains("Orientation       = (0.0, 0.0, 0.0, 1.0)", d);
        Assert.DoesNotContain("(NB)", d);
    }

    [Fact]
    public void ConstantPos_NonIdentityOrientation_DecodesAllFourOrientFloats()
    {
        string d = Dump("constpos_oriented_94");

        Assert.Contains("GameID            = 0x00000094", d);
        Assert.Contains("Position          = (55201.1, -35835.8, 0.0)", d);
        // A real rotation: the last two orient floats are nonzero, so all four
        // slots are exercised (not just the trailing w of the identity frames).
        Assert.Contains("Orientation       = (0.0, 0.0, 0.951, -0.308)", d);
        Assert.DoesNotContain("(NB)", d);
    }

    // Cross-opcode GameID agreement, all frames from the same capture:
    //   * 0x06EE13DE -- spawned by Create (#370), moved by AdvPos (#372), and
    //     given an effect by ObjectEffect (#372). Three different decoders, same
    //     4 LE bytes, same id.
    //   * 0x06EE13F7 -- spawned by Create (#376) and parked by ConstantPos (#376).
    // If any decoder had the wrong GameID offset or endianness the ids would not
    // match and the packets could not be correlated into one object's lifecycle.
    [Fact]
    public void CrossOpcode_GameIds_Agree()
    {
        var create370 = Dump("create_friendship_06ee13de");
        var advpos372 = Dump("advpos_minimal_bitmask0");
        var objeffect372 = Dump("objeffect_obj_06ee13de");
        Assert.Contains("GameID            = 0x06EE13DE", create370);
        Assert.Contains("GameID            = 0x06EE13DE", advpos372);
        Assert.Contains("GameID            = 0x06EE13DE", objeffect372);

        var create376 = Dump("create_asset_ffff_type39");
        var constpos376 = Dump("constpos_origin_06ee13f7");
        Assert.Contains("GameID            = 0x06EE13F7", create376);
        Assert.Contains("GameID            = 0x06EE13F7", constpos376);
    }

    // ── VerbUpdate 0x5C ──────────────────────────────────────────────────────
    // The context-menu verb list. GameID and BOTH int32 Counts are big-endian
    // (ntohl at emit); the int16 {Attribute, VerbID} entries are little-endian --
    // a mixed-endian packet. Two passes: a disabled/too-far pass and an enabled
    // pass. The second Count only lands on the right byte if the first pass's
    // entries were each consumed as exactly 4 bytes.

    [Fact]
    public void VerbUpdate_EmptyEnablePass_DecodesSingleDisabledVerb()
    {
        string d = Dump("verb_obj450_dis_toofar");

        Assert.Contains("GameID            = 0x000001C2  (450)  (BE -- ntohl at emit)", d);
        Assert.Contains("Count[0]          = 0  (BE)", d);
        Assert.Contains("Count[1]          = 1  (BE)", d);
        Assert.Contains("[1.0]           = Attr=DIS_TOOFAR Verb=0x000A (10)", d);
        // No phantom entry in the empty pass 0.
        Assert.DoesNotContain("[0.0]", d);
        Assert.DoesNotContain("(NB)", d);
        Assert.DoesNotContain("truncated", d);
    }

    [Fact]
    public void VerbUpdate_BothPasses_DecodesEnableAndDisable()
    {
        string d = Dump("verb_obj450_both_passes");

        Assert.Contains("GameID            = 0x000001C2  (450)  (BE -- ntohl at emit)", d);
        // Pass 0 is the disabled/too-far entry, pass 1 the enabled entry. Both
        // present means the second Count was read at the correct offset.
        Assert.Contains("Count[0]          = 1  (BE)", d);
        Assert.Contains("[0.0]           = Attr=DIS_TOOFAR Verb=0x000A (10)", d);
        Assert.Contains("Count[1]          = 1  (BE)", d);
        Assert.Contains("[1.0]           = Attr=ENABLE Verb=0x000A (10)", d);
        Assert.DoesNotContain("(NB)", d);
        Assert.DoesNotContain("truncated", d);
    }

    // ── CameraControl 0x92 ───────────────────────────────────────────────────
    // int32 Message, int32 GameID. BOTH are big-endian on the wire: the GameID is
    // always ntohl'd by callers and Message is a pre-swapped literal. A host-LE
    // GameID read is the ntohl trap that points the camera at a garbage id -- the
    // same byte-order class of bug that crashed the client via ServerRedirect.

    [Fact]
    public void CameraControl_BigEndian_DecodesMessageAndGameId()
    {
        string d = Dump("camera_msg4_obj450");

        Assert.Contains("Message           = 0x00000004  (4)  (BE -- pre-swapped at emit)", d);
        // GameID 450 == 0x000001C2 read big-endian. A LE read would yield
        // 0xC2010000 -- a garbage id that matches no object.
        Assert.Contains("GameID            = 0x000001C2  (450)  (BE -- ntohl at emit)", d);
        Assert.DoesNotContain("0xC2010000", d);
        Assert.DoesNotContain("(NB)", d);
        Assert.DoesNotContain("truncated", d);
    }

    [Fact]
    public void CameraControl_SecondSample_DecodesLargeGameId()
    {
        string d = Dump("camera_msg3_obj3a1ec7");

        Assert.Contains("Message           = 0x00000003  (3)", d);
        Assert.Contains("GameID            = 0x003A1EC7", d);
        Assert.DoesNotContain("0xC71E3A00", d);          // the LE-misread garbage
        Assert.DoesNotContain("(NB)", d);
    }

    // ── Navigation 0x99 ──────────────────────────────────────────────────────
    // A PACKED 14-byte struct: int32 GameID, float Signature, u8 PlayerHasVisited,
    // int32 NavType, u8 IsHuge. The pack means NavType sits at offset 9, NOT a
    // 4-aligned offset -- a decoder that assumed natural alignment would read it
    // from the wrong byte and pick up the IsHuge byte too.

    [Fact]
    public void Navigation_Type1_DecodesPackedFields()
    {
        string d = Dump("nav_obj_c5_type1");

        Assert.Contains("GameID            = 0x000000C5", d);
        Assert.Contains("Signature         = 37000.0", d);
        Assert.Contains("PlayerHasVisited  = 1", d);
        Assert.Contains("NavType           = 1", d);      // unaligned int32 at offset 9
        Assert.Contains("IsHuge            = 0", d);
        Assert.DoesNotContain("(NB)", d);
        Assert.DoesNotContain("truncated", d);
    }

    [Fact]
    public void Navigation_Type2_DecodesDistinctSignatureAndType()
    {
        string d = Dump("nav_obj_94_type2");

        Assert.Contains("GameID            = 0x00000094", d);
        Assert.Contains("Signature         = 28000.0", d);
        Assert.Contains("NavType           = 2", d);
        Assert.DoesNotContain("(NB)", d);
    }

    // Object 450 (0x000001C2) appears across four opcodes in this capture:
    // SetTarget (#1368) clears it, VerbUpdate (#1372) lists its verbs, and
    // CameraControl (#1712) points the camera at it. SetTarget reads the id
    // little-endian; VerbUpdate and CameraControl read it big-endian (ntohl at
    // emit). All three must resolve to the SAME 450 -- the mixed-endian decode is
    // correct only if it does.
    [Fact]
    public void CrossOpcode_Object450_AgreesAcrossEndianness()
    {
        var settarget = Dump("settarget_clear_basic");      // GameID is LE here
        var verb = Dump("verb_obj450_dis_toofar");          // GameID is BE here
        var camera = Dump("camera_msg4_obj450");            // GameID is BE here
        Assert.Contains("GameID            = 0x000001C2", settarget);
        Assert.Contains("GameID            = 0x000001C2  (450)", verb);
        Assert.Contains("GameID            = 0x000001C2  (450)", camera);
    }

    // ── Decal 0x10 ───────────────────────────────────────────────────────────
    // int32 GameID, int16 DecalCount, then DecalCount * 24-byte DecalItem
    // (Index, decal_id, float H/S/V, float opacity). The per-item stride is the
    // bug surface: a wrong DecalItem size lands item N's fields on the wrong byte.

    [Fact]
    public void Decal_TwoItems_DecodesBothDecalItems()
    {
        string d = Dump("decal_obj_06ee13de");

        Assert.Contains("GameID            = 0x06EE13DE", d);
        Assert.Contains("DecalCount        = 2", d);
        Assert.Contains("[0] Index       = 0x00000001", d);
        Assert.Contains("[0] DecalID     = 0x00000019  (25)", d);
        Assert.Contains("[0] H/S/V       = 1, 1, 1", d);
        Assert.Contains("[0] Opacity     = 1.0", d);
        // The second item only lands correctly if the first consumed exactly 24B.
        Assert.Contains("[1] Index       = 0x00000002", d);
        Assert.Contains("[1] DecalID     = 0x00000019  (25)", d);
        Assert.DoesNotContain("(NB)", d);
        Assert.DoesNotContain("too short", d);
    }

    [Fact]
    public void Decal_SecondObject_DecodesDistinctDecalId()
    {
        string d = Dump("decal_obj_3ad922");

        Assert.Contains("GameID            = 0x003AD922", d);
        Assert.Contains("[0] DecalID     = 0x00000053  (83)", d);
        Assert.Contains("[1] DecalID     = 0x00000053  (83)", d);
        Assert.DoesNotContain("(NB)", d);
    }

    // ── NameDecal 0xB2 ───────────────────────────────────────────────────────
    // int32 GameID, char Name[32] (NUL-terminated), float RGB[3]. 48 bytes. The
    // name read must stop at the NUL and skip the zero pad so RGB lands at 36.

    [Fact]
    public void NameDecal_DecodesShipNameAndWhiteRgb()
    {
        string d = Dump("namedecal_revenge_jenquai");

        Assert.Contains("GameID            = 0x06EE13DE", d);
        Assert.Contains("Name              = \"Revenge of the Jenquai\"", d);
        Assert.Contains("RGB               = (1.0, 1.0, 1.0)", d);
        Assert.DoesNotContain("(NB)", d);
        Assert.DoesNotContain("truncated", d);
    }

    [Fact]
    public void NameDecal_DecodesTintedRgb()
    {
        string d = Dump("namedecal_blitzer_colored");

        Assert.Contains("GameID            = 0x003AFED3", d);
        Assert.Contains("Name              = \"Blitzer\"", d);
        // A real non-white tint -- all three RGB floats are distinct, so the
        // offset-36 RGB block is read in full (not a 1.0 constant).
        Assert.Contains("RGB               = (0.89, 0.592, 0.341)", d);
        Assert.DoesNotContain("(NB)", d);
    }

    // ── Subparts 0xB4 ────────────────────────────────────────────────────────
    // int32 GameID (BE), int32 NumSubParts (BE), then NumSubParts pairs of a
    // NUL-terminated bone path and a BE int32 asset id. Variable-length strings
    // mean every later field's offset depends on the prior bone's length.

    [Fact]
    public void Subparts_DecodesFourBonesAndAssets()
    {
        string d = Dump("subparts_obj_06ee13de");

        Assert.Contains("GameID            = 0x06EE13DE  (116265950)  (BE -- ntohl at emit)", d);
        Assert.Contains("NumSubParts       = 4", d);
        Assert.Contains("[0] Bone        = \"~01\"", d);
        Assert.Contains("[0] AssetID     = 0x0000066B  (1643)", d);
        // The variable-length bone paths -- each later entry only parses if the
        // prior NUL-terminated string consumed exactly to its terminator.
        Assert.Contains("[1] Bone        = \"~01/~03_01\"", d);
        Assert.Contains("[1] AssetID     = 0x00000732  (1842)", d);
        Assert.Contains("[2] Bone        = \"~01/~03_02\"", d);
        Assert.Contains("[3] Bone        = \"~02\"", d);
        Assert.Contains("[3] AssetID     = 0x00000683  (1667)", d);
        Assert.DoesNotContain("(NB)", d);
        Assert.DoesNotContain("truncated", d);
    }

    // ── WarpIndex 0x9C ───────────────────────────────────────────────────────
    // A single LE int32 warp index; -1 is the "no warp" sentinel.

    [Fact]
    public void WarpIndex_DecodesIndexOne()
    {
        string d = Dump("warpindex_one");

        Assert.Contains("Index             = 1", d);
        Assert.DoesNotContain("(none)", d);
        Assert.DoesNotContain("(NB)", d);
    }

    [Fact]
    public void WarpIndex_NegativeOne_GetsNoneGloss()
    {
        string d = Dump("warpindex_none");

        Assert.Contains("Index             = -1  (none)", d);
        Assert.DoesNotContain("(NB)", d);
    }

    // Object 0x06EE13DE is read by SIX different decoders in this capture, all
    // from the same logical id: Create (#370, LE), AdvPos (#372, LE), ObjectEffect
    // (#372, LE), Decal (#372, LE), NameDecal (#372, LE) and Subparts (#370, BE).
    // The Subparts frame stores the id big-endian while the rest store it little-
    // endian, so this also locks the mixed-endian agreement -- every decoder must
    // resolve the same object or its packets fall out of the lifecycle.
    [Fact]
    public void CrossOpcode_Object06EE13DE_AgreesAcrossSixDecoders()
    {
        foreach (var name in new[] {
            "create_friendship_06ee13de", "advpos_minimal_bitmask0",
            "objeffect_obj_06ee13de", "decal_obj_06ee13de",
            "namedecal_revenge_jenquai", "subparts_obj_06ee13de" })
        {
            Assert.Contains("GameID            = 0x06EE13DE", Dump(name));
        }
    }

    // ── Start 0x05 ───────────────────────────────────────────────────────────
    // A bare LE int32 -- the client's in-sector avatar id. Sector-assigned, so
    // it changes per sector entry (10069 vs 3126 below) within one session, and
    // carries no PLAYER_TAG bits.

    [Fact]
    public void Start_FirstSector_DecodesAvatarId()
    {
        string d = Dump("start_avatarid_10069");

        Assert.Contains("StartID           = 0x00002755  (10069)  (client's in-sector avatar id; sector-assigned)", d);
        Assert.DoesNotContain("(NB)", d);
        Assert.DoesNotContain("truncated", d);
    }

    [Fact]
    public void Start_LaterSector_DecodesDifferentAvatarId()
    {
        // Same single-character session, different sector entry, different id --
        // the regression pin against treating StartID as a session constant.
        var first = Dump("start_avatarid_10069");
        var later = Dump("start_avatarid_3126");
        Assert.Contains("StartID           = 0x00002755  (10069)", first);
        Assert.Contains("StartID           = 0x00000C36  (3126)", later);
        Assert.NotEqual(first, later);
    }

    // ── SimplePos 0x08 ───────────────────────────────────────────────────────
    // int32 GameID, u32 TimeStamp, float Pos[3], float Orient[4], float Vel[3] =
    // 48 bytes. The w-last identity quaternion is the offset pin.

    [Fact]
    public void SimplePos_Docked_DecodesIdentityQuaternionAndZeroVelocity()
    {
        string d = Dump("simplepos_docked_obj_cb");

        Assert.Contains("GameID            = 0x000000CB", d);
        Assert.Contains("TimeStamp         = 0x256FE044", d);
        Assert.Contains("Position          = (76284.45, -17325.62, 0.0)", d);
        // Identity quaternion: the 1.0 lands on the LAST orient float.
        Assert.Contains("Orientation       = (0.0, 0.0, 0.0, 1.0)", d);
        Assert.Contains("Velocity          = (0.0, 0.0, 0.0)", d);
        Assert.DoesNotContain("(NB)", d);
        Assert.DoesNotContain("truncated", d);
    }

    [Fact]
    public void SimplePos_Oriented_DecodesRealQuaternion()
    {
        string d = Dump("simplepos_oriented_obj_6c");

        Assert.Contains("GameID            = 0x0000006C", d);
        // A real rotation: all four orient floats distinct and nonzero, so the
        // whole 16-byte Orient block is read (complement to the identity frame).
        Assert.Contains("Position          = (15164.1, 15164.1, -879.007)", d);
        Assert.Contains("Orientation       = (-0.063, -0.024, -0.129, 0.989)", d);
        Assert.DoesNotContain("(NB)", d);
    }

    // ── ClientAvatar 0x37 / ClientShip 0x47 ──────────────────────────────────
    // Both are a bare LE int32 GameID. In the sector-entry burst the server sends
    // ClientAvatar then ClientShip with the SAME id == the player's own ship --
    // the "this is you" pair.

    [Fact]
    public void ClientAvatar_And_ClientShip_AgreeOnYourShipId()
    {
        var avatar = Dump("clientavatar_your_ship_06ee13de");
        var ship   = Dump("clientship_your_ship_06ee13de");
        Assert.Contains("GameID            = 0x06EE13DE", avatar);
        Assert.Contains("GameID            = 0x06EE13DE", ship);
        Assert.DoesNotContain("(NB)", avatar);
        Assert.DoesNotContain("(NB)", ship);
    }

    [Fact]
    public void ClientAvatar_And_ClientShip_DecodeNonPlayerIds()
    {
        // A different (non-player) id through both decoders -- guards a hard-wired
        // self id and pins the offset/endianness independently of 0x06EE13DE.
        Assert.Contains("GameID            = 0x003B027D", Dump("clientavatar_npc_3b027d"));
        Assert.Contains("GameID            = 0x003B027D", Dump("clientship_npc_3b027d"));
    }

    // ── ClientType 0x3C ──────────────────────────────────────────────────────
    [Fact]
    public void ClientType_DecodesSectorType()
    {
        string d = Dump("clienttype_space_zero");

        Assert.Contains("ClientType        = 0x00000000  (0)", d);
        Assert.DoesNotContain("(NB)", d);
        Assert.DoesNotContain("truncated", d);
    }

    // ── PlanetPos 0x3F ───────────────────────────────────────────────────────
    // 48-byte PACKED: int32 GameID, u32 TimeStamp, float X/Y/Z, int32 OrbitID,
    // then six orbit/rotate floats. Every 0x3F frame across all three captures
    // has zero orbit fields (orbital motion is client-side), so a synthetic frame
    // pins the orbit-field offsets the corpus never exercises.

    [Fact]
    public void PlanetPos_Static_DecodesPositionWithZeroOrbit()
    {
        string d = Dump("planetpos_static_cc");

        Assert.Contains("GameID            = 0x000000CC", d);
        Assert.Contains("TimeStamp         = 0x256FE044", d);
        Assert.Contains("Position          = X=111773.0  Y=39449.4  Z=0.0", d);
        Assert.Contains("OrbitID           = 0x00000000  (0)", d);
        Assert.Contains("OrbitDist         = 0.0", d);
        Assert.Contains("TiltAngle         = 0.0", d);
        Assert.DoesNotContain("(NB)", d);
        Assert.DoesNotContain("truncated", d);
    }

    [Fact]
    public void PlanetPos_SecondBody_DecodesDistinctPosition()
    {
        string d = Dump("planetpos_static_76");

        Assert.Contains("GameID            = 0x00000076", d);
        // Three distinct floats incl. two negatives and a nonzero Z.
        Assert.Contains("Position          = X=-39332.9  Y=-102688.0  Z=-5000.0", d);
        Assert.DoesNotContain("(NB)", d);
    }

    [Fact]
    public void PlanetPos_Synthetic_DecodesEveryOrbitField()
    {
        // No corpus frame carries orbit data (all 33 across capture_1/2/3 are
        // zero there), so synthesise an orbiting body to pin the six orbit/rotate
        // float offsets. Each only lands correctly if OrbitID (int32 at 20) and
        // every prior float consumed exactly 4 bytes. Values chosen distinct:
        // OrbitID 5, OrbitDist 1000, OrbitAngle 0.2, OrbitRate 0.6, RotateAngle
        // 0.1, RotateRate 1.5707964, TiltAngle -0.5.
        byte[] p = Convert.FromHexString(
            "2a000000" + "40e00100" + "00004843" + "00004843" + "0000c842" +
            "05000000" + "00007a44" + "cdcc4c3e" + "9a99193f" + "cdcccc3d" +
            "db0fc93f" + "000000bf");
        string d = PacketRecord.Resolve((ushort)0x3F, p).DumpToString();

        Assert.Contains("OrbitID           = 0x00000005  (5)", d);
        Assert.Contains("OrbitDist         = 1000.0", d);
        Assert.Contains("OrbitAngle        = 0.2", d);
        Assert.Contains("OrbitRate         = 0.6", d);
        Assert.Contains("RotateAngle       = 0.1", d);
        Assert.Contains("RotateRate        = 1.571", d);
        Assert.Contains("TiltAngle         = -0.5", d);
        Assert.DoesNotContain("(NB)", d);
        Assert.DoesNotContain("truncated", d);
    }

    // ── ServerParameters 0x42 ────────────────────────────────────────────────
    // 70-byte PACKED sector-physics block. The three backdrop bytes at 36/37/38
    // push every later float off natural alignment; BackdropBaseAsset (int16 @64)
    // and SectorNum (uint32 @66) only land if the whole pack is read byte-exact.

    [Fact]
    public void ServerParameters_Glenn_DecodesPackedFields()
    {
        string d = Dump("serverparams_glenn_4515");

        Assert.Contains("ZBandMin          = -2500.0", d);
        Assert.Contains("ZBandMax          = 2500.0", d);
        Assert.Contains("XMin              = -50000.0", d);
        Assert.Contains("YMin              = -150000.0", d);
        Assert.Contains("XMax              = 250000.0", d);
        Assert.Contains("YMax              = 50000.0", d);
        Assert.Contains("DebrisMode        = 0", d);
        Assert.Contains("MaxTilt           = 1.222", d);     // unaligned float after the 3 backdrop bytes
        Assert.Contains("AutoLevel         = 1", d);
        Assert.Contains("ImpulseRate       = 0.026", d);
        Assert.Contains("DecayVelocity     = 9.33", d);
        Assert.Contains("DecaySpin         = 9.33", d);
        Assert.Contains("BackdropBaseAsset = 422", d);       // int16 @64
        Assert.Contains("SectorNum         = 0x000011A3  (4515)", d);   // uint32 @66
        Assert.DoesNotContain("(NB)", d);
        Assert.DoesNotContain("truncated", d);
    }

    [Fact]
    public void ServerParameters_AsteroidBelt_DecodesDistinctBounds()
    {
        string d = Dump("serverparams_asteroidbelt_1077");

        // Symmetric +/-175000 bounds, different from Glenn -- same offsets, new floats.
        Assert.Contains("XMin              = -175000.0", d);
        Assert.Contains("YMin              = -175000.0", d);
        Assert.Contains("XMax              = 175000.0", d);
        Assert.Contains("YMax              = 175000.0", d);
        Assert.Contains("BackdropBaseAsset = 220", d);
        Assert.Contains("SectorNum         = 0x00000435  (1077)", d);
        Assert.DoesNotContain("(NB)", d);
    }

    // ServerParameters.SectorNum cross-validates the ServerHandoff/ServerRedirect
    // ToSectorID for the SAME sector transition. The handoff/redirect encode the
    // sector id (Glenn 4515, Asteroid Belt 1077) at sector-handoff time; the
    // ServerParameters that the destination sector then sends must report the
    // same SectorNum, or the three packets describe different sectors.
    [Fact]
    public void CrossOpcode_SectorNum_AgreesWithHandoffAndRedirect()
    {
        // Glenn == 4515 across handoff, redirect and server-params.
        Assert.Contains("ToSectorID", Dump("serverhandoff_friendship7_to_glenn"));
        Assert.Contains("4515", Dump("serverhandoff_friendship7_to_glenn"));
        Assert.Contains("4515", Dump("serverredirect_to_glenn"));
        Assert.Contains("SectorNum         = 0x000011A3  (4515)", Dump("serverparams_glenn_4515"));

        // Asteroid Belt Beta == 1077 across all three.
        Assert.Contains("1077", Dump("serverhandoff_glenn_to_asteroidbelt"));
        Assert.Contains("1077", Dump("serverredirect_to_asteroidbelt"));
        Assert.Contains("SectorNum         = 0x00000435  (1077)", Dump("serverparams_asteroidbelt_1077"));
    }

    // ── StarbaseSet 0x4F ─────────────────────────────────────────────────────
    // int32 StarbaseID + u8 Action + u8 ExitMode = 6 bytes. The two trailing
    // bytes must be read as separate u8s, not folded into a 4-byte field.

    [Fact]
    public void StarbaseSet_Action0_DecodesIdAndBytes()
    {
        string d = Dump("starbaseset_b05f_action0");

        Assert.Contains("StarbaseID        = 0x0000B05F", d);
        Assert.Contains("Action            = 0", d);
        Assert.Contains("ExitMode          = 0", d);
        Assert.DoesNotContain("(NB)", d);
        Assert.DoesNotContain("truncated", d);
    }

    [Fact]
    public void StarbaseSet_Action1_DecodesActionByteIndependently()
    {
        // Same StarbaseID, Action toggled 0 -> 1 -- pins that the Action byte at
        // offset 4 is read independently of the id.
        string d = Dump("starbaseset_b05f_action1");

        Assert.Contains("StarbaseID        = 0x0000B05F", d);
        Assert.Contains("Action            = 1", d);
        Assert.Contains("ExitMode          = 0", d);
        Assert.DoesNotContain("(NB)", d);
    }

    // ── ManufactureSetManufactureId 0x7F ─────────────────────────────────────
    // A bare LE int32. For the manu-lab anchor the value is GameID|MANU_TAG|
    // PLAYER_TAG, so the LE int32 has bits 31 and 30 set (top byte >= 0xC0). A BE
    // read would strip the tag bits -- the byte-order pin.

    [Fact]
    public void ManufactureSet_ManuLab_DecodesTaggedIdLittleEndian()
    {
        string d = Dump("manufactureset_manulab_tagged");

        // LE 0xF713EE06: top byte 0xF7 has MANU_TAG (bit31) | PLAYER_TAG (bit30).
        // A BE read would give 0x06EE13F7 with no tag bits -- an invalid manu-id.
        Assert.Contains("ManufactureID     = 0xF713EE06", d);
        Assert.DoesNotContain("0x06EE13F7", d);
        Assert.DoesNotContain("(NB)", d);
        Assert.DoesNotContain("truncated", d);
    }

    [Fact]
    public void ManufactureSet_Reset_DecodesZero()
    {
        string d = Dump("manufactureset_reset_zero");

        Assert.Contains("ManufactureID     = 0x00000000", d);
        Assert.DoesNotContain("(NB)", d);
    }

    // ── LoungeNpc 0x52 ───────────────────────────────────────────────────────
    // The largest record we pin: the Friendship 7 Recreation Port lounge, a
    // 3400-byte frame reassembled from a fragmented UDP stream. Header + rooms +
    // terminals + an array of 12 fixed-stride (265B) StationNPC records. The NPC
    // names land at exact offsets only if the 24B-header + 241B-AvatarData stride
    // is right, so pinning every name self-validates the array math.

    [Fact]
    public void LoungeNpc_Friendship7_DecodesStationRoomsAndTerminals()
    {
        string d = Dump("loungenpc_friendship7_full");

        Assert.Contains("StationType       = 6", d);
        Assert.Contains("RoomCount         = 5", d);
        // 5 rooms, distinct styles, identical fog -- pins the 28B room stride.
        Assert.Contains("Room[0]         = num=0 style=0 fog=(100,1000) rgb=(0,0,0)", d);
        Assert.Contains("Room[1]         = num=1 style=2 fog=(100,1000) rgb=(0,0,0)", d);
        Assert.Contains("Room[2]         = num=2 style=11 fog=(100,1000) rgb=(0,0,0)", d);
        Assert.Contains("Room[3]         = num=3 style=130 fog=(100,1000) rgb=(0,0,0)", d);
        Assert.Contains("Room[4]         = num=4 style=0 fog=(100,1000) rgb=(0,0,0)", d);
        // 4 terminals, 16B stride.
        Assert.Contains("NumTerms          = 4", d);
        Assert.Contains("Term[0]         = room=1 loc=1 type=0", d);
        Assert.Contains("Term[1]         = room=1 loc=2 type=2", d);
        Assert.Contains("Term[2]         = room=3 loc=1 type=3", d);
        Assert.Contains("Term[3]         = room=1 loc=3 type=1", d);

        Assert.DoesNotContain("(NB)", d);
        Assert.DoesNotContain("truncated", d);
    }

    [Fact]
    public void LoungeNpc_Friendship7_DecodesAllTwelveNpcsAtFixedStride()
    {
        string d = Dump("loungenpc_friendship7_full");

        Assert.Contains("NumNPCs           = 12", d);

        // Every NPC header (room/loc/npcId/booth) AND name pair. If the 265-byte
        // stride were wrong, a later NPC's name would land mid-AvatarData and garble.
        Assert.Contains("NPC[0]          = room=2 loc=0 npcId=0x0004 booth=0 unk=(0,0)", d);
        Assert.Contains("name          = \"Kah\" / \"Rinno\"", d);
        Assert.Contains("NPC[1]          = room=2 loc=1 npcId=0x0045 booth=2 unk=(0,0)", d);
        Assert.Contains("name          = \"Trevor\" / \"Jorst\"", d);
        // booth=-1 (0xFFFFFFFF) is the no-booth sentinel -- read as signed int32.
        Assert.Contains("NPC[2]          = room=1 loc=2 npcId=0x0003 booth=-1 unk=(0,0)", d);
        Assert.Contains("name          = \"Wenton\" / \"Ness\"", d);
        Assert.Contains("NPC[3]          = room=2 loc=2 npcId=0x0065 booth=4 unk=(0,0)", d);
        Assert.Contains("name          = \"Anveryn\" / \"O'Connell\"", d);
        Assert.Contains("NPC[4]          = room=2 loc=3 npcId=0x007A booth=5 unk=(0,0)", d);
        Assert.Contains("name          = \"Regina\" / \"Flore\"", d);
        Assert.Contains("NPC[5]          = room=1 loc=4 npcId=0x0002 booth=-1 unk=(0,0)", d);
        Assert.Contains("name          = \"Arno\" / \"Suiliman\"", d);
        Assert.Contains("NPC[6]          = room=2 loc=6 npcId=0x00A5 booth=5 unk=(0,0)", d);
        Assert.Contains("name          = \"Kristin\" / \"Sadler\"", d);
        Assert.Contains("NPC[7]          = room=3 loc=7 npcId=0x0127 booth=-1 unk=(0,0)", d);
        Assert.Contains("name          = \"Portia\" / \"LaPointe\"", d);
        Assert.Contains("NPC[8]          = room=2 loc=8 npcId=0x00FF booth=5 unk=(0,0)", d);
        Assert.Contains("name          = \"Sara\" / \"Green\"", d);
        Assert.Contains("NPC[9]          = room=1 loc=9 npcId=0x0001 booth=-1 unk=(0,0)", d);
        Assert.Contains("name          = \"Monty\" / \"duChampe\"", d);
        Assert.Contains("NPC[10]         = room=3 loc=8 npcId=0x0128 booth=-1 unk=(0,0)", d);
        Assert.Contains("name          = \"Belulah\" / \"Lee\"", d);
        Assert.Contains("NPC[11]         = room=2 loc=11 npcId=0x00D1 booth=5 unk=(0,0)", d);
        Assert.Contains("name          = \"Ian\" / \"Darson\"", d);

        // Each NPC's 201-byte cosmetic block is byte-pinned (full frame in the
        // fixture) but not field-decoded -- 12 of them, all accounted for.
        Assert.Equal(12, CountOccurrences(d, "(AvatarData cosmetic block -- not field-decoded)"));
        Assert.DoesNotContain("(NB)", d);
        Assert.DoesNotContain("truncated", d);
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        int n = 0, i = 0;
        while ((i = haystack.IndexOf(needle, i, StringComparison.Ordinal)) >= 0) { n++; i += needle.Length; }
        return n;
    }

    // ── PointEffect 0x0A ─────────────────────────────────────────────────────
    // Fixed 40-byte one-shot point effect (no parent object). Player::PointEffect
    // builds the buffer at fixed offsets, so every field is pinnable exactly.

    [Fact]
    public void PointEffect_DecodesFixedFortyByteLayout()
    {
        string d = Dump("pointeffect_satellite_7392");

        Assert.Contains("ObjectID          = 0x003A06C0", d);
        Assert.Contains("TimeStamp         = 0x257C4870", d);
        Assert.Contains("Position          = (-99144.98, 36557.84, 4282.668)", d);
        Assert.Contains("Duration          = 0", d);
        Assert.Contains("EffectID          = 1013", d);
        Assert.Contains("Scale             = 129.982", d);
        Assert.Contains("HSVShift          = (0.0, 0.0, 0.0)", d);
        Assert.DoesNotContain("(NB)", d);
        Assert.DoesNotContain("truncated", d);
        Assert.DoesNotContain("trailing", d);
    }

    [Fact]
    public void PointEffect_SecondInstance_ConstantEffectIdAndScale()
    {
        // A second spawn: obj id and tick advance, effect id + scale are invariant.
        string d = Dump("pointeffect_satellite_7637");

        Assert.Contains("ObjectID          = 0x003A07A0", d);
        Assert.Contains("TimeStamp         = 0x257C8628", d);
        Assert.Contains("EffectID          = 1013", d);
        Assert.Contains("Scale             = 129.982", d);
        Assert.DoesNotContain("(NB)", d);
    }

    // ── ObjectToObjectLinkedEffect 0x0E ──────────────────────────────────────
    // Fixed 58-byte duration-linked source->target effect, serialised field by
    // field. The effect ids and speedup match the MOBClass weapon-impact call
    // site SendObjectToObjectLinkedEffect(this, p, 0x21, 0x03, 2.0).

    [Fact]
    public void LinkedEffect_DecodesFixedFiftyEightByteLayout()
    {
        string d = Dump("linkedeffect_e1_33_e2_3_speed1");

        Assert.Contains("ObjectID          = 0x003A069A", d);
        Assert.Contains("TimeStamp         = 0x257C4230", d);
        Assert.Contains("SourceID          = 0x0039F9E3", d);
        Assert.Contains("Spacer            = 0", d);
        Assert.Contains("TargetID          = 0x0039F387", d);
        Assert.Contains("LinkedEffectDescID= 33", d);
        Assert.Contains("EffectDescID      = 3", d);
        // Retail populated the target offset; our server reimpl zeroes it.
        Assert.Contains("TargetOffset      = (55.487, -13.827, 0.192)", d);
        Assert.Contains("OutsideTargetRadius= 1", d);
        Assert.Contains("Scale             = 1.0", d);
        Assert.Contains("Speedup           = 1.0", d);
        Assert.DoesNotContain("(NB)", d);
        Assert.DoesNotContain("truncated", d);
        Assert.DoesNotContain("trailing", d);
    }

    [Fact]
    public void LinkedEffect_SpeedupTwo_ReadIndependentlyOfEffectIds()
    {
        // Same effect ids, speedup 2.0 -- pins the Speedup float at offset 0x36.
        string d = Dump("linkedeffect_e1_33_e2_3_speed2");

        Assert.Contains("LinkedEffectDescID= 33", d);
        Assert.Contains("EffectDescID      = 3", d);
        Assert.Contains("Speedup           = 2.0", d);
        Assert.DoesNotContain("(NB)", d);
    }

    // ── RequestTarget 0x17 ───────────────────────────────────────────────────
    // Client->server target selection. Identical 8-byte layout to 0x19 SET_TARGET;
    // both read raw host LE (Player::HandleRequestTarget, GetObjectFromID(TargetID)).

    [Fact]
    public void RequestTarget_DecodesGameIdAndTargetId_LittleEndian()
    {
        string d = Dump("requesttarget_player_targets_2617");

        Assert.Contains("GameID", d);
        Assert.Contains("0x0084E1E9", d);            // E9 E1 84 00 LE == 8708585
        Assert.Contains("TargetID", d);
        Assert.Contains("0x00000A39", d);            // 39 0A 00 00 LE == 2617
        Assert.Contains("(2617)", d);
        Assert.DoesNotContain("???", d);             // all 8 bytes decoded
        Assert.DoesNotContain("[!]", d);
    }

    // ── Action 0x2C ──────────────────────────────────────────────────────────
    // Fixed 16-byte ActionPacket, all four int32 raw LE (Player::HandleAction).

    [Fact]
    public void Action_DecodesGameIdActionTargetOptVar_LittleEndian()
    {
        string d = Dump("action_player_target");

        Assert.Contains("GameID", d);
        Assert.Contains("0x0084E1E9", d);            // same player as the RequestTarget frame
        Assert.Contains("Action", d);
        Assert.Contains("[0004] Action", d);         // Action int32 at offset 4
        Assert.Contains("Target", d);
        Assert.Contains("0x0084DC75", d);            // 75 DC 84 00 LE == 8707189
        Assert.Contains("OptionalVar", d);
        Assert.DoesNotContain("???", d);             // all 16 bytes decoded
        Assert.DoesNotContain("[!]", d);
    }

    // ── VerbRequest 0x5A ─────────────────────────────────────────────────────
    // The mixed-endian regression lock. SubjectID/ObjectID are big-endian (ntohl
    // at parse); Action is little-endian (raw host read, pkt->Action == 1). The
    // BE-read ids here equal the SAME player's 0x17 RequestTarget ids read LE.

    [Fact]
    public void VerbRequest_SubjectAndObjectAreBigEndian_ActionIsLittleEndian()
    {
        string d = Dump("verbrequest_subject_object_action1");

        Assert.Contains("SubjectID", d);
        Assert.Contains("0x0084E1E9", d);            // 00 84 E1 E9 read BE == 8708585
        Assert.Contains("(BE -- ntohl at parse)", d);
        Assert.Contains("ObjectID", d);
        Assert.Contains("0x00000A39", d);            // 00 00 0A 39 read BE == 2617
        Assert.Contains("Action", d);
        Assert.Contains("(LE -- raw host read)", d);
        Assert.Contains("[0008] Action", d);
        Assert.DoesNotContain("???", d);             // all 12 bytes decoded
        Assert.DoesNotContain("[!]", d);
    }

    [Fact]
    public void VerbRequest_BigEndianIds_MatchRequestTargetLittleEndianIds()
    {
        // Cross-packet proof of the endianness split: the SAME player issued a
        // 0x17 RequestTarget (ids LE) then a 0x5A VerbRequest (ids BE) against the
        // same object. Read each with its own convention -> identical ids.
        var verb = Frames["verbrequest_subject_object_action1"].Payload;
        var req  = Frames["requesttarget_player_targets_2617"].Payload;

        int verbSubjectBE = verb[0] << 24 | verb[1] << 16 | verb[2] << 8 | verb[3];      // BE
        int verbObjectBE  = verb[4] << 24 | verb[5] << 16 | verb[6] << 8 | verb[7];      // BE
        int reqGameLE     = req[0] | req[1] << 8 | req[2] << 16 | req[3] << 24;          // LE
        int reqTargetLE   = req[4] | req[5] << 8 | req[6] << 16 | req[7] << 24;          // LE

        Assert.Equal(8708585, reqGameLE);
        Assert.Equal(2617, reqTargetLE);
        Assert.Equal(reqGameLE, verbSubjectBE);
        Assert.Equal(reqTargetLE, verbObjectBE);
        Assert.Equal(1, verb[8] | verb[9] << 8 | verb[10] << 16 | verb[11] << 24);       // Action LE
    }

    // ── StarbaseAvatarChange 0x9D / 0x9E ─────────────────────────────────────
    // Two distinct 28-byte layouts. 0x9D (client->server) has a RoomType slot and
    // ends with ActionFlag; 0x9E (server->client) has no RoomType, Orient sits
    // right after AvatarID, and Room is appended last. Pin both so a future "merge
    // the two layouts" edit breaks here.

    [Fact]
    public void StarbaseAvatarChange_C2S_DecodesRoomTypeOrientPositionActionFlag()
    {
        string d = Dump("starbaseavatarchange_c2s_broadcast");

        Assert.Contains("AvatarID", d);
        Assert.Contains("0x00AACCEE", d);
        Assert.Contains("RoomType", d);
        Assert.Contains("[0004] RoomType", d);             // RoomType int32 at offset 4
        Assert.Contains("Orient            = 0.0", d);
        Assert.Contains("Position          = (17.487, 12.238, 4.111)", d);
        Assert.Contains("ActionFlag", d);
        Assert.Contains("[0018] ActionFlag", d);           // ActionFlag at offset 24 (0x18)
        Assert.Contains("0x00000041", d);                  // 0x41 broadcast
        Assert.Contains("broadcast", d);
        Assert.DoesNotContain("???", d);                   // all 28 bytes decoded
        Assert.DoesNotContain("[!]", d);
    }

    [Fact]
    public void StarbaseAvatarChange_S2C_DecodesOrientPositionFlagRoom_DistinctLayout()
    {
        string d = Dump("starbaseavatarchange_s2c_room1");

        Assert.Contains("AvatarID", d);
        Assert.Contains("0x06EC4715", d);
        Assert.Contains("[0004] Orient", d);               // Orient right after AvatarID (no RoomType)
        Assert.Contains("Position          = (-30.322, -44.953, -0.194)", d);
        Assert.Contains("[0014] ActionFlag", d);           // ActionFlag at offset 20 (0x14)
        Assert.Contains("Room", d);
        Assert.Contains("[0018] Room", d);                 // Room appended last, offset 24 (0x18)
        Assert.DoesNotContain("RoomType", d);              // the C2S-only slot must NOT appear
        Assert.DoesNotContain("???", d);                   // all 28 bytes decoded
        Assert.DoesNotContain("[!]", d);
    }

    // ── Turn 0x12 / Tilt 0x13 / Move 0x14 ────────────────────────────────────
    // The steering trio. Turn/Tilt share an 8-byte {int32 GameID; float Intensity}
    // layout (PacketTurn, raw LE -- no ntohl). Move is a 5-byte {int32 GameID;
    // byte type}; type == 4 is engine-off/break-formation, else engine-on. All
    // three carry the SAME player GameID 0x0084E1E9 as the RequestTarget/Action/
    // VerbRequest frames above -- the cross-packet identity lock extends here too.

    [Fact]
    public void Turn_DecodesGameIdAndFullLeftIntensity_LittleEndian()
    {
        string d = Dump("turn_full_left");

        Assert.Contains("[0000] GameID", d);
        Assert.Contains("0x0084E1E9", d);            // E9 E1 84 00 LE == 8708585, same player
        Assert.Contains("[0004] Intensity", d);
        Assert.Contains("= -1.0", d);                // 00 00 80 BF LE == float -1.0 (full deflection)
        Assert.Contains("(TURN -- yaw rate)", d);    // 0x12 axis annotation
        Assert.DoesNotContain("TILT", d);
        Assert.DoesNotContain("???", d);             // all 8 bytes decoded
        Assert.DoesNotContain("[!]", d);
    }

    [Fact]
    public void Tilt_DecodesFullUpIntensity_SameLayoutAsTurn_OpcodeDistinguishesAxis()
    {
        string d = Dump("tilt_full_up");

        Assert.Contains("[0000] GameID", d);
        Assert.Contains("0x0084E1E9", d);            // identical GameID slot to turn_full_left
        Assert.Contains("[0004] Intensity", d);
        Assert.Contains("= 1.0", d);                 // 00 00 80 3F LE == +1.0 (only the sign byte differs)
        Assert.DoesNotContain("= -1.0", d);
        Assert.Contains("(TILT -- pitch rate)", d);  // 0x13 axis annotation -- NOT the 0x12 "TURN" form
        Assert.DoesNotContain("TURN", d);
        Assert.DoesNotContain("???", d);             // all 8 bytes decoded
        Assert.DoesNotContain("[!]", d);
    }

    [Fact]
    public void TurnTilt_ByteIdenticalStruct_RoutedByOpcode()
    {
        // The 8-byte layout is identical for 0x12 and 0x13; only the opcode the
        // registry passes the record distinguishes them. Prove the routing is
        // wired both ways so a copy-paste 0x13 => TurnTiltRecord(payload, 0x0012)
        // regression is caught even though every field byte matches.
        var turn = Frames["turn_full_left"];
        var tilt = Frames["tilt_full_up"];
        Assert.Equal((ushort)0x12, PacketRecord.Resolve((ushort)turn.Opcode, turn.Payload).Opcode);
        Assert.Equal((ushort)0x13, PacketRecord.Resolve((ushort)tilt.Opcode, tilt.Payload).Opcode);
    }

    [Fact]
    public void Move_DecodesGameIdAndTypeByte_EngineOn()
    {
        string d = Dump("move_type0_engine_on");

        Assert.Contains("[0000] GameID", d);
        Assert.Contains("0x0084E1E9", d);            // same player again; 4-byte int32 GameID
        Assert.Contains("[0004] Type", d);           // single byte at offset 4
        Assert.Contains("(engine on)", d);           // type 0 -> engine on (type == 4 would be break-formation)
        Assert.DoesNotContain("break formation", d);
        Assert.DoesNotContain("???", d);             // all 5 bytes decoded
        Assert.DoesNotContain("[!]", d);
    }

    // ── ComponentPositionalUpdate 0x46 ───────────────────────────────────────
    // 64-byte packed struct: an embedded SimplePositionalUpdate (48B, the SAME
    // layout as 0x08) plus a 4-field tractor tail starting at offset 48. The
    // header's "this[68]" tail comments are off-by-8; the retail frame's
    // "Length = 68 bytes" (== payload 64 + 4) proves the tail is contiguous.

    [Fact]
    public void ComponentPos_DecodesEmbeddedSimplePosPlusTractorTail_LittleEndian()
    {
        string d = Dump("componentpos_tractor_player");

        // Embedded SimplePositionalUpdate half (offsets 0..47).
        Assert.Contains("[0000] GameID", d);
        Assert.Contains("0x0084E261", d);
        Assert.Contains("[0004] TimeStamp", d);
        Assert.Contains("0x251B7ACC", d);
        Assert.Contains("[0008] Position", d);
        Assert.Contains("(-23448.31, 57909.05, 266.981)", d);
        Assert.Contains("[0014] Orientation", d);             // offset 20 (0x14)
        Assert.Contains("(0.0, 0.0, 0.0, 1.0)", d);           // identity quaternion
        Assert.Contains("[0024] Velocity", d);                // offset 36 (0x24)
        Assert.Contains("(0.0, 0.0, 0.0)", d);                // stationary

        // Tractor tail (offsets 48..63) -- the part 0x08 does NOT carry.
        Assert.Contains("[0030] ImpartedDecay", d);           // offset 48 (0x30), NOT 0x44/this[68]
        Assert.Contains("ImpartedDecay     = 9.8", d);
        Assert.Contains("[0034] TractorSpeed", d);            // offset 52 (0x34)
        Assert.Contains("TractorSpeed      = 250.0", d);
        Assert.Contains("[0038] TractorID", d);               // offset 56 (0x38)
        Assert.Contains("0x0084E1E9", d);                     // beam acts on the same player as batches 6/7
        Assert.Contains("(object the beam acts on)", d);
        Assert.Contains("[003C] TractorEffectID", d);         // offset 60 (0x3C)
        Assert.Contains("0x0084E262", d);

        Assert.DoesNotContain("???", d);                      // all 64 bytes decoded
        Assert.DoesNotContain("[!]", d);
    }

    [Fact]
    public void ComponentPos_FirstFortyEightBytes_MatchSimplePosLayout()
    {
        // The embedded simple half must decode identically whether fed through the
        // 0x46 record or a standalone 0x08 SimplePosRecord -- same GameID/TimeStamp/
        // Position/Orientation/Velocity field lines for the first 48 bytes. This
        // locks the "0x46 reuses SimplePositionalUpdate verbatim" invariant.
        var full = Frames["componentpos_tractor_player"].Payload;
        var simpleHalf = full.AsSpan(0, 48).ToArray();

        string viaSimple = new SimplePosRecord(simpleHalf).DumpToString();
        Assert.Contains("0x0084E261", viaSimple);
        Assert.Contains("(-23448.31, 57909.05, 266.981)", viaSimple);
        Assert.Contains("(0.0, 0.0, 0.0, 1.0)", viaSimple);
    }

    // ── PushMessageLine 0x22 / QueueMessageLine 0x21 ─────────────────────────
    // Shared layout: raw NUL-terminated Message, raw NUL-terminated Type
    // (channel), int32 Time, int32 Priority -- all LE, NO length prefix. 0x22 is
    // emitter-grounded (Player::SendPushMessage); 0x21 is the retail-only
    // QueueMessageLine sibling with the identical wire shape.

    [Fact]
    public void PushMessage_LevelUp_DecodesMessageChannelTimePriority()
    {
        string d = Dump("pushmessage_level_up_quickline");

        Assert.Contains("[0000] Message", d);
        Assert.Contains("\"LEVEL UP!\"", d);          // raw NUL-terminated, no length prefix
        Assert.Contains("Type", d);
        Assert.Contains("\"QuickLine\"", d);          // channel string immediately after the NUL
        Assert.Contains("Time", d);
        Assert.Contains("Time              = 0", d);   // SendPushMessage("LEVEL UP!","QuickLine",0,3)
        Assert.Contains("Priority", d);
        Assert.Contains("Priority          = 3", d);
        Assert.DoesNotContain("???", d);              // every byte attributed to a field
        Assert.DoesNotContain("[!]", d);
    }

    [Fact]
    public void QueueMessage_GroupBonus_SameLayoutAsPush_RetailSibling()
    {
        string d = Dump("queuemessage_group_bonus");

        Assert.Contains("[0000] Message", d);
        Assert.Contains("\"Group experience bonus is now 40%\"", d);
        Assert.Contains("\"MessageLine\"", d);
        Assert.Contains("Time              = 3000", d); // corroborates Time (== 0x22 MessageLine duration)
        Assert.Contains("Priority          = 7", d);    // retail value -- not one our server emits
        Assert.DoesNotContain("???", d);
        Assert.DoesNotContain("[!]", d);
    }

    [Fact]
    public void PushQueueMessage_RoutedByOpcode()
    {
        // 0x21 and 0x22 share one record class; only the opcode the registry passes
        // distinguishes them. Pin both directions so a copy-paste swap is caught.
        var push  = Frames["pushmessage_level_up_quickline"];
        var queue = Frames["queuemessage_group_bonus"];
        Assert.Equal((ushort)0x22, PacketRecord.Resolve((ushort)push.Opcode, push.Payload).Opcode);
        Assert.Equal((ushort)0x21, PacketRecord.Resolve((ushort)queue.Opcode, queue.Payload).Opcode);
    }

    // ── InventoryMove 0x27 ───────────────────────────────────────────────────
    // 24-byte struct InvMove, ALL SIX int32 fields big-endian (every one ntohl'd
    // in Player::HandleInventoryMove) -- the uniform-BE cousin of 0x5A. The
    // GameID read BE equals the same player as the LE-id frames, locking the
    // convention; a naive all-LE read would yield a garbage 0xE9E18400 GameID.

    [Fact]
    public void InventoryMove_AllSixFieldsBigEndian()
    {
        string d = Dump("inventorymove_cargo_slot1");

        Assert.Contains("[0000] GameID", d);
        Assert.Contains("0x0084E1E9", d);             // 00 84 E1 E9 read BE == 8708585, the player
        Assert.Contains("(BE -- ntohl at parse)", d);
        Assert.Contains("FromInv           = 18", d);  // 00 00 00 12 BE
        Assert.Contains("FromSlot          = 1", d);   // 00 00 00 01 BE
        Assert.Contains("ToInv             = 0", d);   // 00 00 00 00 BE
        Assert.Contains("ToSlot            = -1", d);  // FF FF FF FF BE sentinel
        Assert.Contains("Num               = -1", d);  // FF FF FF FF BE sentinel
        Assert.DoesNotContain("???", d);               // all 24 bytes decoded
        Assert.DoesNotContain("[!]", d);
    }

    [Fact]
    public void InventoryMove_BigEndianGameId_MatchesLittleEndianFrames()
    {
        // Same player issued LE-id frames (0x17/0x2C) and this BE-id 0x27.
        // Read each with its own convention -> identical GameID. A wrong all-LE
        // read of 0x27 would give 0xE9E18400, not 0x0084E1E9.
        var inv = Frames["inventorymove_cargo_slot1"].Payload;
        var req = Frames["requesttarget_player_targets_2617"].Payload;
        int invGameBE = inv[0] << 24 | inv[1] << 16 | inv[2] << 8 | inv[3];   // BE
        int reqGameLE = req[0] | req[1] << 8 | req[2] << 16 | req[3] << 24;   // LE
        Assert.Equal(8708585, reqGameLE);
        Assert.Equal(reqGameLE, invGameBE);
    }

    // ── Warp 0x9B ────────────────────────────────────────────────────────────
    // Variable-length struct WarpPacket {int32 GameID; short Navs; int32
    // TargetID[Navs]}, ALL little-endian -- Player::HandleWarp casts the buffer
    // to WarpPacket* and reads with no ntohl; SetupWarpNavs copies exactly Navs
    // entries. Payload = 6 + 4*Navs. The 2-nav fixture exercises the array.

    [Fact]
    public void Warp_VariableLengthRoute_AllLittleEndian()
    {
        string d = Dump("warp_two_nav_route");

        Assert.Contains("[0000] GameID", d);
        Assert.Contains("0x00000E05", d);                 // 05 0E 00 00 LE == 3589
        Assert.Contains("Navs              = 2", d);       // 02 00 LE
        Assert.Contains("(LE; count of TargetID route entries)", d);
        Assert.Contains("[0006] TargetID[0]", d);
        Assert.Contains("0x00000A38", d);                 // 38 0A 00 00 LE == 2616
        Assert.Contains("[000A] TargetID[1]", d);
        Assert.Contains("0x00000A39", d);                 // 39 0A 00 00 LE == 2617
        Assert.DoesNotContain("???", d);                  // all 14 bytes decoded
        Assert.DoesNotContain("[!]", d);
    }

    [Fact]
    public void Warp_PayloadLength_MatchesSixPlusFourPerNav()
    {
        var f = Frames["warp_two_nav_route"];
        short navs = (short)(f.Payload[4] | f.Payload[5] << 8);   // LE
        Assert.Equal(2, navs);
        Assert.Equal(6 + 4 * navs, f.Payload.Length);            // 14 bytes
        Assert.Equal((ushort)0x9B, PacketRecord.Resolve((ushort)f.Opcode, f.Payload).Opcode);
    }

    // ── Trade_Action 0x1F ────────────────────────────────────────────────────
    // 5-byte wire: int32 GameID (LE) + u8 Action. Player::TradeAction writes the
    // partner GameID as a raw int32 (no htonl) and a single Action byte. The
    // Action code is annotated from the emitter's call sites (0=open ... 6=cancel).

    [Fact]
    public void TradeAction_GameIdLittleEndian_ActionLabelled()
    {
        string d = Dump("trade_open_window");

        Assert.Contains("[0000] GameID", d);
        Assert.Contains("0x06ED29A2", d);                 // A2 29 ED 06 LE == 116205986
        Assert.Contains("(116205986)", d);
        Assert.Contains("Action            = 0", d);
        Assert.Contains("(open trade window)", d);
        Assert.DoesNotContain("???", d);                  // all 5 bytes decoded
        Assert.DoesNotContain("[!]", d);
    }

    // ── ClientChatRequest 0xA3 ───────────────────────────────────────────────
    // Variable-length, all LE: int32 PlayerID + int32 type + three u16-length-
    // prefixed ASCII strings (no NUL) + int32 DataSize + optional block.
    // HandleClientChatRequest walks it with plain short/int32 reads, no ntohl.

    [Fact]
    public void ClientChatRequest_EnterChannel_AllStringsLittleEndian()
    {
        string d = Dump("chatrequest_enter_channel_general");

        Assert.Contains("[0000] PlayerID", d);
        Assert.Contains("0x0000141E", d);                 // 1E 14 00 00 LE == 5150
        Assert.Contains("Type              = 6", d);
        Assert.Contains("(CCE_ENTER_CHANNEL)", d);
        Assert.Contains("Len1              = 0", d);       // empty-string path
        Assert.Contains("Len2              = 7", d);
        Assert.Contains("String2           = \"General\"", d);
        Assert.Contains("Len3              = 1", d);
        Assert.Contains("DataSize          = 0", d);
        Assert.DoesNotContain("???", d);                  // all 26 bytes decoded
        Assert.DoesNotContain("[!]", d);
    }

    // ── Request_Time 0x44 ────────────────────────────────────────────────────
    // 4-byte client->server: a single int32 ms tick read LE (no ntohl), echoed
    // back in the 0x34 SET_CLIENT_TIME reply for latency measurement.

    [Fact]
    public void RequestTime_SingleTickLittleEndian()
    {
        string d = Dump("requesttime_client_tick");

        Assert.Contains("[0000] ClientTick", d);
        Assert.Contains("ClientTick        = 80574", d);   // BE 3A 01 00 LE == 80574
        Assert.Contains("(LE; client ms tick, echoed in 0x34 reply)", d);
        Assert.DoesNotContain("???", d);                   // all 4 bytes decoded
        Assert.DoesNotContain("[!]", d);
    }

    // ── InventorySort 0x28 ───────────────────────────────────────────────────
    // 21-byte struct InvSort: five int32 (all BE, ntohl'd like 0x27) + a trailing
    // u8 Reverse. TargetInv selects the container; Sort1..3 are the sort keys.

    [Fact]
    public void InventorySort_FiveInt32BigEndian_PlusReverseByte()
    {
        string d = Dump("inventorysort_cargo_by_name");

        Assert.Contains("[0000] ID", d);
        Assert.Contains("0x06ED2AD7", d);                  // BE == 116206295
        Assert.Contains("TargetInv         = 1", d);
        Assert.Contains("(BE; cargo)", d);
        Assert.Contains("Sort1             = 1", d);
        Assert.Contains("(BE; sort by name)", d);
        Assert.Contains("Sort2             = 4", d);
        Assert.Contains("Sort3             = 8", d);
        Assert.Contains("[0014] Reverse", d);              // offset 20 = 0x14
        Assert.Contains("Reverse           = 0", d);
        Assert.Contains("(ascending)", d);
        Assert.DoesNotContain("???", d);                   // all 21 bytes decoded
        Assert.DoesNotContain("[!]", d);
    }

    [Fact]
    public void InventorySort_BigEndianId_NaiveLittleEndianWouldBeGarbage()
    {
        // The five int32s use the same uniform-BE convention as 0x27. Reading ID
        // little-endian would give 0xD72AED06, not the 0x06ED2AD7 the server sees.
        var p = Frames["inventorysort_cargo_by_name"].Payload;
        int idBE = p[0] << 24 | p[1] << 16 | p[2] << 8 | p[3];
        Assert.Equal(116206295, idBE);
        Assert.Equal(0x06ED2AD7, idBE);
    }

    // ── StarbaseRoomChange 0x9F ──────────────────────────────────────────────
    // 12-byte struct {int32 AvatarID; int32 NewRoom; int32 OldRoom}, all LE
    // (HandleStarbaseRoomChange reads with no ntohl). NewRoom precedes OldRoom.

    [Fact]
    public void StarbaseRoomChange_FieldsLittleEndian_NewRoomBeforeOldRoom()
    {
        string d = Dump("starbaseroomchange_move_0_to_1");

        Assert.Contains("[0000] AvatarID", d);
        Assert.Contains("0x00AACCEE", d);                  // EE CC AA 00 LE == 11193582
        Assert.Contains("[0004] NewRoom", d);
        Assert.Contains("NewRoom           = 1", d);
        Assert.Contains("[0008] OldRoom", d);
        Assert.Contains("OldRoom           = 0", d);
        Assert.DoesNotContain("???", d);                   // all 12 bytes decoded
        Assert.DoesNotContain("[!]", d);
    }

    [Fact]
    public void StarbaseRoomChange_EnterStationSentinel()
    {
        string d = Dump("starbaseroomchange_player_06ed2ad7");
        Assert.Contains("0x06ED2AD7", d);                  // D7 2A ED 06 LE == 116206295
        Assert.Contains("NewRoom           = 0", d);
        Assert.Contains("OldRoom           = -1", d);
        Assert.Contains("(LE; -1 = just entered station)", d);
        Assert.DoesNotContain("[!]", d);
    }

    [Fact]
    public void StarbaseRoomChange_LittleEndianAvatarId_MatchesInventorySortBigEndianId()
    {
        // One player, two opcodes, opposite byte orders -- both decode to the
        // same GameID 0x06ED2AD7. 0x28 ntohl's its int32 (BE on the wire);
        // 0x9F reads raw (LE on the wire). The wire bytes are mirror images.
        var room = Frames["starbaseroomchange_player_06ed2ad7"].Payload;
        var sort = Frames["inventorysort_cargo_by_name"].Payload;
        int roomAvatarLE = room[0] | room[1] << 8 | room[2] << 16 | room[3] << 24;
        int sortIdBE     = sort[0] << 24 | sort[1] << 16 | sort[2] << 8 | sort[3];
        Assert.Equal(0x06ED2AD7, roomAvatarLE);
        Assert.Equal(roomAvatarLE, sortIdBE);
    }

    // ── StarbaseRoomChange S2C 0xA0 ──────────────────────────────────────────
    // Byte-identical to 0x9F (same struct, same emitter struct), server->client.
    // Shares StarbaseRoomChangeRecord; routes via the 0xA0 registry entry.

    [Fact]
    public void StarbaseRoomUpdate_S2C_SameStructAsClientVariant()
    {
        string d = Dump("starbaseroomupdate_s2c_move_0_to_1");

        Assert.Contains("[0000] AvatarID", d);
        Assert.Contains("0x06ED240A", d);                  // 0A 24 ED 06 LE == 116204554
        Assert.Contains("(LE; moving player GameID)", d);  // S2C-specific note
        Assert.Contains("NewRoom           = 1", d);
        Assert.Contains("OldRoom           = 0", d);
        Assert.DoesNotContain("???", d);                   // all 12 bytes decoded
        Assert.DoesNotContain("[!]", d);
    }

    [Fact]
    public void StarbaseRoomUpdate_S2C_RoutesToOpcodeA0()
    {
        var f = Frames["starbaseroomupdate_s2c_move_0_to_1"];
        Assert.Equal((ushort)0xA0, PacketRecord.Resolve((ushort)f.Opcode, f.Payload).Opcode);
        // And the C2S fixture still routes to 0x9F via the same shared record.
        var c = Frames["starbaseroomchange_move_0_to_1"];
        Assert.Equal((ushort)0x9F, PacketRecord.Resolve((ushort)c.Opcode, c.Payload).Opcode);
    }

    // ── SelectTalkTree 0x55 ──────────────────────────────────────────────────
    // Client picks an NPC conversation branch. 5-byte struct SelectTalkTree
    // {int32 PlayerID; u8 Selection}, all LE (HandleSelectTalkTree reads with no
    // ntohl). PlayerID is the targeted NPC -- only its low 24 bits matter.

    [Fact]
    public void SelectTalkTree_FieldsLittleEndian_NpcIdAndBranch()
    {
        string d = Dump("selecttalktree_npc_branch_230");

        Assert.Contains("[0000] PlayerID", d);
        Assert.Contains("0x0000141E", d);                       // 1E 14 00 00 LE == 5150
        Assert.Contains("(5150)", d);
        Assert.Contains("(LE; low 24 bits = NPC/object id)", d);
        Assert.Contains("[0004] Selection", d);
        Assert.Contains("(menu branch index)", d);              // Selection 230 == ordinary branch
        Assert.DoesNotContain("???", d);                        // all 5 bytes decoded
        Assert.DoesNotContain("[!]", d);
    }

    [Fact]
    public void SelectTalkTree_BackSelectionSentinel()
    {
        string d = Dump("selecttalktree_npc_back_0");

        Assert.Contains("0x0000141E", d);                       // same NPC, real capture
        Assert.Contains("[0004] Selection", d);
        Assert.Contains("(0 = more/back)", d);                  // reserved-value note on real bytes
        Assert.DoesNotContain("(menu branch index)", d);
        Assert.DoesNotContain("[!]", d);
    }

    [Fact]
    public void SelectTalkTree_RoutesToOpcode55()
    {
        var f = Frames["selecttalktree_npc_branch_230"];
        Assert.Equal((ushort)0x55, PacketRecord.Resolve((ushort)f.Opcode, f.Payload).Opcode);
    }

    // ── RefinerySetItem 0x7C ─────────────────────────────────────────────────
    // Client picks the item to refine. 8-byte struct ManufactureData {int32
    // GameID; int32 Data}. HandleRefineSetItem reads Data LE, byte-swapping only
    // when it exceeds 0xFFFF -- real item template ids stay under that.

    [Fact]
    public void RefinerySetItem_DataLittleEndian_ItemTemplateId()
    {
        string d = Dump("refinerysetitem_template_1237");

        Assert.Contains("[0000] GameID", d);
        Assert.Contains("0x0000271C", d);                                   // 1C 27 00 00 LE == 10012
        Assert.Contains("(LE; refinery context, not consumed by the handler)", d);
        Assert.Contains("[0004] Data", d);
        Assert.Contains("(LE; item template id to refine)", d);             // 1237 < 0xFFFF: LE read stands
        Assert.DoesNotContain("big-endian", d);                            // hedge branch NOT taken
        Assert.DoesNotContain("???", d);                                    // all 8 bytes decoded
        Assert.DoesNotContain("[!]", d);
    }

    [Fact]
    public void RefinerySetItem_DataValueIs1237()
    {
        var f = Frames["refinerysetitem_template_1237"];
        // Data is the second int32 (offset 4), little-endian == 1237.
        Assert.Equal(1237, System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(f.Payload.AsSpan(4, 4)));
        Assert.Equal((ushort)0x7C, PacketRecord.Resolve((ushort)f.Opcode, f.Payload).Opcode);
    }
}
