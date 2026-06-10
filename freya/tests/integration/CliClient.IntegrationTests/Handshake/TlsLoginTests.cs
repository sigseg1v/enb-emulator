// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

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
        var account = TestAccounts.New(_server);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var response = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(account.Username, account.Password), cts.Token);

        Assert.True(response.Valid,
            $"login should have succeeded for '{account.Username}' — raw body: {response.RawBody.TrimEnd()}");
        Assert.False(string.IsNullOrEmpty(response.Ticket),
            "Ticket should be non-empty on success");
        // LinuxAuth.cpp BuildTicketLocked emits "%s-%s" (username +
        // hyphen + a CSPRNG token). The token is 16 bytes drawn from
        // libsodium's randombytes_buf, hex-encoded to exactly 32
        // lowercase hex chars -- no '-' inside it, so the game server's
        // strtok(ticket, "-") still splits username from token cleanly.
        // Asserting on the format keeps us honest if it changes: we pin
        // both the length (16 bytes -> 32 hex) and the alphabet.
        Assert.StartsWith(account.Username + "-", response.Ticket);
        var suffix = response.Ticket[(account.Username.Length + 1)..];
        Assert.True(
            suffix.Length == 32 && suffix.All(Uri.IsHexDigit),
            $"Ticket suffix should be 32 hex chars (16-byte CSPRNG token): '{response.Ticket}'");
    }

    [Fact]
    public async Task WrongPassword_ReturnsInvalid()
    {
        var account = TestAccounts.New(_server);
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
