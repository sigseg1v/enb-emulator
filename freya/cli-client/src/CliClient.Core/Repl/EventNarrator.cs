// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using N7.CliClient.Logging;
using N7.CliClient.Net;

namespace N7.CliClient.Repl;

/// <summary>
/// Turns inbound frames into one-line human-readable notices for the REPL --
/// "Mob (L17, hostile, Mbonae) appeared in scanner range, d=1234", "Took 412
/// damage from Mbonae", "Warp ended", "Loading sector 1710", and so on.
///
/// <para>
/// Every notice is sourced from a faithfully-pinned signal -- the
/// <see cref="SectorWorld"/> model (for scanner contacts) or a directly-parsed
/// packet field (damage / warp / handoff / system message). It never guesses an
/// unpinned wire field and never overstates what the wire says (e.g. it narrates
/// "warp ended", not "interrupted", because 0x009C WARP_INDEX = -1 is the single
/// signal the server sends for both a boundary interrupt and a normal arrival).
/// </para>
///
/// <para>Pure formatting: it reads state and returns strings; the caller decides
/// where they go. Never throws -- a malformed frame yields no notice.</para>
/// </summary>
public sealed class EventNarrator
{
    private readonly SectorWorld _world;
    private readonly Func<int?> _selfGameId;

    public EventNarrator(SectorWorld world, Func<int?> selfGameId)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _selfGameId = selfGameId ?? throw new ArgumentNullException(nameof(selfGameId));
    }

    /// <summary>
    /// Produce the coloured notice lines for one inbound frame, given the world
    /// events <see cref="SectorWorld.Ingest"/> already derived from it. Returns
    /// an empty list when nothing is worth narrating.
    /// </summary>
    public IReadOnlyList<string> Narrate(Packet p, IReadOnlyList<WorldEvent> worldEvents)
    {
        var lines = new List<string>();
        try
        {
            int? self = _selfGameId();

            foreach (var ev in worldEvents)
            {
                // The player's own ship is not a "scanner contact".
                if (self is { } sid && ev.GameId == sid) continue;
                string? line = ev.Kind switch
                {
                    WorldEventKind.Appeared => AppearedLine(ev.GameId, self),
                    WorldEventKind.Departed => DepartedLine(ev),
                    _ => null,
                };
                if (line is not null) lines.Add(line);
            }

            switch (p.Header.Opcode)
            {
                case 0x0064: AddIfNotNull(lines, DamageLine(p.Payload.Span, self)); break;
                case 0x009C: AddIfNotNull(lines, WarpLine(p.Payload.Span)); break;
                case 0x003A: AddIfNotNull(lines, SectorLoadLine(p.Payload.Span)); break;
                case 0x001D: AddIfNotNull(lines, SystemMessageLine(p.Payload.Span)); break;
            }
        }
        catch
        {
            // Best-effort: a notice is a convenience, never a failure point.
        }
        return lines;
    }

    private static void AddIfNotNull(List<string> lines, string? line)
    {
        if (line is not null) lines.Add(line);
    }

    private string? AppearedLine(int gameId, int? self)
    {
        var d = _world.Describe(gameId, self ?? 0);
        if (d is not { } info) return null;
        string kind = Capitalize(info.Kind);
        string name = info.Name ?? $"<gid=0x{gameId:X8}>";
        var parts = new List<string>(3);
        if (info.Level is { } lv) parts.Add($"L{lv.ToString(CultureInfo.InvariantCulture)}");
        if (info.Reaction is not null) parts.Add(SectorWorld.ReactionName(info.Reaction));
        parts.Add(name);
        string dist = info.Dist is { } dd
            ? $", d={dd.ToString("0", CultureInfo.InvariantCulture)}"
            : "";
        return AnsiPalette.Colorize(AnsiPalette.BrightCyan,
            $"* {kind} ({string.Join(", ", parts)}) appeared in scanner range{dist}");
    }

    private static string DepartedLine(WorldEvent ev)
    {
        string kind = Capitalize(ev.DepartedKind ?? "object");
        string name = ev.DepartedName ?? $"<gid=0x{ev.GameId:X8}>";
        return AnsiPalette.Muted($"* {kind} ({name}) left scanner range");
    }

    // 0x0064 CLIENT_DAMAGE: Damage f32 @0, SourceId i32 @16, TargetId i32 @20
    // (ClientDamageRecord). Narrate only damage TO us; a sector-wide damage
    // event for some other combatant is not the operator's concern.
    private string? DamageLine(ReadOnlySpan<byte> s, int? self)
    {
        if (s.Length < 24) return null;
        float dmg = BinaryPrimitives.ReadSingleLittleEndian(s.Slice(0, 4));
        int source = BinaryPrimitives.ReadInt32LittleEndian(s.Slice(16, 4));
        int target = BinaryPrimitives.ReadInt32LittleEndian(s.Slice(20, 4));
        if (self is not { } sid || target != sid) return null;
        string from = _world.NameOf(source) is { Length: > 0 } n
            ? $" from {n}" : "";
        return AnsiPalette.Colorize(AnsiPalette.Red,
            $"* Took {dmg.ToString("0", CultureInfo.InvariantCulture)} damage{from}");
    }

    // 0x009C WARP_INDEX: single i32 LE. >= 0 = advancing to nav leg N
    // (SendWarpIndex(m_WarpNavIndex)); -1 = warp ended (TerminateWarp ->
    // SendWarpIndex(-1), PlayerClass.cpp:2590). The server uses the SAME -1 for
    // a boundary interrupt and a normal arrival, so we say "ended", not
    // "interrupted".
    private static string? WarpLine(ReadOnlySpan<byte> s)
    {
        if (s.Length < 4) return null;
        int idx = BinaryPrimitives.ReadInt32LittleEndian(s.Slice(0, 4));
        return idx < 0
            ? AnsiPalette.Warn("* Warp ended")
            : AnsiPalette.Warn($"* Warping (leg {idx.ToString(CultureInfo.InvariantCulture)})");
    }

    // 0x003A SERVER_HANDOFF: ToSectorID at offset 20, BIG-ENDIAN (the server
    // writes it via ntohl -- SendServerHandoff, PlayerConnection.cpp:10167 --
    // while the rest of the struct is host LE). This is the "loading a new
    // sector" signal.
    private static string? SectorLoadLine(ReadOnlySpan<byte> s)
    {
        if (s.Length < 24) return null;
        int toSector = BinaryPrimitives.ReadInt32BigEndian(s.Slice(20, 4));
        return AnsiPalette.Ok($"* Loading sector {toSector.ToString(CultureInfo.InvariantCulture)}...");
    }

    // 0x001D MESSAGE_STRING: int16 len (strlen+1), u8 color, char[len] msg
    // (NUL-terminated). This is the server's system-notice channel -- it
    // carries "You have left the group" and similar status text -- so surfacing
    // it faithfully covers leave-group and other notices without guessing.
    private static string? SystemMessageLine(ReadOnlySpan<byte> s)
    {
        if (s.Length < 4) return null;
        int len = BinaryPrimitives.ReadUInt16LittleEndian(s.Slice(0, 2));
        if (len <= 1 || 3 + len > s.Length) return null;
        string msg = Encoding.Latin1.GetString(s.Slice(3, len - 1)).TrimEnd('\0');
        if (msg.Length == 0) return null;
        return AnsiPalette.Colorize(AnsiPalette.Green, $"* {msg}");
    }

    private static string Capitalize(string s) =>
        s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
}
