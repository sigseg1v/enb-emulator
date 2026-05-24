// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// New code; project default license (LICENSES/enb-emulator).

using System.Net.Sockets;

namespace N7.CliClient.Net;

/// <summary>
/// A single TCP connection to one of the Net-7 servers (global, master,
/// or sector) with the Westwood RSA + RC4 handshake completed and the
/// per-direction RC4 streams keyed up. Owns the TCP socket and both
/// cipher contexts; everything sent through <see cref="SendAsync"/> is
/// RC4-encrypted on the way out and everything pulled by
/// <see cref="ReceiveAsync"/> is RC4-decrypted as it lands.
/// </summary>
/// <remarks>
/// <para>
/// Wire dance (mirrors proxy/Connection.cpp::DoClientKeyExchange):
/// </para>
/// <list type="number">
///   <item>Open TCP socket to host:port.</item>
///   <item>Read exactly 74 bytes — the server's public-key packet.
///         We discard the bytes; the pubkey is fixed and baked into
///         <see cref="WestwoodRSA"/>.</item>
///   <item>Pick 8 random bytes — the RC4 session key.</item>
///   <item>Build the 68-byte client key packet via <see cref="RsaHandshake"/>
///         and write it to the socket.</item>
///   <item>Key both <see cref="WestwoodRC4"/> instances (inbound +
///         outbound) off the same 8-byte session key.</item>
///   <item>From here on, every byte sent or received is XORed with
///         its respective keystream as it crosses the boundary.</item>
/// </list>
/// <para>
/// The class is NOT thread-safe. Send and receive must be serialized
/// from a single owner — the EnB protocol is request-driven enough
/// that the CLI client's session loop can do one direction at a time
/// without needing duplex concurrency.
/// </para>
/// </remarks>
public sealed class EncryptedTcpConnection : IAsyncDisposable
{
    private readonly TcpClient _tcp;
    private readonly NetworkStream _stream;
    private readonly WestwoodRC4 _cryptIn = new();
    private readonly WestwoodRC4 _cryptOut = new();
    private readonly byte[] _readBuffer = new byte[PacketHeader.WireSize];

    /// <summary>Endpoint we're connected to — for diagnostics / logs.</summary>
    public string Host { get; }
    public int Port { get; }

    private EncryptedTcpConnection(TcpClient tcp, string host, int port)
    {
        _tcp = tcp;
        _stream = tcp.GetStream();
        Host = host;
        Port = port;
    }

