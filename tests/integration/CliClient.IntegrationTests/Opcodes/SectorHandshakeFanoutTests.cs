// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Buffers.Binary;
using N7.CliClient.Auth;
using N7.CliClient.Net;
using N7.CliClient.Opcodes;
using Xunit;

namespace N7.CliClient.IntegrationTests.Opcodes;

/// <summary>
/// Wave 34 passive-observation +2 ratchet: the server emits a dozen-plus
/// opcodes on the sector-login handshake stream (everything pushed by
/// <c>SectorManager::SectorLogin2</c>'s SendLoginShipData → SendShipInfo
/// → SendServerParameters → SendAllNavs → SendVaMessage chain before
/// <c>SendStart</c> closes the handshake with 0x0005). Before Wave 34
/// the harness <i>discarded</i> those frames — <c>SectorHandshake</c>'s
/// drain loop only watched for the 0x0005 terminator and returned the
/// connection. Wave 34 captures the in-order opcode list as
/// <see cref="SectorHandshake.Session.HandshakeOpcodes"/> so
/// passive-observation tests can assert on the server-originated emits
/// for free, without burning a new client stimulus per opcode.
///
/// <para>
/// This test asserts that two such opcodes — 0x0047 CLIENT_SHIP and
/// 0x0061 AVATAR_DESCRIPTION — both appear in the captured list. Both
/// come from <c>Player::SendLoginShipData</c>
/// (<c>server/src/PlayerClass.cpp:857-901</c>): the handler builds a
/// 4-byte int32_t GameID wire word and emits it once as
/// ENB_OPCODE_0037_CLIENT_AVATAR (line 879) and once as
/// ENB_OPCODE_0047_CLIENT_SHIP (line 880), then constructs an
/// <c>AvatarDescription</c> struct (lines 882-887) and emits it as
/// ENB_OPCODE_0061_AVATAR_DESCRIPTION (line 889). All three SendOpcode
/// calls go through the per-client UDP queue and end up framed inside
/// 0x2016 PACKET_SEQUENCE wrappers on the wire.
/// </para>
///
/// <para>
/// Why <c>SendLoginShipData</c> runs before <c>SendStart</c>:
/// <c>SectorManager::SectorLogin2</c>
/// (<c>server/src/SectorManager.cpp:354-380</c>) calls
/// <c>player-&gt;SendLoginShipData()</c> as part of the per-stage
/// chain that <c>PlayerManager::ProcessNextLoginStage</c> walks before
/// the final <c>PlayerManager::SendStart</c> call
/// (<c>server/src/PlayerConnection.cpp:1068</c>) emits 0x0005 START.
/// Every handshake that reaches the 0x0005 terminator therefore
/// necessarily ran through both the 0x0047 and 0x0061 emits first.
/// </para>
///
/// <para>
/// Why this is a legit +2 ratchet (not a Coverage-Cheat). 0x0047 and
/// 0x0061 are <i>server-originated</i> opcodes that the server has been
/// emitting on every prior wave's handshake — they just weren't
/// observed by any test until now. Wave 34 doesn't add a new server
/// behaviour, doesn't fabricate a reply, doesn't widen any input
/// acceptance; it just records ground truth that was already being
/// produced. Per CLAUDE.md server-integrity, this is exactly the
/// "tightening" the rule explicitly welcomes (we're rejecting more
/// regression classes without changing what the server accepts).
/// </para>
///
/// <para>
/// Other handshake opcodes left on the table for future waves: the
/// captured list typically also contains 0x0037 CLIENT_AVATAR (already
/// in OpcodeId.Known but not in TestedOpcodes — Wave 35 candidate),
/// plus a wider fan of SendCreate / SendSubparts / SendShipColorization
/// / SendDecal / SendNameDecal / SendRelationship /
/// SendAdvancedPositionalUpdate / SendShipInfo / SendServerParameters /
/// SendAllNavs / SendVaMessage / 0x2020 LOGIN_STAGE_S_C inner-opcode
/// emits. Each is a +1 ratchet opportunity if we list its opcode in
/// <see cref="OpcodeId.Known"/>, assert its presence in HandshakeOpcodes,
/// and add a row in <c>TestedOpcodes.Opcodes</c>. Wave 34 lands the
/// two emits with the cleanest documentary lineage
/// (PlayerClass.cpp:857-901 has them as bare SendOpcode calls with
/// well-defined opcode constants); subsequent waves can do batches.
/// </para>
///
/// <para>
/// Regression classes this catches.
/// </para>
/// <list type="bullet">
///   <item>
///     <b>SendLoginShipData removal or short-circuit.</b> If the call
///     vanishes from SectorLogin2's chain or any of the SendOpcode
///     calls inside it are guarded away, the corresponding opcode
///     wouldn't appear in HandshakeOpcodes. Test surfaces immediately.
///   </item>
///   <item>
///     <b>SectorLogin2 call ordering regression.</b> If
///     SendLoginShipData moves to <i>after</i> SendStart, the
///     0x0047/0x0061 frames would arrive past the 0x0005 terminator —
///     DoSectorLoginUntilStartAsync would have already returned and
///     the test would see an empty/incomplete HandshakeOpcodes list.
///   </item>
///   <item>
///     <b>sizeof(long) marshalling regression at PlayerClass.cpp:878.</b>
///     Pre-Phase-K the handler passed <c>&amp;m_CreateInfo.GameID</c>
///     directly with <c>sizeof(long)</c> = 8 bytes on Linux x86_64
///     from a 4-byte wire slot. The fix marshals through a stack
///     <c>int32_t game_id_wire</c> temporary. A revert would emit 4
///     bytes of high-half garbage and the proxy's PACKET_SEQUENCE
///     inner-tuple parser would mis-frame the 0x0061 frame two lines
///     later — knocking out <i>both</i> entries from HandshakeOpcodes.
///   </item>
///   <item>
///     <b>SendOpcode header-width revert at PlayerConnection.cpp:127.</b>
///     Would corrupt every reply opcode in the 0x2016 PACKET_SEQUENCE
///     inner-tuple parser. Neither 0x0047 nor 0x0061 would appear
///     under the correct opcode label.
///   </item>
///   <item>
///     <b>Proxy SendClientPacketSequence inner-opcode guard
///     tightening at UDPProxyToClient_linux.cpp:568.</b> Currently
///     passes both 0x0047 and 0x0061 (both &lt; 0x0FFF). A regression
///     to a tighter upper bound (e.g. opcode &lt; 0x0040 or
///     opcode &lt; 0x0060) would silently drop one or both from the
///     wire and the test would see a missing-opcode failure.
///   </item>
///   <item>
///     <b>DoSectorLoginUntilStartAsync drain-loop regression
///     (Opcodes/SectorHandshake.cs:330-346).</b> If the loop body
///     stops appending to the opcodes list, or terminates on the
///     wrong sentinel, the captured list would be empty/wrong. Wave
///     34 exercises the capture path itself — every subsequent
///     handshake-opcode wave depends on it working.
///   </item>
/// </list>
///
/// <para>
/// Budget: 90s. Handshake ~2s; no additional client stimulus after the
/// session establishes — the assertions run synchronously against
/// already-captured state.
/// </para>
/// </summary>
[Collection(ServerCollection.Name)]
public sealed class SectorHandshakeFanoutTests
{
    private readonly ServerFixture _server;
    private readonly ClientFixture _client;

