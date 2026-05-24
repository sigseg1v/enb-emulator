// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using N7.CliClient.Net;
using N7.CliClient.Opcodes;

namespace N7.CliClient.Logging;

/// <summary>
/// Pretty-prints packets and chat to a <see cref="TextWriter"/>
/// (defaults to <see cref="Console.Out"/>). Parallel to the
/// file-based <see cref="PacketLog"/> and <see cref="ChatLog"/> —
/// callers fan out to both. Output is single-line, human-skimmable,
/// not parseable.
/// </summary>
/// <remarks>
/// Format: <c>[HH:MM:SS.fff] →|← 0x0035 MasterJoin (64 bytes)</c>.
/// Long payloads are hex-summarised to the first 32 bytes + ellipsis.
/// </remarks>
public sealed class ConsoleSink
{
    private const int MaxInlinePayloadBytes = 32;
    private readonly TextWriter _writer;
    private readonly object _gate = new();

    public ConsoleSink() : this(Console.Out) { }

    public ConsoleSink(TextWriter writer)
    {
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
    }

    public void Packet(
        PacketDirection direction,
        Packet packet,
        DateTimeOffset? timestamp = null)
    {
        ArgumentNullException.ThrowIfNull(packet);
        var ts = (timestamp ?? DateTimeOffset.UtcNow).UtcDateTime;
        char arrow = direction == PacketDirection.Outbound ? '→' : '←';
        var opcode = new OpcodeId(packet.Header.Opcode);
        string name = OpcodeNameLookup.TryGetName(opcode) ?? "?";

        var sb = new System.Text.StringBuilder(128);
        sb.Append('[').Append(ts.ToString("HH:mm:ss.fff")).Append("] ");
        sb.Append(arrow).Append(' ');
        sb.Append(opcode.ToString()).Append(' ').Append(name);
        sb.Append(" (").Append(packet.Payload.Length).Append(" bytes)");

        if (!packet.Payload.IsEmpty)
        {
            sb.Append("  ");
            var span = packet.Payload.Span;
            int n = Math.Min(span.Length, MaxInlinePayloadBytes);
            sb.Append(Convert.ToHexString(span[..n]).ToLowerInvariant());
            if (span.Length > n) sb.Append('…');
        }

        lock (_gate)
        {
            _writer.WriteLine(sb.ToString());
            _writer.Flush();
        }
    }

    public void Chat(string sender, string message, string? channel = null,
        DateTimeOffset? timestamp = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(sender);
        ArgumentNullException.ThrowIfNull(message);
        var ts = (timestamp ?? DateTimeOffset.UtcNow).UtcDateTime;
        string chan = string.IsNullOrEmpty(channel) ? "chat" : channel;
        string line = $"[{ts:HH:mm:ss}] [{chan}] {sender}: {message}";
        lock (_gate)
        {
            _writer.WriteLine(line);
            _writer.Flush();
        }
    }

    public void Info(string message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var ts = DateTimeOffset.UtcNow.UtcDateTime;
        string line = $"[{ts:HH:mm:ss}] {message}";
        lock (_gate)
        {
            _writer.WriteLine(line);
            _writer.Flush();
        }
    }
}
