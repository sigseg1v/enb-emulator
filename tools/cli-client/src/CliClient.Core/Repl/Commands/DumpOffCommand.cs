// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

namespace N7.CliClient.Repl.Commands;

/// <summary>
/// <c>dump-off</c> -- stop printing live packets and cancel the
/// background drain started by <c>dump-on</c>. Idempotent.
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
    public string Summary => "stop the live packet tail";
    public string Usage   => "dump-off";

    public async Task<int> ExecuteAsync(
        IReadOnlyList<string> args, TextWriter output, CancellationToken ct)
    {
        _ctx.DumpEnabled = false;
        await _ctx.StopDumpDrainAsync().ConfigureAwait(false);
        await output.WriteLineAsync("dump-off").ConfigureAwait(false);
        return 0;
    }
}
