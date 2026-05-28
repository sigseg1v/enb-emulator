// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond preservation project.
// License: LICENSES/enb-emulator

using N7.CliClient.Auth;
using N7.CliClient.Net;
using N7.CliClient.Opcodes;
using Xunit;

namespace N7.CliClient.IntegrationTests.Opcodes;

/// <summary>
/// Wave 88 hardening test (+0 ratchet, 0x0099): pins the 14-byte
/// byte-exact payload shape of every 0x0099 NAVIGATION frame the server
/// emits during a space-sector login handshake stream.
///
/// <para>
/// Backstory. 0x0099 is server-emitted by
/// <c>Player::SendNavigation</c> at
/// <c>server/src/PlayerConnection.cpp:1140-1150</c>:
/// <code>
///   void Player::SendNavigation(int game_id, float signature,
///                               char visited, int nav_type, char is_huge)
///   {
///       Navigation navigation;
///       navigation.GameID = game_id;
///       navigation.Signature = signature;
///       navigation.PlayerHasVisited = visited;
///       navigation.NavType = nav_type;
///       navigation.IsHuge = is_huge;
///       SendOpcode(ENB_OPCODE_0099_NAVIGATION,
///                  (unsigned char *) &amp;navigation, sizeof(navigation));
///   }
/// </code>
/// The emit copies a stack-local <c>Navigation</c> struct and ships
/// exactly <c>sizeof(navigation)</c> bytes — the length is set by the
/// struct definition in <c>common/include/net7/PacketStructures.h:429-436</c>.
/// Verified via standalone g++ -std=c++17 compile of the struct with
/// ATTRIB_PACKED: int32_t GameID (4) + float Signature (4) + char
/// PlayerHasVisited (1) + int32_t NavType (4) + char IsHuge (1) =
/// <b>14 bytes</b>.
/// </para>
///
/// <para>
/// Primary source citation (CLAUDE.md server-integrity rule). Two
/// converging pieces of evidence:
/// </para>
/// <list type="bullet">
///   <item>
///     The retail PACKET_SEQUENCE comment block at
///     <c>server/src/PlayerConnection.cpp:1128-1138</c> documents the
///     wire layout literally — <c>12 00</c> (length=0x12=18 = 4-byte
///     EnbTcpHeader + 14-byte payload), <c>99 00</c> (opcode), then
///     14 bytes of {int32 GameID, float Sig, char visited, int32 Type,
///     char IsHuge}. This is the wire shape the real client was
///     compiled to receive.
///   </item>
///   <item>
///     The proxy at
///     <c>proxy/ClientToSectorServer.cpp:459</c> hardcodes the same
///     literal length:
///     <code>QueueResponse(packet, index, ENB_OPCODE_0099_NAVIGATION,
///                    (unsigned char *) &amp;aux_data, 14);</code>
///     Two independent code paths reaching the same magic number `14`
///     is itself a primary-source statement of the byte-exact invariant.
///   </item>
/// </list>
///
/// <para>
/// Why a hardening test, not a +1 ratchet wave. 0x0099 is already
/// counted by Wave 54
/// (<see cref="SectorHandshakeFanoutTests.HandshakeEmitsPlanetPositionalUpdateAndNavigationOnSpaceSectorLogin"/>) —
/// the passive-observation assertion that the opcode appears in the
/// handshake stream when the LOGIN routes through the space-sector
/// dispatch branch (the per-planet <c>Planet::SendNavigation</c> at
/// <c>server/src/PlanetClass.cpp:238-253</c> conditionally emits when
/// <c>HasNavInfo() &amp;&amp; NavType() &gt; 0 &amp;&amp; AppearsInRadar()</c>).
/// Wave 54's assertion is opcode-presence only; it would still pass if
/// any individual field's wire width silently changed (a LP64 long for
/// GameID or NavType, a char-to-int promotion for PlayerHasVisited or
/// IsHuge, ATTRIB_PACKED removal letting padding creep in between the
/// chars and the surrounding 4-byte fields). Wave 88 adds the byte-exact
/// 14-byte payload-length assertion the presence-only check cannot make,
/// locking the packed-struct layout in place. +0 ratchet because 0x0099
/// is already counted; depth coverage of a regression class Wave 54 was
/// structurally blind to.
/// </para>
///
/// <para>
/// Wire shape and dispatch path. <c>SectorManager::HandleSectorLogin2</c>
/// at <c>server/src/SectorManager.cpp:295-305</c> branches on
/// <c>m_SectorID</c> — sector IDs &gt; 9999 are stations (route to
/// <c>StationLogin2</c> which does NOT call <c>SendAllNavs</c>), and IDs
/// ≤ 9999 are space (route to <c>SectorLogin2</c> at
/// <c>SectorManager.cpp:354</c> which calls
/// <c>m_ObjectMgr->SendAllNavs(player)</c> at line 369). <c>SendAllNavs</c>
/// (<c>ObjectManager.cpp:406-424</c>) iterates the sector's static
/// object list; for each OT_PLANET (or previously-exposed) Object not
/// already in the player's range list, it dispatches the virtual
/// <c>obj-&gt;SendObject(player)</c> which fans out per-type to
/// <c>Planet::SendObject</c>'s chain — including
/// <c>Planet::SendNavigation(Player *)</c> at PlanetClass.cpp:238 which
/// invokes <c>player-&gt;SendNavigation(GameID(), Signature() + 5000.0f,
/// 1, NavType(), IsHuge())</c> only when the nav-info gate
/// (<c>HasNavInfo() &amp;&amp; NavType() &gt; 0 &amp;&amp; AppearsInRadar()</c>)
/// is true. The hardening test therefore reuses Wave 54's 2-stage
/// pattern: stage 1 creates the avatar via the Luna Station (10151)
/// handshake; stage 2 logs the avatar off and reconnects to sector 1015
/// (Luna space) so the per-planet SendObject chain fires and emits one
/// 0x0099 frame per nav-flagged planet in the sector.
/// </para>
///
/// <para>
/// Pattern lineage. FIFTEENTH hardening-pattern wave (Waves 67/71/76/77/
/// 78/79/80/81/82/83/84/85/86/87 → 88). FOURTH space-sector-arm
/// hardening (after Waves 71 on 0x003C CLIENT_TYPE, 87 on 0x0042
/// SERVER_PARAMETERS, and now 88 on 0x0099). Multi-emit Assert.NotEmpty
/// + Assert.All form (same as Wave 84/85/86/87) — Luna's sector roster
/// has multiple nav-flagged planets so multiple 0x0099 frames land per
/// handshake.
/// </para>
///
/// <para>
/// Regression classes this catches.
/// </para>
/// <list type="bullet">
///   <item>
///     <b>Navigation struct field-width revert at
///     <c>common/include/net7/PacketStructures.h:429-436</c>.</b> Any
///     field re-widening — int32_t → long for GameID or NavType (LP64
///     inflation), char → int32_t for PlayerHasVisited or IsHuge, or a
///     structurally identical retype that bumps padding — would shift
///     the packed length away from 14 bytes and the byte-exact assertion
///     fires immediately.
///   </item>
///   <item>
///     <b>ATTRIB_PACKED removal regression at
///     <c>common/include/net7/PacketStructures.h:436</c>.</b> Dropping
///     the packed attribute lets the compiler insert padding between
///     PlayerHasVisited (offset 8) and NavType (offset 9 → would align
///     to 12), and between NavType (offset 12) and IsHuge (offset 16 →
///     would pad to align the next field, plus end-of-struct padding to
///     4-byte align). The unpacked size jumps to 20+ bytes. The 14-byte
///     assertion catches.
///   </item>
///   <item>
///     <b>SendNavigation emit-length regression at
///     <c>server/src/PlayerConnection.cpp:1149</c>.</b> A copy-paste
///     swap of <c>sizeof(navigation)</c> for a hardcoded literal would
///     mismatch the struct length. Length assertion catches.
///   </item>
///   <item>
///     <b>Proxy QueueResponse literal-14 revert at
///     <c>proxy/ClientToSectorServer.cpp:459</c>.</b> The proxy
///     hardcodes `14` as the byte-count for ENB_OPCODE_0099_NAVIGATION;
///     a regression here would silently mis-frame the proxy's auxdata
///     emit. While Wave 88's test runs against the server (not the
///     proxy), the proxy literal is documentation that 14 is the
///     wire-correct length.
///   </item>
///   <item>
///     <b>SendOpcode header-width revert at
///     <c>server/src/PlayerConnection.cpp:127</c>.</b> Would corrupt
///     every inner opcode in the 0x2016 PACKET_SEQUENCE parser; 0x0099
///     wouldn't appear under its correct label at all (so the
///     <c>Assert.NotEmpty</c> filter catches it before the length check
///     fires).
///   </item>
///   <item>
///     <b>Proxy SendClientPacketSequence inner-opcode guard tightening
///     at <c>proxy/UDPProxyToClient_linux.cpp:568</c>.</b> Currently
///     passes 0x0099 (0x0099 &lt; 0x0FFF). A regression to a tighter
///     upper bound would silently drop 0x0099 from the wire — the
///     captured-frame filter returns empty and the
///     <c>Assert.NotEmpty</c> check fires.
///   </item>
///   <item>
///     <b>Planet::SendNavigation gate regression at
///     <c>server/src/PlanetClass.cpp:245</c>.</b> The current gate is
///     <c>HasNavInfo() &amp;&amp; NavType() &gt; 0 &amp;&amp; AppearsInRadar()</c>;
///     a regression that inverts any clause silently drops 0x0099 from
///     the wire for matching-or-non-matching planets.
///     <c>Assert.NotEmpty</c> catches the total-collapse case; per-frame
///     length still applies to any surviving emit.
///   </item>
///   <item>
///     <b>SendAllNavs dispatch regression at
///     <c>server/src/SectorManager.cpp:369</c>.</b> The call is currently
///     unconditional in <c>SectorLogin2</c>; a conditional gate that
///     doesn't fire for sector 1015 would silently lose 0x0099 entirely.
///     <c>Assert.NotEmpty</c> catches.
///   </item>
///   <item>
///     <b>SectorManager dispatch-branch collapse at
///     <c>server/src/SectorManager.cpp:295-305</c>.</b> If
///     <c>HandleSectorLogin2</c> always took the station path (e.g. a
///     regression flipping the <c>m_SectorID &gt; 9999</c> guard),
///     <c>SectorLogin2</c> never fires for sector 1015, no 0x0099 emits,
///     and <c>Assert.NotEmpty</c> catches.
///   </item>
///   <item>
///     <b>DoSectorLoginUntilStartAsync drain-loop payload-length
///     capture regression
///     (<c>tests/integration/CliClient.IntegrationTests/Opcodes/SectorHandshake.cs:194-205</c>).</b>
///     The Wave 68 harness addition that populates
///     <see cref="SectorHandshake.Session.HandshakeFrames"/> with
///     payload-length info on the <see cref="SectorHandshake.ReestablishAsync"/>
///     path. If a future refactor drops the length field or
///     under-counts payload bytes, this test observes wrong (or zero)
///     lengths for every captured 0x0099 frame.
///   </item>
///   <item>
///     <b>Stage 1 → Stage 2 logoff-and-reconnect path regression at
///     <c>SectorHandshake.cs:436-446</c>.</b> A bare TCP disconnect
///     between stages would leave the in-memory Player around long
///     enough that the stage-2 GlobalConnect hits
///     <c>G_ERROR_ACCOUNT_IN_USE</c>
///     (<c>server/src/UDP_Global.cpp:166-170</c>).
///   </item>
/// </list>
///
/// <para>
/// Budget: 120s. Stage 1 handshake ~2s; stage 1 logoff ~1s; stage 2
/// re-handshake ~2s; assertions run synchronously against
/// already-captured state. No additional client stimulus.
/// </para>
/// </summary>
[Collection(ServerCollection.Name)]
public sealed class SectorNavigationHardeningTests
{
    private const int ExpectedNavigationPayloadLength = 14;

