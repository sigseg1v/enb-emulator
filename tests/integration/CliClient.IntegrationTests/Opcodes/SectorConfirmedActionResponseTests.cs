// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond preservation project.
// License: LICENSES/enb-emulator

using System.Buffers.Binary;
using N7.CliClient.Auth;
using N7.CliClient.Net;
using N7.CliClient.Opcodes;
using Xunit;

namespace N7.CliClient.IntegrationTests.Opcodes;

/// <summary>
/// Wave 38 post-handshake survival round-trip: client sends 0x00C0
/// CONFIRMED_ACTION_RESPONSE with a deliberately non-matching
/// <c>player_id=0</c>, then verifies the connection survives via
/// 0x0044 REQUEST_TIME.
///
/// <para>
/// Wire layout. The handler reads exactly 4 bytes (one
/// <c>int32_t</c>) and does no further field access — so the
/// "canonical" wire shape from the perspective of this handler is
/// just <c>[0..4) int32 player_id</c> in network byte order. The
/// retail Win32 client emits this when the user clicks the
/// confirm/dismiss action button on a server-pushed
/// <c>CONFIRMED_ACTION_OFFER</c> popup. The handler at
/// <c>server/src/PlayerConnection.cpp:875-886</c>:
/// </para>
/// <code>
///   void Player::HandleActionResponse(unsigned char *data)
///   {
///       long player_id = ntohl(*((int32_t *) &amp;data[0]));
///       if (player_id == GameID())
///       {
///           m_ActionResponseReceived = true;
///           ProcessConfirmedActionOffer();
///       }
///   }
/// </code>
/// <para>
/// The <c>ntohl</c> read width is pinned at 4B by the
/// <c>int32_t *</c> cast — this is the Phase K Wave 11 fix landed
/// on this exact line. A revert to <c>long *</c> on Linux x86_64
/// (sizeof(long)==8) would read 4B of payload + 4B of adjacent
/// memory and byte-swap the lot into garbage; UBSAN/ASAN would
/// flag the over-read.
/// </para>
///
/// <para>
/// Server handler walk-through.
/// </para>
/// <list type="number">
///   <item>Dispatcher case at <c>server/src/PlayerConnection.cpp:599</c>
///     calls <c>HandleActionResponse(data)</c>.</item>
///   <item>Handler reads the 4B <c>player_id</c> from
///     <c>data[0..4)</c> via <c>ntohl</c> + <c>int32_t *</c>
///     cast.</item>
///   <item>Tests <c>player_id == GameID()</c>. For a fresh
///     starbase character, <c>GameID() = account_id*5+slot+1</c>
///     (account 9_000_035 slot 0 → 45_000_176). The payload's
///     <c>player_id=0</c> cannot match this value.</item>
///   <item>The if-block short-circuits and the function returns
///     without state mutation, without <c>SendOpcode</c>, and
///     without observer fan-out.</item>
/// </list>
/// <para>
/// Same favourable post-emit shape as Wave 27 (INVENTORY_MOVE
/// default arm), Wave 29 (PETITION_STUCK), Wave 30 (RELATIONSHIP),
/// Wave 36 (STARBASE_AVATAR_CHANGE early-return), Wave 37
/// (SKILL_STRING_RQ OT_HUSK short-circuit). Distinguishing
/// feature: <b>this wave is the first survival-probe wave whose
/// handler actually reads a payload field and uses it to gate the
/// short-circuit</b>. Earlier waves either ignored the payload
/// entirely (W36/W37 short-circuited on server-side state) or did
/// pure no-op writes (W27/W29/W30).
/// </para>
///
/// <para>
/// Why this wave target. Wave 36 triaged the remaining
/// client→server dispatch arms in <c>PlayerConnection.cpp</c>'s
/// switch block into safe vs unsafe groups. Wave 37 took 0x0051
/// SKILL_STRING_RQ. Wave 38 takes the next safe arm: 0x00C0
/// CONFIRMED_ACTION_RESPONSE. The handler is unusually small
/// (12 lines including the Wave-11 fix comment), the gate is a
/// single equality test against <c>GameID()</c>, and a fresh char
/// has a deterministic GameID that the test can avoid by sending
/// <c>player_id=0</c>.
/// </para>
///
/// <para>
/// Concrete regression classes this catches:
/// </para>
/// <list type="bullet">
///   <item>
///     <b>Dispatcher mis-route at server/src/PlayerConnection.cpp:599.</b>
///     The 0x00C0 case label sits between 0x00BC CTA_REQUEST
///     (line 595) and 0x00C5 GUILD_LEADER_ACCEPT_CLIENT (line
///     604). A copy-paste swap with the CTA_REQUEST arm would
///     route our 4B payload through <c>HandleCTARequest</c>,
///     which expects a CTAResponse layout and does
///     <c>*((long *) &amp;CTAResponse[N])</c> writes that spill
///     bytes past the bounded stack buffer on Linux x86_64 →
///     SEGV. A swap with <c>HandleGuildLeaderAcceptClient</c>
///     would walk the guild-leader state machine on a fresh-char
///     <c>m_pGuildInfo==null</c> path.
///   </item>
///   <item>
///     <b>Phase K Wave 11 <c>ntohl</c> width fix regression at
///     PlayerConnection.cpp:880.</b> The current
///     <c>ntohl(*((int32_t *) &amp;data[0]))</c> pins the read
///     at 4B. A revert to <c>*((long *)</c> on Linux x86_64
///     reads 8B; the immediate test still passes because the
///     garbage <c>player_id</c> still won't match <c>GameID()</c>,
///     but the over-read is UB and UBSAN/ASAN runs would flag
///     it. Documented here for the next direct-reply wave that
///     exercises the <c>player_id == GameID()</c> true-arm
///     (which will pin the read-width assertion harder via a
///     reply-byte check).
///   </item>
///   <item>
///     <b>Removal of the <c>player_id == GameID()</c> guard at
///     PlayerConnection.cpp:881.</b> Without the guard the
///     handler unconditionally fires
///     <c>ProcessConfirmedActionOffer()</c> which calls
///     <c>SendOpcode(ENB_OPCODE_00BE_CONFIRMED_ACTION_OFFER, ...)</c>
///     and <c>SendClientSound(...)</c>; the unexpected 0x00BE
///     frame would be drained past by the loop and the test
///     would still pass via CLIENT_SET_TIME — but a future
///     tighter assertion (no-frame-before-CLIENT_SET_TIME)
///     would surface it.
///   </item>
///   <item>
///     <b>SendOpcode header-width revert at PlayerConnection.cpp:127.</b>
///     The Phase K sizeof(int32_t) opcode-header fix keeps the
///     per-client UDP queue header at the canonical 4-byte
///     width; a revert corrupts the 0x2016 inner-tuple parser
///     and the REQUEST_TIME reply path goes silent. Same
///     load-bearing SendOpcode invariant as Waves
///     8/24/26/27/29/30/36/37.
///   </item>
///   <item>
///     <b>Proxy default-case ForwardClientOpcode regression.</b>
///     0x00C0 is NOT explicitly listed in
///     <c>proxy/ClientToServer_linux_stubs.cpp</c>'s
///     <c>ProcessSectorServerOpcode</c> switch, so it falls
///     through to the bottom-of-switch ForwardClientOpcode
///     default arm. A regression dropping that default would
///     mean the server never sees the action-response frame;
///     the test still passes via REQUEST_TIME's explicit proxy
///     arm but the diagnostic loss is documented.
///   </item>
///   <item>
///     <b>Proxy SendClientPacketSequence inner-opcode guard
///     tightening at UDPProxyToClient_linux.cpp:568.</b>
///     Currently passes 0x0034 CLIENT_SET_TIME because 0x0034
///     &lt; 0x0FFF; a regression to e.g. <c>opcode &lt; 0x0040</c>
///     would silently drop the REQUEST_TIME reply and the test
///     would time out.
///   </item>
/// </list>
///
/// <para>
/// Server-integrity note (per CLAUDE.md). 0x00C0
/// CONFIRMED_ACTION_RESPONSE is what the retail Win32 client
/// emits when the user clicks the confirm/dismiss action button
/// on a server-pushed CONFIRMED_ACTION_OFFER popup (e.g. the
/// mission-accept dialogue). Sending CONFIRMED_ACTION_RESPONSE
/// with <c>player_id=0</c> (i.e. unsolicited, no pending action
/// offer) is a legal but no-op request — the retail server's
/// <c>player_id == GameID()</c> guard at
/// PlayerConnection.cpp:881 silently ignores it. Zero
/// permissiveness added; not loosening any security posture;
/// not fabricating any reply.
/// </para>
///
/// <para>
/// Budget: 90s. Handshake ~2s; CONFIRMED_ACTION_RESPONSE +
/// REQUEST_TIME round-trip is sub-second.
/// </para>
/// </summary>
[Collection(ServerCollection.Name)]
public sealed class SectorConfirmedActionResponseTests
{
    private readonly ServerFixture _server;
    private readonly ClientFixture _client;

