// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using N7.CliClient.Logging;
using N7.CliClient.Opcodes.Records;
using Xunit;

namespace N7.CliClient.UnitTests.Captures;

/// <summary>
/// Ground-truth lock for the 3 residual 0x001B AuxHulkIndex frames in the
/// 2026-06-02 net7 captures (all gid 0x0001887D, "Corpse of Crystalline Gamma"):
/// a small corpse create, the full corpse create carrying AuxInventory20/40
/// equip + cargo item lists (540 bytes), and a hulk diff update. The live
/// net-7.org server sent these to the real retail Win32 client, so the bytes are
/// valid primary source.
///
/// These were the 3 frames left over when the AuxMobIndex create/click decoder
/// landed (the other 22 in the old "shipindex divergence" fixture were MobIndex
/// and now decode byte-exact -- see <see cref="MobIndexDecodeTests"/>). They
/// still flag DIVERGENT ([!]) because the AuxHulkIndex layout -- in particular
/// the nested AuxInventory20 (equip) and AuxInventory40 (cargo) item containers,
/// and the diff-packet form -- is not yet modelled. That is a missing decoder,
/// NOT wrong bytes: do not "fix" it by editing the fixture.
///
/// This test keeps the exact divergent bytes in version control and makes the
/// divergence un-loseable. When an AuxHulkIndex decoder lands and these frames
/// decode CLEANLY, THIS TEST WILL FAIL ON PURPOSE -- the signal to move the
/// now-clean frames into a dedicated clean fixture/test (as was done for
/// MobIndex) and delete them from here.
/// </summary>
public sealed class HulkIndexDivergenceTests
{
    private static readonly IReadOnlyDictionary<string, CaptureFixture> Frames =
        CaptureFixture.Load("hulkindex-residual-2026-06-02.txt");

    public HulkIndexDivergenceTests()
    {
        AnsiPalette.Enabled = false;
    }

    public static IEnumerable<object[]> AllFrames() =>
        Frames.Keys.Select(k => new object[] { k });

    [Fact]
    public void Fixture_Loads_AllThreeFrames()
    {
        Assert.Equal(3, Frames.Count);
        Assert.All(Frames.Values, f => Assert.Equal(0x001B, f.Opcode));
        // The three corpse frames: small create, full-inventory create, diff.
        Assert.True(Frames.ContainsKey("hulkidx_cap2_1195"));
        Assert.True(Frames.ContainsKey("hulkidx_cap2_1198"));
        Assert.True(Frames.ContainsKey("hulkidx_cap2_1269"));
    }

    // ── The divergence lock ──────────────────────────────────────────────────
    // Each frame must resolve to the dedicated AuxData record (the outer AuxBase
    // header IS modelled, so never the unknown-opcode GenericRecord) AND must
    // still carry the '[!]' divergence marker, because the AuxHulkIndex body is
    // not yet ported. When a HulkIndex decoder lands and these decode cleanly,
    // this assertion flips and forces the migration described in the summary.
    [Theory]
    [MemberData(nameof(AllFrames))]
    public void EveryResidualFrame_IsAuxData_AndStillFlagsDivergence(string name)
    {
        var f = Frames[name];
        var record = PacketRecord.Resolve((ushort)f.Opcode, f.Payload);

        Assert.IsType<AuxDataRecord>(record);

        string d = record.DumpToString();
        Assert.Contains("[!]", d);
        Assert.Contains("schema diverged", d);
    }
}
