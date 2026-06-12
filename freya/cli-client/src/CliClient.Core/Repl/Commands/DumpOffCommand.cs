// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya

using N7.CliClient.Logging;

namespace N7.CliClient.Repl.Commands;

/// <summary>
/// <c>dump-off</c> -- stop printing live packets. The background sector
/// drain keeps running so chat still echoes at the prompt; only the
/// verbose per-frame dump is silenced. Idempotent.
/// </summary>
public sealed class DumpOffCommand : ICommandHandler
{
    private readonly SessionContext _ctx;

    public DumpOffCommand(SessionContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        _ctx = ctx;
    }

    public string Name    => "dump-off";
    public string Summary => "stop the live packet tail (chat still echoes)";
    public string Usage   => "dump-off";

    // Only worth offering while a dump is actually running.
    public bool Available => _ctx.DumpEnabled;

    public async Task<int> ExecuteAsync(
        IReadOnlyList<string> args, TextWriter output, CancellationToken ct)
    {
        // Leave the sector drain running -- chat-by-default depends on it.
        _ctx.DumpEnabled = false;
        await output.WriteLineAsync(AnsiPalette.Ok("dump-off")).ConfigureAwait(false);
        return 0;
    }
}
