// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond preservation project.
// License: LICENSES/enb-emulator

using N7.CliClient.Auth;
using N7.CliClient.Opcodes;
using Xunit;

namespace N7.CliClient.IntegrationTests.Opcodes;

/// <summary>
/// Wave 85 hardening test (+0 ratchet, 0x0010): pins the byte-exact
/// 54-byte payload shape of every 0x0010 DECAL frame the server
/// emits during a station-arm sector-login handshake.
///
/// <para>
/// Backstory. 0x0010 DECAL is server-emitted by
/// <c>Player::SendDecal</c> at
/// <c>server/src/PlayerConnection.cpp:1162-1185</c>. The handler
/// builds a stack-local <c>Decal</c> struct, populates
/// <c>GameID</c> and <c>DecalCount = (short) decal_count</c>, then
/// fills <c>Item[0..decal_count-1]</c> with per-decal Index,
/// decal_id, HSV[3], opacity. The emit size is computed as a
/// trailing-array slice:
/// <c>size = ((char *) &amp;decal.Item[decal_count]) - ((char *) &amp;decal)</c>
/// then dispatched via
/// <c>SendOpcode(ENB_OPCODE_0010_DECAL, &amp;decal, size)</c>.
/// </para>
///
/// <para>
/// Why the 54-byte constant is wire-faithful here. Although
/// <c>Player::SendDecal</c> is a variable-length emitter in
/// principle (the trailing-array slice size depends on the
/// <c>decal_count</c> parameter, capped at <c>MAX_DECALS = 6</c>),
/// every caller in the handshake path passes <c>decal_count = 2</c>:
/// </para>
/// <list type="number">
///   <item>
///     <c>Player::SendLoginShipData</c> at
///     <c>server/src/PlayerClass.cpp:891</c>:
///     <c>SendDecal(GameID(), m_Database.ship_data.decal, 2)</c> —
///     self-emit to the newly-arrived player.
///   </item>
///   <item>
///     <c>Player::SendShipInfo</c> at
///     <c>server/src/PlayerClass.cpp:951</c>:
///     <c>player_to_send_to-&gt;SendDecal(GameID(), m_Database.ship_data.decal, 2)</c>
///     — fan-out emit to each observer.
///   </item>
/// </list>
/// <para>
/// The literal <c>2</c> in both call sites is load-bearing: the
/// retail Win32 client's DECAL decoder was compiled to ingest the
/// (GameID + DecalCount + 2 × DecalItem) trailing slice. A revert
/// at PlayerClass.cpp:891 or :951 to <c>MAX_DECALS</c> (6) would
/// emit a 150-byte body (6 + 6 × 24 = 150) and either over-fill
/// the client's decode buffer or mis-slot subsequent items.
/// </para>
///
/// <para>
/// Struct layout (PacketStructures.h:445-460):
/// </para>
/// <code>
/// struct DecalItem  // sizeof = 24 ATTRIB_PACKED
/// {
///     int32_t  Index;       // 4 bytes
///     int32_t  decal_id;    // 4 bytes
///     float    HSV[3];      // 12 bytes
///     float    opacity;     // 4 bytes
/// } ATTRIB_PACKED;
///
/// struct Decal
/// {
///     int32_t  GameID;             // 4 bytes
///     short    DecalCount;         // 2 bytes
///     DecalItem Item[MAX_DECALS];  // [up to 6 × 24] bytes — only the first DecalCount entries are emitted
/// } ATTRIB_PACKED;
/// </code>
/// <para>
/// Wire size for <c>decal_count = 2</c> =
/// <c>offsetof(Decal, Item[0])</c> (= 4 + 2 = 6 ATTRIB_PACKED) +
/// <c>2 × sizeof(DecalItem)</c> (= 2 × 24 = 48) = <b>54 bytes</b>.
/// ATTRIB_PACKED on <c>Decal</c> is load-bearing: without it the
/// natural-alignment hole between <c>short DecalCount</c> (offset
/// 4-5) and the int32 <c>Index</c> field of <c>Item[0]</c> (would
/// align to offset 8) inserts 2 padding bytes, growing <c>size</c>
/// to 56.
/// </para>
///
/// <para>
/// Why a hardening test, not a +1 ratchet wave. 0x0010 is already
/// counted by Wave 35
/// (<see cref="SectorHandshakeFanoutTests.HandshakeEmitsFullSendLoginShipDataFanout"/>)
/// which asserts presence-only via
/// <c>Assert.Contains(OpcodeId.Known.Decal.Value, session.HandshakeOpcodes)</c>.
/// Wave 85 tightens that to byte-exact: every captured 0x0010 frame
/// must carry exactly 54 bytes of payload. +0 ratchet because 0x0010
/// is already in TestedOpcodes; depth coverage of a regression class
/// the presence-only assertion is structurally blind to. Mirrors the
/// Wave 67/71/76/77/78/79/80/81/82/83/84 pattern (byte-exact
/// tightenings on already-counted handshake emits). Twelfth
/// hardening-pattern wave.
/// </para>
///
/// <para>
/// Multi-emit invariant. Mirrors Wave 84's
/// <c>Assert.NotEmpty + Assert.All</c> pattern: at least one 0x0010
/// frame must be captured, and every captured 0x0010 frame must
/// carry exactly 54 bytes. The two emit sites
/// (<c>SendLoginShipData</c>:891 self-emit, <c>SendShipInfo</c>:951
/// observer-emit) both pass <c>decal_count = 2</c> so both produce
/// the same 54-byte body — a regression at either site that changed
/// the count to MAX_DECALS would be caught by Assert.All on that
/// site's emit.
/// </para>
///
/// <para>
/// Regression classes this catches.
/// </para>
/// <list type="bullet">
///   <item>
///     <b>Decal struct layout drift in
///     <c>common/include/net7/PacketStructures.h:445-460</c>.</b>
///     A revert of <c>int32_t GameID</c> back to <c>long GameID</c>
///     adds 4 bytes on LP64 Linux (58-byte body); a revert of
///     <c>short DecalCount</c> back to <c>int DecalCount</c> adds 2
///     bytes (56-byte body); a revert of <c>int32_t Index</c> or
///     <c>int32_t decal_id</c> on DecalItem to <c>long</c> adds
///     4 bytes per emitted item (62 bytes for the 2-item emit); a
///     revert of <c>float</c> arrays to <c>double</c> doubles them.
///     The byte-exact 54-byte assertion catches every one.
///   </item>
///   <item>
///     <b>ATTRIB_PACKED removal at PacketStructures.h:451 or
///     :460.</b> Without packing on <c>Decal</c>, the
///     natural-alignment hole between <c>short DecalCount</c>
///     (offset 4-5) and <c>DecalItem.Index</c> (int32, aligns to
///     offset 8) inserts 2 padding bytes → 56-byte body. Without
///     packing on <c>DecalItem</c>, no alignment hole appears in
///     the current field order (4+4+12+4 = 24 already 4-aligned)
///     so this leg is currently size-stable but the assertion
///     pins it.
///   </item>
///   <item>
///     <b>SendDecal <c>decal_count = 2</c> literal-replacement at
///     PlayerClass.cpp:891 / :951.</b> A revert to
///     <c>MAX_DECALS</c> (6) would emit 6 + 6 × 24 = 150 bytes; a
///     drop to 1 would emit 6 + 1 × 24 = 30 bytes; a drop to 0
///     would emit 6 bytes (the GameID + DecalCount header only).
///     Any divergence from 2 changes the 54-byte invariant.
///   </item>
///   <item>
///     <b>SendDecal MAX_DECALS clamp removal at
///     PlayerConnection.cpp:1164-1167.</b> The clamp is what bounds
///     the trailing slice; its removal opens the door to a buffer
///     over-read if a future caller passes <c>decal_count &gt;
///     MAX_DECALS</c>. Wave 85 doesn't directly test the clamp
///     (both callers pass 2) but documents it as a load-bearing
///     guard for the wire shape.
///   </item>
///   <item>
///     <b>SendDecal size-arithmetic regression at
///     PlayerConnection.cpp:1182.</b> A refactor from
///     <c>((char *) &amp;decal.Item[decal_count]) - ((char *)
///     &amp;decal)</c> to <c>sizeof(decal)</c> would emit
///     6 + 6 × 24 = 150 bytes regardless of decal_count (the
///     full struct including all unused Item slots) — catastrophic
///     on the wire.
///   </item>
///   <item>
///     <b>SendDecal removal from the SendLoginShipData chain.</b>
///     If the SendDecal call at PlayerClass.cpp:891 is dropped,
///     <c>Assert.NotEmpty</c> fires (zero captured 0x0010 frames).
///   </item>
///   <item>
///     <b>SendOpcode header-width revert at
///     <c>PlayerConnection.cpp:127</c>.</b> Would corrupt every inner
///     opcode in the 0x2016 PACKET_SEQUENCE parser; 0x0010 wouldn't
///     appear under its correct label — <c>Assert.NotEmpty</c>
///     fires.
///   </item>
///   <item>
///     <b>Proxy SendClientPacketSequence inner-opcode guard tightening
///     at <c>proxy/UDPProxyToClient_linux.cpp:568</c>.</b> Currently
///     passes 0x0010 (0x0010 &lt; 0x0FFF). A regression to a tighter
///     upper bound that excluded 0x0010 would silently drop the
///     frame from the wire — <c>Assert.NotEmpty</c> fires.
///   </item>
///   <item>
///     <b>DoSectorLoginUntilStartAsync drain-loop payload-length
///     capture regression
///     (<c>Opcodes/SectorHandshake.cs:416</c>).</b> A future refactor
///     that drops the length field or under-counts payload bytes
///     specifically for 0x0010 would observe the wrong length on
///     every captured frame.
///   </item>
/// </list>
///
/// <para>
/// Primary source citation (CLAUDE.md server-integrity rule). 0x0010
/// DECAL is server-originated. Wave 85 adds no client stimulus and
/// no server change — pure passive-observation tightening of a
/// retail-faithful wire shape. The 54-byte body is exactly what the
/// retail Win32 client's DECAL decoder was compiled to receive for
/// the standard <c>decal_count = 2</c> handshake emit. No widened
/// input acceptance, no loosened gating, no fabricated replies —
/// server-integrity POSITIVE.
/// </para>
///
/// <para>
/// Budget: 60s. Single-stage station handshake into Luna Station
/// (10151) ~2s; assertions run synchronously against already-captured
/// state. No additional client stimulus.
/// </para>
/// </summary>
[Collection(ServerCollection.Name)]
public sealed class SectorDecalHardeningTests
{
    /// <summary>
    /// Trailing-array slice size for <c>decal_count = 2</c>:
    /// <c>offsetof(Decal, Item[0]) + 2 × sizeof(DecalItem)
    /// = (4 + 2) + 2 × 24 = 54 bytes</c> ATTRIB_PACKED. Matches the
    /// wire size computed by
    /// <c>((char *) &amp;decal.Item[decal_count]) - ((char *) &amp;decal)</c>
    /// at <c>server/src/PlayerConnection.cpp:1182</c> against the
    /// struct definitions at
    /// <c>common/include/net7/PacketStructures.h:445-460</c>. The
    /// literal <c>2</c> at the two handshake call sites
    /// (PlayerClass.cpp:891 and :951) is load-bearing — a revert to
    /// <c>MAX_DECALS</c> (6) would emit 150 bytes.
    /// </summary>
    private const int ExpectedDecalPayloadLength = 54;

