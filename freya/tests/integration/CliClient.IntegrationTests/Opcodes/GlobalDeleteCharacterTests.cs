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
/// 0x0071 GlobalDeleteCharacter → 0x0070 GlobalAvatarList round-trip on
/// the global server port (3805). This is the avatar-deletion path the
/// client invokes from the character-select screen. Wire-side it is the
/// twin of GlobalTicketRequest: the client sends a single be32 slot, the
/// proxy appends the LP-string username (from its own m_AccountName),
/// forwards as 0x200D DELETE_AVATAR over UDP 3810 to the server, and
/// hands the refreshed avatar list back as 0x0070.
///
/// <para>
/// This test is the failure detector for the PacketMethods.h::ExtractLong
/// wire-size bug fixed in <b>this commit</b>. The retail server reads
/// the slot index via <c>ExtractLong((unsigned char*)msg, index)</c> at
/// <c>server/src/UDP_Global.cpp:HandleAvatarDeleteRequest</c>. Before
/// the fix, <c>ExtractLong</c> cast the buffer pointer to <c>long*</c>
/// and dereferenced it — on Linux x86_64 that reads <b>8 bytes</b> for
/// what the wire format defines as a 4-byte field. The extra 4 bytes
/// came from the length prefix of the LP-string username that
/// <c>ExtractDataLS</c> is supposed to consume next, so on Linux every
/// delete request resolved to a wildly-wrong slot, failed
/// <c>GetAvatarID</c>'s [0,4] bounds check, and silently no-op'd the
/// delete. The fix dereferences as <c>int32_t*</c> so the read width
/// matches the wire width regardless of host <c>long</c> size — see
/// <c>proxy/PacketMethods.h</c> and <c>server/src/PacketMethods.h</c>
/// for the matching comments.
/// </para>
///
/// <para>
/// The exchange exercises the same bug class as Phase K's earlier
/// HandleGlobalTicketRequest fix (server/src/UDP_Global.cpp:200), but
/// surfaces through a different code path — <c>ExtractLong</c> is the
/// generic helper used by all UDP-plane handlers that parse fixed-size
/// integer fields, so a regression here would silently break every
/// future opcode that calls into it.
/// </para>
///
/// <para>
/// Wire layout of the 0x0071 payload (matches proxy's
/// <c>UDPClient_linux.cpp:DeleteCharacter</c> after the proxy appends
/// the username from its own session state):
/// <code>
///   client → proxy:  [be32 character_slot]
///   proxy  → server: [be32 character_slot][LP-string username]
/// </code>
/// </para>
///
/// <para>
/// The seeded test account <c>cli_test01</c> has no rows in the
/// <c>avatars</c> table — so any delete-on-empty-slot is a no-op at
/// the file-system level (<c>AccountManager::DeleteCharacter</c> calls
/// <c>DeleteFile</c> which silently ignores a missing path), and the
/// server still emits a refreshed (zeroed) avatar list. That's exactly
/// what makes this a useful failure detector: the test does not
/// require any DB mutation — it only requires the wire round-trip to
/// complete, which it can't if <c>ExtractLong</c> is broken.
/// </para>
/// </summary>
[Collection(ServerCollection.Name)]
public sealed class GlobalDeleteCharacterTests
{
    private readonly ServerFixture _server;
    private readonly ClientFixture _client;

    public GlobalDeleteCharacterTests(ServerFixture server)
    {
        _server = server;
        _client = new ClientFixture(server);
    }

    [Fact]
    public async Task EmptySlot_ReturnsRefreshedAvatarList()
    {
        var account = TestAccounts.New(_server);

        // 40s budget: TLS login + RC4+RSA handshake + GlobalConnect
        // round-trip (sub-second) + the proxy's DeleteCharacter UDP
        // round-trip to the server. Sub-second on the happy path; the
        // wide budget catches a regression where ExtractLong is
        // reverted and the proxy ends up in the WaitForResponse(~5s)
        // timeout fallback (which returns NULL → no 0x0070 forwarded,
        // and the test times out instead of failing with the
        // PacketMethods.h citation in the response_code assertion).
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(40));

