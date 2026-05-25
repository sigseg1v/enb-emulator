// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Buffers.Binary;
using System.Text;
using N7.CliClient.Auth;
using N7.CliClient.Net;
using N7.CliClient.Opcodes;
using N7.CliClient.Opcodes.Inbound;
using N7.CliClient.Opcodes.Outbound;
using Xunit;

namespace N7.CliClient.IntegrationTests.Opcodes;

/// <summary>
/// End-to-end happy-path test for the sector-login handshake:
/// Auth → GlobalConnect → GlobalTicketRequest → MasterJoin → sector
/// TCP LOGIN (0x0002) → drained 0x2020 stages → 0x0005 START.
///
/// <para>
/// This test is the integration harness for the entire Phase K UDP
/// plane plus the proxy's MVAS-fan-out path. It exercises:
/// </para>
/// <list type="bullet">
///   <item>The unconnected global plane on the proxy
///         (<c>proxy/UDPClient_linux.cpp</c> with
///         <c>m_Unconnected=true</c>) — server→proxy in-game UDP comes
///         from <c>server:3806</c> (MVASauth) to the proxy's global-plane
///         source port, which a connected SOCK_DGRAM would silently
///         drop because the peer port doesn't match the connect()'d
///         peer (3810).</item>
///   <item>The proxy's <c>HandleStageConfirm</c> automatically replying
///         0x2021 ACKs on the client's behalf (the client over TCP 3500
///         never sees the 0x2020 frames — the proxy consumes them in
///         <c>HandleCustomOpcode</c>).</item>
///   <item>The server's login state machine (stages 1→2→...→13 in
///         <c>server/src/PlayerManager.cpp:534-601</c>) advancing all
///         the way to <c>CompleteLogin</c> + <c>SendStart</c>.</item>
///   <item>The 4-byte <c>int32_t</c> wire format for stage IDs (Win32
///         <c>sizeof(long)=4</c>; Linux <c>sizeof(long)=8</c> sent 4
///         garbage bytes that scrambled subsequent opcodes in the UDP
///         packet sequence). Same wire-size class as the Phase K
///         MasterJoin / GlobalTicket fixes.</item>
/// </list>
///
/// <para>
/// Prerequisite: GlobalTicketRequest must run first so the server
/// creates the <c>Player</c> object keyed by GameID; otherwise
/// <c>UDP_Master::ProcessHandoff</c> can't find the player and falls
/// to the 0x100A TERMINATE error path (proxy then hits its
/// SendMasterLogin timeout fallback at ~5s).
/// </para>
///
/// <para>
/// Budget: 60s. The actual happy path runs sub-2s; the wide budget
/// catches a regression where any link in the chain falls back to the
/// 5s WaitForResponse timeout. The login state machine has four
/// wait-for-ack rounds (stages 3/6/9/12) plus per-stage server work;
/// each ack round adds ~100ms of poll latency.
/// </para>
/// </summary>
[Collection(ServerCollection.Name)]
public sealed class SectorLoginTests
{
    private readonly ServerFixture _server;
    private readonly ClientFixture _client;

    public SectorLoginTests(ServerFixture server)
    {
        _server = server;
        _client = new ClientFixture(server);
    }