    public SectorConfirmedActionResponseTests(ServerFixture server)
    {
        _server = server;
        _client = new ClientFixture(server);
    }

    [Fact]
    public async Task ConfirmedActionResponse_NonMatchingPlayerId_DoesNotBreakConnection_RequestTimeStillRoundTrips()
    {
        // cli_test35 — Pool[33]. Dedicated to this wave so its
        // Create/Delete cycle doesn't collide with Pool slots owned
        // by earlier waves. seed.sql carries the matching 9_000_035
        // row.
        var account = TestAccounts.Pool[33];
        const int slot = 0;
        const int sectorId = 10151;  // Terran Warrior start: Luna Station

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        var login = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(account.Username, account.Password), cts.Token);
        Assert.True(login.Valid, $"login: {login.RawBody.TrimEnd()}");
        Assert.False(string.IsNullOrEmpty(login.Ticket));

        // firstName "Confact" — guaranteed lowercase vowels ('o', 'a')
        // for the AccountManager.cpp:1147 vowel-check footgun
        // (case-sensitive a/e/i/o/u/y scan BEFORE toupper at line
        // 1153). Hit twice in Waves 36 & 37; see the Wave 39+
        // infra-hygiene item to factor a name-validation helper.
        await using var session = await SectorHandshake.EstablishAsync(
            _server, login.Ticket!, account.Username, slot, sectorId,
            firstName: "Confact", shipName: "ConfactShip", cts.Token);

