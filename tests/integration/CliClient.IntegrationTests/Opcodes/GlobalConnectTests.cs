// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Buffers.Binary;
using System.Text;
using N7.CliClient.Auth;
using N7.CliClient.Net;
using N7.CliClient.Opcodes;
using Xunit;

namespace N7.CliClient.IntegrationTests.Opcodes;

/// <summary>
/// 0x006D GlobalConnect → 0x0070 GlobalAvatarList round-trip against the
/// live proxy on the global server port (3805). This is the integration
/// assertion for Phase K's proxy↔server global UDP control plane
/// (UDP_GLOBAL_SERVER_PORT 3810): the round-trip succeeds iff the proxy
/// can hand a ticket to the server over UDP 3810 (SendTicket → 0x2002
/// TICKET), the server's HandleGlobalOpcode dispatcher
/// (server/src/UDP_Global.cpp:43-76) routes it through ProcessTicketInfo
/// → SendAvatarList, the server's 0x2003 AVATARLIST reply lands back at
/// the proxy's UDPClient receiver, and the proxy serialises a 0x0070
/// frame back to us.
///
/// <para>
/// Before Phase K this plane didn't exist on the Linux build at all —
/// the kyp-era TCP cluster (Connection/ConnectionManager) was deleted
/// in Phase Q and nothing replaced the proxy↔server global handoff
/// until Phase K resurrected it as a dedicated UDP plane. This test
/// is the failure detector for that resurrection.
/// </para>
///
/// <para>
/// Wire layout for 0x006D's payload (matches Win32
/// <c>ClientToGlobalServer.cpp:124</c> and the Linux port at
/// <c>proxy/ClientToServer_linux_stubs.cpp:126-156</c>):
/// <code>
///   [u32 ticket_len_be][char ticket[ticket_len]]
/// </code>
/// The proxy's handler reads the length but only uses
/// <c>&amp;m_RecvBuffer[4]</c> as a NUL-terminated string for
/// <c>strlen(ticket)</c> downstream, so the length is essentially
/// advisory; we still set it correctly for protocol fidelity.
/// </para>
///
/// <para>
/// The reply we wait for is 0x0070, payload of size
/// <c>sizeof(GlobalAvatarList)</c>. We don't decode the body —
/// CliClient.Core has no GlobalAvatarList codec yet (that comes when
/// the workflow that uses it is wired up). Receiving the opcode at all
/// proves the UDP plane round-trip happened.
/// </para>
/// </summary>
[Collection(ServerCollection.Name)]
public sealed class GlobalConnectTests
{
    private readonly ServerFixture _server;
    private readonly ClientFixture _client;

    public GlobalConnectTests(ServerFixture server)
    {
        _server = server;
        _client = new ClientFixture(server);
    }

    [Fact]
    public async Task ValidTicket_RoundTripsThroughUdpGlobalPlane_ReturnsAvatarList()
    {
        var account = TestAccounts.Pool[0];

        // 30s budget: TLS login + RSA handshake + the proxy's UDP round-trip
        // to the server (sub-second in the happy path; the proxy's
        // WaitForResponse has a multi-second timeout if something is wrong).
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        // Get a real ticket via /AuthLogin (Phase J SSL listener + Phase N+
        // libpqxx VerifyAccountInfo path). The server's ProcessTicketInfo
        // calls strtok on the ticket to extract the username prefix, so the
        // ticket must be the real "username-XXXXX" string the auth server
        // issued — not a fake.
        var login = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(account.Username, account.Password),
            cts.Token);
        Assert.True(login.Valid, $"login: {login.RawBody.TrimEnd()}");
        Assert.False(string.IsNullOrEmpty(login.Ticket));

        await using var conn = await EncryptedTcpConnection.ConnectAsync(
            _server.GlobalHost, _server.GlobalPort, cts.Token);

        // Build the GlobalConnect payload: [u32 len_be][ticket bytes][NUL].
        // The NUL is what the proxy's strlen() at SendTicket walks to.
        byte[] ticketBytes = Encoding.ASCII.GetBytes(login.Ticket!);
        byte[] payload = new byte[4 + ticketBytes.Length + 1];
        BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(0, 4), (uint)ticketBytes.Length);
        ticketBytes.CopyTo(payload, 4);
        payload[^1] = 0;

        var packet = Packet.ForOpcode(
            OpcodeId.Known.GlobalConnect.Value,
            payload);

        await conn.SendAsync(packet, cts.Token);

        // Drain until we see a GlobalAvatarList. The proxy may forward a
        // 0x0075 GLOBAL_ERROR instead if the server rejected the ticket
        // (banned account, account-in-use collision); for the seeded pool
        // the happy path is the AvatarList.
        Packet? reply = null;
        while (true)
        {
            var p = await conn.ReceiveAsync(cts.Token);
            Assert.NotNull(p);
            if (p!.Header.Opcode == OpcodeId.Known.GlobalAvatarList.Value)
            {
                reply = p;
                break;
            }

            // 0x0075 GlobalError surfaces if the server rejected us — let
            // the test fail loudly with the error code rather than time
            // out silently. Layout: [u32 msg_len][be32 (err+7)][msg bytes].
            if (p.Header.Opcode == 0x0075)
            {
                var span = p.Payload.Span;
                int errCode = -1;
                if (span.Length >= 8)
                    errCode = BinaryPrimitives.ReadInt32BigEndian(span.Slice(4, 4)) - 7;
                throw new Xunit.Sdk.XunitException(
                    $"server returned GlobalError code={errCode}; expected AvatarList");
            }

            // Any other opcode arriving on the global channel before
            // AvatarList is unexpected for this exchange. Loop and keep
            // draining — the proxy doesn't push spontaneous global
            // packets on the happy path, but a future Phase-K hello may.
        }

        // The reply is a complete GlobalAvatarList struct. We don't decode
        // it here (no codec wired up yet); receiving the opcode at all
        // means the UDP global plane round-trip worked end-to-end:
        //   client → proxy(TCP 3805)
        //   proxy → server(UDP 3810, opcode 0x2002 TICKET)
        //   server → proxy(UDP 3810, opcode 0x2003 AVATARLIST)
        //   proxy → client(TCP 3805, opcode 0x0070 GlobalAvatarList)
        Assert.NotNull(reply);
        Assert.True(reply!.Payload.Length > 0,
            "GlobalAvatarList payload must be non-empty (struct GlobalAvatarList " +
            "is fixed-size and is always memcpy'd whole — empty means the proxy " +
            "shipped a bogus frame).");
    }
}
