// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using N7.CliClient.Auth;
using N7.CliClient.Net;
using N7.CliClient.Opcodes;
using Xunit;

namespace N7.CliClient.IntegrationTests.Opcodes;

/// <summary>
/// Wave 75 direct-stimulus +1 ratchet: drive the 0x0003 LOGOFF emit
/// via the duplicate-login (account-in-use) path and assert the
/// retail Win32 4-byte payload length.
///
/// <para>
/// Why this opcode is the +1 ratchet target. 0x0003 LOGOFF has not
/// been covered by any prior wave — Wave 31's existing
/// SectorLogoffRequestTests covers <b>0x00BA LOGOFF_CONFIRMATION</b>
/// (the response to a client-initiated 0x00B9 LOGOFF_REQUEST), not
/// 0x0003. The two opcodes are distinct: 0x0003 LOGOFF is emitted
/// by <c>Player::ForceLogout</c> at
/// <c>server/src/PlayerConnection.cpp:7990</c> with a 4-byte
/// int32_t GameID payload (Wave 69 server-tightening — was `long`
/// pre-Wave 69, would have emitted 8 bytes on LP64 Linux); 0x00BA
/// LOGOFF_CONFIRMATION is emitted by <c>Player::SendLogoffConfirmation</c>
/// at <c>PlayerConnection.cpp:7751</c> with a zero-byte body. Wave 75
/// lands the FIRST 0x0003 coverage and crosses 50% Phase K coverage
/// (103/207 → 104/207 = 50.2%).
/// </para>
///
/// <para>
/// How 0x0003 fires. <c>Player::ForceLogout</c> is called from three
/// server-side paths: (1) GM-only <c>/resetchar</c> command at
/// <c>PlayerConnection.cpp:7101</c>; (2) GM-only <c>/kick</c> command
/// at <c>PlayerConnection.cpp:7961</c>; (3) the duplicate-login path
/// at <c>PlayerManager.cpp:414</c> inside
/// <c>PlayerManager::CheckAccountInUse</c>, which is itself called
/// from <c>UDP_Connection::HandleGlobalConnect</c> at
/// <c>server/src/UDP_Global.cpp:136</c>. Paths (1) and (2) require GM
/// privilege we don't have in the test harness. Path (3) is the only
/// test-tractable stimulus — open a second concurrent global
/// connection with the same account credentials and the server
/// kicks the first session.
/// </para>
///
/// <para>
/// Duplicate-login flow on the server.
/// </para>
/// <list type="number">
///   <item>
///     Test opens Session A: full handshake at Luna Station 10151
///     via <see cref="SectorHandshake.EstablishAsync"/>. By the time
///     EstablishAsync returns, the player has reached
///     <c>SectorManager::StationLogin2:527</c> <c>SetActive(true)</c>
///     (which fires immediately AFTER <c>SendStart</c> emits the
///     0x0005 START terminator the drain loop waits for). So Session
///     A's <c>Player::Active()</c> returns true.
///   </item>
///   <item>
///     Test opens Session B: a fresh global TCP connection. Test
///     re-runs <see cref="AuthLogin"/> against the same account to
///     get a fresh auth ticket (the existing Session A ticket is
///     still notionally valid but a fresh login is the cleanest
///     setup). Then sends <c>0x00C2 GLOBAL_CONNECT</c> with that
///     ticket on Session B's global socket.
///   </item>
///   <item>
///     Server-side <c>UDP_Connection::HandleGlobalConnect</c> at
///     <c>UDP_Global.cpp:136</c> reads <c>account_name</c> from the
///     ticket (strtok on '-') and calls
///     <c>g_PlayerMgr-&gt;CheckAccountInUse(account_name)</c>.
///   </item>
///   <item>
///     <c>PlayerManager::CheckAccountInUse</c> at
///     <c>PlayerManager.cpp:396-419</c> iterates
///     <c>m_GlobalPlayerList</c>, matches Session A's player by
///     <c>strcasecmp(p-&gt;AccountUsername(), username) == 0</c>,
///     sees <c>p-&gt;Active() == true</c> (Session A is settled),
///     calls <c>p-&gt;Dialog("Your account has tried to login
///     twice. Disconnected.", 0)</c>, then <c>p-&gt;ForceLogout()</c>,
///     then <c>DropPlayerFromGalaxy(p)</c>, then returns true.
///   </item>
///   <item>
///     <c>Player::ForceLogout</c> at <c>PlayerConnection.cpp:7969-7998</c>:
///     <code>
///       int32_t GameIDD = GameID();
///       SendOpcode(ENB_OPCODE_0003_LOGOFF, &amp;GameIDD, sizeof(GameIDD));  // 4 bytes
///       SendPacketCache();
///       usleep(100 * 1000);
///       g_ServerMgr-&gt;m_PlayerMgr.LeaveGroup(GroupID(), GameID());
///       g_ServerMgr-&gt;m_PlayerMgr.DropPlayerFromGalaxy(this);
///     </code>
///     The 0x0003 frame goes onto Session A's per-Player UDP queue,
///     SendPacketCache flushes it synchronously, then 100ms sleep
///     gives the client time to read before the player is torn down.
///   </item>
///   <item>
///     Server-side HandleGlobalConnect proceeds: receives
///     <c>account_in_use == true</c>, replies
///     <c>SendGlobalError(G_ERROR_ACCOUNT_IN_USE, ...)</c> on
///     Session B's global socket, returns false.
///   </item>
///   <item>
///     Client-side Session A: drain inbound frames on the sector
///     TCP, look for 0x0003. Assert <c>payload.Length == 4</c>.
///   </item>
/// </list>
///
/// <para>
/// Why payload.Length == 4 is the load-bearing invariant. Pre-Wave-69
/// the GameID local was declared <c>long</c>; on LP64 Linux that
/// emitted 8 bytes via <c>sizeof(GameIDD)</c>, diverging from the
/// retail Win32 wire shape (LP32 long = 4 bytes). Wave 69's
/// single-token swap (<c>long</c> → <c>int32_t</c>) restored
/// byte-exact agreement with the retail wire format. Wave 75 pins
/// that invariant in place with a passive payload-length assertion
/// PLUS the +1 coverage ratchet — first time 0x0003 is exercised
/// end-to-end through the proxy.
/// </para>
///
/// <para>
/// Regression classes this catches.
/// </para>
/// <list type="bullet">
///   <item>
///     <b>ForceLogout parameter-type revert at
///     <c>PlayerConnection.cpp:7988</c>.</b> Reverting
///     <c>int32_t GameIDD = GameID()</c> back to
///     <c>long GameIDD = GameID()</c> re-inflates
///     <c>sizeof(GameIDD)</c> to 8 on Linux x86_64 → 8-byte payload
///     → length assertion fails.
///   </item>
///   <item>
///     <b>CheckAccountInUse <c>Active()</c> guard removal at
///     <c>PlayerManager.cpp:406</c>.</b> The guard ensures we only
///     ForceLogout players that have COMPLETED handshake. A
///     regression that removes the guard would cause ForceLogout to
///     fire mid-handshake before Session A's UDP socket is fully
///     wired — race condition where the 0x0003 might be emitted
///     before the proxy has the routing table set up.
///   </item>
///   <item>
///     <b>CheckAccountInUse case-insensitive match regression at
///     <c>PlayerManager.cpp:404</c>.</b> The
///     <c>strcasecmp</c> comparison ensures the duplicate match
///     fires even if the test happens to use a different case
///     variant. A regression to <c>strcmp</c> would still match
///     here (both sessions use identical casing) but would silently
///     break the duplicate-login protection in production.
///   </item>
///   <item>
///     <b>HandleGlobalConnect dispatch flip at
///     <c>UDP_Global.cpp:136</c>.</b> If the CheckAccountInUse call
///     is removed or moved past the <c>SendAvatarList</c> emit at
///     line 173, Session B would receive the avatar list instead
///     of G_ERROR_ACCOUNT_IN_USE and Session A would never receive
///     0x0003 — test times out.
///   </item>
///   <item>
///     <b>ForceLogout SendPacketCache removal at
///     <c>PlayerConnection.cpp:7991</c>.</b> Without the explicit
///     SendPacketCache call, the 0x0003 sits in the per-Player UDP
///     queue past the subsequent DropPlayerFromGalaxy and tears
///     down with the player — test times out.
///   </item>
///   <item>
///     <b>ForceLogout usleep(100ms) removal at
///     <c>PlayerConnection.cpp:7992</c>.</b> The 100ms delay between
///     SendPacketCache and DropPlayerFromGalaxy gives the proxy
///     enough time to forward the 0x0003 frame to the client before
///     the TCP-level teardown. Removing it creates a race where the
///     forward may not complete — flaky test.
///   </item>
///   <item>
///     <b>Proxy SendClientPacketSequence inner-opcode guard
///     tightening at <c>proxy/UDPProxyToClient_linux.cpp:568</c>.</b>
///     Currently passes 0x0003 (well below the 0x0FFF upper bound).
///     A regression to <c>opcode &gt;= 0x0004</c> would silently
///     drop 0x0003 from the wire — test times out.
///   </item>
///   <item>
///     <b>SendOpcode header-width revert at
///     <c>PlayerConnection.cpp:127</c>.</b> Same load-bearing
///     invariant as every Phase K wave. Would mis-decode opcodes
///     in the 0x2016 PACKET_SEQUENCE parser → 0x0003 wouldn't
///     appear under its correct label → test times out.
///   </item>
/// </list>
///
/// <para>
/// Server-integrity note (per CLAUDE.md "Server integrity rules").
/// 0x0003 LOGOFF is server-originated; Wave 75 adds no new client
/// input shape and no new server response. The duplicate-login
/// stimulus path is exactly what the retail server did — when the
/// same account credentials connect a second time, the retail
/// server kicked the first session with a 0x0003 LOGOFF frame
/// (the retail Win32 client interprets it as
/// "you-have-been-disconnected-by-another-login" and pops the
/// disconnect dialog). The 4-byte body is the retail wire shape.
/// No server changes, no widened input acceptance, no relaxed
/// posture. Pure preservation-grade fidelity — server-integrity
/// POSITIVE.
/// </para>
///
/// <para>
/// Cleanup. Session A's Player is dropped server-side by the
/// ForceLogout → DropPlayerFromGalaxy chain, so the avatar slot is
/// freed but the DB row persists (SaveLogout queues a
/// SAVE_CODE_LOGOUT). The test's finally-block opens a fresh
/// global TCP, runs SendGlobalConnect with a NEW auth ticket
/// (since the duplicate-login stimulus consumed the in-use state
/// — the account is no longer "in use" by the time cleanup
/// runs), drains the avatar list, and deletes the slot.
/// </para>
///
/// <para>
/// Budget: 90s. Stage 1 handshake ~2s; Stage 2 GlobalConnect
/// trigger ~100ms + the server's intentional 100ms usleep + UDP
/// flush. The Session A drain loop tolerates interleaved frames
/// emitted between handshake completion and the 0x0003 trigger
/// (e.g. periodic chat-event broadcasts).
/// </para>
/// </summary>
[Collection(ServerCollection.Name)]
public sealed class SectorForceLogoutTests
{
    private readonly ServerFixture _server;
    private readonly ClientFixture _client;

