// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya

using N7.CliClient.Logging;

namespace N7.CliClient.Repl.Commands;

/// <summary>
/// <c>narrate-on</c> -- turn on one-line human notices for inbound events
/// (an entity appearing in / leaving scanner range, taking damage, warp
/// progress, loading a new sector, server system messages). On by default;
/// this re-enables it after <c>narrate-off</c>. Independent of <c>dump-on</c>:
/// notices read at the prompt without the verbose per-frame hex tail.
/// Idempotent.
/// </summary>
public sealed class NarrateOnCommand : ICommandHandler
{
    private readonly SessionContext _ctx;

    public NarrateOnCommand(SessionContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        _ctx = ctx;
    }

    public string Name => "narrate-on";
    public string Summary => "narrate inbound events as one-line notices (scanner contacts, damage, warp, sector load)";
    public string Usage => "narrate-on";

    // Only worth offering while narration is off.
    public bool Available => !_ctx.NarrateEnabled;

    public Task<int> ExecuteAsync(
        IReadOnlyList<string> args, TextWriter output, CancellationToken ct)
    {
        _ctx.NarrateEnabled = true;
        // Chat-by-default and narration both ride the sector background drain.
        _ctx.StartSectorDrain();
        output.WriteLine(AnsiPalette.Ok("narrate-on"));
        return Task.FromResult(0);
    }
}
