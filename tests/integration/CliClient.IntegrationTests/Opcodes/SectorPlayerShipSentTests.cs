// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Buffers.Binary;
using N7.CliClient.Auth;
using N7.CliClient.Net;
using N7.CliClient.Opcodes;
using Xunit;

namespace N7.CliClient.IntegrationTests.Opcodes;

/// <summary>
/// Wave 42 survival probe: client sends 0x3004 PLAYER_SHIP_SENT
/// explicitly on the sector connection AFTER the STAGE handshake
/// completes at Luna Station (sector 10151, starbase). The proxy
/// at <c>proxy/ClientToServer_linux_stubs.cpp:413-449</c> already
/// synthesises one of {0x3008 STARBASE_LOGIN_COMPLETE (sector
/// &gt; 9999) | 0x3004 PLAYER_SHIP_SENT (sector &lt; 9999)} during
/// the in-game START_ACK fan-out; for Luna Station the proxy emits
/// 0x3008 implicitly, NOT 0x3004. This test exercises the 0x3004
/// dispatch path explicitly post-handshake -- a path the retail
/// Win32 client only takes from space sectors, but the dispatcher
/// arm at <c>server/src/PlayerConnection.cpp:625-629</c> doesn't
/// gate on sector type, so the handler runs to completion either
/// way.
///
/// <para>
/// Handler walk (PlayerConnection.cpp:625-629):
/// </para>
/// <code>
///   case ENB_OPCODE_3004_PLAYER_SHIP_SENT:
///       SetNavCommence();
///       FinishLogin(true);
///       break;
/// </code>
///
/// <para>
/// <c>SetNavCommence()</c> is the same one-line inline at
/// <c>PlayerClass.h:1055</c> that Wave 47's 0x3008 probe covers:
/// <c>{ m_NavCommence = true; }</c> -- a write-only field with no
/// observers in the server tree. <c>FinishLogin(bool udp)</c> at
/// <c>PlayerClass.cpp:3878-3923</c> is the post-stage-10
/// finalisation: branches on <c>FromSector()</c> (10151 takes the
/// <c>&gt; 9999</c> arm, finds the station entry point via
/// <c>FindStation(0)</c>, sets entry_point_id), calls
/// <c>SetLoginCamera(0, entry_point_id)</c> (inline setter at
/// <c>PlayerClass.h:574</c>, just writes <c>m_CameraSignal</c> +
/// <c>m_CameraID</c>, no emit), <c>CheckNavs()</c> (read-only nav
/// range walk at <c>PlayerClass.cpp:1722</c>), and three bool
/// setters (<c>SetInSpace(true)</c>, <c>SetWormholed(false)</c>).
/// None of these emit a SendOpcode. The handler is observably a
/// no-op on the wire, like Wave 47's 0x3008 probe.
/// </para>
///
/// <para>
/// Payload format: the handler doesn't read <c>data</c> at all
/// (zero casts, zero offset reads). Matching the proxy's wire
/// emit pattern (<c>sizeof(player_id)</c> where
/// <c>long player_id</c> is 4B on Win32 LP32 but 8B on Linux LP64)
/// we send an 8-byte all-zero payload. A zero-byte payload would
/// also work since the handler ignores the data buffer entirely.
/// </para>
///
/// <para>
/// What this catches:
/// </para>
/// <list type="bullet">
///   <item>
///     <b>Dispatcher mis-route at PlayerConnection.cpp:625.</b>
///     The 0x3004 case label sits between 0x2017
///     RESEND_PACKET_SEQUENCE (line 621 -- walks
///     <c>m_ResendQueue</c> with the wire packet_num) and 0x3008
///     STARBASE_LOGIN_COMPLETE (line 631 -- SetNavCommence
///     alone). A swap with the 0x2017 handler routes our 8B
///     zero payload into <c>ReSendOpcodes</c>, which reads a
///     <c>long packet_num = *((short*) &amp;data[0])</c> at
///     PlayerConnection.cpp:265 (sign-extends a zero short to 0
///     -- queue walk finds no match, silent no-op, REQUEST_TIME
///     still echoes -- this swap survives but is the wrong code
///     arm; a stricter byte-pin would catch the divergence). A
///     swap with the 0x3008 handler would skip the FinishLogin
///     side-effects (SetInSpace/SetWormholed/CheckNavs) but
///     SetNavCommence still runs -- survival probe still passes
///     but the FinishLogin invocation is lost.
///   </item>
///   <item>
///     <b>FinishLogin removal from the 0x3004 handler at
///     PlayerConnection.cpp:627.</b> Currently FinishLogin(true)
///     is called unconditionally. A regression dropping it would
///     leave m_InSpace unset on space-sector logins; the survival
///     probe still passes (the handler is observably a no-op on
///     the wire) but a byte-comparison wave that asserts post-
///     handler player state would catch it.
///   </item>
///   <item>
///     <b>FinishLogin SEGV regression at PlayerClass.cpp:3878.</b>
///     A null deref inside FindStation / CheckNavs / SetInSpace
///     would crash the worker thread and our REQUEST_TIME echo
///     would time out. Currently the call chain is null-safe
///     (om null-check, came_from null-check, sm null-check) but
///     a refactor that loses one of these is caught by the
///     survival probe.
///   </item>
///   <item>
///     <b>SendOpcode header-width revert at
///     PlayerConnection.cpp:127.</b> Phase K sizeof(int32_t)
///     opcode-header fix keeps the per-client UDP queue header
///     at 4B; a revert corrupts the 0x2016 PACKET_SEQUENCE
///     inner-tuple parser and the REQUEST_TIME path silents.
///   </item>
///   <item>
///     <b>Proxy default-case ForwardClientOpcode regression
///     for 0x3004.</b> The proxy at
///     <c>proxy/ClientToServer_linux_stubs.cpp:413</c> has an
///     EXPLICIT special case for 0x3004 (only fires during
///     START_ACK fan-out when SectorID &lt; 9999) but for an
///     in-game 0x3004 from the client side it falls through to
///     the bottom-of-switch ForwardClientOpcode default arm; a
///     regression dropping that default would mean the server
///     never sees this frame.
///   </item>
///   <item>
///     <b>Proxy SendClientPacketSequence inner-opcode guard at
///     proxy/UDPProxyToClient_linux.cpp:568 tightening.</b>
///     Currently passes 0x0034 CLIENT_SET_TIME because
///     0x0034 &lt; 0x0FFF; a tightening to e.g.
///     <c>opcode &lt; 0x0030</c> would drop the survival probe's
///     reply.
///   </item>
/// </list>
///
/// <para>
/// Server-integrity (CLAUDE.md). Primary-source proof: the proxy
/// at <c>proxy/ClientToServer_linux_stubs.cpp:413-449</c> already
/// emits 0x3004 to the server during the in-game START_ACK
/// fan-out for space sectors, with payload
/// <c>sizeof(player_id)</c> bytes where
/// <c>long player_id = m_UDPClient-&gt;PlayerID()</c>. The
/// handler accepts the dispatch unconditionally (no sector-type
/// gate). Sending 0x3004 from a starbase sector is exercising
/// an existing dispatch-table entry the server already accepts;
/// no new permissiveness is added, no security-posture change,
/// no fabricated reply. The handler's existence in the dispatch
/// list is preservation-load-bearing: removing it would convert
/// the proxy's existing emit into a
/// <c>default: LogMessage("Bad opcode")</c> spam line on every
/// space-sector login. Same survival-probe pattern as Wave 47's
/// 0x3008 probe (the paired dispatcher arm).
/// </para>
///
/// <para>
/// State-divergence note. On a starbase character, this dispatch
/// flips <c>m_InSpace</c> from false to true, which is not the
/// state machine the retail client triggers from a starbase
/// (retail emits 0x3008 only). The character is deleted at end-
/// of-test so the divergence is contained per-TestAccounts.New
/// scope; sibling tests each get their own scope.
/// </para>
///
/// <para>
/// Budget: 90s. Handshake ~2s; 0x3004 + REQUEST_TIME round-trip
/// is sub-second.
/// </para>
/// </summary>
[Collection(ServerCollection.Name)]
public sealed class SectorPlayerShipSentTests
{
    private readonly ServerFixture _server;
    private readonly ClientFixture _client;

