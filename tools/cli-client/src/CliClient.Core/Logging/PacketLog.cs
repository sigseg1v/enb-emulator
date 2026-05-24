// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Text;
using System.Text.Json;
using N7.CliClient.Net;
using N7.CliClient.Opcodes;

namespace N7.CliClient.Logging;

/// <summary>
/// Append-only NDJSON sink: one packet = one line, flushed on every
/// write. Files this writes are <c>jq</c>-friendly out of the box
/// (each line is a self-contained JSON object) so post-hoc analysis of
/// a session ("show me every chat opcode in the last run") is one
/// shell pipeline.
/// </summary>
/// <remarks>
/// <para>
/// Line schema:
/// </para>
/// <code>
/// {
///   "ts":          "2026-05-24T18:32:11.4581234Z",
///   "direction":   "inbound" | "outbound",
///   "opcode_hex":  "0x0035",
///   "opcode_name": "MasterJoin"   // omitted when unknown
///   "length":      64,             // payload length, NOT header+payload
///   "payload_hex": "0000…"         // lowercase, no separators
///   "decoded":     { … }           // omitted when no decoder ran
/// }
/// </code>
/// <para>
/// Thread-safe — every <see cref="Log"/> call locks a single mutex and
/// flushes the underlying writer. Frequency is well under the noise
/// floor for a stream of game packets so coarse locking is fine.
/// </para>
/// </remarks>
public sealed class PacketLog : IDisposable, IAsyncDisposable
{
    private readonly TextWriter _writer;
    private readonly bool _ownsWriter;
    private readonly object _gate = new();
    private bool _disposed;

    /// <summary>
    /// Build a log that appends to the given file. Directory is created
    /// if missing. The file is opened with FileShare.Read so tail-style
    /// viewers can follow it live.
    /// </summary>
    public static PacketLog OpenFile(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var stream = new FileStream(
            path, FileMode.Append, FileAccess.Write, FileShare.Read);
        var writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = false };
        return new PacketLog(writer, ownsWriter: true);
    }

    /// <summary>
    /// Build a log that writes into an arbitrary <see cref="TextWriter"/>
    /// — used by tests (StringWriter) and by callers who want to fan
    /// out to console + file via a TeeTextWriter.
    /// </summary>
    public PacketLog(TextWriter writer, bool ownsWriter = false)
    {
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        _ownsWriter = ownsWriter;
    }

    /// <summary>
    /// Append one packet to the log. <paramref name="decoded"/> is
    /// optional — when non-null it is JSON-serialised into the
    /// <c>decoded</c> field; pass null when nothing has decoded the
    /// payload yet.
    /// </summary>
    public void Log(
        PacketDirection direction,
        Packet packet,
        object? decoded = null,
        DateTimeOffset? timestamp = null)
    {
        ArgumentNullException.ThrowIfNull(packet);
        ObjectDisposedException.ThrowIf(_disposed, this);

        var ts = timestamp ?? DateTimeOffset.UtcNow;
        var opcode = new OpcodeId(packet.Header.Opcode);
        string? name = OpcodeNameLookup.TryGetName(opcode);

        // Build the line. JsonSerializer for the decoded object keeps
        // formatting consistent (records → properties, enums → strings
        // optionally). For the framing we want a STRICT field order so
        // diffs across runs stay readable — hand-roll the outer object.
        var sb = new StringBuilder(256 + packet.Payload.Length * 2);
        sb.Append('{');
        AppendKv(sb, "ts", ts.ToString("O"), first: true);
        AppendKv(sb, "direction", direction switch
        {
            PacketDirection.Inbound  => "inbound",
            PacketDirection.Outbound => "outbound",
            _ => "unknown",
        });
        AppendKv(sb, "opcode_hex", opcode.ToString());
        if (name is not null) AppendKv(sb, "opcode_name", name);
        AppendKvRaw(sb, "length", packet.Payload.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
        AppendKv(sb, "payload_hex", ToHex(packet.Payload.Span));
        if (decoded is not null)
        {
            sb.Append(",\"decoded\":");
            sb.Append(JsonSerializer.Serialize(decoded, decoded.GetType(), JsonOpts));
        }
        sb.Append('}');

        string line = sb.ToString();
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _writer.WriteLine(line);
            _writer.Flush();
        }
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never,
    };

    private static void AppendKv(StringBuilder sb, string key, string value, bool first = false)
    {
        if (!first) sb.Append(',');
        sb.Append('"').Append(key).Append("\":");
        sb.Append(JsonSerializer.Serialize(value));
    }

    private static void AppendKvRaw(StringBuilder sb, string key, string rawJsonValue, bool first = false)
    {
        if (!first) sb.Append(',');
        sb.Append('"').Append(key).Append("\":").Append(rawJsonValue);
    }

    private static string ToHex(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty) return string.Empty;
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            if (_ownsWriter) _writer.Dispose();
        }
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}
