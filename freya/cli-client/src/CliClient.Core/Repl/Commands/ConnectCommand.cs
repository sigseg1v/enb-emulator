// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya

using System.Net.Sockets;
using N7.CliClient.Logging;

namespace N7.CliClient.Repl.Commands;

/// <summary>
/// Set the host (and optional auth-server port) the REPL talks to, then
/// probe that endpoint to confirm it accepts TCP. Doesn't open a
/// session -- <c>login</c> does that.
/// </summary>
public sealed class ConnectCommand : ICommandHandler
{
    private readonly SessionContext _ctx;

    public ConnectCommand(SessionContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        _ctx = ctx;
    }

    public string Name    => "connect";
    public string Summary => "set host[:auth-port] and probe TCP";

    // Reflect the live default host so Tab / right-arrow fill the value that
    // actually works here: 127.0.0.1 on the host stack, the proxy alias
    // (cliproxy) inside the docker network (seeded from N7_PROXY_HOST).
    public string? Placeholder => $"<host:{_ctx.Host}>";

    // The entry point, and the obvious first action -> leads the suggestions.
    // Once a probe has connected, the next step is login, so connect retires.
    public bool Available => !_ctx.Connected;
    public int Priority => 100;
    public string Usage   =>
        "connect [host[:auth-port]]\n" +
        "  no host: probe the current default (" + _ctx.Host + ")\n" +
        "  default auth-port: " + _ctx.AuthPort + "\n" +
        "  example: connect localhost\n" +
        "  example: connect 127.0.0.1:4443";

    public async Task<int> ExecuteAsync(
        IReadOnlyList<string> args, TextWriter output, CancellationToken ct)
    {
        // A bare `connect` probes the current default host (127.0.0.1, or the
        // proxy alias inside docker) -- no need to retype it every session.
        if (args.Count >= 1)
        {
            string raw = args[0];
            string host;
            int? port = null;
            int colon = raw.LastIndexOf(':');
            if (colon > 0 && colon < raw.Length - 1)
            {
                host = raw[..colon];
                if (!int.TryParse(raw[(colon + 1)..], out int p) || p <= 0 || p > 65535)
                {
                    await output.WriteLineAsync(
                        AnsiPalette.Err($"bad port: {raw[(colon + 1)..]}")).ConfigureAwait(false);
                    return 1;
                }
                port = p;
            }
            else
            {
                host = raw;
            }

            _ctx.Host = host;
            if (port.HasValue) _ctx.AuthPort = port.Value;
        }

        await output.WriteLineAsync(
            AnsiPalette.Muted("target: ") +
            $"auth={AnsiPalette.Value($"{_ctx.EffectiveAuthHost}:{_ctx.AuthPort}")} " +
            $"global={AnsiPalette.Value($"{_ctx.Host}:{_ctx.GlobalPort}")} " +
            $"master={AnsiPalette.Value($"{_ctx.Host}:{_ctx.MasterPort}")} " +
            $"sector={AnsiPalette.Value($"{_ctx.Host}:{_ctx.SectorPort}")}")
            .ConfigureAwait(false);

        try
        {
            using var tcp = new TcpClient();
            await tcp.ConnectAsync(_ctx.EffectiveAuthHost, _ctx.AuthPort, ct).ConfigureAwait(false);
            _ctx.Connected = true;
            await output.WriteLineAsync(
                AnsiPalette.Ok($"probe: {_ctx.EffectiveAuthHost}:{_ctx.AuthPort} accepting TCP"))
                .ConfigureAwait(false);
            return 0;
        }
        catch (Exception ex)
        {
            await output.WriteLineAsync(
                AnsiPalette.Err($"probe failed: {_ctx.EffectiveAuthHost}:{_ctx.AuthPort} -- {ex.Message}"))
                .ConfigureAwait(false);
            return 1;
        }
    }
}
