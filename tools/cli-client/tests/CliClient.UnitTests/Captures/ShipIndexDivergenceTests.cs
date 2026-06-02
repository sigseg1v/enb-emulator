// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using N7.CliClient.Logging;
using N7.CliClient.Opcodes.Records;
using Xunit;

namespace N7.CliClient.UnitTests.Captures;

/// <summary>
/// Ground-truth lock for the 25 live 0x001B AuxData / ShipIndex frames that the
/// 2026-06-02 net7 captures contain and that our decoder flags as DIVERGENT.
///
/// These are net-7.org-2026's NEWER ShipIndex serialization: neither preserved
/// upstream fork (tada-o nor the kyp snapshot) emits this layout, and our server
/// therefore does not produce it. The captures were taken from the live net7
/// server talking to the real retail Win32 client, which rendered the ships, so
/// the bytes are a valid primary source -- matching them is a fidelity goal, not
/// a violation (see plans/11-phase-k-ingame.md Wave 337, reopened [~]).
///
/// The fix is BLOCKED: forcing these bytes through our 58-field _ShipIndex model
/// yields garbage field values (e.g. the NPC-create frames have a 15-byte flag
/// block and a fixed preamble C3 00 AE 40 F8 C0 07 our BuildCreatePacket never
/// emits), proving the layout differs in field gating/order/types -- not merely
/// "our fields plus extras". A correct port needs net-7.org-2026 server source
/// or a far larger varied capture corpus to triangulate each field. Until then
/// hardcoding the captured bytes would emit constant wrong data for every entity
/// and would violate the CLAUDE.md no-guess server-integrity rule.
///
/// This test exists so the exact divergent bytes live in version control (not in
/// ephemeral /tmp analysis files) and so the divergence cannot silently
/// disappear. If a future port lands and these frames start decoding CLEANLY,
/// THIS TEST WILL FAIL ON PURPOSE -- that is the signal to move the now-clean
/// frames into <see cref="LiveTraceDecodeTests"/> (the no-gap/no-flag fixture)
/// and delete them from here. A clean decode here means the gap closed.
/// </summary>
public sealed class ShipIndexDivergenceTests
{
    private static readonly IReadOnlyDictionary<string, CaptureFixture> Frames =
        CaptureFixture.Load("shipindex-newer-format-divergence-2026-06-02.txt");

    public ShipIndexDivergenceTests()
    {
        // Decode to plain text so the marker assertions see no ANSI codes.
        AnsiPalette.Enabled = false;
    }

    public static IEnumerable<object[]> AllFrames() =>
        Frames.Keys.Select(k => new object[] { k });

    [Fact]
    public void Fixture_Loads_AllTwentyFiveFrames()
    {
        Assert.Equal(25, Frames.Count);
        // Every frame is 0x001B AUX_DATA.
        Assert.All(Frames.Values, f => Assert.Equal(0x001B, f.Opcode));
        // The canonical NPC-create representative: "Craxel" from the first pcap.
        Assert.True(Frames.ContainsKey("shipidx_div_cap1_273"));
    }

    // ── The divergence lock ──────────────────────────────────────────────────
    // Each frame must resolve to the dedicated AuxData record (never the
    // unknown-opcode GenericRecord -- the OUTER AuxBase header IS modelled) AND
    // must still carry the '[!]' divergence marker, because the INNER ShipIndex
    // body is net-7.org's unported newer layout. When the port lands and these
    // decode cleanly, this assertion flips and forces the migration described in
    // the class summary.
    [Theory]
    [MemberData(nameof(AllFrames))]
    public void EveryDivergentFrame_IsAuxData_AndStillFlagsDivergence(string name)
    {
        var f = Frames[name];
        var record = PacketRecord.Resolve((ushort)f.Opcode, f.Payload);

        Assert.IsType<AuxDataRecord>(record);

        string d = record.DumpToString();
        Assert.Contains("[!]", d);
        Assert.Contains("schema diverged", d);
    }
}
