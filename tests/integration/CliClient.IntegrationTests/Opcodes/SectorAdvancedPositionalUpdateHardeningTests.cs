// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond preservation project.
// License: LICENSES/enb-emulator

using N7.CliClient.Auth;
using N7.CliClient.Opcodes;
using Xunit;

namespace N7.CliClient.IntegrationTests.Opcodes;

/// <summary>
/// Wave 86 hardening test (+0 ratchet, 0x003E): pins the byte-exact
/// 42-byte payload shape of every 0x003E ADVANCED_POSITIONAL_UPDATE
/// frame the server emits during a station-arm sector-login
/// handshake (Bitmask = 0 — no conditional-field bits set).
///
/// <para>
/// Backstory. 0x003E ADVANCED_POSITIONAL_UPDATE is server-emitted by
/// <c>Player::SendAdvancedPositionalUpdate</c> at
/// <c>server/src/PlayerConnection.cpp:1317-1393</c>. The handler
/// builds a stack-local <c>char packet[sizeof(AdvancedPositionalUpdate)]</c>,
/// memset's it to zero, then manually packs fields via two pointer
/// aliases:
/// <c>int32_t *pLong = (int32_t *) &amp;packet[2]; float *pFloat =
/// (float *) &amp;packet[2];</c>. The <c>int32_t *pLong</c> stride
/// is load-bearing per the Wave 11 inline comment at
/// <c>PlayerConnection.cpp:1322-1325</c> — "wire slots are 4B each
/// (length is computed as <c>2 + 4 * index</c> below). <c>long *</c>
/// on Linux strides by 8B, which double-writes/overlaps every slot.
/// int32_t locks the stride to 4B."
/// </para>
///
/// <para>
/// Why the 42-byte constant is wire-faithful for handshake emits.
/// The handler's emit size is computed as
/// <c>int length = 2 + 4 * index;</c> at
/// <c>PlayerConnection.cpp:1384</c> where <c>index</c> counts
/// 4-byte slots populated. The mandatory 10 slots
/// (always-written regardless of bitmask) are:
/// </para>
/// <list type="number">
///   <item><c>pLong[0]</c>: <c>object_id</c> (GameID — narrowed from
///   <c>long</c> param to int32 via int32_t* alias)</item>
///   <item><c>pLong[1]</c>: <c>GetNet7TickCount()</c> (TimeStamp)</item>
///   <item><c>pFloat[2]</c>: <c>Position[0]</c></item>
///   <item><c>pFloat[3]</c>: <c>Position[1]</c></item>
///   <item><c>pFloat[4]</c>: <c>Position[2]</c></item>
///   <item><c>pFloat[5]</c>: <c>Orientation[0]</c></item>
///   <item><c>pFloat[6]</c>: <c>Orientation[1]</c></item>
///   <item><c>pFloat[7]</c>: <c>Orientation[2]</c></item>
///   <item><c>pFloat[8]</c>: <c>Orientation[3]</c></item>
///   <item><c>pLong[9]</c>: <c>position_info-&gt;MovementID</c></item>
/// </list>
/// <para>
/// Then up to 9 conditional fields are appended per <c>Bitmask</c>
/// bits 0..8: CurrentSpeed (0x0001), SetSpeed (0x0002),
/// Acceleration (0x0004), RotY (0x0008), DesiredY (0x0010),
/// RotZ (0x0020), DesiredZ (0x0040), ImpartedVelocity[3] +
/// ImpartedSpin + ImpartedRoll + ImpartedPitch (0x0080 — 6 slots),
/// UpdatePeriod (0x0100).
/// </para>
///
/// <para>
/// At handshake time the player's <c>m_Position_info.Bitmask</c> is
/// guaranteed to be 0:
/// </para>
/// <list type="bullet">
///   <item>
///     <c>Moveable::Moveable</c> at
///     <c>server/src/Moveable.cpp:12</c> memsets
///     <c>m_Position_info</c> to zero on construction (load-bearing
///     fresh-player invariant).
///   </item>
///   <item>
///     The fresh player has not yet completed any
///     <c>UpdateLocation</c> tick (called per movement-state evaluation
///     at <c>Moveable.cpp:128-235</c>) — the handshake-time emit fires
///     before any tick has ORed in 0x03/0x04/0x07/0x29/0x01 from
///     movement state.
///   </item>
/// </list>
/// <para>
/// With Bitmask = 0, length = <c>2 (bitmask) + 4 × 10 (mandatory
/// slots) = 42 bytes</c>. This is exactly what the retail Win32
/// client's ADVANCED_POSITIONAL_UPDATE decoder was compiled to
/// receive for a stationary observer-setup emit.
/// </para>
///
/// <para>
/// Emission sites in the station-arm handshake. Both 0x003E callers
/// on the handshake path pass the fresh player's own
/// <c>&amp;m_Position_info</c>:
/// </para>
/// <list type="number">
///   <item>
///     <c>Player::SendLoginShipData</c> at
///     <c>server/src/PlayerClass.cpp:896</c>:
///     <c>SendAdvancedPositionalUpdate(GameID(), &amp;m_Position_info)</c>
///     — self-emit to the newly-arrived player.
///   </item>
///   <item>
///     <c>Player::SendShipInfo</c> at
///     <c>server/src/PlayerClass.cpp:970</c>:
///     <c>player-&gt;SendAdvancedPositionalUpdate(GameID(), &amp;m_Position_info)</c>
///     — fan-out emit to each observer in the per-observer SendShipInfo
///     loop.
///   </item>
/// </list>
/// <para>
/// Both ride the same <c>SendAdvancedPositionalUpdate</c> serialiser
/// with Bitmask = 0; both emit the identical 42-byte body.
/// </para>
///
/// <para>
/// Why a hardening test, not a +1 ratchet wave. 0x003E is already
/// counted by Wave 35
/// (<see cref="SectorHandshakeFanoutTests.HandshakeEmitsFullSendLoginShipDataFanout"/>)
/// which asserts presence-only via
/// <c>Assert.Contains(OpcodeId.Known.AdvancedPositionalUpdate.Value, session.HandshakeOpcodes)</c>.
/// Wave 86 tightens that to byte-exact. +0 ratchet because 0x003E
/// is already in TestedOpcodes; depth coverage of a regression
/// class the presence-only assertion is structurally blind to.
/// Mirrors the Wave 67/71/76/77/78/79/80/81/82/83/84/85 pattern.
/// Thirteenth hardening-pattern wave.
/// </para>
///
/// <para>
/// Multi-emit invariant. Mirrors Wave 84/85's
/// <c>Assert.NotEmpty + Assert.All</c> pattern: at least one 0x003E
/// frame must be captured, and every captured 0x003E frame must
/// carry exactly 42 bytes.
/// </para>
///
/// <para>
/// Regression classes this catches.
/// </para>
/// <list type="bullet">
///   <item>
///     <b>SendAdvancedPositionalUpdate <c>int32_t *pLong</c> revert
///     at <c>PlayerConnection.cpp:1325</c>.</b> The Wave 11 inline
///     comment at PlayerConnection.cpp:1322-1325 explicitly documents
///     this regression class: a revert to <c>long *pLong</c> strides
///     by 8B on LP64 Linux, double-writing each slot and emitting an
///     observation-time mid-handshake catastrophic-corruption frame.
///     The 42-byte assertion catches the size-arithmetic side of
///     the same regression (length stays at 42 because <c>index</c>
///     is unchanged, but the wire content is mangled — a downstream
///     typed-codec wave would catch the content side).
///   </item>
///   <item>
///     <b>SendAdvancedPositionalUpdate length-formula drift at
///     <c>PlayerConnection.cpp:1384</c>.</b> A refactor from
///     <c>int length = 2 + 4 * index</c> to <c>sizeof(packet)</c>
///     would emit the full sizeof(AdvancedPositionalUpdate) struct
///     including all unused conditional slots — likely 60+ bytes
///     against the struct layout at PacketStructures.h:906-927.
///   </item>
///   <item>
///     <b>AdvancedPositionalUpdate struct layout drift in
///     <c>common/include/net7/PacketStructures.h:906-927</c>.</b>
///     The struct definition isn't directly emitted (the handler
///     uses raw packet-array packing) but its <c>sizeof</c> is the
///     local-buffer size; a field-width revert that grew
///     <c>sizeof(AdvancedPositionalUpdate)</c> wouldn't change the
///     wire size directly but a future refactor that aligned the
///     handler's packing with the struct would surface the
///     divergence. Wave 86 pins the wire-side invariant; the struct
///     side is documented at PacketStructures.h:906-927 for future
///     hardening (e.g. a Wave that asserts
///     <c>sizeof(AdvancedPositionalUpdate) == 60</c> bytes via a
///     static_assert in a server build).
///   </item>
///   <item>
///     <b>Moveable constructor memset removal at
///     <c>Moveable.cpp:12</c>.</b> The <c>memset(&amp;m_Position_info,
///     0, sizeof(m_Position_info))</c> is what guarantees Bitmask = 0
///     at handshake time. Its removal opens uninitialised-stack
///     reads through into the wire emit — non-deterministic length
///     between 42 and 86 bytes per call. The byte-exact 42-byte
///     assertion fails non-deterministically on any non-zero
///     bitmask bits.
///   </item>
///   <item>
///     <b>Premature UpdateLocation tick at handshake time.</b> If a
///     future SectorLogin2 refactor inserts an UpdateLocation() call
///     into the handshake path before SendAdvancedPositionalUpdate
///     fires, the Bitmask is set to 0x00 at Moveable.cpp:132 first,
///     then may be OR'd to non-zero by the movement-state-dependent
///     branches. For a stationary fresh player (m_MovementState = 0,
///     m_CurrentSpeed = 0) the Bitmask remains 0 even after
///     UpdateLocation, so this is a latent risk rather than a current
///     failure. Documented for future-developer awareness.
///   </item>
///   <item>
///     <b>SendLoginShipData / SendShipInfo m_Position_info pointer
///     drift.</b> A refactor that passed a non-fresh
///     PositionInformation (e.g. from an in-progress NPC's
///     m_Position_info with non-zero Bitmask) would emit a
///     non-42-byte frame.
///   </item>
///   <item>
///     <b>SendAdvancedPositionalUpdate removal from the handshake
///     chain.</b> <c>Assert.NotEmpty</c> fires (zero captured 0x003E
///     frames).
///   </item>
///   <item>
///     <b>SendOpcode header-width revert at
///     <c>PlayerConnection.cpp:127</c>.</b> Would corrupt every inner
///     opcode in the 0x2016 PACKET_SEQUENCE parser; 0x003E wouldn't
///     appear under its correct label — <c>Assert.NotEmpty</c>
///     fires.
///   </item>
///   <item>
///     <b>Proxy SendClientPacketSequence inner-opcode guard tightening
///     at <c>proxy/UDPProxyToClient_linux.cpp:568</c>.</b> Currently
///     passes 0x003E (0x003E &lt; 0x0FFF). A regression to a tighter
///     upper bound that excluded 0x003E would silently drop the
///     frame from the wire — <c>Assert.NotEmpty</c> fires.
///   </item>
///   <item>
///     <b>DoSectorLoginUntilStartAsync drain-loop payload-length
///     capture regression
///     (<c>Opcodes/SectorHandshake.cs:416</c>).</b> A future refactor
///     that drops the length field or under-counts payload bytes
///     specifically for 0x003E would observe the wrong length on
///     every captured frame.
///   </item>
/// </list>
///
/// <para>
/// Primary source citation (CLAUDE.md server-integrity rule). 0x003E
/// ADVANCED_POSITIONAL_UPDATE is server-originated. Wave 86 adds no
/// client stimulus and no server change — pure passive-observation
/// tightening of a retail-faithful wire shape. The 42-byte
/// Bitmask=0 emit is exactly what the retail Win32 client's
/// ADVANCED_POSITIONAL_UPDATE decoder was compiled to receive for
/// a stationary observer-setup emit. No widened input acceptance,
/// no loosened gating, no fabricated replies — server-integrity
/// POSITIVE. Wave 11's inline comment at PlayerConnection.cpp:1322-1325
/// is the primary-source citation for the 4-byte-per-slot stride
/// invariant Wave 86 locks in place.
/// </para>
///
/// <para>
/// Budget: 60s. Single-stage station handshake into Luna Station
/// (10151) ~2s; assertions run synchronously against already-captured
/// state. No additional client stimulus.
/// </para>
/// </summary>
[Collection(ServerCollection.Name)]
public sealed class SectorAdvancedPositionalUpdateHardeningTests
{
    /// <summary>
    /// Length = <c>2 + 4 × 10 = 42 bytes</c> for Bitmask = 0.
    /// Matches the wire size computed by
    /// <c>int length = 2 + 4 * index</c> at
    /// <c>server/src/PlayerConnection.cpp:1384</c> after the
    /// mandatory 10 always-written slots (object_id, TimeStamp,
    /// Position[3], Orientation[4], MovementID). At handshake time
    /// the fresh player's <c>m_Position_info.Bitmask</c> is
    /// guaranteed to be 0 by the Moveable constructor's
    /// <c>memset(&amp;m_Position_info, 0, sizeof(m_Position_info))</c>
    /// at <c>server/src/Moveable.cpp:12</c>, and no UpdateLocation()
    /// tick fires before the handshake emit. The retail Win32 client
    /// was compiled with the same 4-byte-per-slot stride (the
    /// <c>int32_t *pLong</c> at PlayerConnection.cpp:1325 is
    /// Wave 11's explicit narrowing of the original <c>long *</c>
    /// to match retail wire layout).
    /// </summary>
    private const int ExpectedAdvancedPositionalUpdatePayloadLength = 42;

