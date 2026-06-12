// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Linq;
using N7.CliClient.Logging;
using N7.CliClient.Net;
using N7.CliClient.Opcodes;

namespace N7.CliClient.Repl.Commands;

/// <summary>
/// <c>dock &lt;station name-or-gid&gt;</c> -- dock at a nearby station, the
/// two-step ACTION sequence a real client sends when the player selects a
/// station and confirms the dock, then follow the server's handoff so the
/// session actually lands in the station interior (not just fires the request).
/// </summary>
/// <remarks>
/// <para>
/// Two ACTION packets, the dock-side mirror of <see cref="GateCommand"/>'s
/// gate jump (18 -&gt; 19):
/// </para>
/// <list type="number">
///   <item>0x002C ACTION with <c>Action == 28</c> (dock button), Target = the
///   station's game id. <c>Player::HandleAction</c>
///   (server/src/PlayerConnection.cpp:4279) resolves the target via
///   <c>myAction->Target</c> (PlayerConnection.cpp:3830), requires it be an
///   <c>OT_STATION</c> with <c>m_Gating == false</c>, sets <c>m_Gating</c>,
///   registers the station, then calls <c>SectorManager::Dock</c>
///   (server/src/SectorManager.cpp), which resolves the station's destination
///   interior sector (<c>obj->Destination()</c>), stores it via
///   <c>SetStargateDestination</c>, sends a camera-control + approach, and
///   moves the avatar toward the station.</item>
///   <item>0x002C ACTION with <c>Action == 7</c> (docking complete,
///   PlayerConnection.cpp:3881). If <c>StargateDestination() &gt; 0</c> the
///   server runs <c>SectorManager::SectorServerHandoff</c> to that interior
///   sector and emits a 0x003A SERVER_HANDOFF.</item>
/// </list>
/// <para>
/// As with gate, there is NO server-side range check on either action -- the
/// handoff fires regardless of the avatar's distance from the station. We
/// still pace the two packets ~6s apart to mirror the retail dock-approach
/// camera rather than racing the server's dock animation. (The exact retail
/// delay is a real-client-verification item; the format is what the CLI pins.)
/// </para>
/// <para>
/// The destination interior sector id is &gt; 9999 (the station id-space the
/// server keys StationLogin on), so after a successful dock <c>undock</c>
/// becomes available and <c>dock</c> hides -- the inverse of the space state.
/// <c>SectorServerHandoff</c> drops the player from the old sector with
/// <c>DropPlayerFromSector</c> and keeps the player node alive, so the re-join
/// reuses the SAME avatar id; the follow-the-handoff sequence is shared via
/// <see cref="HandoffFollow"/>.
/// </para>
/// </remarks>
public sealed class DockCommand : ICommandHandler
{
    // Mirror the retail dock-approach camera delay; send the finish (Action=7)
    // just after so we do not race the server's dock sequence. Same pacing as
    // the gate jump's B_CAMERA_CONTROL delay.
    private static readonly TimeSpan DockAnimationDelay = TimeSpan.FromMilliseconds(6000);

    private readonly SessionContext _ctx;
    public DockCommand(SessionContext ctx) { ArgumentNullException.ThrowIfNull(ctx); _ctx = ctx; }

    public string Name    => "dock";
    public string Summary => "dock at a nearby station by name or gid (0x002C ACTION 28 -> 7)";
    public string Usage   =>
        "dock <name-or-gid>\n" +
        "  Target = a tracked station name (with spaces, e.g. `dock Mars Station`)\n" +
        "  or its gid (0x.. / decimal). Tab-completes from `navs`. Sends the\n" +
        "  retail dock sequence (0x002C ACTION Action=28 to select the station,\n" +
        "  then Action=7 ~6s later to confirm), waits for the server's 0x003A\n" +
        "  SERVER_HANDOFF, then re-joins the station interior with the same avatar\n" +
        "  id. On success you are docked; use `undock` to launch back into space.";
    public string? Placeholder => "<name-or-gid>";

