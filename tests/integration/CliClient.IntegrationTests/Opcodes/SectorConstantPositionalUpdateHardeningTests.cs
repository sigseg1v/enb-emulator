// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond preservation project.
// License: LICENSES/enb-emulator

using N7.CliClient.Auth;
using N7.CliClient.Opcodes;
using Xunit;

namespace N7.CliClient.IntegrationTests.Opcodes;

/// <summary>
/// Wave 82 hardening test (+0 ratchet, 0x0040): pins the byte-exact
/// 32-byte payload shape of every 0x0040 CONSTANT_POSITIONAL_UPDATE
/// frame the server emits during the station-sector login handshake
/// stream.
///
/// <para>
/// Backstory. 0x0040 CONSTANT_POSITIONAL_UPDATE is server-emitted by
/// <c>Player::SendConstantPositionalUpdate</c> at
/// <c>server/src/PlayerConnection.cpp:1202-1220</c>. The handler
/// constructs a stack-local <c>ConstantPositionalUpdate</c> struct
/// (PacketStructures.h:514-519), zero-initialises it via memset,
/// populates <c>GameID = game_id</c>, <c>Position[0..2]</c> from the
/// (x,y,z) parameters, and optionally <c>Orientation[0..3]</c> when
/// the caller supplies one. It then emits via
/// <c>SendOpcode(ENB_OPCODE_0040_CONSTANT_POSITIONAL_UPDATE, &amp;update, sizeof(update))</c>.
/// SendConstantPositionalUpdate is called from
/// <c>SectorManager::StationLogin</c> at <c>SectorManager.cpp:480</c>
/// as <c>player-&gt;SendConstantPositionalUpdate(ManuID, 0, 0, 0)</c>
/// — the manufacturing-lab pseudo-object position. The wire size is
/// computed via <c>sizeof(update)</c> against the <c>ATTRIB_PACKED</c>
/// struct, which evaluates to
/// <c>4 (int32_t GameID) + 12 (float Position[3]) + 16 (float Orientation[4]) = 32</c>
/// bytes. The retail Win32 client was compiled to receive exactly this
/// 32-byte body.
/// </para>
///
/// <para>
/// Why a hardening test, not a +1 ratchet wave. 0x0040 is already
/// counted by Wave 51
/// (<see cref="SectorHandshakeFanoutTests.HandshakeEmitsFullSendLoginShipDataFanout"/>) —
/// the passive-observation assertion that the opcode appears in the
/// handshake stream during a station-sector login. Wave 51's assertion
/// is opcode-presence only; it would still pass if the
/// <c>ConstantPositionalUpdate</c> struct layout drifted (e.g.
/// <c>GameID</c> widening from <c>int32_t</c> back to <c>long</c>
/// would add 4 bytes on LP64 Linux, <c>Position</c> widening from
/// <c>float[3]</c> to <c>double[3]</c> would add 12 bytes, or
/// <c>Orientation</c> widening from <c>float[4]</c> to <c>double[4]</c>
/// would add 16 bytes). Wave 82 adds the byte-exact 32-byte
/// payload-length assertion the presence-only check cannot make,
/// locking the wire shape in place. +0 ratchet because 0x0040 is
/// already counted; depth coverage of a regression class Wave 51 was
/// structurally blind to. Mirrors the Wave 67/71/76/77/78/79/80/81
/// pattern (byte-exact tightenings on already-counted handshake
/// emits).
/// </para>
///
/// <para>
/// Wire shape and dispatch path. <c>SectorManager::HandleSectorLogin</c>
/// at <c>server/src/SectorManager.cpp:324-336</c> branches on
/// <c>m_SectorID</c> — sector IDs &gt; 9999 are stations (route to
/// <c>StationLogin</c> → <c>StationLogin2</c>). Inside StationLogin
/// the SendConstantPositionalUpdate call at
/// <c>SectorManager.cpp:480</c> emits the manufacturing-lab
/// pseudo-object's static (0,0,0) position so the client renders the
/// lab UI anchored at origin. The station handshake into Luna Station
/// (10151) is the same 1-stage path Wave 51 and the prior eight
/// hardening waves exercise; Wave 82 reuses it without modification —
/// same account pool, same firstName / shipName payload, same drain
/// loop — and just adds the byte-exact length assertion on the
/// captured 0x0040 frames.
/// </para>
///
/// <para>
/// Regression classes this catches.
/// </para>
/// <list type="bullet">
///   <item>
///     <b>ConstantPositionalUpdate struct layout regression in
///     <c>common/include/net7/PacketStructures.h:514-519</c>.</b>
///     <c>int32_t GameID; float Position[3]; float Orientation[4]</c>
///     with <c>ATTRIB_PACKED</c>. A regression widening <c>GameID</c>
///     back to <c>long</c> would add 4 bytes on LP64 Linux (36 bytes
///     total). A regression widening <c>Position</c> from
///     <c>float[3]</c> to <c>double[3]</c> would add 12 bytes (44
///     bytes total). A regression widening <c>Orientation</c> from
///     <c>float[4]</c> to <c>double[4]</c> would add 16 bytes (48
///     bytes total).
///   </item>
///   <item>
///     <b>SendConstantPositionalUpdate sizeof(update) replacement at
///     <c>PlayerConnection.cpp:1219</c>.</b> A regression to a literal
///     constant (32) that drifts from the actual struct size, or to
///     <c>sizeof(long) + 12 + 16</c> on LP64 (36), would emit a
///     wrongly-sized frame.
///   </item>
///   <item>
///     <b>SendConstantPositionalUpdate parameter-type widening at
///     <c>PlayerConnection.cpp:1202</c>.</b> The signature is
///     <c>SendConstantPositionalUpdate(long game_id, ...)</c> — note
///     <c>long</c> on the C++ parameter but the assignment to
///     <c>update.GameID</c> (int32_t) implicitly truncates to 4 bytes
///     on LP64; a struct-side widening of <c>GameID</c> from
///     <c>int32_t</c> to <c>long</c> would lose that truncation and
///     write 8 bytes into the struct (post-struct-change the wire
///     grows by 4 bytes).
///   </item>
///   <item>
///     <b>StationLogin fanout chain truncation at
///     <c>SectorManager.cpp:480</c>.</b> A regression that removes the
///     <c>player-&gt;SendConstantPositionalUpdate(ManuID, 0, 0, 0)</c>
///     call from StationLogin (or moves it past the SendStart
///     terminator) would drop 0x0040 from the handshake stream
///     entirely — the <c>Assert.NotEmpty</c> filter catches it before
///     the length check fires.
///   </item>
///   <item>
///     <b>SendOpcode header-width revert at
///     <c>PlayerConnection.cpp:127</c>.</b> Would corrupt every inner
///     opcode in the 0x2016 PACKET_SEQUENCE parser; 0x0040 wouldn't
///     appear under its correct label at all.
///   </item>
///   <item>
///     <b>Proxy SendClientPacketSequence inner-opcode guard tightening
///     at <c>proxy/UDPProxyToClient_linux.cpp:568</c>.</b> Currently
///     passes 0x0040 (0x0040 &lt; 0x0FFF). A regression to a tighter
///     upper bound would silently drop 0x0040 from the wire — the
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
///     0x0040 frame.
///   </item>
/// </list>
///
/// <para>
/// Primary source citation (CLAUDE.md server-integrity rule). 0x0040
/// CONSTANT_POSITIONAL_UPDATE is server-originated. Wave 82 adds no
/// client stimulus and no server change — pure passive-observation
/// tightening of a retail-faithful wire shape. The 32-byte body is
/// exactly what the retail Win32 client's CONSTANT_POSITIONAL_UPDATE
/// decoder was compiled to receive; any drift breaks the client. No
/// widened input acceptance, no loosened gating, no fabricated
/// replies — server-integrity POSITIVE.
/// </para>
///
/// <para>
/// Budget: 60s. Single-stage station handshake into Luna Station
/// (10151) ~2s; assertions run synchronously against already-captured
/// state. No additional client stimulus.
/// </para>
/// </summary>
[Collection(ServerCollection.Name)]
public sealed class SectorConstantPositionalUpdateHardeningTests
{
    /// <summary>
    /// 4 (int32 GameID) + 12 (float Position[3]) + 16 (float Orientation[4]) = 32.
    /// Matches the wire size computed by
    /// <c>sizeof(ConstantPositionalUpdate)</c> against the
    /// <c>ATTRIB_PACKED</c> struct at
    /// <c>common/include/net7/PacketStructures.h:514-519</c>.
    /// </summary>
    private const int ExpectedConstantPositionalUpdatePayloadLength = 32;

