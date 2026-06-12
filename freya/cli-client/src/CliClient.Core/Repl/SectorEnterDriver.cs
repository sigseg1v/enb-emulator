// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Buffers.Binary;
using System.Text;
using N7.CliClient.Net;
using N7.CliClient.Opcodes;
using N7.CliClient.Opcodes.Inbound;
using N7.CliClient.Opcodes.Outbound;

namespace N7.CliClient.Repl;

/// <summary>
/// Non-xUnit port of the integration-test SectorHandshake driver. Lives
/// here so the REPL's `create` and `enter` commands can drive the real
/// global -> master -> sector chain without dragging xUnit's
/// <c>Assert</c> / <c>XunitException</c> types into the app binary.
/// </summary>
public static class SectorEnterDriver
{
    public sealed record SectorEntryResult(
        EncryptedTcpConnection Sector,
        int GameId,
        int StartId,
        int Slot,
        int SectorId,
        IReadOnlyList<Packet> HandshakeFrames);

    /// <summary>
    /// Drive GlobalCreateCharacter on an already-open global connection
    /// and wait for the refreshed avatar list. Returns the decoded list.
    /// </summary>
    public static async Task<GlobalAvatarList> CreateCharacterOnSlotAsync(
        EncryptedTcpConnection global,
        string accountUsername,
        int slot,
        string firstName,
        int race,
        int profession,
        int gender,
        string shipName,
        CancellationToken ct)
    {
        byte[] payload = BuildCreateCharacterPayload(
            galaxyId: 1,
            characterSlot: slot,
            accountUsername: accountUsername,
            firstName: firstName,
            race: race,
            profession: profession,
            gender: gender,
            shipName: shipName);

        await global.SendAsync(
            Packet.ForOpcode(OpcodeId.Known.GlobalCreateCharacter.Value, payload), ct);

        var reply = await DrainUntilOpcode(global, OpcodeId.Known.GlobalAvatarList.Value, ct);
        return (GlobalAvatarList)new GlobalAvatarListCodec().DecodeInbound(reply.Payload.Span);
    }

    /// <summary>
    /// Run the full sector-entry handshake against the live stack on
    /// an existing avatar. Caller owns the returned sector connection
    /// (and the global one they passed in stays open).
    /// </summary>
    public static async Task<SectorEntryResult> EnterAsync(
        SessionContext ctx,
        EncryptedTcpConnection global,
        int slot,
        int sectorId,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(global);

        int gameId = await RequestTicketAsync(global, slot, ct);
        const int PlayerTag = 1 << 30;
        if ((gameId & PlayerTag) == 0)
            throw new InvalidOperationException(
                $"GameID 0x{gameId:X8} missing PLAYER_TAG -- GlobalTicketRequest hit the failure path.");

        var redirect = await DoMasterJoinAsync(
            ctx.Host, ctx.MasterPort, ctx.Ticket!, gameId, sectorId, ct);
        if (redirect.SectorId != sectorId)
            throw new InvalidOperationException(
                $"ServerRedirect sector mismatch: got {redirect.SectorId}, expected {sectorId}");

        var (sectorConn, startId, packets) = await DoSectorLoginUntilStartAsync(
            ctx.Host, ctx.SectorPort, ctx.Ticket!, gameId, sectorId, ct);

        return new SectorEntryResult(sectorConn, gameId, startId, slot, sectorId, packets);
    }

    /// <summary>
    /// Re-join a sector the server has handed the avatar off to (the
    /// destination of a 0x003A SERVER_HANDOFF, e.g. after <c>undock</c>).
    /// Mirrors what the real client does on a handoff: a fresh MasterJoin
    /// + sector LOGIN against the new sector, re-using the EXISTING
    /// <paramref name="gameId"/> and ticket -- NOT a new GlobalTicketRequest.
    /// The server kept the player node alive (LaunchIntoSpace calls
    /// DropPlayerFromSector, not DropPlayerFromGalaxy, and set the node's
    /// sector_num to the target -- SectorManager.cpp:574-575), so the same
    /// gameId re-attaches to the space sector. The proxy re-points its
    /// UDP back-channel to the new sector inside HandleMasterJoin
    /// (proxy/ClientToMasterServer.cpp:108).
    /// </summary>
    public static async Task<SectorEntryResult> FollowHandoffAsync(
        SessionContext ctx,
        int gameId,
        int slot,
        int toSectorId,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ctx);

