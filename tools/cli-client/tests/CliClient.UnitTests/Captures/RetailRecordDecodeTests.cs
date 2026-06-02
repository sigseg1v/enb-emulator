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
    public void Fixture_Loads_AllFortyFrames()
    {
        Assert.Equal(40, Frames.Count);
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
}
