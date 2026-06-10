// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using N7.CliClient.Opcodes;
using N7.CliClient.Session;
using N7.CliClient.Workflows;
using Xunit;

namespace N7.CliClient.UnitTests.Workflows;

public sealed class ConnectAndLoginTests
{
    [Fact]
    public void Constructor_RejectsNullOptions()
    {
        Assert.Throws<ArgumentNullException>(
            () => new ConnectAndLogin(null!, new OpcodeRegistry()));
    }

    [Fact]
    public void Constructor_RejectsNullRegistry()
    {
        Assert.Throws<ArgumentNullException>(
            () => new ConnectAndLogin(SampleOptions(), null!));
    }

    [Fact]
    public async Task Run_NullGuard_Throws()
    {
        var wf = new ConnectAndLogin(SampleOptions(), new OpcodeRegistry());
        await Assert.ThrowsAsync<ArgumentNullException>(() => wf.RunAsync(null!));
    }

    [Fact]
    public async Task Run_LoginToUnreachableHost_TripsGuardCleanly()
    {
        // Point login at a port that nothing's listening on. The TCP
        // connect will fail immediately, the workflow should trip the
        // guard with a transport error and return a clean result rather
        // than throwing.
        var opts = new ConnectAndLoginOptions
        {
            Username = "test",
            Password = "test",
            LoginHost = "127.0.0.1",
            LoginPort = 1,
        };
        var wf = new ConnectAndLogin(opts, new OpcodeRegistry());

        using var guard = new HealthGuard();
        var result = await wf.RunAsync(guard, CancellationToken.None);

        Assert.False(result.LoginValid);
        Assert.Null(result.Ticket);
        Assert.Equal(SessionStage.Disconnected, result.Stage);
        Assert.Equal(0, result.InboundPackets);
        Assert.NotNull(result.Aborted);
        Assert.True(guard.Tripped);
    }

    [Fact]
    public void Options_HaveSensibleDefaults()
    {
        var opts = SampleOptions();
        Assert.Equal("127.0.0.1", opts.LoginHost);
        Assert.Equal(443, opts.LoginPort);
        Assert.Equal("127.0.0.1", opts.GlobalHost);
        Assert.Equal(3500, opts.GlobalPort);
        Assert.True(opts.AcceptUntrustedLoginCertificate);
        Assert.Equal(TimeSpan.FromSeconds(5), opts.IdleDuration);
    }

    private static ConnectAndLoginOptions SampleOptions() => new()
    {
        Username = "test",
        Password = "test",
    };
}
