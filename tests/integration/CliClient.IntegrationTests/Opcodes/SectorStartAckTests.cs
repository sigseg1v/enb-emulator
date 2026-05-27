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
/// Wave 10 post-START_ACK survival round-trip: client sends 0x0006
/// START_ACK (the client's "I have processed your START frame" handshake
/// close), and we then verify the connection survives by sending
/// 0x0044 REQUEST_TIME and observing the 0x0034 CLIENT_SET_TIME reply.
///
/// <para>
/// Why this is an indirect (survival) test rather than a direct reply
/// assertion. The original Wave 10 conception sent START_ACK and waited
/// for 0x0092 CAMERA_CONTROL — but that path is genuinely unreachable
/// for a freshly-created character per the real server's logic:
/// </para>
///
/// <list type="bullet">
///   <item>
///     <c>Player::HandleStartAck</c> at
///     <c>server/src/PlayerConnection.cpp:1603</c> only calls
///     <c>SendLoginCamera()</c> when
///     <c>PlayerIndex()-&gt;GetSectorNum() &lt; MAX_SECTOR_ID</c>.
///   </item>
///   <item>
///     <c>MAX_SECTOR_ID</c> is <c>9999</c>
///     (<c>server/src/Net7.h:363</c>). In-space sectors are
///     <c>&lt;9999</c>; starbases are <c>&gt;9999</c>.
///   </item>
///   <item>
///     Every entry in <c>StartSector[]</c>
///     (<c>server/src/StaticData.h:63-74</c>) for all classes is a
///     starbase in the 10151..10551 range. There is no in-space
///     starting sector for a freshly-created character.
///   </item>
/// </list>
///
/// <para>
/// Per CLAUDE.md server-integrity rules ("NEVER make the server accept
/// inputs or behaviour the real server did not"), we cannot move or
/// drop the <c>MAX_SECTOR_ID</c> check or fabricate a CAMERA_CONTROL
/// emit for the starbase branch — that would be making our server emit
/// a packet the retail server explicitly does not. The retail
/// client+server pair really did this: at starbase, after START_ACK,
/// the server stays silent and the client renders the starbase 3D
/// scene from local data. The proxy follows START_ACK with
/// <c>0x3008 STARBASE_LOGIN_COMPLETE</c> which the server consumes
/// silently (only calls <c>SetNavCommence()</c>, no reply — see
/// <c>server/src/PlayerConnection.cpp:632</c>).
/// </para>
///
/// <para>
/// What this test actually catches:
/// </para>
///
/// <list type="bullet">
///   <item>
///     <b>Server crash on HandleStartAck.</b> If anyone introduces a
///     null-deref or assertion in the START_ACK path, the server-side
///     connection tears down and the follow-up REQUEST_TIME never
///     gets a reply — test times out instead of returning. The handler
///     is small but it does call <c>SetActive(true)</c> and a guarded
///     <c>SendLoginCamera()</c> — both are real code paths that could
///     break.
///   </item>
///   <item>
///     <b>Proxy crash on the 0x3008 follow-up.</b> The proxy's
///     <c>ENB_OPCODE_0006_START_ACK</c> branch
///     (<c>proxy/ClientToServer_linux_stubs.cpp:413-449</c>) forwards
///     START_ACK to the server and then synthesises and forwards
///     either <c>0x3008 STARBASE_LOGIN_COMPLETE</c> (sector &gt;9999)
///     or <c>0x3004 PLAYER_SHIP_SENT</c> (sector &lt;9999). The
///     synthesised opcode carries the player_id as its payload via
///     <c>(char *) &amp;player_id</c>; a regression in that codepath
///     would propagate a wrong-sized or wrong-endian player_id to the
///     server's <c>ENB_OPCODE_3008_STARBASE_LOGIN_COMPLETE</c> handler,
///     which dereferences <c>SetNavCommence()</c> on the connection's
///     bound player — wrong player_id would resolve to NULL and segfault
///     the sector thread. Test then times out.
///   </item>
///   <item>
///     <b>Proxy drops the connection after START_ACK.</b> The proxy
///     flips <c>SetLoginComplete(true)</c> on three separate state
///     machines after forwarding START_ACK. A regression that closed
///     the UDP plane on this transition (e.g. mistaken cleanup of
///     m_SectorTCPRequest state — set to <c>false</c> at line 447)
///     would prevent the REQUEST_TIME reply from being routed back to
///     us.
///   </item>
///   <item>
///     <b>Active() side effects.</b> <c>HandleStartAck</c> flips the
///     player from inactive to active. While no opcode in this test
///     directly gates on Active(), a regression that crashed the
///     SectorManager during the inactive→active transition (object
///     visibility re-evaluation, etc.) would manifest as a hung sector
///     thread and a timeout here.
///   </item>
/// </list>
///
/// <para>
/// Server-integrity note (per CLAUDE.md). This test sends exactly what
/// the retail Win32 client sends and asserts nothing the retail server
/// doesn't also do. 0x0006 START_ACK has an empty payload — the
/// server's <c>HandleStartAck(unsigned char *data)</c> takes a
/// <c>data</c> parameter but never dereferences it
/// (<c>server/src/PlayerConnection.cpp:1603</c>). The REQUEST_TIME
/// round-trip we use as the survival probe is itself fully retail-shaped
/// (see <see cref="SectorRequestTimeTests"/> for the Wave 9 direct
/// coverage of that opcode pair and the sizeof(long) regression it
/// catches).
/// </para>
///
/// <para>
/// Why not just delete this and rely on SectorRequestTimeTests.
/// SectorRequestTimeTests doesn't send START_ACK at all — it stops
/// after the handshake's 0x0005 START frame and does its REQUEST_TIME
/// round-trip from there. So the START_ACK code path on both the
/// server and the proxy is currently uncovered by any test. This test
/// closes that gap: it actually fires START_ACK, lets the proxy do
/// its 0x3008 follow-up, lets the server flip Active() and run the
/// MAX_SECTOR_ID guard, and only then does the survival probe.
/// </para>
///
/// <para>
/// Budget: 90s. Handshake ~2s; START_ACK + proxy's 0x3008 + REQUEST_TIME
/// round-trip should be sub-second. Wide budget covers stage-ack retry
/// in the login state machine if anything drops mid-handshake.
/// </para>
/// </summary>
[Collection(ServerCollection.Name)]
public sealed class SectorStartAckTests
{
    private readonly ServerFixture _server;
    private readonly ClientFixture _client;

