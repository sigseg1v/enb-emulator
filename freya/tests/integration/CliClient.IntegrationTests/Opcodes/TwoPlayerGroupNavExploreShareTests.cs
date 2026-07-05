// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya

using System.Buffers.Binary;
using N7.CliClient.Auth;
using N7.CliClient.Net;
using N7.CliClient.Opcodes;
using Xunit;

namespace N7.CliClient.IntegrationTests.Opcodes;

/// <summary>
/// Regression pin for formation/group EXPLORATION SHARING: when a grouped
/// player explores a nav, every in-range formation member must be able to
/// SEE that nav on their own map -- not merely be credited the explore XP
/// for it.
///
/// <para>
/// The bug (internal inconsistency between two code paths that must agree):
/// </para>
/// <list type="bullet">
///   <item>
///     <b>XP + discovery message: shared to the formation.</b>
///     <c>Player::AwardNavExploreXP</c>
///     (<c>server/src/PlayerExperience.cpp:104</c>) ends with
///     <c>PlayerManager::GroupExploreXP</c>
///     (<c>server/src/GroupManager.cpp:339</c>), which loops every group
///     member in the owner's sector within 40k and calls
///     <c>AwardExploreXP(msg, ...)</c> -- so each formation member gets the
///     explore XP AND the "Discovered &lt;nav&gt;" message.
///   </item>
///   <item>
///     <b>The nav OBJECT + map entry: sent to the discoverer only.</b>
///     <c>ObjectManager::CheckNavRanges</c>
///     (<c>server/src/ObjectManager.cpp:613</c>) reveals per-player by
///     proximity: it sets the SINGLE passed <c>Player*</c>'s
///     <c>ExposedNavList</c>/<c>ExploredNavList</c> and calls
///     <c>SendObject(player)</c> / <c>SendNavigation(player)</c> on that one
///     player. It iterates nobody's group.
///   </item>
/// </list>
///
/// <para>
/// Net effect pre-fix: a formation member was told "you discovered
/// Sector Gate To Earth", was given the XP for it, and was never sent the
/// gate object or the 0x0099 NAVIGATION map entry. The nav never appeared
/// on their map, and flying back through the area did nothing (the member's
/// own <c>CheckNavRanges</c> only reveals what THAT member flies within
/// scan range of, and the leader towing the formation past it does not put
/// the member's own proximity check over the line at map-scale distances).
/// Observed live in multibox play: the follower earned group explore XP for
/// a gate the leader reached in formation while the gate stayed off the
/// follower's map.
/// </para>
///
/// <para>
/// The fix (<c>server/src/GroupManager.cpp</c> <c>GroupRevealNav</c>, called
/// from <c>AwardNavExploreXP</c> right after <c>GroupExploreXP</c>): mirror
/// GroupExploreXP's member gating VERBATIM (same sector, within 40k of the
/// owner, active &amp; not incapacitated) and, for each member that does not
/// already have the nav, set its <c>ExposedNavList</c>/<c>ExploredNavList</c>,
/// persist the discovery (<c>SaveDiscoverNav</c>/<c>SaveExploreNav</c>), and
/// send it the same <c>SendObject</c> + <c>SendNavigation</c> the discoverer
/// got. The reveal set is exactly the XP set, so the two can never drift.
/// No-op for a solo (ungrouped) player, so non-grouped play is unchanged.
/// </para>
///
/// <para>
/// CLAUDE.md server-integrity. This is a CORRECTNESS change proceeding on
/// (1) the internal inconsistency above -- the server already asserted the
/// member discovered the nav (XP + message) while withholding the object,
/// which is self-contradictory -- and (2) owner first-hand retail testimony
/// that Earth &amp; Beyond formation flight shared exploration (map reveal),
/// not just XP. It does NOT widen input acceptance, loosen any gate, or
/// relax the security posture: the packets emitted are the SAME 0x0004
/// object create + 0x0099 NAVIGATION the server already sends the
/// discoverer (byte-format already pinned by
/// <see cref="SectorNavigationHardeningTests"/>), only the recipient set is
/// widened to the members that were already credited the XP. Real-client
/// verification is tracked in <c>plans/29-client-verification.md</c>
/// (CV-GROUPNAV).
/// </para>
///
/// <para>
/// Why this needs two grouped players. The reveal fan-out is observable
/// only at a SECOND avatar: member B receives a 0x0099 NAVIGATION for the
/// nav the leader A explored, with <c>PlayerHasVisited == 1</c>, even though
/// B never flew within its own scan range of it. A single-player test
/// cannot witness the fan-out at all.
/// </para>
///
/// <para>
/// <b>BLOCKED by Net7Proxy single-tenancy</b> -- identical reason to
/// <see cref="TwoPlayerChatSenderSpoofTests"/> and
/// <see cref="TwoPlayerStarbaseRoomChangeFanoutTests"/>: the proxy global
/// state (g_ServerMgr-&gt;m_UDPClient, m_MasterConnection, the LOGIN_STAGE
/// auto-ACK path) is set most-recently-wins by every MasterJoin
/// (<c>proxy/ClientToMasterServer.cpp:104</c>), so Player B's handshake
/// clobbers Player A's UDP routing and A times out at login stage 9.
/// Unskipping requires per-session UDPClient demultiplexing in the PROXY
/// (the server is already multi-tenant, as this very fix relies on). The
/// server fix is verified by code inspection + the CV-GROUPNAV real-client
/// check; this test pins the regression and will run as-is once the proxy
/// multiplexes AND the harness gains a way to drive A within scan range of
/// a real nav (today there is no in-test movement/proximity driver, so even
/// with a multiplexing proxy the explore trigger must be added). The test
/// body encodes the intended shape.
/// </para>
/// </summary>
[Collection(ServerCollection.Name)]
public sealed class TwoPlayerGroupNavExploreShareTests : SectorIntegrationTest
{
    public TwoPlayerGroupNavExploreShareTests(ServerFixture server) : base(server) { }

