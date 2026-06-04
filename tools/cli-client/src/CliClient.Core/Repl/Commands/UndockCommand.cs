// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Buffers.Binary;
using N7.CliClient.Logging;
using N7.CliClient.Net;
using N7.CliClient.Opcodes;

namespace N7.CliClient.Repl.Commands;

/// <summary>
/// <c>undock</c> -- launch the avatar out of the station it is docked in and
/// into the surrounding sector's open space, the packet a real client sends
/// when the player clicks "Launch".
/// </summary>
/// <remarks>
/// <para>
/// Wire: 0x004E STARBASE_REQUEST (struct StarbaseRequest, 9 bytes:
/// <c>int32 PlayerID, int32 StarbaseID, byte Action</c>) with
/// <c>Action == 1</c> ("exit station"). The server's
/// <c>Player::HandleStarbaseRequest</c> (server/src/PlayerConnection.cpp:9848)
/// routes Action 1 to <c>SectorManager::LaunchIntoSpace</c>
/// (server/src/SectorManager.cpp:558), which drops the player from the
/// station sector, computes the parent SPACE sector as
/// <c>station_sector_id / 10</c> (e.g. Guiana 10251 -> 1025), and issues a
/// 0x003A SERVER_HANDOFF to that sector. This -- NOT 0x009F -- is what
/// actually puts the avatar in space. (0x009F STARBASE_ROOM_CHANGE only
/// walks the avatar between rooms WITHIN the station; in the live capture it
/// fires several times around the launch but never triggers the handoff.)
/// </para>
/// <para>
/// Primary source: the live cleartext proxy&lt;-&gt;server capture
/// <c>proxy/local-debug/net7-live-2026-06-01-login-undock-dock-logout.pcap</c>,
/// the seq=26 client-&gt;server frame to udp/3636. After the sector framing
/// (<c>[len][opcode][GameID][seq]</c>) the 0x004E payload is exactly
/// <c>2a 99 03 00  53 4e 00 00  01</c> -- PlayerID 0x0003992a (the avatar's
/// GameID with the 0x40000000 PLAYER_TAG bit cleared), StarbaseID 0x00004e53,
/// Action 1.
/// </para>
/// <para>
/// For Action 1 the server uses the connection-bound Player and ignores both
/// PlayerID and StarbaseID (PlayerConnection.cpp:9879 case 1 calls
/// <c>LaunchIntoSpace(this)</c> with neither field), so the launch fires
/// regardless of what we put there. We still reproduce the retail field
/// values for fidelity: PlayerID = our untagged GameID, StarbaseID = 0.
/// </para>
/// </remarks>
public sealed class UndockCommand : ICommandHandler
{
    // server/src/SectorData.h:29 -- #define PLAYER_TAG (1<<30)
    private const int PlayerTag = 1 << 30;

    private readonly SessionContext _ctx;
    public UndockCommand(SessionContext ctx) { ArgumentNullException.ThrowIfNull(ctx); _ctx = ctx; }

    public string Name    => "undock";
    public string Summary => "launch out of the station into space (0x004E StarbaseRequest, Action=1)";
    public string Usage   =>
        "undock\n" +
        "  Sends the retail Launch packet (0x004E STARBASE_REQUEST with Action=1).\n" +
        "  The server drops you from the station sector and hands you off to the\n" +
        "  parent space sector (station_id / 10). Follow with `list` to see the\n" +
        "  space objects, then `move`/`warp`.";
    public string? Placeholder => null;
    public bool Available => _ctx.Sector is not null && _ctx.GameId is not null;

    public async Task<int> ExecuteAsync(IReadOnlyList<string> args, TextWriter output, CancellationToken ct)
    {
        if (_ctx.Sector is null || _ctx.GameId is not { } id)
        {
            await output.WriteLineAsync(AnsiPalette.Warn("not in a sector -- run `enter` first")).ConfigureAwait(false);
            return 1;
        }

        // Match the retail convention: PlayerID is the GameID with the
        // PLAYER_TAG bit cleared. StarbaseID is ignored by the server for
        // Action 1, so 0 is correct.
        int playerId = id & ~PlayerTag;

        await _ctx.Sector.SendAsync(
            Packet.ForOpcode(OpcodeId.Known.StarbaseRequest.Value, BuildUndockFrame(playerId, 0)), ct)
            .ConfigureAwait(false);

        await output.WriteLineAsync(
            AnsiPalette.Ok("undock requested ") +
            AnsiPalette.Muted("(0x004E Action=1 -> LaunchIntoSpace). Watch space fill in with `list`."))
            .ConfigureAwait(false);
        return 0;
    }

    /// <summary>
    /// Build the 9-byte STARBASE_REQUEST payload a real client sends to
    /// launch out of a station: <c>[PlayerID LE][StarbaseID LE][Action=1]</c>.
    /// Byte-pinned to the live capture's seq=26 frame.
    /// </summary>
    public static byte[] BuildUndockFrame(int playerId, int starbaseId)
    {
        var payload = new byte[9];
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(0), playerId);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(4), starbaseId);
        payload[8] = 1; // Action = 1 (exit station / launch into space)
        return payload;
    }
}
