// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond preservation project.
// License: LICENSES/enb-emulator

using N7.CliClient.Auth;
using N7.CliClient.Opcodes;
using Xunit;

namespace N7.CliClient.IntegrationTests.Opcodes;

/// <summary>
/// Wave 79 hardening test (+0 ratchet, 0x0089): pins the byte-exact
/// 9-byte payload shape of every 0x0089 RELATIONSHIP frame the server
/// emits during the station-sector login handshake stream.
///
/// <para>
/// Backstory. 0x0089 RELATIONSHIP is server-emitted by
/// <c>Player::SendRelationship</c> at
/// <c>server/src/PlayerConnection.cpp:10703-10712</c>. The handler
/// constructs a stack-local <c>Relationship</c> struct
/// (PacketStructures.h:824-829), populates
/// <c>ObjectID = ntohl(ObjectID)</c>, <c>Reaction = Reaction</c>,
/// <c>IsAttacking = (IsAttacking ? 1 : 0)</c>, and emits via
/// <c>SendOpcode(ENB_OPCODE_0089_RELATIONSHIP, &amp;response, sizeof(response))</c>.
/// SendRelationship is called from <c>Player::SendLoginShipData</c> at
/// <c>PlayerClass.cpp:893</c> as
/// <c>SendRelationship(GameID(), RELATIONSHIP_FRIENDLY, false)</c>,
/// and from the peer-broadcast <c>Player::SendShipData</c> at
/// <c>PlayerClass.cpp:953</c>. The wire size is computed via
/// <c>sizeof(response)</c> against the <c>ATTRIB_PACKED</c>
/// <c>Relationship</c> struct, which evaluates to
/// <c>4 (int32_t ObjectID) + 4 (int32_t Reaction) + 1 (char IsAttacking) = 9</c>
/// bytes. The retail Win32 client was compiled to receive exactly this
/// 9-byte body.
/// </para>
///
/// <para>
/// Why a hardening test, not a +1 ratchet wave. 0x0089 is already
/// counted by Wave 51
/// (<see cref="SectorHandshakeFanoutTests.HandshakeEmitsFullSendLoginShipDataFanout"/>) —
/// the passive-observation assertion that the opcode appears in the
/// handshake stream during a station-sector login. Wave 51's assertion
/// is opcode-presence only; it would still pass if the
/// <c>Relationship</c> struct layout drifted (e.g. <c>ObjectID</c> or
/// <c>Reaction</c> widening from <c>int32_t</c> back to <c>long</c>
/// would add 4 bytes per field on LP64 Linux, or the
/// <c>IsAttacking</c> field widening from <c>char</c> to
/// <c>int32_t</c> would add 3 bytes). Wave 79 adds the byte-exact
/// 9-byte payload-length assertion the presence-only check cannot
/// make, locking the wire shape in place. +0 ratchet because 0x0089
/// is already counted; depth coverage of a regression class Wave 51
/// was structurally blind to. Mirrors the Wave 67/71/76/77/78 pattern
/// (byte-exact tightenings on already-counted handshake emits).
/// </para>
///
/// <para>
/// Wire shape and dispatch path. <c>SectorManager::HandleSectorLogin</c>
/// at <c>server/src/SectorManager.cpp:324-336</c> branches on
/// <c>m_SectorID</c> — sector IDs &gt; 9999 are stations (route to
/// <c>StationLogin</c> → <c>StationLogin2</c>), which calls
/// <c>SendLoginShipData</c> (<c>PlayerClass.cpp:855</c>). That function
/// dispatches the per-ship fanout chain, and after the Decal and
/// NameDecal emits, calls
/// <c>SendRelationship(GameID(), RELATIONSHIP_FRIENDLY, false)</c> at
/// <c>PlayerClass.cpp:893</c>. The station handshake into Luna Station
/// (10151) is the same 1-stage path Wave 51 exercises; Wave 79 reuses
/// it without modification — same account pool, same firstName /
/// shipName payload, same drain loop — and just adds the byte-exact
/// length assertion on the captured 0x0089 frames.
/// </para>
///
/// <para>
/// Regression classes this catches.
/// </para>
/// <list type="bullet">
///   <item>
///     <b>Relationship struct layout regression in
///     <c>common/include/net7/PacketStructures.h:824-829</c>.</b>
///     <c>int32_t ObjectID; int32_t Reaction; char IsAttacking</c>
///     with <c>ATTRIB_PACKED</c>. A regression widening
///     <c>ObjectID</c> or <c>Reaction</c> back to <c>long</c> would
///     add 4 bytes per field on LP64 Linux (13 or 17 bytes total). A
///     regression widening <c>IsAttacking</c> from <c>char</c> to
///     <c>int32_t</c> would add 3 bytes (12 bytes total). The
///     pre-migration ObjectID/Reaction longs would have walked the
///     proxy's PACKET_SEQUENCE inner-tuple parser off the next inner
///     [len][opcode] tuple.
///   </item>
///   <item>
///     <b>SendRelationship sizeof(response) replacement at
///     <c>PlayerConnection.cpp:10711</c>.</b> A regression to a
///     literal constant (9) that drifts from the actual struct size,
///     or to <c>sizeof(long) * 2 + 1</c> on LP64 (17), would emit a
///     wrongly-sized frame.
///   </item>
///   <item>
///     <b>SendRelationship ntohl byte-order semantics.</b>
///     <c>response.ObjectID = ntohl(ObjectID)</c> at
///     PlayerConnection.cpp:10706 byte-swaps a host-order value. The
///     retail server's quirk; a regression that removed the ntohl
///     would change the wire bytes (not the length), so this test
///     wouldn't catch it directly — documented as a known limitation,
///     a future ChatStream-style payload-content assertion wave would
///     tighten further.
///   </item>
///   <item>
///     <b>SendLoginShipData fanout chain truncation at
///     <c>PlayerClass.cpp:893</c>.</b> A regression that removes the
///     <c>SendRelationship(GameID(), RELATIONSHIP_FRIENDLY, false)</c>
///     call from the SendLoginShipData chain (or moves it past the
///     chain terminator) would drop 0x0089 from the handshake stream
///     entirely.
///   </item>
///   <item>
///     <b>SendOpcode header-width revert at
///     <c>PlayerConnection.cpp:127</c>.</b> Would corrupt every inner
///     opcode in the 0x2016 PACKET_SEQUENCE parser; 0x0089 wouldn't
///     appear under its correct label at all (so the
///     <c>Assert.NotEmpty</c> filter catches it before the length
///     check fires).
///   </item>
///   <item>
///     <b>Proxy SendClientPacketSequence inner-opcode guard tightening
///     at <c>proxy/UDPProxyToClient_linux.cpp:568</c>.</b> Currently
///     passes 0x0089 (0x0089 &lt; 0x0FFF). A regression to a tighter
///     upper bound would silently drop 0x0089 from the wire — the
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
///     0x0089 frame.
///   </item>
/// </list>
///
/// <para>
/// Primary source citation (CLAUDE.md server-integrity rule). 0x0089
/// RELATIONSHIP is server-originated. Wave 79 adds no client stimulus
/// and no server change — pure passive-observation tightening of a
/// retail-faithful wire shape. The 9-byte body is exactly what the
/// retail Win32 client's RELATIONSHIP decoder was compiled to receive;
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
public sealed class SectorRelationshipHardeningTests
{
    /// <summary>
    /// 4 (int32 ObjectID) + 4 (int32 Reaction) + 1 (char IsAttacking) = 9.
    /// Matches the wire size computed by <c>sizeof(Relationship)</c>
    /// against the <c>ATTRIB_PACKED</c> struct at
    /// <c>common/include/net7/PacketStructures.h:824-829</c>.
    /// </summary>
    private const int ExpectedRelationshipPayloadLength = 9;

