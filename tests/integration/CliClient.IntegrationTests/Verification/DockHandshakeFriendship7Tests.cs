// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using N7.CliClient.Auth;
using N7.CliClient.IntegrationTests.Opcodes;
using Xunit;
using Xunit.Abstractions;

namespace N7.CliClient.IntegrationTests.Verification;

/// <summary>
/// Wave 116 (plan Wave 313) station-sector handshake byte-diff test
/// targeting sector 45151 (Friendship 7 Recreation Port, Glenn Commission,
/// Sirius system) -- the SAME destination station as the retail capture in
/// <c>archive/kyp-snapshot/capturedPackets/capture_1.rar</c> (extracted to
/// <c>/tmp/cap/capture_1.txt</c>). Where the existing
/// <see cref="DockHandshakeRetailParityTests"/> pins sector-INVARIANT shape
/// (no 0x00A5 self-broadcast, 0x007F MANU_TAG bits) using any station, this
/// suite targets the EXACT station retail's player landed in so that
/// per-station content (lounge NPCs, station NPC roster, starbase config)
/// is part of the byte-diff.
///
/// <para>
/// Caveats. The character we create is race=0 profession=0
/// (Terran/Warrior, home StartSector=10151 Luna) per the
/// <see cref="SectorHandshake.BuildCreateCharacterPayload"/> defaults.
/// The retail capture's player ("Ace") was already an established
/// avatar parking at Friendship 7 -- they did not first-login there.
/// Our fresh-character LOGIN to sector_id=45151 exercises the
/// <c>SectorManager::StationLogin</c> path (sector_id &gt; 9999) on a
/// player whose saved sector_num is still 10151 from
/// <c>ReInitializeSavedData</c>. If the real server gated first-login
/// on player.sector_num == ToSectorID, this test would have caught
/// our previous over-permissiveness -- but per CLAUDE.md
/// server-integrity rules we accept whatever the real server accepted,
/// no more and no less. A test rejection here is a primary-source
/// finding worth investigating.
/// </para>
///
/// <para>
/// What this test pins. The histogram of opcodes emitted by the sector
/// TCP from the first server frame after the client's LOGIN through and
/// including the terminating 0x0005 START frame. Counts come from a
/// direct grep of capture_1.txt; see the <see cref="ExpectedRetailHistogram"/>
/// constant for the extraction recipe. A divergence is either (a)
/// StationLogin code-path drift in our server vs retail -- the
/// actionable kind -- or (b) starbase 73 / sector 45151 missing or
/// differing seed data in our DB vs the retail server's prod state --
/// the curatorial kind. Both matter; the diff distinguishes them.
/// </para>
/// </summary>
[Collection(ServerCollection.Name)]
public sealed class DockHandshakeFriendship7Tests
{
    private readonly ServerFixture _server;
    private readonly ClientFixture _client;
    private readonly ITestOutputHelper _out;

    public DockHandshakeFriendship7Tests(ServerFixture server, ITestOutputHelper output)
    {
        _server = server;
        _client = new ClientFixture(server);
        _out = output;
    }

    /// <summary>
    /// Retail sector-handshake opcode histogram from
    /// <c>/tmp/cap/capture_1.txt</c>, lines 159..4977 (login-through-START).
    /// Each entry is "{count}x 0x{opcode:X4}". Sorted DESC by count then
    /// ASC by opcode value -- matches the ordering used by the actual-
    /// histogram computation in the test below.
    ///
    /// <para>Extraction recipe:</para>
    /// <code>
    /// awk 'BEGIN{dir=""; done=0} done==1 {next}
    ///      /^Packet #.*Server-&gt;Client/{dir="s2c"; next}
    ///      /^Packet #.*Client-&gt;Server/{dir="c2s"; next}
    ///      dir=="s2c" &amp;&amp; /^ [0-9A-F]{2} 00 +Opcode 0x/{print $4;
    ///         if ($4 == "0x05") done=1}' /tmp/cap/capture_1.txt
    /// | sort | uniq -c | sort -rn
    /// </code>
    ///
    /// <para>
    /// 103 total frames. 0x0025 ItemBase dominates (77x) because
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
         1x 0x0036
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

    [Fact]
    public async Task StationHandshake_AgainstFriendship7Sector45151_OpcodeHistogramMatchesRetailCapture()
    {
        var account = TestAccounts.New(_server);
        const int slot = 0;
        // Friendship 7 Recreation Port -- starbase 73, Glenn Commission,
        // Sirius (sector 4515). Retail capture_1.txt's MasterJoin frame
        // 220 sets ToSectorId = 0x0000B05F = 45151 (verified by
        // CaptureReplayTests.MasterJoin_RealCaptureBytes_RoundTripIdentity).
        const int friendship7SectorId = 45151;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120));

        var login = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(account.Username, account.Password), cts.Token);
        Assert.True(login.Valid, $"login: {login.RawBody.TrimEnd()}");
        Assert.False(string.IsNullOrEmpty(login.Ticket));

        await using var session = await SectorHandshake.EstablishAsync(
            _server, login.Ticket!, account.Username, slot, friendship7SectorId,
            firstName: "Friend7Pin", shipName: "Friend7Ship", cts.Token);

        try
        {
            var actualHistogram = string.Join("\n", session.HandshakeOpcodes
                .GroupBy(o => o)
                .OrderByDescending(g => g.Count())
                .ThenBy(g => g.Key)
                .Select(g => $"{g.Count(),2}x 0x{g.Key:X4}"));

            // Always dump the actual sequence to xUnit output so a failure
            // surfaces both the histogram diff and the ordered frame list
            // for direct inspection against /tmp/cap/capture_1.txt.
            _out.WriteLine("Our sector-handshake opcode histogram (Friendship 7, 45151):");
            _out.WriteLine(actualHistogram);
            _out.WriteLine("");
            _out.WriteLine("Expected retail histogram:");
            _out.WriteLine(ExpectedRetailHistogram);
            _out.WriteLine("");
            _out.WriteLine($"Ordered sequence ({session.HandshakeOpcodes.Count} frames):");
            for (int i = 0; i < session.HandshakeOpcodes.Count; i++)
            {
                var frame = session.HandshakeFrames[i];
                _out.WriteLine($"  [{i,3}] 0x{frame.Opcode:X4}  payload={frame.PayloadLength,5}B");
            }

            Assert.Equal(ExpectedRetailHistogram, actualHistogram);
        }
        finally
        {
            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await SectorHandshake.DeleteCreatedCharacterAsync(session.Global, slot, cleanupCts.Token); }
            catch { /* best-effort cleanup */ }
        }
    }
}
