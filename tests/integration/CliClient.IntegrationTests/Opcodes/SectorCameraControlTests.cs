// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond preservation project.
// License: LICENSES/enb-emulator

using N7.CliClient.Auth;
using N7.CliClient.Net;
using N7.CliClient.Opcodes;
using Xunit;

namespace N7.CliClient.IntegrationTests.Opcodes;

/// <summary>
/// Wave 103 +1 ratchet test (0x0092 CAMERA_CONTROL). Pins ground truth
/// that the server emits a 0x0092 CAMERA_CONTROL frame with an exactly
/// 8-byte payload after the client sends 0x0006 START_ACK against a
/// SPACE-sector handshake (sector_id ≤ MAX_SECTOR_ID).
///
/// <para>
/// Dispatch path (server-emitted, server-integrity POSITIVE).
/// </para>
/// <list type="bullet">
///   <item>
///     <c>Player::HandleStartAck</c> at
///     <c>server/src/PlayerConnection.cpp:1613-1627</c>: after
///     <c>SetActive(true)</c>, the handler gates on
///     <c>PlayerIndex()-&gt;GetSectorNum() &lt; MAX_SECTOR_ID</c> and
///     only on the SPACE arm calls
///     <c>SendLoginCamera()</c>.
///   </item>
///   <item>
///     <c>MAX_SECTOR_ID</c> = <c>9999</c>
///     (<c>server/src/Net7.h</c>). In-space sectors are
///     <c>≤ 9999</c>; starbases are <c>&gt; 9999</c>.
///   </item>
///   <item>
///     <c>Player::SendLoginCamera</c>
///     (<c>server/src/PlayerClass.cpp:511-531</c>) — inside the
///     <c>Active()</c> guard (which <c>SetActive(true)</c> at
///     PlayerConnection.cpp:1617 satisfies), unconditionally calls
///     <c>SendCameraControl(m_CameraSignal, m_CameraID)</c>. The
///     preceding <c>FindGate(m_FromSectorID)</c>/<c>SendActivateRenderState</c>
///     branch only fires when the avatar arrived through a gate; for a
///     freshly-reestablished session <c>came_from</c> is null and only
///     the unconditional CAMERA_CONTROL emit fires.
///   </item>
///   <item>
///     <c>Player::SendCameraControl</c> at
///     <c>server/src/PlayerConnection.cpp:4516-4524</c> emits
///     <c>SendOpcode(ENB_OPCODE_0092_CAMERA_CONTROL, &amp;data,
///     sizeof(data))</c>. <c>data</c> is a <c>CameraControl</c> struct.
///   </item>
///   <item>
///     <c>CameraControl</c> struct
///     (<c>common/include/net7/PacketStructures.h</c>): two
///     <c>int32_t</c> fields (<c>Message</c>, <c>GameID</c>) with
///     <c>ATTRIB_PACKED</c> = <b>8 bytes</b>. Verified via
///     <c>tr -d '\r' &lt; PacketStructures.h | grep -A 6 "struct
///     CameraControl"</c>.
///   </item>
///   <item>
///     Proxy <c>SendClientPacketSequence</c> inner-opcode guard at
///     <c>proxy/UDPProxyToClient_linux.cpp:568</c> passes 0x0092
///     (0x0092 &lt; 0x0FFF), so the server-emitted frame propagates to
///     the client unchanged.
///   </item>
/// </list>
///
/// <para>
/// Why a SPACE-arm reestablish is required (and why Wave 10
/// <see cref="SectorStartAckTests"/> can't catch this). Every
/// <c>StartSector[]</c> entry
/// (<c>server/src/StaticData.h:63-74</c>) is a starbase in the
/// <c>10151..10551</c> range — so the first login of a fresh character
/// always lands at <c>sector_num &gt; MAX_SECTOR_ID</c> and the
/// HandleStartAck guard at PlayerConnection.cpp:1623 skips
/// SendLoginCamera. The Wave 10 test docstring explicitly identifies
/// this as the "genuinely unreachable" path that needs a SPACE-sector
/// arrival. Wave 103 lights it up by reusing the 2-stage
/// station→space pattern (stage 1 EstablishAsync at 10151 to create
/// the avatar; stage 2 LogoffRequest+drain+ReestablishAsync at 1015 so
/// <c>Player::ReadSavedData</c>'s <c>ReloadSavedData</c> path
/// preserves the LOGIN packet's <c>ToSectorID = 1015</c>, routing
/// through <c>SectorManager::SectorLogin</c> and dropping HandleStartAck
/// into the <c>SendLoginCamera</c> branch).
/// </para>
///
/// <para>
/// Server-integrity citation (CLAUDE.md). Pure passive-observation of a
/// retail-shaped stimulus the real Win32 client also sent (empty-payload
/// 0x0006 START_ACK after the server's 0x0005 START). The server's
/// <c>HandleStartAck(unsigned char *data)</c> never dereferences
/// <c>data</c> (PlayerConnection.cpp:1613-1627), matching the
/// retail-client wire shape. No server change, no widened input
/// acceptance, no fabricated reply — Wave 103 just exercises a dispatch
/// branch the suite hadn't covered.
/// </para>
///
/// <para>
/// Regression classes this catches.
/// </para>
/// <list type="bullet">
///   <item>
///     <b>HandleStartAck MAX_SECTOR_ID guard inversion at
///     <c>server/src/PlayerConnection.cpp:1623</c>.</b> A flip from
///     <c>&lt;</c> to <c>&gt;</c> would silently skip SendLoginCamera
///     for the SPACE arm; no 0x0092 frame would arrive and the drain
///     loop times out.
///   </item>
///   <item>
///     <b>SendLoginCamera Active() guard regression at
///     <c>server/src/PlayerClass.cpp:514</c>.</b> A regression to
///     <c>if (!Active())</c> would skip the unconditional
///     SendCameraControl emit; or a refactor moving SetActive(true)
///     out of HandleStartAck would leave Active() false at the
///     SendLoginCamera call site.
///   </item>
///   <item>
///     <b>SendCameraControl emit-site removal at
///     <c>server/src/PlayerClass.cpp:528</c>.</b> The unconditional
///     call inside the Active() guard is the only path that emits
///     0x0092 during a fresh login; deletion would surface as a drain
///     timeout.
///   </item>
///   <item>
///     <b>CameraControl struct layout regression in
///     <c>common/include/net7/PacketStructures.h</c>.</b> Any
///     int32_t→long widen of <c>Message</c> or <c>GameID</c> would
///     balloon <c>sizeof(CameraControl)</c> past 8 (to 16 on LP64
///     Linux); or ATTRIB_PACKED removal could grow it via alignment
///     padding. The 8-byte length assertion catches both immediately.
///   </item>
///   <item>
///     <b>SendOpcode emit-length regression at
///     <c>server/src/PlayerConnection.cpp:4523</c>.</b> A hardcoded
///     literal replacing <c>sizeof(data)</c> would catch any mismatch
///     against the actual struct shape; the 8-byte assertion pins
///     ground truth.
///   </item>
///   <item>
///     <b>SendOpcode header-width revert at
///     <c>server/src/PlayerConnection.cpp:127</c>.</b> Would corrupt
///     every inner opcode in the 0x2016 PACKET_SEQUENCE parser;
///     0x0092 wouldn't appear under its correct label and the drain
///     loop times out.
///   </item>
///   <item>
///     <b>Proxy SendClientPacketSequence inner-opcode guard
///     tightening at <c>proxy/UDPProxyToClient_linux.cpp:568</c>.</b>
///     Currently passes 0x0092 (0x0092 &lt; 0x0FFF). A tighter upper
///     bound would silently drop the frame; drain loop times out.
///   </item>
///   <item>
///     <b>ReestablishAsync sector_num preservation regression at
///     <c>server/src/PlayerSaves.cpp:289-291</c>.</b> If
///     ReloadSavedData stops preserving the LOGIN packet's
///     ToSectorID, the second handshake would route through
///     StationLogin instead of SectorLogin, sector_num would land
///     &gt;9999 again, and HandleStartAck's guard would skip
///     SendLoginCamera. Drain loop times out.
///   </item>
///   <item>
///     <b>Stage 1 → Stage 2 logoff-and-reconnect path regression at
///     <c>SectorHandshake.cs:436-446</c>.</b> Identical risk to the
///     Wave 71/102 pattern: a bare TCP disconnect between stages would
///     leave the in-memory Player alive, hitting
///     <c>G_ERROR_ACCOUNT_IN_USE</c> on stage 2 GlobalConnect.
///   </item>
/// </list>
///
/// <para>
/// Coverage delta. 0x0092 was not previously counted by any test in
/// the Coverage/TestedOpcodes catalog (Wave 10's
/// <see cref="SectorStartAckTests"/> explicitly documented why it
/// couldn't reach 0x0092). Wave 103 adds it: 104/207 → <b>105/207</b>
/// (50.7%). FIRST in-suite assertion against a SendCameraControl emit.
/// </para>
///
/// <para>
/// Budget: 120s. Stage 1 handshake ~2s; stage 1 logoff ~1s; stage 2
/// re-handshake ~2s; START_ACK + CAMERA_CONTROL round-trip is
/// sub-second. Wide budget covers stage-ack retry in the login state
/// machine if anything drops mid-handshake.
/// </para>
/// </summary>
[Collection(ServerCollection.Name)]
public sealed class SectorCameraControlTests
{
    private const int ExpectedCameraControlPayloadLength = 8;

