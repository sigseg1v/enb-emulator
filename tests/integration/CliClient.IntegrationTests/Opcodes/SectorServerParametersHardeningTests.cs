// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond preservation project.
// License: LICENSES/enb-emulator

using N7.CliClient.Auth;
using N7.CliClient.Net;
using N7.CliClient.Opcodes;
using Xunit;

namespace N7.CliClient.IntegrationTests.Opcodes;

/// <summary>
/// Wave 87 hardening test (+0 ratchet, 0x0042): pins the 70-byte
/// byte-exact payload shape of every 0x0042 SERVER_PARAMETERS frame
/// the server emits during a space-sector login handshake stream.
///
/// <para>
/// Backstory. 0x0042 is server-emitted by
/// <c>SectorManager::SendServerParameters</c> at
/// <c>server/src/SectorManager.cpp:264-293</c>:
/// <code>
///   void SectorManager::SendServerParameters(Player *player)
///   {
///       ServerParameters parameters;
///       memset(&amp;parameters, 0, sizeof(parameters));
///       parameters.ZBandMin = m_SectorData-&gt;server_params.ZBandMin;
///       /* ...21 field copies... */
///       player-&gt;SendOpcode(ENB_OPCODE_0042_SERVER_PARAMETERS,
///                          (unsigned char *) &amp;parameters,
///                          sizeof(parameters));
///   }
/// </code>
/// The emit copies a stack-local <c>ServerParameters</c> struct and
/// ships exactly <c>sizeof(parameters)</c> bytes over the wire — the
/// length is set by the struct definition in
/// <c>common/include/net7/PacketStructures.h:375-398</c>, not by any
/// runtime quantity. With <c>ATTRIB_PACKED</c> applied and verified
/// field tally (8 × float ZBandMin..FogFar = 32, int32_t DebrisMode = 4,
/// 3 × char LightBackdrop/FogBackdrop/SwapBackdrop = 3, 3 × float
/// BackdropFogNear/BackdropFogFar/MaxTilt = 12, char AutoLevel = 1,
/// 3 × float ImpulseRate/DecayVelocity/DecaySpin = 12, short
/// BackdropBaseAsset = 2, uint32_t SectorNum = 4 → <b>70 bytes</b>).
/// </para>
///
/// <para>
/// Primary source citation (CLAUDE.md server-integrity rule). The
/// SendOpcode-with-sizeof emission pattern is the only call site that
/// produces a 0x0042 frame on the wire; no other server path emits
/// SERVER_PARAMETERS. The 70-byte shape is determined entirely by the
/// packed struct definition and the <c>SendOpcode(..., sizeof(parameters))</c>
/// idiom — a regression to any individual field type (e.g. inflating a
/// char back to int32_t, or a short back to int32_t, or DebrisMode
/// back to LP64 long) would immediately shift the wire length away
/// from 70. No widened input acceptance, no loosened gating, no
/// fabricated replies — exactly the "tightening" the rule welcomes.
/// </para>
///
/// <para>
/// Why a hardening test, not a +1 ratchet wave. 0x0042 is already
/// counted by Wave 53
/// (<see cref="SectorHandshakeFanoutTests.HandshakeEmitsServerParametersOnSpaceSectorLogin"/>) —
/// the passive-observation assertion that the opcode appears in the
/// handshake stream when the LOGIN routes through the space-sector
/// dispatch branch. Wave 53's assertion is opcode-presence only; it
/// would still pass if any single field's wire width silently changed
/// (a LP64 long for DebrisMode, a removed ATTRIB_PACKED inflating the
/// struct via padding, a struct-field reorder that adds compiler
/// padding before a misaligned short, etc.). Wave 87 adds the
/// byte-exact 70-byte payload-length assertion the presence-only check
/// cannot make, locking the packed-struct layout in place. +0 ratchet
/// because 0x0042 is already counted; depth coverage of a regression
/// class Wave 53 was structurally blind to.
/// </para>
///
/// <para>
/// Wire shape and dispatch path. <c>SectorManager::HandleSectorLogin2</c>
/// at <c>server/src/SectorManager.cpp:295-305</c> branches on
/// <c>m_SectorID</c> — sector IDs &gt; 9999 are stations (route to
/// <c>StationLogin2</c> which does NOT call SendServerParameters), and
/// IDs ≤ 9999 are space (route to <c>SectorLogin2</c> at
/// <c>SectorManager.cpp:354</c> which calls
/// <c>SendServerParameters(player)</c> at line 364). The hardening
/// test therefore reuses Wave 53's 2-stage pattern: stage 1 creates
/// the avatar via the Luna Station (10151) handshake which writes the
/// <c>avatar_level_info</c> row; stage 2 cleanly logs the avatar off,
/// reconnects, and LOGINs to sector 1015 (Luna space) so the second
/// handshake's <c>ReadSavedData</c> takes the <c>ReloadSavedData</c>
/// path (<c>server/src/PlayerSaves.cpp:289-291</c>) which preserves
/// the space sector_num — routing through <c>SectorLogin2</c> and
/// emitting exactly one 0x0042 frame per login.
/// </para>
///
/// <para>
/// Pattern lineage. FOURTEENTH hardening-pattern wave (Waves 67/71/76/
/// 77/78/79/80/81/82/83/84/85/86 → 87). Wave 84 introduced the
/// multi-emit <c>Assert.NotEmpty</c> + <c>Assert.All</c> pattern for
/// opcodes that fan out from multiple emit sites; Wave 87 is a
/// single-emit-per-login case (only the space-sector
/// <c>SectorLogin2</c> arm emits it) but adopts the same form for
/// uniformity with downstream waves that may pin additional 0x0042
/// emit sites if the server gains a per-sector reload trigger. Wave 87
/// is the THIRD wave (after Waves 71 / 76+ stationary-arm sweep) to
/// pin a SPACE-sector-arm emit — earlier waves focused on station-arm
/// fanout opcodes. Tightening the space-arm shapes locks in the LP64
/// convergence on the dispatch branch the kyp upstream patched
/// independently.
/// </para>
///
/// <para>
/// Regression classes this catches.
/// </para>
/// <list type="bullet">
///   <item>
///     <b>ServerParameters struct field-width revert at
///     <c>common/include/net7/PacketStructures.h:375-398</c>.</b> Any
///     field re-widening — char → int32_t for the three backdrop
///     booleans (LightBackdrop/FogBackdrop/SwapBackdrop) or AutoLevel,
///     short → int32_t for BackdropBaseAsset, int32_t → long for
///     DebrisMode or SectorNum (LP64 inflation), or a structurally
///     identical retype that bumps padding — would shift the packed
///     length away from 70 bytes and the byte-exact assertion fires
///     immediately.
///   </item>
///   <item>
///     <b>ATTRIB_PACKED removal regression at
///     <c>common/include/net7/PacketStructures.h:398</c>.</b> Dropping
///     the packed attribute lets the compiler insert padding between
///     the three consecutive chars and the following float, between
///     AutoLevel and ImpulseRate, between BackdropBaseAsset and
///     SectorNum, etc. The unpacked size would jump to 76+ bytes
///     depending on the platform's default alignment. The 70-byte
///     assertion catches.
///   </item>
///   <item>
///     <b>SendServerParameters emit-length regression at
///     <c>server/src/SectorManager.cpp:292</c>.</b> A copy-paste swap
///     of <c>sizeof(parameters)</c> for a hardcoded literal (or any
///     wrong sizeof target) would mismatch the struct length. Length
///     assertion catches.
///   </item>
///   <item>
///     <b>SendOpcode header-width revert at
///     <c>server/src/PlayerConnection.cpp:127</c>.</b> Would corrupt
///     every inner opcode in the 0x2016 PACKET_SEQUENCE parser; 0x0042
///     wouldn't appear under its correct label at all (so
///     <c>Assert.NotEmpty</c> catches it before the length check
///     fires).
///   </item>
///   <item>
///     <b>Proxy SendClientPacketSequence inner-opcode guard tightening
///     at <c>proxy/UDPProxyToClient_linux.cpp:568</c>.</b> Currently
///     passes 0x0042 (0x0042 &lt; 0x0FFF). A regression to a tighter
///     upper bound would silently drop 0x0042 from the wire — the
///     captured-frame filter returns empty and the
///     <c>Assert.NotEmpty</c> check fires.
///   </item>
///   <item>
///     <b>SectorManager dispatch-branch collapse at
///     <c>server/src/SectorManager.cpp:295-305</c>.</b> If
///     <c>HandleSectorLogin2</c> always took the station path (e.g. a
///     regression flipping the <c>m_SectorID &gt; 9999</c> guard),
///     <c>SectorLogin2</c> never fires for sector 1015, no 0x0042
///     emits, and <c>Assert.NotEmpty</c> catches.
///   </item>
///   <item>
///     <b>SendServerParameters guard regression at
///     <c>SectorManager.cpp:364</c>.</b> The current call is
///     unconditional; a conditional gate (e.g. only emitting when
///     <c>m_SectorData-&gt;server_params</c> has nonzero contents)
///     would skip the emit on test sectors with default
///     <c>server_params</c>. <c>Assert.NotEmpty</c> catches.
///   </item>
///   <item>
///     <b>DoSectorLoginUntilStartAsync drain-loop payload-length
///     capture regression
///     (<c>tests/integration/CliClient.IntegrationTests/Opcodes/SectorHandshake.cs:194-205</c>).</b>
///     The Wave 68 harness addition that populates
///     <see cref="SectorHandshake.Session.HandshakeFrames"/> with
///     payload-length info on the <see cref="SectorHandshake.ReestablishAsync"/>
///     path. If a future refactor drops the length field or
///     under-counts payload bytes, this test observes wrong (or zero)
///     lengths for every captured 0x0042 frame.
///   </item>
///   <item>
///     <b>Stage 1 → Stage 2 logoff-and-reconnect path regression at
///     <c>SectorHandshake.cs:436-446</c>.</b> A bare TCP disconnect
///     between stages would leave the in-memory Player around long
///     enough that the stage-2 GlobalConnect hits
///     <c>G_ERROR_ACCOUNT_IN_USE</c>
///     (<c>server/src/UDP_Global.cpp:166-170</c>). The explicit 0x00B9
///     LOGOFF_REQUEST plus drain-until-0x00BA pattern is the same one
///     Wave 52's fanout test established; a regression breaking it
///     surfaces here as a stage-2 failure with the
///     <c>G_ERROR_ACCOUNT_IN_USE</c> diagnostic.
///   </item>
/// </list>
///
/// <para>
/// Budget: 120s. Stage 1 handshake ~2s; stage 1 logoff ~1s; stage 2
/// re-handshake ~2s; assertions run synchronously against
/// already-captured state. No additional client stimulus.
/// </para>
/// </summary>
[Collection(ServerCollection.Name)]
public sealed class SectorServerParametersHardeningTests
{
    private const int ExpectedServerParametersPayloadLength = 70;

