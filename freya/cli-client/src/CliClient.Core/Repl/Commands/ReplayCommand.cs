// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya

using N7.CliClient.Logging;
using N7.CliClient.Opcodes.Records;
using N7.CliClient.Replay;

namespace N7.CliClient.Repl.Commands;

/// <summary>
/// <c>replay &lt;path/to/capture.bin&gt;</c> -- load an ENBREPLAY capture
/// (produced by <c>tools/capture-extract/</c>) and dump every frame
/// through the per-opcode record classes. Offline counterpart to
/// <see cref="DumpCommand"/>: needs no live server, just exercises the
/// decode path against recorded retail bytes.
/// </summary>
public sealed class ReplayCommand : ICommandHandler
{
    public string Name    => "replay";
    public string Summary => "load an ENBREPLAY capture and dump every frame as a structured record";
    public string Usage   => "replay <path/to/capture.bin>";
    public string? Placeholder => "<path/to/capture.bin>";

    public async Task<int> ExecuteAsync(
        IReadOnlyList<string> args, TextWriter output, CancellationToken ct)
    {
        if (args.Count < 1)
        {
            await output.WriteLineAsync(
                AnsiPalette.Warn($"usage: {Usage}")).ConfigureAwait(false);
            return 1;
        }

        string path = args[0];
        ReplayFile replay;
        try
        {
            replay = ReplayFile.Load(path);
        }
        catch (Exception ex)
        {
            await output.WriteLineAsync(
                AnsiPalette.Err($"failed to load replay: {ex.Message}")).ConfigureAwait(false);
            return 1;
        }

        await output.WriteLineAsync(
            AnsiPalette.Ok($"loaded {replay.Frames.Count} frames") +
            AnsiPalette.Muted($" from {path} (meta={FormatMeta(replay.Metadata)})"))
            .ConfigureAwait(false);

        for (int i = 0; i < replay.Frames.Count; i++)
        {
            if (ct.IsCancellationRequested) return 1;
            var frame = replay.Frames[i];
            string opcodeName = NameOr(frame.Opcode);
            string header = AnsiPalette.Colorize(
                AnsiPalette.Cyan + AnsiPalette.Bold,
                $"#{i,4}  0x{frame.Opcode:X4} {opcodeName,-26}  len={frame.Payload.Length}");
            await output.WriteLineAsync(header).ConfigureAwait(false);
            var rec = PacketRecord.Resolve(frame.Opcode, frame.Payload);
            await output.WriteAsync(rec.DumpToString()).ConfigureAwait(false);
        }

        return 0;
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
}
