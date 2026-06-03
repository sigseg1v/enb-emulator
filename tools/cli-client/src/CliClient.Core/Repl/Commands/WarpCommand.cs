// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Buffers.Binary;
using N7.CliClient.Logging;
using N7.CliClient.Net;
using N7.CliClient.Opcodes;

namespace N7.CliClient.Repl.Commands;

/// <summary>
/// <c>warp &lt;gid&gt;</c> -- ask the server to warp to a target via a one-entry
/// 0x009B WARP route (struct WarpPacket: int32 GameID; short Navs; int32
/// TargetID[Navs]; LE). The target is a tracked object's game id (0x.. /
/// decimal / a tracked name).
/// </summary>
/// <remarks>
/// <para>
/// Fire-and-forget on purpose. The retail client has NO cancel-warp opcode:
/// server-side <c>Player::TerminateWarp</c> is only ever driven by game events
/// (sector boundary, gravity well, combat hull damage, gate/dock, GM
/// /warpreset) or disconnect -- never a "stop my warp" packet. So unlike
/// <c>move</c> (which ESC genuinely aborts by ceasing the MVAS feed) there is
/// nothing faithful for a warp ESC to send; we do not fake one. The warp runs
/// the route until it arrives or the game interrupts it.
/// </para>
/// </remarks>
public sealed class WarpCommand : ICommandHandler
{
    private readonly SessionContext _ctx;
    public WarpCommand(SessionContext ctx) { ArgumentNullException.ThrowIfNull(ctx); _ctx = ctx; }

    public string Name    => "warp";
    public string Summary => "warp to a target gid (direct; no client-side cancel exists)";
    public string Usage   =>
        "warp <gid>\n" +
        "  gid = 0x.. / decimal / a tracked object name. Sends a one-waypoint\n" +
        "  0x009B WARP route. Fire-and-forget: there is no cancel-warp packet, so\n" +
        "  the warp runs until it arrives or the game interrupts it.";
    public string? Placeholder => "<gid>";
    public bool Available => _ctx.Sector is not null && _ctx.GameId is not null;

    public async Task<int> ExecuteAsync(IReadOnlyList<string> args, TextWriter output, CancellationToken ct)
    {
        if (_ctx.Sector is null || _ctx.GameId is not { } id)
        {
            await output.WriteLineAsync(AnsiPalette.Warn("not in a sector -- run `enter` first")).ConfigureAwait(false);
            return 1;
        }
        if (args.Count == 0)
        {
            await output.WriteLineAsync(AnsiPalette.Warn($"usage: {Usage}")).ConfigureAwait(false);
            return 1;
        }
        if (TargetArg.Resolve(args[0], _ctx.World) is not { } target)
        {
            await output.WriteLineAsync(AnsiPalette.Warn(
                $"can't resolve '{args[0]}' to a target -- give 0x.. / decimal gid or a tracked name (`list`)")).ConfigureAwait(false);
            return 1;
        }

        // WarpPacket: int32 GameID; short Navs (=1); int32 TargetID[0]. LE.
        var payload = new byte[10];
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(0), id);
        BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(4), 1);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(6), target);

        await _ctx.Sector.SendAsync(
            Packet.ForOpcode(OpcodeId.Known.Warp.Value, payload), ct).ConfigureAwait(false);

        string label = _ctx.World.NameOf(target) is { } n ? $"{n} (0x{target:X8})" : $"0x{target:X8}";
        await output.WriteLineAsync(
            AnsiPalette.Ok("warp requested to ") + AnsiPalette.Value(label) +
            AnsiPalette.Muted(". Watch arrival with `list`.")).ConfigureAwait(false);
        return 0;
    }
}
