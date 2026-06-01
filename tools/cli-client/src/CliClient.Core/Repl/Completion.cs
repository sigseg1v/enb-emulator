// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

namespace N7.CliClient.Repl;

/// <summary>
/// A snapshot of one command for the interactive completer: its name,
/// whether it's usable in the current session state, and the grey
/// ghost-text hint for its arguments.
/// </summary>
public sealed record CommandSpec(string Name, bool Available, string? Placeholder);

/// <summary>
/// Pure completion logic for the REPL line editor -- candidate filtering
/// and ghost-text computation. No console I/O lives here so it can be unit
/// tested; <see cref="LineEditor"/> is the thin interactive shell on top.
/// </summary>
public static class Completion
{
    /// <summary>Available command names, sorted, case-insensitively.</summary>
    public static IReadOnlyList<string> AvailableNames(IReadOnlyList<CommandSpec> specs)
        => specs.Where(s => s.Available)
                .Select(s => s.Name)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToArray();

    /// <summary>
    /// The first whitespace-delimited token of <paramref name="buffer"/>
    /// (the command word), or "" if the buffer is blank.
    /// </summary>
    public static string FirstToken(string buffer)
    {
        string t = buffer.TrimStart();
        int sp = t.IndexOf(' ');
        return sp < 0 ? t : t[..sp];
    }

    /// <summary>True once a space separates the command word from its args.</summary>
    public static bool PastFirstToken(string buffer) => buffer.TrimStart().Contains(' ');

    /// <summary>
    /// Available command names whose name starts with the first token. When
    /// the buffer is blank, every available command is a candidate. Empty
    /// once we're past the command word (args don't command-complete).
    /// </summary>
    public static IReadOnlyList<string> FirstTokenCandidates(
        string buffer, IReadOnlyList<CommandSpec> specs)
    {
        if (PastFirstToken(buffer)) return Array.Empty<string>();
        string prefix = buffer.TrimStart();
        var names = AvailableNames(specs);
        if (prefix.Length == 0) return names;
        return names
            .Where(n => n.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    /// <summary>
    /// The grey ghost text to render after the buffer:
    /// <list type="bullet">
    ///   <item>blank buffer → the space-joined list of available commands;</item>
    ///   <item>typing the command word → the remainder of the best match
    ///         (fish-style inline autosuggest), with a <c>(+N)</c> tail when
    ///         more than one command matches;</item>
    ///   <item>command word complete, no arg yet → the command's argument
    ///         placeholder; hidden once the user starts an argument.</item>
    /// </list>
    /// </summary>
    public static string Ghost(string buffer, IReadOnlyList<CommandSpec> specs)
    {
        if (!PastFirstToken(buffer))
        {
            string prefix = buffer.TrimStart();
            var cands = FirstTokenCandidates(buffer, specs);
            if (prefix.Length == 0)
                return string.Join("  ", cands);
            if (cands.Count == 0) return string.Empty;

            string first = cands[0];
            string remainder = first.Length >= prefix.Length ? first[prefix.Length..] : string.Empty;
            if (cands.Count > 1) remainder += $"  (+{cands.Count - 1})";
            return remainder;
        }

        string cmd = FirstToken(buffer);
        var spec = specs.FirstOrDefault(
            s => string.Equals(s.Name, cmd, StringComparison.OrdinalIgnoreCase));
        if (spec is null || !spec.Available || spec.Placeholder is null) return string.Empty;

        // Only show the placeholder while no argument has been typed yet.
        string afterCmd = buffer.TrimStart()[Math.Min(cmd.Length, buffer.TrimStart().Length)..].TrimStart();
        return afterCmd.Length == 0 ? spec.Placeholder : string.Empty;
    }
}

/// <summary>
/// Reads one line of input, writing its own prompt. Lets the REPL stay a
/// pure dispatcher while an implementation decides whether to run an
/// interactive editor (TTY) or fall back to a plain reader (piped stdin).
/// </summary>
public interface ILineInput
{
    Task<string?> ReadLineAsync(
        string prompt, TextReader input, TextWriter output, CancellationToken ct);
}
