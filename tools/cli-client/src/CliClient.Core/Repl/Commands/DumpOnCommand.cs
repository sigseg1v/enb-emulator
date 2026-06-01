// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

namespace N7.CliClient.Repl.Commands;

/// <summary>
/// <c>dump-on</c> -- start printing every packet that crosses the
/// global or sector socket. Outbound frames are tagged with a yellow
/// arrow, inbound with a cyan one; known opcodes get a structured
/// field decode followed by a hex+ASCII gutter, unknown opcodes get
/// just the gutter so the operator can scan the raw bytes for nulls
/// or other nonsense values. Idempotent.
/// </summary>
public sealed class DumpOnCommand : ICommandHandler
{
    private readonly SessionContext _ctx;

    public DumpOnCommand(SessionContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        _ctx = ctx;
    }

    public string Name    => "dump-on";
    public string Summary => "live-print every packet to/from the active connection";
    public string Usage   => "dump-on";

    public Task<int> ExecuteAsync(
        IReadOnlyList<string> args, TextWriter output, CancellationToken ct)
    {
        _ctx.DumpOutput = output;
        _ctx.DumpEnabled = true;

        // If we are already in-sector, start a background drain so the
        // tail keeps flowing while the prompt sits idle. (Pre-enter we
        // have no socket to drain; the toggle just flips a flag and
        // commands that already do their own receive loops -- login,
        // list, enter -- pick it up automatically.)
        _ctx.StartDumpDrain();

        string where = _ctx.Sector is not null ? "sector"
                     : _ctx.Global is not null ? "global"
                     : "no active connection -- dumps will start with the next login/enter";
        output.WriteLine($"dump-on: tailing {where}");
        return Task.FromResult(0);
    }
}
