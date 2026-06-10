// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Buffers.Binary;
using System.Text;
using N7.CliClient.Auth;
using N7.CliClient.Net;
using N7.CliClient.Opcodes;
using N7.CliClient.Opcodes.Inbound;
using Xunit;

namespace N7.CliClient.IntegrationTests.Opcodes;

/// <summary>
/// 0x006E GlobalTicketRequest → 0x006F GlobalTicket round-trip on the
/// global server port (3805). This is the avatar-selection step that
/// follows GlobalConnect: after the client renders the five avatar
/// slots from the GlobalAvatarList reply, it sends a
/// GlobalTicketRequest carrying the chosen slot. The server resolves
/// the slot to a real avatar_id via AccountManager::GetAvatarID, sets
/// up the Player object, and replies with a GlobalTicket the client
/// hands to the master server on the next hop (MasterJoin).
///
/// <para>
/// This test exercises the <i>happy</i> path. The retail server's
/// GetAvatarID derives avatar_id from (account_id, slot) by formula
/// (<c>account_id * 5 + slot + 1</c>, see
/// <c>login-server/Net7SSL/AccountManager.h:30</c>) and does NOT cross-
/// check whether the avatar row exists in the DB — so for any valid
/// (account, slot in [0,4]) pair the server returns a fabricated
/// avatar_id, sets up a Player, and ACKs back to the proxy with a
/// 0x2005 AVATARLOGIN_CONFIRM. The proxy then synthesises a 0x006F
/// GlobalTicket with response_code=0 and avatar_id=GameID
/// (CharacterID | PLAYER_TAG where PLAYER_TAG = 1&lt;&lt;30 = 0x40000000).
/// </para>
///
/// <para>
/// This test is the failure detector for two Phase-K bugs that would
/// otherwise prevent 0x006E from working on the Linux build at all:
/// </para>
/// <list type="bullet">
///   <item>
///     <b>Server-side wire-size bug (request)</b>:
///     <c>server/src/UDP_Global.cpp:HandleGlobalTicketRequest</c> used
///     to read <c>char_slot</c> as a Linux <c>long</c> (8 bytes), but
///     the proxy writes only 4 bytes (the slot index, BE). The high 4
///     bytes pulled in the length-prefix of the username string,
///     yielding wildly-wrong slot numbers that failed GetAvatarID's
///     [0,4] bounds check. Fixed at <c>UDP_Global.cpp:200</c>.
///   </item>
///   <item>
///     <b>Server-side wire-size bug (reply)</b>: SendOpcode was
///     dispatching the AVATARLOGIN_CONFIRM payload as
///     <c>sizeof(long)=8</c> bytes when the proxy reads only 4 — so
///     even if GetAvatarID had succeeded, the proxy would have failed
///     to parse the confirm correctly and the failure-path ticket
///     would have surfaced anyway. Fixed at <c>UDP_Global.cpp:237</c>.
///   </item>
/// </list>
/// <para>
/// If either Phase K bug regresses, this test fails: the proxy's
/// SendAvatarLogin WaitForResponse loop times out (~5s), the proxy
/// synthesises a 0x006F with response_code=1002 (galaxy full),
/// avatar_id=0x40000000 sentinel, and the response_code assertion
/// below catches it. We assert the success path explicitly so a
/// regression is unambiguous rather than just "test slower than
/// usual".
/// </para>
///
/// <para>
/// Wire layout of the 0x006E payload (matches proxy's
/// <c>UDPClient_linux.cpp:SendAvatarLogin</c>):
/// <code>
///   [be32 char_slot][LP-string m_AccountName]
/// </code>
/// The client only sends <c>be32 char_slot</c>; the proxy stamps the
/// account name from its own m_AccountName before forwarding over UDP
/// 3810 — the trailing LP-string is added inside the proxy, not by
/// the client.
/// </para>
///
/// <para>
/// Wire layout of the 0x006F reply (decoded by
/// <see cref="GlobalTicketCodec"/>):
/// <code>
///   offset  size  field         encoding
///   0       4     response_code be32  (0 on success)
///   20      4     avatar_id     be32  (GameID = CharacterID | PLAYER_TAG)
///   24      4     sector_id     be32  (0 here — m_Player_Avatar_List slot 0 is zeroed)
///   32      4     level         host  (admin_level — 0 for seeded test account)
///   48..63  16    ticket string "MY_Avatar_Ticket\0..."
/// </code>
/// </para>
/// </summary>
[Collection(ServerCollection.Name)]
public sealed class GlobalTicketRequestTests
{
    private readonly ServerFixture _server;
    private readonly ClientFixture _client;

    public GlobalTicketRequestTests(ServerFixture server)
    {
        _server = server;
        _client = new ClientFixture(server);
    }

    [Fact]
    public async Task ValidSlot_ReturnsSuccessTicketWithGameId()
    {
        var account = TestAccounts.New(_server);

        // 40s budget: TLS login + RC4+RSA handshake + GlobalConnect
        // round-trip (sub-second) + the proxy's SendAvatarLogin UDP
        // round-trip to the server. On the happy path this lands
        // sub-second; the wide budget catches a regression where one
        // of the Phase K wire-size fixes is undone and the proxy ends
        // up in the WaitForResponse (~5s) timeout fallback.
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(40));

