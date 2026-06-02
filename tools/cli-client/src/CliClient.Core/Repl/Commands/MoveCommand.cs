// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Buffers.Binary;
using System.Globalization;
using N7.CliClient.Net;

namespace N7.CliClient.Repl.Commands;

/// <summary>
/// <c>move &lt;x&gt; &lt;y&gt; &lt;z&gt; [send]</c> -- fly the avatar toward a
/// sector coordinate by feeding MVAS position updates, the way a real client's
/// proxy would (but computed, since a headless client has no engine).
/// </summary>
/// <remarks>
/// <para>
/// Dry-run by default (prints the datagram). With <c>send</c> the CLI becomes a
/// full UDP client for the in-flight phase: it opens a
/// <see cref="SectorUdpClient"/>, which feeds our own position to the server
/// AND receives the live sector stream the server reroutes to our socket. The
/// flight is realistic: each tick we orient toward the target, step by
/// <c>engineSpeed / sendRate</c>, send the position+heading at the server's
/// suggested rate, and stop once within an arrival delta. The server only
/// sweeps for in-range navs while moving (PlayerClass.cpp CalcNewPosition/
/// CheckNavs), so the motion is what makes navs and objects fan in -- watch
/// them with <c>list</c>.
/// </para>
/// </remarks>
public sealed class MoveCommand : ICommandHandler
{
    private const float DefaultSpeed = 1500f;   // units/sec when no ship aux MaxSpeed seen
    private const float ArriveDelta  = 1200f;   // stop when this close to target
    private const int   MaxTicks     = 1200;    // hard cap so a bad target can't loop forever

    private readonly SessionContext _ctx;

    public MoveCommand(SessionContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        _ctx = ctx;
    }

    public string Name    => "move";
    public string Summary => "fly toward sector coords (dry-run; 'send' drives MVAS for real)";
    public string Usage   =>
        "move <x> <y> <z> [send]\n" +
        "  dry-run prints the MVAS datagram; 'send' opens the sector UDP client,\n" +
        "  orients toward the target, flies at engine speed feeding position\n" +
        "  updates, and stops within arrival range. Watch the world with `list`.";
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

        if (_ctx.World.SelfSnapshot(id).Pos is not { } start0)
        {
            await output.WriteLineAsync(
                "no own position yet -- wait for the sector fanout, then retry").ConfigureAwait(false);
            return 1;
        }

        if (!send)
        {
            byte[] dg = MvasClient.BuildDatagram(id, sequence: 1, tx, ty, tz);
            await output.WriteLineAsync(
                $"move (dry-run): ({start0.X:0.0}, {start0.Y:0.0}, {start0.Z:0.0}) -> " +
                $"({tx:0.0}, {ty:0.0}, {tz:0.0})  player=0x{id:X8}").ConfigureAwait(false);
            await output.WriteLineAsync(
                $"  MVAS 0x1004 datagram ({dg.Length}B): {Convert.ToHexString(dg)}").ConfigureAwait(false);
            await output.WriteLineAsync(
                "  not sent. Append 'send' to fly there as a full UDP client").ConfigureAwait(false);
            await output.WriteLineAsync(
                "  (opens the sector UDP socket; the live sector stream then arrives there).")
                .ConfigureAwait(false);
            return 0;
        }

        // --- become a full UDP client for the in-flight phase ---
        if (_ctx.SectorUdp is null)
        {
            // The proxy TCP feed is about to go dark (the server reroutes this
            // player's stream to our UDP socket once we send MVAS), so retire
            // the TCP drain and stand up the UDP client feeding the same hooks.
            await _ctx.StopSectorDrainAsync().ConfigureAwait(false);
            var udp = new SectorUdpClient(
                _ctx.EffectiveMvasHost, _ctx.MvasPort, _ctx.ReplayInboundFrame,
                msg => { try { _ctx.DumpOutput.WriteLine(msg); } catch { } });
            udp.Start(ct);
            _ctx.SectorUdp = udp;
            await output.WriteLineAsync(
                $"sector UDP client up on local port {udp.LocalPort} -> {_ctx.EffectiveMvasHost}:{_ctx.MvasPort}; " +
                "live sector stream now arrives here").ConfigureAwait(false);
        }
        var client = _ctx.SectorUdp;

