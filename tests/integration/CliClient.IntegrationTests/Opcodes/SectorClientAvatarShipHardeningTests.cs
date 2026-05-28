// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond preservation project.
// License: LICENSES/enb-emulator

using N7.CliClient.Auth;
using N7.CliClient.Opcodes;
using Xunit;

namespace N7.CliClient.IntegrationTests.Opcodes;

/// <summary>
/// Wave 77 hardening test (+0 ratchet, 0x0037 + 0x0047): pins the
/// byte-exact 4-byte payload shape of every 0x0037 CLIENT_AVATAR and
/// 0x0047 CLIENT_SHIP frame the server emits during the station-sector
/// login handshake stream.
///
/// <para>
/// Backstory. 0x0037 CLIENT_AVATAR and 0x0047 CLIENT_SHIP are both
/// server-emitted by <c>Player::SendLoginShipData</c> at
/// <c>server/src/PlayerClass.cpp:879-880</c> (own-player fanout) and by
/// <c>Player::SendShipData</c> at <c>server/src/PlayerClass.cpp:928-929</c>
/// (peer-broadcast variant, gated on <c>this == player</c>). Both emit
/// the same 4-byte <c>int32_t game_id_wire = (int32_t) m_CreateInfo.GameID</c>
/// — a narrowed <c>m_CreateInfo.GameID</c> (which is <c>long</c>,
/// 8 bytes on LP64 Linux). The narrowing was the Wave 36 server-tightening
/// fix (per the <c>// Phase K: wire field is 4-byte GameID</c> comment at
/// PlayerClass.cpp:874-877). Without that narrowing the raw 8-byte
/// <c>m_CreateInfo.GameID</c> would have walked the client's
/// PACKET_SEQUENCE parser off the inner <c>[len][opcode]</c> tuple.
/// </para>
///
/// <para>
/// Why a hardening test, not a +1 ratchet wave. Both 0x0037 and 0x0047
/// are already counted by Wave 51
/// (<see cref="SectorHandshakeFanoutTests.HandshakeEmitsFullSendLoginShipDataFanout"/>) —
/// the passive-observation assertion that both opcodes appear in the
/// handshake stream during a station-sector login. Wave 51's assertion
/// is opcode-presence only; it would still pass if the
/// <c>int32_t game_id_wire</c> narrowing at PlayerClass.cpp:878/927
/// were reverted to <c>m_CreateInfo.GameID</c> directly (which is
/// <c>long</c>, 8 bytes on LP64 Linux — would silently re-introduce the
/// Wave 36 corruption). Wave 77 adds the byte-exact 4-byte payload-length
/// assertion the presence-only check cannot make, locking the wire shape
/// in place. +0 ratchet because both opcodes are already counted; depth
/// coverage of a regression class Wave 51 was structurally blind to.
/// Mirrors the Wave 67/71/76 pattern (byte-exact tightenings on
/// already-counted handshake emits) — bundles the 0x0037/0x0047 pair
/// because both ride the identical <c>int32_t game_id_wire</c>
/// marshalling path; a regression on the narrowing comes through both
/// opcodes simultaneously.
/// </para>
///
/// <para>
/// Wire shape and dispatch path. <c>SectorManager::HandleSectorLogin</c>
/// at <c>server/src/SectorManager.cpp:324-336</c> branches on
/// <c>m_SectorID</c> — sector IDs &gt; 9999 are stations (route to
/// <c>StationLogin</c> → <c>StationLogin2</c>), which calls
/// <c>SendLoginShipData</c> (<c>PlayerClass.cpp:855</c>). That function
/// dispatches the per-ship fanout chain
/// (<c>SendCreate</c> → <c>SendSubparts</c> →
/// <c>SendShipColorization(this, 8)</c> →
/// <c>SendOpcode(0x0037, &amp;game_id_wire, 4)</c> →
/// <c>SendOpcode(0x0047, &amp;game_id_wire, 4)</c> → ...). The station
/// handshake into Luna Station (10151) is the same 1-stage path Wave 51
/// exercises; Wave 77 reuses it without modification — same account pool,
/// same firstName / shipName payload, same drain loop — and just adds the
/// byte-exact length assertions on the captured 0x0037 and 0x0047 frames.
/// </para>
///
/// <para>
/// Regression classes this catches.
/// </para>
/// <list type="bullet">
///   <item>
///     <b>game_id_wire narrowing revert at <c>PlayerClass.cpp:878</c>
///     and <c>PlayerClass.cpp:927</c>.</b> If the explicit
///     <c>int32_t game_id_wire = (int32_t) m_CreateInfo.GameID</c>
///     temporary is removed and the raw <c>m_CreateInfo.GameID</c>
///     (<c>long</c>) is passed directly to <c>SendOpcode</c> with
///     <c>sizeof(m_CreateInfo.GameID)</c>, the payload would be 8 bytes
///     on LP64 Linux instead of 4 — silently re-introducing the Wave 36
///     PACKET_SEQUENCE corruption.
///   </item>
///   <item>
///     <b>game_id_wire type-width regression — <c>int32_t</c> →
///     <c>int16_t</c> or <c>int64_t</c>.</b> Either direction would
///     emit a wrongly-sized frame (2 or 8 bytes); a 2-byte frame would
///     leave 2 trailing bytes of stack visible to the client.
///   </item>
///   <item>
///     <b>sizeof(game_id_wire) replaced with a literal that drifts from
///     the variable's actual size.</b> A common refactor mistake;
///     <c>sizeof(int32_t)</c> versus a hard-coded <c>4</c> is fine, but
///     <c>sizeof(int)</c> on a future ILP64 platform would diverge.
///   </item>
///   <item>
///     <b>SendOpcode header-width revert at
///     <c>PlayerConnection.cpp:127</c>.</b> Would corrupt every inner
///     opcode in the 0x2016 PACKET_SEQUENCE parser; 0x0037 and 0x0047
///     wouldn't appear under their correct labels at all (so the
///     <c>Assert.NotEmpty</c> filter catches it before the length check
///     fires).
///   </item>
///   <item>
///     <b>Proxy SendClientPacketSequence inner-opcode guard tightening
///     at <c>proxy/UDPProxyToClient_linux.cpp:568</c>.</b> Currently
///     passes 0x0037 and 0x0047 (both &lt; 0x0FFF). A regression to a
///     tighter upper bound would silently drop both from the wire — the
///     captured-frame filter returns empty and the
///     <c>Assert.NotEmpty</c> check fires.
///   </item>
///   <item>
///     <b>SendLoginShipData fanout chain truncation at
///     <c>PlayerClass.cpp:879-880</c>.</b> A regression that removes
///     either <c>SendOpcode(0x0037, ...)</c> or
///     <c>SendOpcode(0x0047, ...)</c> from the SendLoginShipData chain
///     (or moves them past the chain terminator) would drop the opcode
///     from the handshake stream entirely.
///   </item>
///   <item>
///     <b>DoSectorLoginUntilStartAsync drain-loop payload-length
///     capture regression
///     (<c>tests/integration/CliClient.IntegrationTests/Opcodes/SectorHandshake.cs</c>).</b>
///     The HandshakeFrames capture path populated by the drain loop
///     records the payload-length of every inbound frame. If a future
///     refactor drops the length field or under-counts payload bytes,
///     this test observes wrong (or zero) lengths for every captured
///     0x0037 / 0x0047 frame.
///   </item>
/// </list>
///
/// <para>
/// Primary source citation (CLAUDE.md server-integrity rule). 0x0037
/// CLIENT_AVATAR and 0x0047 CLIENT_SHIP are both server-originated.
/// Wave 77 adds no client stimulus and no server change — pure
/// passive-observation tightening of a retail-faithful wire shape. The
/// 4-byte body is exactly what the retail Win32 client's
/// CLIENT_AVATAR/CLIENT_SHIP decoders were compiled to receive; any
/// drift breaks the client. No widened input acceptance, no loosened
/// gating, no fabricated replies — server-integrity POSITIVE.
/// </para>
///
/// <para>
/// Budget: 60s. Single-stage station handshake into Luna Station
/// (10151) ~2s; assertions run synchronously against already-captured
/// state. No additional client stimulus.
/// </para>
/// </summary>
[Collection(ServerCollection.Name)]
public sealed class SectorClientAvatarShipHardeningTests
{
    /// <summary>
    /// 4 bytes — <c>sizeof(int32_t)</c>. The wire field is the narrowed
    /// <c>int32_t game_id_wire = (int32_t) m_CreateInfo.GameID</c>
    /// temporary at <c>PlayerClass.cpp:878</c> / <c>PlayerClass.cpp:927</c>.
    /// </summary>
    private const int ExpectedGameIdWirePayloadLength = 4;

