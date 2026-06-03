// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Buffers.Binary;
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
    /// A short, uncoloured label for the prompt that tracks how far along the
    /// flow the session is: <c>offline</c> -> <c>connected</c> -> the avatar
    /// name once in-world -> <c>name@sector</c> once in a sector. Program.cs
    /// wraps it in colour; this stays plain so it is safe to log or test.
    /// </summary>
    public string PromptLabel
    {
        get
        {
            if (Sector is not null)
            {
                string who = string.IsNullOrEmpty(Username) ? "in-sector" : Username;
                return ActiveSectorId is { } sid ? $"{who}@{sid}" : who;
            }
            if (Global is not null) return string.IsNullOrEmpty(Username) ? "online" : Username;
            if (Connected) return "connected";
            return "offline";
        }
    }

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

    /// <summary>
    /// Optional coordinator with the interactive <see cref="LineEditor"/>. When
    /// set and a prompt is on screen, chat echo is printed *above* the prompt
    /// (which is then redrawn) instead of clobbering the line the user is typing
    /// on. Null on the non-interactive path -- chat then writes plainly.
    /// </summary>
    public LivePrompt? LivePrompt { get; set; }

    private readonly object _chatGate = new();

    private CancellationTokenSource? _dumpDrainCts;
    private Task? _dumpDrainTask;
    private MvasClient? _mvas;

    // The server drops any player whose LastAccessTime is older than 120s
    // (PlayerManager.cpp idle check -> DropPlayerFromGalaxy). A live retail
    // client never trips this because its Net7Proxy streams MVAS position to
    // the server continuously (UDPProxyMVAS.cpp). A headless REPL session has
    // no such stream, so it must keep itself alive. 0x0044 REQUEST_TIME is the
    // lightest faithful choice: retail clients sent it for clock sync, the
    // server just echoes the time back (HandleRequestTime), and -- like every
    // inbound opcode -- its dispatch resets LastAccessTime
    // (PlayerConnection.cpp:645). 30s gives a 4x margin under the 120s window.
    private static readonly TimeSpan KeepaliveInterval = TimeSpan.FromSeconds(30);
    private CancellationTokenSource? _keepaliveCts;
    private Task? _keepaliveTask;

    // The retail client pings the MVAS port with 0x3005 COMMS_ALIVE on a
    // periodic timer (~30s) regardless of whether the avatar is moving, which
    // refreshes the same LastAccessTime the 120s idle reaper checks
    // (server/src/UDP_MVAS.cpp HandleKeepCommsAlive -> SetLastAccessTime). Our
    // MvasClient otherwise only emits 0x1004 while the avatar is MOVING, so an
    // idle in-space CLI avatar would send nothing on the MVAS leg. This mirrors
    // the real client's keepalive on the same UDP MVAS endpoint the 0x1004
    // position feed goes to.
    public const int CommsAliveIntervalSeconds = 30;
    private static readonly TimeSpan CommsAliveInterval =
        TimeSpan.FromSeconds(CommsAliveIntervalSeconds);
    private CancellationTokenSource? _commsAliveCts;
    private Task? _commsAliveTask;

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

    /// <summary>
    /// Name of the most recent player who sent us a group invite (0x001E GROUP,
    /// Flag 0x01), or null if none is pending. Set when the invite frame
    /// arrives; the <c>group-invite-accept</c> command reads it to decide
    /// whether there is anything to accept. Cleared once accepted/left.
    /// </summary>
    public string? PendingGroupInviter { get; set; }

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

    /// <summary>
    /// Start a background keepalive that sends a 0x0044 REQUEST_TIME over the
    /// active sector connection every <see cref="KeepaliveInterval"/> so the
    /// server's 120s idle reaper does not drop an in-sector REPL session that
    /// is just sitting at the prompt. Idempotent; no-op if no sector
    /// connection is active. The send is serialized with all other writers by
    /// <see cref="EncryptedTcpConnection"/>'s send gate. Started by <c>enter</c>
    /// once the sector connection is live; stopped on dispose.
    /// </summary>
    public void StartKeepalive()
    {
        if (_keepaliveTask is { IsCompleted: false }) return;
        var conn = _sector;
        if (conn is null) return;

        var cts = new CancellationTokenSource();
        _keepaliveCts = cts;
        _keepaliveTask = Task.Run(async () =>
        {
            try
            {
                while (!cts.IsCancellationRequested)
                {
                    await Task.Delay(KeepaliveInterval, cts.Token).ConfigureAwait(false);
                    var payload = new byte[4];
                    BinaryPrimitives.WriteInt32LittleEndian(payload, Environment.TickCount);
                    await conn.SendAsync(
                        Packet.ForOpcode(OpcodeId.Known.RequestTime.Value, payload),
                        cts.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) { /* expected on stop */ }
            catch (Exception ex)
            {
                // The connection died (the drain will also see EOF). Surface
                // it once and let the loop end -- nothing to keep alive.
                try { DumpOutput.WriteLine($"[keepalive] stopped: {ex.GetType().Name}: {ex.Message}"); } catch { }
            }
        });
    }

    /// <summary>Cancel the background keepalive task. Idempotent.</summary>
    public async Task StopKeepaliveAsync()
    {
        var cts = _keepaliveCts;
        var task = _keepaliveTask;
        _keepaliveCts = null;
        _keepaliveTask = null;
        if (cts is null) return;
        try { cts.Cancel(); } catch { }
        if (task is not null)
        {
            try { await task.ConfigureAwait(false); } catch { }
        }
        cts.Dispose();
    }

    /// <summary>
    /// Start a background loop that sends a 0x3005 COMMS_ALIVE on the MVAS UDP
    /// endpoint every <see cref="CommsAliveInterval"/>, mirroring the retail
    /// client's periodic keepalive. Runs whether or not the avatar is moving,
    /// for as long as the MVAS session is active. Idempotent; no-op until a
    /// <see cref="GameId"/> has been allocated (the server keys the refresh on
    /// it). Started by <c>enter</c> alongside the TCP keepalive; stopped on
    /// dispose. Uses the shared <see cref="Mvas"/> emitter so the UDP sequence
    /// counter stays monotonic with the position feed.
    /// </summary>
    public void StartCommsAlive()
    {
        if (_commsAliveTask is { IsCompleted: false }) return;
        if (GameId is null) return;

        var cts = new CancellationTokenSource();
        _commsAliveCts = cts;
        _commsAliveTask = Task.Run(async () =>
        {
            try
            {
                while (!cts.IsCancellationRequested)
                {
                    await Task.Delay(CommsAliveInterval, cts.Token).ConfigureAwait(false);
                    if (GameId is { } id) Mvas.SendCommsAlive(id);
                }
            }
            catch (OperationCanceledException) { /* expected on stop */ }
            catch (Exception ex)
            {
                try { DumpOutput.WriteLine($"[comms-alive] stopped: {ex.GetType().Name}: {ex.Message}"); } catch { }
            }
        });
    }

    /// <summary>Cancel the background COMMS_ALIVE loop. Idempotent.</summary>
    public async Task StopCommsAliveAsync()
    {
        var cts = _commsAliveCts;
        var task = _commsAliveTask;
        _commsAliveCts = null;
        _commsAliveTask = null;
        if (cts is null) return;
        try { cts.Cancel(); } catch { }
        if (task is not null)
        {
            try { await task.ConfigureAwait(false); } catch { }
        }
        cts.Dispose();
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
        AnnounceGroupInvite(p);
        PrintIfEnabled(p, PacketDirection.Inbound);
    }

    private const string GroupInviteSuffix = " is requesting you to join their group";

    /// <summary>
    /// Surface an inbound group invite (0x001E GROUP, Flag 0x01, message
    /// "&lt;name&gt; is requesting you to join their group") as a one-line prompt
    /// telling the operator how to accept, and remember the inviter so
    /// <c>group-invite-accept</c> has something to act on. Never throws -- a
    /// malformed frame must not kill the drain.
    /// </summary>
    private void AnnounceGroupInvite(Packet p)
    {
        try
        {
            if (p.Header.Opcode != 0x001E) return;
            var span = p.Payload.Span;
            if (span.Length < 4 || span[2] != 0x01) return;       // Flag 0x01 == group invite
            int len = BinaryPrimitives.ReadUInt16LittleEndian(span);
            if (len <= 1 || 3 + len > span.Length) return;
            string msg = System.Text.Encoding.Latin1.GetString(span.Slice(3, len - 1));
            string inviter = msg.EndsWith(GroupInviteSuffix, StringComparison.Ordinal)
                ? msg[..^GroupInviteSuffix.Length]
                : msg;
            PendingGroupInviter = inviter;

            string line = AnsiPalette.Colorize(
                AnsiPalette.BrightYellow + AnsiPalette.Bold,
                $"** You have been invited to a group by {inviter}. " +
                "Use `group-invite-accept` to accept.");
            lock (_chatGate)
            {
                if (LivePrompt is { } lp && lp.TryWriteLineAbove(line)) return;
                ChatOutput.WriteLine(line);
                ChatOutput.Flush();
            }
        }
        catch { /* best-effort notification */ }
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
            // When the interactive editor has a prompt on screen, slot the chat
            // line in above it and let the prompt redraw beneath; otherwise
            // (piped output, between lines, mid-command) write it plainly.
            if (LivePrompt is { } lp && lp.TryWriteLineAbove(colored)) return;
            ChatOutput.WriteLine(colored);
            ChatOutput.Flush();
        }
    }

    /// <summary>
    /// Cleanly log out: stop the keepalive / comms-alive / sector-drain loops
    /// and close both TCP planes. Closing the sockets is what the real client
    /// does on quit -- the server reaps the player on disconnect
    /// (PlayerConnection.cpp drop-on-disconnect -> LeaveGroup +
    /// DropPlayerFromGalaxy), so no synthetic logoff opcode is needed (and
    /// inventing one would be an unpinned wire change). Idempotent -- safe to
    /// call from the <c>quit</c> command and again from <see cref="DisposeAsync"/>.
    /// </summary>
    public async Task LogoutAsync()
    {
        await StopKeepaliveAsync().ConfigureAwait(false);
        await StopCommsAliveAsync().ConfigureAwait(false);
        await StopSectorDrainAsync().ConfigureAwait(false);
        if (SectorUdp is not null) { await SectorUdp.StopAsync().ConfigureAwait(false); SectorUdp.Dispose(); SectorUdp = null; }
        if (_sector is not null) { DetachDumpHook(_sector); await _sector.DisposeAsync(); _sector = null; }
        if (_global is not null) { DetachDumpHook(_global); await _global.DisposeAsync(); _global = null; }
    }

    public async ValueTask DisposeAsync()
    {
        await LogoutAsync().ConfigureAwait(false);
        _mvas?.Dispose();
    }
}
