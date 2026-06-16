// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya

using System.Buffers.Binary;
using N7.CliClient.Logging;
using N7.CliClient.Net;
using N7.CliClient.Opcodes;
using N7.CliClient.Opcodes.Records;

namespace N7.CliClient.Repl.Commands;

/// <summary>
/// Build a 0x00BC CTA_REQUEST packet (struct CTARequest: int32 SourceID,
/// TargetID, Action; 12 bytes LE, read raw host-order in
/// Player::HandleCTARequest). SourceID is our own tagged GameID (the server
/// strips PLAYER_TAG in GetPlayer). TargetID is unused for every formation
/// action -- only Action 12 "request target" reads it -- so it is sent equal to
/// SourceID. Shared by the formation subcommands, which are all just a CTA with
/// a different Action code.
/// </summary>
internal static class CtaRequestBuilder
{
    public static byte[] Build(int sourceId, int action)
    {
        var b = new byte[12];
        BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(0), sourceId);
        BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(4), sourceId);   // TargetID (unused)
        BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(8), action);
        return b;
    }
}

/// <summary>
/// <c>formation &lt;pipe|block|slot|join|break&gt;</c> -- group formation control
/// via 0x00BC CTA_REQUEST (struct CTARequest: int32 SourceID, TargetID, Action;
/// 12 bytes LE, read raw host-order in Player::HandleCTARequest, dispatched by
/// PlayerManager::GroupAction). SourceID is our own tagged GameID; the server
/// strips PLAYER_TAG in GetPlayer. TargetID is unused for every formation action
/// (only Action 12 "request target" reads it), so it is sent as SourceID.
///
/// Action codes (server/src/GroupManager.cpp GroupAction):
///   4 slot-back, 5 block, 6 pipe -> SetFormation (LEADER only; the server
///     verifies the sender is Member[0] and rejects otherwise).
///   7 form up   -> FormUp (a member snaps into the leader's active formation;
///     requires same sector and within 5000 of the leader).
///   8 leave formation -> LeaveFormation (a member drops out of formation).
///   9 break formation -> BreakFormation (the leader ends the formation).
///
/// Gating mirrors the server's own rules from the self group state
/// (<see cref="SessionContext.SelfGroup"/>): pipe/block/slot need leadership,
/// join needs being a non-leader member not yet formed up, and break is leave
/// (member) or break (leader). Availability and the tab candidates track that
/// state so only the currently-valid verbs are offered.
/// </summary>
public sealed class FormationCommand : ICommandHandler
{
    private const int ActionSlot = 4;
    private const int ActionBlock = 5;
    private const int ActionPipe = 6;
    private const int ActionJoin = 7;   // form up
    private const int ActionLeave = 8;   // leave formation (member)
    private const int ActionBreak = 9;   // break formation (leader)

    private readonly SessionContext _ctx;
    public FormationCommand(SessionContext ctx) { ArgumentNullException.ThrowIfNull(ctx); _ctx = ctx; }

    public string Name => "formation";
    public string Summary => "group formation: pipe/block/slot (leader), join, break/leave";
    public string Usage =>
        "formation <pipe|block|slot|join|break>\n" +
        "  formation pipe|block|slot  leader: set the group's formation (0x00BC Action 6/5/4).\n" +
        "  formation join             member: form up into the leader's formation (Action 7).\n" +
        "                             Needs same sector and within 5000 of the leader.\n" +
        "  formation break            leader: end the formation (Action 9);\n" +
        "                             member: drop out of formation (Action 8).";
    public string? Placeholder => "<pipe|block|slot|join|break>";

    // Only meaningful while grouped. Hidden when solo.
    public bool Available => _ctx.Sector is not null && _ctx.GameId is not null && _ctx.SelfGroup.InGroup;

