// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using N7.CliClient.Auth;
using N7.CliClient.Logging;
using N7.CliClient.Net;
using N7.CliClient.Opcodes;
using N7.CliClient.Opcodes.Inbound;
using N7.CliClient.Session;

namespace N7.CliClient.Workflows;

/// <summary>
/// End-to-end smoke workflow: TLS login → global TCP connect → drain
/// inbound packets for a fixed idle period → clean disconnect. The
/// dev-stack integration test in <c>tests/integration/</c> runs this
/// against <c>docker compose up</c> and asserts it ends in
/// <see cref="ConnectAndLoginResult.Stage"/> = Global (or whatever the
/// server advances us to during the idle window — Master if a redirect
/// arrives, etc.).
/// </summary>
/// <remarks>
/// <para>
/// What this workflow does NOT do: send <c>0x006D GlobalConnect</c>,
/// follow ServerRedirects, send any opcodes at all. It's the
/// minimum-viable "can the client + server even talk" path. Sending
/// in-game opcodes is covered by Items 10-13.
/// </para>
/// <para>
/// Hard-rule compliance: the workflow takes a <see cref="HealthGuard"/>;
/// every send/receive is gated by <c>guard.Token</c>; an early server
/// disconnect or rate spike aborts cleanly with the guard's reason
/// surfaced in <see cref="ConnectAndLoginResult.Aborted"/>.
/// </para>
/// </remarks>
public sealed class ConnectAndLogin
{
    private readonly ConnectAndLoginOptions _options;
    private readonly OpcodeRegistry _registry;
    private readonly PacketLog? _packetLog;
    private readonly ConsoleSink? _console;

    public ConnectAndLogin(
        ConnectAndLoginOptions options,
        OpcodeRegistry registry,
        PacketLog? packetLog = null,
        ConsoleSink? console = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(registry);
        _options = options;
        _registry = registry;
        _packetLog = packetLog;
        _console = console;
    }

    public async Task<ConnectAndLoginResult> RunAsync(
        HealthGuard guard,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(guard);

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, guard.Token);
        var token = linked.Token;

        _console?.Info($"login: {_options.Username}@{_options.LoginHost}:{_options.LoginPort}");

        AuthLoginResponse login;
        try
        {
            var auth = new AuthLoginClient(
                _options.LoginHost,
                _options.LoginPort,
                acceptUntrustedCertificates: _options.AcceptUntrustedLoginCertificate,
                diagnostics: msg => _console?.Info($"[auth] {msg}"));

            login = await auth.LoginAsync(
                new AuthLoginRequest(_options.Username, _options.Password),
                token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            guard.Trip($"login transport error: {ex.Message}");
            return new ConnectAndLoginResult(
                LoginValid: false,
                Ticket: null,
                Stage: SessionStage.Disconnected,
                InboundPackets: 0,
                Aborted: guard.Reason);
        }

        if (!login.Valid || string.IsNullOrEmpty(login.Ticket))
        {
            _console?.Info($"login: rejected (Valid={login.Valid})");
            return new ConnectAndLoginResult(
                LoginValid: false,
                Ticket: null,
                Stage: SessionStage.Disconnected,
                InboundPackets: 0,
                Aborted: "login server returned Valid=False");
        }

        _console?.Info($"login: ok, ticket length={login.Ticket.Length}");

        await using var session = new CliSession(_registry, login.Ticket);

        try
        {
            await session.ConnectGlobalAsync(
                _options.GlobalHost, _options.GlobalPort, token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            guard.Trip($"global connect failed: {ex.Message}");
            return new ConnectAndLoginResult(
                LoginValid: true,
                Ticket: login.Ticket,
                Stage: SessionStage.Authenticated,
                InboundPackets: 0,
                Aborted: guard.Reason);
        }

        _console?.Info($"global: connected to {_options.GlobalHost}:{_options.GlobalPort}");

        int inboundCount = await DrainInboundAsync(
            session, guard, _options.IdleDuration, token).ConfigureAwait(false);

        _console?.Info($"idle: drained {inboundCount} packets in {_options.IdleDuration.TotalSeconds:0.#}s");

        return new ConnectAndLoginResult(
            LoginValid: true,
            Ticket: login.Ticket,
            Stage: session.Stage,
            InboundPackets: inboundCount,
            Aborted: guard.Tripped ? guard.Reason : null);
    }

    private async Task<int> DrainInboundAsync(
        CliSession session,
        HealthGuard guard,
        TimeSpan idle,
        CancellationToken ct)
    {
        int count = 0;
        using var idleCts = new CancellationTokenSource(idle);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, idleCts.Token);

        try
        {
            while (!linked.IsCancellationRequested)
            {
                Packet? p;
                try
                {
                    p = await session.ReceiveAsync(linked.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    guard.Trip($"receive failed: {ex.Message}");
                    break;
                }

                if (p is null)
                {
                    guard.OnDisconnect("server closed connection during idle");
                    break;
                }

                count++;
                guard.OnPacketReceived(p);
                _packetLog?.Log(PacketDirection.Inbound, p, decoded: TryDecode(p));
            }
        }
        catch (OperationCanceledException)
        {
            // expected on idle timeout
        }

        return count;
    }

    private object? TryDecode(Packet p)
    {
        try
        {
            var codec = _registry.Resolve(new OpcodeId(p.Header.Opcode));
            if (codec is UnknownOpcodeCodec) return null;
            return codec.DecodeInbound(p.Payload.Span);
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>Outcome of a <see cref="ConnectAndLogin"/> run.</summary>
public sealed record ConnectAndLoginResult(
    bool LoginValid,
    string? Ticket,
    SessionStage Stage,
    int InboundPackets,
    string? Aborted);
