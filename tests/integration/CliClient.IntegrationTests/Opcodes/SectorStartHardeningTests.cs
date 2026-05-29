// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond preservation project.
// License: LICENSES/enb-emulator

using N7.CliClient.Auth;
using N7.CliClient.Opcodes;
using Xunit;

namespace N7.CliClient.IntegrationTests.Opcodes;

/// <summary>
/// Wave 83 hardening test (+0 ratchet, 0x0005): pins the byte-exact
/// 4-byte payload shape of the single 0x0005 START frame the server
/// emits as the terminator of every sector-login handshake.
///
/// <para>
/// Backstory. 0x0005 START is server-emitted by
/// <c>Player::SendStart</c> at
/// <c>server/src/PlayerConnection.cpp:1079-1089</c>. The handler
/// narrows the <c>long start_id</c> parameter to <c>int32_t</c> via
/// <c>int32_t start_id_wire = (int32_t) start_id;</c> and emits via
/// <c>SendOpcode(ENB_OPCODE_0005_START, &amp;start_id_wire, sizeof(start_id_wire))</c>.
/// The narrowing is load-bearing on LP64 Linux: passing
/// <c>&amp;start_id</c> with <c>sizeof(start_id)</c> directly would
/// emit 8 bytes and push 4 garbage bytes into the next opcode in the
/// UDP packet sequence the proxy hands to the client (same regression
/// class as the MasterJoin / GlobalTicket sizeof(long) narrowings the
/// Phase K wave series documents inline). SendStart is called from
/// <c>SectorManager::SectorLogin2</c> at
/// <c>server/src/SectorManager.cpp:379</c> (space arm) and
/// <c>SectorManager::StationLogin2</c> at <c>SectorManager.cpp:526</c>
/// (station arm) as the final fan-out step before the handshake
/// completes — every sector-login that completes necessarily emits
/// exactly one 0x0005 START frame, and that frame is the terminator
/// the test harness's
/// <c>SectorHandshake.DoSectorLoginUntilStartAsync</c> drain loop
/// (Opcodes/SectorHandshake.cs:386-428) waits for. The wire size is
/// <c>sizeof(int32_t) = 4</c> bytes — exactly what the retail Win32
/// client's START decoder was compiled to receive.
/// </para>
///
/// <para>
/// Why a hardening test, not a +1 ratchet wave. 0x0005 is already
/// counted by Wave 7 (<see cref="SectorStartAckTests"/>'s
/// <c>StartAck_DoesNotBreakConnection_RequestTimeStillRoundTrips</c>),
/// which exercises the client-side 0x0006 START_ACK after the server
/// has emitted 0x0005 START — the server emit is an implicit
/// precondition of that test rather than an explicit assertion. The
/// drain loop captures 0x0005 (line 416 records the terminator before
/// returning) but no test asserts the byte-exact length of the
/// captured frame. Wave 83 adds that byte-exact 4-byte length
/// assertion, locking the wire shape in place. +0 ratchet because
/// 0x0005 is already counted; depth coverage of a regression class
/// the existing test was structurally blind to. Mirrors the Wave
/// 67/71/76/77/78/79/80/81/82 pattern (byte-exact tightenings on
/// already-counted handshake emits).
/// </para>
///
/// <para>
/// Wire shape and dispatch path. The 0x0005 START frame is the LAST
/// frame in the handshake stream — the drain loop returns immediately
/// after recording it. The terminator-only invariant matters: a
/// regression that emits an extra 0x0005 mid-handshake (e.g. a refactor
/// duplicating the SendStart call site, or a SectorLogin2 ordering
/// regression that emits 0x0005 before the fan-out completes) would
/// truncate the captured HandshakeFrames list and silently drop
/// downstream emits from the test's view. Wave 83 asserts there is
/// exactly ONE 0x0005 frame in the captured stream — both
/// <c>Assert.Single</c> (count) and the per-frame
/// <c>Assert.Equal(4, payloadLength)</c> (length) together pin the
/// retail-faithful terminator invariant.
/// </para>
///
/// <para>
/// Regression classes this catches.
/// </para>
/// <list type="bullet">
///   <item>
///     <b>SendStart sizeof(long) revert at
///     <c>PlayerConnection.cpp:1087-1088</c>.</b> The narrowing to
///     <c>int32_t start_id_wire</c> is what keeps the wire emit at 4
///     bytes on LP64 Linux. A regression to
///     <c>SendOpcode(..., &amp;start_id, sizeof(start_id))</c> would
///     emit 8 bytes, push 4 garbage bytes into the next opcode in the
///     UDP packet sequence the proxy hands to the client, and break
///     the retail Win32 client's START decoder. The byte-exact
///     length assertion catches this immediately.
///   </item>
///   <item>
///     <b>SendStart removal at SectorManager.cpp:379 / :526.</b> A
///     regression that removes the
///     <c>player-&gt;SendStart(player-&gt;CharacterID())</c> call
///     from either SectorLogin2 or StationLogin2 would mean the drain
///     loop never sees its terminator and times out at maxFrames=4000
///     before this assertion runs — surfaces as a
///     drain-loop-timeout XunitException, not an assertion failure,
///     but is caught all the same.
///   </item>
///   <item>
///     <b>Duplicated SendStart emit.</b> A regression that emits an
///     extra 0x0005 mid-handshake would still terminate the drain
///     loop on the first occurrence — but the downstream fan-out
///     emits would be silently dropped from HandshakeFrames, breaking
///     the eight prior hardening waves (76, 77, 78, 79, 80, 81, 82
///     and 71/67). The <c>Assert.Single</c> count check would fire
///     only if BOTH 0x0005 frames somehow appeared in HandshakeFrames
///     — which they don't given the drain loop's early return — so
///     the duplicate-emit class is partially indirect; but the
///     downstream tests' presence assertions catch the indirect
///     effect. Wave 83 documents the invariant directly.
///   </item>
///   <item>
///     <b>SendOpcode header-width revert at
///     <c>PlayerConnection.cpp:127</c>.</b> Would corrupt every inner
///     opcode in the 0x2016 PACKET_SEQUENCE parser; 0x0005 wouldn't
///     appear under its correct label at all — the drain loop would
///     time out.
///   </item>
///   <item>
///     <b>Proxy SendClientPacketSequence inner-opcode guard tightening
///     at <c>proxy/UDPProxyToClient_linux.cpp:568</c>.</b> Currently
///     passes 0x0005 (0x0005 &lt; 0x0FFF). A regression to a tighter
///     upper bound that excluded 0x0005 would silently drop the
///     terminator from the wire and the drain loop would time out.
///   </item>
///   <item>
///     <b>DoSectorLoginUntilStartAsync drain-loop payload-length
///     capture regression
///     (<c>Opcodes/SectorHandshake.cs:416</c>).</b> The capture path
///     records <c>(reply.Header.Opcode, reply.Payload.Length)</c> for
///     every inbound frame including the terminator. If a future
///     refactor drops the length field or under-counts payload bytes
///     for the terminator specifically, this test observes the wrong
///     length on the captured 0x0005 frame.
///   </item>
/// </list>
///
/// <para>
/// Primary source citation (CLAUDE.md server-integrity rule). 0x0005
/// START is server-originated. Wave 83 adds no client stimulus and
/// no server change — pure passive-observation tightening of a
/// retail-faithful wire shape. The 4-byte body is exactly what the
/// retail Win32 client's START decoder was compiled to receive (the
/// Win32 <c>sizeof(long) == 4</c> footprint); the inline comment at
/// <c>PlayerConnection.cpp:1083-1086</c> documents the existing fix
/// against the LP64 Linux <c>sizeof(long) == 8</c> divergence. Wave
/// 83 locks that fix in place. No widened input acceptance, no
/// loosened gating, no fabricated replies — server-integrity
/// POSITIVE.
/// </para>
///
/// <para>
/// Budget: 60s. Single-stage station handshake into Luna Station
/// (10151) ~2s; assertions run synchronously against already-captured
/// state. No additional client stimulus.
/// </para>
/// </summary>
[Collection(ServerCollection.Name)]
public sealed class SectorStartHardeningTests
{
    /// <summary>
    /// <c>sizeof(int32_t) = 4</c>. Matches the wire size computed by
    /// <c>sizeof(start_id_wire)</c> at
    /// <c>server/src/PlayerConnection.cpp:1088</c> after the explicit
    /// <c>int32_t</c> narrowing at line 1087. The retail Win32 client
    /// was compiled with <c>sizeof(long) == 4</c> and expects exactly
    /// this 4-byte body.
    /// </summary>
    private const int ExpectedStartPayloadLength = 4;

    private readonly ServerFixture _server;
    private readonly ClientFixture _client;

    public SectorStartHardeningTests(ServerFixture server)
    {
        _server = server;
        _client = new ClientFixture(server);
    }

    [Fact]
    public async Task Start_TerminatorOfStationSectorHandshake_HasExactly4BytePayload()
    {
        var account = TestAccounts.New(_server);
        const int slot = 0;
        const int stationSectorId = 10151;  // Terran Warrior start: Luna Station

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));

        var login = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(account.Username, account.Password), cts.Token);
        Assert.True(login.Valid, $"login: {login.RawBody.TrimEnd()}");
        Assert.False(string.IsNullOrEmpty(login.Ticket));

        await using var session = await SectorHandshake.EstablishAsync(
            _server, login.Ticket!, account.Username, slot, stationSectorId,
            firstName: "Start83", shipName: "Start83Ship", cts.Token);

        var startFrames = session.HandshakeFrames
            .Where(f => f.Opcode == OpcodeId.Known.Start.Value)
            .ToList();

        Assert.Single(startFrames);
        Assert.Equal(ExpectedStartPayloadLength, startFrames[0].PayloadLength);
    }
}
