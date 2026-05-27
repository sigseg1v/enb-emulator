// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using N7.CliClient.Auth;
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
        // cli_test31 — Pool[29]. Layout: Pool[0..8]=cli_test01..09,
        // Pool[9..22]=cli_test11..24, Pool[23..28]=cli_test25..30,
        // Pool[29]=cli_test31 (this test). Pool skips cli_test10
        // which is the out-of-pool STRESS_TEST_CLOSED fixture.
        var account = TestAccounts.Pool[29];
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
        // cli_test32 — Pool[30]. Dedicated account so this test's
        // create/delete cycle doesn't race against the sibling
        // HandshakeEmitsClientShipAndAvatarDescription test (which
        // also creates+deletes a character on Pool[29]/slot 0).
        var account = TestAccounts.Pool[30];
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
}
