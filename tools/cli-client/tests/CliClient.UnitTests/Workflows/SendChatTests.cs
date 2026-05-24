// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// New code; project default license (LICENSES/enb-emulator).

using System.Net;
using System.Net.Sockets;
using N7.CliClient.Net;
using N7.CliClient.Opcodes;
using N7.CliClient.Opcodes.Inbound;
using N7.CliClient.Opcodes.Outbound;
using N7.CliClient.Session;
using N7.CliClient.Workflows;
using Xunit;

namespace N7.CliClient.UnitTests.Workflows;

public sealed class SendChatTests
{
    private static OpcodeRegistry MakeRegistry()
    {
        var r = new OpcodeRegistry();
        r.Register(new ServerRedirectCodec());
        return r;
    }

    [Fact]
    public void Constructor_RejectsNullSession()
    {
        Assert.Throws<ArgumentNullException>(() => new SendChat(null!));
    }

    [Fact]
    public async Task Send_EmptyMessage_Throws()
    {
        await using var session = new CliSession(MakeRegistry(), "T1234567890");
        var workflow = new SendChat(session);

        await Assert.ThrowsAsync<ArgumentException>(
            () => workflow.SendAsync(1, ChatChannel.Local, ""));
    }

    [Fact]
    public async Task Send_BeforeConnect_PropagatesSessionError()
    {
        // The CliSession is Authenticated but not connected to any TCP
        // endpoint. SendChat should surface that as InvalidOperation
        // rather than silently swallow.
        await using var session = new CliSession(MakeRegistry(), "T1234567890");
        var workflow = new SendChat(session);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => workflow.SendAsync(1, ChatChannel.Local, "hello"));
    }

    /// <summary>
    /// End-to-end: stand up a loopback server that completes the RSA + RC4
    /// handshake, push a ClientChat through SendChat, verify the bytes
    /// that arrive on the server side match the codec's wire layout.
    /// </summary>
    [Fact]
    public async Task Send_DeliversCodec_OutputOverTheWire()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var serverTask = Task.Run(async () =>
        {
            using var serverTcp = await listener.AcceptTcpClientAsync(cts.Token);
            using var stream = serverTcp.GetStream();

            // RSA handshake: emit zero pubkey, read encrypted block.
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
            return (Opcode: (ushort)header.Opcode, Payload: payload);
        });

        await using var session = new CliSession(MakeRegistry(), "TICKET1234567890ABCD");
        await session.ConnectGlobalAsync("127.0.0.1", port, cts.Token);

        var workflow = new SendChat(session);
        await workflow.SendAsync(
            gameId: 0x11223344,
            channel: ChatChannel.Broadcast,
            message: "hi",
            ct: cts.Token);

        var got = await serverTask;
        Assert.Equal(0x0033, got.Opcode);

        // Expected wire bytes mirror ClientChatCodecTests.Encode_LayoutMatches_*:
        //   [0..3]  GameID LE = 44 33 22 11
        //   [4]     Type      = 4 (Broadcast)
        //   [5..6]  Size LE   = 03 00
        //   [7..9]  "hi\0"
        byte[] expected = { 0x44, 0x33, 0x22, 0x11, 0x04, 0x03, 0x00, (byte)'h', (byte)'i', 0x00 };
        Assert.Equal(expected, got.Payload);

        listener.Stop();
    }
}
