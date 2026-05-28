// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond preservation project.
// License: LICENSES/enb-emulator

using N7.CliClient.Auth;
using N7.CliClient.Net;
using N7.CliClient.Opcodes;
using Xunit;

namespace N7.CliClient.IntegrationTests.Opcodes;

/// <summary>
/// Wave 71 hardening test (+0 ratchet, 0x003C): pins the 4-byte
/// byte-exact payload shape of every 0x003C CLIENT_TYPE frame the
/// server emits during a space-sector login handshake stream.
///
/// <para>
/// Backstory. 0x003C is server-emitted by
/// <c>Player::SendClientType</c> at
/// <c>server/src/PlayerConnection.cpp:1071-1075</c>:
/// <code>
///   void Player::SendClientType(int32_t client_type)
///   {
///       SendOpcode(ENB_OPCODE_003C_CLIENT_TYPE,
///                  (unsigned char *) &amp;client_type, sizeof(client_type));
///   }
/// </code>
/// SendOpcode emits exactly <c>sizeof(client_type)</c> bytes. Before
/// Wave 69 the parameter type was <c>long</c> — on the retail Win32
/// server (LP32) that's 4 bytes; on our Linux server (LP64) it was 8
/// bytes — a silent wire-shape drift away from the retail format the
/// real client was compiled to receive. Wave 69's LP32/LP64 mass
/// tightening sweep narrowed the parameter to <c>int32_t</c> so
/// <c>sizeof(client_type)</c> locks to 4 bytes on every platform.
/// </para>
///
/// <para>
/// Primary source citation (CLAUDE.md server-integrity rule). The kyp
/// linux-port-legacy snapshot has <c>SendClientType(long client_type)</c>
/// with <c>sizeof(client_type)</c> in the SendOpcode call. On Win32
/// (LP32) that evaluated to 4 bytes — the wire shape the retail client
/// was compiled to receive. The Linux build silently inflated to 8
/// bytes because LP64 widens <c>long</c>. Wave 69's single-token swap
/// (<c>long</c> → <c>int32_t</c>) restores byte-exact agreement with
/// the retail wire format. No widened input acceptance, no loosened
/// gating, no fabricated replies — exactly the "tightening" the rule
/// welcomes.
/// </para>
///
/// <para>
/// Why a hardening test, not a +1 ratchet wave. 0x003C is already
/// counted by Wave 52
/// (<see cref="SectorHandshakeFanoutTests.HandshakeEmitsClientTypeAndGalaxyMapOnSpaceSectorLogin"/>) —
/// the passive-observation assertion that the opcode appears in the
/// handshake stream when the LOGIN routes through the space-sector
/// dispatch branch. Wave 52's assertion is opcode-presence only; it
/// would still pass if the payload silently grew to 8 bytes on Linux
/// (the regression Wave 69 fixes). Wave 71 adds the byte-exact 4-byte
/// payload-length assertion the presence-only check cannot make,
/// locking the LP32/LP64 convergence in place. +0 ratchet because
/// 0x003C is already counted; depth coverage of a regression class
/// Wave 52 was structurally blind to.
/// </para>
///
/// <para>
/// Wire shape and dispatch path. <c>SectorManager::HandleSectorLogin</c>
/// at <c>server/src/SectorManager.cpp:324-336</c> branches on
/// <c>m_SectorID</c> — sector IDs &gt; 9999 are stations (route to
/// <c>StationLogin</c> which does NOT call SendClientType), and IDs
/// ≤ 9999 are space (route to <c>SectorLogin</c> at
/// <c>SectorManager.cpp:347</c> which calls
/// <c>player->SendClientType(m_SectorData->sector_type)</c>). The
/// hardening test therefore reuses Wave 52's 2-stage pattern: stage 1
/// creates the avatar via the Luna Station (10151) handshake which
/// writes the <c>avatar_level_info</c> row; stage 2 cleanly logs the
/// avatar off, reconnects, and LOGINs to sector 1015 (Luna space) so
/// the second handshake's <c>ReadSavedData</c> takes the
/// <c>ReloadSavedData</c> path
/// (<c>server/src/PlayerSaves.cpp:289-291</c>) which preserves the
/// space sector_num — routing through <c>SectorLogin</c> and emitting
/// exactly one 0x003C frame per login.
/// </para>
///
/// <para>
/// Regression classes this catches.
/// </para>
/// <list type="bullet">
///   <item>
///     <b>SendClientType parameter-type revert at
///     <c>server/src/PlayerConnection.cpp:1071</c>.</b> The whole point
///     of Wave 69 — reverting <c>int32_t client_type</c> back to
///     <c>long client_type</c> would re-inflate
///     <c>sizeof(client_type)</c> to 8 bytes on Linux x86_64,
///     ballooning every 0x003C frame to an 8-byte payload. The 4-byte
///     length assertion fails immediately.
///   </item>
///   <item>
///     <b>SendClientType header-declaration revert at
///     <c>server/src/PlayerClass.h:979</c>.</b> The declaration must
///     match the definition; a mismatch would either fail to compile
///     or silently bind to a different overload — either way the wire
///     shape drifts. Caught by the length assertion plus compile-time
///     by the C++ ODR.
///   </item>
///   <item>
///     <b>SendOpcode header-width revert at
///     <c>server/src/PlayerConnection.cpp:127</c>.</b> Would corrupt
///     every inner opcode in the 0x2016 PACKET_SEQUENCE parser; 0x003C
///     wouldn't appear under its correct label at all (so the
///     <c>Assert.NotEmpty</c> filter catches it before the length
///     check fires).
///   </item>
///   <item>
///     <b>Proxy SendClientPacketSequence inner-opcode guard tightening
///     at <c>proxy/UDPProxyToClient_linux.cpp:568</c>.</b> Currently
///     passes 0x003C (0x003C &lt; 0x0FFF). A regression to a tighter
///     upper bound would silently drop 0x003C from the wire — the
///     captured-frame filter returns empty and the
///     <c>Assert.NotEmpty</c> check fires.
///   </item>
///   <item>
///     <b>SectorManager dispatch-branch collapse at
///     <c>server/src/SectorManager.cpp:324-336</c>.</b> If
///     <c>HandleSectorLogin</c> always took the station path (e.g. a
///     regression flipping the <c>m_SectorID &gt; 9999</c> guard),
///     <c>SectorLogin</c> never fires for sector 1015, no 0x003C
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
///     lengths for every captured 0x003C frame.
///   </item>
///   <item>
///     <b>Stage 1 → Stage 2 logoff-and-reconnect path regression at
///     <c>SectorHandshake.cs:436-446</c>.</b> A bare TCP disconnect
///     between stages would leave the in-memory Player around long
///     enough that the stage-2 GlobalConnect hits
///     <c>G_ERROR_ACCOUNT_IN_USE</c>
///     (<c>server/src/UDP_Global.cpp:166-170</c>). The explicit 0x00B9
///     LOGOFF_REQUEST plus drain-until-0x00BA pattern is the same one
///     Wave 52's fanout test established; a regression breaking it
///     surfaces here as a stage-2 failure with the
///     <c>G_ERROR_ACCOUNT_IN_USE</c> diagnostic.
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
public sealed class SectorClientTypeHardeningTests
{
    private const int ExpectedClientTypePayloadLength = 4;

    private readonly ServerFixture _server;
    private readonly ClientFixture _client;

    public SectorClientTypeHardeningTests(ServerFixture server)
    {
        _server = server;
        _client = new ClientFixture(server);
    }

    [Fact]
    public async Task ClientType_EmittedDuringSpaceSectorHandshake_HasExactly4BytePayload()
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
            firstName: "CtPin71", shipName: "CtPin71Ship", cts.Token);

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
            // (SectorManager.cpp:332), which emits 0x003C CLIENT_TYPE
            // at line 347. Filter HandshakeFrames for 0x003C and pin
            // every frame to a 4-byte payload.
            await using var spaceSession = await SectorHandshake.ReestablishAsync(
                _server, login.Ticket!, slot, spaceSectorId, cts.Token);

            var clientTypeFrames = spaceSession.HandshakeFrames
                .Where(f => f.Opcode == OpcodeId.Known.ClientType.Value)
                .ToList();

            Assert.NotEmpty(clientTypeFrames);
            Assert.All(clientTypeFrames, f =>
                Assert.Equal(ExpectedClientTypePayloadLength, f.PayloadLength));
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