    private readonly ServerFixture _server;
    private readonly ClientFixture _client;

    public SectorCameraControlTests(ServerFixture server)
    {
        _server = server;
        _client = new ClientFixture(server);
    }

    [Fact]
    public async Task StartAck_AfterSpaceArmHandshake_EmitsCameraControlWithEightBytePayload()
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

        // Stage 1: create avatar via the StationLogin handshake at Luna Station.
        var stationSession = await SectorHandshake.EstablishAsync(
            _server, login.Ticket!, account.Username, slot, stationSectorId,
            firstName: "CamPin103", shipName: "CamPin103Ship", cts.Token);

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
            // sector 1015. HandleSectorLogin dispatches to SectorLogin
            // (SectorManager.cpp:332), the handshake drains to 0x0005
            // START with sector_num=1015.
            await using var spaceSession = await SectorHandshake.ReestablishAsync(
                _server, login.Ticket!, slot, spaceSectorId, cts.Token);

            // START_ACK payload is empty in the retail client — the
            // server's HandleStartAck(unsigned char *data) signature
            // takes data but never dereferences it
            // (PlayerConnection.cpp:1613-1627).
            await spaceSession.Sector.SendAsync(
                Packet.ForOpcode(OpcodeId.Known.StartAck.Value, ReadOnlyMemory<byte>.Empty),
                cts.Token);

