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
/// Wave 23 post-handshake survival round-trip: client sends a zero-byte
/// 0x001A DEBUG, then verifies the connection survives by round-tripping
/// 0x0044 REQUEST_TIME.
///
/// <para>
/// Wire layout. None. The retail Win32 client emits DEBUG with no
/// structured payload — the opcode itself is the entire signal. Server
/// <c>Player::HandleDebug</c> (<c>server/src/PlayerConnection.cpp:10760</c>)
/// accepts <c>unsigned char *data</c> but never reads from it.
/// </para>
///
/// <para>
/// Server handler. The handler body is literally one line:
/// <code>
///     void Player::HandleDebug(unsigned char *data)
///     {
///         LogDebug("Received Debug packet\n");
///     }
/// </code>
/// And the LogDebug callee
/// (<c>server/src/ServerManager.cpp:745</c>) has TWO short-circuits:
/// <code>
///     void LogDebug(char *format, ...)
///     {
///         if (!g_Debug) return;
///         return; //no logdebugs for now, crashes the server
///         ...
///     }
/// </code>
/// So even if the global <c>g_Debug</c> flag were enabled at runtime,
/// the hard-coded early-return on line 749 would still suppress the
/// log. <c>HandleDebug</c> is therefore a true no-op: no payload parse,
/// no I/O, no DB write, no state mutation, no reply.
/// </para>
///
/// <para>
/// Why survival probe rather than direct reply assertion. The handler
/// emits no opcode reply. The retail server's HandleDebug is the same
/// no-op (the Net-7 source matches retail here); retail emits no reply
/// either. Per the CLAUDE.md server-integrity rule we cannot fabricate
/// one — survival probe is the only assertable post-condition.
/// </para>
///
/// <para>
/// Why empty payload. The handler ignores <c>data</c> entirely, so
/// any number of bytes (zero through receive-buffer-cap) is equally
/// valid. Empty is the smallest wire footprint and the most likely
/// retail emission (debug-channel signals tend to be opcode-only). It
/// also stresses any future refactor that adds payload parsing without
/// a length guard: a regression that wrote
/// <c>SomeStruct *s = (SomeStruct *)data;</c> would crash on the first
/// field deref against the zero-byte buffer (technically against the
/// caller's receive-buffer pointer, which exists, but the field would
/// read into unrelated buffer slack — far less obvious to debug than a
/// clean null-deref). Sending non-empty bytes would still hit the same
/// no-op branch but would test only the receive-buffer discard path,
/// not anything the handler does.
/// </para>
///
/// <para>
/// Concrete regression classes this catches:
/// </para>
/// <list type="bullet">
///   <item>
///     <b>Dispatcher mis-route at
///     <c>server/src/PlayerConnection.cpp:451</c>.</b> The case label
///     sits between 0x0019 SET_TARGET (the HandleRequestTarget reply
///     emitter, not a dispatched-to case) and 0x001D MESSAGE_STRING in
///     the same ~200-entry hand-maintained switch; a copy-paste error
///     swapping HandleDebug for any nearby handler that reads a struct
///     would crash on the first field deref against a zero-byte
///     payload — most adjacent handlers expect at least 4-12 bytes.
///   </item>
///   <item>
///     <b>Proxy default-case <c>ForwardClientOpcode</c> regression.</b>
///     0x001A is NOT explicitly listed in
///     <c>proxy/ClientToServer_linux_stubs.cpp</c>, so it hits the
///     <c>default:</c> arm and falls through to the bottom-of-switch
///     forward. A regression dropping this opcode would surface as a
///     timeout waiting for CLIENT_SET_TIME.
///   </item>
///   <item>
///     <b>HandleDebug body regression that adds payload parsing
///     without a length guard.</b> Any future refactor that promotes
///     HandleDebug to do real work (e.g. take a debug-command string)
///     must include a length check on the receive frame size; absent
///     that, the zero-byte payload this test sends will crash the
///     handler on first field deref.
///   </item>
///   <item>
///     <b>LogDebug early-return removal regression.</b> If either of
///     the two short-circuits in
///     <c>ServerManager.cpp:745-749</c> is removed, the
///     <c>vsprintf_s</c> + <c>ostringstream</c> + <c>LogMessage</c>
///     path runs with <c>g_Debug==false</c> in our docker stack and
///     (per the inline comment "//no logdebugs for now, crashes the
///     server") corrupts state or crashes. The test would surface that
///     as a CLIENT_SET_TIME timeout.
///   </item>
/// </list>
///
/// <para>
/// Server-integrity note (per CLAUDE.md). 0x001A DEBUG is a
/// debug-channel opcode the retail Win32 client sometimes emits when
/// the user issues a /debug command. The retail server's HandleDebug
/// is the same no-op — accepts the opcode, writes nothing back. We are
/// not making the server accept any new input shape; we are not
/// fabricating any reply. Zero-byte payload is the canonical retail
/// emission for opcode-only debug signals.
/// </para>
///
/// <para>
/// Budget: 90s. Handshake ~2s; DEBUG+REQUEST_TIME round-trip is
/// sub-second.
/// </para>
/// </summary>
[Collection(ServerCollection.Name)]
public sealed class SectorDebugTests
{
    private readonly ServerFixture _server;
    private readonly ClientFixture _client;

    public SectorDebugTests(ServerFixture server)
    {
        _server = server;
        _client = new ClientFixture(server);
    }

    [Fact]
    public async Task Debug_EmptyPayload_DoesNotBreakConnection_RequestTimeStillRoundTrips()
    {
        // cli_test20 — Pool[18]. Dedicated to this test so its
        // Create/Delete cycle doesn't collide with Pool[3..17] which
        // are owned by prior Phase K waves.
        var account = TestAccounts.Pool[18];
        const int slot = 0;
        const int sectorId = 10151;  // Terran Warrior start: Luna Station

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        var login = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(account.Username, account.Password), cts.Token);
        Assert.True(login.Valid, $"login: {login.RawBody.TrimEnd()}");
        Assert.False(string.IsNullOrEmpty(login.Ticket));

        await using var session = await SectorHandshake.EstablishAsync(
            _server, login.Ticket!, account.Username, slot, sectorId,
            firstName: "Debby", shipName: "DebShip", cts.Token);

        try
        {
            // 0x001A DEBUG — zero-byte payload. HandleDebug ignores
            // data entirely (no struct cast, no field reads). The
            // handler body is literally one line of LogDebug, and
            // LogDebug itself early-returns before doing anything.
            await session.Sector.SendAsync(
                Packet.ForOpcode(OpcodeId.Known.Debug.Value, Array.Empty<byte>()),
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
                $"drained {maxFrames} frames after sending 0x001A DEBUG (empty payload) + 0x0044 REQUEST_TIME " +
                $"without seeing 0x0034 CLIENT_SET_TIME. " +
                $"Likely the server's HandleDebug grew payload parsing without a length guard and crashed on the zero-byte payload, " +
                $"the LogDebug early-return at ServerManager.cpp:749 was removed and the vsprintf_s path corrupted state, " +
                $"the proxy default-case forwarding dropped the opcode, " +
                $"or the dispatcher case at PlayerConnection.cpp:451 got mis-routed to a nearby struct-reading handler.");
        }
        finally
        {
            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await SectorHandshake.DeleteCreatedCharacterAsync(session.Global, slot, cleanupCts.Token); }
            catch { /* best-effort cleanup */ }
        }
    }
}
