// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// New code; project default license (LICENSES/enb-emulator).

using N7.CliClient.Net;
using N7.CliClient.Session;
using Xunit;

namespace N7.CliClient.IntegrationTests.Robustness;

/// <summary>
/// Phase S hard rule #2 in action: when the server (or anything claiming
/// to be the server) floods the client with traffic, HealthGuard must
/// trip on the rate-limit threshold and the workflow's cancellation
/// token must fire — preventing a runaway loop from re-flooding the
/// server in response.
/// </summary>
/// <remarks>
/// Pure-HealthGuard rate-limit unit tests already live in
/// CliClient.UnitTests/Session/HealthGuardTests.cs. The job of these
/// tests is the *integration* slice: a real TCP connection, real RC4
/// stream, real frames coming through ReceiveAsync — and the guard
/// trips on the actual <see cref="Packet"/> values the wire produced,
/// not synthetic <see cref="Packet.ForOpcode"/> calls.
/// </remarks>
public sealed class RateLimitRespectTests
{
    [Fact]
    public async Task ServerFloodsClient_HealthGuardTripsAndCancelsToken()
    {
        // The scripted server completes the handshake then ships frames
        // as fast as it can for ~250ms. The client loop drains frames
        // and notifies HealthGuard; the guard's per-second budget is 50
        // so anything past the first ~50 frames trips it.
        const int budget = 50;

        await using var bad = new ScriptedServer(async (stream, ct) =>
        {
            var (_, outbound) = await ScriptedServer.HandshakeAsServerAsync(stream, ct);
            var deadline = DateTime.UtcNow.AddMilliseconds(250);
            while (!ct.IsCancellationRequested && DateTime.UtcNow < deadline)
            {
                byte[] frame = ScriptedServer.EncryptFrame(outbound, opcode: 0x0099, payload: Array.Empty<byte>());
                try { await stream.WriteAsync(frame, ct); }
                catch { break; }
            }
        });

        var opts = new HealthGuardOptions { MaxPacketsPerSecond = budget };
        using var guard = new HealthGuard(opts);

        await using var conn = await EncryptedTcpConnection.ConnectAsync(
            bad.Host, bad.Port, CancellationToken.None);

        // Drain until the guard trips or we time out. The test cancels
        // its own loop on guard trip — that's the "respect" behaviour
        // we're asserting.
        using var loopTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            guard.Token, loopTimeout.Token);

        int seen = 0;
        try
        {
            while (!linked.Token.IsCancellationRequested)
            {
                Packet? p = await conn.ReceiveAsync(linked.Token);
                if (p is null) break;
                seen++;
                guard.OnPacketReceived(p);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected — guard tripped and cancelled our linked token.
        }
        catch (EndOfStreamException)
        {
            // Server's script exit closes the socket; acceptable terminal.
        }

        Assert.True(guard.Tripped, $"guard did not trip after {seen} frames");
        Assert.Contains("inbound packet rate", guard.Reason!);
        Assert.True(guard.Token.IsCancellationRequested);
        // Sanity: we received at least the budget — if we tripped on
        // the first frame something is wrong with our threshold logic.
        Assert.True(seen >= budget,
            $"tripped too early: only {seen} frames seen before trip (budget={budget})");
    }

    [Fact]
    public async Task ClientSelfFlood_HealthGuardTripsOnOutboundBudget()
    {
        // Counter-scenario: a buggy workflow that fires too many sends
        // in a tight loop. The guard catches that too — we should never
        // be the source of a server-overload either.
        const int budget = 25;

        var opts = new HealthGuardOptions { MaxPacketsPerSecond = budget };
        using var guard = new HealthGuard(opts);

        await using var bad = new ScriptedServer(async (stream, ct) =>
        {
            await ScriptedServer.HandshakeAsServerAsync(stream, ct);
            // Just sink whatever the client sends; the test asserts on
            // the guard, not on the server-side observation.
            byte[] sink = new byte[4096];
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    int n = await stream.ReadAsync(sink, ct);
                    if (n == 0) break;
                }
            }
            catch { /* shutdown */ }
        });

        await using var conn = await EncryptedTcpConnection.ConnectAsync(
            bad.Host, bad.Port, CancellationToken.None);

        // Fire packets until the guard trips or we hit a (forbidden)
        // soft ceiling that proves we never tripped.
        int sent = 0;
        const int hardCap = budget * 4; // if we get this far the guard is broken
        while (!guard.Tripped && sent < hardCap)
        {
            var packet = Packet.ForOpcode(0x0035, Array.Empty<byte>());
            await conn.SendAsync(packet, CancellationToken.None);
            guard.OnPacketSent(packet);
            sent++;
        }

        Assert.True(guard.Tripped, $"guard did not trip after {sent} sends");
        Assert.Contains("outbound packet rate", guard.Reason!);
        Assert.True(sent >= budget,
            $"tripped too early: only {sent} sends before trip (budget={budget})");
    }
}
