// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond preservation project.
// License: LICENSES/enb-emulator

using N7.CliClient.Auth;
using N7.CliClient.Net;
using N7.CliClient.Opcodes;
using Xunit;

namespace N7.CliClient.IntegrationTests.Opcodes;

/// <summary>
/// Wave 68 hardening test (+0 ratchet, 0x007F): pins the 4-byte
/// byte-exact payload shape of every 0x007F MANUFACTURE_SET_MANUFACTURE_ID
/// frame the server emits during the sector-login handshake stream.
///
/// <para>
/// Backstory. 0x007F is server-emitted by
/// <c>Player::SetManufactureID</c> at
/// <c>server/src/PlayerConnection.cpp:10134-10138</c>:
/// <code>
///   void Player::SetManufactureID(int32_t mfg_id)
///   {
///       SendOpcode(ENB_OPCODE_007F_MANUFACTURE_SET_MANUFACTURE_ID,
///                  (unsigned char *) &amp;mfg_id, sizeof(mfg_id));
///   }
/// </code>
/// SendOpcode emits exactly <c>sizeof(mfg_id)</c> bytes. Before Wave 68
/// the parameter type was <c>long</c> — on the retail Win32 server
/// (LP32) that's 4 bytes; on our Linux server (LP64) it's 8 bytes — a
/// silent wire-shape drift away from the retail format the real client
/// expects. Wave 68 tightens the parameter to <c>int32_t</c> so
/// <c>sizeof(mfg_id)</c> locks to 4 bytes on every platform.
/// </para>
///
/// <para>
/// Primary source citation (CLAUDE.md server-integrity rule). The kyp
/// linux-port-legacy snapshot
/// (<c>archive/kyp-snapshot/linux-port-legacy/PlayerConnection.cpp:6924-6928</c>)
/// is the unaltered Win32-derived source: <c>SetManufactureID(long mfg_id)</c>
/// with <c>sizeof(mfg_id)</c> in the SendOpcode call. On Win32 (LP32)
/// that evaluated to 4 bytes — the wire shape the retail client was
/// compiled to receive. The Linux build silently inflated to 8 bytes
/// because LP64 widens <c>long</c>. Wave 68's single-token swap
/// (<c>long</c> → <c>int32_t</c>) restores byte-exact agreement with
/// the retail wire format. No widened input acceptance, no loosened
/// gating, no fabricated replies — exactly the "tightening" the rule
/// welcomes.
/// </para>
///
/// <para>
/// Why a hardening test, not a +1 ratchet wave. 0x007F is already
/// counted by Wave 35 (<see cref="SectorHandshakeFanoutTests"/>'s
/// <c>HandshakeEmitsFullSendLoginShipDataFanout</c>) — the
/// passive-observation assertion that the opcode appears in
/// <see cref="SectorHandshake.Session.HandshakeOpcodes"/>. Wave 35's
/// assertion is opcode-presence only; it would still pass if the
/// payload silently grew to 8 bytes on Linux (the regression Wave 68
/// fixes). Wave 68 adds the byte-exact 4-byte payload-length assertion
/// the presence-only check cannot make, locking the LP32/LP64
/// convergence in place. +0 ratchet because 0x007F is already counted;
/// depth coverage of a regression class Wave 35 was structurally
/// blind to.
/// </para>
///
/// <para>
/// Wire shape. <c>SectorManager::SectorLogin</c>
/// (<c>server/src/SectorManager.cpp:345</c>) calls
/// <c>player->SetManufactureID(0)</c> on space-sector login, and
/// <c>SectorManager::StationLogin</c>
/// (<c>server/src/SectorManager.cpp:475</c>) calls
/// <c>player->SetManufactureID(ntohl(ManuID))</c> on station login —
/// either way SetManufactureID emits exactly one 0x007F frame per
/// sector login. The Luna Station handshake (sector 10151) goes
/// through StationLogin; the test asserts every captured 0x007F frame
/// has a 4-byte payload.
/// </para>
///
/// <para>
/// Regression classes this catches.
/// </para>
/// <list type="bullet">
///   <item>
///     <b>SetManufactureID parameter-type revert at
///     <c>server/src/PlayerConnection.cpp:10134</c>.</b> The whole point
///     of Wave 68 — reverting <c>int32_t mfg_id</c> back to <c>long
///     mfg_id</c> would re-inflate <c>sizeof(mfg_id)</c> to 8 bytes on
///     Linux x86_64, ballooning every 0x007F frame to an 8-byte
///     payload. The 4-byte length assertion fails immediately.
///   </item>
///   <item>
///     <b>SetManufactureID header-declaration revert at
///     <c>server/src/PlayerClass.h:1028</c>.</b> The declaration must
///     match the definition; a mismatch would either fail to compile
///     or silently bind to a different overload — either way the wire
///     shape drifts. Caught by the length assertion plus compile-time
///     by the C++ ODR.
///   </item>
///   <item>
///     <b>SendOpcode header-width revert at
///     <c>server/src/PlayerConnection.cpp:127</c>.</b> Would corrupt
///     every inner opcode in the 0x2016 PACKET_SEQUENCE parser; 0x007F
///     wouldn't appear under its correct label at all (so the
///     Assert.NotEmpty filter catches it before the length check
///     fires).
///   </item>
///   <item>
///     <b>Proxy SendClientPacketSequence inner-opcode guard tightening
///     at <c>proxy/UDPProxyToClient_linux.cpp:568</c>.</b> Currently
///     passes 0x007F (0x007F &lt; 0x0FFF). A regression to a tighter
///     upper bound would silently drop 0x007F from the wire — the
///     captured-frame filter returns empty and the Assert.NotEmpty
///     check fires.
///   </item>
///   <item>
///     <b>SectorManager call-site signature drift at
///     <c>server/src/SectorManager.cpp:345/475/538</c>.</b> If a future
///     refactor reintroduces a <c>long</c> overload or removes one of
///     the three SetManufactureID call sites, fewer (or zero) 0x007F
///     frames land in the handshake stream — the Assert.NotEmpty fires
///     and identifies the regression.
///   </item>
///   <item>
///     <b>DoSectorLoginUntilStartAsync drain-loop payload-length
///     capture regression
///     (<c>tests/integration/CliClient.IntegrationTests/Opcodes/SectorHandshake.cs:396-428</c>).</b>
///     Wave 68 added payload-length capture to the handshake harness via
///     <see cref="SectorHandshake.Session.HandshakeFrames"/>. If a
///     future refactor drops the length field or starts under-counting
///     payload bytes, this test would observe wrong (or zero) lengths
///     for every captured 0x007F frame — every subsequent
///     payload-length hardening wave depends on this capture path
///     working.
///   </item>
/// </list>
///
/// <para>
/// Budget: 90s. Handshake ~2s; the assertions run synchronously against
/// already-captured state. No additional client stimulus after the
/// session establishes.
/// </para>
/// </summary>
[Collection(ServerCollection.Name)]
public sealed class SectorManufactureSetManufactureIdHardeningTests
{
    private const int ExpectedMfgIdPayloadLength = 4;