    public SectorForceLogoutTests(ServerFixture server)
    {
        _server = server;
        _client = new ClientFixture(server);
    }

    [Fact]
    public async Task ForceLogout_TriggeredByDuplicateGlobalConnect_EmitsLogoffWithExactly4BytePayload()
    {
        var account = TestAccounts.For();
        const int slot = 0;
        const int sectorId = 10151;  // Luna Station

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        // -------- Session A: full handshake, settle into Active() state. --------
        var loginA = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(account.Username, account.Password), cts.Token);
        Assert.True(loginA.Valid, $"loginA: {loginA.RawBody.TrimEnd()}");

        await using var sessionA = await SectorHandshake.EstablishAsync(
            _server, loginA.Ticket!, account.Username, slot, sectorId,
            firstName: "DupKick", shipName: "DupKickShip", cts.Token);

        try
        {
            // -------- Session B: open a SECOND global TCP, send GlobalConnect
            // with a FRESH ticket for the same account. The server's
            // UDP_Global::HandleGlobalConnect → CheckAccountInUse →
            // ForceLogout chain will emit a 0x0003 LOGOFF on Session A's
            // sector TCP.
            var loginB = await _client.AuthLogin.LoginAsync(
                new AuthLoginRequest(account.Username, account.Password), cts.Token);
            Assert.True(loginB.Valid, $"loginB: {loginB.RawBody.TrimEnd()}");

            await using (var globalB = await EncryptedTcpConnection.ConnectAsync(
                _server.GlobalHost, _server.GlobalPort, cts.Token))
            {
                await SectorHandshake.SendGlobalConnectAsync(globalB, loginB.Ticket!, cts.Token);
                // Don't drain globalB further — we expect G_ERROR_ACCOUNT_IN_USE
                // but the load-bearing assertion is what fires on Session A.
            }

            // -------- Session A: drain inbound on sector TCP, find the
            // 0x0003 LOGOFF frame, assert byte-exact 4-byte body. The
            // 100ms usleep in ForceLogout between SendPacketCache and
            // DropPlayerFromGalaxy guarantees the frame arrives before
            // the connection is torn down server-side.
            int framesSeen = 0;
            const int maxFrames = 400;
            while (framesSeen++ < maxFrames)
            {
                var reply = await sessionA.Sector.ReceiveAsync(cts.Token);
                Assert.NotNull(reply);

                if (reply!.Header.Opcode != OpcodeId.Known.Logoff.Value)
                    continue;

                // 0x0003 LOGOFF wire layout (Wave 69 tightening):
                //   [0..4) int32 GameID
                // Total: 4 bytes. Pre-Wave-69 this was sizeof(long) = 8
                // bytes on LP64 Linux — wire-shape divergence from retail.
                Assert.Equal(4, reply.Payload.Length);
                return;
            }

            throw new Xunit.Sdk.XunitException(
                $"drained {maxFrames} frames on Session A after the duplicate-login " +
                "stimulus without seeing 0x0003 LOGOFF. Likely PlayerManager::CheckAccountInUse's " +
                "Active() guard or strcasecmp match broke, UDP_Connection::HandleGlobalConnect " +
                "stopped calling CheckAccountInUse, Player::ForceLogout's SendPacketCache or " +
                "usleep(100ms) was removed (race tears down Session A before flush), the proxy's " +
                "SendClientPacketSequence guard at UDPProxyToClient_linux.cpp:568 tightened to " +
                "exclude 0x0003, or SendOpcode header-width regressed at PlayerConnection.cpp:127.");
        }
        finally
        {
            // Cleanup: Session A's Player has been ForceLogout'd +
            // DropPlayerFromGalaxy'd server-side, so the in-memory
            // player is gone but the DB row persists (SaveLogout queued
            // a SAVE_CODE_LOGOUT). Open a FRESH global connection with
            // a NEW auth ticket — by this point the account is no
            // longer "in use" so the connect succeeds.
            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try
            {
                var cleanupLogin = await _client.AuthLogin.LoginAsync(
                    new AuthLoginRequest(account.Username, account.Password), cleanupCts.Token);
                if (cleanupLogin.Valid)
                {
                    await using var cleanupGlobal = await EncryptedTcpConnection.ConnectAsync(
                        _server.GlobalHost, _server.GlobalPort, cleanupCts.Token);
                    await SectorHandshake.SendGlobalConnectAsync(
                        cleanupGlobal, cleanupLogin.Ticket!, cleanupCts.Token);
                    await SectorHandshake.DrainUntilOpcode(
                        cleanupGlobal, OpcodeId.Known.GlobalAvatarList.Value, cleanupCts.Token);
                    await SectorHandshake.DeleteCreatedCharacterAsync(
                        cleanupGlobal, slot, cleanupCts.Token);
                }
            }
            catch { /* best-effort cleanup */ }
        }
    }
}
