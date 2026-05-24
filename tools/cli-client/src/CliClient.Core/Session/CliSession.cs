// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using N7.CliClient.Net;
using N7.CliClient.Opcodes;
using N7.CliClient.Opcodes.Inbound;

namespace N7.CliClient.Session;

/// <summary>
/// Orchestrates the global → master → sector connection chain for a
/// single CLI client session. Holds at most one
/// <see cref="EncryptedTcpConnection"/> at a time; on
/// <see cref="ServerRedirect"/> the current connection is closed and
/// a new one is opened to the redirect target with a fresh RSA + RC4
/// handshake.
/// </summary>
/// <remarks>
/// <para>
/// This is a thin coordinator — it doesn't own the AuthLogin flow
/// (caller does that first and passes the ticket in) and doesn't own
/// the per-opcode workflow logic (callers wire that up through
/// <see cref="ReceiveAsync"/> + the opcode registry).
/// </para>
/// <para>
/// Production callers:
/// </para>
/// <code>
///   var ticket = await loginClient.LoginAsync(...);
///   await using var session = new CliSession(opcodeRegistry, ticket.Ticket);
///   await session.ConnectGlobalAsync("127.0.0.1", 3500);
///   while (true) {
///       var packet = await session.ReceiveAsync();
///       if (packet is null) break;
///       var decoded = opcodeRegistry.Resolve(new OpcodeId(packet.Header.Opcode))
///                                   .DecodeInbound(packet.Payload.Span);
///       if (decoded is ServerRedirect r) await session.FollowRedirectAsync(r);
///       // ... else hand to workflow ...
///   }
/// </code>
/// </remarks>
public sealed class CliSession : IAsyncDisposable
{
    private readonly OpcodeRegistry _registry;
    private EncryptedTcpConnection? _current;

    /// <summary>The 20-byte auth ticket from <c>/AuthLogin</c>.</summary>
    public string Ticket { get; }

    /// <summary>Current stage in the global → master → sector chain.</summary>
    public SessionStage Stage { get; private set; } = SessionStage.Disconnected;

    /// <summary>
    /// Endpoint of the current TCP connection, or null if not connected.
    /// Diagnostic only.
    /// </summary>
    public (string Host, int Port)? CurrentEndpoint =>
        _current is null ? null : (_current.Host, _current.Port);

    public CliSession(OpcodeRegistry registry, string ticket)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentException.ThrowIfNullOrEmpty(ticket);
        _registry = registry;
        Ticket = ticket;
        Stage = SessionStage.Authenticated;
    }

    /// <summary>
    /// Open the initial TCP connection — typically to the proxy on
    /// port 3500. After this returns, the connection is past the
    /// RSA + RC4 handshake and ready for opcode I/O.
    /// </summary>
    public async Task ConnectGlobalAsync(string host, int port, CancellationToken ct = default)
    {
        if (Stage != SessionStage.Authenticated)
            throw new InvalidOperationException(
                $"ConnectGlobalAsync requires Authenticated stage, was {Stage}");

        _current = await EncryptedTcpConnection.ConnectAsync(host, port, ct).ConfigureAwait(false);
        Stage = SessionStage.Global;
    }

    /// <summary>
    /// Send a packet on the current connection. Throws if not connected.
    /// </summary>
    public Task SendAsync(Packet packet, CancellationToken ct = default)
    {
        if (_current is null)
            throw new InvalidOperationException($"not connected (stage={Stage})");
        return _current.SendAsync(packet, ct);
    }

    /// <summary>
    /// Receive the next packet on the current connection. Returns null
    /// when the remote closes the socket cleanly.
    /// </summary>
    public Task<Packet?> ReceiveAsync(CancellationToken ct = default)
    {
        if (_current is null)
            throw new InvalidOperationException($"not connected (stage={Stage})");
        return _current.ReceiveAsync(ct);
    }

    /// <summary>
    /// Close the current TCP connection and open a fresh one to the
    /// endpoint the server redirected us to. Transitions Global →
    /// Master or Master → Sector — pick which with
    /// <paramref name="nextStage"/> based on what you just received.
    /// </summary>
    public async Task FollowRedirectAsync(
        ServerRedirect redirect,
        SessionStage nextStage,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(redirect);
        if (nextStage <= Stage)
            throw new ArgumentException(
                $"nextStage {nextStage} must be later than current {Stage}",
                nameof(nextStage));

        await DisconnectCurrentAsync().ConfigureAwait(false);

        _current = await EncryptedTcpConnection.ConnectAsync(
            redirect.ServerEndPoint.Address.ToString(),
            redirect.ServerEndPoint.Port,
            ct).ConfigureAwait(false);
        Stage = nextStage;
    }

    /// <summary>Convenience accessor for the opcode registry the session was built with.</summary>
    public OpcodeRegistry Registry => _registry;

    private async Task DisconnectCurrentAsync()
    {
        if (_current is null) return;
        await _current.DisposeAsync().ConfigureAwait(false);
        _current = null;
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectCurrentAsync().ConfigureAwait(false);
        Stage = SessionStage.Disconnected;
    }
}
