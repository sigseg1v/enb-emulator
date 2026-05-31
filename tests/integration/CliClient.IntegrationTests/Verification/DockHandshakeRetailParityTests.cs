// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Buffers.Binary;
using N7.CliClient.Auth;
using N7.CliClient.IntegrationTests.Opcodes;
using N7.CliClient.Net;
using N7.CliClient.Opcodes;
using Xunit;

namespace N7.CliClient.IntegrationTests.Verification;

/// <summary>
/// Wave 115 retail-parity regression suite for the station-sector
/// handshake stream. Each test in this class asserts a shape invariant
/// we observed in the retail single-player dock capture
/// (<c>archive/kyp-snapshot/capturedPackets/capture_1.rar</c>) that our
/// server was silently violating prior to Waves 112/113. Distinct from the
/// per-opcode "hardening" suites under <c>Opcodes/</c> -- those pin
/// individual opcode shapes; this one pins cross-opcode invariants
/// (ordering, presence, byte-level field semantics) the per-opcode
/// pins cannot express.
///
/// <para>
/// Sector caveat. The retail capture's destination station is sector
/// 45151 (Friendship 7 Recreation Port, Glenn Commission, Sirius
/// system) -- see <c>CaptureReplayTests.MasterJoin_...</c> which pins
/// <c>ToSectorId == 0x0000B05F == 45151</c> from MasterJoin frame
/// 220, and the chat-channel "Sector 45151" strings throughout the
/// post-dock window. Our tests below use sector 10151 (Luna Station)
/// because that is the StartSector for the test character's
/// race/profession (Terran Warrior; race*3+profession = 0). Both
/// invariants asserted here -- no 0x00A5 emit to self, and 0x007F
/// payload LE-decode with MANU_TAG|PLAYER_TAG bits set -- are
/// properties of the <c>SectorManager::StationLogin</c> code path
/// itself, not of any specific station; they hold for both 10151 and
/// 45151. The retail capture's bytes are the primary-source proof
/// that the real Win32 client expects this shape from the StationLogin
/// path.
/// </para>
///
/// <para>
/// Failure of any test here means our station-login emit-stream has
/// drifted from the retail wire behaviour the real client was
/// compiled against -- investigate before relaxing.
/// </para>
/// </summary>
[Collection(ServerCollection.Name)]
public sealed class DockHandshakeRetailParityTests
{
    private readonly ServerFixture _server;
    private readonly ClientFixture _client;

    public DockHandshakeRetailParityTests(ServerFixture server)
    {
        _server = server;
        _client = new ClientFixture(server);
    }

    /// <summary>
    /// Wave 113 regression pin. The station-sector handshake stream
    /// must NOT contain any 0x00A5 ClientChatEvent frames. The single
    /// 0x00A5 our server was emitting was a CHEV_LOGGED_IN (type=1)
    /// inadvertently sent to the source player due to an
    /// operator-precedence bug in
    /// <c>PlayerManager::SendGlobalChatEvent</c>
    /// (<c>server/src/PlayerManager.cpp:818</c>) -- the
    /// <c>p != source</c> self-skip was bypassed when source was
    /// GM-tier because <c>&amp;&amp;</c> binds tighter than
    /// <c>||</c>.
    ///
    /// <para>
    /// Retail capture_1.txt frames 4977..14745 (the entire dock-and-
    /// post-dock window) contain ZERO 0x00A5 type=1 frames -- single-
    /// player Ace receives no CHEV_LOGGED_IN at any point in his
    /// session. Every 0x00A5 in the capture (line 4996, 7178, 9355,
    /// ...) is type=7 (sector-channel join), and all of them appear
    /// AFTER 0x0005 START (line 4977). The first 0x00A5 in our
    /// handshake stream was a CHEV_LOGGED_IN appearing as the first
    /// sector-TCP frame, i.e. before SendStart.
    /// </para>
    ///
    /// <para>
    /// The narrower invariant -- "no 0x00A5 at all during handshake"
    /// -- is the stronger of two valid retail-parity assertions.
    /// Retail's capture is single-player, so the broadcast to other
    /// online GMs/friends is not exercised; the handshake itself,
    /// however, definitively does not push 0x00A5 to the joining
    /// player. The fix preserves the cross-player broadcast intent
    /// while correctly self-skipping the source.
    /// </para>
    /// </summary>
    [Fact]
    public async Task StationHandshake_EmitsNoClientChatEventFramesToSelf_MatchesRetailCapture()
    {
        var account = TestAccounts.New(_server);
        const int slot = 0;
        const int stationSectorId = 10151;  // Luna Station, StartSector[0*3+0] (Terran/Warrior)

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        var login = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(account.Username, account.Password), cts.Token);
        Assert.True(login.Valid, $"login: {login.RawBody.TrimEnd()}");
        Assert.False(string.IsNullOrEmpty(login.Ticket));

        await using var session = await SectorHandshake.EstablishAsync(
            _server, login.Ticket!, account.Username, slot, stationSectorId,
            firstName: "ChevPin", shipName: "ChevPinShip", cts.Token);

