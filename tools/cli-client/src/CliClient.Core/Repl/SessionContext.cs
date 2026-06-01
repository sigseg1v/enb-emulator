// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using N7.CliClient.Logging;
using N7.CliClient.Net;
using N7.CliClient.Opcodes;
using N7.CliClient.Opcodes.Inbound;
using N7.CliClient.Opcodes.Records;
using N7.CliClient.Session;

namespace N7.CliClient.Repl;

/// <summary>
/// Mutable session state shared between REPL commands. One instance is
/// constructed when the REPL starts and the commands pass it around to
/// keep `connect` -> `login` -> `list` -> `create` -> `enter` coherent.
///
/// <para>
/// Holds at most one long-lived global <see cref="EncryptedTcpConnection"/>
/// (the post-login global channel) and at most one in-sector
/// <see cref="EncryptedTcpConnection"/> after `enter` succeeds.
/// </para>
/// </summary>
public sealed class SessionContext : IAsyncDisposable
{
    // Hosts default to localhost; ports default to the dev-stack values
    // from docker-compose.yml. `connect` overrides Host and AuthPort.
    public string Host { get; set; } = "127.0.0.1";

    public int AuthPort { get; set; } = 4443;
    public int GlobalPort { get; set; } = 3805;
    public int MasterPort { get; set; } = 3801;
    public int SectorPort { get; set; } = 3500;

    /// <summary>Accept self-signed TLS certs (true for the docker dev stack).</summary>
    public bool AcceptUntrustedTls { get; set; } = true;

    public OpcodeRegistry Registry { get; }

    public string? Username { get; set; }

    /// <summary>20-byte auth ticket from /AuthLogin. Null until <c>login</c>.</summary>
    public string? Ticket { get; set; }

    /// <summary>Last decoded GlobalAvatarList from the active global channel.</summary>
    public GlobalAvatarList? AvatarList { get; set; }

    private EncryptedTcpConnection? _global;
    private EncryptedTcpConnection? _sector;

    /// <summary>Long-lived global connection after <c>login</c>. Null otherwise.</summary>
    public EncryptedTcpConnection? Global
    {
        get => _global;
        set
        {
            DetachDumpHook(_global);
            _global = value;
            AttachDumpHook(_global);
        }
    }

    /// <summary>Long-lived sector connection after <c>enter</c>. Null otherwise.</summary>
    public EncryptedTcpConnection? Sector
    {
        get => _sector;
        set
        {
            DetachDumpHook(_sector);
            _sector = value;
            AttachDumpHook(_sector);
            // Restart any active background drain on the new sector
            // connection so dump-on keeps fed across enter.
            if (DumpEnabled) RestartDumpDrain();
        }
    }

    /// <summary>PLAYER_TAG-bit-set avatar id allocated by GlobalTicketRequest.</summary>
    public int? GameId { get; set; }

    /// <summary>Slot of the avatar currently active in-sector.</summary>
    public int? ActiveSlot { get; set; }

    /// <summary>Sector id the active avatar entered.</summary>
    public int? ActiveSectorId { get; set; }

    // ---------- dump-on / dump-off live tail state ----------

    /// <summary>
    /// When true, every packet that crosses <see cref="Global"/> or
    /// <see cref="Sector"/> is structurally printed to
    /// <see cref="DumpOutput"/>. Toggled by the <c>dump-on</c> /
    /// <c>dump-off</c> REPL commands.
    /// </summary>
    public bool DumpEnabled { get; set; }

    /// <summary>
    /// Where to print dumped packets. Set once when the REPL starts;
    /// defaults to <see cref="Console.Out"/> if not provided.
    /// </summary>
    public TextWriter DumpOutput { get; set; } = Console.Out;

    private CancellationTokenSource? _dumpDrainCts;
    private Task? _dumpDrainTask;

    public SessionContext(OpcodeRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        Registry = registry;
    }

