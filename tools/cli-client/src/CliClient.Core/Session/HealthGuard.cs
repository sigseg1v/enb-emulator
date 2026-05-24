// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// New code; project default license (LICENSES/enb-emulator).

using System.Collections.Concurrent;
using N7.CliClient.Logging;
using N7.CliClient.Net;
using N7.CliClient.Opcodes;

namespace N7.CliClient.Session;

/// <summary>
/// Enforces hard-rule #2 from <c>plans/19-phase-s-cli-client.md</c>:
/// "The CLI client must always respect the server. If the server
/// imposes limits, returns garbage, or shows signs of crashing /
/// overload, the CLI client stops the offending workflow immediately.
/// No retry storms. No bypass attempts."
/// </summary>
/// <remarks>
/// <para>
/// HealthGuard is the central kill-switch. It observes every packet a
/// workflow sends or receives, watches for disconnect / error opcodes
/// / response timeouts / packet-rate spikes, and on trip:
/// </para>
/// <list type="number">
///   <item>Cancels <see cref="Token"/> (every workflow operation
///   listens on it, so they stop next await).</item>
///   <item>Records <see cref="Reason"/> for diagnostic logging.</item>
///   <item>Logs the reason via the optional <see cref="ConsoleSink"/>.</item>
/// </list>
/// <para>
/// Tripping is a one-shot terminal state — the guard never re-arms.
/// To start a new session, build a new guard.
/// </para>
/// <para>
/// What HealthGuard does NOT do: retry, reconnect, or hide the
/// failure. It just stops the workflow and surfaces the cause.
/// </para>
/// </remarks>
public sealed class HealthGuard : IDisposable
{
    private readonly HealthGuardOptions _opts;
    private readonly ConsoleSink? _sink;
    private readonly CancellationTokenSource _cts = new();
    private readonly HashSet<ushort> _errorOpcodes;
    private readonly object _gate = new();

    // Sliding-window timestamps: one ring buffer per direction.
    // Using a simple Queue<long> of ticks for portability and clarity —
    // we never measure more than 1s back so it stays tiny.
    private readonly Queue<long> _inboundTicks = new();
    private readonly Queue<long> _outboundTicks = new();
    private static readonly long OneSecondTicks = TimeSpan.TicksPerSecond;

    // Pending response watchdogs.
    private readonly ConcurrentDictionary<long, PendingExpectation> _pending = new();
    private long _nextPendingId;

    private string? _reason;
    private bool _disposed;

    public HealthGuard(HealthGuardOptions? options = null, ConsoleSink? sink = null)
    {
        _opts = options ?? new HealthGuardOptions();
        _sink = sink;
        _errorOpcodes = _opts.ErrorOpcodes.Select(o => o.Value).ToHashSet();
    }

    /// <summary>Cancelled when the guard trips. Workflows pass this into every async call.</summary>
    public CancellationToken Token => _cts.Token;

    /// <summary>True if the guard has tripped (terminal).</summary>
    public bool Tripped => _cts.IsCancellationRequested;

    /// <summary>The reason for the first trip, or null if the guard hasn't tripped.</summary>
    public string? Reason
    {
        get { lock (_gate) return _reason; }
    }

    /// <summary>Extend the configured error-opcode set at runtime.</summary>
    public void RegisterErrorOpcode(OpcodeId opcode)
    {
        lock (_gate) _errorOpcodes.Add(opcode.Value);
    }

    /// <summary>
    /// Call when a packet is received from the server. Updates the
    /// inbound rate window and trips on error opcodes / rate spikes.
    /// </summary>
    public void OnPacketReceived(Packet packet)
    {
        ArgumentNullException.ThrowIfNull(packet);
        if (Tripped) return;

        ushort opcode = packet.Header.Opcode;
        bool isError;
        lock (_gate) isError = _errorOpcodes.Contains(opcode);
        if (isError)
        {
            Trip($"server sent error opcode 0x{opcode:X4}");
            return;
        }

        if (RecordAndCheckRate(_inboundTicks, "inbound")) return;

        // Any pending expectation that matches the opcode is satisfied.
        foreach (var (id, pending) in _pending)
        {
            if (pending.OpcodeFilter is null
                || pending.OpcodeFilter.Value.Value == opcode)
            {
                if (_pending.TryRemove(id, out var p))
                    p.Cancel();
                break;
            }
        }
    }

