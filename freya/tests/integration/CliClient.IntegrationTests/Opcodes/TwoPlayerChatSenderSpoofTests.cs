// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Buffers.Binary;
using N7.CliClient.Auth;
using N7.CliClient.Net;
using N7.CliClient.Opcodes;
using N7.CliClient.Opcodes.Outbound;
using N7.CliClient.Opcodes.Records;
using Xunit;

namespace N7.CliClient.IntegrationTests.Opcodes;

/// <summary>
/// AJ-4 spoof-defeat pin for 0x0033 CLIENT_CHAT sender attribution.
///
/// <para>
/// The bug (server/src/PlayerConnection.cpp HandleClientChat, Type 2/3/4
/// = guild/local/broadcast): the server resolved the displayed sender of
/// a guild/local/broadcast chat from the wire-controlled
/// <c>chat-&gt;GameID</c>, not from the authenticated connection. Both
/// <c>PlayerManager::BroadcastChat</c> and <c>LocalChat</c> begin with
/// <c>Player *s = GetPlayer(GameID)</c> (PlayerManager.cpp:706 / :740)
/// and attribute the emitted 0x00A5 CLIENT_CHAT_EVENT to <c>s</c>. So an
/// authenticated player could put any avatar's GameID in the packet and
/// have the server broadcast chat <em>as that avatar</em> -- and, for
/// Local, speak from the victim's position, since the 25000-unit range
/// gate (PlayerManager.cpp:761) measures from the resolved sender. The
/// GroupChat branch already (correctly) passed <c>GameID()</c>.
/// </para>
///
/// <para>
/// The fix passes <c>GameID()</c> (the authenticated connection's id) to
/// all three of GuildChat/LocalChat/BroadcastChat, ignoring the wire
/// field. Pure tightening -- the retail client only ever sends its own
/// id here.
/// </para>
///
/// <para>
/// Why this needs two players. The spoof is observable only at a second
/// avatar: when attacker A broadcasts with <c>chat-&gt;GameID</c> set to
/// victim B's id, the server fans the 0x00A5 out to every OTHER player in
/// the resolved sender's sector (<c>p-&gt;GameID() != GameID</c> self-skip,
/// PlayerManager.cpp:724). The two behaviours diverge at B's pipe:
/// </para>
/// <list type="bullet">
///   <item>
///     <b>Fixed.</b> Sender resolves to A. BroadcastChat walks A's sector
///     list, skips A, sends to B. B receives a 0x00A5 whose sender
///     LastName is <em>attacker A's</em> first name.
///   </item>
///   <item>
///     <b>Vulnerable (pre-fix).</b> Sender resolves to B (the spoofed id).
///     BroadcastChat walks B's sector list and self-skips B, so B receives
///     <em>nothing</em>; instead A would receive the message attributed to
///     B. The single-player chat tests can't witness either case.
///   </item>
/// </list>
/// So the discriminating assertion is simply: B receives a 0x00A5
/// CLIENT_CHAT_EVENT whose <see cref="ClientChatEventRecord.ChatEvent.Sender"/>
/// equals attacker A's first name. Pre-fix B would time out with no frame.
///
/// <para>
/// <b>BLOCKED by proxy single-tenancy</b> -- identical reason to
/// <see cref="TwoPlayerStarbaseRoomChangeFanoutTests"/>: the Net7Proxy
/// global state (g_ServerMgr-&gt;m_UDPClient, m_MasterConnection, the
/// LOGIN_STAGE auto-ACK path) is set most-recently-wins by every
/// MasterJoin (proxy/ClientToMasterServer.cpp:104), so Player B's
/// handshake clobbers Player A's UDP routing and A times out at login
/// stage 9. Unskipping requires per-session UDPClient demultiplexing in
/// the PROXY (the server is already multi-tenant). The server fix itself
/// is verified by code inspection and the real-client check tracked in
/// plans/29 (CV-15); this test pins the regression and will run as-is
/// once the proxy multiplexes. Same established precedent as the
/// room-change fan-out test.
/// </para>
/// </summary>
[Collection(ServerCollection.Name)]
public sealed class TwoPlayerChatSenderSpoofTests : SectorIntegrationTest
{
    public TwoPlayerChatSenderSpoofTests(ServerFixture server) : base(server) { }

    private const string ProxySingleTenancySkip =
        "BLOCKED by Net7Proxy single-tenancy: the proxy global state " +
        "(g_ServerMgr->m_UDPClient, m_MasterConnection, LOGIN_STAGE auto-ACK " +
        "path at proxy/UDPProxyToClient_linux.cpp:600-628) is set " +
        "most-recently-wins by every MasterJoin (proxy/ClientToMasterServer.cpp:104). " +
        "Player B's handshake clobbers Player A's UDP routing; A times out at " +
        "login stage 9. Verified 2026-05-29. Unskip requires per-session " +
        "UDPClient demultiplexing in the proxy -- a substantial refactor " +
        "of ServerManager / UDPClient / Connection. The test code shape " +
        "is correct and will work as-is once the proxy multiplexes.";

