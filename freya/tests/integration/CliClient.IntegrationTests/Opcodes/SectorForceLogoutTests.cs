// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya

using N7.CliClient.Auth;
using N7.CliClient.Net;
using N7.CliClient.Opcodes;
using N7.CliClient.Opcodes.Outbound;
using Xunit;

namespace N7.CliClient.IntegrationTests.Opcodes;

/// <summary>
/// Wave 75 direct-stimulus +1 ratchet (re-stimulated 2026-07-01): drive
/// the 0x0003 LOGOFF emit via the GM <c>/resetchar</c> self-kick and
/// assert the retail Win32 4-byte payload length.
///
/// <para>
/// Why this opcode matters. 0x0003 LOGOFF is distinct from 0x00BA
/// LOGOFF_CONFIRMATION (Wave 31): 0x0003 is emitted by
/// <c>Player::ForceLogout</c> at <c>server/src/PlayerConnection.cpp</c>
/// with a 4-byte int32_t GameID payload (Wave 69 server-tightening --
/// was `long` pre-Wave 69, which emitted 8 bytes on LP64 Linux); 0x00BA
/// is the zero-body response to a client-initiated 0x00B9
/// LOGOFF_REQUEST.
/// </para>
///
/// <para>
/// Stimulus history. Wave 75 originally fired ForceLogout via the
/// duplicate-login path (<c>PlayerManager::CheckAccountInUse</c>, called
/// from <c>UDP_Connection::ProcessTicketInfo</c>): a second global
/// connect on the same account force-logged-out the first session. That
/// account-level guard was removed by owner direction on 2026-06-30
/// (multiboxing two DIFFERENT characters of one account is wanted
/// behaviour -- see plans/99-decisions-log.md and plans/29
/// CV-AZ-DUPCHAR), so the duplicate-global-connect stimulus no longer
/// triggers ForceLogout. The surviving same-CHARACTER duplicate check
/// (<c>PlayerManager::CheckForDuplicatePlayers</c> at SetCharacterID
/// time) calls <c>DropPlayerFromGalaxy</c> directly WITHOUT ForceLogout,
/// so it never emits 0x0003 either.
/// </para>
///
/// <para>
/// Remaining ForceLogout call sites are all slash commands in
/// <c>Player::ProcessChat</c>: (1) <c>/kick &lt;name&gt;</c>
/// (<c>Player::HandleKick</c>, AdminLevel &gt;= GM) -- needs a SECOND
/// concurrent sector session to be the victim, which the single-client
/// proxy forbids (see the BLOCKED two-player fan-out stubs); (2)
/// <c>/resetchar</c> (AdminLevel &gt;= GM) -- SELF-targeted:
/// <c>SendVaMessageC(17, "Character %s reset to zero")</c>, then
/// <c>ForceLogout()</c>, then <c>WipeCharacter()</c>. Path (2) needs
/// only one session, so it is the test-tractable stimulus. Test
/// accounts are seeded with status 100 (admin), so the GM gate passes.
/// </para>
///
/// <para>
/// Flow: establish a session at Luna Station 10151, send 0x0033
/// CLIENT_CHAT with body <c>/resetchar</c>, then drain the sector TCP
/// for 0x0003 and assert <c>payload.Length == 4</c>.
/// <c>Player::ForceLogout</c> emits the frame, calls
/// <c>SendPacketCache</c> to flush synchronously, and sleeps 100ms
/// before <c>DropPlayerFromGalaxy</c> tears the player down -- the
/// frame is on the wire before teardown. <c>WipeCharacter</c> queues an
/// asynchronous SAVE_CODE_CHARACTER_PROGRESS_WIPE (progress rows only;
/// the avatar_info row persists), so the cleanup slot-delete still
/// works.
/// </para>
///
/// <para>
/// Why payload.Length == 4 is the load-bearing invariant. Pre-Wave-69
/// the GameID local was declared <c>long</c>; on LP64 Linux that
/// emitted 8 bytes via <c>sizeof(GameIDD)</c>, diverging from the
/// retail Win32 wire shape (LP32 long = 4 bytes). Wave 69's
/// single-token swap (<c>long</c> to <c>int32_t</c>) restored
/// byte-exact agreement; this test pins it.
/// </para>
///
/// <para>
/// Regression classes this catches.
/// </para>
/// <list type="bullet">
///   <item>
///     <b>ForceLogout parameter-type revert.</b> Reverting
///     <c>int32_t GameIDD = GameID()</c> back to <c>long</c>
///     re-inflates <c>sizeof(GameIDD)</c> to 8 on Linux x86_64 →
///     8-byte payload → length assertion fails.
///   </item>
///   <item>
///     <b><c>/resetchar</c> AdminLevel gate widening or arm removal in
///     <c>Player::ProcessChat</c>.</b> Removing the arm (or the GM
///     gate rejecting the seeded admin account) means no 0x0003 --
///     test times out.
///   </item>
///   <item>
///     <b>ForceLogout SendPacketCache removal.</b> Without the
///     explicit flush the 0x0003 sits in the per-Player UDP queue past
///     the subsequent DropPlayerFromGalaxy and tears down with the
///     player -- test times out.
///   </item>
///   <item>
///     <b>ForceLogout usleep(100ms) removal.</b> The delay between
///     SendPacketCache and DropPlayerFromGalaxy gives the proxy time
///     to forward the frame before TCP teardown -- removal makes the
///     test flaky.
///   </item>
///   <item>
///     <b>Proxy SendClientPacketSequence inner-opcode guard
///     tightening at <c>proxy/UDPProxyToClient_linux.cpp</c>.</b>
///     Currently passes 0x0003 (below the 0x0FFF upper bound). A
///     regression to <c>opcode &gt;= 0x0004</c> would silently drop
///     0x0003 from the wire -- test times out.
///   </item>
///   <item>
///     <b>SendOpcode header-width revert at
///     <c>PlayerConnection.cpp:127</c>.</b> Would mis-decode opcodes
///     in the 0x2016 PACKET_SEQUENCE parser -- test times out.
///   </item>
/// </list>
///
/// <para>
/// Server-integrity note (per CLAUDE.md "Server integrity rules").
/// 0x0003 LOGOFF is server-originated; the stimulus is a GM slash
/// command the inherited server already implements, gated on the same
/// AdminLevel &gt;= GM check as every other GM command the suite
/// exercises. No server changes, no widened input acceptance, no
/// relaxed posture.
/// </para>
///
/// <para>
/// Cleanup. The player is dropped server-side by the ForceLogout →
/// DropPlayerFromGalaxy chain, so the avatar slot's Player is freed but
/// the DB row persists. The finally-block opens a fresh global TCP with
/// a NEW auth ticket, drains the avatar list, and deletes the slot.
/// </para>
///
/// <para>
/// Budget: 90s. Handshake ~2s; CHAT → 0x0003 round-trip is sub-second
/// (plus the server's intentional 100ms usleep). The drain loop
/// tolerates interleaved frames (the "Character ... reset to zero"
/// 0x0011 message, periodic broadcasts).
/// </para>
/// </summary>
[Collection(ServerCollection.Name)]
public sealed class SectorForceLogoutTests : SectorIntegrationTest
{
    public SectorForceLogoutTests(ServerFixture server) : base(server) { }

