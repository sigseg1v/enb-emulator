// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

namespace N7.CliClient.Repl.Commands;

/// <summary>
/// <c>create &lt;class&gt; &lt;name&gt;</c> -- send GlobalCreateCharacter
/// on the first empty avatar slot. Class is a two-letter code
/// (race + profession): TW TT TE JW JT JE PW PT PE.
/// </summary>
public sealed class CreateCommand : ICommandHandler
{
    private readonly SessionContext _ctx;

    public CreateCommand(SessionContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        _ctx = ctx;
    }

    public string Name    => "create";
    public string Summary => "create a character in the first empty slot";
    public string Usage   =>
        "create [character] <class> <firstname>\n" +
        "  class: 2-letter race+profession code\n" +
        "    races:        T=Terran  J=Jenquai  P=Progen\n" +
        "    professions:  W=Warrior T=Trader   E=Explorer\n" +
        "  example: create JE Griever\n" +
        "  example: create character JE Griever";

    public async Task<int> ExecuteAsync(
        IReadOnlyList<string> args, TextWriter output, CancellationToken ct)
    {
        if (_ctx.Global is null || _ctx.AvatarList is null || _ctx.Username is null)
        {
            await output.WriteLineAsync("not logged in -- run `login` first").ConfigureAwait(false);
            return 1;
        }

        // Allow the example phrasing `create character JE Griever` by
        // dropping a leading "character" literal.
        int idx = 0;
        if (args.Count > 0 && string.Equals(args[0], "character", StringComparison.OrdinalIgnoreCase))
            idx = 1;

        if (args.Count - idx < 2)
        {
            await output.WriteLineAsync("usage: create [character] <class> <firstname>").ConfigureAwait(false);
            return 1;
        }

        if (!CharacterClass.TryParseCode(args[idx], out int race, out int profession))
        {
            await output.WriteLineAsync($"bad class code '{args[idx]}' (try JE, TW, PT, ...)").ConfigureAwait(false);
            return 1;
        }

        string firstName = args[idx + 1];
        if (firstName.Length == 0 || firstName.Length > 19)
        {
            await output.WriteLineAsync("firstname must be 1-19 ASCII chars").ConfigureAwait(false);
            return 1;
        }

        int slot = -1;
        for (int i = 0; i < _ctx.AvatarList.Avatars.Length; i++)
        {
            var s = _ctx.AvatarList.Avatars[i];
            if (string.IsNullOrEmpty(s.Data.FirstName) && s.Info.AccountIdLsb == 0)
            {
                slot = i;
                break;
            }
        }
        if (slot < 0)
        {
            await output.WriteLineAsync("no empty slots -- delete a character first").ConfigureAwait(false);
            return 1;
        }

        string shipName = firstName + "'s Ship";
        if (shipName.Length > 25) shipName = firstName;

        await output.WriteLineAsync(
            $"create: slot={slot} class={CharacterClass.RaceName(race)} {CharacterClass.ProfessionName(profession)} " +
            $"name='{firstName}' ship='{shipName}'")
            .ConfigureAwait(false);

        try
        {
            var avatars = await SectorEnterDriver.CreateCharacterOnSlotAsync(
                _ctx.Global,
                _ctx.Username,
                slot,
                firstName,
                race,
                profession,
                gender: 0,
                shipName,
                ct).ConfigureAwait(false);

            _ctx.AvatarList = avatars;
            await ListCommand.PrintAvatarsAsync(avatars, output).ConfigureAwait(false);
            return 0;
        }
        catch (Exception ex)
        {
            await output.WriteLineAsync($"create failed: {ex.Message}").ConfigureAwait(false);
            return 1;
        }
    }
}
