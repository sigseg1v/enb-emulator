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
/// Wave 70 post-handshake survival round-trip: client sends 0x0098
/// GALAXY_MAP_REQUEST on the sector connection after STAGE handshake
/// completes, then verifies pipe survival via 0x0044 REQUEST_TIME.
///
/// <para>
/// Server handler. Dispatcher case at
/// <c>server/src/PlayerConnection.cpp:567-569</c> calls
/// <c>HandleGalaxyMapRequest()</c> (no-args overload — the dispatcher
/// invokes it without forwarding the payload pointer because the
/// retail client sends an empty-body 0x0098). The handler itself at
/// <c>server/src/PlayerConnection.cpp:10715-10720</c> is literally
/// one effective line: <c>SendOpcode(ENB_OPCODE_2011_GALAXY_MAP_CACHE,
/// 0, 0)</c>. No state mutation, no fan-out, no DB write — the
/// purest one-liner emit handler in the 0x0090-0x009F dispatch range.
/// </para>
///
/// <para>
/// Wire shape. 0x0098 is empty-body. The dispatcher reads
/// <c>HandleClientOpcode(short opcode, short bytes, unsigned char *data)</c>
/// but the case arm passes <c>data</c> to nothing — the handler
/// signature is parameterless. We send a 0-byte payload to match the
/// retail wire form.
/// </para>
///
/// <para>
/// Why survival-probe instead of byte-exact reply assertion. The reply
/// (0x2011 GALAXY_MAP_CACHE, empty body) is silently dropped by the
/// proxy at <c>proxy/UDPProxyToClient_linux.cpp::HandleCustomOpcode</c>
/// — Wave 70 also lands the proxy-side fidelity fix that has the
/// launcher-side opcode set (0x2011 GALAXY_MAP_CACHE, 0x2012-0x2014
/// PROSPECT/TRACTOR/LOOT, 0x2018-0x2019 STATIC/RESOURCE_OBJECT_CREATE)
/// return TRUE (handled-and-dropped) so the packet sequence walker
/// advances cleanly. Pre-fix it returned FALSE, which fell through to
/// the &lt; 0x0FFF guard at <c>SendClientPacketSequence</c>: the guard
/// set <c>terminate = true</c>, the function returned false, the
/// caller marked the packet PACKET_BLANK and never advanced
/// m_CurrentPacketNum — every subsequent server reply queued behind
/// the stuck slot until the test cancellation token fired. The Win32
/// retail proxy consumes 0x2011 locally too (UDPProxyToClient.cpp:370
/// SendCachedGalaxyMap + SetReceivedGalaxyMap, UDPClient.cpp:263
/// SendCachedGalaxyMap); it never forwards 0x2011 over the game-
/// protocol TCP channel. Silent-drop is preservation-faithful, the
/// stall was a bug. Survival probe via REQUEST_TIME confirms the
/// post-fix pipe stays open.
/// </para>
///
/// <para>
/// Concrete regression classes this catches.
/// </para>
/// <list type="bullet">
///   <item>
///     <b>Dispatcher mis-route at <c>PlayerConnection.cpp:567-569</c>.</b>
///     The 0x0098 case sits between 0x008D INCAPACITANCE_REQUEST
///     (line 563, Wave 29 abandoned target — known SEGV via
///     <c>NPCs->Avatar</c> global mutation) and 0x009B WARP (line 571,
///     heavy state mutation). A copy-paste swap with the adjacent
///     INCAPACITANCE arm on our empty payload would drive
///     HandleIncapacitanceRequest's <c>long player = *data</c> read
///     past byte 0 of a 0-byte buffer (UB — read from past-end of
///     framing buffer or possibly NULL). A swap with HandleWarp on
///     empty payload would over-read for the Warp struct cast.
///     Survival via REQUEST_TIME catches the crash boundary.
///   </item>
///   <item>
///     <b>HandleGalaxyMapRequest one-liner regression at
///     <c>PlayerConnection.cpp:10715-10720</c>.</b> Currently emits a
///     single empty 0x2011 GALAXY_MAP_CACHE frame. A refactor that
///     swaps in a SendDataFileToClient call (which the commented-out
///     line at PlayerConnection.cpp:10719 hints at) could attempt to
///     fopen() a GalaxyMap.dat that doesn't exist in the test
///     environment — the fopen failure path's NULL-deref would crash
///     the connection. Survival catches.
///   </item>
///   <item>
///     <b>SendOpcode header-width revert at
///     <c>server/src/PlayerConnection.cpp:127</c>.</b> Would corrupt
///     every inner opcode in the 0x2016 PACKET_SEQUENCE parser —
///     REQUEST_TIME echo silents and the test times out. Same shape
///     as Waves 11/27/29/30/36/37/38/39/40/41/44.
///   </item>
///   <item>
///     <b>Proxy default-case ForwardClientOpcode dropping 0x0098.</b>
///     0x0098 is not explicitly listed in
///     <c>proxy/ClientToServer_linux_stubs.cpp</c>'s
///     ProcessSectorServerOpcode switch (verified by grep — no 0x0098
///     references in proxy/*.cpp outside the openssl vendored tables),
///     so it falls through to the bottom-of-switch default
///     ForwardClientOpcode arm. A regression dropping that default arm
///     would mean the server never sees the GALAXY_MAP_REQUEST frame
///     — REQUEST_TIME uses the same default arm, so a true default
///     drop also silents REQUEST_TIME echo and the test times out.
///   </item>
///   <item>
///     <b>Proxy <c>SendClientPacketSequence</c> guard inversion at
///     <c>UDPProxyToClient_linux.cpp:568</c>.</b> Currently blocks
///     0x2011 (>= 0x0FFF). An inversion that started forwarding
///     0x2011 would let the empty 0x2011 frame through — survival
///     probe would still pass (REQUEST_TIME echo unaffected), but a
///     future byte-comparison wave on 0x2011 would catch it.
///     Documented for completeness; not asserted here.
///   </item>
/// </list>
///
/// <para>
/// Per CLAUDE.md server-integrity. 0x0098 GALAXY_MAP_REQUEST is what
/// the retail Win32 client emits when the user opens the galaxy-map
/// UI. The retail server's response is exactly the one-line
/// SendOpcode(0x2011, 0, 0) we have today — the cache-empty signal.
/// No widened input acceptance, no loosened gating, no fabricated
/// reply. The proxy guard that blocks 0x2011 is a known
/// preservation-fidelity gap (retail proxy forwards 0x2011); fixing
/// that gap is a separate proxy-side wave.
/// </para>
///
/// <para>
/// Budget: 90s. Handshake ~2s; 0x0098 + REQUEST_TIME round-trip is
/// sub-second.
/// </para>
/// </summary>
[Collection(ServerCollection.Name)]
public sealed class SectorGalaxyMapRequestTests
{
    private readonly ServerFixture _server;
    private readonly ClientFixture _client;

