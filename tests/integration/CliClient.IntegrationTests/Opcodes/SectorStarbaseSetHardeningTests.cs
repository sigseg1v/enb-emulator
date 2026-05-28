// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond preservation project.
// License: LICENSES/enb-emulator

using N7.CliClient.Auth;
using N7.CliClient.Opcodes;
using Xunit;

namespace N7.CliClient.IntegrationTests.Opcodes;

/// <summary>
/// Wave 80 hardening test (+0 ratchet, 0x004F): pins the byte-exact
/// 6-byte payload shape of every 0x004F STARBASE_SET frame the server
/// emits during the station-sector login handshake stream.
///
/// <para>
/// Backstory. 0x004F STARBASE_SET is server-emitted by
/// <c>Player::SendStarbaseSet</c> at
/// <c>server/src/PlayerConnection.cpp:708-717</c>. The handler
/// constructs a stack-local <c>StarbaseSet</c> struct
/// (PacketStructures.h:943-948), zero-initialises it via memset,
/// populates <c>StarbaseID = PlayerIndex()-&gt;GetSectorNum()</c>,
/// <c>Action = action</c>, <c>ExitMode = exit_mode</c>, and emits via
/// <c>SendOpcode(ENB_OPCODE_004F_STARBASE_SET, &amp;starbase_set, sizeof(starbase_set))</c>.
/// SendStarbaseSet is called from <c>SectorManager::StationLogin2</c>
/// at <c>server/src/SectorManager.cpp:514</c> as
/// <c>player-&gt;SendStarbaseSet(0, 0)</c> during the station-sector
/// handshake (action=0, exit_mode=0 — the "entering starbase" form),
/// and again at <c>SectorManager.cpp:537</c> during the station-exit
/// flow as <c>player-&gt;SendStarbaseSet(1, 0)</c>. The wire size is
/// computed via <c>sizeof(starbase_set)</c> against the
/// <c>ATTRIB_PACKED</c> struct, which evaluates to
/// <c>4 (int32_t StarbaseID) + 1 (char Action) + 1 (char ExitMode) = 6</c>
/// bytes. The retail Win32 client was compiled to receive exactly this
/// 6-byte body.
/// </para>
///
/// <para>
/// Why a hardening test, not a +1 ratchet wave. 0x004F is already
/// counted by Wave 51
/// (<see cref="SectorHandshakeFanoutTests.HandshakeEmitsFullSendLoginShipDataFanout"/>) —
/// the passive-observation assertion that the opcode appears in the
/// handshake stream during a station-sector login — and by Wave 73's
/// negative-assertion counterpart (asserts 0x004F does NOT appear in
/// space-sector handshakes). Wave 51's assertion is opcode-presence
/// only; it would still pass if the <c>StarbaseSet</c> struct layout
/// drifted (e.g. <c>StarbaseID</c> widening from <c>int32_t</c> back
/// to <c>long</c> would add 4 bytes on LP64 Linux, or <c>Action</c> /
/// <c>ExitMode</c> widening from <c>char</c> to <c>int32_t</c> would
/// add 3 bytes each). Wave 80 adds the byte-exact 6-byte payload-length
/// assertion the presence-only check cannot make, locking the wire
/// shape in place. +0 ratchet because 0x004F is already counted;
/// depth coverage of a regression class Wave 51 / Wave 73 were
/// structurally blind to. Mirrors the Wave 67/71/76/77/78/79 pattern
/// (byte-exact tightenings on already-counted handshake emits).
/// </para>
///
/// <para>
/// Wire shape and dispatch path. <c>SectorManager::HandleSectorLogin</c>
/// at <c>server/src/SectorManager.cpp:324-336</c> branches on
/// <c>m_SectorID</c> — sector IDs &gt; 9999 are stations (route to
/// <c>StationLogin</c> → <c>StationLogin2</c>). Inside StationLogin2
/// the call at <c>SectorManager.cpp:514</c> emits the entering-starbase
/// 0x004F frame (action=0, exit_mode=0); the StarbaseID is the calling
/// player's sector number (10151 for Luna Station in this test). The
/// station handshake into Luna Station (10151) is the same 1-stage
/// path Wave 51 / Wave 73 exercise; Wave 80 reuses it without
/// modification — same account pool, same firstName / shipName
/// payload, same drain loop — and just adds the byte-exact length
/// assertion on the captured 0x004F frames.
/// </para>
///
/// <para>
/// Regression classes this catches.
/// </para>
/// <list type="bullet">
///   <item>
///     <b>StarbaseSet struct layout regression in
///     <c>common/include/net7/PacketStructures.h:943-948</c>.</b>
///     <c>int32_t StarbaseID; char Action; char ExitMode</c> with
///     <c>ATTRIB_PACKED</c>. A regression widening <c>StarbaseID</c>
///     back to <c>long</c> would add 4 bytes on LP64 Linux (10 bytes
///     total). A regression widening <c>Action</c> or <c>ExitMode</c>
///     from <c>char</c> to <c>int32_t</c> would add 3 bytes each
///     (9 or 12 bytes total).
///   </item>
///   <item>
///     <b>SendStarbaseSet sizeof(starbase_set) replacement at
///     <c>PlayerConnection.cpp:716</c>.</b> A regression to a literal
///     constant (6) that drifts from the actual struct size, or to
///     <c>sizeof(long) + 2</c> on LP64 (10), would emit a wrongly-sized
///     frame.
///   </item>
///   <item>
///     <b>SendStarbaseSet parameter-type widening at
///     <c>PlayerConnection.cpp:708</c>.</b> The signature is
///     <c>SendStarbaseSet(char action, char exit_mode)</c>; a widening
///     to <c>int</c> wouldn't change the wire (the struct fields are
///     still <c>char</c>) but a coincident struct change to widen
///     the fields would propagate through.
///   </item>
///   <item>
///     <b>StationLogin2 fanout chain truncation at
///     <c>SectorManager.cpp:514</c>.</b> A regression that removes the
///     <c>player-&gt;SendStarbaseSet(0, 0)</c> call from StationLogin2
///     (or moves it past the SendStart terminator) would drop 0x004F
///     from the handshake stream entirely.
///   </item>
///   <item>
///     <b>SendOpcode header-width revert at
///     <c>PlayerConnection.cpp:127</c>.</b> Would corrupt every inner
///     opcode in the 0x2016 PACKET_SEQUENCE parser; 0x004F wouldn't
///     appear under its correct label at all (so the
///     <c>Assert.NotEmpty</c> filter catches it before the length
///     check fires).
///   </item>
///   <item>
///     <b>Proxy SendClientPacketSequence inner-opcode guard tightening
///     at <c>proxy/UDPProxyToClient_linux.cpp:568</c>.</b> Currently
///     passes 0x004F (0x004F &lt; 0x0FFF). A regression to a tighter
///     upper bound would silently drop 0x004F from the wire — the
///     captured-frame filter returns empty and the
///     <c>Assert.NotEmpty</c> check fires.
///   </item>
///   <item>
///     <b>DoSectorLoginUntilStartAsync drain-loop payload-length
///     capture regression
///     (<c>tests/integration/CliClient.IntegrationTests/Opcodes/SectorHandshake.cs</c>).</b>
///     The HandshakeFrames capture path populated by the drain loop
///     records the payload-length of every inbound frame. If a future
///     refactor drops the length field or under-counts payload bytes,
///     this test observes wrong (or zero) lengths for every captured
///     0x004F frame.
///   </item>
/// </list>
///
/// <para>
/// Primary source citation (CLAUDE.md server-integrity rule). 0x004F
/// STARBASE_SET is server-originated. Wave 80 adds no client stimulus
/// and no server change — pure passive-observation tightening of a
/// retail-faithful wire shape. The 6-byte body is exactly what the
/// retail Win32 client's STARBASE_SET decoder was compiled to receive;
/// any drift breaks the client. No widened input acceptance, no
/// loosened gating, no fabricated replies — server-integrity POSITIVE.
/// </para>
///
/// <para>
/// Budget: 60s. Single-stage station handshake into Luna Station
/// (10151) ~2s; assertions run synchronously against already-captured
/// state. No additional client stimulus.
/// </para>
/// </summary>
[Collection(ServerCollection.Name)]
public sealed class SectorStarbaseSetHardeningTests
{
    /// <summary>
    /// 4 (int32 StarbaseID) + 1 (char Action) + 1 (char ExitMode) = 6.
    /// Matches the wire size computed by <c>sizeof(StarbaseSet)</c>
    /// against the <c>ATTRIB_PACKED</c> struct at
    /// <c>common/include/net7/PacketStructures.h:943-948</c>.
    /// </summary>
    private const int ExpectedStarbaseSetPayloadLength = 6;