        var login = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(account.Username, account.Password),
            cts.Token);
        Assert.True(login.Valid, $"login: {login.RawBody.TrimEnd()}");
        Assert.False(string.IsNullOrEmpty(login.Ticket));

        await using var conn = await EncryptedTcpConnection.ConnectAsync(
            _server.GlobalHost, _server.GlobalPort, cts.Token);

        // ---- Step 1: GlobalConnect (puts the proxy into ticket-consumed state) ----
        byte[] ticketBytes = Encoding.ASCII.GetBytes(login.Ticket!);
        byte[] connectPayload = new byte[4 + ticketBytes.Length + 1];
        BinaryPrimitives.WriteUInt32BigEndian(connectPayload.AsSpan(0, 4), (uint)ticketBytes.Length);
        ticketBytes.CopyTo(connectPayload, 4);
        connectPayload[^1] = 0;

        await conn.SendAsync(
            Packet.ForOpcode(OpcodeId.Known.GlobalConnect.Value, connectPayload),
            cts.Token);

        // Drain to the initial GlobalAvatarList so we know the proxy is
        // session-ready for any further global-channel work. Surface
        // GlobalError loudly if the server rejected us.
        await DrainUntilOpcode(conn, OpcodeId.Known.GlobalAvatarList.Value, cts.Token);

        // ---- Step 2: GlobalDeleteCharacter with slot=0 ----
        // Payload from client is a single be32 slot. The proxy appends
        // the LP-string username before sending 0x200D on UDP 3810. The
        // server reads the slot via ExtractLong — this is the call site
        // the PacketMethods.h fix unblocks on Linux.
        byte[] deletePayload = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(deletePayload, 0);  // slot 0

        await conn.SendAsync(
            Packet.ForOpcode(OpcodeId.Known.GlobalDeleteCharacter.Value, deletePayload),
            cts.Token);

        // ---- Step 3: drain until 0x0070 GlobalAvatarList (the refreshed reply) ----
        var reply = await DrainUntilOpcode(conn, OpcodeId.Known.GlobalAvatarList.Value, cts.Token);
        Assert.Equal(GlobalAvatarListCodec.WireSize, reply.Payload.Length);

        var decoded = (GlobalAvatarList)new GlobalAvatarListCodec()
            .DecodeInbound(reply.Payload.Span);

        // Five fixed slots, zero-filled because the seeded account has
        // no avatars. Same shape as the initial GlobalConnect reply.
        Assert.Equal(5, decoded.Avatars.Length);
        foreach (var slot in decoded.Avatars)
        {
            Assert.Equal(string.Empty, slot.Data.FirstName);
            Assert.Equal(string.Empty, slot.Data.LastName);
            Assert.Equal(0, slot.Info.AccountIdLsb);
        }

        // Galaxy table populated — proves the server reached
        // BuildAvatarList's tail (not an early ExtractLong-bounds-fail
        // exit). NumGalaxies==1, MaxPlayers>0, name non-empty.
        Assert.True(decoded.Galaxies.Length >= 1,
            "GlobalAvatarList delete-reply should carry at least one galaxy entry. " +
            "An empty galaxy table means the server bailed out before BuildAvatarList " +
            "ran its tail — typically because ExtractLong read garbage for character_slot " +
            "and GetAvatarID rejected the [0,4] bounds check, leaving SendAvatarList " +
            "to walk a half-initialised GlobalAvatarList.");
        var galaxy = decoded.Galaxies[0];
        Assert.False(string.IsNullOrEmpty(galaxy.Name));
        Assert.Equal(1, galaxy.GalaxyId);
        Assert.True(galaxy.MaxPlayers > 0);
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