    /// <summary>Call when a packet is sent to the server. Updates the outbound rate window.</summary>
    public void OnPacketSent(Packet packet)
    {
        ArgumentNullException.ThrowIfNull(packet);
        if (Tripped) return;
        RecordAndCheckRate(_outboundTicks, "outbound");
    }

    /// <summary>
    /// Call when the connection drops. Always trips — disconnects mean
    /// the workflow's assumptions about server state no longer hold.
    /// </summary>
    public void OnDisconnect(string reason)
    {
        Trip($"server disconnected ({reason})");
    }

    /// <summary>
    /// Register an expectation that the server will reply (optionally
    /// with a specific opcode) within the timeout. Returns a handle
    /// that the caller disposes when the expectation is fulfilled.
    /// If neither the disposer fires nor a matching inbound packet
    /// arrives in time, the guard trips.
    /// </summary>
    public IDisposable BeginExpectResponse(
        string label,
        TimeSpan? timeout = null,
        OpcodeId? opcodeFilter = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(label);
        ObjectDisposedException.ThrowIf(_disposed, this);

        var pending = new PendingExpectation(
            this, label, timeout ?? _opts.ResponseTimeout, opcodeFilter);
        long id = Interlocked.Increment(ref _nextPendingId);
        _pending[id] = pending;
        pending.Id = id;
        pending.Arm();
        return pending;
    }

    /// <summary>
    /// Force-trip the guard with a caller-supplied reason. Useful when
    /// a workflow sees something the guard can't (malformed payload,
    /// unexpected state transition).
    /// </summary>
    public void Trip(string reason)
    {
        ArgumentException.ThrowIfNullOrEmpty(reason);
        lock (_gate)
        {
            if (_reason is not null) return; // first-trip wins
            _reason = reason;
        }
        _sink?.Info($"[health] TRIPPED: {reason}");
        try { _cts.Cancel(); } catch (ObjectDisposedException) { }
    }

    private bool RecordAndCheckRate(Queue<long> bucket, string direction)
    {
        long now = DateTimeOffset.UtcNow.UtcTicks;
        long cutoff = now - OneSecondTicks;
        int count;
        lock (bucket)
        {
            bucket.Enqueue(now);
            while (bucket.TryPeek(out long oldest) && oldest < cutoff)
                bucket.Dequeue();
            count = bucket.Count;
        }
        if (count > _opts.MaxPacketsPerSecond)
        {
            Trip($"{direction} packet rate {count}/s exceeded threshold {_opts.MaxPacketsPerSecond}/s");
            return true;
        }
        return false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var (id, p) in _pending)
        {
            if (_pending.TryRemove(id, out var pending))
                pending.Cancel();
        }
        _cts.Dispose();
    }

    private sealed class PendingExpectation : IDisposable
    {
        private readonly HealthGuard _owner;
        private readonly string _label;
        private readonly TimeSpan _timeout;
        public OpcodeId? OpcodeFilter { get; }
        public long Id { get; set; }

        private CancellationTokenSource? _timer;
        private int _settled; // 0 = pending, 1 = resolved/timed-out

        public PendingExpectation(
            HealthGuard owner, string label, TimeSpan timeout, OpcodeId? filter)
        {
            _owner = owner;
            _label = label;
            _timeout = timeout;
            OpcodeFilter = filter;
        }

        public void Arm()
        {
            _timer = new CancellationTokenSource(_timeout);
            _timer.Token.Register(OnTimeout);
        }

        private void OnTimeout()
        {
            if (Interlocked.Exchange(ref _settled, 1) != 0) return;
            _owner._pending.TryRemove(Id, out _);
            _owner.Trip($"response timeout: '{_label}' (>{_timeout.TotalSeconds:0.##}s)");
        }

        public void Cancel()
        {
            if (Interlocked.Exchange(ref _settled, 1) != 0) return;
            try { _timer?.Dispose(); } catch (ObjectDisposedException) { }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _settled, 1) != 0) return;
            _owner._pending.TryRemove(Id, out _);
            try { _timer?.Dispose(); } catch (ObjectDisposedException) { }
        }
    }
}