    private readonly ServerFixture _server;
    private readonly ClientFixture _client;

    public SectorStarbaseSetHardeningTests(ServerFixture server)
    {
        _server = server;
        _client = new ClientFixture(server);
    }

    [Fact]
    public async Task StarbaseSet_EmittedDuringStationSectorHandshake_HasExactly6BytePayload()
    {
        var account = TestAccounts.For();
        const int slot = 0;
        const int stationSectorId = 10151;  // Terran Warrior start: Luna Station

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));

        var login = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(account.Username, account.Password), cts.Token);
        Assert.True(login.Valid, $"login: {login.RawBody.TrimEnd()}");
        Assert.False(string.IsNullOrEmpty(login.Ticket));

        await using var session = await SectorHandshake.EstablishAsync(
            _server, login.Ticket!, account.Username, slot, stationSectorId,
            firstName: "SbSet80", shipName: "SbSet80Ship", cts.Token);

        var starbaseSetFrames = session.HandshakeFrames
            .Where(f => f.Opcode == OpcodeId.Known.StarbaseSet.Value)
            .ToList();

        Assert.NotEmpty(starbaseSetFrames);
        Assert.All(starbaseSetFrames, f =>
            Assert.Equal(ExpectedStarbaseSetPayloadLength, f.PayloadLength));
    }

    /// <summary>
    /// Wave 97 frame-count hardening (+0 ratchet, 0x004F): pins the
    /// exact 1-frame emit-count invariant Wave 80's payload-length
    /// hardening was structurally blind to. The captured single-player
    /// station-sector handshake stream emits 0x004F STARBASE_SET
    /// exactly once — from <c>SectorManager::StationLogin2</c> at
    /// <c>server/src/SectorManager.cpp:514</c>
    /// (<c>player-&gt;SendStarbaseSet(0, 0)</c> for the entering-starbase
    /// state with action=0, exit_mode=0).
    ///
    /// <para>
    /// Single-player single-sector scope. The only other
    /// <c>SendStarbaseSet</c> call site is:
    /// </para>
    /// <list type="bullet">
    ///   <item><c>SectorManager::LaunchIntoSpace</c> at
    ///     SectorManager.cpp:537 — <c>player-&gt;SendStarbaseSet(1, 0)</c>
    ///     for the leaving-starbase state, only fires when the player
    ///     undocks. Does NOT fire during a passive station login.</item>
    /// </list>
    /// <para>
    /// So 0x004F is exactly 1 emit per station-sector login. The
    /// captured count is deterministic at exactly 1. This is the
    /// SECOND manu-lab-arm-style single-emit pinning (after Wave 96
    /// CONSTANT_POSITIONAL_UPDATE) — both pin SectorManager-side
    /// single emitters rather than SendLoginShipData self-emits.
    /// </para>
    ///
    /// <para>
    /// Why a separate test method, not an in-place assertion. Wave 80's
    /// existing test caps at <c>Assert.NotEmpty + Assert.All(payload==6)</c>,
    /// which would still pass if a refactor added a spurious second
    /// emit. Keeping the count assertion in its own method preserves
    /// Wave 80's narrow-scope failure surface and gives Wave 97 a
    /// discrete test artifact for the regression-class catalogue.
    /// Mirrors the Wave 91-96 sibling-method pattern.
    /// </para>
    ///
    /// <para>
    /// Regression classes this catches (beyond what Wave 80 catches).
    /// </para>
    /// <list type="bullet">
    ///   <item>
    ///     <b>Spurious extra <c>SendStarbaseSet</c> in the chain.</b>
    ///     A refactor adding a SendLoginShipData self-emit or a
    ///     pre-handshake confirmation emit would produce 2+ frames.
    ///     Wave 80's <c>Assert.NotEmpty</c> still passes (every frame
    ///     is still 6B). Wave 97's <c>Assert.Single</c> catches.
    ///   </item>
    ///   <item>
    ///     <b>StationLogin2 split refactor that double-emits the
    ///     entering-starbase state.</b> A refactor that emits both
    ///     action=0 and action=1 during login (e.g. for a hypothetical
    ///     "reset-then-enter" handshake) would surface as 2+ frames.
    ///     Wave 97 catches.
    ///   </item>
    /// </list>
    ///
    /// <para>
    /// Per CLAUDE.md server-integrity. Pure passive-observation
    /// tightening. No client stimulus, no server change. The 1-frame
    /// invariant is a retail-faithful invariant of the StationLogin2
    /// entering-starbase-only dispatch pattern.
    /// </para>
    /// </summary>
    [Fact]
    public async Task StarbaseSet_EmittedExactlyOnceDuringStationSectorHandshake_PinsStationLogin2Emit()
    {
        var account = TestAccounts.For();
        const int slot = 0;
        const int stationSectorId = 10151;  // Terran Warrior start: Luna Station

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));

        var login = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(account.Username, account.Password), cts.Token);
        Assert.True(login.Valid, $"login: {login.RawBody.TrimEnd()}");
        Assert.False(string.IsNullOrEmpty(login.Ticket));

        await using var session = await SectorHandshake.EstablishAsync(
            _server, login.Ticket!, account.Username, slot, stationSectorId,
            firstName: "SbSet97", shipName: "SbSet97Ship", cts.Token);

        var starbaseSetFrames = session.HandshakeFrames
            .Where(f => f.Opcode == OpcodeId.Known.StarbaseSet.Value)
            .ToList();

        // Wave 97 pins the 1-frame invariant: StationLogin2 entering-starbase
        // emit (SectorManager.cpp:514). LaunchIntoSpace at SectorManager.cpp:537
        // does not fire during passive station login.
        var single = Assert.Single(starbaseSetFrames);
        Assert.Equal(ExpectedStarbaseSetPayloadLength, single.PayloadLength);
    }
}