    private readonly ServerFixture _server;
    private readonly ClientFixture _client;

    public SectorServerParametersHardeningTests(ServerFixture server)
    {
        _server = server;
        _client = new ClientFixture(server);
    }

    [Fact]
    public async Task ServerParameters_EmittedDuringSpaceSectorHandshake_HasExactly70BytePayload()
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

        // Stage 1: create avatar and complete the StationLogin handshake.
        var stationSession = await SectorHandshake.EstablishAsync(
            _server, login.Ticket!, account.Username, slot, stationSectorId,
            firstName: "SrvPar87", shipName: "SrvPar87Ship", cts.Token);

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
            // sector 1015. HandleSectorLogin2 dispatches to SectorLogin2
            // (SectorManager.cpp:303), which emits 0x0042
            // SERVER_PARAMETERS at line 364. Filter HandshakeFrames for
            // 0x0042 and pin every frame to a 70-byte payload.
            await using var spaceSession = await SectorHandshake.ReestablishAsync(
                _server, login.Ticket!, slot, spaceSectorId, cts.Token);

            var serverParametersFrames = spaceSession.HandshakeFrames
                .Where(f => f.Opcode == OpcodeId.Known.ServerParameters.Value)
                .ToList();

            Assert.NotEmpty(serverParametersFrames);
            Assert.All(serverParametersFrames, f =>
                Assert.Equal(ExpectedServerParametersPayloadLength, f.PayloadLength));
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