        var redirect = await DoMasterJoinAsync(
            ctx.Host, ctx.MasterPort, ctx.Ticket!, gameId, toSectorId, ct);
        if (redirect.SectorId != toSectorId)
            throw new InvalidOperationException(
                $"ServerRedirect sector mismatch after handoff: got {redirect.SectorId}, expected {toSectorId}");

        var (sectorConn, startId, packets) = await DoSectorLoginUntilStartAsync(
            ctx.Host, ctx.SectorPort, ctx.Ticket!, gameId, toSectorId, ct);

        return new SectorEntryResult(sectorConn, gameId, startId, slot, toSectorId, packets);
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

    public static async Task<int> RequestTicketAsync(
        EncryptedTcpConnection conn, int slot, CancellationToken ct)
    {
        byte[] slotPayload = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(slotPayload, slot);

        await conn.SendAsync(
            Packet.ForOpcode(OpcodeId.Known.GlobalTicketRequest.Value, slotPayload), ct);

        var reply = await DrainUntilOpcode(conn, OpcodeId.Known.GlobalTicket.Value, ct);
        var ticket = (GlobalTicket)new GlobalTicketCodec().DecodeInbound(reply.Payload.Span);
        if (ticket.ResponseCode != 0)
            throw new InvalidOperationException(
                $"GlobalTicket response_code={ticket.ResponseCode}; expected 0.");
        return ticket.AvatarId;
    }

    /// <summary>
    /// Build the 539-byte GlobalCreateCharacter wire payload. Mirrors the
    /// shape pinned in the integration suite's SectorHandshake.
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

