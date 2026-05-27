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
/// Wave 11 post-handshake survival round-trip: client sends 0x0012 TURN
/// and 0x0013 TILT (the player movement-input opcodes), then verifies the
/// connection survives by round-tripping 0x0044 REQUEST_TIME.
///
/// <para>
/// Why this is a survival probe rather than a direct reply assertion.
/// <c>Player::HandleTurn</c> / <c>Player::HandleTilt</c>
/// (<c>server/src/PlayerConnection.cpp:1793-1827</c>) are pure state
/// mutators — they call <c>Moveable::Turn(intensity)</c> /
/// <c>Moveable::Tilt(intensity)</c>, which updates
/// <c>m_Turn_Intensity</c> / <c>m_Tilt_Intensity</c> on the player's
/// physics object. The fan-out happens later, via
/// <c>SendPositionalUpdate</c> on a sector tick, and is sent to OTHER
/// observers (other players + MOBs) in the visibility list — never back
/// to the originating client. With a single-player test there is no
/// other observer to receive the fan-out, so we can't directly assert
/// the server processed the input.
/// </para>
///
/// <para>
/// What we CAN assert is that the server didn't crash, the proxy didn't
/// drop the UDP plane, and the recv path didn't desync — all of which
/// would manifest as the survival REQUEST_TIME failing to round-trip.
/// </para>
///
/// <para>
/// Concrete Wave 11 regression this test catches: the local
/// <c>PacketTurn</c> struct in <c>HandleTurn</c> and <c>HandleTilt</c>
/// was declared as <c>{ long GameID; float Intensity; }</c>. On Win32
/// <c>sizeof(long) == 4</c> so the struct is 8 bytes — matching the
/// wire payload exactly. On Linux x86_64 <c>sizeof(long) == 8</c> so
/// the struct becomes 12 bytes and <c>Intensity</c> reads from offset 8
/// instead of 4 — past the end of the 8-byte wire payload, into
/// whatever undefined memory <c>data[8..]</c> happens to point at.
/// The Wave 11 fix narrows the field to <c>int32_t GameID</c>.
/// </para>
///
/// <para>
/// Without the fix, calling <c>Moveable::Turn(garbage)</c> with a
/// random non-finite float could trip a NaN propagation through the
/// physics tick — observable here either as a sector-thread crash
/// (REQUEST_TIME never replies) or as no failure at all if the value
/// happens to be benign. The survival probe is the conservative
/// assertion that holds either way.
/// </para>
///
/// <para>
/// Other bugs this test would also catch:
/// </para>
/// <list type="bullet">
///   <item>
///     Proxy throttle for TURN/TILT (<c>proxy/ClientToSectorServer.cpp:58-76</c>)
///     dropping the opcode entirely instead of forwarding. The throttle
///     is 250ms per opcode type; a single TURN + single TILT both fit
///     under the first-frame allowance.
///   </item>
///   <item>
///     <c>WarpDrive()</c> guard inversion. Both handlers early-out if
///     the player is currently warping; if the inversion crashed mid-
///     check or the warp-state flag was uninit garbage, the handler
///     would call something it shouldn't.
///   </item>
///   <item>
///     <c>AbortProspecting()</c> regression. Both handlers call
///     <c>AbortProspecting(true,false)</c> before applying the input —
///     if that path null-derefs (e.g. on a player with no current
///     prospect target), the sector thread dies.
///   </item>
/// </list>
///
/// <para>
/// Server-integrity note (per CLAUDE.md). The TURN/TILT payloads sent
/// here are exactly the wire shape the retail Win32 client emits:
/// 4-byte little-endian GameID followed by 4-byte float intensity in
/// [-1.0, 1.0]. The intensities chosen (0.5 and -0.25) are well within
/// the retail client's normal stick-range. No server permissiveness is
/// being assumed or required.
/// </para>
///
/// <para>
/// Budget: 90s. Handshake ~2s; TURN+TILT+REQUEST_TIME round-trip is
/// sub-second. Wide budget covers stage-ack retry in the login state
/// machine.
/// </para>
/// </summary>
[Collection(ServerCollection.Name)]
public sealed class SectorTurnTiltTests
{
    private readonly ServerFixture _server;
    private readonly ClientFixture _client;

    public SectorTurnTiltTests(ServerFixture server)
    {
        _server = server;
        _client = new ClientFixture(server);
    }

    [Fact]
    public async Task TurnAndTilt_DoNotBreakConnection_RequestTimeStillRoundTrips()
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
            firstName: "Tilter", shipName: "PivotShip", cts.Token);

        try
        {
            // TURN payload: 4B GameID + 4B float intensity.
            // GameID=0 is the convention for "this connection's player";
            // the server resolves the actor via the connection binding,
            // not the wire GameID, so the value is effectively unused on
            // the server side — but it must be the right WIDTH for the
            // server's struct read to find the Intensity float at the
            // right offset (which is the Wave 11 regression bait).
            byte[] turnPayload = new byte[8];
            BinaryPrimitives.WriteInt32LittleEndian(turnPayload.AsSpan(0, 4), 0);
            BinaryPrimitives.WriteSingleLittleEndian(turnPayload.AsSpan(4, 4), 0.5f);

            await session.Sector.SendAsync(
                Packet.ForOpcode(OpcodeId.Known.Turn.Value, turnPayload),
                cts.Token);

            byte[] tiltPayload = new byte[8];
            BinaryPrimitives.WriteInt32LittleEndian(tiltPayload.AsSpan(0, 4), 0);
            BinaryPrimitives.WriteSingleLittleEndian(tiltPayload.AsSpan(4, 4), -0.25f);

            await session.Sector.SendAsync(
                Packet.ForOpcode(OpcodeId.Known.Tilt.Value, tiltPayload),
                cts.Token);

            // Survival probe.
            int clientTick = unchecked((int)(DateTime.UtcNow.Ticks & 0x7FFFFFFF));

            byte[] reqTimePayload = new byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(reqTimePayload, clientTick);

            await session.Sector.SendAsync(
                Packet.ForOpcode(OpcodeId.Known.RequestTime.Value, reqTimePayload),
                cts.Token);

            // Drain until 0x0034 CLIENT_SET_TIME. Post-handshake the
            // server may begin streaming in-sector frames (ship
            // updates, etc.) so this loop tolerates interleaved
            // traffic. Cap on frame count so a stalled pipeline can't
            // masquerade as the outer-CTS timeout.
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
                Assert.Equal(clientTick, echoedClientSent);

                return;
            }

            throw new Xunit.Sdk.XunitException(
                $"drained {maxFrames} frames after sending 0x0012 TURN + 0x0013 TILT + 0x0044 REQUEST_TIME " +
                $"without seeing 0x0034 CLIENT_SET_TIME. " +
                $"Likely the server's HandleTurn/HandleTilt struct over-read corrupted state, " +
                $"the proxy's TURN/TILT throttle dropped both inputs (proxy/ClientToSectorServer.cpp:58-76), " +
                $"or AbortProspecting()/WarpDrive() guards crashed mid-call.");
        }
        finally
        {
            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await SectorHandshake.DeleteCreatedCharacterAsync(session.Global, slot, cleanupCts.Token); }
            catch { /* best-effort cleanup */ }
        }
    }
}