    /// <summary>
    /// Wave 104 frame-count hardening (+0 ratchet, 0x0042): pins the
    /// exact 1-frame emit-count invariant Wave 87's payload-length
    /// hardening was structurally blind to. The captured single-player
    /// space-sector login stream emits 0x0042 SERVER_PARAMETERS exactly
    /// once — from <c>SectorManager::SectorLogin2</c> at
    /// <c>server/src/SectorManager.cpp:364</c>
    /// (<c>SendServerParameters(player)</c>), which dispatches to
    /// <c>SectorManager::SendServerParameters</c> at SectorManager.cpp:264-293
    /// where the single <c>SendOpcode(ENB_OPCODE_0042_SERVER_PARAMETERS, ...,
    /// sizeof(parameters))</c> call lives. SectorLogin2 is the space-arm
    /// dispatch path (PlayerConnection.cpp:324-336 routes sector_id ≤ 9999
    /// to SectorLogin → SectorLogin2; sector_id &gt; 9999 routes to
    /// StationLogin which does NOT call SendServerParameters).
    ///
    /// <para>
    /// The 0x0042 SendOpcode at SectorManager.cpp:292 is the only emit
    /// site for this opcode in the entire server source tree (verified
    /// by grep: SectorManager.cpp:292 is the unique
    /// ENB_OPCODE_0042_SERVER_PARAMETERS SendOpcode call). So 0x0042 is
    /// exactly 1 emit per single-player space-sector login. Mirrors the
    /// Wave 91-103 sibling-method pattern.
    /// </para>
    ///
    /// <para>
    /// Why a separate test method, not an in-place assertion. Wave 87's
    /// existing test caps at <c>Assert.NotEmpty + Assert.All(payload==70)</c>,
    /// which would still pass if a refactor added a spurious second
    /// emit (e.g., re-introducing the commented-out
    /// <c>SendDataFileToClient("ServerParameters.dat")</c> call at
    /// SectorManager.cpp:363 alongside the SendServerParameters call,
    /// then accidentally re-emitting via both paths). Keeping the count
    /// assertion in its own method preserves Wave 87's narrow-scope
    /// failure surface for payload-length-only regressions.
    /// </para>
    ///
    /// <para>
    /// Catches:
    /// </para>
    /// <list type="bullet">
    ///   <item>
    ///     <b>Duplicate SendServerParameters at SectorManager.cpp:364.</b>
    ///     A copy-paste regression that introduced a second SendServerParameters
    ///     call (e.g., one before and one after the SendAllNavs block) would
    ///     emit 2 frames. <c>Assert.NotEmpty + Assert.All</c> would still pass
    ///     (both frames have the correct 70-byte length); <c>Assert.Single</c>
    ///     catches.
    ///   </item>
    ///   <item>
    ///     <b>SendServerParameters loop regression at SectorManager.cpp:264-293.</b>
    ///     The current implementation emits exactly one SendOpcode call per
    ///     invocation. A future refactor that wrapped the SendOpcode in a
    ///     loop (e.g., to emit per-sub-sector params) without guarding the
    ///     iteration count would inflate the emit count. <c>Assert.Single</c>
    ///     catches.
    ///   </item>
    ///   <item>
    ///     <b>Spurious mid-handshake re-emit from a sector handoff path.</b>
    ///     If a handler in SectorLogin / SectorLogin2 was modified to re-emit
    ///     0x0042 on the same connection (e.g., as part of a Phase K fidelity
    ///     fix for sector transitions), <c>Assert.Single</c> catches the
    ///     extra emit. This is the same invariant class as Wave 91-103's
    ///     sibling pinning waves.
    ///   </item>
    ///   <item>
    ///     <b>StationLogin path leak.</b> If a future refactor moved
    ///     SendServerParameters into a shared helper that StationLogin
    ///     accidentally inherited, a station-only handshake would gain
    ///     a 0x0042 emit. (Not covered by this test directly — this
    ///     test is space-arm only — but the symmetric station-arm
    ///     coverage is implicit via the existing handshake-fanout tests
    ///     that would see an unexpected 0x0042 frame in their station
    ///     captures.)
    ///   </item>
    ///   <item>
    ///     <b>HandshakeFrames capture regression at SectorHandshake.cs:194-205.</b>
    ///     The Wave 68 harness addition populates Session.HandshakeFrames
    ///     during the ReestablishAsync drain loop. If a future refactor
    ///     drops or under-counts the frame-capture path, the
    ///     <c>Assert.Single</c> over-counts or under-counts versus
    ///     reality. This is the same regression class as Wave 87's
    ///     length-only assertion would see.
    ///   </item>
    ///   <item>
    ///     <b>Stage 1 → Stage 2 logoff-and-reconnect path regression at
    ///     SectorHandshake.cs:436-446.</b> Mirror of Wave 87's same item
    ///     — a bare TCP disconnect leaves the in-memory Player around,
    ///     stage-2 GlobalConnect hits G_ERROR_ACCOUNT_IN_USE, both
    ///     Wave 87's and Wave 104's assertions surface the failure
    ///     mode identically.
    ///   </item>
    /// </list>
    ///
    /// <para>
    /// Budget: 120s. Stage 1 handshake ~2s; stage 1 logoff ~1s; stage 2
    /// re-handshake ~2s; assertions run synchronously. No additional
    /// client stimulus.
    /// </para>
    ///
    /// <para>
    /// Per CLAUDE.md server-integrity: pure passive-observation
    /// tightening of a pre-existing single-emit invariant. No client
    /// stimulus, no server change, no permissiveness added.
    /// </para>
    /// </summary>
    [Fact]
    public async Task ServerParameters_EmittedExactlyOnceDuringSpaceSectorHandshake_PinsSelfEmit()
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