    // Dockable only from open space (a station-interior sector is > 9999, where
    // there is no station to approach -- use `undock` instead). Mirrors the
    // undock availability gate inverted.
    public bool Available =>
        _ctx.Sector is not null && _ctx.GameId is not null && _ctx.ActiveSectorId is not > 9999;

    // Tab-complete station names from the tracked sector world (nearest first).
    // Whole-line so a name with spaces completes as one unit.
    public bool WholeLineArg => true;
    public IReadOnlyList<string>? ArgCandidates =>
        _ctx.GameId is { } id ? _ctx.World.StationNames(id) : null;

    public async Task<int> ExecuteAsync(IReadOnlyList<string> args, TextWriter output, CancellationToken ct)
    {
        if (_ctx.Sector is null || _ctx.GameId is not { } id)
        {
            await output.WriteLineAsync(AnsiPalette.Warn("not in a sector -- run `enter` first")).ConfigureAwait(false);
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
        if (TargetArg.ResolveWords(args, _ctx.World) is not { } station)
        {
            await output.WriteLineAsync(AnsiPalette.Warn(
                $"can't resolve '{string.Join(' ', args)}' to a station -- give 0x.. / decimal gid or a tracked name (`navs`)")).ConfigureAwait(false);
            return 1;
        }

        var oldSector = _ctx.Sector;
        string stationLabel = _ctx.World.NameOf(station) is { } n ? $"{n} (0x{station:X8})" : $"0x{station:X8}";

        // Arm the handoff capture BEFORE the finish packet, so the background
        // drain (which owns the sector socket) records the server's 0x003A
        // SERVER_HANDOFF reply cleanly -- we never cancel a mid-frame RC4
        // read on this connection.
        var handoffTask = _ctx.ArmHandoffCapture();

        // Step 1: Action=28 (dock button) -- select the station. Target = gid.
        // Reuse the shared 16-byte ActionPacket builder.
        await oldSector.SendAsync(
            Packet.ForOpcode(OpcodeId.Known.Action.Value, GateCommand.BuildActionFrame(id, 28, station, 0)), ct)
            .ConfigureAwait(false);
        await output.WriteLineAsync(
            AnsiPalette.Ok("dock selected ") + AnsiPalette.Value(stationLabel) +
            AnsiPalette.Muted(" (0x002C Action=28). Approaching station...")).ConfigureAwait(false);

        // Pace the finish to mirror the retail dock-approach animation delay.
        try { await Task.Delay(DockAnimationDelay, ct).ConfigureAwait(false); }
        catch (OperationCanceledException)
        {
            await output.WriteLineAsync(AnsiPalette.Warn("dock cancelled before finish")).ConfigureAwait(false);
            return 1;
        }

        // Step 2: Action=7 (docking complete) -- triggers the handoff to the
        // StargateDestination the server stored on Action=28. Target carries the
        // station gid for fidelity; the server reads StargateDestination, not Target.
        await oldSector.SendAsync(
            Packet.ForOpcode(OpcodeId.Known.Action.Value, GateCommand.BuildActionFrame(id, 7, station, 0)), ct)
            .ConfigureAwait(false);
        await output.WriteLineAsync(
            AnsiPalette.Ok("dock confirmed ") +
            AnsiPalette.Muted("(0x002C Action=7 -> SectorServerHandoff). Awaiting server handoff..."))
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
                "no 0x003A SERVER_HANDOFF arrived within 15s -- not a station, " +
                "station has no destination set, or it is offline")).ConfigureAwait(false);
            return 1;
        }

        await output.WriteLineAsync(
            AnsiPalette.Muted("handoff -> ") + AnsiPalette.Value(_ctx.SectorLabel(handoff.ToSectorId)) +
            AnsiPalette.Muted("; re-joining...")).ConfigureAwait(false);

        return await HandoffFollow.CompleteAsync(
            _ctx, output, id, slot, handoff.ToSectorId, handoff.FromSectorId, oldSector, "docked", ct)
            .ConfigureAwait(false);
    }
}
