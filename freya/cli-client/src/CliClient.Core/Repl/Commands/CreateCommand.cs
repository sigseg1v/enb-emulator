// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya

using N7.CliClient.Logging;

namespace N7.CliClient.Repl.Commands;

/// <summary>
/// <c>create &lt;class&gt; &lt;name&gt;</c> -- send GlobalCreateCharacter
/// on the first empty avatar slot. Class is one of the nine real EnB
/// class codes: TE TT TS / JD JS JE / PW PP PS.
/// </summary>
public sealed class CreateCommand : ICommandHandler
{
    private readonly SessionContext _ctx;

    public CreateCommand(SessionContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        _ctx = ctx;
    }

    public string Name => "create";
    public string Summary => "create a character in the first empty slot";
    public string Usage =>
        "create [character] <class> <firstname>\n" +
        "  class: 2-letter EnB class code\n" +
        "    Terran:   TE=Enforcer  TT=Trader   TS=Scout\n" +
        "    Jenquai:  JD=Defender  JS=Seeker   JE=Explorer\n" +
        "    Progen:   PW=Warrior   PP=Privateer PS=Sentinel\n" +
        "  example: create JE Griever\n" +
        "  example: create character JE Griever";
    public string? Placeholder => "<class> <firstname>";

    // Available once logged in. When the account has NO characters yet, creating
    // one is the obvious next step so `create` leads the suggestions; once there
    // are characters, `enter` leads and `create` drops behind it. The mirror of
    // EnterCommand's priority -- the two never tie.
    public bool Available => _ctx.Global is not null && _ctx.AvatarList is not null;
    public int Priority => HasCharacters ? 90 : 110;

    private bool HasCharacters =>
        _ctx.AvatarList is not null &&
        _ctx.AvatarList.Avatars.Any(a => !string.IsNullOrEmpty(a.Data.FirstName));

    public async Task<int> ExecuteAsync(
        IReadOnlyList<string> args, TextWriter output, CancellationToken ct)
    {
        if (_ctx.Global is null || _ctx.AvatarList is null || _ctx.Username is null)
        {
            await output.WriteLineAsync(
                AnsiPalette.Warn("not logged in -- run `login` first")).ConfigureAwait(false);
            return 1;
        }

        // Allow the example phrasing `create character JE Griever` by
        // dropping a leading "character" literal.
        int idx = 0;
        if (args.Count > 0 && string.Equals(args[0], "character", StringComparison.OrdinalIgnoreCase))
            idx = 1;

        if (args.Count - idx < 2)
        {
            await output.WriteLineAsync(
                AnsiPalette.Warn("usage: create [character] <class> <firstname>")).ConfigureAwait(false);
            return 1;
        }

        if (!CharacterClass.TryParseCode(args[idx], out int race, out int profession))
        {
            await output.WriteLineAsync(
                AnsiPalette.Err($"bad class code '{args[idx]}' (try JE, TE, PW, ...)")).ConfigureAwait(false);
            return 1;
        }

        string firstName = args[idx + 1];
        // Mirror the server's hard length bound (AccountManager::CreateCharacter
        // rejects < 3 as G_ERROR_TOO_SHORT) so an obvious miss fails instantly
        // instead of after a round-trip. The vowel / repeating-char / forbidden
        // rules stay server-authoritative (surfaced via the GlobalError text).
        if (firstName.Length < 3 || firstName.Length > 19)
        {
            await output.WriteLineAsync(
                AnsiPalette.Err("firstname must be 3-19 ASCII chars")).ConfigureAwait(false);
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
            await output.WriteLineAsync(
                AnsiPalette.Warn("no empty slots -- delete a character first")).ConfigureAwait(false);
            return 1;
        }

        string shipName = firstName + "'s Ship";
        if (shipName.Length > 25) shipName = firstName;

        await output.WriteLineAsync(
            AnsiPalette.Muted("create: ") +
            AnsiPalette.Muted($"slot={slot} ") +
            AnsiPalette.Info($"{CharacterClass.RaceName(race)} {CharacterClass.ClassName(race, profession)} ({CharacterClass.ClassCode(race, profession)})") + " " +
            AnsiPalette.Muted("name=") + AnsiPalette.Accent($"'{firstName}'") + " " +
            AnsiPalette.Muted($"ship='{shipName}'"))
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
            await ListCommand.PrintAvatarsAsync(_ctx, avatars, output).ConfigureAwait(false);
            return 0;
        }
        catch (Exception ex)
        {
            await output.WriteLineAsync(
                AnsiPalette.Err($"create failed: {ex.Message}")).ConfigureAwait(false);
            return 1;
        }
    }
}