    private const string ProxySingleTenancySkip =
        "BLOCKED by Net7Proxy single-tenancy: Player B's MasterJoin clobbers " +
        "Player A's UDP routing (proxy/ClientToMasterServer.cpp:104), so A times " +
        "out at login stage 9 -- same wall as TwoPlayerChatSenderSpoofTests / " +
        "TwoPlayerStarbaseRoomChangeFanoutTests. Additionally requires an in-test " +
        "driver to move leader A within scan range of a real nav to trigger " +
        "AwardNavExploreXP. Server fix (GroupManager.cpp GroupRevealNav) verified " +
        "by code inspection + plans/29 CV-GROUPNAV. Unskip once the proxy " +
        "multiplexes per session AND a proximity-explore trigger exists.";

    // GroupAction FormUp selector (PlayerManager::GroupAction dispatches on
    // Action-4; FormUp is case 3 -> Action 7). See SectorCtaRequestTests.
    private const int GroupActionFormUp = 7;

    [Fact(Skip = ProxySingleTenancySkip)]
    public async Task LeaderExploresNav_InRangeFormationMember_ReceivesNavigationMapEntry()
    {
        var accountA = TestAccounts.New(_server);  // formation leader / discoverer
        var accountB = TestAccounts.New(_server);  // in-range formation member
        const int slot = 0;
        // A space sector with real navs the leader can fly to (not a starbase).
        const int sectorId = 1071;  // Jupiter.
        const string leaderName = "Avara";   // must contain a vowel (G_ERROR_ONE_VOWEL=4)
        const string memberName = "Bevora";

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(200));

        var loginA = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(accountA.Username, accountA.Password), cts.Token);
        Assert.True(loginA.Valid, $"loginA: {loginA.RawBody.TrimEnd()}");

        var loginB = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(accountB.Username, accountB.Password), cts.Token);
        Assert.True(loginB.Valid, $"loginB: {loginB.RawBody.TrimEnd()}");

        var sessionA = Track(await SectorHandshake.EstablishAsync(
            _server, loginA.Ticket!, accountA.Username, slot, sectorId,
            firstName: leaderName, shipName: "AvaraShip", cts.Token));

        var sessionB = Track(await SectorHandshake.EstablishAsync(
            _server, loginB.Ticket!, accountB.Username, slot, sectorId,
            firstName: memberName, shipName: "BevoraShip", cts.Token));

        Assert.NotEqual(sessionA.GameId, sessionB.GameId);

        // Form the group: leader A issues FormUp targeting member B. (A full
        // invite/accept handshake would go here; FormUp stands in for the
        // group-membership setup the reveal fan-out gates on.)
        byte[] formPayload = new byte[12];
        BinaryPrimitives.WriteInt32LittleEndian(formPayload.AsSpan(0, 4), (int)sessionA.GameId);
        BinaryPrimitives.WriteInt32LittleEndian(formPayload.AsSpan(4, 4), (int)sessionB.GameId);
        BinaryPrimitives.WriteInt32LittleEndian(formPayload.AsSpan(8, 4), GroupActionFormUp);
        await sessionA.Sector.SendAsync(
            Packet.ForOpcode(OpcodeId.Known.CtaRequest.Value, formPayload), cts.Token);

        // Leader A now flies within scan range of a nav so its own
        // CheckNavRanges explores it -> AwardNavExploreXP -> GroupExploreXP
        // (XP to B) -> GroupRevealNav (the fix: object + 0x0099 NAVIGATION to
        // B). Driving A into range needs an in-test movement driver that does
        // not yet exist; see the skip reason.

        // The discriminating assertion: member B receives a 0x0099 NAVIGATION
        // for the nav A explored, flagged PlayerHasVisited == 1, WITHOUT B
        // ever flying within its own scan range of it. Pre-fix B receives XP
        // but no NAVIGATION and this drains to timeout.
        using var drainCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
        drainCts.CancelAfter(TimeSpan.FromSeconds(30));

        while (true)
        {
            var frame = await sessionB.Sector.ReceiveAsync(drainCts.Token);
            Assert.NotNull(frame);
            if (frame!.Header.Opcode != OpcodeId.Known.Navigation.Value)
                continue;

            // 0x0099 NAVIGATION wire (14B packed, pinned by
            // SectorNavigationHardeningTests): int32 GameID; float Signature;
            // uint8 PlayerHasVisited; int32 NavType; uint8 IsHuge. The nav the
            // leader explored is now on the member's map, marked visited by the
            // shared reveal.
            var span = frame.Payload.Span;
            Assert.True(span.Length >= 14, $"NAVIGATION truncated: {span.Length}B");
            byte playerHasVisited = span[8];
            Assert.Equal((byte)1, playerHasVisited);
            return;
        }
    }
}
