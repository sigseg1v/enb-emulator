// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using N7.CliClient.Logging;
using N7.CliClient.Opcodes.Inbound;

namespace N7.CliClient.Repl.Commands;

/// <summary>
/// <c>list</c> -- context-aware. In a sector it re-dumps the live
/// <see cref="SectorWorld"/> (nearby objects + own position); otherwise it
/// reprints the cached character list from the last GlobalAvatarList. Never
/// re-queries the server -- both views are recomputed from state we hold.
/// </summary>
public sealed class ListCommand : ICommandHandler
{
    private readonly SessionContext _ctx;

    public ListCommand(SessionContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        _ctx = ctx;
    }

    public string Name    => "list";
    public string Summary => "in-sector: list nearby objects; else: cached characters";
    public string Usage   => "list";

    // Useful once logged in (character list) or in a sector (nearby).
    public bool Available => _ctx.AvatarList is not null || _ctx.Sector is not null;

    public async Task<int> ExecuteAsync(
        IReadOnlyList<string> args, TextWriter output, CancellationToken ct)
    {
        // In-sector: re-compute and dump the nearby world model.
        if (_ctx.Sector is not null && _ctx.GameId is { } selfId)
        {
            _ctx.World.Render(output, selfId);
            return 0;
        }

        if (_ctx.AvatarList is null)
        {
            await output.WriteLineAsync(
                AnsiPalette.Warn("no avatar list yet -- run `login` first")).ConfigureAwait(false);
            return 1;
        }
        await PrintAvatarsAsync(_ctx.AvatarList, output).ConfigureAwait(false);
        return 0;
    }

    public static async Task PrintAvatarsAsync(GlobalAvatarList list, TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(list);
        ArgumentNullException.ThrowIfNull(output);

        await output.WriteLineAsync(AnsiPalette.Head("characters:")).ConfigureAwait(false);
        int filled = 0;
        for (int i = 0; i < list.Avatars.Length; i++)
        {
            var slot = list.Avatars[i];
            bool empty = string.IsNullOrEmpty(slot.Data.FirstName) && slot.Info.AccountIdLsb == 0;
            if (empty)
            {
                await output.WriteLineAsync(
                    AnsiPalette.Muted($"  [{i}] <empty>")).ConfigureAwait(false);
                continue;
            }
            filled++;
            string race = CharacterClass.RaceName(slot.Data.Race);
            string prof = CharacterClass.ProfessionName(slot.Data.Profession);
            string loc = string.IsNullOrEmpty(slot.Info.Location) ? "?" : slot.Info.Location;
            await output.WriteLineAsync(
                "  " + AnsiPalette.Muted($"[{i}]") + " " +
                AnsiPalette.Accent($"{slot.Data.FirstName,-20}") + " " +
                AnsiPalette.Info($"{race}/{prof}") + "  " +
                AnsiPalette.Muted($"sector={slot.Info.SectorId} loc={loc} ") +
                AnsiPalette.Value(
                    $"levels(C/E/T)={slot.Info.CombatLevel}/{slot.Info.ExploreLevel}/{slot.Info.TradeLevel}"))
                .ConfigureAwait(false);
        }
        if (list.Galaxies.Length > 0)
        {
            await output.WriteLineAsync(
                AnsiPalette.Head($"galaxies: {list.NumGalaxies}")).ConfigureAwait(false);
            for (int i = 0; i < list.Galaxies.Length; i++)
            {
                var g = list.Galaxies[i];
                await output.WriteLineAsync(
                    "  " + AnsiPalette.Muted($"[{i}]") + " " +
                    AnsiPalette.Accent(g.Name) + "  " +
                    AnsiPalette.Value($"{g.IpAddress}:{g.Port}") + "  " +
                    AnsiPalette.Muted($"players={g.NumPlayers}/{g.MaxPlayers}"))
                    .ConfigureAwait(false);
            }
        }
        await output.WriteLineAsync(
            AnsiPalette.Muted($"({filled}/{list.Avatars.Length} slots filled)"))
            .ConfigureAwait(false);
    }
}