    private readonly ServerFixture _server;
    private readonly ClientFixture _client;

    public SectorNavigationHardeningTests(ServerFixture server)
    {
        _server = server;
        _client = new ClientFixture(server);
    }

    [Fact]
    public async Task Navigation_EmittedDuringSpaceSectorHandshake_HasExactly14BytePayload()
    {
        var account = TestAccounts.For();
        const int slot = 0;
        const int stationSectorId = 10151;  // Terran Warrior start: Luna Station
        const int spaceSectorId = 1015;     // Luna (space, sector_type=ST_PLANET)

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120));

        var login = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(account.Username, account.Password), cts.Token);
        Assert.True(login.Valid, $"login: {login.RawBody.TrimEnd()}");
        Assert.False(string.IsNullOrEmpty(login.Ticket));

        // Stage 1: create avatar and complete the StationLogin handshake.
        var stationSession = await SectorHandshake.EstablishAsync(
            _server, login.Ticket!, account.Username, slot, stationSectorId,
            firstName: "NavPin88", shipName: "NavPin88Ship", cts.Token);

        try
        {
            // Cleanly tear down stage 1 with an explicit 0x00B9
            // LOGOFF_REQUEST so the server runs DropPlayerFromGalaxy
            // synchronously (avoids G_ERROR_ACCOUNT_IN_USE in stage 2).
            byte[] logoffPayload = new byte[8];
            await stationSession.Sector.SendAsync(
                Packet.ForOpcode(OpcodeId.Known.LogoffRequest.Value, logoffPayload),
                cts.Token);
            await SectorHandshake.DrainUntilOpcode(
                stationSession.Sector, OpcodeId.Known.LogoffConfirmation.Value, cts.Token);
            await stationSession.DisposeAsync();

            // Stage 2: reconnect (no char create) and LOGIN to space
            // sector 1015. HandleSectorLogin2 dispatches to SectorLogin2
            // (SectorManager.cpp:303), which calls SendAllNavs at
            // SectorManager.cpp:369; the per-planet Planet::SendNavigation
            // chain (PlanetClass.cpp:238-253) fires for each nav-flagged
            // planet in sector 1015 and emits a 14-byte 0x0099 frame.
            await using var spaceSession = await SectorHandshake.ReestablishAsync(
                _server, login.Ticket!, slot, spaceSectorId, cts.Token);

            var navigationFrames = spaceSession.HandshakeFrames
                .Where(f => f.Opcode == OpcodeId.Known.Navigation.Value)
                .ToList();

            Assert.NotEmpty(navigationFrames);
            Assert.All(navigationFrames, f =>
                Assert.Equal(ExpectedNavigationPayloadLength, f.PayloadLength));
        }
        finally
        {
            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try
            {
                await using var cleanupGlobal = await EncryptedTcpConnection.ConnectAsync(
                    _server.GlobalHost, _server.GlobalPort, cleanupCts.Token);
                await SectorHandshake.SendGlobalConnectAsync(
                    cleanupGlobal, login.Ticket!, cleanupCts.Token);
                await SectorHandshake.DrainUntilOpcode(
                    cleanupGlobal, OpcodeId.Known.GlobalAvatarList.Value, cleanupCts.Token);
                await SectorHandshake.DeleteCreatedCharacterAsync(
                    cleanupGlobal, slot, cleanupCts.Token);
            }
            catch { /* best-effort cleanup */ }
        }
    }
}
