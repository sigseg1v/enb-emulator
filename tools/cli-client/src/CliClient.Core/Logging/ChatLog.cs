// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// New code; project default license (LICENSES/enb-emulator).

using System.Text;

namespace N7.CliClient.Logging;

/// <summary>
/// Separate, human-readable log for chat lines. Lives alongside the
/// NDJSON <see cref="PacketLog"/> but is plain text — the use case is
/// "I want to skim what people said during a session" without grepping
/// through every game-state opcode.
/// </summary>
/// <remarks>
/// Line format (UTF-8, LF-terminated): <c>YYYY-MM-DDTHH:MM:SS.fffZ [channel] sender: message</c>.
/// When channel is null/empty the brackets collapse to <c>[chat]</c>.
/// </remarks>
public sealed class ChatLog : IDisposable, IAsyncDisposable
{
    private readonly TextWriter _writer;
    private readonly bool _ownsWriter;
    private readonly object _gate = new();
    private bool _disposed;

    public static ChatLog OpenFile(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var stream = new FileStream(
            path, FileMode.Append, FileAccess.Write, FileShare.Read);
        var writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = false };
        return new ChatLog(writer, ownsWriter: true);
    }

    public ChatLog(TextWriter writer, bool ownsWriter = false)
    {
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        _ownsWriter = ownsWriter;
    }

    /// <summary>Append one chat line. Sender / message are required; channel optional.</summary>
    public void Log(
        string sender,
        string message,
        string? channel = null,
        DateTimeOffset? timestamp = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(sender);
        ArgumentNullException.ThrowIfNull(message);
        ObjectDisposedException.ThrowIf(_disposed, this);

        var ts = timestamp ?? DateTimeOffset.UtcNow;
        string chan = string.IsNullOrEmpty(channel) ? "chat" : channel;
        string line = $"{ts.UtcDateTime:yyyy-MM-ddTHH:mm:ss.fffZ} [{chan}] {sender}: {message}";

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _writer.WriteLine(line);
            _writer.Flush();
        }
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
