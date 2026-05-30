// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

namespace N7.CliClient.Repl;

/// <summary>
/// Race / profession lookup used by <c>create</c>. Values mirror the
/// retail-server tables in <c>archive/kyp-snapshot/linux-port-legacy/StaticData.h</c>:
/// <c>StartSector[race*3+profession]</c> is the station the avatar
/// spawns in on first login.
/// </summary>
public static class CharacterClass
{
    public const int RaceTerran   = 0;
    public const int RaceJenquai  = 1;
    public const int RaceProgen   = 2;

    public const int ProfWarrior  = 0;
    public const int ProfTrader   = 1;
    public const int ProfExplorer = 2;

    private static readonly int[] StartSectorTable =
    {
        // Terran                  Jenquai                  Progen
        10151, 10201, 10251,       10551, 10401, 10521,     10361, 10371, 10301,
    };

    public static int StartSector(int race, int profession)
    {
        int index = race * 3 + profession;
        if (index < 0 || index >= StartSectorTable.Length)
            throw new ArgumentOutOfRangeException(
                nameof(profession), $"invalid race={race}, profession={profession}");
        return StartSectorTable[index];
    }

    public static string RaceName(int race) => race switch
    {
        0 => "Terran",
        1 => "Jenquai",
        2 => "Progen",
        _ => $"Race({race})",
    };

    public static string ProfessionName(int profession) => profession switch
    {
        0 => "Warrior",
        1 => "Trader",
        2 => "Explorer",
        _ => $"Prof({profession})",
    };

    /// <summary>
    /// Parse a two-letter class code (e.g. "JE" = Jenquai Explorer).
    /// First char is race (T/J/P), second is profession (W/T/E).
    /// </summary>
    public static bool TryParseCode(string code, out int race, out int profession)
    {
        race = -1;
        profession = -1;
        if (string.IsNullOrEmpty(code) || code.Length != 2) return false;
        char r = char.ToUpperInvariant(code[0]);
        char p = char.ToUpperInvariant(code[1]);
        race = r switch
        {
            'T' => RaceTerran,
            'J' => RaceJenquai,
            'P' => RaceProgen,
            _ => -1,
        };
        profession = p switch
        {
            'W' => ProfWarrior,
            'T' => ProfTrader,
            'E' => ProfExplorer,
            _ => -1,
        };
        return race >= 0 && profession >= 0;
    }
}
