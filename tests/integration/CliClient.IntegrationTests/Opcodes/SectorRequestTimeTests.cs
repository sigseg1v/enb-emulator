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
/// Second post-START sector opcode round-trip: client sends 0x0044
/// REQUEST_TIME with its tick; server replies with 0x0034 CLIENT_SET_TIME
/// carrying {ClientSent, ServerReceived, ServerSent} (3 × int32_t = 12B).
///
/// <para>
/// This is the simplest possible in-sector request/reply pair: no player
/// state matters (HandleRequestTime is unconditional —
/// server/src/PlayerConnection.cpp:1619), and the server echoes the
/// client tick back as <c>ClientSent</c>, giving us a positive
/// correlation handle that a wire-size or byte-order bug would
/// immediately blow up.
/// </para>
///
/// <para>
/// Concrete bugs this test detects:
/// </para>
/// <list type="bullet">
///   <item>
///     <b>HandleRequestTime sizeof(long) over-read</b> — Win32 wire
///     payload is 4 bytes (a single <c>long</c> = 4 on Win32), but the
///     unmigrated Linux server read <c>*((long *) data)</c> which is 8
///     bytes. That pulled 4B of payload plus 4B of whatever followed it
///     in the recv buffer. The Wave 9 fix in
///     <c>server/src/PlayerConnection.cpp:1624</c> casts to
///     <c>int32_t*</c>. Without the fix, the echoed ClientSent value
///     varies run-to-run with whatever the recv buffer leaked.
///   </item>
///   <item>
///     <b>ClientSetTime wire-struct migration</b> — the struct was
///     <c>{long ClientSent; long ServerReceived; long ServerSent;}</c>
///     pre-Phase-K Wave 7. On Linux that was 24B vs canonical 12B,
///     which would have shifted every subsequent opcode in a
///     PacketSequence. The migration to int32_t lives in
///     <c>common/include/net7/PacketStructures.h:563</c>; this test
///     would fail on the size check (expected 12B, got 24B) if anyone
///     reverted it.
///   </item>
///   <item>
///     <b>AuxBase::m_Max_Buffer uninit regression</b> — 53 of 57
///     AuxBase subclasses leave <c>m_Max_Buffer</c> uninitialised. On
///     Win32 the heap garbage happened to be large enough that
///     <c>AddData()</c>'s overflow guard passed through; on Linux the
///     garbage is often small, so every <c>AddData()</c> call hits the
///     "Error: Bufferoverflow in Aux!" branch. With 3+ players in the
///     same sector the per-player serialisation flood saturates stdout
///     and stalls the sector tick — REQUEST_TIME never reaches
///     HandleRequestTime, and the test times out. Wave 9 initialises
///     <c>m_Max_Buffer</c> to <c>ULONG_MAX</c> in <c>AuxBase::AuxBase()</c>
///     so non-overriding subclasses inherit a safe default
///     (<c>server/src/AuxClasses/AuxBase.cpp</c>).
///   </item>
///   <item>
///     <b>UDP fan-out + proxy SendClientPacketSequence integrity</b> —
///     same path as <see cref="SectorChatTests"/>: SendOpcode →
///     m_UDPQueue → 0x2016 PACKET_SEQUENCE → proxy unpacking → TCP 3500.
///     A regression in that pipeline would suppress the reply and we'd
///     timeout on the drain.
///   </item>
/// </list>
///
/// <para>
/// Budget: 90s. Handshake ~2s; request/reply itself is sub-second
/// (server tick + UDP queue flush). Wide budget covers stage-ack retry
/// in the login state machine if anything drops.
/// </para>
/// </summary>
[Collection(ServerCollection.Name)]
public sealed class SectorRequestTimeTests
{
    private readonly ServerFixture _server;
    private readonly ClientFixture _client;

    public SectorRequestTimeTests(ServerFixture server)
    {
        _server = server;
        _client = new ClientFixture(server);
    }