    [RetryFact]
    public async Task ForceLogout_TriggeredByAdminResetchar_EmitsLogoffWithExactly4BytePayload()
    {
        var account = TestAccounts.New(_server);
        const int slot = 0;
        const int sectorId = 10151;  // Luna Station

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        var login = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(account.Username, account.Password), cts.Token);
        Assert.True(login.Valid, $"login: {login.RawBody.TrimEnd()}");

        await using var session = await SectorHandshake.EstablishAsync(
            _server, login.Ticket!, account.Username, slot, sectorId,
            firstName: "ResetKick", shipName: "ResetKickShip", cts.Token);

        try
        {
            // GM self-kick: /resetchar runs SendVaMessageC(17, ...) then
            // ForceLogout() (emits the 4-byte 0x0003 + SendPacketCache +
            // 100ms usleep) then WipeCharacter() (async progress wipe).
            var codec = new ClientChatCodec();
            var chat = new ClientChatMessage(
                GameId: session.GameId,
                Type: ChatChannel.Group,
                Message: "/resetchar");

            await session.Sector.SendAsync(
                Packet.ForOpcode(
                    OpcodeId.Known.ClientChat.Value,
                    codec.EncodeOutbound(chat)),
                cts.Token);

            // Drain inbound on the sector TCP, find the 0x0003 LOGOFF
            // frame, assert byte-exact 4-byte body.
            int framesSeen = 0;
            const int maxFrames = 400;
            while (framesSeen++ < maxFrames)
            {
                var reply = await session.Sector.ReceiveAsync(cts.Token);
                Assert.NotNull(reply);

                if (reply!.Header.Opcode != OpcodeId.Known.Logoff.Value)
                    continue;

                // 0x0003 LOGOFF wire layout (Wave 69 tightening):
                //   [0..4) int32 GameID
                // Total: 4 bytes. Pre-Wave-69 this was sizeof(long) = 8
                // bytes on LP64 Linux -- wire-shape divergence from retail.
                Assert.Equal(4, reply.Payload.Length);
                return;
            }

            throw new Xunit.Sdk.XunitException(
                $"drained {maxFrames} frames after sending 0x0033 CLIENT_CHAT with body " +
                "\"/resetchar\" without seeing 0x0003 LOGOFF. Likely the /resetchar arm's " +
                "AdminLevel >= GM gate rejected the seeded admin account (status 100), the " +
                "slash-prefix dispatch in Player::ProcessChat broke, Player::ForceLogout's " +
                "SendPacketCache or usleep(100ms) was removed (race tears down the session " +
                "before flush), the proxy's SendClientPacketSequence guard tightened to " +
                "exclude 0x0003, or SendOpcode header-width regressed at PlayerConnection.cpp:127.");
        }
        finally
        {
            // Cleanup: the Player has been ForceLogout'd +
            // DropPlayerFromGalaxy'd server-side, so the in-memory player
            // is gone but the avatar_info row persists (WipeCharacter only
            // queues a progress wipe). Open a FRESH global connection with
            // a NEW auth ticket and delete the slot.
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