    private readonly ServerFixture _server;
    private readonly ClientFixture _client;

    public SectorAdvancedPositionalUpdateHardeningTests(ServerFixture server)
    {
        _server = server;
        _client = new ClientFixture(server);
    }

    [Fact]
    public async Task AdvancedPositionalUpdate_EmittedDuringStationSectorHandshake_HasExactly42BytePayload()
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
            firstName: "AdvPos86", shipName: "AdvPos86Ship", cts.Token);

        var advancedPositionalUpdateFrames = session.HandshakeFrames
            .Where(f => f.Opcode == OpcodeId.Known.AdvancedPositionalUpdate.Value)
            .ToList();

        Assert.NotEmpty(advancedPositionalUpdateFrames);
        Assert.All(advancedPositionalUpdateFrames, f =>
            Assert.Equal(ExpectedAdvancedPositionalUpdatePayloadLength, f.PayloadLength));
    }

    /// <summary>
    /// Wave 93 frame-count hardening (+0 ratchet, 0x003E): pins the
    /// exact 1-frame emit-count invariant Wave 86's payload-length
    /// hardening was structurally blind to. The captured single-player
    /// station-sector handshake stream emits 0x003E
    /// ADVANCED_POSITIONAL_UPDATE exactly once — from
    /// <c>Player::SendLoginShipData</c> at
    /// <c>server/src/PlayerClass.cpp:896</c>
    /// (<c>SendAdvancedPositionalUpdate(GameID(), &amp;m_Position_info)</c>
    /// for the player-self positional setup).
    ///
    /// <para>
    /// Single-player single-sector scope. The other
    /// <c>Player::SendAdvancedPositionalUpdate</c> call sites are:
    /// </para>
    /// <list type="bullet">
    ///   <item><c>Player::SendShipData</c> at PlayerClass.cpp:970 —
    ///     peer-broadcast fan-out, fires once per tracked observer.
    ///     No observers in single-player handshake.</item>
    ///   <item><c>Player::SendStartMovementRefresh</c> at
    ///     PlayerClass.cpp:2018 — triggered by movement events.
    ///     No movement during passive handshake.</item>
    ///   <item><c>Player::SendLocationAndSpeed</c> at PlayerClass.cpp:2074 —
    ///     triggered by location/speed updates.
    ///     No location updates during passive handshake.</item>
    ///   <item><c>Player::SendToVisibilityList</c> at PlayerClass.cpp:3115
    ///     and 3138 — peer-visibility broadcast fan-out.
    ///     No visibility-list members in single-player handshake.</item>
    /// </list>
    /// <para>
    /// Unlike 0x0004 CREATE (Wave 91) and 0x0089 RELATIONSHIP
    /// (Wave 92), the station-arm <c>SectorManager::StationLogin</c>
    /// does NOT call <c>SendAdvancedPositionalUpdate</c> for the
    /// manu-lab pseudo-object — only SendCreate and SendRelationship
    /// get the second emit. So 0x003E is exactly 1 emit per
    /// single-player handshake, not 2. The captured count is
    /// deterministic at exactly 1.
    /// </para>
    ///
    /// <para>
    /// Why a separate test method, not an in-place assertion. Wave 86's
    /// existing test caps at <c>Assert.NotEmpty + Assert.All(payload==42)</c>,
    /// which would still pass if a refactor added a spurious second
    /// emit. Keeping the count assertion in its own method preserves
    /// Wave 86's narrow-scope failure surface and gives Wave 93 a
    /// discrete test artifact for the regression-class catalogue.
    /// Mirrors the Wave 91/92 sibling-method pattern.
    /// </para>
    ///
    /// <para>
    /// Regression classes this catches (beyond what Wave 86 catches).
    /// </para>
    /// <list type="bullet">
    ///   <item>
    ///     <b>Spurious extra <c>SendAdvancedPositionalUpdate</c> in
    ///     the chain.</b> A refactor that wrongly invokes the
    ///     peer-broadcast emit on the player-self (e.g. accidentally
    ///     including the new player in their own visibility list, or
    ///     calling SendShipData with include_player=true from a
    ///     handshake-arm code path) would produce 2+ frames. Wave 86's
    ///     <c>Assert.NotEmpty</c> still passes (every frame is still
    ///     42B). Wave 93's <c>Assert.Single</c> catches.
    ///   </item>
    ///   <item>
    ///     <b>SendLoginShipData refactor that splits the self-emit into
    ///     a "before" and "after" reposition emit.</b> A symmetric
    ///     refactor adding a pre-handshake or post-handshake emit
    ///     (e.g. for a hypothetical "starting position confirm" step)
    ///     would surface as 2+ frames. Wave 93 catches.
    ///   </item>
    /// </list>
    ///
    /// <para>
    /// Per CLAUDE.md server-integrity. Pure passive-observation
    /// tightening. No client stimulus, no server change. The 1-frame
    /// invariant is a retail-faithful invariant of the
    /// SendLoginShipData self-emit-only dispatch pattern (no
    /// manu-lab second emit for this opcode).
    /// </para>
    /// </summary>
    [Fact]
    public async Task AdvancedPositionalUpdate_EmittedExactlyOnceDuringStationSectorHandshake_PinsSelfEmit()
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
            firstName: "AdvPos93", shipName: "AdvPos93Ship", cts.Token);

        var advancedPositionalUpdateFrames = session.HandshakeFrames
            .Where(f => f.Opcode == OpcodeId.Known.AdvancedPositionalUpdate.Value)
            .ToList();

        // Wave 93 pins the 1-frame invariant: SendLoginShipData self
        // (PlayerClass.cpp:896). No manu-lab second emit, no observer
        // fan-out in single-player handshake.
        var single = Assert.Single(advancedPositionalUpdateFrames);
        Assert.Equal(ExpectedAdvancedPositionalUpdatePayloadLength, single.PayloadLength);
    }
}