        try
        {
            var chevFrames = session.HandshakeOpcodes
                .Where(op => op == OpcodeId.Known.ClientChatEvent.Value)
                .ToList();

            Assert.Empty(chevFrames);
        }
        finally
        {
            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await SectorHandshake.DeleteCreatedCharacterAsync(session.Global, slot, cleanupCts.Token); }
            catch { /* best-effort cleanup */ }
        }
    }

    /// <summary>
    /// Wave 112 regression pin. The 0x007F MANUFACTURE_SET_MANUFACTURE_ID
    /// frame emitted by <c>SectorManager::StationLogin</c>
    /// (<c>server/src/SectorManager.cpp:483</c>) must put the player's
    /// ManuID on the wire as little-endian host-order bytes -- the
    /// payload's LE-int32 decode must have both <c>MANU_TAG</c>
    /// (bit 31) and <c>PLAYER_TAG</c> (bit 30) set.
    ///
    /// <para>
    /// Backstory. The line was previously
    /// <c>player->SetManufactureID(ntohl(ManuID))</c>. On x86 ntohl is
    /// a byteswap; SetManufactureID then memcpys the swapped int's raw
    /// bytes onto the wire. The net effect was that the wire bytes
    /// were the BIG-endian encoding of ManuID -- so when the client
    /// LE-decoded them, the top byte (which should carry the
    /// MANU_TAG|PLAYER_TAG bits 11000000) ended up as the LOW byte of
    /// ManuID (avatar-id LSB), invalidating the manu-id and stripping
    /// the tag bits the client uses to recognise it as a manu-lab
    /// anchor.
    /// </para>
    ///
    /// <para>
    /// Primary source citation (CLAUDE.md server-integrity rule).
    /// Retail capture_1.txt 0x7F frame at line 3769, payload bytes
    /// <c>06 EE 13 F7</c>. LE-int32 decode = 0xF713EE06; the top byte
    /// 0xF7 = 11110111 has both bit 31 (MANU_TAG) and bit 30
    /// (PLAYER_TAG) set, plus avatar-id high bits. Conversely a
    /// BE-int32 decode = 0x06EE13F7 has neither tag bit set, which is
    /// not a valid manu-id. The capture proves the wire is LE
    /// host-order; the ntohl was a classic CLAUDE.md Trap 1.
    /// </para>
    /// </summary>
    [Fact]
    public async Task StationHandshake_ManufactureSetManufactureIdPayload_DecodesLittleEndianWithTagBits()
    {
        var account = TestAccounts.New(_server);
        const int slot = 0;
        const int stationSectorId = 10151;  // Luna Station, StartSector[0*3+0] (Terran/Warrior)
        const uint ManuTag = 1u << 31;
        const uint PlayerTag = 1u << 30;
        const uint TagMask = ManuTag | PlayerTag;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        var login = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(account.Username, account.Password), cts.Token);
        Assert.True(login.Valid, $"login: {login.RawBody.TrimEnd()}");
        Assert.False(string.IsNullOrEmpty(login.Ticket));

        await using var session = await SectorHandshake.EstablishAsync(
            _server, login.Ticket!, account.Username, slot, stationSectorId,
            firstName: "MfgEndian", shipName: "MfgEndianShip", cts.Token);

        try
        {
            // StationLogin emits two 0x007F frames during the handshake:
            // the zero-anchor early in HandleSectorLogin (SectorManager.cpp:353
            // via SectorLogin, only on space-arm) does NOT fire here -- on
            // the station path only the ManuID anchor at SectorManager.cpp:483
            // emits. Filter to nonzero payloads so the assertion targets the
            // manu-lab anchor specifically. If the zero anchor ever migrates
            // into the station path, this test simply asserts on the
            // manu-lab emit and ignores the zero one (still correct).
            var mfgPayloads = session.HandshakePayloads
                .Where(f => f.Opcode == OpcodeId.Known.ManufactureSetManufactureId.Value)
                .Select(f => f.Payload)
                .ToList();

            Assert.NotEmpty(mfgPayloads);
            var manuLabPayload = mfgPayloads.FirstOrDefault(
                p => p.Length == 4 && BinaryPrimitives.ReadUInt32LittleEndian(p) != 0);
            Assert.NotNull(manuLabPayload);

            uint manuId = BinaryPrimitives.ReadUInt32LittleEndian(manuLabPayload!);
            Assert.True((manuId & TagMask) == TagMask,
                $"0x007F manu-lab payload LE-int32 = 0x{manuId:X8}; expected MANU_TAG|PLAYER_TAG (top 2 bits) set. " +
                $"Wire bytes: {BitConverter.ToString(manuLabPayload!)}. " +
                $"Retail reference: capture_1.txt line 3769 payload 06 EE 13 F7 -> 0xF713EE06.");
        }
        finally
        {
            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await SectorHandshake.DeleteCreatedCharacterAsync(session.Global, slot, cleanupCts.Token); }
            catch { /* best-effort cleanup */ }
        }
    }
}
