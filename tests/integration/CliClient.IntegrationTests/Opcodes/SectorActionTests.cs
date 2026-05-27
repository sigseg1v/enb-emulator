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
/// Wave 13 post-handshake survival round-trip: client sends 0x002C ACTION
/// (the catch-all combat / interaction / docking opcode) with a sub-action
/// that is a server-side no-op, then verifies the connection survives by
/// round-tripping 0x0044 REQUEST_TIME.
///
/// <para>
/// Why survival probe rather than direct reply assertion.
/// <c>Player::HandleAction</c>
/// (<c>server/src/PlayerConnection.cpp:3708</c>) dispatches on
/// <c>myAction-&gt;Action</c> through a 30-ish entry switch. The vast
/// majority of sub-actions need authoritative in-space state — a valid
/// target object, an equipped weapon, a registered starbase, a started
/// trade — none of which a freshly-handed-off starbase character has.
/// Pick a sub-action that lands cleanly with zero side effects:
/// <c>case 23 // keep trading???</c> (lines 4104-4108) is a literal
/// commented-out no-op. The handler reads the canonical 16-byte
/// ActionPacket struct (GameID/Action/Target/OptionalVar as int32_t LE)
/// and falls into an empty case body, then returns. No reply opcode is
/// emitted on this branch and the retail server doesn't emit one either,
/// so per the CLAUDE.md server-integrity rule we cannot fabricate one.
/// </para>
///
/// <para>
/// What we CAN assert: the dispatcher accepted the wire format, the
/// switch found case 23, the proxy didn't drop or mangle the frame, and
/// the connection survives — all observable through a follow-up 0x0044
/// REQUEST_TIME round-trip.
/// </para>
///
/// <para>
/// Concrete regression class this catches: if anyone reverts the Wave 11
/// PacketStructures.h <c>long</c>→<c>int32_t</c> migration on
/// ActionPacket, the struct would grow from 16B to 32B on Linux x86_64
/// and the handler would read Action from offset 8 (where the wire has
/// Target's high half) instead of offset 4. The dispatched sub-action
/// number would then be garbage and almost certainly miss case 23 → fall
/// to the default branch → emit "UNRECOGNIZED ACTION! SUBMIT BUG
/// REPORT!" via SendVaMessage. The survival probe still passes in that
/// regression (the connection doesn't die) but the explicit case-23
/// payload choice makes the retail-fidelity intent visible in the test.
/// </para>
///
/// <para>
/// Other bugs this test would also catch:
/// </para>
/// <list type="bullet">
///   <item>
///     Proxy <c>ProcessSectorServerOpcode</c> for ACTION
///     (<c>proxy/ClientToServer_linux_stubs.cpp:471-477</c>) dropping or
///     double-forwarding the opcode. The current path forwards
///     explicitly then calls <c>ProcessAction_Linux</c> (whose body is
///     all <c>//</c>-commented no-ops); a regression that called
///     <c>ForwardClientOpcode</c> twice would manifest as two server-side
///     handler invocations — benign for sub-action 23 but a silent
///     duplicate-input hazard for state-mutating sub-actions later.
///   </item>
///   <item>
///     <c>HandleAction</c>'s lookup of <c>obj</c> via
///     <c>GetObjectFromID(myAction-&gt;Target)</c> returning a bogus
///     pointer for the Target=0 sentinel and crashing on a deref before
///     reaching the switch. Sub-action 23 doesn't touch <c>obj</c>, so a
///     null-deref pre-switch would surface here as the survival probe
///     never completing.
///   </item>
/// </list>
///
/// <para>
/// Server-integrity note (per CLAUDE.md). The ACTION payload sent here
/// is exactly the wire shape the retail Win32 client emits: 4-byte LE
/// GameID, Action, Target, OptionalVar. Sub-action 23 ("keep trading")
/// is one of the retail client's published values; we are not making the
/// server accept anything it didn't previously accept. The retail server
/// also emits no direct reply on this branch — that's why we use a
/// survival probe rather than asserting a fabricated reply.
/// </para>
///
/// <para>
/// Budget: 90s. Handshake ~2s; ACTION+REQUEST_TIME round-trip is
/// sub-second. Wide budget covers stage-ack retry in the login state
/// machine.
/// </para>
/// </summary>
[Collection(ServerCollection.Name)]
public sealed class SectorActionTests
{
    private readonly ServerFixture _server;
    private readonly ClientFixture _client;

    public SectorActionTests(ServerFixture server)
    {
        _server = server;
        _client = new ClientFixture(server);
    }

    [Fact]
    public async Task Action_NoOpSubAction_DoesNotBreakConnection_RequestTimeStillRoundTrips()
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
            firstName: "Actor", shipName: "ActShip", cts.Token);

        try
        {
            // ActionPacket wire layout — 16 bytes total, all int32_t LE:
            //   [0..4)   GameID       — retail client sets the actor's
            //                            game id; server resolves the
            //                            actor via the connection
            //                            binding so this field is
            //                            effectively unused, but its
            //                            width matters for the struct
            //                            offset of Action.
            //   [4..8)   Action       — sub-action selector. 23 =
            //                            "keep trading???" (a commented-
            //                            out no-op in HandleAction).
            //   [8..12)  Target       — target game id. 0 (none) is
            //                            safe for sub-action 23 because
            //                            the case body never touches it.
            //   [12..16) OptionalVar  — sub-action-specific scalar;
            //                            unused by sub-action 23.
            // common/include/net7/PacketStructures.h:546
            byte[] actionPayload = new byte[16];
            BinaryPrimitives.WriteInt32LittleEndian(actionPayload.AsSpan(0, 4), 0);
            BinaryPrimitives.WriteInt32LittleEndian(actionPayload.AsSpan(4, 4), 23);
            BinaryPrimitives.WriteInt32LittleEndian(actionPayload.AsSpan(8, 4), 0);
            BinaryPrimitives.WriteInt32LittleEndian(actionPayload.AsSpan(12, 4), 0);

            await session.Sector.SendAsync(
                Packet.ForOpcode(OpcodeId.Known.Action.Value, actionPayload),
                cts.Token);

            // Survival probe.
            int clientTick = unchecked((int)(DateTime.UtcNow.Ticks & 0x7FFFFFFF));

            byte[] reqTimePayload = new byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(reqTimePayload, clientTick);

            await session.Sector.SendAsync(
                Packet.ForOpcode(OpcodeId.Known.RequestTime.Value, reqTimePayload),
                cts.Token);

            // Drain until 0x0034 CLIENT_SET_TIME. Post-handshake the
            // server may begin streaming in-sector frames so this loop
            // tolerates interleaved traffic. Cap on frame count so a
            // stalled pipeline can't masquerade as the outer-CTS
            // timeout.
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
                $"drained {maxFrames} frames after sending 0x002C ACTION (sub=23) + 0x0044 REQUEST_TIME " +
                $"without seeing 0x0034 CLIENT_SET_TIME. " +
                $"Likely the server's HandleAction switch fell to default (UNRECOGNIZED ACTION) " +
                $"and the connection state was corrupted, the proxy's ProcessSectorServerOpcode dispatch " +
                $"(proxy/ClientToServer_linux_stubs.cpp:471-477) dropped the frame, " +
                $"or HandleAction's pre-switch GetObjectFromID(Target=0) crashed.");
        }
        finally
        {
            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await SectorHandshake.DeleteCreatedCharacterAsync(session.Global, slot, cleanupCts.Token); }
            catch { /* best-effort cleanup */ }
        }
    }
}