    private readonly ServerFixture _server;
    private readonly ClientFixture _client;

    public SectorClientAvatarShipHardeningTests(ServerFixture server)
    {
        _server = server;
        _client = new ClientFixture(server);
    }

    [Fact]
    public async Task ClientAvatarAndClientShip_EmittedDuringStationSectorHandshake_HaveExactly4BytePayload()
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
            firstName: "AvShip77", shipName: "AvShip77Ship", cts.Token);

        var clientAvatarFrames = session.HandshakeFrames
            .Where(f => f.Opcode == OpcodeId.Known.ClientAvatar.Value)
            .ToList();
        var clientShipFrames = session.HandshakeFrames
            .Where(f => f.Opcode == OpcodeId.Known.ClientShip.Value)
            .ToList();

        Assert.NotEmpty(clientAvatarFrames);
        Assert.NotEmpty(clientShipFrames);
        Assert.All(clientAvatarFrames, f =>
            Assert.Equal(ExpectedGameIdWirePayloadLength, f.PayloadLength));
        Assert.All(clientShipFrames, f =>
            Assert.Equal(ExpectedGameIdWirePayloadLength, f.PayloadLength));
    }

    /// <summary>
    /// Wave 101 frame-count hardening (+0 ratchet, 0x0037 + 0x0047):
    /// pins the exact 1-frame emit-count invariant Wave 77's
    /// payload-length hardening was structurally blind to. The
    /// captured single-player station-sector handshake stream emits
    /// 0x0037 CLIENT_AVATAR and 0x0047 CLIENT_SHIP exactly once each
    /// — from <c>Player::SendLoginShipData</c> at
    /// <c>server/src/PlayerClass.cpp:879</c> and
    /// <c>server/src/PlayerClass.cpp:880</c>
    /// (paired <c>SendOpcode</c> calls marshalling the 4-byte
    /// <c>int32_t game_id_wire = (int32_t) m_CreateInfo.GameID</c>).
    ///
    /// <para>
    /// Single-player single-sector scope. The other call sites for
    /// both opcodes are inside <c>Player::SendShipData</c> at
    /// PlayerClass.cpp:928 / :929 — guarded by
    /// <c>if (this == player)</c>. <c>SendShipData</c> is called from
    /// peer-visibility paths (AddPlayerToRangeList, ShipUpgrade,
    /// HandleSlashCommands, HandleRecustomizeShipDone,
    /// RecalculateEnergyRegen) — none of which fire during a passive
    /// station-sector login. So 0x0037 and 0x0047 are exactly 1 emit
    /// each per station-sector login. Mirrors the Wave 94/95/98/99/100-style
    /// SendLoginShipData self-emit pinning, but pinning a PAIR of
    /// co-emitted opcodes — FIRST paired-pinning in Phase K.
    /// </para>
    ///
    /// <para>
    /// Why a separate test method, not an in-place assertion. Wave 77's
    /// existing test caps at <c>Assert.NotEmpty + Assert.All(payload==4)</c>
    /// for both opcodes, which would still pass if a refactor added a
    /// spurious second emit on either. Keeping the count assertion in
    /// its own method preserves Wave 77's narrow-scope failure surface.
    /// Mirrors the Wave 91-100 sibling-method pattern.
    /// </para>
    ///
    /// <para>
    /// Per CLAUDE.md server-integrity. Pure passive-observation
    /// tightening. No client stimulus, no server change.
    /// </para>
    /// </summary>
    [Fact]
    public async Task ClientAvatarAndClientShip_EmittedExactlyOnceDuringStationSectorHandshake_PinsSelfEmits()
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
            firstName: "AvShip101", shipName: "AvShip101Ship", cts.Token);

        var clientAvatarFrames = session.HandshakeFrames
            .Where(f => f.Opcode == OpcodeId.Known.ClientAvatar.Value)
            .ToList();
        var clientShipFrames = session.HandshakeFrames
            .Where(f => f.Opcode == OpcodeId.Known.ClientShip.Value)
            .ToList();

        // Wave 101 pins the 1-frame-each invariant: SendLoginShipData
        // self-emits at PlayerClass.cpp:879 (0x0037) and PlayerClass.cpp:880
        // (0x0047). The SendShipData broadcast arm at PlayerClass.cpp:928/929
        // is guarded by `if (this == player)` and does not fire during a
        // single-player station-sector login.
        var singleAvatar = Assert.Single(clientAvatarFrames);
        var singleShip = Assert.Single(clientShipFrames);
        Assert.Equal(ExpectedGameIdWirePayloadLength, singleAvatar.PayloadLength);
        Assert.Equal(ExpectedGameIdWirePayloadLength, singleShip.PayloadLength);
    }
}