    public SectorStartAckTests(ServerFixture server)
    {
        _server = server;
        _client = new ClientFixture(server);
    }

    [Fact]
    public async Task StartAck_DoesNotBreakConnection_RequestTimeStillRoundTrips()
    {
        var account = TestAccounts.For();
        const int slot = 0;
        const int sectorId = 10151;  // Terran Warrior start: Luna Station

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        var login = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(account.Username, account.Password), cts.Token);
        Assert.True(login.Valid, $"login: {login.RawBody.TrimEnd()}");
        Assert.False(string.IsNullOrEmpty(login.Ticket));

        await using var session = await SectorHandshake.EstablishAsync(
            _server, login.Ticket!, account.Username, slot, sectorId,
            firstName: "Acker", shipName: "AckShip", cts.Token);

        try
        {
            // START_ACK payload is empty in the retail client — the
            // server's HandleStartAck signature takes `unsigned char *data`
            // but never dereferences it (PlayerConnection.cpp:1603).
            // The server side already knows which player is acking via
            // the connection's bound player id.
            await session.Sector.SendAsync(
                Packet.ForOpcode(OpcodeId.Known.StartAck.Value, ReadOnlyMemory<byte>.Empty),
                cts.Token);

            // Survival probe. Send a REQUEST_TIME with a per-run
            // sentinel tick; the server echoes it back as
            // ClientSetTime.ClientSent. If START_ACK or the proxy's
            // 0x3008 follow-up tore the connection down, this either
            // fails to send or times out on the receive.
            int clientTick = unchecked((int)(DateTime.UtcNow.Ticks & 0x7FFFFFFF));

            byte[] payload = new byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(payload, clientTick);

            await session.Sector.SendAsync(
                Packet.ForOpcode(OpcodeId.Known.RequestTime.Value, payload),
                cts.Token);

            // Drain until 0x0034 CLIENT_SET_TIME. Cap on frame count so
            // a stalled pipeline can't masquerade as the outer-CTS
            // timeout. Post-START_ACK the server may begin streaming
            // in-sector frames (ship updates, etc.) so this loop must
            // tolerate interleaved traffic.
            int framesSeen = 0;
            const int maxFrames = 400;
            while (framesSeen++ < maxFrames)
            {
                var reply = await session.Sector.ReceiveAsync(cts.Token);
                Assert.NotNull(reply);

                if (reply!.Header.Opcode != OpcodeId.Known.ClientSetTime.Value)
                    continue;

                // 0x0034 wire layout (ClientSetTime struct):
                //   [0..4)  int32  ClientSent
                //   [4..8)  int32  ServerReceived
                //   [8..12) int32  ServerSent
                // common/include/net7/PacketStructures.h:563
                var span = reply.Payload.Span;
                Assert.Equal(12, span.Length);

                int echoedClientSent = BinaryPrimitives.ReadInt32LittleEndian(span[..4]);

                // The exact-echo assertion catches a leak in the server
                // recv-buffer reader (the Wave 9 sizeof(long) class of
                // bug) AND proves the connection genuinely round-tripped
                // through to HandleRequestTime — meaning START_ACK and
                // the proxy's 0x3008 follow-up didn't tear anything down.
                Assert.Equal(clientTick, echoedClientSent);

                return;
            }

            throw new Xunit.Sdk.XunitException(
                $"drained {maxFrames} frames after sending 0x0006 START_ACK + 0x0044 REQUEST_TIME " +
                $"without seeing 0x0034 CLIENT_SET_TIME. " +
                $"Likely the server crashed on HandleStartAck, the proxy crashed on the synthesised " +
                $"0x3008 STARBASE_LOGIN_COMPLETE follow-up, or the proxy dropped the UDP plane on the " +
                $"SetLoginComplete(true) transition (ClientToServer_linux_stubs.cpp:440-443).");
        }
        finally
        {
            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await SectorHandshake.DeleteCreatedCharacterAsync(session.Global, slot, cleanupCts.Token); }
            catch { /* best-effort cleanup */ }
        }
    }
}
