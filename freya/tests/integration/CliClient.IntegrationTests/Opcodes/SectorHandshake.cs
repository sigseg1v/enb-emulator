// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Buffers.Binary;
using System.Text;
using N7.CliClient.Net;
using N7.CliClient.Opcodes;
using N7.CliClient.Opcodes.Inbound;
using N7.CliClient.Opcodes.Outbound;
using Xunit;

namespace N7.CliClient.IntegrationTests.Opcodes;

/// <summary>
/// Shared driver for the full Phase K sector-login handshake:
/// auth ticket → GlobalConnect → CreateCharacter → GlobalTicketRequest
/// → MasterJoin → sector TCP LOGIN → drain to 0x0005 START.
///
/// <para>
/// Tests that exercise post-login behaviour (chat, movement, inventory,
/// etc.) call <see cref="EstablishAsync"/> to land in an authoritative
/// in-sector state, then drive the returned <see cref="Session"/> for
/// their specific opcode under test. Pulled out of
/// <see cref="SectorLoginTests"/> so the login plumbing has one home
/// rather than being copy-pasted per opcode test.
/// </para>
/// </summary>
public static class SectorHandshake
{
    /// <summary>
    /// State handed back from <see cref="EstablishAsync"/>. Owns the
    /// still-open global and sector TCP connections; callers drive
    /// additional opcodes through <see cref="Sector"/> and reuse
    /// <see cref="Global"/> for the post-test character cleanup.
    /// </summary>
    public sealed class Session : IAsyncDisposable
    {
        public required EncryptedTcpConnection Global { get; init; }
        public required EncryptedTcpConnection Sector { get; init; }

        /// <summary>
        /// PLAYER_TAG-bit-set avatar id allocated by
        /// <c>UDP_Global::HandleGlobalTicketRequest</c>. The server
        /// uses this to key the in-memory Player; every subsequent
        /// in-sector opcode is routed through it.
        /// </summary>
        public required int GameId { get; init; }

        /// <summary>
        /// Start id returned in the 0x0005 START frame
        /// (PlayerManager::SendStart). Captured so the caller can
        /// echo it in a 0x0006 START_ACK.
        /// </summary>
        public required int StartId { get; init; }

        /// <summary>Character slot used for the avatar this session belongs to.</summary>
        public required int Slot { get; init; }

        /// <summary>
        /// Every opcode received on <see cref="Sector"/> between the
        /// LOGIN frame and the 0x0005 START frame that terminates the
        /// handshake drain. Captured so passive-observation tests can
        /// assert on opcodes the server emits as part of
        /// SectorManager::SectorLogin2 (SendLoginShipData →
        /// SendShipInfo → SendServerParameters → SendAllNavs →
        /// SendVaMessage → SendStart) without re-running the
        /// handshake. List order matches receive order; duplicates
        /// preserved. Wave 34 lit this up.
        /// </summary>
        public required IReadOnlyList<ushort> HandshakeOpcodes { get; init; }

        /// <summary>
        /// Same set of frames as <see cref="HandshakeOpcodes"/> but with
        /// each entry's wire-payload length attached. Wave 68 lit this up
        /// so byte-exact hardening tests can pin a specific emit's
        /// payload size from the captured handshake stream without
        /// re-driving the login flow. Order, duplication, and 0x0005
        /// terminator semantics all match <see cref="HandshakeOpcodes"/>.
        /// </summary>
        public required IReadOnlyList<(ushort Opcode, int PayloadLength)> HandshakeFrames { get; init; }

        /// <summary>
        /// Same set of frames as <see cref="HandshakeFrames"/> but with
        /// the full wire-payload bytes attached instead of just the
        /// length. Wave 114 lit this up so byte-exact hardening tests
        /// can diff a specific emit's payload bytes against a retail
        /// capture fixture without re-driving the login flow. Order,
        /// duplication, and 0x0005 terminator semantics all match
        /// <see cref="HandshakeFrames"/>. The arrays are defensive
        /// copies of the receive buffer.
        /// </summary>
        public required IReadOnlyList<(ushort Opcode, byte[] Payload)> HandshakePayloads { get; init; }

