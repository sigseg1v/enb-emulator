// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

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

    /// <summary>Execute the command. Returns an exit-style int (see interface remarks).</summary>
    Task<int> ExecuteAsync(
        IReadOnlyList<string> args,
        TextWriter output,
        CancellationToken ct);
}
