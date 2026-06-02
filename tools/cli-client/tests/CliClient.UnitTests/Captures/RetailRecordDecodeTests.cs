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
/// RelationshipRecord, AvatarDescriptionRecord.
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
    public void Fixture_Loads_AllElevenFrames()
    {
        Assert.Equal(11, Frames.Count);
        Assert.Equal(0x25, Frames["itembase_sand"].Opcode);
        Assert.Equal(98, Frames["itembase_sand"].Payload.Length);
        Assert.Equal(0x1B, Frames["aux_ship_turret"].Opcode);
        Assert.Equal(0x11, Frames["colorization_default"].Opcode);
        Assert.Equal(134, Frames["colorization_default"].Payload.Length);
        Assert.Equal(0x61, Frames["avatardesc_ace"].Opcode);
        Assert.Equal(260, Frames["avatardesc_ace"].Payload.Length);
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
}