        public async ValueTask DisposeAsync()
        {
            // Clean logoff before closing the sockets. A bare socket close
            // strands the server's Player as Active() forever: the 30s
            // account-in-use reap (UDP_Global.cpp ProcessTicketInfo ->
            // PlayerManager::CheckAccountInUse) only frees players already
            // flagged inactive, and a dirty disconnect never flips that
            // flag. Across a serial run these stranded Active players pile
            // up until the server's player pool is exhausted and the global
            // plane stops issuing 0x0070 GLOBAL_AVATAR_LIST -- the
            // session-accumulation "wedge" that only a server restart
            // cleared (a proxy recycle cannot, the leak is server-side). An
            // explicit 0x00B9 LOGOFF_REQUEST runs
            // Player::HandleLogoffRequest -> DropPlayerFromGalaxy ->
            // SetActive(false) synchronously, freeing the slot exactly as a
            // real client quitting does. Best-effort: a half-dead or wedged
            // sector link must not hang teardown.
            try
            {
                using var logoffCts = new CancellationTokenSource(
                    TimeSpan.FromSeconds(5));
                byte[] logoffPayload = new byte[8];
                await Sector.SendAsync(
                    Packet.ForOpcode(
                        OpcodeId.Known.LogoffRequest.Value, logoffPayload),
                    logoffCts.Token);
                await DrainUntilOpcode(
                    Sector, OpcodeId.Known.LogoffConfirmation.Value,
                    logoffCts.Token);
            }
            catch
            {
                // best-effort; fall through to the socket close regardless
            }

            await Sector.DisposeAsync();
            await Global.DisposeAsync();
        }
    }

    /// <summary>
    /// Run the full handshake against the live docker stack and return
    /// the open connections + identifiers. Caller MUST eventually call
    /// <see cref="DeleteCreatedCharacterAsync"/> (typically in a finally
    /// block) so a re-run lands in the empty-slot baseline.
    /// </summary>
    public static Task<Session> EstablishAsync(
        ServerFixture server,
        string authTicket,
        string accountUsername,
        int slot,
        int sectorId,
        string firstName,
        string shipName,
        CancellationToken ct,
        int race = 0,
        int profession = 0,
        int gender = 0)
        => WithProxyRecycleOnWedgeAsync(
            server, ct,
            () => EstablishOnceAsync(
                server, authTicket, accountUsername, slot, sectorId,
                firstName, shipName, ct, race, profession, gender),
            // On retry the redo recreates the avatar on the same slot with
            // the same fixed name; clear any avatar the wedged attempt left
            // behind (its in-attempt delete ran on a connection the proxy
            // recycle then dropped) so the redo's create does not collide.
            clearSlot: c => TryDeleteCharacterAsync(server, authTicket, slot, c));

    /// <summary>
    /// Two-stage STATION login. Fresh characters are forced to their home
    /// SPACE StartSector on first login by
    /// <c>Player::ReInitializeSavedData</c> (the <c>StartSector[]</c> table
    /// in <c>server/src/StaticData.h</c> is all space sector ids &lt; 10000,
    /// a deliberate fidelity decision -- fresh chars spawn in space with the
    /// home station visible nearby, not docked). A station handshake --
    /// <c>SectorManager::StationLogin2</c> and the furniture opcodes it fans
    /// out (StarbaseSet, NameDecal, Decal, ClientAvatar/ClientShip,
    /// ConstantPositionalUpdate, Create, ManufactureSet) -- is therefore only
    /// reachable on a SECOND login. This helper runs stage 1 (create avatar +
    /// complete the forced home-space login via <see cref="EstablishAsync"/>),
    /// disposes the home session -- whose teardown logs off cleanly with a
    /// 0x00B9 LOGOFF_REQUEST so the server runs DropPlayerFromGalaxy
    /// synchronously (avoids G_ERROR_ACCOUNT_IN_USE on stage 2, see
    /// <see cref="Session.DisposeAsync"/>) -- then runs stage 2
    /// (<see cref="ReestablishAsync"/> to
    /// <paramref name="stationSectorId"/>, whose ToSectorID the second login
    /// preserves -- see <see cref="ReestablishAsync"/>). Returns the station
    /// Session; the caller asserts on its <see cref="Session.HandshakeFrames"/>.
    /// </summary>
    public static async Task<Session> EstablishAtStationAsync(
        ServerFixture server,
        string authTicket,
        string accountUsername,
        int slot,
        int stationSectorId,
        int homeSpaceSectorId,
        string firstName,
        string shipName,
        CancellationToken ct)
    {
        var homeSession = await EstablishAsync(
            server, authTicket, accountUsername, slot, homeSpaceSectorId,
            firstName, shipName, ct);

        // Disposing the home session logs it off cleanly (0x00B9 ->
        // DropPlayerFromGalaxy, see Session.DisposeAsync), dropping the
        // stage-1 Player synchronously so stage 2's relogin on the same
        // account does not hit G_ERROR_ACCOUNT_IN_USE.
        await homeSession.DisposeAsync();

        return await ReestablishAsync(server, authTicket, slot, stationSectorId, ct);
    }

