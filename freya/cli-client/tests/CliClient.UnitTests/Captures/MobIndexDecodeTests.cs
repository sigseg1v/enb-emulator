// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using N7.CliClient.Logging;
using N7.CliClient.Opcodes.Records;
using Xunit;

namespace N7.CliClient.UnitTests.Captures;

/// <summary>
/// Byte-exact lock for the 22 live 0x001B AuxMobIndex create/click frames in the
/// 2026-06-02 net7 captures (NPCs and creatures: "Craxel", "Juuona Master",
/// "Jounna Youngling", "Love Bug", "Crystalline Gamma", plus two click/target
/// bodies). The live net-7.org server sent these to the real retail Win32 client
/// which rendered them, so the bytes are primary-source ground truth.
///
/// These were formerly quarantined as a "newer ShipIndex divergence" -- a wrong
/// diagnosis. The producing class is <c>AuxMobIndex</c>, whose BuildCreatePacket
/// / BuildClickPacket hand-roll a fixed 15-byte flag block with cross-byte
/// <c>buffer[N] &amp; mask</c> gating and 0x05 deletion markers; that layout is
/// NOT the generic AuxBase "present at bit N+4" form, so it lives in the
/// dedicated <see cref="N7.CliClient.Opcodes.Records.Aux.AuxMobIndexDecoder"/>
/// rather than as an <c>AuxSchema</c> candidate. The embedded Shield is the
/// 13-byte AuxPercent the live server emits (one flag byte + EndTime + per-tick
/// + start value). DO NOT edit the fixture bytes.
/// </summary>
public sealed class MobIndexDecodeTests
{
    private static readonly IReadOnlyDictionary<string, CaptureFixture> Frames =
        CaptureFixture.Load("mobindex-create-click-2026-06-02.txt");

    public MobIndexDecodeTests()
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

    [Fact]
    public void Fixture_Loads_AllTwentyTwoFrames()
    {
        Assert.Equal(22, Frames.Count);
        Assert.All(Frames.Values, f => Assert.Equal(0x001B, f.Opcode));
        Assert.True(Frames.ContainsKey("mobidx_cap1_273"));   // "Craxel" create
        Assert.True(Frames.ContainsKey("mobidx_cap2_991"));   // a click/target body
    }

    // ── The universal contract ───────────────────────────────────────────────
    // Every MobIndex frame must decode to the dedicated AuxData record (never the
    // unknown-opcode GenericRecord), consume every byte (no '???'/'(NB)' gap),
    // and carry NO divergence/truncation flag. The capture is correct by
    // definition; anything else is a decoder bug.
    [Theory]
    [MemberData(nameof(AllFrames))]
    public void EveryMobIndexFrame_DecodesCleanly_AsMobIndex(string name)
    {
        var f = Frames[name];
        var record = PacketRecord.Resolve((ushort)f.Opcode, f.Payload);

        Assert.IsType<AuxDataRecord>(record);

        string d = record.DumpToString();
        Assert.Contains(Field("AuxType") + "MobIndex", d);
        Assert.DoesNotContain("???", d);
        Assert.DoesNotContain("(NB)", d);
        Assert.DoesNotContain("[!]", d);
        Assert.DoesNotContain("schema diverged", d);
        Assert.DoesNotContain("truncated", d);
        Assert.DoesNotContain("undecoded", d);
    }

    // The dump pads a field name to 18 columns then appends "= "; a nested field
    // is rendered with a 2-space-per-depth indent BEFORE that padding. Compute
    // the exact left side so the assertions don't hand-count whitespace.
    private static string Field(string name, int depth = 0)
        => (new string(' ', depth * 2) + name).PadRight(18) + "= ";

    // ── "Craxel" create -- the canonical NPC-spawn representative ─────────────
    // Header GameID + Name, the 13-byte embedded Shield (StartValue 1.0 = full),
    // IsOrganic, CombatLevel, then the five 0x05 deletion markers for the
    // absent QuadrantDamage/Damage decals/Lego, then the two engine state ints.
    [Fact]
    public void Craxel_Create_DecodesNameShieldCombatLevelAndMarkers()
    {
        string d = Dump("mobidx_cap1_273");

        Assert.Contains(Field("AuxType") + "MobIndex (create)", d);
        Assert.Contains(Field("MobIndex.GameID") + "0x0001872C", d);
        Assert.Contains(Field("Name") + "\"Craxel\"", d);
        Assert.Contains(Field("Shield.StartValue", 1) + "1.0", d);
        Assert.Contains(Field("IsOrganic") + "true", d);
        Assert.Contains(Field("CombatLevel") + "7  (0x00000007)", d);
        Assert.Contains(Field("QuadrantDamage", 1) + "(deleted: 0x05)", d);
        Assert.Contains(Field("Lego", 1) + "(deleted: 0x05)", d);
        Assert.Contains("EngineThrustState", d);
        Assert.Contains("EngineTrailType", d);
    }

    // ── A multi-word NPC name proves the AddString length prefix is right ─────
    [Fact]
    public void JuuonaMaster_Create_DecodesMultiWordName()
    {
        string d = Dump("mobidx_cap1_317");
        Assert.Contains(Field("AuxType") + "MobIndex (create)", d);
        Assert.Contains(Field("Name") + "\"Juuona Master\"", d);
        Assert.Contains(Field("CombatLevel") + "10  (0x0000000A)", d);
    }

    // ── A click/target body -- no Name, but Shield + MaxShield + Faction ──────
    // The trimmed packet sent when a player targets the object. Its first flag
    // byte is the literal 0x06 BuildClickPacket constant; it carries the shield
    // pair and the faction string ("None").
    [Fact]
    public void ClickBody_DecodesShieldMaxShieldAndFaction()
    {
        string d = Dump("mobidx_cap2_991");

        Assert.Contains(Field("AuxType") + "MobIndex (click)", d);
        Assert.Contains(Field("Shield.StartValue", 1) + "1.0", d);
        Assert.Contains(Field("MaxShield") + "8960.0", d);
        Assert.Contains(Field("FactionIdentifier") + "\"None\"", d);
        Assert.DoesNotContain(Field("Name"), d);
    }

    // ── The world model harvests Name + CombatLevel from these frames ─────────
    [Fact]
    public void Summary_ExtractsNameAndCombatLevel_FromCreate()
    {
        var f = Frames["mobidx_cap1_273"];
        var s = AuxDataRecord.TryExtractSummary(f.Payload);

        Assert.NotNull(s);
        Assert.Equal(0x0001872Cu, s!.Value.GameId);
        Assert.Equal("Craxel", s.Value.Name);
        Assert.Equal(7u, s.Value.CombatLevel);
    }
}
