// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya

using System.Buffers.Binary;
using N7.CliClient.Logging;
using N7.CliClient.Net;
using N7.CliClient.Opcodes;

namespace N7.CliClient.Repl.Commands;

/// <summary>
/// Build a 0x002C ACTION packet (struct ActionPacket: int32 GameID, Action,
/// Target, OptionalVar; 16 bytes LE, no byte-swap -- the server reads every
/// field raw host-order in Player::HandleAction). Shared by the group
/// subcommands, which are all just ACTION with a different Action code.
/// </summary>
internal static class ActionPacketBuilder
{
    public const int Invite = 10;   // invite target to group  (Target = target gid)
    public const int Accept = 11;   // accept group invitation
    public const int Leave = 14;   // leave group

    public static byte[] Build(int gameId, int action, int target = -1, int optionalVar = 0)
    {
        var b = new byte[16];
        BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(0), gameId);
        BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(4), action);
        BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(8), target);
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
/// <c>group &lt;invite|accept|leave|create&gt;</c> -- the grouping verbs, each a
/// 0x002C ACTION with a different Action code:
/// <list type="bullet">
///   <item><c>group invite &lt;player&gt;</c> -- Action 10. Resolve the name from
///   tracked sector objects (see <c>list</c>) and invite them. This is also how a
///   group is "created": the server has no separate create-group action -- a
///   group springs into being when the first invite is accepted.</item>
///   <item><c>group accept</c> -- Action 11. Accept a pending invite (gated on
///   having actually seen one, so it cannot fire an accept the server has nothing
///   to match).</item>
///   <item><c>group leave</c> -- Action 14. Leave the current group.</item>
///   <item><c>group create</c> -- guidance only: there is no create-group action;
///   it points the user at <c>group invite</c>.</item>
/// </list>
/// No subcommand prints the current group state and the available verbs.
/// </summary>
public sealed class GroupCommand : ICommandHandler
{
    private readonly SessionContext _ctx;
    public GroupCommand(SessionContext ctx) { ArgumentNullException.ThrowIfNull(ctx); _ctx = ctx; }

    public string Name => "group";
    public string Summary => "group up: invite / accept / leave (create == invite the first member)";
    public string Usage =>
        "group <invite|accept|leave|create>\n" +
        "  group invite <player>  invite a tracked nearby player by name (0x002C Action 10).\n" +
        "                         A group is created by inviting -- there is no separate\n" +
        "                         create action; the group forms when the invite is accepted.\n" +
        "  group accept           accept a pending group invite (Action 11).\n" +
        "  group leave            leave the current group (Action 14).\n" +
        "  group create           explains that grouping starts with `group invite`.\n" +
        "  group                  show current group state and the available verbs.";
    public string? Placeholder => "<invite|accept|leave|create>";
    public bool Available => _ctx.Sector is not null && _ctx.GameId is not null;

    private static readonly string[] Subcommands = { "invite", "accept", "leave", "create" };
    public IReadOnlyList<string>? ArgCandidates => Subcommands;

    public async Task<int> ExecuteAsync(IReadOnlyList<string> args, TextWriter output, CancellationToken ct)
    {
        if (_ctx.Sector is null || _ctx.GameId is null)
        {
            await output.WriteLineAsync(AnsiPalette.Warn("not in a sector -- run `enter` first")).ConfigureAwait(false);
            return 1;
        }
        if (args.Count == 0)
        {
            await PrintStateAsync(output).ConfigureAwait(false);
            return 0;
        }

        string sub = args[0].ToLowerInvariant();
        var rest = args.Count > 1 ? args.Skip(1).ToArray() : Array.Empty<string>();
        return sub switch
        {
            "invite" => await InviteAsync(rest, output, ct).ConfigureAwait(false),
            "accept" => await AcceptAsync(output, ct).ConfigureAwait(false),
            "leave" => await LeaveAsync(output, ct).ConfigureAwait(false),
            "create" => await CreateAsync(output).ConfigureAwait(false),
            _ => await UnknownAsync(sub, output).ConfigureAwait(false),
        };
    }

    private async Task PrintStateAsync(TextWriter output)
    {
        var g = _ctx.SelfGroup;
        string state = !g.InGroup
            ? "not in a group"
            : g.IsLeader
                ? (g.InFormation ? "group leader, formation active" : "group leader")
                : (g.InFormation ? "group member, formation active" : "group member");
        await output.WriteLineAsync(AnsiPalette.Muted("group: ") + AnsiPalette.Value(state)).ConfigureAwait(false);
        if (_ctx.PendingGroupInviter is { } inviter)
            await output.WriteLineAsync(
                AnsiPalette.Muted("pending invite from ") + AnsiPalette.Value(inviter) +
                AnsiPalette.Muted(" -- `group accept`")).ConfigureAwait(false);
        await output.WriteLineAsync(AnsiPalette.Muted("verbs: invite <player> | accept | leave | create")).ConfigureAwait(false);
    }

    private async Task<int> InviteAsync(IReadOnlyList<string> rest, TextWriter output, CancellationToken ct)
    {
        if (rest.Count == 0)
        {
            await output.WriteLineAsync(AnsiPalette.Warn("usage: group invite <player>")).ConfigureAwait(false);
            return 1;
        }
        string name = string.Join(' ', rest);
        if (_ctx.World.FindByName(name) is not { } target)
        {
            await output.WriteLineAsync(AnsiPalette.Warn(
                $"no tracked player named '{name}' -- run `list` first, they must be in your sector")).ConfigureAwait(false);
            return 1;
        }

        await ActionPacketBuilder.SendAsync(_ctx, ActionPacketBuilder.Invite, target, ct).ConfigureAwait(false);
        await output.WriteLineAsync(
            AnsiPalette.Ok("group invite sent to ") + AnsiPalette.Value(name) +
            AnsiPalette.Muted($" (0x{target:X8})")).ConfigureAwait(false);
        return 0;
    }

    private async Task<int> AcceptAsync(TextWriter output, CancellationToken ct)
    {
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

    private async Task<int> LeaveAsync(TextWriter output, CancellationToken ct)
    {
        await ActionPacketBuilder.SendAsync(_ctx, ActionPacketBuilder.Leave, target: -1, ct).ConfigureAwait(false);
        _ctx.PendingGroupInviter = null;
        await output.WriteLineAsync(AnsiPalette.Ok("group leave sent")).ConfigureAwait(false);
        return 0;
    }

    private static async Task<int> CreateAsync(TextWriter output)
    {
        // There is no create-group action on the wire (the 0x002C ACTION verbs
        // are invite/accept/decline/disband/leave/kick/LFG -- no "create"). A
        // group is created implicitly by inviting the first member, so this verb
        // exists only to explain that rather than fabricate an opcode.
        await output.WriteLineAsync(AnsiPalette.Muted(
            "a group is created by inviting -- run `group invite <player>`; " +
            "the group forms when they accept.")).ConfigureAwait(false);
        return 0;
    }

    private async Task<int> UnknownAsync(string sub, TextWriter output)
    {
        await output.WriteLineAsync(AnsiPalette.Warn(
            $"unknown group verb '{sub}' -- expected invite | accept | leave | create")).ConfigureAwait(false);
        return 1;
    }
}