    // One establish attempt: open a fresh global conn, create the avatar,
    // master-join, and drive the sector handshake to 0x0005 START. Throws
    // ProxyWedgeException (via MasterJoinThenSectorLoginAsync) when the proxy
    // wedge stalls the handshake; the public EstablishAsync wrapper recycles
    // the proxy and retries with fresh connections.
    private static async Task<Session> EstablishOnceAsync(
        ServerFixture server,
        string authTicket,
        string accountUsername,
        int slot,
        int sectorId,
        string firstName,
        string shipName,
        CancellationToken ct,
        int race = 0,
        int profession = 0,
        int gender = 0)
    {
        var globalConn = await EncryptedTcpConnection.ConnectAsync(
            server.GlobalHost, server.GlobalPort, ct);

        bool characterCreated = false;
        try
        {
            await SendGlobalConnectAsync(globalConn, authTicket, ct);
            await DrainUntilOpcode(globalConn, OpcodeId.Known.GlobalAvatarList.Value, ct);

            await CreateCharacterOnSlotAsync(
                globalConn, accountUsername, slot, firstName, shipName, ct,
                race, profession, gender);
            characterCreated = true;

            int gameId = await RequestTicketAsync(globalConn, slot, ct);

            const int PlayerTag = 1 << 30;
            Assert.True((gameId & PlayerTag) != 0,
                $"GameID 0x{gameId:X8} missing PLAYER_TAG -- GlobalTicketRequest hit the failure path.");

            var (sectorConn, startId, handshakePayloads) =
                await MasterJoinThenSectorLoginAsync(
                    server, authTicket, gameId, sectorId, ct);

            return new Session
            {
                Global = globalConn,
                Sector = sectorConn,
                GameId = gameId,
                StartId = startId,
                Slot = slot,
                HandshakeOpcodes = handshakePayloads.Select(f => f.Opcode).ToList(),
                HandshakeFrames = handshakePayloads.Select(f => (f.Opcode, f.Payload.Length)).ToList(),
                HandshakePayloads = handshakePayloads,
            };
        }
        catch
        {
            // Best-effort: if we created the avatar before failing, delete it
            // so a re-run lands in the empty-slot baseline. A leaked avatar
            // makes the next run's fixed-name create fail with GlobalError
            // code=1 (name already taken). Reuse the still-open global
            // connection -- opening a second one would trip the server's
            // duplicate-login force-kick and the delete would silently no-op.
            if (characterCreated)
            {
                try
                {
                    using var cleanupCts = new CancellationTokenSource(
                        TimeSpan.FromSeconds(15));
                    await DeleteCreatedCharacterAsync(globalConn, slot, cleanupCts.Token);
                }
                catch { /* best-effort cleanup; surface the original failure */ }
            }
            await globalConn.DisposeAsync();
            throw;
        }
    }

