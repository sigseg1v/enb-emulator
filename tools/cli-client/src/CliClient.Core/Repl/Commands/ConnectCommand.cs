// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Net.Sockets;

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
    public string Usage   =>
        "connect <host>[:auth-port]\n" +
        "  default auth-port: 4443 (docker dev stack)\n" +
        "  example: connect localhost          (uses 4443)\n" +
        "  example: connect 127.0.0.1:4443";

    public async Task<int> ExecuteAsync(
        IReadOnlyList<string> args, TextWriter output, CancellationToken ct)
    {
        if (args.Count < 1)
        {
            await output.WriteLineAsync("usage: connect <host>[:auth-port]").ConfigureAwait(false);
            return 1;
        }

        string raw = args[0];
        string host;
        int? port = null;
        int colon = raw.LastIndexOf(':');
        if (colon > 0 && colon < raw.Length - 1)
        {
            host = raw[..colon];
            if (!int.TryParse(raw[(colon + 1)..], out int p) || p <= 0 || p > 65535)
            {
                await output.WriteLineAsync($"bad port: {raw[(colon + 1)..]}").ConfigureAwait(false);
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

        await output.WriteLineAsync(
            $"target: auth={_ctx.Host}:{_ctx.AuthPort} global={_ctx.Host}:{_ctx.GlobalPort} " +
            $"master={_ctx.Host}:{_ctx.MasterPort} sector={_ctx.Host}:{_ctx.SectorPort}")
            .ConfigureAwait(false);

        try
        {
            using var tcp = new TcpClient();
            await tcp.ConnectAsync(_ctx.Host, _ctx.AuthPort, ct).ConfigureAwait(false);
            await output.WriteLineAsync(
                $"probe: {_ctx.Host}:{_ctx.AuthPort} accepting TCP").ConfigureAwait(false);
            return 0;
        }
        catch (Exception ex)
        {
            await output.WriteLineAsync(
                $"probe failed: {_ctx.Host}:{_ctx.AuthPort} -- {ex.Message}").ConfigureAwait(false);
            return 1;
        }
    }
}