        var login = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(account.Username, account.Password),
            cts.Token);
        Assert.True(login.Valid, $"login: {login.RawBody.TrimEnd()}");
        Assert.False(string.IsNullOrEmpty(login.Ticket));

        await using var conn = await EncryptedTcpConnection.ConnectAsync(
            _server.GlobalHost, _server.GlobalPort, cts.Token);

        // ---- Step 1: GlobalConnect ----
        // The proxy needs the ticket-consumed state set before it will
        // accept a TicketRequest (m_AccountUsername is populated by
        // ProcessTicketInfo on the UDP-2002 round-trip); replaying the
        // GlobalConnect from GlobalConnectTests is the only way to put
        // the connection into that state.
        byte[] ticketBytes = Encoding.ASCII.GetBytes(login.Ticket!);
        byte[] connectPayload = new byte[4 + ticketBytes.Length + 1];
        BinaryPrimitives.WriteUInt32BigEndian(connectPayload.AsSpan(0, 4), (uint)ticketBytes.Length);
        ticketBytes.CopyTo(connectPayload, 4);
        connectPayload[^1] = 0;

        await conn.SendAsync(
            Packet.ForOpcode(OpcodeId.Known.GlobalConnect.Value, connectPayload),
            cts.Token);

        // Drain until we see the AvatarList, the prerequisite for any
        // further global-channel traffic. If the server rejects the
        // ticket we surface the GlobalError code rather than time out.
        await DrainUntilOpcode(conn, OpcodeId.Known.GlobalAvatarList.Value, cts.Token);

        // ---- Step 2: GlobalTicketRequest with slot=0 ----
        // Payload is a single be32 slot number. The proxy will append
        // the LP-string username from its own m_AccountName before
        // sending the 0x2004 AVATARLOGIN to the server over UDP 3810.
        byte[] ticketRequestPayload = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(ticketRequestPayload, 0);  // slot 0

        await conn.SendAsync(
            Packet.ForOpcode(OpcodeId.Known.GlobalTicketRequest.Value, ticketRequestPayload),
            cts.Token);

        // ---- Step 3: drain until 0x006F GlobalTicket ----
        var reply = await DrainUntilOpcode(conn, OpcodeId.Known.GlobalTicket.Value, cts.Token);
        Assert.Equal(GlobalTicketCodec.WireSize, reply.Payload.Length);

        var decoded = (GlobalTicket)new GlobalTicketCodec()
            .DecodeInbound(reply.Payload.Span);

        // Happy path: proxy's ProcessGlobalTicket calls
        // SendGlobalTicket(player_id, sector_id, admin_level, issue=true);
        // issue=true stamps response_code=0 (see SendGlobalTicket in
        // proxy/ClientToServer_linux_stubs.cpp:284-287). A non-zero
        // response_code here means we landed on the failure path —
        // typically 1002 (galaxy full) if the proxy's WaitForResponse
        // timed out, which would point at a regression of the Phase K
        // wire-size fix in server/src/UDP_Global.cpp:200 (char_slot
        // read) or :237 (avatar_id_wire send).
        Assert.True(decoded.ResponseCode == 0,
            $"GlobalTicket response_code={decoded.ResponseCode}; expected 0 (success). " +
            $"1002 means the proxy hit the WaitForResponse(~5s) timeout fallback — " +
            $"check the wire-size fixes in server/src/UDP_Global.cpp:200,237 " +
            $"and proxy/UDPClient_linux.cpp::SendAvatarLogin avatar_id read.");

        // avatar_id field carries the player's GameID =
        // (server-computed avatar_id) | PLAYER_TAG. PLAYER_TAG is
        // (1<<30) = 0x40000000 (see
        // login-server/Net7SSL/ClientToGlobalServer.cpp:11). Asserting
        // the bit is set verifies the proxy didn't fall back to the
        // failure sentinel (the failure path emits avatar_id = exactly
        // 0x40000000 with no avatar_id ORed in).
        const int PlayerTag = 1 << 30;
        Assert.True((decoded.AvatarId & PlayerTag) != 0,
            $"avatar_id=0x{decoded.AvatarId:X8} missing PLAYER_TAG bit (1<<30); " +
            $"means the proxy returned the failure sentinel, not a real GameID.");
        Assert.NotEqual(GlobalTicketCodec.FailureAvatarIdSentinel, decoded.AvatarId);

        // Avatar_id derivation: GameID = (account_id * 5 + slot + 1) |
        // PLAYER_TAG. For account 9_000_001, slot 0 → CharacterID =
        // 45_000_006 → GameID = 45_000_006 | 0x40000000 = 0x42AE5906.
        int expectedCharacterId = (int)((long)account.Id * 5 + 0 + 1);
        int expectedGameId = expectedCharacterId | PlayerTag;
        Assert.Equal(expectedGameId, decoded.AvatarId);

        // sector_id is read by ProcessGlobalTicket from
        // m_Player_Avatar_List.avatar[0].info.sector_id. For the seeded
        // account with no avatars, BuildAvatarList leaves this zeroed.
        Assert.Equal(0, decoded.SectorId);

        // Ticket string is hard-coded to "MY_Avatar_Ticket" in the
        // proxy's SendGlobalTicket; absence means the codec walked off
        // into a NUL field or the struct layout drifted.
        Assert.Equal("MY_Avatar_Ticket", decoded.TicketString);
    }

    /// <summary>
    /// Drain the global channel until we see <paramref name="targetOpcode"/>.
    /// Surfaces GlobalError (0x0075) loudly so an unexpected server-side
    /// rejection produces a meaningful failure instead of a timeout.
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

            if (p.Header.Opcode == 0x0075)
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
