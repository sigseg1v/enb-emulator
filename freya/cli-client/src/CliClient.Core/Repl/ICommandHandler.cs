// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya

namespace N7.CliClient.Repl;

/// <summary>
/// One REPL command. Implementations register with <see cref="Repl"/>
/// at startup; the loop dispatches by <see cref="Name"/>.
/// </summary>
/// <remarks>
/// Handlers receive the already-parsed argument list (one entry per
/// whitespace-separated token after the command name). They return an
/// exit-style int that the REPL treats as:
/// <list type="bullet">
///   <item>0 — success, keep looping.</item>
///   <item>negative — quit the REPL with that as the exit code (the
///   built-in <c>quit</c> handler returns -1).</item>
///   <item>positive — non-fatal error, keep looping but record it.</item>
/// </list>
/// </remarks>
public interface ICommandHandler
{
    /// <summary>The token typed at the prompt (e.g. "connect"). Case-insensitive match.</summary>
    string Name { get; }

    /// <summary>One-line help text shown by the built-in <c>help</c> command.</summary>
    string Summary { get; }

    /// <summary>Multi-line detailed usage, shown by <c>help &lt;name&gt;</c>.</summary>
    string Usage { get; }

    /// <summary>
    /// Whether this command is usable in the current session state. Drives
    /// the interactive completer -- unavailable commands are hidden from the
    /// grey suggestion list. Defaults to always-available; stateful commands
    /// (login needs a connection, chat needs a sector, ...) override it.
    /// </summary>
    bool Available => true;

    /// <summary>
    /// Grey ghost-text hint for this command's arguments, shown after the
    /// command word once it is complete (e.g. connect -> <c>&lt;ip:127.0.0.1&gt;</c>).
    /// Null when the command takes no arguments.
    /// </summary>
    string? Placeholder => null;

    /// <summary>
    /// Ordering hint for the interactive completer. Available commands with a
    /// higher priority are offered first, so the obvious next step in the flow
    /// (connect, then login, then create) leads the suggestion list and the
    /// Tab menu. Ties break alphabetically. Defaults to 0 (no preference).
    /// </summary>
    int Priority => 0;

    /// <summary>
    /// Live completion values for this command's FIRST argument slot -- e.g.
    /// the account's character names for <c>enter</c>. Re-read on each
    /// keystroke so it tracks session state (after <c>login</c> populates the
    /// avatar list, <c>enter</c>'s candidates appear). Null when the argument
    /// has no enumerable set, in which case the static <see cref="Placeholder"/>
    /// drives completion instead.
    /// </summary>
    IReadOnlyList<string>? ArgCandidates => null;

    /// <summary>
    /// When true, the completer treats the ENTIRE text after the command word
    /// as one argument, so a candidate whose name contains spaces (a warp/gate
    /// target like "Mars Gate") completes as a unit instead of being split into
    /// separate argument tokens. Defaults false (space-delimited arguments).
    /// </summary>
    bool WholeLineArg => false;

    /// <summary>Execute the command. Returns an exit-style int (see interface remarks).</summary>
    Task<int> ExecuteAsync(
        IReadOnlyList<string> args,
        TextWriter output,
        CancellationToken ct);
}