    private readonly ServerFixture _server;
    private readonly ClientFixture _client;

    public SectorManufactureSetManufactureIdHardeningTests(ServerFixture server)
    {
        _server = server;
        _client = new ClientFixture(server);
    }

    [Fact]
    public async Task ManufactureSetManufactureId_EmittedDuringHandshake_HasExactly4BytePayload()
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
            firstName: "MfgPin68", shipName: "MfgPin68Ship", cts.Token);

        try
        {
            // Pull every 0x007F frame captured during the handshake drain.
            // StationLogin (sector_id > 9999, our 10151 path) calls
            // SetManufactureID(ntohl(ManuID)) exactly once per login, so
            // we expect at least one frame; assert all are 4-byte.
            var mfgFrames = session.HandshakeFrames
                .Where(f => f.Opcode == OpcodeId.Known.ManufactureSetManufactureId.Value)
                .ToList();

            Assert.NotEmpty(mfgFrames);
            Assert.All(mfgFrames, f =>
                Assert.Equal(ExpectedMfgIdPayloadLength, f.PayloadLength));
        }
        finally
        {
            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await SectorHandshake.DeleteCreatedCharacterAsync(session.Global, slot, cleanupCts.Token); }
            catch { /* best-effort cleanup */ }
        }
    }

    /// <summary>
    /// Wave 105 frame-count hardening (+0 ratchet, 0x007F): pins the
    /// exact 1-frame emit-count invariant Wave 68's payload-length
    /// hardening was structurally blind to. The captured single-player
    /// station-sector handshake stream emits 0x007F
    /// MANUFACTURE_SET_MANUFACTURE_ID exactly once — from
    /// <c>SectorManager::StationLogin</c> at
    /// <c>server/src/SectorManager.cpp:475</c>
    /// (<c>player-&gt;SetManufactureID(ntohl(ManuID))</c> for the
    /// manufacture-lab anchor).
    ///
    /// <para>
    /// Three known call sites — <c>SectorManager::SectorLogin</c> at
    /// SectorManager.cpp:345 (space-arm, <c>SetManufactureID(0)</c>),
    /// <c>SectorManager::StationLogin</c> at SectorManager.cpp:475
    /// (station-arm, <c>SetManufactureID(ntohl(ManuID))</c>), and
    /// <c>SectorManager::LaunchIntoSpace</c> at SectorManager.cpp:538
    /// (station-to-space transition, <c>SetManufactureID(0)</c>). For a
    /// station-only handshake landing on sector 10151
    /// (<c>sector_id &gt; 9999</c> → StationLogin path per HandleSectorLogin
    /// at PlayerConnection.cpp:324-336), only the StationLogin
    /// SectorManager.cpp:475 site fires. SectorLogin never runs because
    /// the dispatch branch is taken on sector_id, and LaunchIntoSpace
    /// only fires on an explicit launch request from station to space.
    /// </para>
    ///
    /// <para>
    /// Structurally distinct from Waves 96/97 — those pin SectorManager
    /// single-emit invariants for opcodes with NO SendLoginShipData
    /// self-emit path. Wave 105 is the same shape: SetManufactureID has
    /// no SendLoginShipData self-emit (the only callers are the three
    /// SectorManager sites above; grep over server/src for
    /// <c>SetManufactureID\b</c> verifies). A regression that adds a
    /// SendLoginShipData self-emit (e.g. mirroring the SendShipInfo
    /// self-emit pattern) would surface as 2+ frames. Wave 68's
    /// <c>Assert.NotEmpty</c> still passes (every frame is still 4B).
    /// Wave 105's <c>Assert.Single</c> catches.
    /// </para>
    ///
    /// <para>
    /// Why a separate test method, not an in-place assertion. Wave 68's
    /// existing test caps at <c>Assert.NotEmpty + Assert.All(payload==4)</c>,
    /// which would still pass if a refactor added a spurious second
    /// emit. Keeping the count assertion in its own method preserves
    /// Wave 68's narrow-scope failure surface and gives Wave 105 a
    /// discrete test artifact for the regression-class catalogue.
    /// Mirrors the Wave 91/92/93/94/95/96/97/98/99/100/101/102/104
    /// sibling-method pattern.
    /// </para>
    ///
    /// <para>
    /// Regression classes this catches (beyond what Wave 68 catches).
    /// </para>
    /// <list type="bullet">
    ///   <item>
    ///     <b>Spurious extra <c>SetManufactureID</c> in the chain.</b>
    ///     A refactor adding a SendLoginShipData self-emit (e.g. for a
    ///     hypothetical "ship manu-state confirm" step) would produce
    ///     2+ frames. Wave 68's <c>Assert.NotEmpty</c> still passes
    ///     (every frame is still 4B). Wave 105's <c>Assert.Single</c>
    ///     catches.
    ///   </item>
    ///   <item>
    ///     <b>StationLogin refactor that splits the manu-lab emit into
    ///     pre-/post-handshake emits.</b> A symmetric refactor adding a
    ///     pre-handshake or post-handshake manu-lab re-anchor would
    ///     surface as 2+ frames. Wave 105 catches.
    ///   </item>
    ///   <item>
    ///     <b>SectorLogin call site bleeding into the station-arm
    ///     dispatch.</b> A regression to HandleSectorLogin's branch at
    ///     PlayerConnection.cpp:324-336 — e.g. dropping the sector_id
    ///     &gt; 9999 gate — would let the station-arm fall through to
    ///     SectorLogin (SectorManager.cpp:345 site) on top of the
    ///     StationLogin emit, producing 2 frames. Wave 105 catches.
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
    public async Task ManufactureSetManufactureId_EmittedExactlyOnceDuringStationSectorHandshake_PinsSelfEmit()
    {
        var account = TestAccounts.For();
        const int slot = 0;
        const int stationSectorId = 10151;  // Terran Warrior start: Luna Station

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        var login = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(account.Username, account.Password), cts.Token);
        Assert.True(login.Valid, $"login: {login.RawBody.TrimEnd()}");
        Assert.False(string.IsNullOrEmpty(login.Ticket));

        await using var session = await SectorHandshake.EstablishAsync(
            _server, login.Ticket!, account.Username, slot, stationSectorId,
            firstName: "MfgPin105", shipName: "MfgPin105Ship", cts.Token);

        try
        {
            var mfgFrames = session.HandshakeFrames
                .Where(f => f.Opcode == OpcodeId.Known.ManufactureSetManufactureId.Value)
                .ToList();

            // Wave 105 pins the 1-frame invariant: StationLogin manu-lab
            // anchor (SectorManager.cpp:475). Only emit-site reached on the
            // single-player station-sector handshake dispatch path.
            var single = Assert.Single(mfgFrames);
            Assert.Equal(ExpectedMfgIdPayloadLength, single.PayloadLength);
        }
        finally
        {
            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await SectorHandshake.DeleteCreatedCharacterAsync(session.Global, slot, cleanupCts.Token); }
            catch { /* best-effort cleanup */ }
        }
    }

    /// <summary>
    /// Wave 106 frame-count hardening (+0 ratchet, 0x007F): pins the
    /// exact 1-frame emit-count invariant of the SPACE-arm dispatch
    /// branch (complement of Wave 105's STATION-arm pin). The captured
    /// single-player space-sector handshake stream emits 0x007F
    /// MANUFACTURE_SET_MANUFACTURE_ID exactly once — from
    /// <c>SectorManager::SectorLogin</c> at
    /// <c>server/src/SectorManager.cpp:345</c>
    /// (<c>player-&gt;SetManufactureID(0)</c> on space-arm anchor).
    ///
    /// <para>
    /// Dispatch reminder: <c>SectorManager::HandleSectorLogin</c>
    /// (<c>server/src/SectorManager.cpp:324-336</c>) branches on
    /// <c>m_SectorID</c>: <c>&gt; 9999</c> → <c>StationLogin</c>;
    /// <c>≤ 9999</c> → <c>SectorLogin</c>. Sector 1015 (Luna space,
    /// <c>sector_type=ST_PLANET</c>) takes the SectorLogin branch.
    /// SectorLogin emits exactly one <c>SetManufactureID(0)</c> at
    /// line 345; StationLogin's manu-lab-anchor emit at line 475 never
    /// fires; LaunchIntoSpace at line 538 only fires on an explicit
    /// in-session undock request, not during the handshake itself.
    /// </para>
    ///
    /// <para>
    /// Two-stage station→space pattern (mirrors Waves 89/102/103/104).
    /// Stage 1: create the avatar via the station-sector handshake
    /// (sector 10151) — the only handshake that hits the character-create
    /// surface. Stage 2: cleanly LOGOFF_REQUEST/LOGOFF_CONFIRMATION the
    /// stage-1 session so the server's <c>DropPlayerFromGalaxy</c> runs
    /// synchronously (otherwise G_ERROR_ACCOUNT_IN_USE in stage 2). Then
    /// reconnect (no char create) and LOGIN to space sector 1015.
    /// </para>
    ///
    /// <para>
    /// Structurally distinct from Wave 105. Wave 105 lands on sector
    /// 10151 (StationLogin path) and pins the <c>SetManufactureID(ntohl(ManuID))</c>
    /// manu-lab-anchor emit. Wave 106 lands on sector 1015 (SectorLogin
    /// path) and pins the <c>SetManufactureID(0)</c> space-arm anchor
    /// emit. Both branches of the HandleSectorLogin dispatch are now
    /// pinned to "exactly one 0x007F frame per login handshake".
    /// </para>
    ///
    /// <para>
    /// Regression classes this catches (beyond what Wave 105 catches).
    /// </para>
    /// <list type="bullet">
    ///   <item>
    ///     <b>Spurious extra <c>SetManufactureID</c> in the space-arm
    ///     chain.</b> A refactor adding a SendLoginShipData self-emit
    ///     (mirroring the SendShipInfo self-emit pattern) for the
    ///     space-arm branch would produce 2+ frames. Wave 68's
    ///     <c>Assert.NotEmpty</c> still passes (every frame is still 4B).
    ///     Wave 105 doesn't fire (different dispatch path). Wave 106
    ///     catches.
    ///   </item>
    ///   <item>
    ///     <b>SectorLogin refactor that splits the space-arm anchor into
    ///     pre-/post-handshake emits.</b> A symmetric refactor adding a
    ///     pre-handshake or post-handshake space-arm re-anchor would
    ///     surface as 2+ frames. Wave 106 catches.
    ///   </item>
    ///   <item>
    ///     <b>StationLogin call site bleeding into the space-arm
    ///     dispatch.</b> A regression to HandleSectorLogin's branch at
    ///     SectorManager.cpp:324-336 — e.g. inverting the sector_id
    ///     comparison — would let the space-arm fall through to
    ///     StationLogin (SectorManager.cpp:475 site) on top of the
    ///     SectorLogin emit, producing 2 frames. Wave 106 catches; Wave
    ///     105 may also fire (depending on direction of the regression).
    ///   </item>
    ///   <item>
    ///     <b>LaunchIntoSpace handshake-time bleed at
    ///     SectorManager.cpp:538.</b> If a refactor calls LaunchIntoSpace
    ///     (or its inner SetManufactureID(0) emit) during the handshake
    ///     drain rather than only on explicit undock, the space-arm
    ///     handshake would surface 2 frames. Wave 106 catches.
    ///   </item>
    /// </list>
    ///
    /// <para>
    /// Per CLAUDE.md server-integrity. Pure passive-observation
    /// tightening. No client stimulus, no server change. The 1-frame
    /// invariant is a retail-faithful invariant of the single-call
    /// SectorLogin dispatch.
    /// </para>
    /// </summary>
    [Fact]
    public async Task ManufactureSetManufactureId_EmittedExactlyOnceDuringSpaceSectorHandshake_PinsSelfEmit()
    {
        var account = TestAccounts.For();
        const int slot = 0;
        const int stationSectorId = 10151;  // Terran Warrior start: Luna Station
        const int spaceSectorId = 1015;     // Luna (space, sector_type=ST_PLANET)

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120));

        var login = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(account.Username, account.Password), cts.Token);
        Assert.True(login.Valid, $"login: {login.RawBody.TrimEnd()}");
        Assert.False(string.IsNullOrEmpty(login.Ticket));

        // Stage 1: create avatar via station-sector handshake.
        var stationSession = await SectorHandshake.EstablishAsync(
            _server, login.Ticket!, account.Username, slot, stationSectorId,
            firstName: "MfgPin106", shipName: "MfgPin106Ship", cts.Token);

        try
        {
            // Cleanly tear down stage 1 with an explicit 0x00B9
            // LOGOFF_REQUEST so the server runs DropPlayerFromGalaxy
            // synchronously (avoids G_ERROR_ACCOUNT_IN_USE in stage 2).
            byte[] logoffPayload = new byte[8];
            await stationSession.Sector.SendAsync(
                Packet.ForOpcode(OpcodeId.Known.LogoffRequest.Value, logoffPayload),
                cts.Token);
            await SectorHandshake.DrainUntilOpcode(
                stationSession.Sector, OpcodeId.Known.LogoffConfirmation.Value, cts.Token);
            await stationSession.DisposeAsync();

            // Stage 2: reconnect (no char create) and LOGIN to space
            // sector 1015. HandleSectorLogin dispatches to SectorLogin
            // (SectorManager.cpp:332), which emits 0x007F
            // MANUFACTURE_SET_MANUFACTURE_ID(0) at line 345. Filter
            // HandshakeFrames for 0x007F and pin the count to exactly
            // one + the payload length to 4 bytes.
            await using var spaceSession = await SectorHandshake.ReestablishAsync(
                _server, login.Ticket!, slot, spaceSectorId, cts.Token);

            var mfgFrames = spaceSession.HandshakeFrames
                .Where(f => f.Opcode == OpcodeId.Known.ManufactureSetManufactureId.Value)
                .ToList();

            // Wave 106 pins the 1-frame invariant: SectorLogin space-arm
            // anchor (SectorManager.cpp:345). Only emit-site reached on the
            // single-player space-sector handshake dispatch path.
            var single = Assert.Single(mfgFrames);
            Assert.Equal(ExpectedMfgIdPayloadLength, single.PayloadLength);
        }
        finally
        {
            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try
            {
                await using var cleanupGlobal = await EncryptedTcpConnection.ConnectAsync(
                    _server.GlobalHost, _server.GlobalPort, cleanupCts.Token);
                await SectorHandshake.SendGlobalConnectAsync(
                    cleanupGlobal, login.Ticket!, cleanupCts.Token);
                await SectorHandshake.DrainUntilOpcode(
                    cleanupGlobal, OpcodeId.Known.GlobalAvatarList.Value, cleanupCts.Token);
                await SectorHandshake.DeleteCreatedCharacterAsync(
                    cleanupGlobal, slot, cleanupCts.Token);
            }
            catch { /* best-effort cleanup */ }
        }
    }
}
