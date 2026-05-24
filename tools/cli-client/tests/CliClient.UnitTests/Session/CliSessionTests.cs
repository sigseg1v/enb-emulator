// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// New code; project default license (LICENSES/enb-emulator).

using System.Net;
using System.Net.Sockets;
using N7.CliClient.Net;
using N7.CliClient.Opcodes;
using N7.CliClient.Opcodes.Inbound;
using N7.CliClient.Session;
using Xunit;

namespace N7.CliClient.UnitTests.Session;

public sealed class CliSessionTests
{
    private static OpcodeRegistry MakeRegistry()
    {
        var r = new OpcodeRegistry();
        r.Register(new ServerRedirectCodec());
        return r;
    }

    [Fact]
    public void Constructor_RejectsNullRegistry()
    {
        Assert.Throws<ArgumentNullException>(() => new CliSession(null!, "ticket"));
    }

    [Fact]
    public void Constructor_RejectsEmptyTicket()
    {
        Assert.Throws<ArgumentException>(() => new CliSession(MakeRegistry(), ""));
    }

    [Fact]
    public void NewSession_IsAuthenticated()
    {
        var session = new CliSession(MakeRegistry(), "ABCDEF1234567890");
        Assert.Equal(SessionStage.Authenticated, session.Stage);
        Assert.Null(session.CurrentEndpoint);
    }

    [Fact]
    public async Task SendOrReceive_BeforeConnect_Throws()
    {
        await using var session = new CliSession(MakeRegistry(), "T");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => session.SendAsync(Packet.ForOpcode(0, ReadOnlyMemory<byte>.Empty)));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => session.ReceiveAsync());
    }

    [Fact]
    public async Task FollowRedirect_RejectsBackwardsStage()
    {
        await using var session = new CliSession(MakeRegistry(), "T");
        var redirect = new ServerRedirect(SectorId: 1, ServerEndPoint: new IPEndPoint(IPAddress.Loopback, 3500));

        // Authenticated → Authenticated is not a forward transition.
        await Assert.ThrowsAsync<ArgumentException>(
            () => session.FollowRedirectAsync(redirect, SessionStage.Authenticated));
    }

    [Fact]
    public async Task ConnectGlobal_FromWrongStage_Throws()
    {
        await using var session = new CliSession(MakeRegistry(), "T");
        await session.DisposeAsync(); // forces stage to Disconnected

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => session.ConnectGlobalAsync("127.0.0.1", 3500));
    }

    /// <summary>
    /// End-to-end: spin up a fake "global" listener that does the
    /// server-side handshake, dispatch the client there via
    /// <see cref="CliSession.ConnectGlobalAsync"/>, send a packet from
    /// the client, verify the server decrypts the opcode correctly.
    /// This is the only test that exercises CliSession against a real
    /// socket (the rest is plumbing validation).
    /// </summary>
    [Fact]
    public async Task ConnectGlobal_HandshakesAndCanSend()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var serverTask = Task.Run(async () =>
        {
            using var serverTcp = await listener.AcceptTcpClientAsync(cts.Token);
            using var stream = serverTcp.GetStream();

            // Server-side: send 74-byte pubkey, read 68-byte client key
            // packet, derive RC4 key, then read one encrypted packet.
            byte[] pubkey = new byte[RsaHandshake.ServerPubkeyPacketSize];
            await stream.WriteAsync(pubkey, cts.Token);
            await stream.FlushAsync(cts.Token);

            byte[] lenAndBlock = new byte[RsaHandshake.ClientKeyPacketSize];
            int off = 0;
            while (off < lenAndBlock.Length)
            {
                int n = await stream.ReadAsync(lenAndBlock.AsMemory(off), cts.Token);
                if (n == 0) throw new EndOfStreamException();
                off += n;
            }
            byte[] decrypted = new byte[WestwoodRSA.BlockSize];
            WestwoodRSA.DecryptBlock(lenAndBlock.AsSpan(4, WestwoodRSA.BlockSize), decrypted);
            byte[] sessionKey = RsaHandshake.ExtractSessionKeyFromDecryptedBlock(decrypted);

            var rc4 = new WestwoodRC4();
            rc4.PrepareKey(sessionKey);

            byte[] hdr = new byte[PacketHeader.WireSize];
            off = 0;
            while (off < hdr.Length)
            {
                int n = await stream.ReadAsync(hdr.AsMemory(off), cts.Token);
                if (n == 0) throw new EndOfStreamException();
                off += n;
            }
            rc4.Transform(hdr);
            var header = PacketHeader.Read(hdr);

            byte[] payload = new byte[header.PayloadLength];
            off = 0;
            while (off < payload.Length)
            {
                int n = await stream.ReadAsync(payload.AsMemory(off), cts.Token);
                if (n == 0) throw new EndOfStreamException();
                off += n;
            }
            rc4.Transform(payload);
            return new { Opcode = header.Opcode, Payload = payload };
        });

        await using var session = new CliSession(MakeRegistry(), "TICKET1234567890ABCD");
        await session.ConnectGlobalAsync("127.0.0.1", port, cts.Token);

        Assert.Equal(SessionStage.Global, session.Stage);
        Assert.NotNull(session.CurrentEndpoint);
        Assert.Equal(port, session.CurrentEndpoint!.Value.Port);

        byte[] body = new byte[] { 1, 2, 3, 4 };
        await session.SendAsync(Packet.ForOpcode(0x0035, body), cts.Token);

        var serverGot = await serverTask;
        Assert.Equal(0x0035, serverGot.Opcode);
        Assert.Equal(body, serverGot.Payload);

        listener.Stop();
    }
}
