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
/// into the surrounding sector (room -1 = in space), the single packet a real
/// client sends when the player clicks "Launch".
/// </summary>
/// <remarks>
/// <para>
/// Wire: a 0x009F STARBASE_ROOM_CHANGE (struct StarbaseRoomChange, 12 bytes,
/// little-endian: int32 AvatarID, then two room int32s). The retail client's
/// undock frame is byte-exact <c>[AvatarID LE][00 00 00 00][FF FF FF FF]</c> --
/// see the live cleartext proxy&lt;-&gt;server capture
/// <c>proxy/local-debug/net7-live-2026-06-01-login-undock-dock-logout.pcap</c>,
/// the seq=15 client-&gt;server frame to udp/3636 (t=9.46s, right after the
/// docked handshake completes). The trailing 0xFFFFFFFF (-1) is the field the
/// server's <c>Player::HandleStarbaseRoomChange</c> assigns to <c>m_Room</c> as
/// the destination, putting the avatar in space; the re-dock frame is the same
/// struct with the two room words swapped (<c>[FF FF FF FF][00 00 00 00]</c>).
/// </para>
/// <para>
/// We deliberately reproduce the retail bytes verbatim rather than build the
/// struct from its (suspect) C field names -- the capture is the authority on
/// byte order, and the field labels in PacketStructures.h disagree with it.
/// </para>
/// </remarks>
public sealed class UndockCommand : ICommandHandler
{
    private readonly SessionContext _ctx;
    public UndockCommand(SessionContext ctx) { ArgumentNullException.ThrowIfNull(ctx); _ctx = ctx; }

    public string Name    => "undock";
    public string Summary => "launch out of the station into space (0x009F room -> -1)";
    public string Usage   =>
        "undock\n" +
        "  Sends the retail Launch packet (0x009F STARBASE_ROOM_CHANGE with the\n" +
        "  destination room = -1), moving the docked avatar into the sector's open\n" +
        "  space. Follow with `list` to see the space objects, then `move`/`warp`.";
    public string? Placeholder => null;
    public bool Available => _ctx.Sector is not null && _ctx.GameId is not null;

    public async Task<int> ExecuteAsync(IReadOnlyList<string> args, TextWriter output, CancellationToken ct)
    {
        if (_ctx.Sector is null || _ctx.GameId is not { } id)
        {
            await output.WriteLineAsync(AnsiPalette.Warn("not in a sector -- run `enter` first")).ConfigureAwait(false);
            return 1;
        }

        await _ctx.Sector.SendAsync(
            Packet.ForOpcode(OpcodeId.Known.StarbaseRoomChange.Value, BuildUndockFrame(id)), ct).ConfigureAwait(false);

        await output.WriteLineAsync(
            AnsiPalette.Ok("undock requested ") +
            AnsiPalette.Muted("(0x009F room -> -1). Watch space fill in with `list`.")).ConfigureAwait(false);
        return 0;
    }

    /// <summary>
    /// Build the 12-byte STARBASE_ROOM_CHANGE payload a real client sends to
    /// undock: the avatar's sector GameID, then the two room words as they
    /// appear on the wire -- <c>0x00000000</c> followed by <c>0xFFFFFFFF</c>
    /// (-1). Byte-pinned to the live capture's seq=15 frame.
    /// </summary>
    public static byte[] BuildUndockFrame(int gameId)
    {
        var payload = new byte[12];
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(0), gameId);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(4), 0);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(8), -1);
        return payload;
    }
}
