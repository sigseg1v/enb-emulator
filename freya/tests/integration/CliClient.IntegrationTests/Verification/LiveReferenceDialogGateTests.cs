// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya

using N7.CliClient.Logging;
using N7.CliClient.Opcodes.Records;
using Xunit;

namespace N7.CliClient.IntegrationTests.Verification;

/// <summary>
/// Phase AF (plans/33) -- byte-pins for the vendor/dialog and gate-jump bands
/// against frames captured from the LIVE Net-7 reference server
/// Each of these opcodes already had a dedicated CLI record;
/// this confirms those records decode the live reference bytes with full
/// coverage (no undecoded gap, no truncation flag) and produce the expected
/// fields. See <see cref="LiveReferenceFabricationTests"/> for the fabrication
/// + combat bands.
/// </summary>
public sealed class LiveReferenceDialogGateTests
{
    public LiveReferenceDialogGateTests() => AnsiPalette.Enabled = false;

    private static string DecodeClean(ushort opcode, string fixture, int expectLen, System.Type recordType)
    {
        byte[] b = HexFixture.Load(fixture);
        Assert.Equal(expectLen, b.Length);
        var rec = PacketRecord.Resolve(opcode, b);
        Assert.IsType(recordType, rec);
        string dump = rec.DumpToString();
        Assert.DoesNotContain("???", dump);   // every byte decoded
        Assert.DoesNotContain("[!]", dump);   // no truncation / overrun flag
        return dump;
    }

    [Fact]
    public void TalkTree_0x0054_VendorDialog_LiveCapture()
    {
        string d = DecodeClean(0x0054, "live_talktree_0054_vendor.hex", 166, typeof(TalkTreeRecord));
        Assert.Contains("Greetings! Starstrukk", d);
        Assert.Contains("Trade!", d);
        Assert.Contains("No Thanks!", d);
    }

    [Fact]
    public void TalkTreeAction_0x0056_LiveCapture()
    {
        string d = DecodeClean(0x0056, "live_talktreeaction_0056.hex", 4, typeof(TalkTreeActionRecord));
        Assert.Contains("Action", d);
    }

    [Fact]
    public void ClientSound_0x006A_Coin_LiveCapture()
    {
        string d = DecodeClean(0x006A, "live_clientsound_006A_coin.hex", 18, typeof(ClientSoundRecord));
        Assert.Contains("coin.wav", d);
    }

    [Fact]
    public void ClientSetTime_0x0034_LiveCapture()
    {
        // The live reference emits ServerReceived == ServerSent (+0 tick),
        // matching our server -- a live data point against the Z-4 "retail +1"
        // note (plans/33, plans/26 Z-4).
        string d = DecodeClean(0x0034, "live_clientsettime_0034.hex", 12, typeof(ClientSetTimeRecord));
        Assert.Contains("0x51529FB0", d);   // ServerReceived AND ServerSent
    }

    [Fact]
    public void ServerHandoff_0x003A_GateJump_LiveCapture()
    {
        string d = DecodeClean(0x003A, "live_serverhandoff_003A.hex", 112, typeof(ServerHandoffRecord));
        Assert.Contains("MY_Avatar_Ticket", d);
        Assert.Contains("Ishuan (Castor System)", d);
        Assert.Contains("Yokan", d);
        Assert.Contains("Capella", d);
    }

    [Fact]
    public void GalaxyMap_0x0097_LiveCapture()
    {
        string d = DecodeClean(0x0097, "live_galaxymap_0097.hex", 31, typeof(GalaxyMapRecord));
        Assert.Contains("Capella", d);
        Assert.Contains("Yokan", d);
    }

    [Fact]
    public void WarpIndex_0x009C_LiveCapture()
    {
        string d = DecodeClean(0x009C, "live_warpindex_009C.hex", 4, typeof(WarpIndexRecord));
        Assert.Contains("-1", d);
    }
}
