// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond preservation project.
// License: LICENSES/enb-emulator

using N7.CliClient.Auth;
using N7.CliClient.Opcodes;
using Xunit;

namespace N7.CliClient.IntegrationTests.Opcodes;

/// <summary>
/// Wave 81 hardening test (+0 ratchet, 0x00B2): pins the byte-exact
/// 48-byte payload shape of every 0x00B2 NAME_DECAL frame the server
/// emits during the station-sector login handshake stream.
///
/// <para>
/// Backstory. 0x00B2 NAME_DECAL is server-emitted by
/// <c>Player::SendNameDecal</c> at
/// <c>server/src/PlayerConnection.cpp:1187-1200</c>. The handler
/// constructs a stack-local <c>NameDecal</c> struct
/// (PacketStructures.h:462-467), zero-initialises it via memset,
/// populates <c>GameID = GameID()</c>, <c>RGB[0..2]</c> from
/// <c>m_Database.ship_data.ship_name_color</c>, and copies the
/// ship-name string into the 32-byte fixed-width <c>Name</c> field via
/// <c>strncpy_s</c> with explicit NUL terminator. It then emits via
/// <c>send_to-&gt;SendOpcode(ENB_OPCODE_00B2_NAME_DECAL, &amp;name_decal, sizeof(name_decal))</c>.
/// SendNameDecal is called from <c>Player::SendLoginShipData</c>
/// during the handshake fan-out. The wire size is computed via
/// <c>sizeof(name_decal)</c> against the <c>ATTRIB_PACKED</c> struct,
/// which evaluates to
/// <c>4 (int32_t GameID) + 32 (char Name[32]) + 12 (float RGB[3]) = 48</c>
/// bytes. The retail Win32 client was compiled to receive exactly this
/// 48-byte body.
/// </para>
///
/// <para>
/// Why a hardening test, not a +1 ratchet wave. 0x00B2 is already
/// counted by Wave 51
/// (<see cref="SectorHandshakeFanoutTests.HandshakeEmitsFullSendLoginShipDataFanout"/>) —
/// the passive-observation assertion that the opcode appears in the
/// handshake stream during a station-sector login. Wave 51's assertion
/// is opcode-presence only; it would still pass if the
/// <c>NameDecal</c> struct layout drifted (e.g. <c>GameID</c> widening
/// from <c>int32_t</c> back to <c>long</c> would add 4 bytes on LP64
/// Linux, <c>Name</c> widening from 32 to 64 bytes would add 32 bytes,
/// or <c>RGB</c> widening from <c>float[3]</c> to <c>double[3]</c>
/// would add 12 bytes). Wave 81 adds the byte-exact 48-byte
/// payload-length assertion the presence-only check cannot make,
/// locking the wire shape in place. +0 ratchet because 0x00B2 is
/// already counted; depth coverage of a regression class Wave 51 was
/// structurally blind to. Mirrors the Wave 67/71/76/77/78/79/80
/// pattern (byte-exact tightenings on already-counted handshake emits).
/// </para>
///
/// <para>
/// Wire shape and dispatch path. <c>SectorManager::HandleSectorLogin</c>
/// at <c>server/src/SectorManager.cpp:324-336</c> branches on
/// <c>m_SectorID</c> — sector IDs &gt; 9999 are stations (route to
/// <c>StationLogin</c> → <c>StationLogin2</c>). The station handshake
/// into Luna Station (10151) is the same 1-stage path Wave 51 / Wave
/// 67 / Wave 71 / Wave 76 / Wave 77 / Wave 78 / Wave 79 / Wave 80
/// exercise; Wave 81 reuses it without modification — same account
/// pool, same firstName / shipName payload, same drain loop — and just
/// adds the byte-exact length assertion on the captured 0x00B2 frames.
/// </para>
///
/// <para>
/// Regression classes this catches.
/// </para>
/// <list type="bullet">
///   <item>
///     <b>NameDecal struct layout regression in
///     <c>common/include/net7/PacketStructures.h:462-467</c>.</b>
///     <c>int32_t GameID; char Name[32]; float RGB[3]</c> with
///     <c>ATTRIB_PACKED</c>. A regression widening <c>GameID</c> back
///     to <c>long</c> would add 4 bytes on LP64 Linux (52 bytes
///     total). A regression widening <c>Name</c> from 32 to 64 bytes
///     (e.g. to accommodate longer ship names without truncation)
///     would add 32 bytes (80 bytes total). A regression widening
///     <c>RGB</c> from <c>float[3]</c> to <c>double[3]</c> would add
///     12 bytes (60 bytes total).
///   </item>
///   <item>
///     <b>SendNameDecal sizeof(name_decal) replacement at
///     <c>PlayerConnection.cpp:1199</c>.</b> A regression to a literal
///     constant (48) that drifts from the actual struct size, or to
///     <c>sizeof(long) + 32 + 12</c> on LP64 (52), would emit a
///     wrongly-sized frame.
///   </item>
///   <item>
///     <b>strncpy_s buffer-width drift at
///     <c>PlayerConnection.cpp:1196-1197</c>.</b> Currently
///     <c>strncpy_s(name_decal.Name, sizeof(name_decal.Name), ..., sizeof(name_decal.Name) - 1)</c>
///     with an explicit NUL terminator. A regression replacing
///     <c>sizeof(name_decal.Name)</c> with a literal that drifts from
///     32 wouldn't change the wire size (Name is still 32 bytes in
///     the struct) but a coincident struct change to widen Name would
///     propagate through.
///   </item>
///   <item>
///     <b>SendLoginShipData fanout chain truncation at
///     <c>PlayerClass.cpp</c>.</b> A regression that removes the
///     <c>SendNameDecal</c> call from the fan-out chain (or moves it
///     past the SendStart terminator) would drop 0x00B2 from the
///     handshake stream entirely — the <c>Assert.NotEmpty</c> filter
///     catches it before the length check fires.
///   </item>
///   <item>
///     <b>SendOpcode header-width revert at
///     <c>PlayerConnection.cpp:127</c>.</b> Would corrupt every inner
///     opcode in the 0x2016 PACKET_SEQUENCE parser; 0x00B2 wouldn't
///     appear under its correct label at all (so the
///     <c>Assert.NotEmpty</c> filter catches it before the length
///     check fires).
///   </item>
///   <item>
///     <b>Proxy SendClientPacketSequence inner-opcode guard tightening
///     at <c>proxy/UDPProxyToClient_linux.cpp:568</c>.</b> Currently
///     passes 0x00B2 (0x00B2 &lt; 0x0FFF). A regression to a tighter
///     upper bound would silently drop 0x00B2 from the wire — the
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
///     0x00B2 frame.
///   </item>
/// </list>
///
/// <para>
/// Primary source citation (CLAUDE.md server-integrity rule). 0x00B2
/// NAME_DECAL is server-originated. Wave 81 adds no client stimulus
/// and no server change — pure passive-observation tightening of a
/// retail-faithful wire shape. The 48-byte body is exactly what the
/// retail Win32 client's NAME_DECAL decoder was compiled to receive;
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
public sealed class SectorNameDecalHardeningTests
{
    /// <summary>
    /// 4 (int32 GameID) + 32 (char Name[32]) + 12 (float RGB[3]) = 48.
    /// Matches the wire size computed by <c>sizeof(NameDecal)</c>
    /// against the <c>ATTRIB_PACKED</c> struct at
    /// <c>common/include/net7/PacketStructures.h:462-467</c>.
    /// </summary>
    private const int ExpectedNameDecalPayloadLength = 48;