    private readonly ServerFixture _server;
    private readonly ClientFixture _client;

    public SectorDecalHardeningTests(ServerFixture server)
    {
        _server = server;
        _client = new ClientFixture(server);
    }

    [Fact]
    public async Task Decal_EmittedDuringStationSectorHandshake_HasExactly54BytePayload()
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
            firstName: "Decal85", shipName: "Decal85Ship", cts.Token);

        var decalFrames = session.HandshakeFrames
            .Where(f => f.Opcode == OpcodeId.Known.Decal.Value)
            .ToList();

        Assert.NotEmpty(decalFrames);
        Assert.All(decalFrames, f =>
            Assert.Equal(ExpectedDecalPayloadLength, f.PayloadLength));
    }

    /// <summary>
    /// Wave 95 frame-count hardening (+0 ratchet, 0x0010): pins the
    /// exact 1-frame emit-count invariant Wave 85's payload-length
    /// hardening was structurally blind to. The captured single-player
    /// station-sector handshake stream emits 0x0010 DECAL exactly
    /// once — from <c>Player::SendLoginShipData</c> at
    /// <c>server/src/PlayerClass.cpp:891</c>
    /// (<c>SendDecal(GameID(), m_Database.ship_data.decal, 2)</c> for
    /// the player-self decal setup).
    ///
    /// <para>
    /// Single-player single-sector scope. The only other
    /// <c>Player::SendDecal</c> call site is:
    /// </para>
    /// <list type="bullet">
    ///   <item><c>Player::SendShipData</c> at PlayerClass.cpp:951 —
    ///     peer-broadcast fan-out, fires once per tracked observer.
    ///     No observers in single-player handshake.</item>
    /// </list>
    /// <para>
    /// Unlike 0x0004 CREATE (Wave 91) and 0x0089 RELATIONSHIP (Wave 92),
    /// the station-arm <c>SectorManager::StationLogin</c> does NOT call
    /// <c>SendDecal</c> for the manu-lab pseudo-object — only SendCreate
    /// and SendRelationship get the second emit. So 0x0010 is exactly
    /// 1 emit per single-player handshake, not 2. The captured count
    /// is deterministic at exactly 1. Mirrors the Wave 93/94
    /// Assert.Single pattern.
    /// </para>
    ///
    /// <para>
    /// Why a separate test method, not an in-place assertion. Wave 85's
    /// existing test caps at <c>Assert.NotEmpty + Assert.All(payload==54)</c>,
    /// which would still pass if a refactor added a spurious second
    /// emit. Keeping the count assertion in its own method preserves
    /// Wave 85's narrow-scope failure surface and gives Wave 95 a
    /// discrete test artifact for the regression-class catalogue.
    /// Mirrors the Wave 91/92/93/94 sibling-method pattern.
    /// </para>
    ///
    /// <para>
    /// Regression classes this catches (beyond what Wave 85 catches).
    /// </para>
    /// <list type="bullet">
    ///   <item>
    ///     <b>Spurious extra <c>SendDecal</c> in the chain.</b> A
    ///     refactor that wrongly invokes the peer-broadcast emit on
    ///     the player-self (e.g. accidentally including the new player
    ///     in their own visibility list, or calling SendShipData with
    ///     include_player=true from a handshake-arm code path) would
    ///     produce 2+ frames. Wave 85's <c>Assert.NotEmpty</c> still
    ///     passes (every frame is still 54B). Wave 95's
    ///     <c>Assert.Single</c> catches.
    ///   </item>
    ///   <item>
    ///     <b>SendLoginShipData refactor that splits the self-emit into
    ///     a "before" and "after" decal-set emit.</b> A symmetric
    ///     refactor adding a pre-handshake or post-handshake emit
    ///     (e.g. for a hypothetical "redecal confirm" step) would
    ///     surface as 2+ frames. Wave 95 catches.
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
    public async Task Decal_EmittedExactlyOnceDuringStationSectorHandshake_PinsSelfEmit()
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
            firstName: "Decal95", shipName: "Decal95Ship", cts.Token);

        var decalFrames = session.HandshakeFrames
            .Where(f => f.Opcode == OpcodeId.Known.Decal.Value)
            .ToList();

        // Wave 95 pins the 1-frame invariant: SendLoginShipData self
        // (PlayerClass.cpp:891). No manu-lab second emit, no observer
        // fan-out in single-player handshake.
        var single = Assert.Single(decalFrames);
        Assert.Equal(ExpectedDecalPayloadLength, single.PayloadLength);
    }
}
