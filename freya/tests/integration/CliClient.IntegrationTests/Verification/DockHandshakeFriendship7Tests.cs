// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya

using N7.CliClient.Auth;
using N7.CliClient.IntegrationTests.Opcodes;
using N7.CliClient.Net;
using N7.CliClient.Opcodes;
using Xunit;
using Xunit.Abstractions;

namespace N7.CliClient.IntegrationTests.Verification;

/// <summary>
/// Station-sector handshake byte-diff test targeting sector 45151
/// (Friendship 7 Recreation Port, Glenn Commission, Sirius system) --
/// the SAME destination station as the retail capture in
/// <c>archive/kyp-snapshot/capturedPackets/capture_1.rar</c>. Where
/// <see cref="DockHandshakeRetailParityTests"/> pins sector-INVARIANT
/// shape (no 0x00A5 self-broadcast, 0x007F MANU_TAG bits) using any
/// station, this suite targets the EXACT station retail's player landed
/// in so that per-station content (lounge NPCs, station NPC roster,
/// starbase config) is part of the byte-diff.
///
/// <para>
/// Sequencing. A fresh character's first sector LOGIN to any sector
/// gets routed through their race/profession <c>StartSector</c> by
/// <see cref="Player.ReInitializeSavedData"/> (Terran/Warrior =&gt;
/// 10151 Luna), which resets <c>sector_num</c> to the home StartSector
/// regardless of the LOGIN packet's ToSectorId. To actually exercise
/// Friendship 7's <c>SectorManager::StationLogin</c> we two-stage:
/// stage 1 EstablishAsync at Luna home so the avatar gets initialized
/// + an <c>avatar_level_info</c> row is written; LogoffRequest + drain
/// + Dispose to release the sector connection; stage 2 ReestablishAsync
/// at 45151. The second login takes the <c>ReloadSavedData</c> path
/// (avatar_level_info exists), which preserves the sector_num set by
/// <c>Player::HandleLogin</c> from the LOGIN packet's ToSectorId
/// (<c>server/src/PlayerSaves.cpp:289-291</c>). Player::GetSectorManager
/// then returns the Friendship 7 SectorManager, not Luna's. This
/// mirrors the retail capture player's path: an established avatar
/// re-logging into Friendship 7, not a first-time character creation
/// there. Phase K Wave 117 (commit ac7d49f) enabled SendGalaxyMap in
/// StationLogin off this test, but only checked histogram counts -- the
/// 0x0097 GalaxyMap payload bytes shipped Luna's strings, not
/// Friendship 7's, because the fresh-character stage-1 flow this test
/// originally used routed to Luna's SectorManager. Wave 118 (this
/// rewrite) corrects the routing so the byte content is actually
/// Friendship 7's.
/// </para>
///
/// <para>
/// What this test pins. The histogram of opcodes emitted by the sector
/// TCP from the first server frame after the client's LOGIN through and
/// including the terminating 0x0005 START frame -- on the stage-2
/// Friendship 7 session. Counts come from a hex-dump of the retail
/// capture; see the <see cref="ExpectedRetailHistogram"/> constant for
/// the extraction recipe a contributor can reproduce against
/// <c>archive/kyp-snapshot/capturedPackets/capture_1.rar</c>. A
/// divergence is either (a) StationLogin code-path drift in our server
/// vs retail -- the actionable kind -- or (b) starbase 73 / sector
/// 45151 missing or differing seed data in our DB vs the retail
/// server's prod state -- the curatorial kind addressed by
/// <c>plans/25-phase-y-data-import.md</c>. Both matter; the diff
/// distinguishes them.
/// </para>
/// </summary>
[Collection(ServerCollection.Name)]
public sealed class DockHandshakeFriendship7Tests : SectorIntegrationTest
{
    private readonly ITestOutputHelper _out;

    public DockHandshakeFriendship7Tests(ServerFixture server, ITestOutputHelper output) : base(server)
    {
        _out = output;
    }

