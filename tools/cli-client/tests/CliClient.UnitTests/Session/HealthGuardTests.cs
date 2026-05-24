// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using N7.CliClient.Net;
using N7.CliClient.Opcodes;
using N7.CliClient.Session;
using Xunit;

namespace N7.CliClient.UnitTests.Session;

public sealed class HealthGuardTests
{
    [Fact]
    public void NewGuard_IsNotTripped()
    {
        using var g = new HealthGuard();
        Assert.False(g.Tripped);
        Assert.Null(g.Reason);
        Assert.False(g.Token.IsCancellationRequested);
    }

    [Fact]
    public void OnDisconnect_TripsImmediately()
    {
        using var g = new HealthGuard();
        g.OnDisconnect("RST from peer");
        Assert.True(g.Tripped);
        Assert.Contains("server disconnected", g.Reason!);
        Assert.Contains("RST from peer",       g.Reason!);
        Assert.True(g.Token.IsCancellationRequested);
    }

    [Fact]
    public void Trip_IsOneShot_FirstReasonWins()
    {
        using var g = new HealthGuard();
        g.Trip("first");
        g.Trip("second");
        Assert.Equal("first", g.Reason);
    }

    [Fact]
    public void OnPacketReceived_WithErrorOpcode_Trips()
    {
        var opts = new HealthGuardOptions
        {
            ErrorOpcodes = new[] { new OpcodeId(0x0099) },
        };
        using var g = new HealthGuard(opts);
        g.OnPacketReceived(Packet.ForOpcode(0x0099, Array.Empty<byte>()));
        Assert.True(g.Tripped);
        Assert.Contains("0x0099", g.Reason!);
    }

    [Fact]
    public void OnPacketReceived_NonErrorOpcode_DoesNotTrip()
    {
        using var g = new HealthGuard();
        g.OnPacketReceived(Packet.ForOpcode(0x0035, Array.Empty<byte>()));
        Assert.False(g.Tripped);
    }

    [Fact]
    public void RegisterErrorOpcode_AddsAtRuntime()
    {
        using var g = new HealthGuard();
        g.RegisterErrorOpcode(new OpcodeId(0x00FF));
        g.OnPacketReceived(Packet.ForOpcode(0x00FF, Array.Empty<byte>()));
        Assert.True(g.Tripped);
    }

    [Fact]
    public void OnPacketReceived_RateSpike_Trips()
    {
        var opts = new HealthGuardOptions { MaxPacketsPerSecond = 10 };
        using var g = new HealthGuard(opts);

        for (int i = 0; i < 9; i++)
            g.OnPacketReceived(Packet.ForOpcode(0x0035, Array.Empty<byte>()));
        Assert.False(g.Tripped);

        for (int i = 0; i < 5; i++)
            g.OnPacketReceived(Packet.ForOpcode(0x0035, Array.Empty<byte>()));
        Assert.True(g.Tripped);
        Assert.Contains("inbound packet rate", g.Reason!);
    }

    [Fact]
    public void OnPacketSent_RateSpike_Trips()
    {
        var opts = new HealthGuardOptions { MaxPacketsPerSecond = 5 };
        using var g = new HealthGuard(opts);
        for (int i = 0; i < 10; i++)
            g.OnPacketSent(Packet.ForOpcode(0x0035, Array.Empty<byte>()));
        Assert.True(g.Tripped);
        Assert.Contains("outbound packet rate", g.Reason!);
    }

    [Fact]
    public async Task BeginExpectResponse_FiresIfNoResponse()
    {
        using var g = new HealthGuard();
        using (g.BeginExpectResponse("test-op", TimeSpan.FromMilliseconds(50)))
        {
            await Task.Delay(200);
        }
        Assert.True(g.Tripped);
        Assert.Contains("response timeout", g.Reason!);
        Assert.Contains("test-op",          g.Reason!);
    }

    [Fact]
    public async Task BeginExpectResponse_DisposedBeforeTimeout_DoesNotTrip()
    {
        using var g = new HealthGuard();
        using (g.BeginExpectResponse("test-op", TimeSpan.FromMilliseconds(500)))
        {
            await Task.Delay(50);
        } // disposed here cancels the timeout
        await Task.Delay(600);
        Assert.False(g.Tripped);
    }

    [Fact]
    public async Task BeginExpectResponse_MatchingInboundOpcode_Resolves()
    {
        using var g = new HealthGuard();
        using var _ = g.BeginExpectResponse(
            "wait-for-redirect",
            TimeSpan.FromMilliseconds(500),
            opcodeFilter: new OpcodeId(0x0036));

        await Task.Delay(50);
        g.OnPacketReceived(Packet.ForOpcode(0x0036, Array.Empty<byte>()));
        await Task.Delay(600);

        Assert.False(g.Tripped);
    }

    [Fact]
    public async Task BeginExpectResponse_WrongInboundOpcode_DoesNotResolve_Trips()
    {
        using var g = new HealthGuard();
        using var _ = g.BeginExpectResponse(
            "wait-for-redirect",
            TimeSpan.FromMilliseconds(100),
            opcodeFilter: new OpcodeId(0x0036));

        g.OnPacketReceived(Packet.ForOpcode(0x0099, Array.Empty<byte>()));
        await Task.Delay(300);

        Assert.True(g.Tripped);
    }

    [Fact]
    public void Token_IsCancelled_AfterTrip()
    {
        using var g = new HealthGuard();
        g.OnDisconnect("test");
        Assert.True(g.Token.IsCancellationRequested);
    }

    [Fact]
    public void Dispose_CleansUpWithoutThrowing()
    {
        var g = new HealthGuard();
        g.Dispose();
        g.Dispose(); // double dispose is fine
    }

    [Fact]
    public void OnPacketReceived_AfterTrip_IsNoOp()
    {
        using var g = new HealthGuard();
        g.OnDisconnect("test");
        // Doesn't throw, doesn't change Reason.
        g.OnPacketReceived(Packet.ForOpcode(0x0035, Array.Empty<byte>()));
        Assert.Contains("test", g.Reason!);
    }
}