    private readonly ServerFixture _server;
    private readonly ClientFixture _client;

    public SectorConstantPositionalUpdateHardeningTests(ServerFixture server)
    {
        _server = server;
        _client = new ClientFixture(server);
    }

    [Fact]
    public async Task ConstantPositionalUpdate_EmittedDuringStationSectorHandshake_HasExactly32BytePayload()
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
            firstName: "CnstPos82", shipName: "CnstPos82Ship", cts.Token);

        var constantPositionalUpdateFrames = session.HandshakeFrames
            .Where(f => f.Opcode == OpcodeId.Known.ConstantPositionalUpdate.Value)
            .ToList();

        Assert.NotEmpty(constantPositionalUpdateFrames);
        Assert.All(constantPositionalUpdateFrames, f =>
            Assert.Equal(ExpectedConstantPositionalUpdatePayloadLength, f.PayloadLength));
    }

    /// <summary>
    /// Wave 96 frame-count hardening (+0 ratchet, 0x0040): pins the
    /// exact 1-frame emit-count invariant Wave 82's payload-length
    /// hardening was structurally blind to. The captured single-player
    /// station-sector handshake stream emits 0x0040
    /// CONSTANT_POSITIONAL_UPDATE exactly once — from
    /// <c>SectorManager::StationLogin</c> at
    /// <c>server/src/SectorManager.cpp:480</c>
    /// (<c>player-&gt;SendConstantPositionalUpdate(ManuID, 0, 0, 0)</c>
    /// for the manufacture-lab pseudo-object positional anchor).
    ///
    /// <para>
    /// Structurally distinct from Wave 93/94/95 — this opcode does NOT
    /// fire from <c>Player::SendLoginShipData</c>. The
    /// <c>SendConstantPositionalUpdate</c> handler at
    /// <c>server/src/PlayerConnection.cpp:1202</c> has exactly one
    /// known caller in the entire server: the station-arm
    /// <c>SectorManager::StationLogin</c> manu-lab anchor at
    /// SectorManager.cpp:480 (verified by grep over server/src
    /// `SendConstantPositionalUpdate\b` — only the SectorManager.cpp:480
    /// call site appears alongside the declaration and definition).
    /// </para>
    ///
    /// <para>
    /// This is the FIRST frame-count hardening that pins a
    /// SectorManager-only single-emit invariant (Waves 93/94/95 all
    /// pinned SendLoginShipData self-emit-only opcodes). For 0x0040,
    /// the inverse holds: there's no SendLoginShipData self-emit at
    /// all — the manu-lab anchor is the only emit on the handshake
    /// path. A regression that adds a SendLoginShipData self-emit
    /// (e.g. for a hypothetical "player stationary position confirm"
    /// step) would surface as 2+ frames. A regression that removes
    /// the SectorManager.cpp:480 manu-lab emit would leave the
    /// captured count at 0 — Wave 82's <c>Assert.NotEmpty</c> catches
    /// that side already, but Wave 96's <c>Assert.Single</c> is the
    /// tighter bound for spurious-extra-emit regressions.
    /// </para>
    ///
    /// <para>
    /// Why a separate test method, not an in-place assertion. Wave 82's
    /// existing test caps at <c>Assert.NotEmpty + Assert.All(payload==32)</c>,
    /// which would still pass if a refactor added a spurious second
    /// emit. Keeping the count assertion in its own method preserves
    /// Wave 82's narrow-scope failure surface and gives Wave 96 a
    /// discrete test artifact for the regression-class catalogue.
    /// Mirrors the Wave 91/92/93/94/95 sibling-method pattern.
    /// </para>
    ///
    /// <para>
    /// Regression classes this catches (beyond what Wave 82 catches).
    /// </para>
    /// <list type="bullet">
    ///   <item>
    ///     <b>Spurious extra <c>SendConstantPositionalUpdate</c> in
    ///     the chain.</b> A refactor adding a SendLoginShipData
    ///     self-emit (e.g. mirroring the SendAdvancedPositionalUpdate
    ///     self-emit pattern Wave 93 pins) would produce 2+ frames.
    ///     Wave 82's <c>Assert.NotEmpty</c> still passes (every frame
    ///     is still 32B). Wave 96's <c>Assert.Single</c> catches.
    ///   </item>
    ///   <item>
    ///     <b>StationLogin refactor that splits the manu-lab emit into
    ///     pre-/post-handshake emits.</b> A symmetric refactor adding
    ///     a pre-handshake or post-handshake manu-lab positional
    ///     re-anchor would surface as 2+ frames. Wave 96 catches.
    ///   </item>
    /// </list>
    ///
    /// <para>
    /// Per CLAUDE.md server-integrity. Pure passive-observation
    /// tightening. No client stimulus, no server change. The 1-frame
    /// invariant is a retail-faithful invariant of the StationLogin
    /// manu-lab-anchor-only dispatch pattern (no SendLoginShipData
    /// self-emit for this opcode).
    /// </para>
    /// </summary>
    [Fact]
    public async Task ConstantPositionalUpdate_EmittedExactlyOnceDuringStationSectorHandshake_PinsManuLabEmit()
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
            firstName: "CnstPos96", shipName: "CnstPos96Ship", cts.Token);

        var constantPositionalUpdateFrames = session.HandshakeFrames
            .Where(f => f.Opcode == OpcodeId.Known.ConstantPositionalUpdate.Value)
            .ToList();

        // Wave 96 pins the 1-frame invariant: StationLogin manu-lab
        // anchor (SectorManager.cpp:480). Only known caller of
        // SendConstantPositionalUpdate in the entire server during
        // the station-sector handshake path.
        var single = Assert.Single(constantPositionalUpdateFrames);
        Assert.Equal(ExpectedConstantPositionalUpdatePayloadLength, single.PayloadLength);
    }
}