    /// <summary>
    /// Retail sector-handshake opcode histogram extracted from the hex-dump
    /// inside <c>archive/kyp-snapshot/capturedPackets/capture_1.rar</c>,
    /// sector socket only (IP 159.153.232.46 for the sector-server host;
    /// the .146 IP is the master / login host whose frames are NOT part
    /// of our sector-TCP drain). Each entry is "{count}x 0x{opcode:X4}".
    /// Sorted DESC by count then ASC by opcode value -- matches the
    /// ordering used by the actual-histogram computation in the test below.
    ///
    /// <para>Extraction recipe (key constraint: filter by sector IP, not
    /// just by direction -- the global server on .146 emits its own 0x0036
    /// CLIENT_REDIRECT frame during the handoff which is NOT a sector
    /// frame and would otherwise leak in). Run from the repo root:</para>
    /// <code>
    /// unrar p -inul archive/kyp-snapshot/capturedPackets/capture_1.rar \
    ///  | awk 'BEGIN{dir=""; done=0} done==1 {next}
    ///         /^Packet #.*Server-&gt;Client.*159\.153\.232\.46:/{dir="s2c-sector"; next}
    ///         /^Packet #.*Server-&gt;Client/{dir="s2c-other"; next}
    ///         /^Packet #.*Client-&gt;Server/{dir="c2s"; next}
    ///         /^Packet #/{dir=""; next}
    ///         dir=="s2c-sector" &amp;&amp; /^ [0-9A-F]{2} 00 +Opcode 0x/{
    ///           print $4; if ($4 == "0x05") done=1
    ///         }' \
    ///  | sort | uniq -c | sort -k1,1rn -k2,2
    /// </code>
    ///
    /// <para>
    /// 101 total sector frames. 0x0025 ItemBase dominates (77x) because
    /// Friendship 7 is a casino lounge with a packed NPC roster; the
    /// 0x0052 LoungeNpc frame is 3404 bytes packing those NPCs'
    /// dialog/avatar/ship data.
    /// </para>
    /// </summary>
    private const string ExpectedRetailHistogram = """
        77x 0x0025
         3x 0x001B
         2x 0x0004
         2x 0x0089
         1x 0x0005
         1x 0x0009
         1x 0x0010
         1x 0x0011
         1x 0x001D
         1x 0x0034
         1x 0x0037
         1x 0x003E
         1x 0x0040
         1x 0x0047
         1x 0x004F
         1x 0x0052
         1x 0x0061
         1x 0x007F
         1x 0x0097
         1x 0x00B2
         1x 0x00B4
        """;

    // Blocked on Phase Y seed data, NOT a forced green. Against a fresh
    // stack our actual histogram is (full diff vs ExpectedRetailHistogram):
    //   0x0025 ITEM_BASE :  4  vs 77  (-73)  -- dominant gap
    //   0x001B AUX_DATA  :  4  vs  3  (+1)
    //   0x0009           :  0  vs  1  (-1, absent)
    //   every other opcode TYPE and count matches exactly.
    // The 73-frame 0x0025 deficit is Friendship 7's retail vendor/NPC
    // inventory (starbase 73 / sector 45151) that our DB does not yet seed
    // -- the curatorial gap tracked by plans/25-phase-y-data-import.md.
    // The residual deltas (extra AUX_DATA, missing 0x0009) are NOT proven
    // data-only: they COULD be StationLogin code-path drift. But they
    // cannot be isolated while the dominant item gap stands, because item
    // count perturbs both AUX_DATA chunking and item-adjacent emission.
    // So this test is blocked until the inventory is seeded; at that point
    // the residual must be re-examined (NOT auto-cleared) -- if any delta
    // survives a fully-seeded run it is a real code-path bug. The
    // Assert.Equal below stays exact on purpose: do not narrow it to force
    // a pass. See plans/11-phase-k-ingame.md (CI-suite-rot wave).
    private const string SkipReason =
        "Blocked on Phase Y: Friendship 7 (sector 45151 / starbase 73) vendor-NPC " +
        "inventory not seeded -- our 0x0025 ITEM_BASE count is 4 vs retail's 77. " +
        "Residual 0x0009/0x001B deltas can only be diagnosed after the item gap is " +
        "closed; re-enable and re-examine post-import (plans/25). Assertion kept exact.";