    public SectorPlayerShipSentTests(ServerFixture server)
    {
        _server = server;
        _client = new ClientFixture(server);
    }

    [Fact]
    public async Task PlayerShipSent_OnFreshChar_DoesNotBreakConnection_RequestTimeStillRoundTrips()
    {
        var account = TestAccounts.New(_server);
        const int slot = 0;
        const int sectorId = 10151;  // Terran Warrior start: Luna Station (> 9999 = starbase)

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        var login = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(account.Username, account.Password), cts.Token);
        Assert.True(login.Valid, $"login: {login.RawBody.TrimEnd()}");
        Assert.False(string.IsNullOrEmpty(login.Ticket));

        // firstName "Eshipia" contains 'e', 'i' for the AccountManager
        // vowel-check footgun (case-sensitive a/e/i/o/u/y BEFORE toupper).
        await using var session = await SectorHandshake.EstablishAsync(
            _server, login.Ticket!, account.Username, slot, sectorId,
            firstName: "Eshipia", shipName: "EshipiaShip", cts.Token);

        try
        {
            // 0x3004 PLAYER_SHIP_SENT -- server handler at
            // server/src/PlayerConnection.cpp:625-629 calls
            // SetNavCommence() (write-only bool) then FinishLogin(true)
            // (state finalisation, no SendOpcode emit). The handler
            // doesn't read `data` at all. Matching the proxy's wire
            // emit pattern (sizeof(long player_id) = 8 on Linux LP64;
            // the proxy fills it but the handler ignores) we send an
            // 8-byte all-zero payload.
            byte[] payload = new byte[8];

            await session.Sector.SendAsync(
                Packet.ForOpcode(OpcodeId.Known.PlayerShipSent.Value, payload),
                cts.Token);

            // Survival probe: did the connection survive the
            // PLAYER_SHIP_SENT handler? Send REQUEST_TIME and assert
            // CLIENT_SET_TIME echoes our sentinel tick.
            int clientTick = unchecked((int)(DateTime.UtcNow.Ticks & 0x7FFFFFFF));

            byte[] reqTimePayload = new byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(reqTimePayload, clientTick);

            await session.Sector.SendAsync(
                Packet.ForOpcode(OpcodeId.Known.RequestTime.Value, reqTimePayload),
                cts.Token);

            // Drain until 0x0034 CLIENT_SET_TIME. Tolerate interleaved
            // in-sector frames (positional updates from observers, plus
            // any frames FinishLogin's downstream state transition may
            // trigger -- the handler itself emits nothing but the state
            // flip may surface later via the broadcast pipeline).
            int framesSeen = 0;
            const int maxFrames = 400;
            while (framesSeen++ < maxFrames)
            {
                var reply = await session.Sector.ReceiveAsync(cts.Token);
                Assert.NotNull(reply);

                if (reply!.Header.Opcode != OpcodeId.Known.ClientSetTime.Value)
                    continue;

                var span = reply.Payload.Span;
                Assert.Equal(12, span.Length);

                int echoedClientSent = BinaryPrimitives.ReadInt32LittleEndian(span[..4]);
                Assert.Equal(clientTick, echoedClientSent);

                return;
            }

            throw new Xunit.Sdk.XunitException(
                $"drained {maxFrames} frames after sending 0x3004 PLAYER_SHIP_SENT + 0x0044 REQUEST_TIME " +
                $"without seeing 0x0034 CLIENT_SET_TIME. " +
                $"Likely the dispatcher arm at PlayerConnection.cpp:625 was removed (server would default-LogMessage 'Bad opcode'), " +
                $"FinishLogin at PlayerClass.cpp:3878 SEGV'd inside FindStation / CheckNavs / SetInSpace, " +
                $"the proxy's bottom-of-switch ForwardClientOpcode default at proxy/ClientToServer_linux_stubs.cpp dropped 0x3004, " +
                $"or the SendOpcode header-width fix at PlayerConnection.cpp:127 was reverted.");
        }
        finally
        {
            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await SectorHandshake.DeleteCreatedCharacterAsync(session.Global, slot, cleanupCts.Token); }
            catch { /* best-effort cleanup */ }
        }
    }
}
