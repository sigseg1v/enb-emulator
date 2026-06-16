// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya

using N7.CliClient.Logging;
using N7.CliClient.Net;
using N7.CliClient.Opcodes.Records;
using N7.CliClient.Replay;

namespace N7.CliClient.Repl.Commands;

/// <summary>
/// <c>dump &lt;firstname&gt; [--compare path/to/replay.bin] [--drain seconds]</c>
///
/// Run the same global -> master -> sector handshake as <c>enter</c>, but
/// pretty-print a structured description of every received frame so
/// fields with corrupted/null/nonsense values are visually obvious.
/// Optional <c>--compare</c> aligns each received frame with the matching
/// frame in an ENBREPLAY retail capture and flags opcode / length / byte
/// divergences. <c>--drain</c> controls how long we keep listening past
/// the 0x0005 START terminator (default 3 s).
/// </summary>
public sealed class DumpCommand : ICommandHandler
{
    private readonly SessionContext _ctx;

    public DumpCommand(SessionContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        _ctx = ctx;
    }

    public string Name => "dump";
    public string Summary => "enter the sector and structurally-dump every frame the server sends";
    public string Usage => "dump <firstname> [--compare <replay.bin>] [--drain <seconds>]";

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
                AnsiPalette.Warn($"usage: {Usage}")).ConfigureAwait(false);
            return 1;
        }

        string firstName = args[0];
        string? comparePath = null;
        TimeSpan drain = TimeSpan.FromSeconds(3);

        for (int i = 1; i < args.Count; i++)
        {
            switch (args[i])
            {
                case "--compare":
                    if (i + 1 >= args.Count)
                    {
                        await output.WriteLineAsync(
                            AnsiPalette.Err("--compare needs a file path")).ConfigureAwait(false);
                        return 1;
                    }
                    comparePath = args[++i];
                    break;
                case "--drain":
                    if (i + 1 >= args.Count || !int.TryParse(args[++i], out int secs))
                    {
                        await output.WriteLineAsync(
                            AnsiPalette.Err("--drain needs an integer seconds value")).ConfigureAwait(false);
                        return 1;
                    }
                    drain = TimeSpan.FromSeconds(Math.Max(0, secs));
                    break;
                default:
                    await output.WriteLineAsync(
                        AnsiPalette.Err($"unknown option: {args[i]}")).ConfigureAwait(false);
                    return 1;
            }
        }

        int slot = -1, sectorId = -1;
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

        ReplayFile? replay = null;
        if (comparePath is not null)
        {
            try
            {
                replay = ReplayFile.Load(comparePath);
                await output.WriteLineAsync(
                    AnsiPalette.Ok("loaded retail capture: ") +
                    AnsiPalette.Value($"{replay.Frames.Count} frames") +
                    AnsiPalette.Muted($", meta={FormatMeta(replay.Metadata)}"))
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                await output.WriteLineAsync(
                    AnsiPalette.Err($"failed to load --compare file: {ex.Message}"))
                    .ConfigureAwait(false);
                return 1;
            }
        }

        await output.WriteLineAsync(
            AnsiPalette.Muted("entering sector ") + AnsiPalette.Value($"{sectorId}") +
            AnsiPalette.Muted(" on slot ") + AnsiPalette.Value($"{slot}") +
            AnsiPalette.Muted(" as ") + AnsiPalette.Accent(firstName) +
            AnsiPalette.Muted($" (dump mode, drain={drain.TotalSeconds:0}s)..."))
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

        await output.WriteLineAsync(
            AnsiPalette.Ok("in-sector: ") +
            AnsiPalette.Muted("gameId=") + AnsiPalette.Value($"0x{result.GameId:X8}") + " " +
            AnsiPalette.Muted("startId=") + AnsiPalette.Value($"0x{result.StartId:X8}") + " " +
            AnsiPalette.Muted("handshake-frames=") + AnsiPalette.Value($"{result.HandshakeFrames.Count}"))
            .ConfigureAwait(false);

        var extra = await DrainBriefly(result.Sector, drain, ct).ConfigureAwait(false);

        var allFrames = new List<Packet>(result.HandshakeFrames.Count + extra.Count);
        allFrames.AddRange(result.HandshakeFrames);
        allFrames.AddRange(extra);

        await DumpFrames(output, allFrames, replay).ConfigureAwait(false);
        await PrintCoverageSummary(output, allFrames, replay).ConfigureAwait(false);
        return 0;
    }

    private static async Task DumpFrames(
        TextWriter output, IReadOnlyList<Packet> ours, ReplayFile? replay)
    {
        await output.WriteLineAsync(
            AnsiPalette.Head($"---- frame dump ({ours.Count} total) ----"))
            .ConfigureAwait(false);
        for (int i = 0; i < ours.Count; i++)
        {
            var p = ours[i];
            ushort op = p.Header.Opcode;
            int len = p.Payload.Length;
            string opcodeName = NameOr(op);

            if (replay is null)
            {
                string header = AnsiPalette.Colorize(
                    AnsiPalette.Cyan + AnsiPalette.Bold,
                    $"#{i,4}  0x{op:X4} {opcodeName,-26}  len={len}");
                await output.WriteLineAsync(header).ConfigureAwait(false);
                var rec = PacketRecord.Resolve(op, p.Payload.Span);
                await output.WriteAsync(rec.DumpToString()).ConfigureAwait(false);
            }
            else
            {
                var retail = TryGetReplayFrame(replay, i);
                string status;
                string headerColor;
                if (retail is null)
                {
                    status = "[extra: no retail frame at this index]";
                    headerColor = AnsiPalette.Magenta;
                }
                else if (retail.Opcode != op)
                {
                    status = $"[OPCODE MISMATCH: retail=0x{retail.Opcode:X4} {NameOr(retail.Opcode)}]";
                    headerColor = AnsiPalette.BrightRed + AnsiPalette.Bold;
                }
                else if (retail.Payload.Length != len)
                {
                    status = $"[LEN MISMATCH: retail={retail.Payload.Length} bytes]";
                    headerColor = AnsiPalette.Yellow;
                }
                else if (!p.Payload.Span.SequenceEqual(retail.Payload))
                {
                    status = "[byte-diff]";
                    headerColor = AnsiPalette.Magenta;
                }
                else
                {
                    status = "[match]";
                    headerColor = AnsiPalette.Green;
                }
                string header = AnsiPalette.Colorize(
                    headerColor,
                    $"#{i,4}  0x{op:X4} {opcodeName,-26}  len={len}  {status}");
                await output.WriteLineAsync(header).ConfigureAwait(false);
                var ourRec = PacketRecord.Resolve(op, p.Payload.Span);
                await output.WriteAsync(ourRec.DumpToString()).ConfigureAwait(false);
                if (retail is not null && retail.Opcode == op
                    && !p.Payload.Span.SequenceEqual(retail.Payload))
                {
                    await output.WriteLineAsync(
                        AnsiPalette.Colorize(AnsiPalette.Magenta, "  --- retail capture ---"))
                        .ConfigureAwait(false);
                    var retailRec = PacketRecord.Resolve(
                        retail.Opcode, retail.Payload);
                    await output.WriteAsync(retailRec.DumpToString()).ConfigureAwait(false);
                }
            }
        }
    }

    private static ReplayFrame? TryGetReplayFrame(ReplayFile replay, int index) =>
        index < 0 || index >= replay.Frames.Count ? null : replay.Frames[index];

    private static async Task PrintCoverageSummary(
        TextWriter output, IReadOnlyList<Packet> ours, ReplayFile? replay)
    {
        await output.WriteLineAsync(AnsiPalette.Head("---- opcode coverage ----")).ConfigureAwait(false);
        var oursCounts = CountByOpcode(ours.Select(p => p.Header.Opcode));
        await output.WriteLineAsync(
            AnsiPalette.Muted("ours:   ") +
            AnsiPalette.Value($"{oursCounts.Count} distinct opcodes, {ours.Count} frames"))
            .ConfigureAwait(false);

        if (replay is null) return;

        var retailCounts = CountByOpcode(replay.Frames.Select(f => f.Opcode));
        await output.WriteLineAsync(
            AnsiPalette.Muted("retail: ") +
            AnsiPalette.Value($"{retailCounts.Count} distinct opcodes, {replay.Frames.Count} frames"))
            .ConfigureAwait(false);

        var missing = retailCounts.Keys.Except(oursCounts.Keys).OrderBy(o => o).ToList();
        var extra = oursCounts.Keys.Except(retailCounts.Keys).OrderBy(o => o).ToList();

        if (missing.Count > 0)
        {
            await output.WriteLineAsync(AnsiPalette.Err(
                "[!] opcodes present in retail but NOT emitted by our server:"))
                .ConfigureAwait(false);
            foreach (var op in missing)
                await output.WriteLineAsync(
                    AnsiPalette.Muted($"    0x{op:X4} {NameOr(op)}  (retail x{retailCounts[op]})"))
                    .ConfigureAwait(false);
        }
        if (extra.Count > 0)
        {
            await output.WriteLineAsync(AnsiPalette.Warn(
                "[!] opcodes emitted by our server but NOT in the retail capture:"))
                .ConfigureAwait(false);
            foreach (var op in extra)
                await output.WriteLineAsync(
                    AnsiPalette.Muted($"    0x{op:X4} {NameOr(op)}  (ours x{oursCounts[op]})"))
                    .ConfigureAwait(false);
        }
        if (missing.Count == 0 && extra.Count == 0)
            await output.WriteLineAsync(AnsiPalette.Ok("opcode sets match.")).ConfigureAwait(false);
    }

    private static Dictionary<ushort, int> CountByOpcode(IEnumerable<ushort> opcodes)
    {
        var d = new Dictionary<ushort, int>();
        foreach (var o in opcodes) d[o] = d.GetValueOrDefault(o) + 1;
        return d;
    }

    private static string NameOr(ushort opcode) =>
        N7.CliClient.Logging.OpcodeNameLookup.TryGetName(
            new N7.CliClient.Opcodes.OpcodeId(opcode)) ?? "?";

    private static string FormatMeta(IReadOnlyDictionary<string, string> meta)
    {
        var parts = new List<string>();
        foreach (var key in new[] { "capture", "frame_count", "avatar_name", "ship_name", "sector_id" })
            if (meta.TryGetValue(key, out var v)) parts.Add($"{key}={v}");
        return parts.Count == 0 ? "(none)" : string.Join(" ", parts);
    }

    private static async Task<List<Packet>> DrainBriefly(
        EncryptedTcpConnection conn, TimeSpan window, CancellationToken outer)
    {
        var frames = new List<Packet>();
        if (window <= TimeSpan.Zero) return frames;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(outer);
        cts.CancelAfter(window);
        try
        {
            while (!cts.IsCancellationRequested)
            {
                var p = await conn.ReceiveAsync(cts.Token).ConfigureAwait(false);
                if (p is null) break;
                frames.Add(p);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception) { }
        return frames;
    }
}