    [Fact(Skip = ProxySingleTenancySkip)]
    public async Task AttackerBroadcastWithSpoofedSenderId_VictimSeesAttackerName()
    {
        var accountA = TestAccounts.New(_server);  // attacker
        var accountB = TestAccounts.New(_server);  // victim / spoof target
        const int slot = 0;
        const int sectorId = 10151;  // Luna Station: Terran Warrior start, a starbase.
        const string attackerName = "Avara";  // must contain a vowel (G_ERROR_ONE_VOWEL=4)
        const string victimName = "Bevora";
        const string spoofMessage = "spoofed broadcast";

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(200));

        var loginA = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(accountA.Username, accountA.Password), cts.Token);
        Assert.True(loginA.Valid, $"loginA: {loginA.RawBody.TrimEnd()}");

        var loginB = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(accountB.Username, accountB.Password), cts.Token);
        Assert.True(loginB.Valid, $"loginB: {loginB.RawBody.TrimEnd()}");

        var sessionA = Track(await SectorHandshake.EstablishAsync(
            _server, loginA.Ticket!, accountA.Username, slot, sectorId,
            firstName: attackerName, shipName: "AvaraShip", cts.Token));

        var sessionB = Track(await SectorHandshake.EstablishAsync(
            _server, loginB.Ticket!, accountB.Username, slot, sectorId,
            firstName: victimName, shipName: "BevoraShip", cts.Token));

        Assert.NotEqual(sessionA.GameId, sessionB.GameId);

        // Attacker A sends a Broadcast (Type=4) chat but lies about the
        // sender: chat->GameID is set to VICTIM B's GameId. Post-fix the
        // server ignores the wire id and attributes the broadcast to A
        // (its authenticated connection), so B sees a 0x00A5 with
        // LastName == attacker A's first name. Pre-fix the server resolved
        // the sender as B, self-skipped B, and B saw nothing.
        var codec = new ClientChatCodec();
        var spoofedChat = new ClientChatMessage(
            GameId: sessionB.GameId,          // the LIE
            Type: ChatChannel.Broadcast,
            Message: spoofMessage);
        var chatPacket = Packet.ForOpcode(
            OpcodeId.Known.ClientChat.Value, codec.EncodeOutbound(spoofedChat));

        // Login-stage 10 sector-list race (same as the room-change
        // fan-out test): EstablishAsync returns at 0x0005 START before
        // AddPlayerToSectorList fires. Until both players are on the
        // sector bitmap, BroadcastChat's fan-out loop finds no recipient.
        // Re-sending the chat is idempotent for our purposes -- each send
        // re-runs the fan-out; once both are on the list B sees the frame.
        TimeSpan attemptTimeout = TimeSpan.FromSeconds(2);
        const int maxAttempts = 60;
        int attempt;

        for (attempt = 0; attempt < maxAttempts; attempt++)
        {
            await sessionA.Sector.SendAsync(chatPacket, cts.Token);

            using var attemptCts =
                CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
            attemptCts.CancelAfter(attemptTimeout);

            try
            {
                while (true)
                {
                    var reply = await sessionB.Sector.ReceiveAsync(attemptCts.Token);
                    Assert.NotNull(reply);

                    if (reply!.Header.Opcode != OpcodeId.Known.ClientChatEvent.Value)
                        continue;

                    var ev = ClientChatEventRecord.TryExtract(reply.Payload.Span);
                    Assert.NotNull(ev);

                    // Only consider the broadcast we triggered (channel
                    // "Broadcast", our message body); ignore unrelated
                    // status/login chat events that may interleave.
                    if (ev!.Value.Channel != "Broadcast" || ev.Value.Message != spoofMessage)
                        continue;

                    // The spoof-defeat assertion: the sender the server
                    // attributed the broadcast to is the ATTACKER (A), NOT
                    // the spoofed victim id (B). If this is B's name, the
                    // server trusted chat->GameID and the hole is open.
                    Assert.Equal(attackerName, ev.Value.Sender);
                    Assert.NotEqual(victimName, ev.Value.Sender);
                    return;
                }
            }
            catch (OperationCanceledException) when (!cts.IsCancellationRequested)
            {
                // Attempt window elapsed -- either the stage-10 race hasn't
                // resolved for both players or the fan-out raced the pulse.
                // Retry.
            }
        }

        throw new Xunit.Sdk.XunitException(
            $"sent 0x0033 CLIENT_CHAT (Broadcast, spoofed sender id=0x{sessionB.GameId:X8}) " +
            $"{attempt} times (2s attempt window) without victim B seeing a 0x00A5 " +
            $"CLIENT_CHAT_EVENT for the broadcast. " +
            $"Attacker A.GameID=0x{sessionA.GameId:X8}, victim B.GameID=0x{sessionB.GameId:X8}. " +
            $"Pre-fix this is EXACTLY the symptom: the server resolved the sender as the " +
            $"spoofed id B, self-skipped B in BroadcastChat (PlayerManager.cpp:724), and B " +
            $"received nothing. Post-fix B must see the broadcast attributed to A. " +
            $"(Or the stage-10 sector-list race never resolved for both players within budget.)");
    }
}
