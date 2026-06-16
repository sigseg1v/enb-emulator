// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya

using System.Globalization;
using N7.CliClient.Logging;

namespace N7.CliClient.Repl.Commands;

/// <summary>
/// <c>navs</c> -- list the navigable destinations the server has revealed in
/// the current sector (discovered/visible navs, stargates, and stations),
/// nearest first. These are exactly the targets <c>warp</c>, <c>gate</c>, and
/// <c>dock</c> accept by name, so this is how the player finds a warp/gate/dock
/// target. Recomputed from the live <see cref="SectorWorld"/> -- never
/// re-queries the server.
/// </summary>
public sealed class NavsCommand : ICommandHandler
{
    private readonly SessionContext _ctx;

    public NavsCommand(SessionContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        _ctx = ctx;
    }

    public string Name => "navs";
    public string Summary => "list navs, gates, and stations in this sector (warp/gate/dock targets)";
    public string Usage =>
        "navs\n" +
        "  Lists every navigable destination the server has shown this sector --\n" +
        "  discovered/visible navs, stargates, and stations -- nearest first, with\n" +
        "  its gid and distance. These are the names `warp`, `gate`, and `dock`\n" +
        "  accept (and Tab-complete).";
    public string? Placeholder => null;
    public bool Available => _ctx.Sector is not null && _ctx.GameId is not null;

    public async Task<int> ExecuteAsync(IReadOnlyList<string> args, TextWriter output, CancellationToken ct)
    {
        if (_ctx.Sector is null || _ctx.GameId is not { } id)
        {
            await output.WriteLineAsync(AnsiPalette.Warn("not in a sector -- run `enter` first")).ConfigureAwait(false);
            return 1;
        }

        var targets = _ctx.World.NavTargets(id);
        string where = _ctx.ActiveSectorId is { } sid ? _ctx.SectorLabel(sid) : "this sector";
        await output.WriteLineAsync(
            AnsiPalette.Head($"nav targets in {where}: {targets.Count}") +
            AnsiPalette.Muted("  (warp/gate/dock targets, nearest first)")).ConfigureAwait(false);

        if (targets.Count == 0)
        {
            await output.WriteLineAsync(AnsiPalette.Muted(
                "  (none tracked yet -- fly around or wait for the sector fanout)")).ConfigureAwait(false);
            return 0;
        }

        foreach (var t in targets)
        {
            string distStr = t.Dist is { } d ? $"d={d:0.0}" : "d=?";
            // Only navs carry a visited/discovered state; gates/stations don't.
            string visited = t.IsNav
                ? (t.Visited == true ? " visited" : " unvisited")
                : "";
            string radar = t.OnRadar == false ? " off-radar" : "";
            await output.WriteLineAsync(
                "  " + AnsiPalette.Info($"{t.Kind,-18}") + " " +
                AnsiPalette.Accent($"{t.Name,-28}") + " " +
                AnsiPalette.Muted($"0x{t.GameId:X8}  ") +
                AnsiPalette.Value($"{distStr,-12}") +
                AnsiPalette.Muted(visited + radar)).ConfigureAwait(false);
        }
        return 0;
    }
}