    [Fact]
    public async Task FullSectorLogin_ReceivesStart()
    {
        // cli_test04 — Pool[3]. Reserved here so the per-compose-lifetime
        // CreateCharacter / DeleteCharacter cycle this test runs can't
        // collide on IsUsernameUnique with the create-character test
        // (which uses Pool[2]).
        var account = TestAccounts.Pool[3];
        const int slot = 0;

        // Terran Warrior starting sector from avatar_base
        // (StartSector[0*3+0] = 10151 = Luna Station). The seeded test
        // accounts have NO avatars in the DB (per GlobalDeleteCharacter
        // / GlobalCreateCharacter docstrings) — so a happy-path sector
        // LOGIN requires creating one first. ReadDatabase in
        // FirstLogin/SectorLogin needs real avatar rows, otherwise
        // the login state machine stalls at stage 1 with no
        // ship/inventory loaded.
        const int sectorId = 10151;
        const string CharacterFirstName = "Loginus";
        const string ShipName = "LoginShip";

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));

        // ---- Auth login → ticket ----
        var login = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(account.Username, account.Password),
            cts.Token);
        Assert.True(login.Valid, $"login: {login.RawBody.TrimEnd()}");
        Assert.False(string.IsNullOrEmpty(login.Ticket));

        // ---- GlobalConnect + CreateCharacter (must precede TicketRequest) ----
        await using var globalConn = await EncryptedTcpConnection.ConnectAsync(
            _server.GlobalHost, _server.GlobalPort, cts.Token);

        await SendGlobalConnectAsync(globalConn, login.Ticket!, cts.Token);
        await DrainUntilOpcode(globalConn, OpcodeId.Known.GlobalAvatarList.Value, cts.Token);

        try
        {
            await CreateCharacterOnSlotAsync(
                globalConn, account.Username, slot, CharacterFirstName, ShipName, cts.Token);

            // ---- GlobalTicketRequest → GameID (allocates Player on server) ----
            int gameId = await RequestTicketAsync(globalConn, slot, cts.Token);

            const int PlayerTag = 1 << 30;
            Assert.True((gameId & PlayerTag) != 0,
                $"GameID 0x{gameId:X8} missing PLAYER_TAG — GlobalTicketRequest hit the failure path. " +
                $"Verify GlobalTicketRequestTests passes first.");

            // ---- MasterJoin → ServerRedirect ----
            // With a valid GameID the server's ProcessHandoff finds the
            // player and replies 0x2009 with the real sector port; the proxy
            // builds the ServerRedirect from that. No 5s fallback.
            var redirect = await DoMasterJoinAsync(
                login.Ticket!, gameId, sectorId, cts.Token);

            Assert.Equal(sectorId, redirect.SectorId);
            Assert.Equal(_server.SectorPort, redirect.ServerEndPoint.Port);

            // ---- Sector TCP LOGIN → drain until START ----
            await DoSectorLoginUntilStartAsync(login.Ticket!, gameId, sectorId, cts.Token);
        }
        finally
        {
            // Cleanup: delete the created character so a re-run of the
            // test (or any other test using this account) starts from the
            // empty-slot baseline. Use a fresh CTS — the outer one may
            // have fired on a test failure, but we still want to attempt
            // cleanup.
            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await DeleteCharacterOnSlotAsync(globalConn, slot, cleanupCts.Token); }
            catch { /* best-effort cleanup; primary failure already reported */ }
        }
    }

    private static async Task SendGlobalConnectAsync(
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

    private static async Task CreateCharacterOnSlotAsync(
        EncryptedTcpConnection conn,
        string accountUsername,
        int slot,
        string firstName,
        string shipName,
        CancellationToken ct)
    {
        byte[] payload = BuildCreateCharacterPayload(
            galaxyId: 1,
            characterSlot: slot,
            accountUsername: accountUsername,
            firstName: firstName,
            race: 0,        // Terran
            profession: 0,  // Warrior  →  StartSector[0*3+0] = 10151 Luna Station
            gender: 0,
            shipName: shipName);

        await conn.SendAsync(
            Packet.ForOpcode(OpcodeId.Known.GlobalCreateCharacter.Value, payload), ct);

        await DrainUntilOpcode(conn, OpcodeId.Known.GlobalAvatarList.Value, ct);
    }

    private static async Task<int> RequestTicketAsync(
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

    private static async Task DeleteCharacterOnSlotAsync(
        EncryptedTcpConnection conn, int slot, CancellationToken ct)
    {
        byte[] payload = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(payload, (uint)slot);

        await conn.SendAsync(
            Packet.ForOpcode(OpcodeId.Known.GlobalDeleteCharacter.Value, payload), ct);

        await DrainUntilOpcode(conn, OpcodeId.Known.GlobalAvatarList.Value, ct);
    }

    /// <summary>
    /// Build the 539-byte GlobalCreateCharacter wire payload. Mirrors
    /// <c>GlobalCreateCharacterTests.BuildCreateCharacterPayload</c> —
    /// see that file for the field-by-field justification.
    /// </summary>
    private static byte[] BuildCreateCharacterPayload(
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
    /// Open a fresh master-server TCP connection, send MasterJoin with
    /// the real GameID (NOT the bare account.Id — that's the fallback
    /// path used by MasterJoinTests), and wait for the ServerRedirect.
    /// </summary>
    private async Task<ServerRedirect> DoMasterJoinAsync(
        string authTicket, int gameId, int sectorId, CancellationToken ct)
    {
        await using var conn = await EncryptedTcpConnection.ConnectAsync(
            _server.MasterHost, _server.MasterPort, ct);

        // Match MasterJoinTests's ticket-byte construction: truncate the
        // ASCII hex ticket to 20 bytes. The proxy's HandleMasterJoin
        // doesn't validate the ticket field byte-for-byte today; the
        // matching primary-source fix is to hex-decode the auth ticket,
        // not truncate, but that's an orthogonal Phase K item.
        var ticketBytes = new byte[MasterJoinCodec.TicketLength];
        Encoding.ASCII.GetBytes(
            authTicket.AsSpan(0, Math.Min(authTicket.Length, MasterJoinCodec.TicketLength)),
            ticketBytes);

        var join = new MasterJoinRequest(
            Unknown1: 0,
            Unknown2: 0,
            Unknown3: 0,
            AvatarIdMsb: 0,
            AvatarIdLsb: gameId,
            ToSectorId: sectorId,
            FromSectorId: 0,
            PlayerLevel: 1,
            Unknown8: 0,
            Unknown9: 0,
            Unknown10: 0,
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
    /// frame, and drain the reply stream until 0x0005 START arrives.
    /// Intermediate opcodes (sector data files, ship info, etc.) are
    /// drained opaquely. The proxy auto-ACKs 0x2020 LOGIN_STAGE frames
    /// on the server's MVAS plane, so they never reach the client TCP.
    /// </summary>
    private async Task DoSectorLoginUntilStartAsync(
        string authTicket, int gameId, int sectorId, CancellationToken ct)
    {
        await using var conn = await EncryptedTcpConnection.ConnectAsync(
            _server.SectorHost, _server.SectorPort, ct);

        await conn.SendAsync(BuildLoginPacket(authTicket, gameId, sectorId), ct);

        // Drain frames. Cap iterations so a server-side stall ends in a
        // test failure rather than a 60s cts cancellation: with the
        // server's 100ms PlayerManager poll and per-stage work, even a
        // verbose login path (sector data file → greeting → ship info →
        // lounge NPCs → ...) lands in a few hundred frames at most.
        int framesSeen = 0;
        const int maxFrames = 4000;
        while (framesSeen++ < maxFrames)
        {
            var reply = await conn.ReceiveAsync(ct);
            Assert.NotNull(reply);

            ushort op = reply!.Header.Opcode;
            if (op == OpcodeId.Known.Start.Value)
            {
                // Happy path: server reached CompleteLogin (stage 13)
                // and called SendStart. We don't ACK the START here —
                // the proxy's ENB_OPCODE_0006_START_ACK handler does
                // that for us once we send 0x0006 back; but the test's
                // job is just "did we reach START".
                return;
            }
        }

        throw new Xunit.Sdk.XunitException(
            $"drained {maxFrames} frames from sector TCP without seeing 0x0005 START. " +
            $"Likely the login state machine stalled (check server log for stage progression) " +
            $"or the unconnected global plane regressed (server→proxy 0x2020 frames dropped, " +
            $"server then loops forever in WaitForLoginAck).");
    }

    /// <summary>
    /// Build the 137-byte Login payload — a packed Login struct
    /// (<c>common/include/net7/PacketStructures.h:407-413</c>):
    /// MasterJoin (64) + TimeSent (4) + LoginData (65) + TimeReceived (4).
    /// </summary>
    private static Packet BuildLoginPacket(string authTicket, int gameId, int sectorId)
    {
        var payload = new byte[64 + 4 + 65 + 4];

        // [0..64) — embedded MasterJoin (BE int32s + 20 ticket bytes).
        var ticketBytes = new byte[MasterJoinCodec.TicketLength];
        Encoding.ASCII.GetBytes(
            authTicket.AsSpan(0, Math.Min(authTicket.Length, MasterJoinCodec.TicketLength)),
            ticketBytes);

        var join = new MasterJoinRequest(
            Unknown1: 0,
            Unknown2: 0,
            Unknown3: 0,
            AvatarIdMsb: 0,
            AvatarIdLsb: gameId,
            ToSectorId: sectorId,
            FromSectorId: 0,
            PlayerLevel: 1,
            Unknown8: 0,
            Unknown9: 0,
            Unknown10: 0,
            Ticket: ticketBytes);

        new MasterJoinCodec().EncodeOutbound(join).CopyTo(payload, 0);

        // [64..68) — TimeSent. Server stores it verbatim into
        // m_JoinTime (PlayerConnection.cpp:669); doesn't byte-swap.
        // Leave zero — value isn't used by anything we assert against.

        // [68..133) — LoginData: 40 + 18 ts + 7 nulls. Server doesn't
        // inspect this either; zero-fill is fine. (The 18-byte
        // timestamp string was a debug-only field on retail.)

        // [133..137) — TimeReceived. Same story as TimeSent.

        return Packet.ForOpcode(OpcodeId.Known.Login.Value, payload);
    }

    /// <summary>
    /// Drain until <paramref name="targetOpcode"/>; surface a 0x0075
    /// GlobalError loudly instead of timing out.
    /// </summary>
    private static async Task<Packet> DrainUntilOpcode(
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