    private readonly ServerFixture _server;
    private readonly ClientFixture _client;

    public SectorNameDecalHardeningTests(ServerFixture server)
    {
        _server = server;
        _client = new ClientFixture(server);
    }

    [Fact]
    public async Task NameDecal_EmittedDuringStationSectorHandshake_HasExactly48BytePayload()
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
            firstName: "NmDecal81", shipName: "NmDecal81Ship", cts.Token);

        var nameDecalFrames = session.HandshakeFrames
            .Where(f => f.Opcode == OpcodeId.Known.NameDecal.Value)
            .ToList();

        Assert.NotEmpty(nameDecalFrames);
        Assert.All(nameDecalFrames, f =>
            Assert.Equal(ExpectedNameDecalPayloadLength, f.PayloadLength));
    }

    /// <summary>
    /// Wave 94 frame-count hardening (+0 ratchet, 0x00B2): pins the
    /// exact 1-frame emit-count invariant Wave 81's payload-length
    /// hardening was structurally blind to. The captured single-player
    /// station-sector handshake stream emits 0x00B2 NAME_DECAL exactly
    /// once — from <c>Player::SendLoginShipData</c> at
    /// <c>server/src/PlayerClass.cpp:892</c>
    /// (<c>SendNameDecal(this)</c> for the player-self name-decal setup).
    ///
    /// <para>
    /// Single-player single-sector scope. The other
    /// <c>Player::SendNameDecal</c> call site is:
    /// </para>
    /// <list type="bullet">
    ///   <item><c>Player::SendShipData</c> at PlayerClass.cpp:952 —
    ///     peer-broadcast fan-out, fires once per tracked observer.
    ///     No observers in single-player handshake.</item>
    /// </list>
    /// <para>
    /// Unlike 0x0004 CREATE (Wave 91) and 0x0089 RELATIONSHIP (Wave 92),
    /// the station-arm <c>SectorManager::StationLogin</c> does NOT call
    /// <c>SendNameDecal</c> for the manu-lab pseudo-object — only
    /// SendCreate and SendRelationship get the second emit. So 0x00B2
    /// is exactly 1 emit per single-player handshake, not 2. The
    /// captured count is deterministic at exactly 1. Mirrors the
    /// Wave 93 ADVANCED_POSITIONAL_UPDATE Assert.Single pattern.
    /// </para>
    ///
    /// <para>
    /// Why a separate test method, not an in-place assertion. Wave 81's
    /// existing test caps at <c>Assert.NotEmpty + Assert.All(payload==48)</c>,
    /// which would still pass if a refactor added a spurious second emit.
    /// Keeping the count assertion in its own method preserves Wave 81's
    /// narrow-scope failure surface and gives Wave 94 a discrete test
    /// artifact for the regression-class catalogue. Mirrors the
    /// Wave 91/92/93 sibling-method pattern.
    /// </para>
    ///
    /// <para>
    /// Regression classes this catches (beyond what Wave 81 catches).
    /// </para>
    /// <list type="bullet">
    ///   <item>
    ///     <b>Spurious extra <c>SendNameDecal</c> in the chain.</b> A
    ///     refactor that wrongly invokes the peer-broadcast emit on the
    ///     player-self (e.g. accidentally including the new player in
    ///     their own visibility list, or calling SendShipData with
    ///     include_player=true from a handshake-arm code path) would
    ///     produce 2+ frames. Wave 81's <c>Assert.NotEmpty</c> still
    ///     passes (every frame is still 48B). Wave 94's
    ///     <c>Assert.Single</c> catches.
    ///   </item>
    ///   <item>
    ///     <b>SendLoginShipData refactor that splits the self-emit into
    ///     a "before" and "after" name-decal emit.</b> A symmetric
    ///     refactor adding a pre-handshake or post-handshake emit (e.g.
    ///     for a hypothetical "rename confirm" step) would surface as
    ///     2+ frames. Wave 94 catches.
    ///   </item>
    /// </list>
    ///
    /// <para>
    /// Per CLAUDE.md server-integrity. Pure passive-observation
    /// tightening. No client stimulus, no server change. The 1-frame
    /// invariant is a retail-faithful invariant of the
    /// SendLoginShipData self-emit-only dispatch pattern (no manu-lab
    /// second emit for this opcode).
    /// </para>
    /// </summary>
    [Fact]
    public async Task NameDecal_EmittedExactlyOnceDuringStationSectorHandshake_PinsSelfEmit()
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
            firstName: "NmDecal94", shipName: "NmDecal94Ship", cts.Token);

        var nameDecalFrames = session.HandshakeFrames
            .Where(f => f.Opcode == OpcodeId.Known.NameDecal.Value)
            .ToList();

        // Wave 94 pins the 1-frame invariant: SendLoginShipData self
        // (PlayerClass.cpp:892). No manu-lab second emit, no observer
        // fan-out in single-player handshake.
        var single = Assert.Single(nameDecalFrames);
        Assert.Equal(ExpectedNameDecalPayloadLength, single.PayloadLength);
    }
}
