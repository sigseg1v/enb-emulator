// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya

using System.Buffers.Binary;
using N7.CliClient.Logging;
using N7.CliClient.Net;
using N7.CliClient.Opcodes;

namespace N7.CliClient.Repl.Commands;

/// <summary>
/// <c>gate &lt;gid&gt;</c> -- jump through a stargate to its destination sector,
/// the two-step ACTION sequence a real client sends when the player selects a
/// gate and confirms the jump, then follow the server's handoff so the session
/// actually lands in the destination sector.
/// </summary>
/// <remarks>
/// <para>
/// Two ACTION packets, mirroring the real client's gate jump:
/// </para>
/// <list type="number">
///   <item>0x002C ACTION with <c>Action == 18</c> (gate button), Target = the
///   stargate's game id. The server's <c>Player::HandleAction</c>
///   (server/src/PlayerConnection.cpp:3923) requires the target be an
///   OT_STARGATE, sets <c>m_Gating</c>, terminates any warp, and calls
///   <c>GateActivate</c> -&gt; <c>SectorManager::Gate</c>
///   (server/src/SectorManager.cpp:639), which resolves and stores
///   <c>StargateDestination</c>, opens the gate, moves the avatar toward it,
///   and schedules a B_CAMERA_CONTROL event at +5800ms.</item>
///   <item>0x002C ACTION with <c>Action == 19</c> (finish gate sequence,
///   PlayerConnection.cpp:3965). If <c>StargateDestination() &gt; 0</c> the
///   server runs <c>SectorManager::SectorServerHandoff</c> to that sector and
///   emits a 0x003A SERVER_HANDOFF.</item>
/// </list>
/// <para>
/// There is NO server-side range check on either action -- the handoff fires
/// regardless of the avatar's distance from the gate (PlayerConnection.cpp
/// case 18/19 never test position). We still pace the two packets ~6s apart to
/// mirror the retail B_CAMERA_CONTROL delay rather than racing the server's
/// gate-open animation.
/// </para>
/// <para>
/// <c>SectorServerHandoff</c> (SectorManager.cpp:579) drops the player from the
/// old sector with <c>DropPlayerFromSector</c> (NOT DropPlayerFromGalaxy) and
/// keeps the player node alive -- identical semantics to LaunchIntoSpace -- so
/// the re-join reuses the SAME avatar id, exactly like <c>undock</c>. The
/// follow-the-handoff sequence is shared via <see cref="HandoffFollow"/>.
/// </para>
/// </remarks>
public sealed class GateCommand : ICommandHandler
{
    // Mirror the retail B_CAMERA_CONTROL delay (SectorManager::Gate schedules it
    // at +5800ms); send the finish (Action=19) just after so we don't race the
    // server's gate-open sequence.
    private static readonly TimeSpan GateAnimationDelay = TimeSpan.FromMilliseconds(6000);

    private readonly SessionContext _ctx;
    public GateCommand(SessionContext ctx) { ArgumentNullException.ThrowIfNull(ctx); _ctx = ctx; }

    public string Name    => "gate";
    public string Summary => "jump through a stargate to its destination sector (0x002C ACTION 18 -> 19)";
    public string Usage   =>
        "gate <name-or-gid>\n" +
        "  Target = a tracked stargate name (with spaces, e.g. `gate Mars Gate`)\n" +
        "  or its gid (0x.. / decimal). Tab-completes from `navs`. Sends the\n" +
        "  retail gate sequence (0x002C ACTION Action=18 to select the gate, then\n" +
        "  Action=19 ~6s later to confirm), waits for the server's 0x003A\n" +
        "  SERVER_HANDOFF, then re-joins the destination sector with the same\n" +
        "  avatar id. On success you are in the new sector; run `list`.";
    public string? Placeholder => "<name-or-gid>";
    public bool Available => _ctx.Sector is not null && _ctx.GameId is not null;

    // Tab-complete gate names from the tracked sector world (nearest first).
    // Whole-line so a name with spaces ("Mars Gate") completes as one unit.
    public bool WholeLineArg => true;
    public IReadOnlyList<string>? ArgCandidates =>
        _ctx.GameId is { } id
            ? _ctx.World.NavTargets(id).Where(t => t.IsGate).Select(t => t.Name).Distinct().ToArray()
            : null;