    public static async Task<ServerRedirect> DoMasterJoinAsync(
        string masterHost, int masterPort,
        string authTicket, int gameId, int sectorId,
        CancellationToken ct)
    {
        await using var conn = await EncryptedTcpConnection.ConnectAsync(
            masterHost, masterPort, ct);

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
            if (reply is null)
                throw new InvalidOperationException(
                    "master server closed the connection before sending a ServerRedirect");
            if (reply.Header.Opcode == OpcodeId.Known.ServerRedirect.Value)
            {
                return (ServerRedirect)new ServerRedirectCodec()
                    .DecodeInbound(reply.Payload.Span);
            }
        }
    }

    public static async Task<(EncryptedTcpConnection conn, int startId, IReadOnlyList<Packet> frames)>
        DoSectorLoginUntilStartAsync(
            string sectorHost, int sectorPort,
            string authTicket, int gameId, int sectorId,
            CancellationToken ct)
    {
        var conn = await EncryptedTcpConnection.ConnectAsync(sectorHost, sectorPort, ct);

        try
        {
            await conn.SendAsync(BuildLoginPacket(authTicket, gameId, sectorId), ct);

            var frames = new List<Packet>();
            int framesSeen = 0;
            const int maxFrames = 4000;
            while (framesSeen++ < maxFrames)
            {
                var reply = await conn.ReceiveAsync(ct);
                if (reply is null)
                    throw new InvalidOperationException(
                        "sector server closed the connection during the LOGIN drain");

                frames.Add(reply);

                if (reply.Header.Opcode == OpcodeId.Known.Start.Value)
                {
                    int startId = reply.Payload.Length >= 4
                        ? BinaryPrimitives.ReadInt32LittleEndian(reply.Payload.Span[..4])
                        : 0;

                    // Faithful login completion: the real client replies to
                    // 0x0005 START with 0x0006 START_ACK. The proxy turns that
                    // into 0x3004 PLAYER_SHIP_SENT to the server, which is what
                    // drives Player::FinishLogin -> SetInSpace(true). Without it
                    // the avatar stays !InSpace forever, so the server's
                    // UpdatePlayerVisibilityList early-returns and we never see
                    // (or are seen by) other players in the sector -- even
                    // though sector chat already works (that path keys off the
                    // sector player-list, set at HandleSectorLogin3, not InSpace).
                    // See proxy/ClientToServer_linux_stubs.cpp ENB_OPCODE_0006.
                    await SendStartAckAsync(conn, startId, ct);

                    return (conn, startId, frames);
                }
            }

            throw new InvalidOperationException(
                $"drained {maxFrames} frames from sector TCP without seeing 0x0005 START.");
        }
        catch
        {
            await conn.DisposeAsync();
            throw;
        }
    }

    /// <summary>
    /// Reply to 0x0005 START with 0x0006 START_ACK, echoing the start id as
    /// a 4-byte little-endian int (matches PlayerConnection::SendStart's wire
    /// shape). The server's HandleStartAck ignores the payload; the proxy
    /// reads the leading int for its log line and -- crucially -- emits
    /// 0x3004 PLAYER_SHIP_SENT on our behalf, completing the in-space login.
    /// </summary>
    public static async Task SendStartAckAsync(
        EncryptedTcpConnection conn, int startId, CancellationToken ct)
        => await conn.SendAsync(BuildStartAckPacket(startId), ct);

    /// <summary>
    /// The 0x0006 START_ACK frame: a single 4-byte little-endian start id,
    /// echoing the value from the 0x0005 START frame (the on-wire shape the
    /// real client uses -- see PlayerConnection::SendStart, which writes the
    /// id as an int32). Exposed for byte-pinning tests.
    /// </summary>
    public static Packet BuildStartAckPacket(int startId)
    {
        byte[] payload = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(payload, startId);
        return Packet.ForOpcode(OpcodeId.Known.StartAck.Value, payload);
    }

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
    /// of letting the caller time out.
    /// </summary>
    public static async Task<Packet> DrainUntilOpcode(
        EncryptedTcpConnection conn,
        ushort targetOpcode,
        CancellationToken ct)
    {
        while (true)
        {
            var p = await conn.ReceiveAsync(ct);
            if (p is null)
                throw new InvalidOperationException(
                    $"connection closed before opcode 0x{targetOpcode:X4} arrived");

            if (p.Header.Opcode == targetOpcode)
                return p;

            if (p.Header.Opcode == OpcodeId.Known.GlobalError.Value)
            {
                var span = p.Payload.Span;
                int errCode = -1;
                if (span.Length >= 8)
                    errCode = BinaryPrimitives.ReadInt32BigEndian(span.Slice(4, 4)) - 7;
                throw new InvalidOperationException(
                    $"server rejected the request: {GlobalErrorMessage(errCode)} " +
                    $"(GlobalError code={errCode})");
            }
        }
    }

    /// <summary>
    /// A human reason for a server GlobalError code. Mirrors the server's
    /// own G_ERROR_* enum (server/src/UDP_Global.cpp) so the REPL surfaces
    /// "name too short" instead of a bare number; unknown codes pass through.
    /// </summary>
    internal static string GlobalErrorMessage(int code) => code switch
    {
        0  => "account banned",
        1  => "that character name is already taken",
        2  => "name contains invalid characters",
        3  => "name too short (minimum 3 characters)",
        4  => "name needs at least one vowel (a/e/i/o/u/y)",
        5  => "name has more than 3 repeating characters",
        6  => "character name is on the restricted list",
        7  => "login ticket invalid or expired",
        8  => "auth server unavailable",
        9  => "account inactive",
        10 => "ship name is on the restricted list",
        11 => "server internal error",
        12 => "server closed (stress-test gate)",
        13 => "account already in use (another session is logged in -- log it out first)",
        14 => "server is shutting down",
        _  => "unknown reason",
    };
}
