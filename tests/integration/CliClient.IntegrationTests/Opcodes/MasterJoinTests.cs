// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Net;
using N7.CliClient.Net;
using N7.CliClient.Opcodes;
using N7.CliClient.Opcodes.Inbound;
using N7.CliClient.Opcodes.Outbound;
using Xunit;

namespace N7.CliClient.IntegrationTests.Opcodes;

/// <summary>
/// 0x0035 MasterJoin → 0x0036 ServerRedirect round-trip against the
/// live proxy on the master server port (3801). The proxy's Linux
/// HandleMasterJoin (proxy/ClientToMasterServer.cpp:93-146) tries to
/// hand the join off to the UDP plane (SendMasterLogin → UDP 3808 →
/// wait for 0x2009 confirm), and on timeout (~5s) falls back to a
/// hardcoded ServerRedirect at PROXY_LOCAL_TCP_PORT (3500) so the
/// client's state machine keeps moving. In this test environment the
/// server isn't running the matching MVAS UDP responder, so we always
/// land in the timeout-fallback path; we still get a ServerRedirect,
/// just ~5s later than the happy path would deliver it.
///
/// <para>
/// This test is therefore a "the wire round-trips, the codec encodes
/// MasterJoin correctly, the proxy parses it without crashing, the
/// ServerRedirect comes back well-formed" assertion — not a full
/// "the join succeeded and routed us to the right sector" assertion.
/// The latter needs Phase K's UDP plane fully wired plus a real
/// game-server process answering on UDP 3808 (server stack is
/// blocked there today).
/// </para>
/// </summary>
[Collection(ServerCollection.Name)]
public sealed class MasterJoinTests
{
    private readonly ServerFixture _server;
    private readonly ClientFixture _client;

    public MasterJoinTests(ServerFixture server)
    {
        _server = server;
        _client = new ClientFixture(server);
    }

    [Fact]
    public async Task ValidMasterJoin_ReceivesServerRedirect()
    {
        var account = TestAccounts.New(_server);

        // 30s budget: TLS login + RSA handshake + ~5s UDP timeout in
        // the proxy's HandleMasterJoin fallback path + slack. The
        // happy-path (UDP responder present) would finish well under
        // 5s, but in this env the fallback is the realistic timing.
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        // Log in to get a real ticket — the proxy's MasterJoin handler
        // accepts whatever ticket bytes the client sends, but using a
        // genuine one keeps the test honest if Phase K later validates
        // the ticket on the proxy side.
        var login = await _client.AuthLogin.LoginAsync(
            new global::N7.CliClient.Auth.AuthLoginRequest(account.Username, account.Password),
            cts.Token);
        Assert.True(login.Valid, $"login: {login.RawBody.TrimEnd()}");

        await using var conn = await EncryptedTcpConnection.ConnectAsync(
            _server.MasterHost, _server.MasterPort, cts.Token);

        // Ticket-field width mismatch is deliberate and worth knowing about:
        // /AuthLogin returns a 40-char ASCII hex ticket (20 binary bytes
        // serialised as hex), but the MasterJoin packet reserves only 20
        // bytes for the ticket field. Retail captures pass 20 raw binary
        // bytes there. We don't currently have the binary-decode helper
        // wired up in CliClient.Core, and the Linux proxy's HandleMasterJoin
        // doesn't validate the ticket byte-for-byte today (it only reads
        // avatar_id_lsb + ToSectorID — see proxy/ClientToMasterServer.cpp:93-100),
        // so truncating the ASCII hex to its first 20 bytes is a sound
        // placeholder. Phase K's UDP plane completion will revisit this:
        // when SendTicket actually validates the ticket against the
        // login-server's session table, we'll need to ship the hex
        // *decoded* to 20 binary bytes, not truncated as ASCII.
        var ticketBytes = new byte[MasterJoinCodec.TicketLength];
        System.Text.Encoding.ASCII.GetBytes(
            login.Ticket!.AsSpan(0, Math.Min(login.Ticket.Length, MasterJoinCodec.TicketLength)),
            ticketBytes);

        var join = new MasterJoinRequest(
            Unknown1: 0,
            Unknown2: 0,
            Unknown3: 0,
            AvatarIdMsb: 0,
            AvatarIdLsb: account.Id,
            ToSectorId: 1,
            FromSectorId: 0,
            PlayerLevel: 1,
            Unknown8: 0,
            Unknown9: 0,
            Unknown10: 0,
            Ticket: ticketBytes);

        var codec = new MasterJoinCodec();
        var packet = Packet.ForOpcode(
            OpcodeId.Known.MasterJoin.Value,
            codec.EncodeOutbound(join));

        await conn.SendAsync(packet, cts.Token);

        // Drain until we see a ServerRedirect. Master server might emit
        // intermediate frames during the UDP attempt; we react to opcode.
        ServerRedirect? redirect = null;
        while (redirect is null)
        {
            var reply = await conn.ReceiveAsync(cts.Token);
            Assert.NotNull(reply);
            if (reply!.Header.Opcode == OpcodeId.Known.ServerRedirect.Value)
            {
                redirect = (ServerRedirect) new ServerRedirectCodec()
                    .DecodeInbound(reply.Payload.Span);
            }
        }

        // Sector ID echoed back from the join request.
        Assert.Equal(1, redirect.SectorId);

        // Fallback path always redirects to PROXY_LOCAL_TCP_PORT (3500).
        // Don't pin the IP — the proxy uses its own m_IpAddress which
        // is the docker-bridge address (172.x.x.x), not 127.0.0.1.
        Assert.Equal(_server.SectorPort, redirect.ServerEndPoint.Port);
        Assert.True(
            !redirect.ServerEndPoint.Address.Equals(IPAddress.Any),
            $"redirect IP must be a real address, got {redirect.ServerEndPoint.Address}");
    }
}