    public async Task<int> ExecuteAsync(IReadOnlyList<string> args, TextWriter output, CancellationToken ct)
    {
        if (_ctx.Sector is null || _ctx.GameId is not { } id)
        {
            await output.WriteLineAsync(AnsiPalette.Warn("not in a sector -- run `enter` then `undock` first")).ConfigureAwait(false);
            return 1;
        }
        if (_ctx.Ticket is null || _ctx.ActiveSlot is not { } slot)
        {
            await output.WriteLineAsync(AnsiPalette.Warn("session incomplete (no ticket/slot) -- run `login` then `enter`")).ConfigureAwait(false);
            return 1;
        }
        if (args.Count == 0)
        {
            await output.WriteLineAsync(AnsiPalette.Warn($"usage: {Usage}")).ConfigureAwait(false);
            return 1;
        }
        if (TargetArg.ResolveWords(args, _ctx.World) is not { } gate)
        {
            await output.WriteLineAsync(AnsiPalette.Warn(
                $"can't resolve '{string.Join(' ', args)}' to a gate -- give 0x.. / decimal gid or a tracked name (`navs`)")).ConfigureAwait(false);
            return 1;
        }

        var oldSector = _ctx.Sector;
        string gateLabel = _ctx.World.NameOf(gate) is { } n ? $"{n} (0x{gate:X8})" : $"0x{gate:X8}";

        // Arm the handoff capture BEFORE the finish packet, so the background
        // drain (which owns the sector socket) records the server's 0x003A
        // SERVER_HANDOFF reply cleanly -- we never cancel a mid-frame RC4
        // read on this connection.
        var handoffTask = _ctx.ArmHandoffCapture();

        // Step 1: Action=18 (gate button) -- select the gate. Target = gate gid.
        await oldSector.SendAsync(
            Packet.ForOpcode(OpcodeId.Known.Action.Value, BuildActionFrame(id, 18, gate, 0)), ct)
            .ConfigureAwait(false);
        await output.WriteLineAsync(
            AnsiPalette.Ok("gate selected ") + AnsiPalette.Value(gateLabel) +
            AnsiPalette.Muted(" (0x002C Action=18). Opening gate...")).ConfigureAwait(false);

        // Pace the finish to mirror the retail gate-open animation delay.
        try { await Task.Delay(GateAnimationDelay, ct).ConfigureAwait(false); }
        catch (OperationCanceledException)
        {
            await output.WriteLineAsync(AnsiPalette.Warn("gate cancelled before finish")).ConfigureAwait(false);
            return 1;
        }

        // Step 2: Action=19 (finish gate sequence) -- triggers the handoff to
        // StargateDestination. Target carries the gate gid for fidelity; the
        // server reads StargateDestination it stored on Action=18, not Target.
        await oldSector.SendAsync(
            Packet.ForOpcode(OpcodeId.Known.Action.Value, BuildActionFrame(id, 19, gate, 0)), ct)
            .ConfigureAwait(false);
        await output.WriteLineAsync(
            AnsiPalette.Ok("gate confirmed ") +
            AnsiPalette.Muted("(0x002C Action=19 -> SectorServerHandoff). Awaiting server handoff..."))
            .ConfigureAwait(false);

        HandoffTarget handoff;
        try
        {
            using var handoffCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            handoffCts.CancelAfter(TimeSpan.FromSeconds(15));
            handoff = await handoffTask.WaitAsync(handoffCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            await output.WriteLineAsync(AnsiPalette.Err(
                "no 0x003A SERVER_HANDOFF arrived within 15s -- not a stargate, or " +
                "gate had no resolvable destination")).ConfigureAwait(false);
            return 1;
        }

        await output.WriteLineAsync(
            AnsiPalette.Muted("handoff -> ") + AnsiPalette.Value(_ctx.SectorLabel(handoff.ToSectorId)) +
            AnsiPalette.Muted("; re-joining...")).ConfigureAwait(false);

        return await HandoffFollow.CompleteAsync(
            _ctx, output, id, slot, handoff.ToSectorId, handoff.FromSectorId, oldSector, "gated", ct)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Build the 16-byte ACTION payload (struct ActionPacket, all int32 LE:
    /// <c>GameID, Action, Target, OptionalVar</c>). The gate sequence uses
    /// Action 18 (select) and 19 (confirm) with Target = the stargate gid.
    /// </summary>
    public static byte[] BuildActionFrame(int gameId, int action, int target, int optionalVar)
    {
        var payload = new byte[16];
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(0),  gameId);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(4),  action);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(8),  target);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(12), optionalVar);
        return payload;
    }
}