    private readonly ServerFixture _server;
    private readonly ClientFixture _client;

    public SectorRelationshipHardeningTests(ServerFixture server)
    {
        _server = server;
        _client = new ClientFixture(server);
    }

    [Fact]
    public async Task Relationship_EmittedDuringStationSectorHandshake_HasExactly9BytePayload()
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
            firstName: "Rel79", shipName: "Rel79Ship", cts.Token);

        var relationshipFrames = session.HandshakeFrames
            .Where(f => f.Opcode == OpcodeId.Known.Relationship.Value)
            .ToList();

        Assert.NotEmpty(relationshipFrames);
        Assert.All(relationshipFrames, f =>
            Assert.Equal(ExpectedRelationshipPayloadLength, f.PayloadLength));
    }

    /// <summary>
    /// Wave 92 frame-count hardening (+0 ratchet, 0x0089): pins the
    /// exact 2-frame emit-count invariant Wave 79's xmldoc flagged
    /// implicitly via the two named dispatch sites. The captured
    /// station-sector handshake stream emits 0x0089 RELATIONSHIP
    /// exactly twice — once from <c>Player::SendLoginShipData</c> at
    /// <c>server/src/PlayerClass.cpp:893</c> (the player-self
    /// relationship, <c>SendRelationship(GameID(), RELATIONSHIP_FRIENDLY,
    /// false)</c>); once from <c>SectorManager::StationLogin</c> at
    /// <c>server/src/SectorManager.cpp:479</c> (the manufacture-lab
    /// pseudo-object relationship,
    /// <c>player-&gt;SendRelationship(ManuID, RELATIONSHIP_FRIENDLY,
    /// false)</c>).
    ///
    /// <para>
    /// Why a separate test method, not an in-place assertion. Wave 79's
    /// existing test caps at <c>Assert.NotEmpty + Assert.All(payload==9)</c>
    /// and the xmldoc documents the two named dispatch sites without
    /// pinning the count. Keeping the count assertion in its own method
    /// preserves Wave 79's narrow-scope failure surface (a count
    /// regression would not surface as a payload-length failure, and
    /// vice versa) and gives Wave 92 a discrete test artifact for the
    /// regression-class catalogue. Mirrors the Wave 91 pattern that
    /// followed Wave 84 for 0x0004 CREATE.
    /// </para>
    ///
    /// <para>
    /// Single-player single-sector scope. The third
    /// <c>Player::SendRelationship</c> call site at
    /// <c>server/src/PlayerClass.cpp:953</c> is inside
    /// <c>Player::SendShipData</c>, which is the peer-broadcast fan-out
    /// invoked when another player's ship enters the observer's tracked
    /// set. In the single-player station handshake the test exercises
    /// (one account logging into an empty Luna Station), no other
    /// players are tracked, so that emit path does not fire. The
    /// captured count is therefore deterministic at exactly 2.
    /// </para>
    ///
    /// <para>
    /// Regression classes this catches (beyond what Wave 79 catches).
    /// </para>
    /// <list type="bullet">
    ///   <item>
    ///     <b><c>SendLoginShipData</c> self-emit deletion at
    ///     <c>server/src/PlayerClass.cpp:893</c>.</b> Removing the
    ///     <c>SendRelationship(GameID(), RELATIONSHIP_FRIENDLY, false)</c>
    ///     call would drop the player-self relationship. Wave 79's
    ///     <c>Assert.NotEmpty</c> would still pass (the StationLogin
    ///     manu-lab relationship remains) and
    ///     <c>Assert.All(payload==9)</c> would still pass (the
    ///     remaining frame is still 9B). Wave 92's
    ///     <c>Assert.Equal(2, count)</c> catches.
    ///   </item>
    ///   <item>
    ///     <b><c>StationLogin</c> manu-lab emit deletion at
    ///     <c>server/src/SectorManager.cpp:479</c>.</b> Removing the
    ///     <c>player-&gt;SendRelationship(ManuID, RELATIONSHIP_FRIENDLY,
    ///     false)</c> would drop the manufacture-lab relationship. Same
    ///     observability gap as the self-emit deletion above — Wave 79
    ///     wouldn't surface it but Wave 92's count check catches.
    ///   </item>
    ///   <item>
    ///     <b>Spurious extra <c>SendRelationship</c> in the chain.</b>
    ///     A refactor that adds a third <c>SendRelationship</c> call
    ///     (e.g. wrongly broadcasting an observed-object relationship
    ///     during single-player handshake) would produce 3+ frames;
    ///     <c>Assert.Equal(2, count)</c> catches.
    ///   </item>
    /// </list>
    ///
    /// <para>
    /// Per CLAUDE.md server-integrity. Pure passive-observation
    /// tightening. No client stimulus, no server change. The 2-frame
    /// invariant is a retail-faithful invariant of the station-sector
    /// SendLoginShipData + StationLogin dispatch pair.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Relationship_EmittedExactlyTwiceDuringStationSectorHandshake_PinsSelfAndManuLabEmits()
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
            firstName: "Rel92", shipName: "Rel92Ship", cts.Token);

        var relationshipFrames = session.HandshakeFrames
            .Where(f => f.Opcode == OpcodeId.Known.Relationship.Value)
            .ToList();

        // Wave 92 pins the 2-frame invariant: SendLoginShipData (self
        // RELATIONSHIP_FRIENDLY at PlayerClass.cpp:893) + StationLogin
        // (ManuID RELATIONSHIP_FRIENDLY at SectorManager.cpp:479).
        Assert.Equal(2, relationshipFrames.Count);
        Assert.All(relationshipFrames, f =>
            Assert.Equal(ExpectedRelationshipPayloadLength, f.PayloadLength));
    }
}
