// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya

using N7.CliClient.Logging;
using N7.CliClient.Net;
using N7.CliClient.Opcodes;
using N7.CliClient.Opcodes.Outbound;

namespace N7.CliClient.Repl.Commands;

/// <summary>
/// <c>starbase &lt;talk|exit|job|jobdesc&gt; [targetId]</c> -- send one 0x004E
/// STARBASE_REQUEST. This is the docked-station interaction handshake: open an
/// NPC's talk tree (the entry point to the vendor / mission UI), exit the
/// station into space, or drive a job terminal. The buy/sell item transfer
/// itself is an inventory move (<c>inv ... vendor ...</c>, 0x0027); this opens
/// the terminal you transfer against.
/// </summary>
/// <remarks>
/// The wire PlayerID is the masked avatar id (session GameID with the top type
/// byte cleared), derived here as <c>GameId &amp; 0x00FFFFFF</c> to match the
/// retail client. Byte format pinned in
/// <see cref="N7.CliClient.Opcodes.Outbound.StarbaseRequestCodec"/>.
/// </remarks>
public sealed class StarbaseCommand : ICommandHandler
{
    private readonly SessionContext _ctx;
    public StarbaseCommand(SessionContext ctx) { ArgumentNullException.ThrowIfNull(ctx); _ctx = ctx; }

    public string Name    => "starbase";
    public string Summary => "interact with a docked station (talk to NPC / exit / job terminal)";
    public string Usage   =>
        "starbase <talk|exit|job|jobdesc> [targetId]\n" +
        "  talk <id>    open the talk tree of NPC/fixture <id> (vendor, mission, ...)\n" +
        "  exit         launch back into space\n" +
        "  job <id>     open the job terminal <id>\n" +
        "  jobdesc <id> pull up the description of job <id>\n" +
        "  e.g.  starbase talk 452     open vendor NPC 452's terminal\n" +
        "        starbase exit         undock";
    public string? Placeholder => "<talk|exit|job|jobdesc> [targetId]";
    public bool Available => _ctx.Sector is not null && _ctx.GameId is not null;

    public async Task<int> ExecuteAsync(IReadOnlyList<string> args, TextWriter output, CancellationToken ct)
    {
        if (_ctx.Sector is null || _ctx.GameId is not { } gameId)
        {
            await output.WriteLineAsync(AnsiPalette.Warn("not in a sector -- run `enter` first")).ConfigureAwait(false);
            return 1;
        }
        if (args.Count < 1)
        {
            await output.WriteLineAsync(AnsiPalette.Warn($"usage: {Usage}")).ConfigureAwait(false);
            return 1;
        }

        string verb = args[0].ToLowerInvariant();
        byte action = verb switch
        {
            "talk"    => StarbaseRequestMessage.ActionTalkToNpc,
            "exit"    => StarbaseRequestMessage.ActionExitStation,
            "job"     => StarbaseRequestMessage.ActionJobTerminal,
            "jobdesc" => StarbaseRequestMessage.ActionJobDescription,
            _         => 0,
        };
        if (action == 0)
        {
            await output.WriteLineAsync(AnsiPalette.Warn($"unknown action '{verb}' -- usage: {Usage}")).ConfigureAwait(false);
            return 1;
        }

        // exit needs no target; the others identify a fixture/NPC/job.
        int targetId = 0;
        if (action != StarbaseRequestMessage.ActionExitStation)
        {
            if (args.Count < 2)
            {
                await output.WriteLineAsync(AnsiPalette.Warn($"'{verb}' needs a targetId -- usage: {Usage}")).ConfigureAwait(false);
                return 1;
            }
            if (!int.TryParse(args[1], out targetId))
            {
                await output.WriteLineAsync(AnsiPalette.Warn("targetId must be an integer")).ConfigureAwait(false);
                return 1;
            }
        }

        // The PlayerID field carries the masked avatar id (top type byte cleared).
        int playerId = gameId & 0x00FFFFFF;

        byte[] payload = new StarbaseRequestCodec().EncodeOutbound(
            new StarbaseRequestMessage(playerId, targetId, action));

        await _ctx.Sector.SendAsync(
            Packet.ForOpcode(OpcodeId.Known.StarbaseRequest.Value, payload), ct).ConfigureAwait(false);

        await output.WriteLineAsync(
            AnsiPalette.Ok("starbase request sent: ") +
            AnsiPalette.Value($"{verb}" + (action == StarbaseRequestMessage.ActionExitStation ? "" : $" {targetId}"))).ConfigureAwait(false);
        return 0;
    }
}
