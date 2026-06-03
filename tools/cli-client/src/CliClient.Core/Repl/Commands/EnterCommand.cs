// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using N7.CliClient.Logging;
using N7.CliClient.Net;

namespace N7.CliClient.Repl.Commands;

/// <summary>
/// <c>enter &lt;name&gt;</c> -- run the GlobalTicketRequest -> MasterJoin
/// -> sector LOGIN -> drain-to-START handshake against an existing
/// avatar in the cached list. Prints the sector arrival summary from the
/// shared <see cref="SectorWorld"/>.
/// </summary>
public sealed class EnterCommand : ICommandHandler
{
    private readonly SessionContext _ctx;

    public EnterCommand(SessionContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        _ctx = ctx;
    }

    public string Name    => "enter";
    public string Summary => "enter the world as the named character";
    public string Usage   => "enter <firstname>";
    public string? Placeholder => "<firstname>";

    // Available once logged in with a character list to pick from; a primary
    // next step alongside `create`.
    public bool Available => _ctx.Global is not null && _ctx.AvatarList is not null;
    public int Priority => 100;

    public async Task<int> ExecuteAsync(
        IReadOnlyList<string> args, TextWriter output, CancellationToken ct)
    {
        if (_ctx.Global is null || _ctx.AvatarList is null)
        {
            await output.WriteLineAsync(
                AnsiPalette.Warn("not logged in -- run `login` first")).ConfigureAwait(false);
            return 1;
        }
        if (_ctx.Sector is not null)
        {
            await output.WriteLineAsync(AnsiPalette.Warn(
                $"already in sector {_ctx.ActiveSectorId} -- restart the REPL to switch"))
                .ConfigureAwait(false);
            return 1;
        }
        if (args.Count < 1)
        {
            await output.WriteLineAsync(
                AnsiPalette.Warn("usage: enter <firstname>")).ConfigureAwait(false);
            return 1;
        }

        string firstName = args[0];
        int slot = -1;
        int sectorId = -1;
        for (int i = 0; i < _ctx.AvatarList.Avatars.Length; i++)
        {
            var a = _ctx.AvatarList.Avatars[i];
            if (string.Equals(a.Data.FirstName, firstName, StringComparison.OrdinalIgnoreCase))
            {
                slot = i;
                sectorId = a.Info.SectorId > 0
                    ? a.Info.SectorId
                    : CharacterClass.StartSector(a.Data.Race, a.Data.Profession);
                break;
            }
        }
        if (slot < 0)
        {
            await output.WriteLineAsync(AnsiPalette.Err(
                $"no character named '{firstName}' in the cached list (run `list`)"))
                .ConfigureAwait(false);
            return 1;
        }

        await output.WriteLineAsync(
            AnsiPalette.Muted("entering sector ") + AnsiPalette.Value($"{sectorId}") +
            AnsiPalette.Muted(" on slot ") + AnsiPalette.Value($"{slot}") +
            AnsiPalette.Muted(" as ") + AnsiPalette.Accent(firstName) + AnsiPalette.Muted("..."))
            .ConfigureAwait(false);

        SectorEnterDriver.SectorEntryResult result;
        try
        {
            result = await SectorEnterDriver.EnterAsync(_ctx, _ctx.Global, slot, sectorId, ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await output.WriteLineAsync(
                AnsiPalette.Err($"enter failed: {ex.Message}")).ConfigureAwait(false);
            return 1;
        }

        _ctx.Sector = result.Sector;
        _ctx.GameId = result.GameId;
        _ctx.ActiveSlot = result.Slot;
        _ctx.ActiveSectorId = result.SectorId;

        // Fresh sector -> fresh world model.
        _ctx.World.Reset();

        // SectorEnterDriver drains the LOGIN handshake on a brand-new
        // sector connection BEFORE we set _ctx.Sector, so the hooks never
        // saw those frames. Replay them in arrival order now: this feeds
        // the world model, echoes any chat, and dumps when dump-on is set.
        foreach (var f in result.HandshakeFrames)
            _ctx.ReplayInboundFrame(f);

        await output.WriteLineAsync(
            AnsiPalette.Ok("in-sector: ") +
            AnsiPalette.Muted("gameId=") + AnsiPalette.Value($"0x{result.GameId:X8}") + " " +
            AnsiPalette.Muted("startId=") + AnsiPalette.Value($"0x{result.StartId:X8}") + " " +
            AnsiPalette.Muted("handshake-frames=") + AnsiPalette.Value($"{result.HandshakeFrames.Count}"))
            .ConfigureAwait(false);

        // The 0x0005 START frame terminates the handshake but more CREATE /
        // positional / nav frames keep arriving for several seconds after
        // that as the sector finishes its initial fanout. These land via
        // the packet hook (Sector is set) and feed the world model too.
        // Drain for 2s so the arrival summary has a fuller picture without
        // hanging the prompt.
        await DrainBriefly(result.Sector, TimeSpan.FromSeconds(2), ct).ConfigureAwait(false);

        // Foreground drain window is closed; hand the single-reader sector
        // socket to the background drain so async traffic (chat, spawns,
        // positional updates) keeps flowing through the hooks while the
        // prompt is idle. This is what makes `chat`/`list` stay current and
        // dump-on tail without a foreground reader.
        _ctx.StartSectorDrain();

        // Keep the session alive past the server's 120s idle reaper. A live
        // client relies on its proxy's MVAS position stream for this; a
        // headless REPL has none, so it sends its own periodic REQUEST_TIME.
        _ctx.StartKeepalive();

        // Mirror the retail client's MVAS keepalive: a periodic 0x3005
        // COMMS_ALIVE on the UDP MVAS port, sent whether or not the avatar is
        // moving, so an idle in-space avatar refreshes LastAccessTime the same
        // way the real client does (not only via the TCP REQUEST_TIME above).
        _ctx.StartCommsAlive();

        _ctx.World.Render(output, result.GameId);
        return 0;
    }

    private static async Task DrainBriefly(
        EncryptedTcpConnection conn, TimeSpan window, CancellationToken outer)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(outer);
        cts.CancelAfter(window);
        try
        {
            while (!cts.IsCancellationRequested)
            {
                var p = await conn.ReceiveAsync(cts.Token).ConfigureAwait(false);
                if (p is null) break;
                // The packet hook feeds the world model; nothing to collect.
            }
        }
        catch (OperationCanceledException)
        {
            // expected when the window expires
        }
        catch (Exception)
        {
            // best-effort drain; suppress so the summary still prints
        }
    }
}
