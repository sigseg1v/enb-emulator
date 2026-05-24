// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// New code; project default license (LICENSES/enb-emulator).

using System.Net;
using System.Net.Sockets;
using N7.CliClient.Net;
using Xunit;

namespace N7.CliClient.UnitTests.Net;

/// <summary>
/// Live-socket tests: spin up a TcpListener on loopback, run the
/// real <see cref="EncryptedTcpConnection"/> client-side handshake
/// against a hand-rolled server side that does the inverse RSA dance
/// (decrypt the client's RC4 key blob and key its own RC4 mirror).
/// This validates the full RSA + framed-RC4 pipeline against
/// itself — same shape as a Phase T integration test but with a
/// trivial fake server instead of the real Net-7.
/// </summary>
public sealed class EncryptedTcpConnectionTests
{
    /// <summary>
    /// Drives the SERVER side of the handshake — mirror of
    /// proxy/Connection.cpp::DoKeyExchange. Sends a 74-byte "fake
    /// pubkey" packet (the client ignores its contents), reads the
    /// 4-byte BE length + 64-byte encrypted block, RSA-decrypts it,
    /// pulls the 8-byte RC4 session key out of the reversed positions,
    /// keys two RC4 ciphers (in + out), and returns them.
    /// </summary>
    private static async Task<(WestwoodRC4 inboundFromClient, WestwoodRC4 outboundToClient)>
        ServerHandshakeAsync(NetworkStream serverStream, CancellationToken ct)
    {
        // Send 74 bytes of arbitrary pubkey-shaped data — the client
        // skips parsing it.
        byte[] fakePubkey = new byte[RsaHandshake.ServerPubkeyPacketSize];
        for (int i = 0; i < fakePubkey.Length; i++)
            fakePubkey[i] = (byte)(i + 1);
        await serverStream.WriteAsync(fakePubkey, ct);
        await serverStream.FlushAsync(ct);

        // Read the 4-byte BE length.
        byte[] lengthBytes = new byte[4];
        await ReadExactlyAsync(serverStream, lengthBytes, ct);
        uint length = (uint)((lengthBytes[0] << 24) | (lengthBytes[1] << 16)
                             | (lengthBytes[2] << 8) | lengthBytes[3]);
        Assert.Equal((uint)WestwoodRSA.BlockSize, length);

        // Read the 64-byte encrypted block.
        byte[] encrypted = new byte[WestwoodRSA.BlockSize];
        await ReadExactlyAsync(serverStream, encrypted, ct);

        // Decrypt + extract the RC4 session key.
        byte[] decrypted = new byte[WestwoodRSA.BlockSize];
        WestwoodRSA.DecryptBlock(encrypted, decrypted);
        byte[] sessionKey = RsaHandshake.ExtractSessionKeyFromDecryptedBlock(decrypted);

        var inFromClient = new WestwoodRC4();
        var outToClient = new WestwoodRC4();
        inFromClient.PrepareKey(sessionKey);
        outToClient.PrepareKey(sessionKey);
        return (inFromClient, outToClient);
    }

    private static async Task ReadExactlyAsync(NetworkStream s, byte[] buf, CancellationToken ct)
    {
        int off = 0;
        while (off < buf.Length)
        {
            int n = await s.ReadAsync(buf.AsMemory(off), ct);
            if (n == 0) throw new EndOfStreamException("server-side read EOF");
            off += n;
        }
    }

    [Fact]
    public async Task Handshake_Then_RoundTripPacket_DecryptsCorrectlyOnBothSides()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;

        // Server task: accept, run handshake, read one encrypted
        // packet from client, then send one encrypted packet back.
        var serverTask = Task.Run(async () =>
        {
            using var serverTcp = await listener.AcceptTcpClientAsync(cts.Token);
            using var serverStream = serverTcp.GetStream();

            var (inFromClient, outToClient) = await ServerHandshakeAsync(serverStream, cts.Token);

            // Read header (4 bytes) -> decrypt -> read payload -> decrypt.
            byte[] hdr = new byte[PacketHeader.WireSize];
            await ReadExactlyAsync(serverStream, hdr, cts.Token);
            inFromClient.Transform(hdr);
            var header = PacketHeader.Read(hdr);

            byte[] payload = new byte[header.PayloadLength];
            await ReadExactlyAsync(serverStream, payload, cts.Token);
            inFromClient.Transform(payload);

            // Echo it back with a different opcode so the client can
            // tell the round-trip apart from its own send.
            var echo = Packet.ForOpcode(opcode: 0xFFFF, payload);
            byte[] wire = echo.ToWireBytes();
            outToClient.Transform(wire);
            await serverStream.WriteAsync(wire, cts.Token);
            await serverStream.FlushAsync(cts.Token);

            return new { Header = header, Payload = payload };
        });

        // Client side.
        await using var client = await EncryptedTcpConnection.ConnectAsync(
            "127.0.0.1", port, cts.Token);

        byte[] testPayload = new byte[] { 0xCA, 0xFE, 0xBA, 0xBE, 0xDE, 0xAD };
        await client.SendAsync(Packet.ForOpcode(opcode: 0x0035, testPayload), cts.Token);

        var received = await client.ReceiveAsync(cts.Token);

        var serverGot = await serverTask;

        // Server saw the client's MasterJoin opcode and payload after decrypt.
        Assert.Equal(0x0035, serverGot.Header.Opcode);
        Assert.Equal(testPayload, serverGot.Payload);

        // Client got the echo back with opcode 0xFFFF and the same payload.
        Assert.NotNull(received);
        Assert.Equal(0xFFFF, received!.Header.Opcode);
        Assert.Equal(testPayload, received.Payload.ToArray());

        listener.Stop();
    }

    [Fact]
    public async Task ConnectAsync_RejectsBadPort()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => EncryptedTcpConnection.ConnectAsync("127.0.0.1", 0));
    }

    [Fact]
    public async Task ConnectAsync_RejectsBadHost()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => EncryptedTcpConnection.ConnectAsync("", 3500));
    }
}