    /// <summary>
    /// Connect to <paramref name="host"/>:<paramref name="port"/> and
    /// complete the RSA + RC4 handshake. The returned connection is
    /// ready for <see cref="SendAsync"/> / <see cref="ReceiveAsync"/>.
    /// </summary>
    public static async Task<EncryptedTcpConnection> ConnectAsync(
        string host,
        int port,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(host);
        if (port <= 0 || port > 65535)
            throw new ArgumentOutOfRangeException(nameof(port), port, "port must be 1..65535");

        var tcp = new TcpClient();
        try
        {
            await tcp.ConnectAsync(host, port, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            tcp.Dispose();
            throw;
        }

        var conn = new EncryptedTcpConnection(tcp, host, port);
        try
        {
            await conn.DoClientKeyExchangeAsync(cancellationToken).ConfigureAwait(false);
            return conn;
        }
        catch
        {
            await conn.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// Build a connection from an already-open <see cref="TcpClient"/>
    /// without running the handshake. Test-only — production callers
    /// use <see cref="ConnectAsync"/>.
    /// </summary>
    internal static EncryptedTcpConnection FromTcpClientForTesting(
        TcpClient tcp, string host, int port)
    {
        return new EncryptedTcpConnection(tcp, host, port);
    }

    /// <summary>
    /// Test hook: complete the handshake on a pre-supplied
    /// <see cref="EncryptedTcpConnection"/>. Production callers don't
    /// need this — <see cref="ConnectAsync"/> does it.
    /// </summary>
    internal Task RunHandshakeForTesting(CancellationToken ct)
        => DoClientKeyExchangeAsync(ct);

    private async Task DoClientKeyExchangeAsync(CancellationToken ct)
    {
        // 1. Read the server's 74-byte pubkey packet. We don't actually
        //    parse it — the pubkey is hardcoded into WestwoodRSA. We
        //    just need to consume the bytes the server sent so the
        //    stream is positioned for our reply.
        byte[] serverPubkey = new byte[RsaHandshake.ServerPubkeyPacketSize];
        await ReadExactAsync(serverPubkey, ct).ConfigureAwait(false);

        // 2. Build the encrypted-RC4-key packet (4-byte BE length + 64
        //    RSA-encrypted bytes) and the corresponding 8-byte session
        //    key.
        (byte[] wire, byte[] sessionKey) = RsaHandshake.BuildClientKeyPacket();

        // 3. Send it.
        await _stream.WriteAsync(wire, ct).ConfigureAwait(false);
        await _stream.FlushAsync(ct).ConfigureAwait(false);

        // 4. Key both RC4 ciphers off the same 8-byte session key.
        //    Inbound and outbound get independent permutation state.
        _cryptIn.PrepareKey(sessionKey);
        _cryptOut.PrepareKey(sessionKey);
    }

    /// <summary>
    /// Send an unencrypted <see cref="Packet"/>. The full frame
    /// (header + payload) is serialised, RC4-encrypted in place, and
    /// written to the socket. The packet header's <c>Size</c> field
    /// is whatever the caller built it as — we trust the codec layer
    /// to have computed it correctly.
    /// </summary>
    public async Task SendAsync(Packet packet, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(packet);

        byte[] wire = packet.ToWireBytes();
        _cryptOut.Transform(wire);
        await _stream.WriteAsync(wire, cancellationToken).ConfigureAwait(false);
        await _stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Receive exactly one <see cref="Packet"/> from the wire. Reads
    /// the 4-byte header (RC4-decrypts it), uses the size field to
    /// know how many payload bytes to read, then reads + decrypts the
    /// payload. Returns null on clean EOF; throws on partial reads.
    /// </summary>
    public async Task<Packet?> ReceiveAsync(CancellationToken cancellationToken = default)
    {
        // 1. Read + decrypt the 4-byte header.
        if (!await TryReadExactAsync(_readBuffer, cancellationToken).ConfigureAwait(false))
            return null;
        _cryptIn.Transform(_readBuffer);

        var header = PacketHeader.Read(_readBuffer);
        if (header.Size < PacketHeader.WireSize)
        {
            throw new InvalidDataException(
                $"received frame with size {header.Size} < header size {PacketHeader.WireSize} " +
                $"(opcode 0x{header.Opcode:X4}); likely a desynced RC4 stream — abort the session");
        }

        // 2. Read + decrypt the payload.
        int payloadLength = header.PayloadLength;
        byte[] payload = new byte[payloadLength];
        if (payloadLength > 0)
        {
            await ReadExactAsync(payload, cancellationToken).ConfigureAwait(false);
            _cryptIn.Transform(payload);
        }

        return new Packet(header, payload);
    }

    private async Task ReadExactAsync(byte[] buffer, CancellationToken ct)
    {
        if (!await TryReadExactAsync(buffer, ct).ConfigureAwait(false))
        {
            throw new EndOfStreamException(
                $"remote {Host}:{Port} closed the connection mid-read " +
                $"(needed {buffer.Length} bytes)");
        }
    }

    private async Task<bool> TryReadExactAsync(byte[] buffer, CancellationToken ct)
    {
        int offset = 0;
        while (offset < buffer.Length)
        {
            int n = await _stream.ReadAsync(
                buffer.AsMemory(offset, buffer.Length - offset),
                ct).ConfigureAwait(false);
            if (n == 0)
                return offset == 0 ? false : throw new EndOfStreamException(
                    $"remote {Host}:{Port} closed the connection mid-read " +
                    $"(got {offset} of {buffer.Length} bytes)");
            offset += n;
        }
        return true;
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await _stream.DisposeAsync().ConfigureAwait(false);
        }
        catch { /* swallow on close */ }
        _tcp.Dispose();
    }
}
