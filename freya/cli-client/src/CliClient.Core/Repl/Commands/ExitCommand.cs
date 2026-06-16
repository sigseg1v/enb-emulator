// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya

using N7.CliClient.Logging;

namespace N7.CliClient.Repl.Commands;

/// <summary>
/// <c>quit</c> (aliases <c>exit</c>, <c>stop</c>) -- log out cleanly, then leave
/// the REPL. "Log out" means tearing down the keepalive loops and closing both
/// TCP planes; the server reaps the player on disconnect (no synthetic logoff
/// opcode, which would be an unpinned wire change). Returns -1 so the loop
/// exits with code 0.
/// </summary>
/// <remarks>
/// Replaces the REPL's built-in no-op quit handler (which only returned -1 and
/// left teardown to disposal) so that <c>stop</c> is recognised instead of
/// printing "unknown command", and so the logout is explicit and acknowledged.
/// </remarks>
public sealed class ExitCommand : ICommandHandler
{
    private readonly SessionContext _ctx;
    public ExitCommand(SessionContext ctx) { ArgumentNullException.ThrowIfNull(ctx); _ctx = ctx; }

    public string Name => "quit";
    public string Summary => "log out and exit (aliases: exit, stop)";
    public string Usage => "quit  (aliases: exit, stop)";

    public async Task<int> ExecuteAsync(IReadOnlyList<string> args, TextWriter output, CancellationToken ct)
    {
        bool wasOnline = _ctx.Global is not null || _ctx.Sector is not null;
        if (wasOnline)
            await output.WriteLineAsync(AnsiPalette.Muted("logging out...")).ConfigureAwait(false);

        await _ctx.LogoutAsync().ConfigureAwait(false);

        await output.WriteLineAsync(
            AnsiPalette.Ok(wasOnline ? "logged out. bye." : "bye.")).ConfigureAwait(false);
        return -1;   // negative -> REPL quits (exit code 0)
    }
}
