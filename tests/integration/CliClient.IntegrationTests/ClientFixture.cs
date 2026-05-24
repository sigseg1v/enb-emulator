// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// New code; project default license (LICENSES/enb-emulator).

using N7.CliClient.Auth;
using N7.CliClient.Net;
using N7.CliClient.Opcodes;
using N7.CliClient.Opcodes.Inbound;
using N7.CliClient.Session;

namespace N7.CliClient.IntegrationTests;

/// <summary>
/// Per-test helper that builds the Phase S types tests will drive:
/// an <see cref="OpcodeRegistry"/> with the full named-opaque coverage
/// plus typed codecs, an <see cref="AuthLoginClient"/> wired to the
/// fixture's TLS endpoint, and convenience methods for the common
/// login → connect → ticket-handoff sequence.
///
/// <para>
/// Stateless across tests. Construct one per test; dispose anything
/// you create. The <see cref="ServerFixture"/> is shared (via the
/// <see cref="ServerCollection"/>); this class is the per-test seam.
/// </para>
///
/// <para>
/// Phase T Item 1 ships the constructor + registry + auth client +
/// the smoke-test helper. Item 2 adds the fixture-account pool.
/// Items 3+ add the per-area helpers (handshake assertions, opcode
/// round-trip helpers, capture-replay loader).
/// </para>
/// </summary>
public sealed class ClientFixture
{
    private readonly ServerFixture _server;

    public ClientFixture(ServerFixture server)
    {
        _server = server ?? throw new ArgumentNullException(nameof(server));

        Registry = new OpcodeRegistry();
        Registry.RegisterAllNamedOpaque();
        Registry.Register(new ServerRedirectCodec());

        AuthLogin = new AuthLoginClient(
            _server.LoginHost,
            _server.LoginPort,
            acceptUntrustedCertificates: true);
    }

    public OpcodeRegistry  Registry  { get; }
    public AuthLoginClient AuthLogin { get; }

    /// <summary>
    /// Convenience: log in, connect to the global server, return the
    /// connected session. Caller owns disposal.
    /// </summary>
    public async Task<CliSession> ConnectGlobalAsync(
        string user, string pass, CancellationToken ct)
    {
        var login = await AuthLogin.LoginAsync(new AuthLoginRequest(user, pass), ct);
        if (!login.Valid || string.IsNullOrEmpty(login.Ticket))
            throw new InvalidOperationException(
                $"login rejected for user '{user}' (raw response: {login.RawBody.TrimEnd()})");

        var session = new CliSession(Registry, login.Ticket);
        try
        {
            await session.ConnectGlobalAsync(_server.GlobalHost, _server.GlobalPort, ct);
            return session;
        }
        catch
        {
            await session.DisposeAsync();
            throw;
        }
    }
}