    // Offer only the verbs valid for the current role/formation state, re-read on
    // each keystroke so it tracks group changes live.
    public IReadOnlyList<string>? ArgCandidates
    {
        get
        {
            var g = _ctx.SelfGroup;
            if (!g.InGroup) return null;
            var list = new List<string>();
            if (g.IsLeader)
            {
                list.Add("pipe");
                list.Add("block");
                list.Add("slot");
            }
            else if (!g.InFormation)
            {
                list.Add("join");
            }
            list.Add("break");   // leader breaks; member leaves
            return list;
        }
    }

    public async Task<int> ExecuteAsync(IReadOnlyList<string> args, TextWriter output, CancellationToken ct)
    {
        if (_ctx.Sector is null || _ctx.GameId is null)
        {
            await output.WriteLineAsync(AnsiPalette.Warn("not in a sector -- run `enter` first")).ConfigureAwait(false);
            return 1;
        }
        var g = _ctx.SelfGroup;
        if (!g.InGroup)
        {
            await output.WriteLineAsync(AnsiPalette.Warn("not in a group -- use `group invite <player>` first")).ConfigureAwait(false);
            return 1;
        }
        if (args.Count == 0)
        {
            await output.WriteLineAsync(AnsiPalette.Warn($"usage: {Usage}")).ConfigureAwait(false);
            return 1;
        }

        string sub = args[0].ToLowerInvariant();
        switch (sub)
        {
            case "pipe": return await SetFormationAsync("Pipe", ActionPipe, g, output, ct).ConfigureAwait(false);
            case "block": return await SetFormationAsync("Block", ActionBlock, g, output, ct).ConfigureAwait(false);
            case "slot": return await SetFormationAsync("Slot Back", ActionSlot, g, output, ct).ConfigureAwait(false);

            case "join":
                if (g.IsLeader)
                {
                    await output.WriteLineAsync(AnsiPalette.Warn("you are the leader -- set a formation with `formation pipe|block|slot`")).ConfigureAwait(false);
                    return 1;
                }
                if (g.InFormation)
                {
                    await output.WriteLineAsync(AnsiPalette.Warn("already formed up -- `formation break` to drop out")).ConfigureAwait(false);
                    return 1;
                }
                await SendAsync(ActionJoin, ct).ConfigureAwait(false);
                await output.WriteLineAsync(AnsiPalette.Ok("forming up into the leader's formation")).ConfigureAwait(false);
                return 0;

            case "break":
                if (g.IsLeader)
                {
                    await SendAsync(ActionBreak, ct).ConfigureAwait(false);
                    await output.WriteLineAsync(AnsiPalette.Ok("breaking formation")).ConfigureAwait(false);
                }
                else
                {
                    await SendAsync(ActionLeave, ct).ConfigureAwait(false);
                    await output.WriteLineAsync(AnsiPalette.Ok("leaving formation")).ConfigureAwait(false);
                }
                return 0;

            default:
                await output.WriteLineAsync(AnsiPalette.Warn(
                    $"unknown formation verb '{sub}' -- expected pipe | block | slot | join | break")).ConfigureAwait(false);
                return 1;
        }
    }

    private async Task<int> SetFormationAsync(string label, int action, AuxDataRecord.GroupState g, TextWriter output, CancellationToken ct)
    {
        if (!g.IsLeader)
        {
            await output.WriteLineAsync(AnsiPalette.Warn("only the group leader can set a formation")).ConfigureAwait(false);
            return 1;
        }
        await SendAsync(action, ct).ConfigureAwait(false);
        await output.WriteLineAsync(
            AnsiPalette.Ok("formation set to ") + AnsiPalette.Value(label) +
            AnsiPalette.Muted(" -- members `formation join` to form up")).ConfigureAwait(false);
        return 0;
    }

    private Task SendAsync(int action, CancellationToken ct)
    {
        byte[] payload = CtaRequestBuilder.Build(_ctx.GameId!.Value, action);
        return _ctx.Sector!.SendAsync(
            Packet.ForOpcode(OpcodeId.Known.CtaRequest.Value, payload), ct);
    }
}
