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

        public async ValueTask DisposeAsync()
        {
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
    public static async Task<Session> EstablishAsync(
        ServerFixture server,
        string authTicket,
        string accountUsername,
        int slot,
        int sectorId,
        string firstName,
        string shipName,
        CancellationToken ct)
    {
        var globalConn = await EncryptedTcpConnection.ConnectAsync(
            server.GlobalHost, server.GlobalPort, ct);

        try
        {
            await SendGlobalConnectAsync(globalConn, authTicket, ct);
            await DrainUntilOpcode(globalConn, OpcodeId.Known.GlobalAvatarList.Value, ct);

            await CreateCharacterOnSlotAsync(
                globalConn, accountUsername, slot, firstName, shipName, ct);

            int gameId = await RequestTicketAsync(globalConn, slot, ct);

            const int PlayerTag = 1 << 30;
            Assert.True((gameId & PlayerTag) != 0,
                $"GameID 0x{gameId:X8} missing PLAYER_TAG — GlobalTicketRequest hit the failure path.");

            var redirect = await DoMasterJoinAsync(server, authTicket, gameId, sectorId, ct);
            Assert.Equal(sectorId, redirect.SectorId);
            Assert.Equal(server.SectorPort, redirect.ServerEndPoint.Port);

            var (sectorConn, startId, handshakeOpcodes) = await DoSectorLoginUntilStartAsync(
                server, authTicket, gameId, sectorId, ct);

            return new Session
            {
                Global = globalConn,
                Sector = sectorConn,
                GameId = gameId,
                StartId = startId,
                Slot = slot,
                HandshakeOpcodes = handshakeOpcodes,
            };
        }
        catch
        {
            await globalConn.DisposeAsync();
            throw;
        }
    }

    /// <summary>
    /// Best-effort post-test cleanup: send 0x0071 GlobalDeleteCharacter
    /// on <paramref name="global"/> for <paramref name="slot"/> and wait
    /// for the refreshed avatar list. Wrap in try/catch at the call site
    /// — primary test failure has already been reported.
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
    /// first 4 bytes of the START payload), and the list of opcodes
    /// seen during the drain (terminating 0x0005 included) so
    /// passive-observation tests can assert on handshake fan-out
    /// emits like 0x0037 CLIENT_AVATAR, 0x0047 CLIENT_SHIP, and
    /// 0x0061 AVATAR_DESCRIPTION that the server pushes from
    /// SendLoginShipData before SendStart.
    /// </summary>
    public static async Task<(EncryptedTcpConnection conn, int startId, IReadOnlyList<ushort> opcodes)>
        DoSectorLoginUntilStartAsync(
            ServerFixture server, string authTicket, int gameId, int sectorId,
            CancellationToken ct)
    {
        var conn = await EncryptedTcpConnection.ConnectAsync(
            server.SectorHost, server.SectorPort, ct);

        try
        {
            await conn.SendAsync(BuildLoginPacket(authTicket, gameId, sectorId), ct);

            var opcodes = new List<ushort>();
            int framesSeen = 0;
            const int maxFrames = 4000;
            while (framesSeen++ < maxFrames)
            {
                var reply = await conn.ReceiveAsync(ct);
                Assert.NotNull(reply);

                opcodes.Add(reply!.Header.Opcode);

                if (reply.Header.Opcode == OpcodeId.Known.Start.Value)
                {
                    int startId = reply.Payload.Length >= 4
                        ? BinaryPrimitives.ReadInt32LittleEndian(reply.Payload.Span[..4])
                        : 0;
                    return (conn, startId, opcodes);
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
    /// Build the 137-byte Login payload — a packed Login struct
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
