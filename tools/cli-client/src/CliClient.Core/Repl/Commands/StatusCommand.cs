// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using N7.CliClient.Logging;

namespace N7.CliClient.Repl.Commands;

/// <summary>
/// <c>status</c> -- ask the proxy for its proxy&lt;-&gt;server link state and
/// print whether that leg is connected and DTLS-encrypted. The CLI talks to the
/// proxy, not the server, so this is the only honest source for the encryption
/// state of the proxy&lt;-&gt;server UDP leg (the proxy is what negotiates it).
/// </summary>
public sealed class StatusCommand : ICommandHandler
{
    private readonly SessionContext _ctx;

    public StatusCommand(SessionContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        _ctx = ctx;
    }

    public string Name    => "status";
    public string Summary => "show proxy link state + DTLS encryption of the server leg";
    public string Usage   => "status";

    // Needs the global channel to reach the proxy's status opcode.
    public bool Available => _ctx.Global is not null;

    public async Task<int> ExecuteAsync(
        IReadOnlyList<string> args, TextWriter output, CancellationToken ct)
    {
        if (_ctx.Global is null)
        {
            await output.WriteLineAsync(
                AnsiPalette.Warn("not logged in -- run `login` first")).ConfigureAwait(false);
            return 1;
        }

        var status = await ProxyStatus
            .QueryAsync(_ctx.Global, TimeSpan.FromSeconds(2), ct).ConfigureAwait(false);

        if (status is null)
        {
            await output.WriteLineAsync(AnsiPalette.Warn(
                "proxy did not answer the status request (an older proxy may not support it)"))
                .ConfigureAwait(false);
            await output.WriteLineAsync(ProxyStatus.DtlsLine(null)).ConfigureAwait(false);
            return 1;
        }

        await output.WriteLineAsync(
            AnsiPalette.Muted("proxy<->server: ") +
            (status.Connected ? AnsiPalette.Ok("connected") : AnsiPalette.Err("no link")) + " " +
            AnsiPalette.Muted("live-dtls-assocs=") + AnsiPalette.Value($"{status.LiveDtlsAssociations}"))
            .ConfigureAwait(false);
        await output.WriteLineAsync(ProxyStatus.DtlsLine(status)).ConfigureAwait(false);
        return 0;
    }
}
