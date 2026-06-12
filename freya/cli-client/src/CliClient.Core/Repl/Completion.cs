// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

namespace N7.CliClient.Repl;

/// <summary>
/// A snapshot of one command for the interactive completer: its name,
/// whether it's usable in the current session state, the grey ghost-text
/// hint for its arguments, and a flow-ordering priority (higher = offered
/// first; ties break alphabetically).
/// </summary>
public sealed record CommandSpec(
    string Name, bool Available, string? Placeholder, int Priority = 0,
    IReadOnlyList<string>? ArgCandidates = null, bool WholeLineArg = false);

/// <summary>
/// Pure completion logic for the REPL line editor -- candidate filtering
/// and ghost-text computation. No console I/O lives here so it can be unit
/// tested; <see cref="LineEditor"/> is the thin interactive shell on top.
/// </summary>
public static class Completion
{
    /// <summary>
    /// Available command names, highest <see cref="CommandSpec.Priority"/>
    /// first (the expected next step in the flow), then alphabetically.
    /// </summary>
    public static IReadOnlyList<string> AvailableNames(IReadOnlyList<CommandSpec> specs)
        => specs.Where(s => s.Available)
                .OrderByDescending(s => s.Priority)
                .ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
                .Select(s => s.Name)
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
        if (spec is null || !spec.Available) return string.Empty;

        // Args typed so far (after the command word). TrimStart drops the
        // separator space; what remains is the in-progress first argument until
        // the user types a second space.
        string afterCmd = buffer.TrimStart()[Math.Min(cmd.Length, buffer.TrimStart().Length)..].TrimStart();

        // Dynamic first-arg candidates (e.g. character names for `enter`) take
        // precedence over the static placeholder while still on the first arg.
        // A whole-line-arg command (warp/gate/dock: targets whose names contain
        // spaces) keeps matching against the entire remainder, not just the
        // first space-delimited token.
        if (spec.ArgCandidates is { Count: > 0 } argCands && (spec.WholeLineArg || !afterCmd.Contains(' ')))
        {
            if (afterCmd.Length == 0)
                return string.Join("  ", argCands);
            var matches = argCands
                .Where(c => c.StartsWith(afterCmd, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (matches.Length > 0)
            {
                string first = matches[0];
                string remainder = first.Length >= afterCmd.Length ? first[afterCmd.Length..] : string.Empty;
                if (matches.Length > 1) remainder += $"  (+{matches.Length - 1})";
                return remainder;
            }
            // No candidate matches what's typed -- fall back to the placeholder.
        }

        if (spec.Placeholder is null) return string.Empty;
        // Only show the placeholder while no argument has been typed yet.
        return afterCmd.Length == 0 ? spec.Placeholder : string.Empty;
    }

    /// <summary>
    /// The full buffer text after Tab fills the suggested value for the
    /// argument currently being typed, or null when nothing applies. Only
    /// acts past the command word. A placeholder slot may embed a suggestion:
    /// <list type="bullet">
    ///   <item><c>&lt;name:default&gt;</c> -> <c>default</c>
    ///         (e.g. <c>&lt;ip:127.0.0.1&gt;</c> -> <c>127.0.0.1</c>);</item>
    ///   <item><c>[a|b|c]</c> -> the first option matching what's typed.</item>
    /// </list>
    /// Whatever the user has already typed for the argument must be a prefix
    /// of the suggestion, so Tab never clobbers a value in progress.
    /// </summary>
    public static string? CompleteArgument(string buffer, IReadOnlyList<CommandSpec> specs)
    {
        if (!PastFirstToken(buffer)) return null;

        string cmd = FirstToken(buffer);
        var spec = specs.FirstOrDefault(
            s => s.Available && string.Equals(s.Name, cmd, StringComparison.OrdinalIgnoreCase));
        if (spec is null) return null;

        // Args after the command word; preserve a trailing space (it means
        // "starting a fresh argument", not "editing the last one").
        string args = buffer.TrimStart()[cmd.Length..].TrimStart();
        string[] parts = args.Length == 0
            ? Array.Empty<string>()
            : args.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        bool onNewArg = args.Length == 0 || char.IsWhiteSpace(args[^1]);
        int argIndex = onNewArg ? parts.Length : parts.Length - 1;
        string partial = onNewArg ? string.Empty : parts[^1];

        // Whole-line-arg commands (warp/gate/dock) treat everything after the
        // command word as ONE argument, so a target name with spaces ("Mars
        // Gate") Tab-completes as a unit. Match the full remainder against the
        // candidate set and cycle on repeated Tab.
        if (spec.WholeLineArg && spec.ArgCandidates is { Count: > 0 } wholeCands)
        {
            string? match = wholeCands.FirstOrDefault(
                c => c.StartsWith(args, StringComparison.OrdinalIgnoreCase));
            if (match is null) return null;
            string filled = cmd + " " + match;
            if (filled != buffer) return filled;
            string? next = wholeCands
                .SkipWhile(c => !string.Equals(c, match, StringComparison.OrdinalIgnoreCase))
                .Skip(1)
                .FirstOrDefault(c => c.StartsWith(args, StringComparison.OrdinalIgnoreCase));
            return next is null ? null : cmd + " " + next;
        }

        // First-arg dynamic candidates (e.g. character names for `enter`):
        // fill the first one that the partial is a prefix of, before consulting
        // the static placeholder slots.
        if (argIndex == 0 && spec.ArgCandidates is { Count: > 0 } cands)
        {
            string? match = cands.FirstOrDefault(
                c => c.StartsWith(partial, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                string filled = cmd + " " + match;
                if (filled != buffer) return filled;
                // Already equals the first match -- try the next candidate so a
                // repeated Tab cycles through same-prefix names.
                string? nextMatch = cands
                    .SkipWhile(c => !string.Equals(c, match, StringComparison.OrdinalIgnoreCase))
                    .Skip(1)
                    .FirstOrDefault(c => c.StartsWith(partial, StringComparison.OrdinalIgnoreCase));
                return nextMatch is null ? null : cmd + " " + nextMatch;
            }
        }

        if (spec.Placeholder is null) return null;
        string[] slots = spec.Placeholder.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (argIndex < 0 || argIndex >= slots.Length) return null;

        string? value = SuggestArg(slots[argIndex], partial);
        if (value is null) return null;

        string rebuilt = cmd + " " + string.Join(' ', parts.Take(argIndex).Append(value));
        return rebuilt == buffer ? null : rebuilt;
    }

    /// <summary>The suggested value for one placeholder slot, or null.</summary>
    private static string? SuggestArg(string slot, string partial)
    {
        // <name:default> -- the text after the first colon is the default.
        if (slot.Length >= 2 && slot[0] == '<' && slot[^1] == '>')
        {
            int colon = slot.IndexOf(':');
            if (colon < 0 || colon >= slot.Length - 2) return null;   // <name> or <name:>
            string def = slot[(colon + 1)..^1];
            return def.StartsWith(partial, StringComparison.OrdinalIgnoreCase) ? def : null;
        }

        // [a|b|c] -- the first option that matches what's already typed.
        if (slot.Length >= 2 && slot[0] == '[' && slot[^1] == ']')
        {
            foreach (string opt in slot[1..^1].Split('|'))
                if (opt.StartsWith(partial, StringComparison.OrdinalIgnoreCase))
                    return opt;
        }

        return null;
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
