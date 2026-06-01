// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Globalization;
using N7.CliClient.Net;

namespace N7.CliClient.Repl.Commands;

/// <summary>
/// <c>move &lt;x&gt; &lt;y&gt; &lt;z&gt; [send]</c> -- compute the MVAS position
/// updates that would fly the avatar to a sector coordinate, the way a real
/// client's proxy emits them by scraping the engine's position.
/// </summary>
/// <remarks>
/// <para>
/// Dry-run by default: it prints the exact datagram(s) it would send.
/// Transmitting requires an explicit trailing <c>send</c> token, because of a
/// hard architectural constraint in the proxied dev stack:
/// </para>
/// <para>
/// the server routes a player's downstream sector data (including the nav-
/// exposure frames this command is meant to trigger) to a single
/// addr/port pair, <c>m_Player_IPAddr</c>/<c>m_Player_Port</c>
/// (server/src/PlayerConnection.cpp:246). That pair is (re)set to the source
/// of every inbound MVAS datagram by <c>SetPlayerPortIP</c>
/// (server/src/UDP_MVAS.cpp:149). In the live stack the proxy is that source,
/// so data flows back through the proxy to our TCP sector channel. If WE send
/// MVAS from our own UDP socket, the server redirects this player's entire
/// sector stream to us over UDP -- our TCP feed (where the world model reads
/// navs) goes dark. Driving movement properly therefore needs the CLI to run
/// as a full UDP client (own the UDP receive + 0x2016 reliability layer),
/// which is out of scope for the passive observer. <c>send</c> is kept for
/// that future mode / for moving the avatar server-side where another client
/// is observing.
/// </para>
/// </remarks>
public sealed class MoveCommand : ICommandHandler
{
    private const int Steps = 12;
    private const int StepDelayMs = 80;

    private readonly SessionContext _ctx;

    public MoveCommand(SessionContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        _ctx = ctx;
    }

    public string Name    => "move";
    public string Summary => "show (or with 'send' transmit) MVAS position updates to fly";
    public string Usage   =>
        "move <x> <y> <z> [send]\n" +
        "  dry-run prints the MVAS datagram; 'send' transmits it.\n" +
        "  NOTE: against the proxied stack, sending reroutes your sector\n" +
        "  stream to this UDP socket and stops navs arriving on the TCP feed.";
    public string? Placeholder => "<x> <y> <z>";

    public bool Available => _ctx.Sector is not null && _ctx.GameId is not null;

    public async Task<int> ExecuteAsync(
        IReadOnlyList<string> args, TextWriter output, CancellationToken ct)
    {
        if (_ctx.Sector is null || _ctx.GameId is not { } id)
        {
            await output.WriteLineAsync("not in a sector -- run `enter` first").ConfigureAwait(false);
            return 1;
        }

        bool send = args.Count >= 4 && string.Equals(args[3], "send", StringComparison.OrdinalIgnoreCase);

        if (args.Count < 3
            || !float.TryParse(args[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float tx)
            || !float.TryParse(args[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float ty)
            || !float.TryParse(args[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float tz))
        {
            await output.WriteLineAsync("usage: move <x> <y> <z> [send]  (numbers)").ConfigureAwait(false);
            return 1;
        }

        var start = _ctx.World.SelfSnapshot(id).Pos ?? (tx, ty, tz);

        if (!send)
        {
            byte[] dg = MvasClient.BuildDatagram(id, sequence: 1, tx, ty, tz);
            await output.WriteLineAsync(
                $"move (dry-run): ({start.X:0.0}, {start.Y:0.0}, {start.Z:0.0}) -> " +
                $"({tx:0.0}, {ty:0.0}, {tz:0.0})  player=0x{id:X8}").ConfigureAwait(false);
            await output.WriteLineAsync(
                $"  MVAS 0x1004 datagram ({dg.Length}B): {Convert.ToHexString(dg)}").ConfigureAwait(false);
            await output.WriteLineAsync(
                "  not sent. Sending against the proxied stack reroutes this player's").ConfigureAwait(false);
            await output.WriteLineAsync(
                "  sector stream to this UDP socket (server collapses MVAS-source and").ConfigureAwait(false);
            await output.WriteLineAsync(
                "  data-dest); navs would stop arriving on the TCP feed. Append 'send'").ConfigureAwait(false);
            await output.WriteLineAsync(
                "  to transmit anyway (moves the avatar server-side).").ConfigureAwait(false);
            return 0;
        }

        await output.WriteLineAsync(
            $"move: ({start.X:0.0}, {start.Y:0.0}, {start.Z:0.0}) -> ({tx:0.0}, {ty:0.0}, {tz:0.0})  " +
            $"player=0x{id:X8} via {Steps} MVAS updates (sector stream now reroutes to UDP)")
            .ConfigureAwait(false);

        try
        {
            for (int i = 1; i <= Steps; i++)
            {
                float t = (float)i / Steps;
                _ctx.Mvas.SendPosition(id,
                    start.X + (tx - start.X) * t,
                    start.Y + (ty - start.Y) * t,
                    start.Z + (tz - start.Z) * t);
                await Task.Delay(StepDelayMs, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            await output.WriteLineAsync($"move failed: {ex.Message}").ConfigureAwait(false);
            return 1;
        }

        await output.WriteLineAsync("  position fed via MVAS UDP.").ConfigureAwait(false);
        return 0;
    }
}
