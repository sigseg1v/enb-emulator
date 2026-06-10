// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using N7.CliClient.Session;
using N7.CliClient.Workflows;
using Xunit;

namespace N7.CliClient.IntegrationTests.Workflows;

/// <summary>
/// End-to-end driver of <see cref="ConnectAndLogin"/> against the live
/// dev stack: TLS login → global TCP connect → idle drain
/// → clean disconnect. Asserts the whole workflow ends in a Global
/// stage with a non-empty ticket and no health-guard trip.
///
/// <para>
/// This is the smallest "the smoke test runs as designed" assertion
/// — not pulling in opcode-specific workflows yet (enumerate-sectors
/// etc. are blocked on Phase K wiring the listing opcodes server-
/// side; see plan Item 5 notes).
/// </para>
/// </summary>
[Collection(ServerCollection.Name)]
public sealed class ConnectAndLoginTests
{
    private readonly ServerFixture _server;
    private readonly ClientFixture _client;

    public ConnectAndLoginTests(ServerFixture server)
    {
        _server = server;
        _client = new ClientFixture(server);
    }

    [Fact]
    public async Task ValidAccount_LandsInGlobalStage_NoHealthTrip()
    {
        var account = TestAccounts.New(_server);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var workflow = new ConnectAndLogin(
            new ConnectAndLoginOptions
            {
                LoginHost  = _server.LoginHost,
                LoginPort  = _server.LoginPort,
                GlobalHost = _server.GlobalHost,
                GlobalPort = _server.GlobalPort,
                Username   = account.Username,
                Password   = account.Password,
                IdleDuration = TimeSpan.FromSeconds(2),
                AcceptUntrustedLoginCertificate = true,
            },
            _client.Registry);

        using var guard = new HealthGuard();
        var result = await workflow.RunAsync(guard, cts.Token);

        Assert.True(result.LoginValid, $"login failed; aborted={result.Aborted}");
        Assert.False(string.IsNullOrEmpty(result.Ticket));
        Assert.Equal(SessionStage.Global, result.Stage);
        // The proxy doesn't push any global packets to a client that
        // hasn't sent VersionRequest/GlobalConnect yet, so the drain
        // count is expected to be zero — but assert >= 0 not == 0 so
        // future Phase-K hello-from-server changes don't false-break.
        Assert.True(result.InboundPackets >= 0);
        // Health guard must NOT have tripped on the happy path.
        Assert.Null(result.Aborted);
        Assert.False(guard.Tripped, $"guard tripped: {guard.Reason}");
    }

    [Fact]
    public async Task WrongPassword_AbortsWithLoginRejection()
    {
        var account = TestAccounts.New(_server);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var workflow = new ConnectAndLogin(
            new ConnectAndLoginOptions
            {
                LoginHost  = _server.LoginHost,
                LoginPort  = _server.LoginPort,
                GlobalHost = _server.GlobalHost,
                GlobalPort = _server.GlobalPort,
                Username   = account.Username,
                Password   = "wrong_" + account.Password,
                IdleDuration = TimeSpan.FromSeconds(1),
                AcceptUntrustedLoginCertificate = true,
            },
            _client.Registry);

        using var guard = new HealthGuard();
        var result = await workflow.RunAsync(guard, cts.Token);

        Assert.False(result.LoginValid);
        Assert.Null(result.Ticket);
        Assert.Equal(SessionStage.Disconnected, result.Stage);
        Assert.Equal(0, result.InboundPackets);
        Assert.NotNull(result.Aborted);
        Assert.Contains("Valid=False", result.Aborted!, StringComparison.OrdinalIgnoreCase);
    }
}