    [Fact]
    public async Task RequestTime_RoundTripsClientSentTickAndReturnsServerTimes()
    {
        // cli_test06 — Pool[5]. Owned by this test exclusively so the
        // per-compose Create/Delete cycle doesn't collide with Pool[3]
        // (SectorLogin) or Pool[4] (SectorChat).
        var account = TestAccounts.Pool[5];
        const int slot = 0;
        const int sectorId = 10151;  // Terran Warrior start: Luna Station

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        var login = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(account.Username, account.Password), cts.Token);
        Assert.True(login.Valid, $"login: {login.RawBody.TrimEnd()}");
        Assert.False(string.IsNullOrEmpty(login.Ticket));

        await using var session = await SectorHandshake.EstablishAsync(
            _server, login.Ticket!, account.Username, slot, sectorId,
            firstName: "Tempora", shipName: "TimeShip", cts.Token);

        try
        {
            // Pick a sentinel that is (a) easy to spot in a hex dump,
            // (b) unique per run so a leaked-recv-buffer regression
            // can't accidentally echo a matching value, and (c) fits in
            // an int32_t. Low 31 bits of UTC ticks works.
            int clientTick = unchecked((int)(DateTime.UtcNow.Ticks & 0x7FFFFFFF));

            byte[] payload = new byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(payload, clientTick);

            await session.Sector.SendAsync(
                Packet.ForOpcode(OpcodeId.Known.RequestTime.Value, payload),
                cts.Token);

            // Drain until 0x0034. The server may interleave other
            // post-login fan-out frames; cap on frame count so a
            // stalled pipeline can't masquerade as the outer-CTS
            // timeout.
            int framesSeen = 0;
            const int maxFrames = 200;
            while (framesSeen++ < maxFrames)
            {
                var reply = await session.Sector.ReceiveAsync(cts.Token);
                Assert.NotNull(reply);

                if (reply!.Header.Opcode != OpcodeId.Known.ClientSetTime.Value)
                    continue;

                // 0x0034 wire layout (ClientSetTime struct):
                //   [0..4)  int32  ClientSent      (echoed client tick)
                //   [4..8)  int32  ServerReceived  (server tick at recv)
                //   [8..12) int32  ServerSent      (server tick at send)
                // common/include/net7/PacketStructures.h:563
                var span = reply.Payload.Span;
                Assert.Equal(12, span.Length);

                int echoedClientSent = BinaryPrimitives.ReadInt32LittleEndian(span[..4]);
                int serverReceived   = BinaryPrimitives.ReadInt32LittleEndian(span[4..8]);
                int serverSent       = BinaryPrimitives.ReadInt32LittleEndian(span[8..12]);

                // The single strongest assertion: the server echoed
                // back our exact tick. A sizeof(long) over-read at the
                // server (4B payload + 4B garbage) would put garbage
                // here. A wire-byte-order bug would also fail.
                Assert.Equal(clientTick, echoedClientSent);

                // Server tick is GetNet7TickCount(); both fields are
                // taken at the same moment, so they should be equal
                // OR ServerSent strictly later than ServerReceived.
                // Non-zero proves the server actually wrote them
                // (zeros would mean the struct went out uninitialised).
                Assert.NotEqual(0, serverReceived);
                Assert.NotEqual(0, serverSent);
                Assert.True(serverSent >= serverReceived,
                    $"server_sent={serverSent} < server_received={serverReceived}; clock travelled backwards or fields are swapped.");

                return;
            }

            throw new Xunit.Sdk.XunitException(
                $"drained {maxFrames} frames after sending 0x0044 REQUEST_TIME " +
                $"without seeing 0x0034 CLIENT_SET_TIME. " +
                $"Likely the server's HandleRequestTime didn't fire, or the proxy's " +
                $"SendClientPacketSequence dropped the inner 0x0034 out of the 0x2016 envelope.");
        }
        finally
        {
            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await SectorHandshake.DeleteCreatedCharacterAsync(session.Global, slot, cleanupCts.Token); }
            catch { /* best-effort cleanup */ }
        }
    }
}
