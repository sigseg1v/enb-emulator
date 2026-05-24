// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// New code; project default license (LICENSES/enb-emulator).

using N7.CliClient.Auth;
using Xunit;

namespace N7.CliClient.IntegrationTests.Handshake;

/// <summary>
/// First wire-level assertions against the live login server: TLS
/// terminates, <c>/AuthLogin</c> returns the expected Valid + Ticket
/// shape for a known-good account, returns Valid=False for bad
/// credentials.
/// </summary>
[Collection(ServerCollection.Name)]
public sealed class TlsLoginTests
{
    private readonly ServerFixture _server;
    private readonly ClientFixture _client;

    public TlsLoginTests(ServerFixture server)
    {
        _server = server;
        _client = new ClientFixture(server);
    }

    [Fact]
    public async Task ValidAccount_ReturnsValidTicket()
    {
        var account = TestAccounts.Pool[0];
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var response = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(account.Username, account.Password), cts.Token);

        Assert.True(response.Valid,
            $"login should have succeeded for '{account.Username}' — raw body: {response.RawBody.TrimEnd()}");
        Assert.False(string.IsNullOrEmpty(response.Ticket),
            "Ticket should be non-empty on success");
        // LinuxAuth.cpp issues a 40-character hex ticket (20 binary
        // bytes → 40 hex chars). Don't pin to exactly 40; assert
        // "looks like a hex string with at least 20 chars" so changes
        // to ticket size don't false-positive.
        Assert.True(response.Ticket.Length >= 20,
            $"Ticket looks too short: '{response.Ticket}'");
    }

    [Fact]
    public async Task WrongPassword_ReturnsInvalid()
    {
        var account = TestAccounts.Pool[0];
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var response = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(account.Username, "definitely_not_" + account.Password),
            cts.Token);

        Assert.False(response.Valid,
            $"login must NOT succeed with wrong password — raw body: {response.RawBody.TrimEnd()}");
        Assert.True(string.IsNullOrEmpty(response.Ticket),
            "Ticket must be empty on a failed login");
    }

    [Fact]
    public async Task NonexistentAccount_ReturnsInvalid()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var response = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest("user_that_does_not_exist_in_seed", "anything"),
            cts.Token);

        Assert.False(response.Valid);
        Assert.True(string.IsNullOrEmpty(response.Ticket));
    }
}