            // Drain until 0x0092 CAMERA_CONTROL. Cap frames so a stalled
            // pipeline can't masquerade as the outer-CTS timeout.
            // SendLoginCamera fires after the SetActive(true) flip; the
            // post-START_ACK in-sector frame fan-out is bounded but can
            // be busy, so allow a generous frame budget.
            int framesSeen = 0;
            const int maxFrames = 400;
            while (framesSeen++ < maxFrames)
            {
                var reply = await spaceSession.Sector.ReceiveAsync(cts.Token);
                Assert.NotNull(reply);

                if (reply!.Header.Opcode != OpcodeId.Known.CameraControl.Value)
                    continue;

                Assert.Equal(ExpectedCameraControlPayloadLength, reply.Payload.Length);
                return;
            }

            throw new Xunit.Sdk.XunitException(
                $"drained {maxFrames} frames after sending 0x0006 START_ACK against the SPACE-arm " +
                $"handshake without seeing 0x0092 CAMERA_CONTROL. Likely the HandleStartAck " +
                $"MAX_SECTOR_ID guard inverted, SendLoginCamera's Active() guard tripped (SetActive " +
                $"didn't fire), SendCameraControl was removed, the proxy dropped the frame, or " +
                $"ReestablishAsync's sector_num preservation regressed and the dispatch took the " +
                $"station path again.");
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
