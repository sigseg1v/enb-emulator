// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using N7.CliClient.Logging;
using N7.CliClient.Net;

namespace N7.CliClient.Repl;

/// <summary>
/// Shared "the server handed us off to another sector -- re-join it" sequence,
/// used by every command that triggers a 0x003A SERVER_HANDOFF and has to
/// follow it: <c>undock</c> (0x004E LaunchIntoSpace) and <c>gate</c>
/// (0x002C ACTION 18-&gt;19 SectorServerHandoff). Both server paths drop the
/// player from the OLD sector with <c>DropPlayerFromSector</c> (NOT
/// DropPlayerFromGalaxy) and keep the player node alive, so the re-join reuses
/// the SAME GameId -- no fresh GlobalTicketRequest. See
/// <see cref="SectorEnterDriver.FollowHandoffAsync"/>.
/// </summary>
public static class HandoffFollow
{
    /// <summary>
    /// Quiesce the loops that own the old sector socket, re-join
    /// <paramref name="toSectorId"/> with the existing <paramref name="gameId"/>,
    /// swap the active connection over, reset+repopulate the world model, and
    /// restart the keepalive loops. Disposes <paramref name="oldSector"/> on
    /// success. Returns 0 on success, 1 if the re-join failed (message already
    /// written to <paramref name="output"/>).
    /// </summary>
    /// <param name="arrivedLabel">Short lead word for the success line, e.g.
    /// "in space" (undock) or "gated".</param>
    public static async Task<int> CompleteAsync(
        SessionContext ctx,
        TextWriter output,
        int gameId,
        int slot,
        int toSectorId,
        EncryptedTcpConnection oldSector,
        string arrivedLabel,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(oldSector);

        // The old sector connection is finished: the handoff dropped us from
        // that sector. Quiesce the background loops that own it; the RC4-stream
        // position on the old socket no longer matters -- we re-join fresh.
        await ctx.StopKeepaliveAsync().ConfigureAwait(false);
        await ctx.StopCommsAliveAsync().ConfigureAwait(false);
        await ctx.StopSectorDrainAsync().ConfigureAwait(false);

        SectorEnterDriver.SectorEntryResult result;
        try
        {
            result = await SectorEnterDriver.FollowHandoffAsync(ctx, gameId, slot, toSectorId, ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await output.WriteLineAsync(AnsiPalette.Err($"handoff re-join failed: {ex.Message}")).ConfigureAwait(false);
            return 1;
        }

        // Swap the active sector connection to the new sector and dispose the
        // old one (the Sector setter detaches its hooks first).
        ctx.Sector = result.Sector;
        ctx.ActiveSectorId = result.SectorId;
        await oldSector.DisposeAsync().ConfigureAwait(false);

        // Fresh sector -> fresh world model. Replay the re-join handshake frames
        // (consumed before Sector was set, so the hooks missed them) to feed the
        // world model and dump tail.
        ctx.World.Reset();
        foreach (var f in result.HandshakeFrames)
            ctx.ReplayInboundFrame(f);

        // Hand the new socket to the background drain + restart the keepalives
        // (the avatar now refreshes LastAccessTime the same way a post-`enter`
        // session does).
        ctx.StartSectorDrain();
        ctx.StartKeepalive();
        ctx.StartCommsAlive();

        await output.WriteLineAsync(
            AnsiPalette.Ok($"{arrivedLabel}: ") +
            AnsiPalette.Muted("sector=") + AnsiPalette.Value(ctx.SectorLabel(result.SectorId)) + " " +
            AnsiPalette.Muted("handshake-frames=") + AnsiPalette.Value($"{result.HandshakeFrames.Count}") +
            AnsiPalette.Muted(" -- run `list` for the sector objects.")).ConfigureAwait(false);

        ctx.World.Render(output, result.GameId);
        return 0;
    }
}