    public SectorHandshakeFanoutTests(ServerFixture server)
    {
        _server = server;
        _client = new ClientFixture(server);
    }

    [Fact]
    public async Task HandshakeEmitsClientShipAndAvatarDescription()
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
            firstName: "Fanout", shipName: "FanoutShip", cts.Token);

        try
        {
            // SectorHandshake.DoSectorLoginUntilStartAsync now captures
            // every opcode received between the LOGIN frame and the
            // terminating 0x0005 START into Session.HandshakeOpcodes.
            // Per Player::SendLoginShipData (server/src/PlayerClass.cpp:857-901),
            // the server emits 0x0047 CLIENT_SHIP (line 880) and
            // 0x0061 AVATAR_DESCRIPTION (line 889) before SendStart
            // closes the handshake.
            Assert.Contains(OpcodeId.Known.ClientShip.Value, session.HandshakeOpcodes);
            Assert.Contains(OpcodeId.Known.AvatarDescription.Value, session.HandshakeOpcodes);
        }
        finally
        {
            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await SectorHandshake.DeleteCreatedCharacterAsync(session.Global, slot, cleanupCts.Token); }
            catch { /* best-effort cleanup */ }
        }
    }

    /// <summary>
    /// Wave 35 passive-observation +14 ratchet: assert that the full
    /// fan-out of opcodes captured during the sector-login handshake
    /// are all present in <see cref="SectorHandshake.Session.HandshakeOpcodes"/>.
    /// All 14 are server-originated emits the server has been pushing
    /// on every prior wave's handshake — Wave 35 simply records them
    /// as ground truth.
    ///
    /// <para>
    /// Per-opcode emit citations (kept in <c>TestedOpcodes.cs</c> for
    /// each entry's regression-class commentary): 0x0004 CREATE
    /// (PlayerConnection.cpp:1531), 0x0010 DECAL (PlayerConnection.cpp:1182),
    /// 0x0011 COLORIZATION (PlayerClass.cpp:1363), 0x001B AUX_DATA
    /// (PlayerClass.cpp:959+et al — multi-site SendAux*), 0x0025 ITEM_BASE
    /// (ItemBaseManager.cpp:114), 0x0037 CLIENT_AVATAR (PlayerClass.cpp:879),
    /// 0x003E ADVANCED_POSITIONAL_UPDATE (PlayerConnection.cpp:1383),
    /// 0x0040 CONSTANT_POSITIONAL_UPDATE (PlayerConnection.cpp:1217),
    /// 0x004F STARBASE_SET (PlayerConnection.cpp:716), 0x0052 LOUNGE_NPC
    /// (PlayerConnection.cpp:9721), 0x007F MANUFACTURE_SET_MANUFACTURE_ID
    /// (PlayerConnection.cpp:10129), 0x0089 RELATIONSHIP
    /// (PlayerConnection.cpp:10698), 0x00B2 NAME_DECAL
    /// (PlayerConnection.cpp:1197), 0x00B4 SUBPARTS (PlayerClass.cpp:1041).
    /// </para>
    ///
    /// <para>
    /// All 14 opcodes ride the standard SendOpcode→m_UDPQueue→
    /// SendPacketCache→0x2016 PACKET_SEQUENCE→proxy SendClientPacketSequence→
    /// TCP fan-out path. Per CLAUDE.md server-integrity: server-originated,
    /// no new client stimulus, no server change, no widened input
    /// acceptance — this is exactly the "tightening" the rule welcomes.
    /// </para>
    /// </summary>
    [Fact]
    public async Task HandshakeEmitsFullSendLoginShipDataFanout()
    {
        var account = TestAccounts.For();
        const int slot = 0;
        const int sectorId = 10151;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        var login = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(account.Username, account.Password), cts.Token);
        Assert.True(login.Valid, $"login: {login.RawBody.TrimEnd()}");

        await using var session = await SectorHandshake.EstablishAsync(
            _server, login.Ticket!, account.Username, slot, sectorId,
            firstName: "Fanout", shipName: "FanoutShip2", cts.Token);

        try
        {
            // 14 opcodes from the captured handshake stream, sorted by
            // opcode value. Each assertion produces a clean per-opcode
            // failure message identifying which emit went missing.
            Assert.Contains(OpcodeId.Known.Create.Value, session.HandshakeOpcodes);
            Assert.Contains(OpcodeId.Known.Decal.Value, session.HandshakeOpcodes);
            Assert.Contains(OpcodeId.Known.Colorization.Value, session.HandshakeOpcodes);
            Assert.Contains(OpcodeId.Known.AuxData.Value, session.HandshakeOpcodes);
            Assert.Contains(OpcodeId.Known.ItemBase.Value, session.HandshakeOpcodes);
            Assert.Contains(OpcodeId.Known.ClientAvatar.Value, session.HandshakeOpcodes);
            Assert.Contains(OpcodeId.Known.AdvancedPositionalUpdate.Value, session.HandshakeOpcodes);
            Assert.Contains(OpcodeId.Known.ConstantPositionalUpdate.Value, session.HandshakeOpcodes);
            Assert.Contains(OpcodeId.Known.StarbaseSet.Value, session.HandshakeOpcodes);
            Assert.Contains(OpcodeId.Known.LoungeNpc.Value, session.HandshakeOpcodes);
            Assert.Contains(OpcodeId.Known.ManufactureSetManufactureId.Value, session.HandshakeOpcodes);
            Assert.Contains(OpcodeId.Known.Relationship.Value, session.HandshakeOpcodes);
            Assert.Contains(OpcodeId.Known.NameDecal.Value, session.HandshakeOpcodes);
            Assert.Contains(OpcodeId.Known.Subparts.Value, session.HandshakeOpcodes);
        }
        finally
        {
            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await SectorHandshake.DeleteCreatedCharacterAsync(session.Global, slot, cleanupCts.Token); }
            catch { /* best-effort cleanup */ }
        }
    }

    /// <summary>
    /// Wave 52 passive-observation +2 ratchet: assert that 0x003C
    /// CLIENT_TYPE and 0x0097 GALAXY_MAP both appear in the captured
    /// handshake stream when the LOGIN packet targets a SPACE sector
    /// (<c>sector_id &lt;= 9999</c>) rather than a station
    /// (<c>sector_id &gt; 9999</c>).
    ///
    /// <para>
    /// Why a separate test from <see cref="HandshakeEmitsFullSendLoginShipDataFanout"/>:
    /// all prior handshake-fanout tests use sectorId=10151 (Luna Station)
    /// which routes through <c>SectorManager::HandleSectorLogin</c>'s
    /// station branch — <c>SectorManager::StationLogin</c>
    /// (<c>server/src/SectorManager.cpp:460-505</c>) — where neither
    /// SendClientType nor SendGalaxyMap is called. SendGalaxyMap is
    /// literally commented out at SectorManager.cpp:467 in the station
    /// path, and SendClientType is only called from the SPACE-sector
    /// branch at SectorManager.cpp:347. To observe either opcode the
    /// LOGIN packet has to target a sector with id ≤ 9999.
    /// </para>
    ///
    /// <para>
    /// Choice of sector: 1015 = "Luna" (sector_type=1 ST_PLANET, system
    /// 378 Sol). Picked because (a) it's the closest space neighbour to
    /// the avatar's stored Luna Station position (10151), so the
    /// post-login object/range-list bookkeeping is exercising the same
    /// Sol-system code paths the existing fanout tests touch; and
    /// (b) sector_type=1 means SendClientType's payload is a non-zero
    /// int32 (the literal sector_type integer is shipped to the client
    /// — verifies the wire field isn't getting zeroed by a regression).
    /// </para>
    ///
    /// <para>
    /// Dispatch path. <c>Player::HandleLogin</c>
    /// (<c>server/src/PlayerConnection.cpp:674-696</c>) reads
    /// <c>ToSectorID</c> from the LOGIN payload, calls
    /// <c>PlayerIndex()-&gt;SetSectorNum(sector_id)</c> and advances the
    /// stage machine. <c>Player::GetSectorManager</c>
    /// (<c>server/src/PlayerMisc.cpp:354-357</c>) then resolves to
    /// <c>g_ServerMgr-&gt;GetSectorManager(sector_id)</c> — i.e. the
    /// LOGIN-packet's sector id IS what selects the SectorManager.
    /// <c>SectorManager::HandleSectorLogin</c>
    /// (<c>server/src/SectorManager.cpp:324-336</c>) then routes
    /// <c>m_SectorID &gt; 9999</c> to StationLogin, else to SectorLogin.
    /// Passing 1015 puts us in SectorLogin where the two opcodes emit.
    /// </para>
    ///
    /// <para>
    /// 0x003C CLIENT_TYPE emit: <c>SectorManager::SectorLogin</c>
    /// (<c>server/src/SectorManager.cpp:347</c>) calls
    /// <c>player-&gt;SendClientType(m_SectorData-&gt;sector_type)</c> which
    /// invokes <c>Player::SendClientType</c>
    /// (<c>server/src/PlayerConnection.cpp:1071-1075</c>) — a two-line
    /// handler that wires the client_type long to
    /// <c>SendOpcode(ENB_OPCODE_003C_CLIENT_TYPE, ..., sizeof(client_type))</c>.
    /// Unconditional emit; no payload-shape guards.
    /// </para>
    ///
    /// <para>
    /// 0x0097 GALAXY_MAP emit: <c>SectorManager::SectorLogin</c>
    /// (<c>server/src/SectorManager.cpp:344</c>) calls
    /// <c>player-&gt;SendGalaxyMap(m_SystemName, m_SectorName, "")</c>
    /// which invokes <c>Player::SendGalaxyMap</c>
    /// (<c>server/src/PlayerConnection.cpp:10709-10758</c>) — builds a
    /// variable-length packet with three short-prefixed strings
    /// (system / sector / station, with station="" in the SectorLogin
    /// call site) plus a trailing int32 "unknown" sentinel (375), and
    /// emits as <c>SendOpcode(ENB_OPCODE_0097_GALAXY_MAP, ..., size+8)</c>.
    /// Unconditional emit; no payload-shape guards.
    /// </para>
    ///
    /// <para>
    /// Why this is a legit +2 ratchet (not a Coverage-Cheat). Both
    /// opcodes are server-originated emits that have always been
    /// produced on every space-sector handshake — they just weren't
    /// observed because no prior test sent a space-sector LOGIN. No new
    /// server behaviour, no new permissiveness, no widened input
    /// acceptance; the test simply records ground truth from a
    /// dispatch-path branch the suite hadn't exercised. Per CLAUDE.md
    /// server-integrity, this is exactly the "tightening" the rule
    /// welcomes (rejecting more regression classes without changing
    /// what the server accepts).
    /// </para>
    ///
    /// <para>
    /// Regression classes this catches.
    /// </para>
    /// <list type="bullet">
    ///   <item>
    ///     <b>SectorLogin dispatch-branch deletion or short-circuit.</b>
    ///     If <c>HandleSectorLogin</c>'s SPACE-vs-station split
    ///     collapses to "always StationLogin", neither 0x003C nor
    ///     0x0097 would appear in HandshakeOpcodes — distinguishes
    ///     "the SectorLogin path stopped running" from "individual
    ///     SendOpcode calls were removed".
    ///   </item>
    ///   <item>
    ///     <b>SendClientType removal or guard regression.</b> Any
    ///     conditional gate added around SendClientType (e.g. only
    ///     emitting if sector_type != 0) would break the assertion
    ///     for ST_PLANET sectors.
    ///   </item>
    ///   <item>
    ///     <b>SendGalaxyMap removal or guard regression.</b> If
    ///     SectorLogin's GalaxyMap emit at line 344 gets the same
    ///     commenting-out treatment as the line-467 station-path
    ///     copy, 0x0097 would silently vanish from the space-sector
    ///     handshake.
    ///   </item>
    ///   <item>
    ///     <b>sizeof(long) marshalling regression in SendClientType.</b>
    ///     The handler currently passes <c>sizeof(client_type)</c> with
    ///     client_type declared <c>long</c> — on Linux x86_64 that's 8
    ///     bytes for a wire field that needs to be 4 (Phase K Wave 7 +
    ///     11 class of bug). The proxy's PACKET_SEQUENCE inner-tuple
    ///     parser uses the SendOpcode length byte to split inner
    ///     frames, so an oversize length corrupts the very next inner
    ///     opcode — could knock 0x003C OR a neighbour opcode out of
    ///     HandshakeOpcodes. Documented here so a future Phase K wave
    ///     auditing remaining sizeof(long) sites has a test ready to
    ///     fail when the marshalling fix lands.
    ///   </item>
    ///   <item>
    ///     <b>GalaxyMap struct layout regression.</b>
    ///     PlayerConnection.cpp:10714-10721 was explicitly hardened in
    ///     Phase K Wave 11 to use int32_t for Type/Size/PlayerID/unknown
    ///     (Win32 wire layout = 80B; Linux unfixed = 96B with shifted
    ///     offsets). A revert would still emit opcode 0x0097 but with a
    ///     mangled payload — assertion would pass at opcode level but a
    ///     future typed-codec wave would catch the byte-layout drift.
    ///   </item>
    ///   <item>
    ///     <b>Proxy SendClientPacketSequence inner-opcode guard
    ///     tightening at <c>proxy/UDPProxyToClient_linux.cpp:568</c>.</b>
    ///     Currently passes both opcodes (0x003C &lt; 0x0FFF and 0x0097
    ///     &lt; 0x0FFF). A regression to a tighter upper bound would
    ///     silently drop them from the wire and the test would see a
    ///     missing-opcode failure.
    ///   </item>
    /// </list>
    ///
    /// <para>
    /// Budget: 90s. Handshake ~2s; passive observation — no additional
    /// client stimulus after the session establishes.
    /// </para>
    /// </summary>
    [Fact]
    public async Task HandshakeEmitsClientTypeAndGalaxyMapOnSpaceSectorLogin()
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
        // Race=0 Profession=0 (Terran Warrior) → StartSector=10151.
        // ReadSavedData takes the ReInitializeSavedData path (no
        // avatar_level_info row yet) which writes sector_id=10151 plus
        // the full avatar/skill seed needed to subsequently reload.
        var stationSession = await SectorHandshake.EstablishAsync(
            _server, login.Ticket!, account.Username, slot, stationSectorId,
            firstName: "SpaceHs", shipName: "SpaceHsShip", cts.Token);

        try
        {
            // Cleanly tear down stage 1 with an explicit 0x00B9
            // LOGOFF_REQUEST so the server runs DropPlayerFromGalaxy
            // synchronously. A bare TCP disconnect leaves the in-memory
            // Player around long enough that the stage-2 GlobalConnect
            // hits G_ERROR_ACCOUNT_IN_USE (UDP_Global.cpp:166-170).
            byte[] logoffPayload = new byte[8];
            await stationSession.Sector.SendAsync(
                Packet.ForOpcode(OpcodeId.Known.LogoffRequest.Value, logoffPayload),
                cts.Token);
            await SectorHandshake.DrainUntilOpcode(
                stationSession.Sector, OpcodeId.Known.LogoffConfirmation.Value, cts.Token);
            await stationSession.DisposeAsync();

            // Stage 2: reconnect (no char create) and LOGIN to space
            // sector 1015. ReadSavedData now takes the ReloadSavedData
            // path (avatar_level_info exists from stage 1), which
            // preserves the sector_num set by HandleLogin
            // (PlayerSaves.cpp:289-291). GetSectorManager(1015)
            // resolves m_SectorID=1015 ≤ 9999 →
            // SectorManager::HandleSectorLogin dispatches to SectorLogin
            // (SectorManager.cpp:332), which emits 0x0097 GALAXY_MAP
            // (line 344) and 0x003C CLIENT_TYPE (line 347).
            await using var spaceSession = await SectorHandshake.ReestablishAsync(
                _server, login.Ticket!, slot, spaceSectorId, cts.Token);

            Assert.Contains(OpcodeId.Known.ClientType.Value, spaceSession.HandshakeOpcodes);
            Assert.Contains(OpcodeId.Known.GalaxyMap.Value, spaceSession.HandshakeOpcodes);
        }
        finally
        {
            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try
            {
                await using var cleanupGlobal = await N7.CliClient.Net.EncryptedTcpConnection.ConnectAsync(
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
    /// Wave 53 passive-observation +1 ratchet: assert that 0x0042
    /// SERVER_PARAMETERS appears in the captured handshake stream when
    /// the LOGIN packet targets a SPACE sector. SERVER_PARAMETERS is
    /// emitted exclusively from the SPACE-sector branch of stage-2
    /// dispatch (<c>SectorManager::HandleSectorLogin2</c> at
    /// <c>server/src/SectorManager.cpp:295-304</c>):
    /// <c>SectorManager::SectorLogin2</c>
    /// (<c>server/src/SectorManager.cpp:354-380</c>) calls
    /// <c>SendServerParameters(player)</c> at line 364, which builds a
    /// <c>ServerParameters</c> struct and emits
    /// <c>SendOpcode(ENB_OPCODE_0042_SERVER_PARAMETERS, ..., sizeof(parameters))</c>
    /// at <c>SectorManager.cpp:292</c>.
    /// <c>SectorManager::StationLogin2</c>
    /// (<c>server/src/SectorManager.cpp:507-535</c>) — the m_SectorID&gt;9999
    /// branch — does NOT call <c>SendServerParameters</c>, which is why
    /// no prior handshake-fanout test using sectorId=10151 (Luna Station)
    /// ever captured 0x0042.
    ///
    /// <para>
    /// Builds on Wave 52's 2-stage login pattern: stage 1 establishes the
    /// character at the StartSector (Luna Station, 10151) so
    /// <c>ReadSavedData</c> takes the
    /// <c>ReInitializeSavedData</c> seed path; stage 2 reconnects (no
    /// char-create) targeting space sector 1015, where
    /// <c>ReadSavedData</c> takes the <c>ReloadSavedData</c> path that
    /// preserves the sector_num set by <c>HandleLogin</c>
    /// (<c>PlayerSaves.cpp:289-291</c>). The reconnect's stage machine
    /// then walks SectorLogin → SectorLogin2 → SectorLogin3, and the
    /// SectorLogin2 SendServerParameters emit lands in the captured
    /// HandshakeOpcodes list.
    /// </para>
    ///
    /// <para>
    /// Why a separate test from Wave 52 (which already runs the same
    /// 2-stage login): clean per-wave failure attribution and isolated
    /// account ownership — if Wave 53 ever fails it's specifically the
    /// SERVER_PARAMETERS emit (or SectorLogin2 dispatch) that broke,
    /// not the SectorLogin (stage-1) GALAXY_MAP/CLIENT_TYPE path.
    /// Conceptually 0x0042 lives in a different dispatch sub-stage
    /// (HandleSectorLogin2, not HandleSectorLogin) so collapsing them
    /// into one test would lose that signal.
    /// </para>
    ///
    /// <para>
    /// 0x0042 wire layout: the <c>ServerParameters</c> struct at
    /// <c>SectorManager.cpp:264-291</c> packs four float speed
    /// multipliers, four float backdrop scalars, and a backdrop
    /// base-asset int — the receiving client reads it once at
    /// SectorLogin2 time to set sector-wide ship-speed scalars and the
    /// skybox. The struct is currently 36 bytes per the canonical
    /// <c>ServerParameters</c> definition in
    /// <c>common/include/net7/PacketStructures.h</c>; a sizeof regression
    /// here would still produce opcode 0x0042 but with a mangled
    /// payload (assertion would pass at opcode level but a future
    /// typed-codec wave would catch the byte-layout drift).
    /// </para>
    ///
    /// <para>
    /// Why this is a legit +1 ratchet (not a Coverage-Cheat). 0x0042 is
    /// a server-originated emit that has always been produced on every
    /// space-sector stage-2 handshake — it just wasn't observed because
    /// no prior test reached stage 2 on the SectorLogin path. No new
    /// server behaviour, no new permissiveness, no widened input
    /// acceptance. Per CLAUDE.md server-integrity, this is exactly the
    /// "tightening" the rule welcomes.
    /// </para>
    ///
    /// <para>
    /// Regression classes this catches.
    /// </para>
    /// <list type="bullet">
    ///   <item>
    ///     <b>SectorLogin2 dispatch-branch deletion.</b> If
    ///     <c>HandleSectorLogin2</c>'s SPACE-vs-station split
    ///     (<c>SectorManager.cpp:297-303</c>) collapses to "always
    ///     StationLogin2", 0x0042 would silently vanish from the
    ///     space-sector handshake.
    ///   </item>
    ///   <item>
    ///     <b>SendServerParameters removal or guard regression.</b> Any
    ///     conditional gate around <c>SendServerParameters(player)</c> at
    ///     <c>SectorManager.cpp:364</c> (e.g. only emitting for certain
    ///     sector_data flags) would break the assertion. The current
    ///     handler is unconditional.
    ///   </item>
    ///   <item>
    ///     <b>SendOpcode header-width revert at
    ///     <c>PlayerConnection.cpp:127</c>.</b> Would corrupt every
    ///     inner opcode in the 0x2016 PACKET_SEQUENCE parser; 0x0042
    ///     would not appear under its correct label.
    ///   </item>
    ///   <item>
    ///     <b>Proxy SendClientPacketSequence inner-opcode guard
    ///     tightening at <c>proxy/UDPProxyToClient_linux.cpp:568</c>.</b>
    ///     Currently passes 0x0042 (&lt; 0x0FFF). A regression to a
    ///     tighter upper bound would silently drop it from the wire.
    ///   </item>
    ///   <item>
    ///     <b>SectorLogin2 stage-machine progression regression.</b> If
    ///     <c>PlayerManager::ProcessNextLoginStage</c> stops advancing
    ///     past HandleSectorLogin into HandleSectorLogin2, the entire
    ///     SectorLogin2 emit fan (0x0042 plus SendShipInfo's downstream
    ///     ItemBase/AuxData chain, SendAllNavs, etc.) would be missing
    ///     from the captured stream. 0x0042 is the cleanest single-emit
    ///     signal for that stage being reached.
    ///   </item>
    ///   <item>
    ///     <b>DoSectorLoginUntilStartAsync drain-loop regression
    ///     (<c>Opcodes/SectorHandshake.cs:330-346</c>).</b> Same as
    ///     Wave 34/35/52 — exercises the capture path itself.
    ///   </item>
    /// </list>
    ///
    /// <para>
    /// Budget: 120s (same as Wave 52 — the 2-stage login pattern needs
    /// time for the explicit logoff round-trip between stages).
    /// </para>
    /// </summary>
    [Fact]
    public async Task HandshakeEmitsServerParametersOnSpaceSectorLogin()
    {
        var account = TestAccounts.For();
        const int slot = 0;
        const int stationSectorId = 10151;
        const int spaceSectorId = 1015;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120));

        var login = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(account.Username, account.Password), cts.Token);
        Assert.True(login.Valid, $"login: {login.RawBody.TrimEnd()}");
        Assert.False(string.IsNullOrEmpty(login.Ticket));

        var stationSession = await SectorHandshake.EstablishAsync(
            _server, login.Ticket!, account.Username, slot, stationSectorId,
            firstName: "SrvParam", shipName: "SrvParamShip", cts.Token);

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

            Assert.Contains(OpcodeId.Known.ServerParameters.Value, spaceSession.HandshakeOpcodes);
        }
        finally
        {
            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try
            {
                await using var cleanupGlobal = await N7.CliClient.Net.EncryptedTcpConnection.ConnectAsync(
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
    /// Wave 54: assert 0x003F PLANET_POSITIONAL_UPDATE and 0x0099
    /// NAVIGATION are both present in <see cref="SectorHandshake.Session.HandshakeOpcodes"/>
    /// after a SectorLogin2 (space) handshake into sector 1015.
    ///
    /// <para>
    /// Why these opcodes are space-only. Both ride
    /// <c>ObjectManager::SendAllNavs</c>
    /// (<c>server/src/ObjectManager.cpp:406-424</c>), which is called
    /// from <c>SectorManager::SectorLogin2</c>
    /// (<c>server/src/SectorManager.cpp:369</c>) — the SPACE arm of
    /// <c>HandleSectorLogin2</c>'s dispatch
    /// (<c>SectorManager.cpp:297-303</c>). The STATION arm
    /// <c>StationLogin2</c> at <c>SectorManager.cpp:507-535</c> does
    /// NOT call <c>SendAllNavs</c> — stations have no static nav
    /// objects (only the manufacturing-lab pseudo-object created in
    /// <c>StationLogin</c>'s <c>SendCreate(ManuID, ..., CREATE_MANU_LAB)</c>
    /// call at <c>SectorManager.cpp:478</c>). So both opcodes are
    /// strictly diagnostic of "we reached the space-sector
    /// SendAllNavs fan-out", complementing Wave 53's 0x0042
    /// SERVER_PARAMETERS which only proves "we reached SectorLogin2".
    /// </para>
    ///
    /// <para>
    /// What SendAllNavs emits, in order. Iterates
    /// <c>m_StaticSectorList</c> (the sector's static-object roster
    /// loaded at sector-thread spawn time), skipping OT_DECO and
    /// inactive entries. For each remaining object: if it's OT_PLANET
    /// OR was previously in the player's <c>ExposedNavList</c>, AND
    /// not already in <c>ObjectRangeList</c>, calls
    /// <c>obj-&gt;SendObject(player)</c> which fans out
    /// per-object-type. For OT_PLANET the path emits 0x0004 CREATE
    /// (object create), 0x0089 RELATIONSHIP, 0x003F
    /// PLANET_POSITIONAL_UPDATE (orbital position — distinct from
    /// 0x003E ADVANCED for non-orbital and 0x0040 CONSTANT for
    /// static), 0x001B AUX_DATA (planet aux info), 0x0099 NAVIGATION
    /// (the nav-point metadata for the planet — game_id + sig +
    /// visited byte + nav_type + is_huge per
    /// <c>PlayerConnection.cpp:1138-1148</c>'s
    /// <c>SendNavigation</c>). The sequence repeats per nav object;
    /// our test asserts only that BOTH 0x003F and 0x0099 are present
    /// at least once, since sector 1015's exact planet/nav roster is
    /// data-driven and shouldn't anchor a regression test.
    /// </para>
    ///
    /// <para>
    /// Builds on Wave 52/53's 2-stage login pattern. Stage 1 lands at
    /// Luna Station (10151) to seed avatar_level_info; stage 2
    /// re-establishes into space sector 1015 where
    /// <c>ReadSavedData</c>'s ReloadSavedData path preserves the
    /// LOGIN-frame sector_num. The reestablish drives SectorLogin →
    /// SectorLogin2 (space arm) → SendAllNavs.
    /// </para>
    ///
    /// <para>
    /// Why a +2 ratchet (0x003F and 0x0099 in the same wave). Both
    /// opcodes ride the same emit site (per-planet SendObject), so a
    /// regression that takes out one almost certainly takes out the
    /// other. Splitting into two waves would mean two near-identical
    /// 90-second test runs against the same code path. Bundling
    /// them keeps the live-stack cost amortised; we still get
    /// distinct named-opcode rows in TestedOpcodes so a future ratchet
    /// audit sees them as discrete coverage units.
    /// </para>
    ///
    /// <para>
    /// Why this is a legit +2 ratchet (not a Coverage-Cheat). Both
    /// opcodes are server-originated emits the retail server has
    /// always produced on every space-sector handshake — they just
    /// weren't observed because no prior test ran a stage-2 login
    /// into a populated space sector AND captured the post-0x0042
    /// portion of the drain. No new server behaviour, no new
    /// permissiveness, no widened input acceptance. Per CLAUDE.md
    /// server-integrity, this is exactly the "tightening" the rule
    /// welcomes.
    /// </para>
    ///
    /// <para>
    /// Regression classes this catches.
    /// </para>
    /// <list type="bullet">
    ///   <item>
    ///     <b>SendAllNavs removal or guard regression.</b> If
    ///     <c>SectorManager::SectorLogin2</c>'s
    ///     <c>m_ObjectMgr-&gt;SendAllNavs(player)</c> call at
    ///     <c>SectorManager.cpp:369</c> is deleted or gated behind a
    ///     condition that doesn't fire for sector 1015, both 0x003F
    ///     and 0x0099 vanish from the handshake.
    ///   </item>
    ///   <item>
    ///     <b>SendAllNavs iterator-skip regression.</b> If the
    ///     OT_PLANET-or-previously-exposed gate at
    ///     <c>ObjectManager.cpp:416</c> is inverted (e.g. accidentally
    ///     skipping OT_PLANET), 0x003F would still be emitted for
    ///     non-planet objects but the first-login coverage relies on
    ///     OT_PLANET being the always-visible branch (a fresh player
    ///     has empty ExposedNavList) — assertion would fail.
    ///   </item>
    ///   <item>
    ///     <b>m_StaticSectorList load regression.</b> If sector 1015's
    ///     static-object roster fails to load at sector-thread spawn
    ///     time (DB query, sector-data file parse, etc.), the loop
    ///     iterates an empty list and emits nothing. Both opcodes
    ///     missing is the signal.
    ///   </item>
    ///   <item>
    ///     <b>Object::SendObject vtable regression.</b> The base
    ///     <c>virtual void Object::SendObject(Player*)</c> at
    ///     <c>ObjectClass.h:365</c> dispatches per-type to derived
    ///     classes; a vtable corruption or override-removal that hits
    ///     the OT_PLANET path would silently produce no
    ///     PLANET_POSITIONAL_UPDATE or NAVIGATION.
    ///   </item>
    ///   <item>
    ///     <b>SendNavigation opcode-id regression at
    ///     <c>PlayerConnection.cpp:1147</c>.</b> A typo flip from
    ///     <c>ENB_OPCODE_0099_NAVIGATION</c> to a neighbouring define
    ///     would still emit a frame but under a different opcode —
    ///     assertion catches it.
    ///   </item>
    ///   <item>
    ///     <b>SendOpcode header-width revert at
    ///     <c>PlayerConnection.cpp:127</c>.</b> Would corrupt every
    ///     inner opcode in the 0x2016 PACKET_SEQUENCE parser; both
    ///     0x003F and 0x0099 would not appear under their correct
    ///     labels.
    ///   </item>
    ///   <item>
    ///     <b>Proxy SendClientPacketSequence inner-opcode guard
    ///     tightening at <c>proxy/UDPProxyToClient_linux.cpp:568</c>.</b>
    ///     Currently passes 0x003F and 0x0099 (both &lt; 0x0FFF). A
    ///     regression to a tighter upper bound would silently drop
    ///     them from the wire.
    ///   </item>
    ///   <item>
    ///     <b>DoSectorLoginUntilStartAsync drain-loop regression
    ///     (<c>Opcodes/SectorHandshake.cs:330-346</c>).</b> Same as
    ///     Wave 34/35/52/53 — exercises the capture path itself.
    ///   </item>
    /// </list>
    ///
    /// <para>
    /// Budget: 120s (same as Wave 52/53 — the 2-stage login pattern
    /// needs time for the explicit logoff round-trip between stages,
    /// plus the SendAllNavs iteration adds a handful of milliseconds
    /// to the stage-2 drain).
    /// </para>
    /// </summary>
    [Fact]
    public async Task HandshakeEmitsPlanetPositionalUpdateAndNavigationOnSpaceSectorLogin()
    {
        var account = TestAccounts.For();
        const int slot = 0;
        const int stationSectorId = 10151;
        const int spaceSectorId = 1015;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120));

        var login = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(account.Username, account.Password), cts.Token);
        Assert.True(login.Valid, $"login: {login.RawBody.TrimEnd()}");
        Assert.False(string.IsNullOrEmpty(login.Ticket));

        var stationSession = await SectorHandshake.EstablishAsync(
            _server, login.Ticket!, account.Username, slot, stationSectorId,
            firstName: "NavFan", shipName: "NavFanShip", cts.Token);

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

            Assert.Contains(OpcodeId.Known.PlanetPositionalUpdate.Value, spaceSession.HandshakeOpcodes);
            Assert.Contains(OpcodeId.Known.Navigation.Value, spaceSession.HandshakeOpcodes);
        }
        finally
        {
            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try
            {
                await using var cleanupGlobal = await N7.CliClient.Net.EncryptedTcpConnection.ConnectAsync(
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
    /// Wave 73 negative-assertion: 0x004F STARBASE_SET MUST NOT appear
    /// in the handshake fan-out of a SPACE-sector login. The opcode is
    /// emitted unconditionally during StationLogin2 (<c>SectorManager.cpp:514</c>
    /// <c>player-&gt;SendStarbaseSet(0, 0)</c>) for docked characters,
    /// and again unconditionally during LaunchIntoSpace
    /// (<c>SectorManager.cpp:537</c> <c>player-&gt;SendStarbaseSet(1, 0)</c>)
    /// at the explicit station→space transition; it has no SectorLogin2
    /// (space-arm) emit site. A test that re-establishes directly into
    /// Luna space sector 1015 (taking the ReloadSavedData path which
    /// routes through SectorLogin2 rather than StationLogin2) must
    /// observe zero 0x004F frames in HandshakeOpcodes.
    ///
    /// <para>
    /// Why this is a negative-assertion wave (and the first one in
    /// Phase K). Positive presence assertions catch
    /// "opcode-stopped-emitting" regressions; negative absence
    /// assertions catch "opcode-emits-when-it-shouldn't" regressions.
    /// Both shapes matter for preservation fidelity — the retail
    /// Win32 client interprets a 0x004F STARBASE_SET frame as
    /// "you-are-now-docked-at-starbase" (with body field action=0) or
    /// "you-have-just-launched" (with body field action=1); a spurious
    /// 0x004F mid-space-handshake would either confusingly re-dock a
    /// player into a starbase UI overlay (action=0) or re-trigger the
    /// launch-cutscene (action=1). The retail server never emits 0x004F
    /// during a plain space-sector login, so neither should ours. Wave
    /// 73 pins that invariant.
    /// </para>
    ///
    /// <para>
    /// Why +0 ratchet. 0x004F is already counted in TestedOpcodes by
    /// HandshakeEmitsFullSendLoginShipDataFanout (Wave 51 — the
    /// positive presence assertion in the STATION-sector handshake).
    /// Wave 73 strengthens the existing coverage with the absence
    /// invariant on the SPACE-sector branch.
    /// </para>
    ///
    /// <para>
    /// Test structure. 2-stage station→space handshake pattern reused
    /// from Wave 52/54: stage 1 EstablishAsync at Luna Station 10151
    /// creates the avatar via ReInitializeSavedData; explicit
    /// 0x00B9 LOGOFF_REQUEST → 0x00BA LOGOFF_CONFIRMATION round-trip
    /// forces synchronous DropPlayerFromGalaxy (avoids
    /// G_ERROR_ACCOUNT_IN_USE); DisposeAsync tears down the stage-1
    /// sockets; stage 2 ReestablishAsync at sectorId 1015 (Luna space)
    /// takes the ReloadSavedData path which preserves the space
    /// sector_num and routes HandleSectorLogin into the SectorLogin
    /// branch (sector_id ≤ 9999), NOT the StationLogin branch
    /// (sector_id &gt; 9999). After the space handshake completes, we
    /// assert <c>Assert.DoesNotContain(0x004F, spaceSession.HandshakeOpcodes)</c>.
    /// </para>
    ///
    /// <para>
    /// Regression classes this catches.
    /// </para>
    /// <list type="bullet">
    ///   <item>
    ///     <b>SendStarbaseSet leak into SectorLogin2 at
    ///     <c>SectorManager.cpp:354-384</c>.</b> If a refactor
    ///     accidentally copies the
    ///     <c>player-&gt;SendStarbaseSet(0, 0)</c> line from
    ///     StationLogin2:514 into SectorLogin2 (or moves
    ///     SendStarbaseSet earlier in the common HandleSectorLogin
    ///     prologue at SectorManager.cpp:325-352), the space handshake
    ///     would gain a spurious 0x004F — assertion catches.
    ///   </item>
    ///   <item>
    ///     <b>HandleSectorLogin dispatch-branch collapse at
    ///     <c>SectorManager.cpp:324-336</c>.</b> If the
    ///     <c>m_SectorID &gt; 9999</c> guard inverts or collapses
    ///     (e.g. <c>&gt;= 9999</c> instead of <c>&gt; 9999</c>, or both
    ///     branches falling through to StationLogin), sector 1015
    ///     would re-route into StationLogin2 and emit 0x004F. Assertion
    ///     catches.
    ///   </item>
    ///   <item>
    ///     <b>LaunchIntoSpace mis-call during space-sector login.</b>
    ///     LaunchIntoSpace at SectorManager.cpp:534 calls
    ///     SendStarbaseSet(1, 0) and is meant to fire only during the
    ///     explicit station→space transition (when a docked player
    ///     clicks "Launch"). A regression that calls LaunchIntoSpace
    ///     from the SectorLogin path (or from a sector-thread spawn
    ///     hook on entering a space sector) would emit a spurious
    ///     action=1 0x004F. Assertion catches.
    ///   </item>
    ///   <item>
    ///     <b>SendOpcode header-width revert at
    ///     <c>PlayerConnection.cpp:127</c>.</b> Same load-bearing
    ///     invariant as the Wave 49/52/54 positive assertions. A revert
    ///     to <c>sizeof(long) == 8</c> on Linux would shift the 0x2016
    ///     PACKET_SEQUENCE inner-tuple parser by 4 bytes, mis-labelling
    ///     every inner opcode — including potentially producing a fake
    ///     0x004F where there shouldn't be one (mis-decoded from a
    ///     neighbouring opcode's byte slot). Negative assertion would
    ///     catch the false-positive case the positive assertions don't.
    ///   </item>
    ///   <item>
    ///     <b>DoSectorLoginUntilStartAsync drain-loop capture-path
    ///     regression (<c>Opcodes/SectorHandshake.cs:330-346</c>).</b>
    ///     If the capture loop accidentally appends frames AFTER the
    ///     0x0005 START terminator (e.g. continuing past the START
    ///     boundary into the post-handshake stream), it could capture
    ///     a delayed 0x004F that the post-handshake game-state code
    ///     happens to emit — the negative assertion would catch the
    ///     capture-path corruption.
    ///   </item>
    ///   <item>
    ///     <b>Stage 1 → Stage 2 logoff-and-reconnect path regression at
    ///     <c>SectorHandshake.cs:436-446</c>.</b> Same load-bearing
    ///     path as Wave 52/54/71. A bare TCP disconnect between stages
    ///     leaves the in-memory Player around so stage-2 GlobalConnect
    ///     hits G_ERROR_ACCOUNT_IN_USE at UDP_Global.cpp:166-170 —
    ///     test times out before the assertion runs.
    ///   </item>
    /// </list>
    ///
    /// <para>
    /// Server-integrity note (per CLAUDE.md). 0x004F STARBASE_SET is
    /// a server-originated emit; Wave 73 adds no new client input
    /// shapes and no new server response. The retail Win32 client +
    /// retail server pair never emits 0x004F during a plain
    /// space-sector login (it would confuse the space HUD overlay);
    /// our negative assertion pins that retail behaviour. Pure
    /// passive-observation tightening, exactly the kind of fidelity
    /// guard the rule welcomes.
    /// </para>
    ///
    /// <para>
    /// Budget: 120s (same as Wave 52/54 — 2-stage login pattern).
    /// </para>
    /// </summary>
    [Fact]
    public async Task HandshakeDoesNotEmitStarbaseSetOnSpaceSectorLogin()
    {
        var account = TestAccounts.For();
        const int slot = 0;
        const int stationSectorId = 10151;
        const int spaceSectorId = 1015;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120));

        var login = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(account.Username, account.Password), cts.Token);
        Assert.True(login.Valid, $"login: {login.RawBody.TrimEnd()}");
        Assert.False(string.IsNullOrEmpty(login.Ticket));

        var stationSession = await SectorHandshake.EstablishAsync(
            _server, login.Ticket!, account.Username, slot, stationSectorId,
            firstName: "NoSbsLeak", shipName: "NoSbsLeakShip", cts.Token);

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

            // Negative assertion: 0x004F STARBASE_SET must NOT appear in
            // the space-sector handshake. It's a STATION-only emit
            // (SectorManager.cpp:514 in StationLogin2) plus an explicit
            // LaunchIntoSpace emit (SectorManager.cpp:537) which only
            // fires on the docked→space transition, never on a direct
            // space-sector login.
            Assert.DoesNotContain(OpcodeId.Known.StarbaseSet.Value, spaceSession.HandshakeOpcodes);
        }
        finally
        {
            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try
            {
                await using var cleanupGlobal = await N7.CliClient.Net.EncryptedTcpConnection.ConnectAsync(
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
