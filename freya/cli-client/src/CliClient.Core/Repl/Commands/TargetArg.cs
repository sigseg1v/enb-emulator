// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Globalization;

namespace N7.CliClient.Repl.Commands;

/// <summary>
/// Parses a "target" argument shared by <c>move</c> and <c>warp</c>. Accepts a
/// game id in several forms -- <c>gid=0x000186A5</c>, <c>0x000186A5</c>, a bare
/// decimal id, or (angle-bracket placeholder text tolerated) <c>&lt;gid=...&gt;</c>
/// -- and falls back to resolving a tracked object name against the sector
/// world. Returns the game id, or null when nothing matches.
/// </summary>
internal static class TargetArg
{
    public static bool TryParseGid(string token, out int gid)
    {
        gid = 0;
        if (string.IsNullOrWhiteSpace(token)) return false;

        string s = token.Trim().Trim('<', '>');
        if (s.StartsWith("gid=", StringComparison.OrdinalIgnoreCase)) s = s[4..];
        else if (s.StartsWith("id=", StringComparison.OrdinalIgnoreCase)) s = s[3..];

        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return int.TryParse(s.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out gid);

        return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out gid);
    }

    /// <summary>
    /// Resolve a single target token to a game id: a gid literal if it parses
    /// as one, else a tracked object name. Null when neither matches.
    /// </summary>
    public static int? Resolve(string token, SectorWorld world)
    {
        if (TryParseGid(token, out int gid)) return gid;
        return world.FindByName(token.Trim().Trim('<', '>'));
    }
}