        // NOTE: the server only runs the movement/nav loop for players that are
        // InSpace() (server PlayerManager.cpp:476 RunPlayerUpdate). A freshly
        // created character is DOCKED, so MVAS + throttle here do nothing until
        // the avatar launches. Undocking is not a one-shot: 0x004E Action=1 ->
        // SectorManager::LaunchIntoSpace -> SendServerHandoff, i.e. a sector
        // re-login that the CLI must drive (InSpace flips in Player::FinishLogin
        // afterwards). Until that launch handshake exists, this flight is a
        // no-op against a docked avatar -- see plans/19.
        float speed = _ctx.World.SelfSpeed(id) is { } s && s > 0 ? s : DefaultSpeed;
        int freq = Math.Clamp(client.Frequency, 1, 60);
        int intervalMs = Math.Max(1000 / freq, 25);
        float stepDist = speed * (intervalMs / 1000f);
        float arrive = Math.Max(ArriveDelta, stepDist);

        await output.WriteLineAsync(
            $"move: ({start0.X:0.0}, {start0.Y:0.0}, {start0.Z:0.0}) -> ({tx:0.0}, {ty:0.0}, {tz:0.0})  " +
            $"speed={speed:0}u/s rate={freq}Hz step={stepDist:0}u player=0x{id:X8}").ConfigureAwait(false);

        // Engage forward thrust over the sector channel (0x0014 MOVE, type 2).
        // The MVAS feed sets POSITION, but the server only sweeps for in-range
        // navs (Player::CheckNavs) while the avatar has non-zero speed, and
        // speed comes from the throttle (Player::Move -> m_Accelerating), not
        // from the position feed. Without this the avatar teleports silently
        // and no navs expose.
        await SendThrottle(id, type: 2, ct).ConfigureAwait(false);

        (float X, float Y, float Z) cur = start0;
        int ticks = 0;
        try
        {
            while (ticks++ < MaxTicks)
            {
                float dx = tx - cur.X, dy = ty - cur.Y, dz = tz - cur.Z;
                float dist = MathF.Sqrt(dx * dx + dy * dy + dz * dz);
                if (dist <= arrive) break;

                float inv = 1f / dist;                       // unit heading toward target
                (float X, float Y, float Z) head = (dx * inv, dy * inv, dz * inv);
                float adv = MathF.Min(stepDist, dist);
                cur = (cur.X + head.X * adv, cur.Y + head.Y * adv, cur.Z + head.Z * adv);

                client.SendPosition(id, cur.X, cur.Y, cur.Z, head);
                await Task.Delay(intervalMs, ct).ConfigureAwait(false);
            }
            // Settle on the target so the server registers arrival.
            client.SendPosition(id, tx, ty, tz, (0, 0, 0));
            // Kill thrust (0x0014 MOVE, type 4) so we coast to a stop.
            await SendThrottle(id, type: 4, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            await output.WriteLineAsync($"move failed: {ex.Message}").ConfigureAwait(false);
            return 1;
        }

        var landed = _ctx.World.SelfSnapshot(id).Pos;
        string where = landed is { } lp ? $"({lp.X:0.0}, {lp.Y:0.0}, {lp.Z:0.0})" : "(server pos unknown)";
        await output.WriteLineAsync(
            $"  flew {ticks} ticks; {client.ReceivedDatagrams} datagrams in; server reports {where}. " +
            "Run `list` for navs/objects in range.").ConfigureAwait(false);
        return 0;
    }

    // 0x0014 MOVE { int32 GameID; byte type } over the sector channel.
    // type 2 = forward thrust, 4 = kill thrust (server/src/PlayerConnection.cpp
    // HandleMove -> Player::Move). Routed client->proxy->server upstream, which
    // is unaffected by the downstream UDP reroute.
    private async Task SendThrottle(int gameId, int type, CancellationToken ct)
    {
        if (_ctx.Sector is null) return;
        byte[] mp = new byte[5];
        BinaryPrimitives.WriteInt32LittleEndian(mp.AsSpan(0, 4), gameId);
        mp[4] = (byte)type;
        await _ctx.Sector.SendAsync(Net.Packet.ForOpcode(0x0014, mp), ct).ConfigureAwait(false);
    }
}
