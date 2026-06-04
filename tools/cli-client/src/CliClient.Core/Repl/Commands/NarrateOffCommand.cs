// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using N7.CliClient.Logging;

namespace N7.CliClient.Repl.Commands;

/// <summary>
/// <c>narrate-off</c> -- silence the one-line inbound event notices. Chat echo
/// and the world model keep running; only the narration is muted. Idempotent.
/// </summary>
public sealed class NarrateOffCommand : ICommandHandler
{
    private readonly SessionContext _ctx;

    public NarrateOffCommand(SessionContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        _ctx = ctx;
    }

    public string Name    => "narrate-off";
    public string Summary => "stop narrating inbound events";
    public string Usage   => "narrate-off";

    // Only worth offering while narration is on.
    public bool Available => _ctx.NarrateEnabled;

    public Task<int> ExecuteAsync(
        IReadOnlyList<string> args, TextWriter output, CancellationToken ct)
    {
        _ctx.NarrateEnabled = false;
        output.WriteLine(AnsiPalette.Ok("narrate-off"));
        return Task.FromResult(0);
    }
}
