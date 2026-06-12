// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya

namespace N7.CliClient.Repl;

/// <summary>
/// Race / profession lookup used by <c>create</c>. Values mirror
/// <c>server/src/StaticData.h</c>: <c>StartSector[race*3+profession]</c>
/// is the SPACE sector the avatar spawns in on first login. All IDs
/// are &lt; 10000 so the server takes the SectorLogin path
/// (StationLogin is gated on <c>m_SectorID &gt; 9999</c>).
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
        // Terran               Jenquai             Progen
        1015, 1020, 1025,       1055, 1040, 1052,   1036, 1037, 1030,
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

    // The nine EnB classes. ClassIndex = race*3 + profession (server Net7.h).
    // The profession SLOT is an archetype (0=warrior/tank, 1=trader,
    // 2=explorer/scout) but the human-facing class NAME is race-specific, so
    // the code's second letter is the actual class initial, NOT the archetype.
    // These are the real in-game class names and the real two-letter codes the
    // server emits (server/src/UDP_Global.cpp kClassAbbrev) -- there is no "TW"
    // or "JW": Terran warrior = Enforcer = TE, Jenquai warrior = Defender = JD.
    private static readonly (string Name, string Code)[] ClassTable =
    {
        // Terran                  Jenquai                  Progen
        ("Enforcer",  "TE"), ("Trader",  "TT"), ("Scout",     "TS"),
        ("Defender",  "JD"), ("Seeker",  "JS"), ("Explorer",  "JE"),
        ("Warrior",   "PW"), ("Privateer","PP"), ("Sentinel", "PS"),
    };

    /// <summary>The race-specific class name, e.g. (Jenquai, warrior slot) =&gt; "Defender".</summary>
    public static string ClassName(int race, int profession)
    {
        int index = race * 3 + profession;
        if (index < 0 || index >= ClassTable.Length) return $"Class({race},{profession})";
        return ClassTable[index].Name;
    }

    /// <summary>The two-letter class code, e.g. (Jenquai, warrior slot) =&gt; "JD".</summary>
    public static string ClassCode(int race, int profession)
    {
        int index = race * 3 + profession;
        if (index < 0 || index >= ClassTable.Length) return "??";
        return ClassTable[index].Code;
    }

    /// <summary>
    /// Parse one of the nine real class codes (TE TT TS / JD JS JE / PW PP PS)
    /// into its race and profession slot. Case-insensitive.
    /// </summary>
    public static bool TryParseCode(string code, out int race, out int profession)
    {
        race = -1;
        profession = -1;
        if (string.IsNullOrEmpty(code) || code.Length != 2) return false;
        string upper = code.ToUpperInvariant();
        for (int i = 0; i < ClassTable.Length; i++)
        {
            if (ClassTable[i].Code == upper)
            {
                race = i / 3;
                profession = i % 3;
                return true;
            }
        }
        return false;
    }
}
