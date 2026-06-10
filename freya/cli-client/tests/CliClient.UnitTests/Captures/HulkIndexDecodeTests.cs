// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using N7.CliClient.Logging;
using N7.CliClient.Opcodes.Records;
using Xunit;

namespace N7.CliClient.UnitTests.Captures;

/// <summary>
/// Byte-exact lock for the 3 live 0x001B AuxHulkIndex frames in the 2026-06-02
/// net7 captures (all gid 0x0001887D, "Corpse of Crystalline Gamma"): a small
/// corpse create (name + 4 deletion markers), the full corpse create carrying
/// AuxInventory20 (equip) + AuxInventory40 (cargo) item lists -- 540 bytes, two
/// real cargo items -- and a hulk diff update (one plain cargo item). The live
/// net-7.org server sent these to the real retail Win32 client, so the bytes are
/// primary-source ground truth.
///
/// These were the residual frames left behind when the AuxMobIndex decoder
/// landed; they now decode byte-exact via
/// <see cref="N7.CliClient.Opcodes.Records.Aux.AuxHulkIndexDecoder"/> (create +
/// diff). The on-wire bodyLen is (payload-6) for the two non-inventory frames but
/// (payload-8) for the inventory-bearing create -- a live-server quirk; the
/// inner-bundle length is the real boundary. DO NOT edit the fixture bytes.
/// </summary>
public sealed class HulkIndexDecodeTests
{
    private static readonly IReadOnlyDictionary<string, CaptureFixture> Frames =
        CaptureFixture.Load("hulkindex-create-diff-2026-06-02.txt");

    public HulkIndexDecodeTests()
    {
        AnsiPalette.Enabled = false;
    }

    private static string Dump(string name)
    {
        var f = Frames[name];
        return PacketRecord.Resolve((ushort)f.Opcode, f.Payload).DumpToString();
    }

    public static IEnumerable<object[]> AllFrames() =>
        Frames.Keys.Select(k => new object[] { k });

    // The dump pads a field name to 18 columns then appends "= "; nested fields
    // get a 2-space-per-depth indent before that padding (mirrors MobIndex test).
    private static string Field(string name, int depth = 0)
        => (new string(' ', depth * 2) + name).PadRight(18) + "= ";

    [Fact]
    public void Fixture_Loads_AllThreeFrames()
    {
        Assert.Equal(3, Frames.Count);
        Assert.All(Frames.Values, f => Assert.Equal(0x001B, f.Opcode));
        Assert.True(Frames.ContainsKey("hulkidx_cap2_1195"));
        Assert.True(Frames.ContainsKey("hulkidx_cap2_1198"));
        Assert.True(Frames.ContainsKey("hulkidx_cap2_1269"));
    }

    // ── The universal contract ───────────────────────────────────────────────
    // Every HulkIndex frame must decode to the dedicated AuxData record, consume
    // every byte (no '???'/'(NB)' gap), and carry NO divergence/truncation flag.
    [Theory]
    [MemberData(nameof(AllFrames))]
    public void EveryHulkIndexFrame_DecodesCleanly_AsHulkIndex(string name)
    {
        var f = Frames[name];
        var record = PacketRecord.Resolve((ushort)f.Opcode, f.Payload);

        Assert.IsType<AuxDataRecord>(record);

        string d = record.DumpToString();
        Assert.Contains(Field("AuxType") + "HulkIndex", d);
        Assert.Contains(Field("HulkIndex.GameID") + "0x0001887D", d);
        Assert.DoesNotContain("???", d);
        Assert.DoesNotContain("(NB)", d);
        Assert.DoesNotContain("[!]", d);
        Assert.DoesNotContain("schema diverged", d);
        Assert.DoesNotContain("truncated", d);
        Assert.DoesNotContain("undecoded", d);
    }

    // ── small create: name + the four absent-decal deletion markers ───────────
    [Fact]
    public void SmallCreate_DecodesNameAndFourMarkers()
    {
        string d = Dump("hulkidx_cap2_1195");

        Assert.Contains(Field("AuxType") + "HulkIndex (create)", d);
        Assert.Contains(Field("Name") + "\"Corpse of Crystalline Gamma\"", d);
        Assert.Contains(Field("BodyLen") + "37  (payload-6)", d);
        Assert.Contains(Field("QuadrantDamage", 1) + "(deleted: 0x05)", d);
        Assert.Contains(Field("DamageSpot", 1) + "(deleted: 0x05)", d);
        Assert.Contains(Field("DamageLine", 1) + "(deleted: 0x05)", d);
        Assert.Contains(Field("DamageBlotch", 1) + "(deleted: 0x05)", d);
    }

    // ── full create: nested equip(20)+cargo(40), two real cargo items ─────────
    // bodyLen is payload-8 here (the inventory-bearing-create live-server quirk);
    // the frame is delimited by its 540-byte inner-bundle length, not bodyLen.
    [Fact]
    public void FullCreate_DecodesBothInventoriesAndTwoRealItems()
    {
        string d = Dump("hulkidx_cap2_1198");

        Assert.Contains(Field("AuxType") + "HulkIndex (create)", d);
        Assert.Contains(Field("BodyLen") + "532  (payload-8, inventory-bearing)", d);
        Assert.Contains("EquipInv", d);
        Assert.Contains("(20 slots: 20 present, 0 deleted)", d);
        Assert.Contains("CargoInv", d);
        Assert.Contains("(40 slots: 40 present, 0 deleted)", d);

        // The two equipped items the server stored on the corpse, in cargo[0..1].
        Assert.Contains("CargoInv[0].ItemTemplateID", d);
        Assert.Contains("6418", d);
        Assert.Contains("CargoInv[1].ItemTemplateID", d);
        Assert.Contains("1946", d);
        Assert.Contains("9:38.00^21:7.20^", d);     // the InstanceInfo string
        // The empty slots collapse to a single id=-2 line, not a wall of fields.
        Assert.Contains("(empty, id=-2)", d);
    }

    // ── diff: one plain (non-extended) cargo item update ──────────────────────
    [Fact]
    public void Diff_DecodesSinglePlainCargoItem()
    {
        string d = Dump("hulkidx_cap2_1269");

        Assert.Contains(Field("AuxType") + "HulkIndex (diff)", d);
        Assert.Contains(Field("BodyLen") + "19  (payload-6)", d);
        Assert.Contains("CargoInv", d);
        Assert.Contains("(40 slots: 1 present)", d);
        Assert.DoesNotContain(Field("Name"), d);    // diff carries no name
    }

    // ── the world model harvests the corpse Name from these frames ────────────
    [Fact]
    public void Summary_ExtractsCorpseName_FromCreate()
    {
        var f = Frames["hulkidx_cap2_1198"];
        var s = AuxDataRecord.TryExtractSummary(f.Payload);

        Assert.NotNull(s);
        Assert.Equal(0x0001887Du, s!.Value.GameId);
        Assert.Equal("Corpse of Crystalline Gamma", s.Value.Name);
    }
}