    public SectorGalaxyMapRequestTests(ServerFixture server)
    {
        _server = server;
        _client = new ClientFixture(server);
    }

    [Fact]
    public async Task GalaxyMapRequest_OnFreshSession_DoesNotBreakConnection_RequestTimeStillRoundTrips()
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
            firstName: "Gmap70", shipName: "Gmap70Ship", cts.Token);

        try
        {
            // 0x0098 GALAXY_MAP_REQUEST — empty payload. The retail
            // client-side handler is parameterless and the server
            // dispatcher invokes HandleGalaxyMapRequest() with no args
            // (server/src/PlayerConnection.cpp:567-569). The server
            // handler at PlayerConnection.cpp:10715-10720 emits a
            // single 0x2011 GALAXY_MAP_CACHE with empty body —
            // dropped by the proxy guard at
            // UDPProxyToClient_linux.cpp:568 (0x2011 >= 0x0FFF). We
            // can't observe the reply directly so we observe pipe
            // survival via the REQUEST_TIME echo below.
            await session.Sector.SendAsync(
                Packet.ForOpcode(OpcodeId.Known.GalaxyMapRequest.Value, ReadOnlyMemory<byte>.Empty),
                cts.Token);

            // Survival probe: did the connection survive the
            // GALAXY_MAP_REQUEST handler? Send REQUEST_TIME and assert
            // CLIENT_SET_TIME echoes our sentinel tick.
            int clientTick = unchecked((int)(DateTime.UtcNow.Ticks & 0x7FFFFFFF));

            byte[] reqTimePayload = new byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(reqTimePayload, clientTick);

            await session.Sector.SendAsync(
                Packet.ForOpcode(OpcodeId.Known.RequestTime.Value, reqTimePayload),
                cts.Token);

            // Drain until 0x0034 CLIENT_SET_TIME. Tolerate interleaved
            // in-sector frames (positional updates from observers).
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
                $"drained {maxFrames} frames after sending 0x0098 GALAXY_MAP_REQUEST + 0x0044 REQUEST_TIME " +
                $"without seeing 0x0034 CLIENT_SET_TIME. " +
                $"Likely the dispatcher arm at PlayerConnection.cpp:567-569 got mis-routed " +
                $"(swap with HandleIncapacitanceRequest's *data dereference on our 0-byte payload, " +
                $"or swap with HandleWarp's struct cast over-reading our empty body), " +
                $"HandleGalaxyMapRequest got a SendDataFileToClient refactor that NULL-derefs on " +
                $"missing GalaxyMap.dat in the test env, " +
                $"the SendOpcode header-width fix at PlayerConnection.cpp:127 was reverted, " +
                $"or the proxy default-case ForwardClientOpcode arm was dropped.");
        }
        finally
        {
            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await SectorHandshake.DeleteCreatedCharacterAsync(session.Global, slot, cleanupCts.Token); }
            catch { /* best-effort cleanup */ }
        }
    }
}