    /// <summary>
    /// Start a background drain on the active sector connection so
    /// packets flow through <c>PacketReceived</c> while the REPL is
    /// idle. Idempotent.
    ///
    /// <para>
    /// Sector-only on purpose. Pre-enter, the Global connection is
    /// owned by command-driven receive loops (<c>login</c>,
    /// <c>list</c>, <c>enter</c>) which fire the same
    /// <c>PacketReceived</c> hook naturally — running a background
    /// drain in parallel would race the command-owned reads
    /// (<see cref="EncryptedTcpConnection"/> is documented
    /// single-reader) and steal bytes the command was waiting on.
    /// Post-enter, no command currently reads from Sector, so the
    /// drain monopolises it harmlessly.
    /// </para>
    /// </summary>
    public void StartDumpDrain()
    {
        if (_dumpDrainTask is { IsCompleted: false }) return;
        var conn = _sector;
        if (conn is null) return;

        var cts = new CancellationTokenSource();
        _dumpDrainCts = cts;
        _dumpDrainTask = Task.Run(async () =>
        {
            try
            {
                while (!cts.IsCancellationRequested)
                {
                    var pkt = await conn.ReceiveAsync(cts.Token).ConfigureAwait(false);
                    if (pkt is null) break; // remote closed
                }
            }
            catch (OperationCanceledException) { /* expected */ }
            catch (Exception ex)
            {
                try { DumpOutput.WriteLine($"[dump-drain] {ex.GetType().Name}: {ex.Message}"); } catch { }
            }
        });
    }

    /// <summary>Cancel the background drain task. Idempotent.</summary>
    public async Task StopDumpDrainAsync()
    {
        var cts = _dumpDrainCts;
        var task = _dumpDrainTask;
        _dumpDrainCts = null;
        _dumpDrainTask = null;
        if (cts is null) return;
        try { cts.Cancel(); } catch { }
        if (task is not null)
        {
            try { await task.ConfigureAwait(false); } catch { }
        }
        cts.Dispose();
    }

    private void RestartDumpDrain()
    {
        // Fire-and-forget restart -- the property setter can't await.
        _ = Task.Run(async () =>
        {
            await StopDumpDrainAsync().ConfigureAwait(false);
            StartDumpDrain();
        });
    }

    private void AttachDumpHook(EncryptedTcpConnection? conn)
    {
        if (conn is null) return;
        conn.PacketSent     += OnPacketSent;
        conn.PacketReceived += OnPacketReceived;
    }

    private void DetachDumpHook(EncryptedTcpConnection? conn)
    {
        if (conn is null) return;
        conn.PacketSent     -= OnPacketSent;
        conn.PacketReceived -= OnPacketReceived;
    }

    private void OnPacketSent(Packet p)     => PrintIfEnabled(p, PacketDirection.Outbound);
    private void OnPacketReceived(Packet p) => PrintIfEnabled(p, PacketDirection.Inbound);

    /// <summary>
    /// Replay an already-received frame through the dump printer as if it
    /// had crossed the wire just now. Used by <c>enter</c> to surface the
    /// sector-handshake frames that the <see cref="SectorEnterDriver"/>
    /// consumes BEFORE the connection becomes <c>_ctx.Sector</c> and the
    /// dump hook attaches; without this they would silently disappear
    /// even though dump-on was on.
    /// </summary>
    public void ReplayInboundFrame(Packet p) => PrintIfEnabled(p, PacketDirection.Inbound);

    private void PrintIfEnabled(Packet p, PacketDirection dir)
    {
        if (!DumpEnabled) return;
        try
        {
            ushort op = p.Header.Opcode;
            string name = OpcodeNameLookup.TryGetName(new OpcodeId(op)) ?? "?";
            string arrow = dir == PacketDirection.Inbound ? "<--" : "-->";
            string arrowColor = dir == PacketDirection.Inbound
                ? AnsiPalette.BrightCyan + AnsiPalette.Bold
                : AnsiPalette.BrightYellow + AnsiPalette.Bold;
            string header = AnsiPalette.Colorize(
                arrowColor,
                $"{arrow} 0x{op:X4} {name,-26} len={p.Payload.Length}");
            DumpOutput.WriteLine(header);
            var rec = PacketRecord.Resolve(op, p.Payload.Span);
            // Known opcodes -> structured fields + full hex.
            // Unknown opcodes -> just full hex (heuristic ASCII fields
            // on a GenericRecord are noise; the operator wants raw bytes
            // to spot nulls / garbage themselves).
            if (rec is GenericRecord g)
                DumpOutput.Write(g.HexOnlyDump());
            else
                DumpOutput.Write(rec.DumpToString());
            DumpOutput.Flush();
        }
        catch (Exception ex)
        {
            try { DumpOutput.WriteLine($"[dump] format error: {ex.GetType().Name}: {ex.Message}"); } catch { }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopDumpDrainAsync().ConfigureAwait(false);
        if (_sector is not null) { DetachDumpHook(_sector); await _sector.DisposeAsync(); _sector = null; }
        if (_global is not null) { DetachDumpHook(_global); await _global.DisposeAsync(); _global = null; }
    }
}