    /// <summary>
    /// Reconnect against an existing avatar (e.g. one created by a prior
    /// <see cref="EstablishAsync"/> call) and re-run the sector handshake
    /// against <paramref name="sectorId"/>. Skips the
    /// <see cref="CreateCharacterOnSlotAsync"/> step -- the slot is
    /// expected to already contain a created avatar.
    ///
    /// <para>
    /// Use case: drive a sector login at an arbitrary target sector that is
    /// NOT the character's home StartSector. Fresh characters spawn in SPACE
    /// in their home sector (StartSector[race*3+profession], all &lt; 10000 --
    /// see server/src/StaticData.h), so the first login always lands in
    /// <c>SectorManager::SectorLogin</c>. To reach a STATION
    /// (<c>SectorManager::StationLogin</c>, sector_id &gt; 9999, e.g. Luna
    /// Station 10151) or any other non-home sector, a second login is
    /// required. <c>Player::ReadSavedData</c> takes the
    /// <c>ReloadSavedData</c> path on the second login (avatar_level_info
    /// row now exists from the first login's
    /// <c>ReInitializeSavedData</c>), and that path preserves the
    /// sector_num set by <c>Player::HandleLogin</c> from the LOGIN
    /// packet's <c>ToSectorID</c>
    /// (<c>server/src/PlayerSaves.cpp:289-291</c>).
    /// </para>
    /// </summary>
    public static Task<Session> ReestablishAsync(
        ServerFixture server,
        string authTicket,
        int slot,
        int sectorId,
        CancellationToken ct)
        => WithProxyRecycleOnWedgeAsync(
            server, ct,
            () => ReestablishOnceAsync(server, authTicket, slot, sectorId, ct));
            // No clearSlot: the avatar is expected to already exist; the redo
            // reconnects against it rather than recreating it.

    // One reconnect attempt against an existing avatar; see ReestablishAsync.
    private static async Task<Session> ReestablishOnceAsync(
        ServerFixture server,
        string authTicket,
        int slot,
        int sectorId,
        CancellationToken ct)
    {
        var globalConn = await EncryptedTcpConnection.ConnectAsync(
            server.GlobalHost, server.GlobalPort, ct);

        try
        {
            await SendGlobalConnectAsync(globalConn, authTicket, ct);
            await DrainUntilOpcode(globalConn, OpcodeId.Known.GlobalAvatarList.Value, ct);

            int gameId = await RequestTicketAsync(globalConn, slot, ct);

            const int PlayerTag = 1 << 30;
            Assert.True((gameId & PlayerTag) != 0,
                $"GameID 0x{gameId:X8} missing PLAYER_TAG -- GlobalTicketRequest hit the failure path.");

            var (sectorConn, startId, handshakePayloads) =
                await MasterJoinThenSectorLoginAsync(
                    server, authTicket, gameId, sectorId, ct);

            return new Session
            {
                Global = globalConn,
                Sector = sectorConn,
                GameId = gameId,
                StartId = startId,
                Slot = slot,
                HandshakeOpcodes = handshakePayloads.Select(f => f.Opcode).ToList(),
                HandshakeFrames = handshakePayloads.Select(f => (f.Opcode, f.Payload.Length)).ToList(),
                HandshakePayloads = handshakePayloads,
            };
        }
        catch
        {
            await globalConn.DisposeAsync();
            throw;
        }
    }

    // -- Single-client-proxy session-accumulation wedge mitigation -----------
    // The Net7Proxy is a documented single-client bridge. Driving hundreds of
    // serial connect->login->disconnect cycles through ONE instance latches
    // per-session proxy state that, after ~40 logins, wedges every later
    // sector login: the handshake stalls before the 0x0005 START frame at
    // stage 12 (the stage whose confirm coincides with the first in-game
    // position fan-out). A controlled experiment isolated the wedge to the
    // PROXY: restarting ONLY the proxy container (server, login, postgres
    // untouched) recovers the suite, while re-establishing the handshake in
    // place does NOT -- the latched state survives a fresh master-join. So
    // the mitigation is a proxy RECYCLE, not an in-place retry. The precise
    // never-reset proxy global is a real proxy defect tracked in
    // plans/11-phase-k-ingame.md; until it is fixed at source, the suite
    // recycles the proxy on the wedge.
    //
    // This is a TEST-INFRA mitigation, NOT a server-fidelity relaxation: no
    // wire bytes change, and the server only ever sees a clean disconnect +
    // fresh reconnect -- exactly what it would see if the client crashed and
    // relaunched. The escalation lives at the establish level (not inside the
    // handshake) because clearing the wedge drops the caller-owned global
    // connection too, so the whole session -- fresh global conn, avatar,
    // sector -- must be re-established, not just the sector leg.
    private const int EstablishMaxAttempts = 3;
    private static readonly TimeSpan SectorHandshakeAttemptTimeout =
        TimeSpan.FromSeconds(18);

