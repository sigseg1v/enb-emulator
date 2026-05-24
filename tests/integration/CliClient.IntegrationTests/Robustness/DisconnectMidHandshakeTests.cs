// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// New code; project default license (LICENSES/enb-emulator).

using N7.CliClient.Net;
using N7.CliClient.Session;
using Xunit;

namespace N7.CliClient.IntegrationTests.Robustness;

/// <summary>
/// What the CLI client does when the TCP peer hangs up partway through
/// the RSA + RC4 handshake. Per the Phase S hard rules: don't retry,
/// don't hide, surface the failure cleanly.
/// </summary>
/// <remarks>
/// These tests use <see cref="ScriptedServer"/> — they do NOT need the
/// docker stack. Running them with the stack up is fine; they bind to
/// 127.0.0.1:auto-port and don't collide with the real proxy.
/// </remarks>
public sealed class DisconnectMidHandshakeTests
{
    [Fact]
    public async Task ServerClosesBeforeSendingPubkey_ConnectThrowsEofCleanly()
    {
        // Server accepts, closes immediately — client never sees the
        // 74-byte pubkey.
        await using var bad = new ScriptedServer((stream, _) =>
        {
            stream.Close();
            return Task.CompletedTask;
        });

        await Assert.ThrowsAsync<EndOfStreamException>(async () =>
        {
            await using var _ = await EncryptedTcpConnection.ConnectAsync(
                bad.Host, bad.Port, CancellationToken.None);
        });
    }

    [Fact]
    public async Task ServerSendsPartialPubkey_ConnectThrowsEofCleanly()
    {
        // Server sends 10 bytes (less than the 74-byte pubkey) then closes.
        await using var bad = new ScriptedServer(async (stream, ct) =>
        {
            await stream.WriteAsync(new byte[10], ct);
            await stream.FlushAsync(ct);
            stream.Close();
        });

        var ex = await Assert.ThrowsAsync<EndOfStreamException>(async () =>
        {
            await using var _ = await EncryptedTcpConnection.ConnectAsync(
                bad.Host, bad.Port, CancellationToken.None);
        });
        Assert.Contains("closed", ex.Message);
    }

    [Fact]
    public async Task FullHandshakeThenServerCloses_HealthGuardTripsOnDisconnect()
    {
        // Handshake completes; server then closes the socket. The client
        // observes EOF on the next Receive and signals HealthGuard.
        await using var bad = new ScriptedServer(async (stream, ct) =>
        {
            await ScriptedServer.HandshakeAsServerAsync(stream, ct);
            // Server vanishes after the handshake.
            stream.Close();
        });

        using var guard = new HealthGuard();
        await using var conn = await EncryptedTcpConnection.ConnectAsync(
            bad.Host, bad.Port, CancellationToken.None);

        // Clean EOF surfaces as a null Packet.
        Packet? p = await conn.ReceiveAsync(CancellationToken.None);
        Assert.Null(p);

        // Workflows are responsible for telling the guard a disconnect
        // happened. Simulate that here.
        guard.OnDisconnect("peer closed after handshake");

        Assert.True(guard.Tripped);
        Assert.Contains("disconnected", guard.Reason!);
        Assert.True(guard.Token.IsCancellationRequested);
    }

    [Fact]
    public async Task ClientDoesNotRetry_AfterMidHandshakeDisconnect()
    {
        // Phase S rule #2: no retry storms. After a failed handshake the
        // client must NOT internally reconnect; the next attempt is a
        // fresh, explicit ConnectAsync from the caller. We verify by
        // counting the number of accepts on the server side — exactly 1
        // for one ConnectAsync call.
        int acceptCount = 0;
        var firstAccept = new TaskCompletionSource();

        await using var bad = new ScriptedServer((stream, _) =>
        {
            Interlocked.Increment(ref acceptCount);
            firstAccept.TrySetResult();
            stream.Close();
            return Task.CompletedTask;
        });

        await Assert.ThrowsAsync<EndOfStreamException>(async () =>
        {
            await using var _ = await EncryptedTcpConnection.ConnectAsync(
                bad.Host, bad.Port, CancellationToken.None);
        });

        // Give any (forbidden) retry loop time to fire a second connect.
        await firstAccept.Task.WaitAsync(TimeSpan.FromSeconds(1));
        await Task.Delay(200);

        Assert.Equal(1, acceptCount);
    }
}
