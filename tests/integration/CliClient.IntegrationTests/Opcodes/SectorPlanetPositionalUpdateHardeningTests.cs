// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond preservation project.
// License: LICENSES/enb-emulator

using N7.CliClient.Auth;
using N7.CliClient.Net;
using N7.CliClient.Opcodes;
using Xunit;

namespace N7.CliClient.IntegrationTests.Opcodes;

/// <summary>
/// Wave 89 hardening test (+0 ratchet, 0x003F): pins the 48-byte
/// byte-exact payload shape of every 0x003F PLANET_POSITIONAL_UPDATE
/// frame the server emits during a space-sector login handshake stream.
///
/// <para>
/// Backstory. 0x003F is server-emitted by
/// <c>Player::SendPlanetPositionalUpdate</c> at
/// <c>server/src/PlayerConnection.cpp:1264-1283</c>:
/// <code>
///   void Player::SendPlanetPositionalUpdate(long object_id,
///                                           PositionInformation * position_info)
///   {
///       PlanetPositionalUpdate update;
///       memset(&amp;update, 0, sizeof(update));
///       update.GameID = object_id;
///       update.TimeStamp = GetNet7TickCount();
///       update.Position[0..2] = position_info->Position[0..2];
///       update.OrbitID = position_info->OrbitID;
///       update.OrbitDist / OrbitAngle / OrbitRate = ...;
///       update.RotateAngle / RotateRate / TiltAngle = ...;
///       SendOpcode(ENB_OPCODE_003F_PLANET_POSITIONAL_UPDATE,
///                  (unsigned char *) &amp;update, sizeof(update));
///   }
/// </code>
/// The emit copies a stack-local <c>PlanetPositionalUpdate</c> struct
/// and ships exactly <c>sizeof(update)</c> bytes — the length is set by
/// the struct definition in
/// <c>common/include/net7/PacketStructures.h:883-895</c>. Verified via
/// standalone g++ -std=c++17 compile of the struct with ATTRIB_PACKED:
/// int32_t GameID (4) + uint32_t TimeStamp (4) + float Position[3] (12)
/// + int32_t OrbitID (4) + float OrbitDist (4) + float OrbitAngle (4)
/// + float OrbitRate (4) + float RotateAngle (4) + float RotateRate (4)
/// + float TiltAngle (4) = <b>48 bytes</b>.
/// </para>
///
/// <para>
/// Primary source citation (CLAUDE.md server-integrity rule). The
/// retail PacketStructures.h byte-position comments (lines 885-894) are
/// themselves a primary-source record of the retail wire offsets:
/// <c>this[12] 4 bytes</c> for GameID through <c>this[56] 4 bytes</c>
/// for TiltAngle — adding the 4-byte EnbTcpHeader prefix gives the same
/// 12-byte starting offset the comments reference, and the final 4-byte
/// TiltAngle ends at <c>this[60]</c> = payload bytes [0..48). The
/// <c>sizeof(update)</c> emit at PlayerConnection.cpp:1282 is the only
/// site that produces a 0x003F frame; struct size determines wire size.
/// </para>
///
/// <para>
/// Why a hardening test, not a +1 ratchet wave. 0x003F is already
/// counted by Wave 54
/// (<see cref="SectorHandshakeFanoutTests.HandshakeEmitsPlanetPositionalUpdateAndNavigationOnSpaceSectorLogin"/>) —
/// the passive-observation assertion that the opcode appears in the
/// handshake stream when the LOGIN routes through the space-sector
/// dispatch branch (the per-planet <c>Object::SendObject</c> chain
/// dispatches <c>Planet::SendPosition</c> at
/// <c>server/src/PlanetClass.cpp:226-231</c> which calls
/// <c>player-&gt;SendPlanetPositionalUpdate(GameID(), PosInfo())</c>).
/// Wave 54's assertion is opcode-presence only; it would still pass if
/// any individual field's wire width silently changed (a LP64 long for
/// GameID/OrbitID, a double-precision Position swap, ATTRIB_PACKED
/// removal letting padding creep in between the int32 fields and the
/// float arrays). Wave 89 adds the byte-exact 48-byte payload-length
/// assertion the presence-only check cannot make, locking the
/// packed-struct layout in place. +0 ratchet because 0x003F is already
/// counted; depth coverage of a regression class Wave 54 was
/// structurally blind to.
/// </para>
///
/// <para>
/// Wire shape and dispatch path.
/// <c>SectorManager::HandleSectorLogin2</c> at
/// <c>server/src/SectorManager.cpp:295-305</c> branches on
/// <c>m_SectorID</c> — IDs &gt; 9999 are stations (route to
/// <c>StationLogin2</c> which does NOT call <c>SendAllNavs</c>), and
/// IDs ≤ 9999 are space (route to <c>SectorLogin2</c> at
/// <c>SectorManager.cpp:354</c> which calls
/// <c>m_ObjectMgr->SendAllNavs(player)</c> at line 369).
/// <c>SendAllNavs</c> (<c>ObjectManager.cpp:406-424</c>) iterates the
/// sector's static object list; for each OT_PLANET (or previously-
/// exposed) Object not already in the player's range list, it
/// dispatches the virtual <c>obj-&gt;SendObject(player)</c>. The base
/// <c>Object::SendObject</c> at <c>ObjectClass.cpp</c> calls
/// <c>SendCreateInfo / SendObjectEffects / SendRelationship /
/// SendPosition / SendAuxDataPacket / SendNavigation / OnCreate</c> in
/// order; <c>Planet::SendPosition</c> at PlanetClass.cpp:226-231 is the
/// override that produces the 0x003F emit (Planet::SendNavigation at
/// PlanetClass.cpp:238 produces the 0x0099 NAVIGATION Wave 88
/// hardened). The hardening test therefore reuses Wave 54's 2-stage
/// pattern: stage 1 creates the avatar via the Luna Station (10151)
/// handshake; stage 2 logs the avatar off and reconnects to sector
/// 1015 (Luna space) so the per-planet SendObject chain fires and
/// emits one 0x003F frame per planet in the sector.
/// </para>
///
/// <para>
/// Pattern lineage. SIXTEENTH hardening-pattern wave (Waves 67/71/76/77/
/// 78/79/80/81/82/83/84/85/86/87/88 → 89). FIFTH space-sector-arm
/// hardening (after Waves 71 on 0x003C CLIENT_TYPE, 86 on 0x003E
/// ADVANCED, 87 on 0x0042 SERVER_PARAMETERS, 88 on 0x0099 NAVIGATION).
/// Multi-emit Assert.NotEmpty + Assert.All form (same as Wave 88) —
/// Luna's sector roster has multiple planets so multiple 0x003F frames
/// land per handshake.
/// </para>
///
/// <para>
/// Regression classes this catches.
/// </para>
/// <list type="bullet">
///   <item>
///     <b>PlanetPositionalUpdate struct field-width revert at
///     <c>common/include/net7/PacketStructures.h:883-895</c>.</b> Any
///     field re-widening — int32_t → long for GameID or OrbitID (LP64
///     inflation, +4B per field on Linux), uint32_t → uint64_t for
///     TimeStamp, float → double for any Position/Orbit/Rotate/Tilt
///     field, or a structurally identical retype that bumps padding —
///     would shift the packed length away from 48 bytes and the
///     byte-exact assertion fires immediately.
///   </item>
///   <item>
///     <b>ATTRIB_PACKED removal regression at
///     <c>common/include/net7/PacketStructures.h:895</c>.</b> Dropping
///     the packed attribute lets the compiler insert padding to align
///     fields — though the current field ordering happens to be
///     naturally-aligned (all 4-byte primitives), a regression that
///     adds a non-aligned field (e.g. char or short) without
///     ATTRIB_PACKED would surface immediately as length growth.
///   </item>
///   <item>
///     <b>SendPlanetPositionalUpdate emit-length regression at
///     <c>server/src/PlayerConnection.cpp:1282</c>.</b> A copy-paste
///     swap of <c>sizeof(update)</c> for a hardcoded literal would
///     mismatch the struct length. Length assertion catches.
///   </item>
///   <item>
///     <b>SendOpcode header-width revert at
///     <c>server/src/PlayerConnection.cpp:127</c>.</b> Would corrupt
///     every inner opcode in the 0x2016 PACKET_SEQUENCE parser; 0x003F
///     wouldn't appear under its correct label at all (so the
///     <c>Assert.NotEmpty</c> filter catches it before the length check
///     fires).
///   </item>
///   <item>
///     <b>Proxy SendClientPacketSequence inner-opcode guard tightening
///     at <c>proxy/UDPProxyToClient_linux.cpp:568</c>.</b> Currently
///     passes 0x003F (0x003F &lt; 0x0FFF). A regression to a tighter
///     upper bound would silently drop 0x003F from the wire — the
///     captured-frame filter returns empty and the
///     <c>Assert.NotEmpty</c> check fires.
///   </item>
///   <item>
///     <b>Planet::SendPosition emit-site regression at
///     <c>server/src/PlanetClass.cpp:226-231</c>.</b> If the override is
///     removed or routed to a different positional emit (e.g.
///     SendAdvancedPositionalUpdate which produces 0x003E), 0x003F
///     would vanish from the handshake stream and
///     <c>Assert.NotEmpty</c> catches.
///   </item>
///   <item>
///     <b>SendAllNavs dispatch regression at
///     <c>server/src/SectorManager.cpp:369</c>.</b> The call is
///     currently unconditional in <c>SectorLogin2</c>; a conditional
///     gate that doesn't fire for sector 1015 would silently lose
///     0x003F entirely. <c>Assert.NotEmpty</c> catches.
///   </item>
///   <item>
///     <b>SendAllNavs iterator-skip regression at
///     <c>server/src/ObjectManager.cpp:411-423</c>.</b> The current
///     iterator skips OT_DECO and inactive entries; an inverted
///     OT_PLANET/previously-exposed gate would silently lose 0x003F.
///   </item>
///   <item>
///     <b>SectorManager dispatch-branch collapse at
///     <c>server/src/SectorManager.cpp:295-305</c>.</b> If
///     <c>HandleSectorLogin2</c> always took the station path (e.g. a
///     regression flipping the <c>m_SectorID &gt; 9999</c> guard),
///     <c>SectorLogin2</c> never fires for sector 1015, no 0x003F
///     emits, and <c>Assert.NotEmpty</c> catches.
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
///     lengths for every captured 0x003F frame.
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
public sealed class SectorPlanetPositionalUpdateHardeningTests
{
    private const int ExpectedPlanetPositionalUpdatePayloadLength = 48;

    private readonly ServerFixture _server;
    private readonly ClientFixture _client;

    public SectorPlanetPositionalUpdateHardeningTests(ServerFixture server)
    {
        _server = server;
        _client = new ClientFixture(server);
    }

    [Fact]
    public async Task PlanetPositionalUpdate_EmittedDuringSpaceSectorHandshake_HasExactly48BytePayload()
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
            firstName: "PPU89Pin", shipName: "PPU89PinShip", cts.Token);

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
            // SectorManager.cpp:369; the per-planet Object::SendObject
            // chain dispatches Planet::SendPosition (PlanetClass.cpp:226)
            // which emits a 48-byte 0x003F frame for each planet in the
            // sector.
            await using var spaceSession = await SectorHandshake.ReestablishAsync(
                _server, login.Ticket!, slot, spaceSectorId, cts.Token);

            var planetPositionalFrames = spaceSession.HandshakeFrames
                .Where(f => f.Opcode == OpcodeId.Known.PlanetPositionalUpdate.Value)
                .ToList();

            Assert.NotEmpty(planetPositionalFrames);
            Assert.All(planetPositionalFrames, f =>
                Assert.Equal(ExpectedPlanetPositionalUpdatePayloadLength, f.PayloadLength));
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