    /// <summary>
    /// Signals that a sector handshake stalled before the 0x0005 START frame
    /// because the proxy hit its single-client session-accumulation wedge.
    /// Caught by <see cref="WithProxyRecycleOnWedgeAsync"/>, which recycles
    /// the proxy (<see cref="ServerFixture.RestartProxyAsync"/>) and retries
    /// the whole establish with fresh connections.
    /// </summary>
    private sealed class ProxyWedgeException : Exception
    {
        public ProxyWedgeException(int sectorId, int gameId)
            : base($"sector={sectorId}, game=0x{gameId:X8} stalled before " +
                   $"0x0005 START within " +
                   $"{SectorHandshakeAttemptTimeout.TotalSeconds:F0}s")
        {
        }
    }

    /// <summary>
    /// Run <paramref name="attempt"/>; on a <see cref="ProxyWedgeException"/>
    /// recycle the proxy and retry with fresh connections, up to
    /// <see cref="EstablishMaxAttempts"/>. <paramref name="clearSlot"/> (if
    /// supplied) runs after the recycle to guarantee an empty character slot
    /// before a create-path redo. Any other exception propagates unchanged.
    /// </summary>
    private static async Task<Session> WithProxyRecycleOnWedgeAsync(
        ServerFixture server,
        CancellationToken ct,
        Func<Task<Session>> attempt,
        Func<CancellationToken, Task>? clearSlot = null)
    {
        for (int n = 1; ; n++)
        {
            try
            {
                return await attempt();
            }
            catch (ProxyWedgeException ex)
                when (n < EstablishMaxAttempts && !ct.IsCancellationRequested)
            {
                // Surfaced loudly -- the wedge is a real proxy defect, not
                // noise.
                Console.Error.WriteLine(
                    $"[SectorHandshake] establish attempt {n}/{EstablishMaxAttempts} " +
                    $"hit the proxy session-accumulation wedge ({ex.Message}); " +
                    $"recycling the proxy and retrying with fresh connections.");

                await server.RestartProxyAsync(ct);

                if (clearSlot is not null)
                {
                    try { await clearSlot(ct); }
                    catch { /* best-effort; a real collision surfaces on redo */ }
                }
            }
        }
    }

    /// <summary>
    /// Open a fresh global connection, delete the avatar on
    /// <paramref name="slot"/>, and close. Used after a proxy recycle to
    /// guarantee an empty slot before a create-path redo (the wedged
    /// attempt's own delete ran on a connection the recycle then dropped).
    /// The prior global connection is dead post-recycle, so this fresh one
    /// does not trip the server's duplicate-login force-kick.
    /// </summary>
    private static async Task TryDeleteCharacterAsync(
        ServerFixture server, string authTicket, int slot, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(15));

