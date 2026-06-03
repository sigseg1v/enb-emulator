// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Buffers.Binary;
using N7.CliClient.Logging;
using N7.CliClient.Net;
using N7.CliClient.Opcodes;

namespace N7.CliClient.Repl.Commands;

/// <summary>
/// Build a 0x002C ACTION packet (struct ActionPacket: int32 GameID, Action,
/// Target, OptionalVar; 16 bytes LE, no byte-swap -- the server reads every
/// field raw host-order in Player::HandleAction). Shared by the group commands,
/// which are all just ACTION with a different Action code.
/// </summary>
internal static class ActionPacketBuilder
{
    public const int Invite = 10;   // invite target to group  (Target = target gid)
    public const int Accept = 11;   // accept group invitation
    public const int Leave  = 14;   // leave group

    public static byte[] Build(int gameId, int action, int target = -1, int optionalVar = 0)
    {
        var b = new byte[16];
        BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(0),  gameId);
        BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(4),  action);
        BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(8),  target);
        BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(12), optionalVar);
        return b;
    }

    public static Task SendAsync(SessionContext ctx, int action, int target, CancellationToken ct)
    {
        byte[] payload = Build(ctx.GameId!.Value, action, target);
        return ctx.Sector!.SendAsync(
            Packet.ForOpcode(OpcodeId.Known.Action.Value, payload), ct);
    }
}

/// <summary>
/// <c>group-invite &lt;player&gt;</c> -- invite a tracked player (by name) to a
/// group via 0x002C ACTION, Action 10. The target gid is resolved from the
/// sector world model (a player must have been seen, e.g. via <c>list</c>).
/// </summary>
public sealed class GroupInviteCommand : ICommandHandler
{
    private readonly SessionContext _ctx;
    public GroupInviteCommand(SessionContext ctx) { ArgumentNullException.ThrowIfNull(ctx); _ctx = ctx; }

    public string Name    => "group-invite";
    public string Summary => "invite a nearby player to a group by name";
    public string Usage   => "group-invite <player>\n  resolves the name from tracked sector objects (see `list`).";
    public string? Placeholder => "<player>";
    public bool Available => _ctx.Sector is not null && _ctx.GameId is not null;

    public async Task<int> ExecuteAsync(IReadOnlyList<string> args, TextWriter output, CancellationToken ct)
    {
        if (_ctx.Sector is null || _ctx.GameId is null)
        {
            await output.WriteLineAsync(AnsiPalette.Warn("not in a sector -- run `enter` first")).ConfigureAwait(false);
            return 1;
        }
        if (args.Count == 0)
        {
            await output.WriteLineAsync(AnsiPalette.Warn($"usage: {Usage}")).ConfigureAwait(false);
            return 1;
        }
        string name = string.Join(' ', args);
        if (_ctx.World.FindByName(name) is not { } target)
        {
            await output.WriteLineAsync(AnsiPalette.Warn(
                $"no tracked player named '{name}' -- run `list` first, they must be in your sector")).ConfigureAwait(false);
            return 1;
        }

        await ActionPacketBuilder.SendAsync(_ctx, ActionPacketBuilder.Invite, target, ct).ConfigureAwait(false);
        await output.WriteLineAsync(
            AnsiPalette.Ok("group-invite sent to ") + AnsiPalette.Value(name) +
            AnsiPalette.Muted($" (0x{target:X8})")).ConfigureAwait(false);
        return 0;
    }
}

/// <summary>
/// <c>group-invite-accept</c> -- accept a pending group invite via 0x002C
/// ACTION, Action 11. Gated on having actually seen an invite (0x001E GROUP),
/// so it cannot blindly fire an accept the server has nothing to match.
/// </summary>
public sealed class GroupInviteAcceptCommand : ICommandHandler
{
    private readonly SessionContext _ctx;
    public GroupInviteAcceptCommand(SessionContext ctx) { ArgumentNullException.ThrowIfNull(ctx); _ctx = ctx; }

    public string Name    => "group-invite-accept";
    public string Summary => "accept a pending group invite";
    public string Usage   => "group-invite-accept";
    public bool Available => _ctx.Sector is not null && _ctx.GameId is not null && _ctx.PendingGroupInviter is not null;

    public async Task<int> ExecuteAsync(IReadOnlyList<string> args, TextWriter output, CancellationToken ct)
    {
        if (_ctx.Sector is null || _ctx.GameId is null)
        {
            await output.WriteLineAsync(AnsiPalette.Warn("not in a sector -- run `enter` first")).ConfigureAwait(false);
            return 1;
        }
        if (_ctx.PendingGroupInviter is not { } inviter)
        {
            await output.WriteLineAsync(AnsiPalette.Warn("no pending group invite to accept")).ConfigureAwait(false);
            return 1;
        }

        await ActionPacketBuilder.SendAsync(_ctx, ActionPacketBuilder.Accept, target: -1, ct).ConfigureAwait(false);
        _ctx.PendingGroupInviter = null;
        await output.WriteLineAsync(
            AnsiPalette.Ok("accepted group invite from ") + AnsiPalette.Value(inviter)).ConfigureAwait(false);
        return 0;
    }
}

/// <summary>
/// <c>group-leave</c> -- leave the current group via 0x002C ACTION, Action 14.
/// </summary>
public sealed class GroupLeaveCommand : ICommandHandler
{
    private readonly SessionContext _ctx;
    public GroupLeaveCommand(SessionContext ctx) { ArgumentNullException.ThrowIfNull(ctx); _ctx = ctx; }

    public string Name    => "group-leave";
    public string Summary => "leave the current group";
    public string Usage   => "group-leave";
    public bool Available => _ctx.Sector is not null && _ctx.GameId is not null;

    public async Task<int> ExecuteAsync(IReadOnlyList<string> args, TextWriter output, CancellationToken ct)
    {
        if (_ctx.Sector is null || _ctx.GameId is null)
        {
            await output.WriteLineAsync(AnsiPalette.Warn("not in a sector -- run `enter` first")).ConfigureAwait(false);
            return 1;
        }

        await ActionPacketBuilder.SendAsync(_ctx, ActionPacketBuilder.Leave, target: -1, ct).ConfigureAwait(false);
        _ctx.PendingGroupInviter = null;
        await output.WriteLineAsync(AnsiPalette.Ok("group-leave sent")).ConfigureAwait(false);
        return 0;
    }
}