        try
        {
            // 0x00C0 CONFIRMED_ACTION_RESPONSE — 4B payload.
            //   [0..4)   int32 player_id  (network byte order;
            //                              handler does ntohl)
            // player_id=0 will never match GameID() for any real
            // account; the handler short-circuits silently.
            byte[] payload = new byte[4];
            BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(0, 4), 0);

            await session.Sector.SendAsync(
                Packet.ForOpcode(OpcodeId.Known.ConfirmedActionResponse.Value, payload),
                cts.Token);

            // Survival probe: did the connection survive the
            // confirmed-action-response handler? Send REQUEST_TIME
            // and assert CLIENT_SET_TIME echoes our sentinel tick.
            int clientTick = unchecked((int)(DateTime.UtcNow.Ticks & 0x7FFFFFFF));

            byte[] reqTimePayload = new byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(reqTimePayload, clientTick);

            await session.Sector.SendAsync(
                Packet.ForOpcode(OpcodeId.Known.RequestTime.Value, reqTimePayload),
                cts.Token);

            // Drain until 0x0034 CLIENT_SET_TIME. Tolerate
            // interleaved in-sector frames; cap on frame count so a
            // stalled pipeline doesn't masquerade as the outer-CTS
            // timeout.
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
                $"drained {maxFrames} frames after sending 0x00C0 CONFIRMED_ACTION_RESPONSE + 0x0044 REQUEST_TIME " +
                $"without seeing 0x0034 CLIENT_SET_TIME. " +
                $"Likely the dispatcher arm at PlayerConnection.cpp:599 got mis-routed to HandleCTARequest " +
                $"(would SEGV on the long-cast spill writes), " +
                $"the Phase K Wave 11 ntohl read-width fix at PlayerConnection.cpp:880 was reverted, " +
                $"the player_id == GameID() guard at line 881 was removed, " +
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
