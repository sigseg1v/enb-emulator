// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Net;
using System.Net.Sockets;
using N7.CliClient.Net;

namespace N7.CliClient.IntegrationTests.Robustness;

/// <summary>
/// Throw-away TCP listener that runs a caller-supplied script on the
/// single connection it accepts, then closes. Used to exercise the
/// CLI client's behaviour under server distress (early disconnect,
/// garbage replies, packet floods) WITHOUT touching the real server.
/// </summary>
/// <remarks>
/// <para>
/// Required by the Phase T server-integrity guard rail: a Robustness
/// test that needed the real server to misbehave would either (a) need
/// to break the real server (forbidden — server-integrity rule #1) or
/// (b) be impossible to write deterministically. The scripted server is
/// the standard escape hatch — a fake responder we can program to
/// produce the exact pathological behaviour the client needs to
/// withstand.
/// </para>
/// <para>
/// Binds to 127.0.0.1 on an OS-assigned port (port 0). The Port
/// property is populated synchronously after construction; the
/// background accept loop starts immediately. Dispose stops the
/// listener and waits for the script task to drain.
/// </para>
/// </remarks>
public sealed class ScriptedServer : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly Task _acceptTask;
    private readonly CancellationTokenSource _cts = new();

    /// <summary>Port the listener bound to. Pass this to the client under test.</summary>
    public int Port { get; }

    /// <summary>Host the listener bound to. Always loopback for safety.</summary>
    public string Host => "127.0.0.1";

    /// <summary>Exception caught from the script, if any. Inspect after the test.</summary>
    public Exception? ScriptError { get; private set; }

    /// <summary>
    /// Start a scripted listener. The script runs on the accepted
    /// <see cref="NetworkStream"/>; when it returns the stream is closed
    /// and the listener stops.
    /// </summary>
    public ScriptedServer(Func<NetworkStream, CancellationToken, Task> script)
    {
        ArgumentNullException.ThrowIfNull(script);
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        Port = ((IPEndPoint) _listener.LocalEndpoint).Port;
        _acceptTask = Task.Run(() => RunAsync(script, _cts.Token));
    }

    private async Task RunAsync(
        Func<NetworkStream, CancellationToken, Task> script,
        CancellationToken ct)
    {
        try
        {
            using TcpClient tcp = await _listener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
            using NetworkStream stream = tcp.GetStream();
            await script(stream, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { /* shutdown */ }
        catch (Exception ex)
        {
            ScriptError = ex;
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        try { _listener.Stop(); } catch { /* already stopped */ }
        try
        {
            // Bound to prevent a leaked test hanging the runner.
            await _acceptTask.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
        catch { /* swallow */ }
        _cts.Dispose();
    }

    /// <summary>
    /// Convenience: drive the Net-7 server-side of the RSA + RC4
    /// handshake. Sends 74 bytes of dummy "pubkey" (the client doesn't
    /// parse it — see EncryptedTcpConnection.DoClientKeyExchangeAsync),
    /// reads the 68-byte encrypted-key packet the client sends back,
    /// RSA-decrypts it to recover the 8-byte session key, and returns
    /// two RC4 contexts keyed off it. After this method returns, the
    /// caller can send/receive RC4-encrypted opcode frames on the
    /// same stream.
    /// </summary>
    public static async Task<(WestwoodRC4 In, WestwoodRC4 Out)> HandshakeAsServerAsync(
        NetworkStream stream, CancellationToken ct)
    {
        // 1. Ship 74 bytes — client discards them.
        byte[] fakePubkey = new byte[RsaHandshake.ServerPubkeyPacketSize];
        await stream.WriteAsync(fakePubkey, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);

        // 2. Read the client's 68-byte encrypted-key packet.
        byte[] clientKeyPacket = new byte[RsaHandshake.ClientKeyPacketSize];
        int offset = 0;
        while (offset < clientKeyPacket.Length)
        {
            int n = await stream.ReadAsync(
                clientKeyPacket.AsMemory(offset, clientKeyPacket.Length - offset),
                ct).ConfigureAwait(false);
            if (n == 0)
                throw new EndOfStreamException(
                    $"client closed mid-handshake (got {offset} of {clientKeyPacket.Length} bytes)");
            offset += n;
        }

        // 3. Decrypt the 64-byte RSA block (skip 4-byte length prefix).
        byte[] block = new byte[WestwoodRSA.BlockSize];
        WestwoodRSA.DecryptBlock(clientKeyPacket.AsSpan(4, WestwoodRSA.BlockSize), block);

        // 4. Pull the 8-byte session key out (reversed positions [63..56]).
        byte[] sessionKey = RsaHandshake.ExtractSessionKeyFromDecryptedBlock(block);

        // 5. Key two RC4 contexts off the same bytes — symmetric with
        //    the client (one each for inbound + outbound).
        var rcIn = new WestwoodRC4();
        var rcOut = new WestwoodRC4();
        rcIn.PrepareKey(sessionKey);
        rcOut.PrepareKey(sessionKey);
        return (rcIn, rcOut);
    }

    /// <summary>
    /// Build an RC4-encrypted EnB frame ready to write to a NetworkStream.
    /// Uses the supplied outbound RC4 context (which advances in place).
    /// </summary>
    public static byte[] EncryptFrame(WestwoodRC4 outbound, ushort opcode, ReadOnlySpan<byte> payload)
    {
        Packet p = Packet.ForOpcode(opcode, payload.ToArray());
        byte[] wire = p.ToWireBytes();
        outbound.Transform(wire);
        return wire;
    }
}
