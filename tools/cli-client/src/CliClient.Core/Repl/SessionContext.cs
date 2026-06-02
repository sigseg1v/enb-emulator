// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using N7.CliClient.Logging;
using N7.CliClient.Net;
using N7.CliClient.Opcodes;
using N7.CliClient.Opcodes.Inbound;
using N7.CliClient.Opcodes.Outbound;
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

    /// <summary>
    /// MVAS (move-assist) UDP port for position updates. In the live stack the
    /// proxy speaks this on the client's behalf (scraping engine memory); a
    /// headless client feeds it directly. Not host-reachable on the default
    /// docker-compose unless 3806/udp is published. See <see cref="MvasClient"/>.
    /// </summary>
    public int MvasPort { get; set; } = MvasClient.DefaultPort;

    /// <summary>
    /// Host the MVAS/sector UDP client dials. Defaults to <see cref="Host"/>
    /// (the proxy, which is also where the server's published ports live on a
    /// single-host docker setup). Override when the proxy and the server are
    /// reachable at different addresses -- e.g. running the CLI INSIDE the
    /// docker network, where the proxy is the proxy container but the MVAS port
    /// 3806 is on the server container. Set via the N7_MVAS_HOST env var.
    /// </summary>
    public string? MvasHost { get; set; }

    /// <summary>The MVAS host actually used (<see cref="MvasHost"/> or <see cref="Host"/>).</summary>
    public string EffectiveMvasHost => string.IsNullOrEmpty(MvasHost) ? Host : MvasHost;

    /// <summary>
    /// Auth/login host override (defaults to <see cref="Host"/>). Needed when
    /// the login server and the proxy are at different addresses -- e.g. the
    /// CLI running INSIDE the docker network, where auth is the login container
    /// and the TCP planes are the proxy container. Set via N7_AUTH_HOST.
    /// </summary>
    public string? AuthHost { get; set; }

    /// <summary>The auth host actually used (<see cref="AuthHost"/> or <see cref="Host"/>).</summary>
    public string EffectiveAuthHost => string.IsNullOrEmpty(AuthHost) ? Host : AuthHost;

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
            // NB: the background sector drain is started explicitly by
            // `enter` AFTER its own foreground drain window closes -- not
            // here -- so the two don't both read the single-reader socket
            // at once. See EnterCommand.
        }
    }

    /// <summary>PLAYER_TAG-bit-set avatar id allocated by GlobalTicketRequest.</summary>
    public int? GameId { get; set; }

    /// <summary>Slot of the avatar currently active in-sector.</summary>
    public int? ActiveSlot { get; set; }

    /// <summary>Sector id the active avatar entered.</summary>
    public int? ActiveSectorId { get; set; }

    /// <summary>True once <c>connect</c> has probed a reachable endpoint.</summary>
    public bool Connected { get; set; }

    /// <summary>
    /// Running model of objects/navs the server has announced for the
    /// current sector. Fed by the inbound packet hook; read by <c>enter</c>'s
    /// arrival summary and the in-sector <c>list</c> command.
    /// </summary>
    public SectorWorld World { get; } = new();

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

    // ---------- chat echo state ----------

    /// <summary>
    /// When true (the default), every chat frame that crosses a connection
    /// is echoed as a one-line <c>[channel] sender: message</c> to
    /// <see cref="ChatOutput"/>, independent of <see cref="DumpEnabled"/>.
    /// This is what makes chat visible at the prompt without <c>dump-on</c>.
    /// </summary>
    public bool ChatEcho { get; set; } = true;

    /// <summary>Where chat echo lines go. Defaults to <see cref="Console.Out"/>.</summary>
    public TextWriter ChatOutput { get; set; } = Console.Out;

    private readonly object _chatGate = new();

    private CancellationTokenSource? _dumpDrainCts;
    private Task? _dumpDrainTask;
    private MvasClient? _mvas;

    /// <summary>
    /// Lazily-created MVAS position emitter for the active host. Used by the
    /// <c>move</c> command to feed position updates the way a real client's
    /// proxy would. Reused so the UDP sequence counter stays monotonic.
    /// </summary>
    public MvasClient Mvas => _mvas ??= new MvasClient(EffectiveMvasHost, MvasPort);

    /// <summary>
    /// Full-duplex sector UDP client, live once the player starts driving its
    /// own position (the <c>move ... send</c> path). When set, the live sector
    /// stream arrives here (the server reroutes it to our socket) rather than
    /// over the proxy TCP channel. Feeds the same world model + echo hooks.
    /// </summary>
    public SectorUdpClient? SectorUdp { get; set; }

    public SessionContext(OpcodeRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        Registry = registry;
    }

    /// <summary>
    /// Start a background drain on the active sector connection so packets
    /// flow through <c>PacketReceived</c> -- feeding both the chat echo and
    /// (when enabled) the dump printer -- while the REPL sits idle at the
    /// prompt. Idempotent.
    ///
    /// <para>
    /// Sector-only on purpose. Pre-enter, the Global connection is owned by
    /// command-driven receive loops (<c>login</c>, <c>list</c>,
    /// <c>enter</c>) which fire the same <c>PacketReceived</c> hook
    /// naturally — running a background drain in parallel would race the
    /// command-owned reads (<see cref="EncryptedTcpConnection"/> is
    /// documented single-reader) and steal bytes the command was waiting
    /// on. Post-enter, no command reads from Sector, so the drain
    /// monopolises it harmlessly. <c>enter</c> starts it only after its own
    /// foreground drain window closes, for the same single-reader reason.
    /// </para>
    /// </summary>
    public void StartSectorDrain()
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
                try { DumpOutput.WriteLine($"[sector-drain] {ex.GetType().Name}: {ex.Message}"); } catch { }
            }
        });
    }

    /// <summary>Cancel the background sector drain task. Idempotent.</summary>
    public async Task StopSectorDrainAsync()
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

    private void OnPacketSent(Packet p)
    {
        EchoChat(p, PacketDirection.Outbound);
        PrintIfEnabled(p, PacketDirection.Outbound);
    }

    private void OnPacketReceived(Packet p)
    {
        World.Ingest(p);
        EchoChat(p, PacketDirection.Inbound);
        PrintIfEnabled(p, PacketDirection.Inbound);
    }

    /// <summary>
    /// Replay an already-received frame through the dump printer as if it
    /// had crossed the wire just now. Used by <c>enter</c> to surface the
    /// sector-handshake frames that the <see cref="SectorEnterDriver"/>
    /// consumes BEFORE the connection becomes <c>_ctx.Sector</c> and the
    /// dump hook attaches; without this they would silently disappear
    /// even though dump-on was on.
    /// </summary>
    public void ReplayInboundFrame(Packet p)
    {
        World.Ingest(p);
        EchoChat(p, PacketDirection.Inbound);
        PrintIfEnabled(p, PacketDirection.Inbound);
    }

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

    /// <summary>
    /// Echo a chat frame as one readable line, regardless of
    /// <see cref="DumpEnabled"/>. Inbound 0x00A5 CLIENT_CHAT_EVENT and
    /// outbound 0x0033 CLIENT_CHAT are recognised; everything else is
    /// ignored. Never throws -- a malformed frame must not kill the drain.
    /// </summary>
    private void EchoChat(Packet p, PacketDirection dir)
    {
        if (!ChatEcho) return;
        try
        {
            ushort op = p.Header.Opcode;
            if (dir == PacketDirection.Inbound && op == 0x00A5)
            {
                var ev = ClientChatEventRecord.TryExtract(p.Payload.Span);
                if (ev is not { } e) return;
                // Status-only events (LOGGED_IN / ALL_STATUS / friend
                // presence) carry no message body -- not chat, skip them.
                if (string.IsNullOrEmpty(e.Message)) return;
                string channel = string.IsNullOrEmpty(e.Channel) ? "chat" : e.Channel;
                string sender = string.IsNullOrEmpty(e.Sender) ? "?" : e.Sender;
                WriteChatLine(channel, sender, e.Message, incoming: true);
            }
            else if (dir == PacketDirection.Outbound && op == 0x0033)
            {
                if (new ClientChatCodec().DecodeInbound(p.Payload.Span) is not ClientChatMessage m)
                    return;
                var (channel, text) = InterpretOutbound(m.Type, m.Message);
                WriteChatLine(channel, "you", text, incoming: false);
            }
        }
        catch
        {
            // Best-effort: chat echo is a convenience, never a failure point.
        }
    }

    /// <summary>
    /// Map an outbound <see cref="ClientChatMessage"/> to a (channel, text)
    /// pair for display. A leading '/' is a slash command -- surface the
    /// recognised channel words (gm/dev/beta) as their channel with the
    /// '/word ' prefix stripped; other slash commands show as "cmd".
    /// </summary>
    private static (string Channel, string Text) InterpretOutbound(ChatChannel type, string message)
    {
        if (message.StartsWith('/'))
        {
            int sp = message.IndexOf(' ');
            string word = (sp < 0 ? message[1..] : message[1..sp]).ToLowerInvariant();
            string rest = sp < 0 ? string.Empty : message[(sp + 1)..];
            return word switch
            {
                "gm" or "dev" or "beta" => (word, rest),
                _ => ("cmd", message),
            };
        }

        string channel = type switch
        {
            ChatChannel.Target    => "whisper",
            ChatChannel.Group     => "group",
            ChatChannel.Guild     => "guild",
            ChatChannel.Local     => "local",
            ChatChannel.Broadcast => "sector",
            _ => "chat",
        };
        return (channel, message);
    }

    private void WriteChatLine(string channel, string sender, string message, bool incoming)
    {
        string arrow = incoming ? "<--" : "-->";
        string line = $"{arrow} [{channel}] {sender}: {message}";
        string colored = AnsiPalette.Colorize(
            incoming ? AnsiPalette.Green : AnsiPalette.BrightCyan, line);
        lock (_chatGate)
        {
            ChatOutput.WriteLine(colored);
            ChatOutput.Flush();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopSectorDrainAsync().ConfigureAwait(false);
        if (SectorUdp is not null) { await SectorUdp.StopAsync().ConfigureAwait(false); SectorUdp.Dispose(); SectorUdp = null; }
        _mvas?.Dispose();
        if (_sector is not null) { DetachDumpHook(_sector); await _sector.DisposeAsync(); _sector = null; }
        if (_global is not null) { DetachDumpHook(_global); await _global.DisposeAsync(); _global = null; }
    }
}