        var conn = await EncryptedTcpConnection.ConnectAsync(
            server.GlobalHost, server.GlobalPort, cts.Token);
        try
        {
            await SendGlobalConnectAsync(conn, authTicket, cts.Token);
            await DrainUntilOpcode(conn, OpcodeId.Known.GlobalAvatarList.Value, cts.Token);
            await DeleteCreatedCharacterAsync(conn, slot, cts.Token);
        }
        finally
        {
            await conn.DisposeAsync();
        }
    }

    private static async Task<(EncryptedTcpConnection conn, int startId,
        IReadOnlyList<(ushort Opcode, byte[] Payload)> frames)>
        MasterJoinThenSectorLoginAsync(
            ServerFixture server, string authTicket, int gameId, int sectorId,
            CancellationToken ct)
    {
        using var attemptCts =
            CancellationTokenSource.CreateLinkedTokenSource(ct);
        attemptCts.CancelAfter(SectorHandshakeAttemptTimeout);
        try
        {
            var redirect = await DoMasterJoinAsync(
                server, authTicket, gameId, sectorId, attemptCts.Token);
            Assert.Equal(sectorId, redirect.SectorId);
            Assert.Equal(server.SectorPort, redirect.ServerEndPoint.Port);

            return await DoSectorLoginUntilStartAsync(
                server, authTicket, gameId, sectorId, attemptCts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Stalled before 0x0005 START within the attempt window: the
            // proxy's session-accumulation wedge. Re-establishing in place
            // does NOT clear it (verified empirically) -- only a proxy
            // recycle does. Signal the establish-level retry.
            throw new ProxyWedgeException(sectorId, gameId);
        }
    }

    /// <summary>
    /// Best-effort post-test cleanup: send 0x0071 GlobalDeleteCharacter
    /// on <paramref name="global"/> for <paramref name="slot"/> and wait
    /// for the refreshed avatar list. Wrap in try/catch at the call site
    /// -- primary test failure has already been reported.
    /// </summary>
    public static async Task DeleteCreatedCharacterAsync(
        EncryptedTcpConnection global, int slot, CancellationToken ct)
    {
        byte[] payload = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(payload, (uint)slot);

        await global.SendAsync(
            Packet.ForOpcode(OpcodeId.Known.GlobalDeleteCharacter.Value, payload), ct);

        await DrainUntilOpcode(global, OpcodeId.Known.GlobalAvatarList.Value, ct);
    }

    public static async Task SendGlobalConnectAsync(
        EncryptedTcpConnection conn, string ticket, CancellationToken ct)
    {
        byte[] ticketBytes = Encoding.ASCII.GetBytes(ticket);
        byte[] payload = new byte[4 + ticketBytes.Length + 1];
        BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(0, 4), (uint)ticketBytes.Length);
        ticketBytes.CopyTo(payload, 4);
        payload[^1] = 0;

        await conn.SendAsync(
            Packet.ForOpcode(OpcodeId.Known.GlobalConnect.Value, payload), ct);
    }

    public static async Task CreateCharacterOnSlotAsync(
        EncryptedTcpConnection conn,
        string accountUsername,
        int slot,
        string firstName,
        string shipName,
        CancellationToken ct,
        int race = 0,
        int profession = 0,
        int gender = 0)
    {
        // race/profession default to Terran Warrior (StartSector index 0 =
        // 10151 Luna Station) so every existing caller is unchanged. Pass
        // race=2/profession=2 (Progen Explorer, StartSector index 8 = 10301)
        // to create a prospecting-capable Explorer -- prof MUST be 2 or the
        // server clamps SKILL_PROSPECT to 0 at login (see SectorMiningTests).
        byte[] payload = BuildCreateCharacterPayload(
            galaxyId: 1,
            characterSlot: slot,
            accountUsername: accountUsername,
            firstName: firstName,
            race: race,
            profession: profession,
            gender: gender,
            shipName: shipName);

        await conn.SendAsync(
            Packet.ForOpcode(OpcodeId.Known.GlobalCreateCharacter.Value, payload), ct);

        await DrainUntilOpcode(conn, OpcodeId.Known.GlobalAvatarList.Value, ct);
    }

    public static async Task<int> RequestTicketAsync(
        EncryptedTcpConnection conn, int slot, CancellationToken ct)
    {
        byte[] slotPayload = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(slotPayload, slot);

        await conn.SendAsync(
            Packet.ForOpcode(OpcodeId.Known.GlobalTicketRequest.Value, slotPayload), ct);

        var reply = await DrainUntilOpcode(conn, OpcodeId.Known.GlobalTicket.Value, ct);
        var ticket = (GlobalTicket)new GlobalTicketCodec().DecodeInbound(reply.Payload.Span);
        Assert.True(ticket.ResponseCode == 0,
            $"GlobalTicket response_code={ticket.ResponseCode}; expected 0.");
        return ticket.AvatarId;
    }

    /// <summary>
    /// Build the 539-byte GlobalCreateCharacter wire payload. See
    /// <c>GlobalCreateCharacterTests.BuildCreateCharacterPayload</c>
    /// for the field-by-field justification.
    /// </summary>
    public static byte[] BuildCreateCharacterPayload(
        int galaxyId,
        int characterSlot,
        string accountUsername,
        string firstName,
        int race,
        int profession,
        int gender,
        string shipName)
    {
        byte[] payload = new byte[539];

        BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(0, 4), galaxyId);
        BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(4, 4), characterSlot);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(8, 4), 0);

        var usernameBytes = Encoding.ASCII.GetBytes(accountUsername);
        if (usernameBytes.Length >= 65)
            throw new ArgumentException(
                $"account_username '{accountUsername}' is {usernameBytes.Length}B but the wire field is 65B");
        usernameBytes.CopyTo(payload.AsSpan(12, 65));

        const int AvatarOffset = 77;
        var firstNameBytes = Encoding.ASCII.GetBytes(firstName);
        if (firstNameBytes.Length >= 20)
            throw new ArgumentException(
                $"first_name '{firstName}' is {firstNameBytes.Length}B but the wire field is 20B");
        firstNameBytes.CopyTo(payload.AsSpan(AvatarOffset + 0, 20));
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(AvatarOffset + 40, 4), 0);
        payload[AvatarOffset + 44] = 0;
        payload[AvatarOffset + 45] = 0;
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(AvatarOffset + 46, 4), race);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(AvatarOffset + 50, 4), profession);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(AvatarOffset + 54, 4), gender);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(AvatarOffset + 58, 4), 0);

        const int ShipOffset = 318;
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(ShipOffset + 0, 4), race);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(ShipOffset + 4, 4), profession);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(ShipOffset + 8, 4), 0);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(ShipOffset + 12, 4), 0);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(ShipOffset + 16, 4), 0);
        var shipNameBytes = Encoding.ASCII.GetBytes(shipName);
        if (shipNameBytes.Length >= 26)
            throw new ArgumentException(
                $"ship_name '{shipName}' is {shipNameBytes.Length}B but the wire field is 26B");
        shipNameBytes.CopyTo(payload.AsSpan(ShipOffset + 20, 26));

        return payload;
    }

    /// <summary>
    /// Open a master-server TCP connection, send MasterJoin with the
    /// allocated GameID, wait for the ServerRedirect. Closes the
    /// master TCP on return.
    /// </summary>
    public static async Task<ServerRedirect> DoMasterJoinAsync(
        ServerFixture server, string authTicket, int gameId, int sectorId,
        CancellationToken ct)
    {
        await using var conn = await EncryptedTcpConnection.ConnectAsync(
            server.MasterHost, server.MasterPort, ct);

        var ticketBytes = new byte[MasterJoinCodec.TicketLength];
        Encoding.ASCII.GetBytes(
            authTicket.AsSpan(0, Math.Min(authTicket.Length, MasterJoinCodec.TicketLength)),
            ticketBytes);

        var join = new MasterJoinRequest(
            Unknown1: 0, Unknown2: 0, Unknown3: 0,
            AvatarIdMsb: 0, AvatarIdLsb: gameId,
            ToSectorId: sectorId, FromSectorId: 0,
            PlayerLevel: 1, Unknown8: 0, Unknown9: 0, Unknown10: 0,
            Ticket: ticketBytes);

        var packet = Packet.ForOpcode(
            OpcodeId.Known.MasterJoin.Value,
            new MasterJoinCodec().EncodeOutbound(join));

        await conn.SendAsync(packet, ct);

        while (true)
        {
            var reply = await conn.ReceiveAsync(ct);
            Assert.NotNull(reply);
            if (reply!.Header.Opcode == OpcodeId.Known.ServerRedirect.Value)
            {
                return (ServerRedirect)new ServerRedirectCodec()
                    .DecodeInbound(reply.Payload.Span);
            }
        }
    }

    /// <summary>
    /// Open a sector-server TCP connection, send the 137-byte LOGIN
    /// frame, drain the reply stream until 0x0005 START arrives.
    /// Returns the still-open connection so callers can keep driving
    /// in-sector opcodes through it, the start id (read out of the
    /// first 4 bytes of the START payload), and the list of (opcode,
    /// payload-length) frames seen during the drain (terminating 0x0005
    /// included) so passive-observation tests can assert on handshake
    /// fan-out emits like 0x0037 CLIENT_AVATAR, 0x0047 CLIENT_SHIP, and
    /// 0x0061 AVATAR_DESCRIPTION that the server pushes from
    /// SendLoginShipData before SendStart.
    /// </summary>
    public static async Task<(EncryptedTcpConnection conn, int startId, IReadOnlyList<(ushort Opcode, byte[] Payload)> frames)>
        DoSectorLoginUntilStartAsync(
            ServerFixture server, string authTicket, int gameId, int sectorId,
            CancellationToken ct)
    {
        var conn = await EncryptedTcpConnection.ConnectAsync(
            server.SectorHost, server.SectorPort, ct);

        try
        {
            await conn.SendAsync(BuildLoginPacket(authTicket, gameId, sectorId), ct);

            var frames = new List<(ushort, byte[])>();
            int framesSeen = 0;
            const int maxFrames = 4000;
            while (framesSeen++ < maxFrames)
            {
                var reply = await conn.ReceiveAsync(ct);
                Assert.NotNull(reply);

                // Defensive copy: the receive buffer may be pooled/recycled.
                frames.Add((reply!.Header.Opcode, reply.Payload.ToArray()));

                if (reply.Header.Opcode == OpcodeId.Known.Start.Value)
                {
                    int startId = reply.Payload.Length >= 4
                        ? BinaryPrimitives.ReadInt32LittleEndian(reply.Payload.Span[..4])
                        : 0;
                    return (conn, startId, frames);
                }
            }

            throw new Xunit.Sdk.XunitException(
                $"drained {maxFrames} frames from sector TCP without seeing 0x0005 START.");
        }
        catch
        {
            await conn.DisposeAsync();
            throw;
        }
    }

    /// <summary>
    /// Build the 137-byte Login payload -- a packed Login struct
    /// (<c>common/include/net7/PacketStructures.h:407-413</c>):
    /// MasterJoin (64) + TimeSent (4) + LoginData (65) + TimeReceived (4).
    /// </summary>
    public static Packet BuildLoginPacket(string authTicket, int gameId, int sectorId)
    {
        var payload = new byte[64 + 4 + 65 + 4];

        var ticketBytes = new byte[MasterJoinCodec.TicketLength];
        Encoding.ASCII.GetBytes(
            authTicket.AsSpan(0, Math.Min(authTicket.Length, MasterJoinCodec.TicketLength)),
            ticketBytes);

        var join = new MasterJoinRequest(
            Unknown1: 0, Unknown2: 0, Unknown3: 0,
            AvatarIdMsb: 0, AvatarIdLsb: gameId,
            ToSectorId: sectorId, FromSectorId: 0,
            PlayerLevel: 1, Unknown8: 0, Unknown9: 0, Unknown10: 0,
            Ticket: ticketBytes);

        new MasterJoinCodec().EncodeOutbound(join).CopyTo(payload, 0);

        return Packet.ForOpcode(OpcodeId.Known.Login.Value, payload);
    }

    /// <summary>
    /// Drain frames from <paramref name="conn"/> until one with the
    /// given opcode arrives. Surfaces 0x0075 GlobalError loudly instead
    /// of letting the test time out on the outer CTS.
    /// </summary>
    public static async Task<Packet> DrainUntilOpcode(
        EncryptedTcpConnection conn,
        ushort targetOpcode,
        CancellationToken ct)
    {
        while (true)
        {
            var p = await conn.ReceiveAsync(ct);
            Assert.NotNull(p);

            if (p!.Header.Opcode == targetOpcode)
                return p;

            if (p.Header.Opcode == OpcodeId.Known.GlobalError.Value)
            {
                var span = p.Payload.Span;
                int errCode = -1;
                if (span.Length >= 8)
                    errCode = BinaryPrimitives.ReadInt32BigEndian(span.Slice(4, 4)) - 7;
                throw new Xunit.Sdk.XunitException(
                    $"server returned GlobalError code={errCode}; expected opcode 0x{targetOpcode:X4}");
            }
        }
    }
}
