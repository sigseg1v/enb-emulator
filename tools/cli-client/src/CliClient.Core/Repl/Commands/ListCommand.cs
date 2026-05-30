// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using N7.CliClient.Opcodes.Inbound;

namespace N7.CliClient.Repl.Commands;

/// <summary>
/// <c>list</c> -- reprint the avatar list from the last
/// GlobalAvatarList we saw. Doesn't re-query the server.
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
    public string Summary => "show the cached character list";
    public string Usage   => "list";

    public async Task<int> ExecuteAsync(
        IReadOnlyList<string> args, TextWriter output, CancellationToken ct)
    {
        if (_ctx.AvatarList is null)
        {
            await output.WriteLineAsync("no avatar list yet -- run `login` first").ConfigureAwait(false);
            return 1;
        }
        await PrintAvatarsAsync(_ctx.AvatarList, output).ConfigureAwait(false);
        return 0;
    }

    public static async Task PrintAvatarsAsync(GlobalAvatarList list, TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(list);
        ArgumentNullException.ThrowIfNull(output);

        await output.WriteLineAsync("characters:").ConfigureAwait(false);
        int filled = 0;
        for (int i = 0; i < list.Avatars.Length; i++)
        {
            var slot = list.Avatars[i];
            bool empty = string.IsNullOrEmpty(slot.Data.FirstName) && slot.Info.AccountIdLsb == 0;
            if (empty)
            {
                await output.WriteLineAsync($"  [{i}] <empty>").ConfigureAwait(false);
                continue;
            }
            filled++;
            string race = CharacterClass.RaceName(slot.Data.Race);
            string prof = CharacterClass.ProfessionName(slot.Data.Profession);
            string loc = string.IsNullOrEmpty(slot.Info.Location) ? "?" : slot.Info.Location;
            await output.WriteLineAsync(
                $"  [{i}] {slot.Data.FirstName,-20} {race}/{prof}  " +
                $"sector={slot.Info.SectorId} loc={loc} " +
                $"levels(C/E/T)={slot.Info.CombatLevel}/{slot.Info.ExploreLevel}/{slot.Info.TradeLevel}")
                .ConfigureAwait(false);
        }
        if (list.Galaxies.Length > 0)
        {
            await output.WriteLineAsync($"galaxies: {list.NumGalaxies}").ConfigureAwait(false);
            for (int i = 0; i < list.Galaxies.Length; i++)
            {
                var g = list.Galaxies[i];
                await output.WriteLineAsync(
                    $"  [{i}] {g.Name}  {g.IpAddress}:{g.Port}  players={g.NumPlayers}/{g.MaxPlayers}")
                    .ConfigureAwait(false);
            }
        }
        await output.WriteLineAsync($"({filled}/{list.Avatars.Length} slots filled)")
            .ConfigureAwait(false);
    }
}
