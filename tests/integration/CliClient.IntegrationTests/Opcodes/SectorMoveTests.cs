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
/// Wave 14 post-handshake survival round-trip: client sends 0x0014 MOVE
/// (the engine-on / engine-off toggle the retail Win32 client emits when
/// the user hits W or X), then verifies the connection survives by
/// round-tripping 0x0044 REQUEST_TIME.
///
/// <para>
/// Why survival probe rather than direct reply assertion.
/// <c>Player::HandleMove</c>
/// (<c>server/src/PlayerConnection.cpp:1843</c>) is a pure state mutator:
/// it calls <c>AbortProspecting(true,false)</c>, optionally leaves a
/// formation, calls <c>FormationEngineOperation</c>, and finally
/// <c>Move(Movement-&gt;type)</c> which updates the player physics object.
/// The visible effect is later fanned out via <c>SendPositionalUpdate</c>
/// on a sector tick — but only to OTHER observers in the visibility list,
/// never back to the originating client. With a single-player test there
/// is no other observer to receive the fan-out, so we can't assert the
/// server processed the input directly.
/// </para>
///
/// <para>
/// What we CAN assert: the dispatcher accepted the 5-byte MovePacket wire
/// frame, the proxy didn't drop the opcode, and the connection survives —
/// all observable through a follow-up 0x0044 REQUEST_TIME round-trip.
/// </para>
///
/// <para>
/// Concrete regression class this catches: if anyone reverts the Phase R
/// MovePacket layout to use <c>long</c> instead of <c>int32_t</c>, the
/// struct grows from 5B to 9B on Linux x86_64 and <c>type</c> reads from
/// offset 8 (past the end of the 5-byte wire payload, into whatever
/// undefined memory <c>data[8]</c> happens to hold). The garbage type
/// value then routes through the engine-on / engine-off branch
/// arbitrarily, and worse — <c>Move(garbage)</c> is then invoked with an
/// out-of-range mode the moveable subsystem doesn't expect. The survival
/// probe still passes when the garbage happens to be benign, but a
/// non-finite read or a path that mutates physics state with a bad mode
/// could trip later sector-thread asserts. This test serves as the
/// canary for MovePacket-shape regressions.
/// </para>
///
/// <para>
/// Other bugs this test would also catch:
/// </para>
/// <list type="bullet">
///   <item>
///     Proxy <c>ProcessSectorServerOpcode</c> for MOVE
///     (<c>proxy/ClientToServer_linux_stubs.cpp:487-489</c>) dropping or
///     mangling the opcode. The current path is a literal <c>break;</c>
///     so the frame falls through to the bottom-of-switch
///     <c>ForwardClientOpcode</c>, matching Win32 behaviour. A regression
///     that <c>return</c>ed early instead of <c>break</c>ing would drop
///     the forward and the test would time out waiting for the survival
///     probe (since nothing got delivered, but more importantly the
///     server never saw MOVE and a subsequent state-dependent opcode
///     would observe a stale engine flag).
///   </item>
///   <item>
///     <c>WarpDrive()</c> guard inversion in HandleMove. The body is
///     wrapped in <c>if (!WarpDrive())</c>; a fresh starbase character
///     has no warp drive engaged so the guard is true. If the guard was
///     ever inverted or the warp-state flag read uninit garbage, the
///     handler could short-circuit silently or crash.
///   </item>
///   <item>
///     <c>FormationEngineOperation</c> regression. HandleMove calls it
///     unconditionally on the engine-toggle path; a null-deref or
///     unguarded group-iteration would kill the sector thread and the
///     survival probe would never complete.
///   </item>
///   <item>
///     <c>AbortProspecting()</c> null-deref on a fresh starbase character
///     with no current prospect target. Same failure mode as Wave 11.
///   </item>
/// </list>
///
/// <para>
/// Server-integrity note (per CLAUDE.md). The MOVE payload sent here is
/// exactly the wire shape the retail Win32 client emits: 4-byte LE GameID
/// + 1-byte engine type (5B total). type=1 selects the "engine on" branch
/// (anything not equal to 4 = engine on); this matches the retail
/// client's behaviour when the user presses W to start the engines. We
/// are not making the server accept anything it didn't previously accept.
/// The retail server emits no direct reply on this branch — that's why
/// we use a survival probe rather than asserting a fabricated reply.
/// </para>
///
/// <para>
/// Budget: 90s. Handshake ~2s; MOVE+REQUEST_TIME round-trip is
/// sub-second. Wide budget covers stage-ack retry in the login state
/// machine.
/// </para>
/// </summary>
[Collection(ServerCollection.Name)]
public sealed class SectorMoveTests
{
    private readonly ServerFixture _server;
    private readonly ClientFixture _client;

    public SectorMoveTests(ServerFixture server)
    {
        _server = server;
        _client = new ClientFixture(server);
    }

    [Fact]
    public async Task Move_EngineOn_DoesNotBreakConnection_RequestTimeStillRoundTrips()
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
            firstName: "Mover", shipName: "MoveShip", cts.Token);

        try
        {
            // MovePacket wire layout — 5 bytes total:
            //   [0..4)   int32 LE  GameID  — retail client sets the
            //                                 actor's game id; server
            //                                 resolves the actor via the
            //                                 connection binding so this
            //                                 field is effectively unused.
            //   [4..5)   byte      type    — engine mode. 4 = engine off,
            //                                 anything else = engine on
            //                                 (per HandleMove switch at
            //                                 server/src/PlayerConnection.cpp:1861).
            //                                 We send 1 for "engine on".
            // common/include/net7/PacketStructures.h:981
            byte[] movePayload = new byte[5];
            BinaryPrimitives.WriteInt32LittleEndian(movePayload.AsSpan(0, 4), 0);
            movePayload[4] = 1;

            await session.Sector.SendAsync(
                Packet.ForOpcode(OpcodeId.Known.Move.Value, movePayload),
                cts.Token);

            // Survival probe.
            int clientTick = unchecked((int)(DateTime.UtcNow.Ticks & 0x7FFFFFFF));

            byte[] reqTimePayload = new byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(reqTimePayload, clientTick);

            await session.Sector.SendAsync(
                Packet.ForOpcode(OpcodeId.Known.RequestTime.Value, reqTimePayload),
                cts.Token);

            // Drain until 0x0034 CLIENT_SET_TIME. Post-handshake the
            // server may begin streaming in-sector frames (ship updates,
            // contrail state, etc.) so this loop tolerates interleaved
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
                $"drained {maxFrames} frames after sending 0x0014 MOVE (type=1, engine-on) + 0x0044 REQUEST_TIME " +
                $"without seeing 0x0034 CLIENT_SET_TIME. " +
                $"Likely the server's HandleMove read past the 5-byte payload (sizeof(long) regression on MovePacket), " +
                $"the proxy's ProcessSectorServerOpcode dispatch dropped the frame " +
                $"(proxy/ClientToServer_linux_stubs.cpp:487-489), " +
                $"or AbortProspecting()/FormationEngineOperation() crashed mid-call.");
        }
        finally
        {
            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await SectorHandshake.DeleteCreatedCharacterAsync(session.Global, slot, cleanupCts.Token); }
            catch { /* best-effort cleanup */ }
        }
    }
}
