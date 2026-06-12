// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya

namespace N7.CliClient.Repl;

/// <summary>
/// Static sector-id -> name lookup so the CLI can show a human-readable sector
/// name alongside the raw id everywhere a sector is mentioned (the prompt, the
/// character list, gate/undock/dock handoff lines).
/// </summary>
/// <remarks>
/// <para>
/// The names mirror the server content DB's <c>sectors</c> table
/// (<c>net7.sectors.name</c>), the same authoritative source the server reads
/// at runtime -- a baked snapshot of that game content for display only.
/// </para>
/// <para>
/// Every entry is a SPACE sector (id &lt;= 9999). Station-interior sectors
/// (id &gt; 9999, derived at runtime as <c>parent*10 + n</c>) are deliberately
/// absent: they are not rows in the table. While docked, the current sector's
/// name comes from the live 0x0097 GALAXY_MAP Type-4 frame the server sends on
/// arrival (captured in <see cref="SessionContext"/>), so a docked prompt still
/// reads the station's name even though it is not in this table.
/// </para>
/// </remarks>
public static class SectorCatalog
{
    /// <summary>Sector id -> display name, from the content DB's sectors table.</summary>
    public static readonly IReadOnlyDictionary<int, string> Names = new Dictionary<int, string>
    {
        [973] = "Earth Basic Training Yard",
        [974] = "Mars Basic Training Yard",
        [978] = "Callisto Basic Training Yard",
        [1005] = "Mercury",
        [1010] = "Venus",
        [1015] = "Luna",
        [1020] = "High Earth",
        [1025] = "Equatorial Earth",
        [1030] = "Mars Beta",
        [1036] = "Mars Alpha",
        [1037] = "Mars Gamma",
        [1040] = "Ganymede",
        [1052] = "Io",
        [1055] = "Europa",
        [1060] = "Earth",
        [1065] = "Mars",
        [1069] = "Nebulous",
        [1070] = "Jupiter",
        [1071] = "Saturn",
        [1072] = "Uranus",
        [1073] = "Neptune",
        [1074] = "Pluto and Charon",
        [1075] = "Akerons Gate",
        [1076] = "Asteroid Belt Alpha",
        [1077] = "Asteroid Belt Beta",
        [1078] = "Ceres/Thule ",
        [1079] = "Asteroid Belt Gamma",
        [1105] = "Ogun",
        [1110] = "Zuri Maji",
        [1115] = "Aganju",
        [1120] = "Thelugi Rift",
        [1125] = "Eshu",
        [1130] = "Bayanni",
        [1150] = "Menog Swarm",
        [1155] = "Oya",
        [1160] = "Moto",
        [1165] = "Oricha Nla",
        [1170] = "Yemaha",
        [1205] = "Margesi",
        [1215] = "Adriel Prime",
        [1290] = "Loch Mar",
        [1305] = "Nostrand Vor",
        [1309] = "Nostrand Vor Planet",
        [1310] = "Altair III",
        [1425] = "Zweihander",
        [1430] = "Witberg",
        [1440] = "der Todesengel",
        [1493] = "Jagerstadt",
        [1505] = "Antares Frontier",
        [1510] = "Charis Duo",
        [1515] = "Kasdreya's Weil",
        [1520] = "Kor Scorpius",
        [1550] = "Volundar Gap",
        [1580] = "Kor Vanaan",
        [1590] = "Kor Vanaan Planet",
        [1591] = "Kokoyuko Moon",
        [1705] = "Valkyrie Twins",
        [1710] = "Aragoth Prime",
        [1715] = "Varens Girdle",
        [1720] = "Odins Belt",
        [1725] = "Muspelheim",
        [1730] = "Fenris",
        [1735] = "Odin Rex",
        [1740] = "Jotunheim",
        [1745] = "Ragnarok",
        [1750] = "Freya",
        [1755] = "Nifleheim Cloud",
        [1910] = "Kailaasa",
        [1915] = "Kitara's Veil",
        [1920] = "Yokan",
        [1925] = "Dahin",
        [1930] = "Dahin Mining Interest",
        [1935] = "Vishao's Cove",
        [2005] = "Ishuan",
        [2008] = "Ishuan Gas Giant",
        [2009] = "Sandcastle",
        [2010] = "Menorb",
        [2014] = "Menorb Planet",
        [2015] = "Nebiros",
        [2019] = "Nebiros Planet",
        [2105] = "Roc",
        [2130] = "Roc 1",
        [2210] = "Endriago",
        [2215] = "Lagarto",
        [2290] = "GETCo Ruins",
        [2295] = "Porvenir Mons Area",
        [2305] = "Cassiopeia's Belt",
        [2310] = "Chandilar",
        [2315] = "Achenar",
        [2319] = "Chandilar Planet",
        [2405] = "Sulani Highport",
        [2409] = "Gamma Sulani Planet",
        [2410] = "Ashlan's Veil",
        [2505] = "Mondara Maelstrom",
        [2530] = "Skaara (Inside Marcos Head)",
        [2605] = "Mordane's Wake",
        [3515] = "Primus",
        [3530] = "Tarsis",
        [3595] = "Praetorium Mons Area (Planet Primus)",
        [3605] = "Blackbeards Wake",
        [3606] = "Test Sector",
        [3610] = "Paramis",
        [3615] = "Sho''da''kan Nebula",
        [3619] = "Sho'da'kan Planet",
        [3650] = "Unknown Location",
        [3670] = "Canopus",
        [3675] = "Canopus II",
        [3679] = "Canopus II Planet",
        [3705] = "Ardus",
        [4015] = "New Edinburgh",
        [4025] = "Inverness",
        [4030] = "Arduinne",
        [4093] = "Planet Inverness",
        [4095] = "Arduinne Gas Cloud",
        [4115] = "Xipe Totec",
        [4120] = "Swooping Eagle",
        [4195] = "Yasuragi Area ",
        [4205] = "Mazzaroth Maelstrom",
        [4505] = "Glorys Orbit",
        [4510] = "Slayton",
        [4515] = "Glenn",
        [4520] = "Carpenter",
        [4525] = "Shepard",
        [4530] = "Grissom",
        [4535] = "Cooper",
        [4540] = "Sword",
        [4545] = "Shield",
        [4550] = "Spear",
        [4595] = "Grissom Meteorological Site ",
        [4605] = "Unknown Galaxy",
    };

    /// <summary>The sector's name, trimmed, or null if the id is not a known
    /// space sector (e.g. a station-interior id &gt; 9999).</summary>
    public static string? Name(int sectorId)
        => Names.TryGetValue(sectorId, out var n) ? n.Trim() : null;

    /// <summary>
    /// "&lt;name&gt; (&lt;id&gt;)" when the id is a known sector, else
    /// "sector &lt;id&gt;". The fallback for a runtime-known name (station
    /// interiors) lives in <see cref="SessionContext.SectorLabel"/>, which
    /// consults the live 0x0097 name before falling back here.
    /// </summary>
    public static string Label(int sectorId)
        => Name(sectorId) is { } n ? $"{n} ({sectorId})" : $"sector {sectorId}";
}