        var stationSession = await SectorHandshake.EstablishAsync(
            _server, login.Ticket!, account.Username, slot, stationSectorId,
            firstName: "SrvPar104", shipName: "SrvPar104Ship", cts.Token);

        try
        {
            byte[] logoffPayload = new byte[8];
            await stationSession.Sector.SendAsync(
                Packet.ForOpcode(OpcodeId.Known.LogoffRequest.Value, logoffPayload),
                cts.Token);
            await SectorHandshake.DrainUntilOpcode(
                stationSession.Sector, OpcodeId.Known.LogoffConfirmation.Value, cts.Token);
            await stationSession.DisposeAsync();

            await using var spaceSession = await SectorHandshake.ReestablishAsync(
                _server, login.Ticket!, slot, spaceSectorId, cts.Token);

            var serverParametersFrames = spaceSession.HandshakeFrames
                .Where(f => f.Opcode == OpcodeId.Known.ServerParameters.Value)
                .ToList();

            // Wave 104 pins the 1-frame invariant: SendServerParameters
            // self-emit (SectorManager.cpp:292) — the unique
            // ENB_OPCODE_0042_SERVER_PARAMETERS SendOpcode call site in
            // the server.
            var single = Assert.Single(serverParametersFrames);
            Assert.Equal(ExpectedServerParametersPayloadLength, single.PayloadLength);
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
