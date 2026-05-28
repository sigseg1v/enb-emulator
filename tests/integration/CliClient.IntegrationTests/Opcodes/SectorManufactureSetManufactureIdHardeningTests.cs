// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond preservation project.
// License: LICENSES/enb-emulator

using N7.CliClient.Auth;
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
}