    [Fact(Skip = SkipReason)]
    public async Task StationHandshake_AgainstFriendship7Sector45151_OpcodeHistogramMatchesRetailCapture()
    {
        // Non-admin account: the retail capture's player Ace was a
        // normal subscriber, so AdminLevel()=0 and FirstLogin's tiered
        // chat banner at PlayerClass.cpp:397-407 did NOT fire. Default
        // TestAccounts.New uses status=100 (admin) which would trigger
        // the 76B "Dev Chat: /d ..." 0x001D banner at frame [0] and
        // pollute the histogram. Pin matches retail only with this
        // override.
        var account = TestAccounts.New(_server, status: TestAccounts.PlayerTierStatus);
        const int slot = 0;
        // Friendship 7 Recreation Port -- starbase 73, Glenn Commission,
        // Sirius. Retail capture_1.txt's MasterJoin frame 220 sets
        // ToSectorId = 0x0000B05F = 45151 (verified by
        // CaptureReplayTests.MasterJoin_RealCaptureBytes_RoundTripIdentity).
        const int friendship7SectorId = 45151;
        // Terran/Warrior home StartSector -- where the fresh character's
        // first sector login lands no matter what we ask for, because
        // ReInitializeSavedData resets sector_num. Stage 1 lives here.
        const int lunaHomeSectorId = 10151;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(180));

        var login = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(account.Username, account.Password), cts.Token);
        Assert.True(login.Valid, $"login: {login.RawBody.TrimEnd()}");
        Assert.False(string.IsNullOrEmpty(login.Ticket));

        // Stage 1: first sector login at home so ReInitializeSavedData
        // runs and writes the avatar_level_info row. Drain to 0x0005 START
        // (handshake complete), then LogoffRequest + drain LogoffConfirmation
        // for a clean server-side teardown, then dispose to release the
        // sector TCP. We capture nothing from this session -- it exists
        // solely to flip the avatar from "fresh" to "established".
        var homeSession = await SectorHandshake.EstablishAsync(
            _server, login.Ticket!, account.Username, slot, lunaHomeSectorId,
            firstName: "Friend7Pin", shipName: "Friend7Ship", cts.Token);

        try
        {
            await homeSession.Sector.SendAsync(
                Packet.ForOpcode(OpcodeId.Known.LogoffRequest.Value, new byte[8]),
                cts.Token);
            await SectorHandshake.DrainUntilOpcode(
                homeSession.Sector, OpcodeId.Known.LogoffConfirmation.Value, cts.Token);
        }
        finally
        {
            await homeSession.DisposeAsync();
        }

        // Stage 2: reconnect against the now-established avatar and LOGIN
        // to 45151. ReadSavedData takes ReloadSavedData (avatar_level_info
        // exists), which preserves the sector_num HandleLogin sets from
        // the LOGIN packet's ToSectorId. GetSectorManager(45151) returns
        // the Friendship 7 SectorManager and StationLogin emits Friendship
        // 7's GalaxyMap / Greeting / etc.
        var f7Session = Track(await SectorHandshake.ReestablishAsync(
            _server, login.Ticket!, slot, friendship7SectorId, cts.Token));

        var actualHistogram = string.Join("\n", f7Session.HandshakeOpcodes
            .GroupBy(o => o)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key)
            .Select(g => $"{g.Count(),2}x 0x{g.Key:X4}"));

        // Always dump the actual sequence to xUnit output so a failure
        // surfaces both the histogram diff and the ordered frame list
        // for direct inspection against the retail capture's hex-dump.
        _out.WriteLine("Our sector-handshake opcode histogram (Friendship 7, 45151):");
        _out.WriteLine(actualHistogram);
        _out.WriteLine("");
        _out.WriteLine("Expected retail histogram:");
        _out.WriteLine(ExpectedRetailHistogram);
        _out.WriteLine("");
        _out.WriteLine($"Ordered sequence ({f7Session.HandshakeOpcodes.Count} frames):");
        for (int i = 0; i < f7Session.HandshakeOpcodes.Count; i++)
        {
            var frame = f7Session.HandshakeFrames[i];
            _out.WriteLine($"  [{i,3}] 0x{frame.Opcode:X4}  payload={frame.PayloadLength,5}B");
        }

        Assert.Equal(ExpectedRetailHistogram, actualHistogram);
    }
}
