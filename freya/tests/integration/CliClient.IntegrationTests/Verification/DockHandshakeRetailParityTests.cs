// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya

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
/// post-dock window. Our tests below reach sector 10151 (Luna Station)
/// because that is the station docked at the test character's home
/// space sector (Terran Warrior; StartSector[race*3+profession=0] =
/// 1015, the Luna space sector). Fresh characters spawn in SPACE
/// (StartSector ids are all &lt; 10000, see server/src/StaticData.h), so
/// the first login lands in <c>SectorManager::SectorLogin</c>; each test
/// below performs a SECOND login to drive <c>StationLogin</c> -- create +
/// first-login at home space 1015, cleanly LOGOFF, then reconnect and
/// LOGIN to station 10151. Both invariants asserted here -- no 0x00A5
/// emit to self, and the 0x007F payload BIG-endian-decoding to a
/// MANU_TAG|PLAYER_TAG-bearing host ManuID -- are properties of the
/// <c>SectorManager::StationLogin</c> code path itself, not of any
/// specific station; they hold for both 10151 and 45151. The retail
/// capture's bytes are the primary-source proof that the real Win32
/// client expects this shape from the StationLogin path.
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
    /// Reach <c>SectorManager::StationLogin</c> for a fresh character. Fresh
    /// characters spawn in SPACE (StartSector[race*3+profession] &lt; 10000 --
    /// server/src/StaticData.h), so the first sector login always lands in
    /// <c>SectorLogin</c>; a station handshake requires a SECOND login.
    /// Stage 1: create + first-login at the home space sector. Then cleanly
    /// 0x00B9 LOGOFF that session so the server runs
    /// <c>DropPlayerFromGalaxy</c> synchronously (else G_ERROR_ACCOUNT_IN_USE
    /// on the stage-2 login). Stage 2: reconnect (no char create) and LOGIN to
    /// the station sector; the <c>ReloadSavedData</c> path preserves sector_num
    /// from the LOGIN ToSectorID (server/src/PlayerSaves.cpp:289) so
    /// <c>StationLogin</c> runs. Caller owns the returned session and must
    /// clean up the created character on <paramref name="slot"/>.
    /// </summary>
    // Delegates to the shared two-stage helper, which logs off the stage-1
    // home session cleanly via Session.DisposeAsync (0x00B9 ->
    // DropPlayerFromGalaxy) before relogging in at the station. Note the
    // shared helper takes (stationSectorId, homeSpaceSectorId) in that order.
    private Task<SectorHandshake.Session> EstablishAtStationAsync(
        string ticket, string username, int slot,
        int homeSpaceSectorId, int stationSectorId,
        string firstName, string shipName, CancellationToken ct)
        => SectorHandshake.EstablishAtStationAsync(
            _server, ticket, username, slot, stationSectorId, homeSpaceSectorId,
            firstName, shipName, ct);

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
        const int homeSpaceSectorId = 1015;   // Terran Warrior home, StartSector[0*3+0] (Luna space)
        const int stationSectorId = 10151;    // Luna Station (sector > 9999 -> StationLogin)

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120));

        var login = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(account.Username, account.Password), cts.Token);
        Assert.True(login.Valid, $"login: {login.RawBody.TrimEnd()}");
        Assert.False(string.IsNullOrEmpty(login.Ticket));

        await using var session = await EstablishAtStationAsync(
            login.Ticket!, account.Username, slot, homeSpaceSectorId, stationSectorId,
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
    /// Wave 112 regression pin (corrected: the 0x007F field is
    /// byte-reversed on the wire, read BIG-endian). The
    /// 0x007F MANUFACTURE_SET_MANUFACTURE_ID frame emitted by
    /// <c>SectorManager::StationLogin</c>
    /// (<c>server/src/SectorManager.cpp:563</c>) carries the player's
    /// ManuID on the wire as the NETWORK-order (byte-reversed) encoding
    /// of the host ManuID -- unlike every other GameID, which is host
    /// little-endian. A BIG-endian int32 decode of the payload therefore
    /// recovers the host ManuID, which by construction
    /// (<c>ManuID() = GameID() | MANU_TAG</c>, GameID carries PLAYER_TAG)
    /// has both <c>MANU_TAG</c> (bit 31) and <c>PLAYER_TAG</c> (bit 30)
    /// set. This is the robust invariant; a little-endian decode would
    /// instead yield <c>byteswap(ManuID)</c>, whose tag bits land on the
    /// avatar-index LOW byte and are only coincidentally set.
    ///
    /// <para>
    /// Backstory (the regression this pins, and the misconception that
    /// caused it). The emitter must byte-swap before send
    /// (<c>Player::SetManufactureID</c> does <c>htonl(mfg_id)</c>).
    /// Commit 730ba649 REMOVED that swap on the belief that it was a
    /// CLAUDE.md Trap-1 byteswap-of-host-data bug, sending the manu-id
    /// host little-endian like other GameIDs. That broke the analyze
    /// terminal: the client BE-reads 0x007F to key its manufacture
    /// session against the manu-lab object (created under its host GameID,
    /// read LE), so a host-LE 0x007F resolved to byteswap(ManuID), the
    /// lookup missed, the session pointer stayed NULL, and the analyze
    /// window faulted dereferencing it on open. Restored in the emitter
    /// (commit 5c49d467); see plans/29 CV-29.
    /// </para>
    ///
    /// <para>
    /// Primary source citation (CLAUDE.md server-integrity rule). The
    /// retail capture carries the SAME manufacturing-lab GameID two ways
    /// in one stream: the lab's 0x04 CREATE, 0x1B AuxData ("Manufacturing
    /// Lab"), and 0x40 positional-update carry it host-LE as bytes
    /// <c>F7 13 EE 06</c>, while the 0x89 Relationship and the 0x7F
    /// SetManufactureID carry it REVERSED as <c>06 EE 13 F7</c>. The
    /// CREATE is what the client keys the object by; for the 0x7F lookup
    /// to hit that object the client must BE-read it. BE-decode of
    /// <c>06 EE 13 F7</c> = 0x06EE13F7 = the host int the CREATE
    /// established -- the match the analyze window needs. (The retail
    /// capture's specific manu-lab id happens not to carry the high tag
    /// bits; our server's ManuID does, by the GameID|MANU_TAG
    /// construction above -- the load-bearing fact is the CREATE-vs-0x7F
    /// byte reversal, not the absolute tag bits.)
    /// </para>
    ///
    /// <para>
    /// Two-stage station handshake. Since fresh characters spawn in SPACE
    /// in their home sector (StartSector[race*3+profession] &lt; 10000 --
    /// see server/src/StaticData.h), the FIRST sector login always lands in
    /// <c>SectorManager::SectorLogin</c>, whose space-arm anchor emits
    /// <c>SetManufactureID(0)</c> -- a 4-byte ZERO payload with no tag bits.
    /// Reaching <c>SectorManager::StationLogin</c> (sector &gt; 9999, the
    /// nonzero manu-lab anchor) therefore requires a SECOND login: create +
    /// first-login at the home space sector (1015), cleanly LOGOFF that
    /// session so the server runs <c>DropPlayerFromGalaxy</c> (else
    /// G_ERROR_ACCOUNT_IN_USE on the second login), then reconnect and LOGIN
    /// to the station sector (10151). The <c>ReloadSavedData</c> path on the
    /// second login preserves sector_num from the LOGIN ToSectorID
    /// (server/src/PlayerSaves.cpp:289), so the station-arm anchor fires.
    /// Mirrors the two-stage pattern in
    /// <see cref="N7.CliClient.IntegrationTests.Opcodes.SectorManufactureSetManufactureIdHardeningTests"/>
    /// Wave 106 (which goes the other direction, station then space).
    /// </para>
    /// </summary>
    [Fact]
    public async Task StationHandshake_ManufactureSetManufactureIdPayload_DecodesBigEndianToTaggedManuId()
    {
        var account = TestAccounts.New(_server);
        const int slot = 0;
        const int homeSpaceSectorId = 1015;   // Terran Warrior home, StartSector[0*3+0] (Luna space)
        const int stationSectorId = 10151;    // Luna Station (sector > 9999 -> StationLogin)
        const uint ManuTag = 1u << 31;
        const uint PlayerTag = 1u << 30;
        const uint TagMask = ManuTag | PlayerTag;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120));

        var login = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(account.Username, account.Password), cts.Token);
        Assert.True(login.Valid, $"login: {login.RawBody.TrimEnd()}");
        Assert.False(string.IsNullOrEmpty(login.Ticket));

        // Two-stage: create + first-login at home space 1015, LOGOFF, then
        // reconnect and LOGIN to station 10151 so StationLogin runs. The
        // station-arm manu-lab anchor at SectorManager.cpp:563 emits the
        // nonzero SetManufactureID(ManuID) (the byte-swap is in the emitter);
        // the space-arm zero anchor (SectorLogin) does NOT fire on the station
        // path, so the nonzero filter targets the manu-lab emit specifically.
        await using var session = await EstablishAtStationAsync(
            login.Ticket!, account.Username, slot, homeSpaceSectorId, stationSectorId,
            firstName: "MfgEndian", shipName: "MfgEndianShip", cts.Token);

        try
        {
            var mfgPayloads = session.HandshakePayloads
                .Where(f => f.Opcode == OpcodeId.Known.ManufactureSetManufactureId.Value)
                .Select(f => f.Payload)
                .ToList();

            Assert.NotEmpty(mfgPayloads);
            // The field is byte-reversed on the wire, so a nonzero manu-lab
            // anchor is nonzero under any decode; filter on length + nonzero.
            var manuLabPayload = mfgPayloads.FirstOrDefault(
                p => p.Length == 4 && BinaryPrimitives.ReadUInt32BigEndian(p) != 0);
            Assert.NotNull(manuLabPayload);

            // BE-decode recovers the host ManuID (= GameID | MANU_TAG), which
            // carries both tag bits by construction -- the robust invariant
            // proving the emitter byte-swapped (a host-LE 0x007F would BE-decode
            // to byteswap(ManuID) and fail this).
            uint manuId = BinaryPrimitives.ReadUInt32BigEndian(manuLabPayload!);
            Assert.True((manuId & TagMask) == TagMask,
                $"0x007F manu-lab payload BE-int32 = 0x{manuId:X8}; expected MANU_TAG|PLAYER_TAG (top 2 bits) set. " +
                $"Wire bytes: {BitConverter.ToString(manuLabPayload!)}. " +
                $"Retail reference: capture_1.txt line 3769 payload 06 EE 13 F7 -> BE 0x06EE13F7 (matches the manu-lab 0x04 CREATE GameID, host-LE F7 13 EE 06).");
        }
        finally
        {
            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await SectorHandshake.DeleteCreatedCharacterAsync(session.Global, slot, cleanupCts.Token); }
            catch { /* best-effort cleanup */ }
        }
    }
}
