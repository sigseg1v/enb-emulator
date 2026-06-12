// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya

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
/// The pair of sector ids carried by a 0x003A SERVER_HANDOFF: where the server
/// is sending the avatar (<paramref name="ToSectorId"/>) and the sector it is
/// handing off FROM (<paramref name="FromSectorId"/>). The re-join MasterJoin
/// echoes FromSectorId so the destination sector spawns the avatar at the gate
/// that links back to the origin.
/// </summary>
public readonly record struct HandoffTarget(int ToSectorId, int FromSectorId);

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

    /// <summary>
    /// How <c>login</c> reads a password not given on the command line.
    /// Defaults to the console (asterisk-masked on a TTY); overridable for tests.
    /// </summary>
    public IPasswordPrompt PasswordPrompt { get; set; } = new ConsolePasswordPrompt();

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

    // Sector id -> name learned at runtime from a 0x0097 GALAXY_MAP Type-4
    // frame (the server's "you are here" update), keyed when one arrives while
    // ActiveSectorId is known. This is the only name source for a station
    // interior (id > 9999), which SectorCatalog does not carry; static space
    // sectors fall back to SectorCatalog, so only the >9999 interiors need this.
    private readonly Dictionary<int, string> _sectorNameCache = new();
    private readonly object _sectorNameGate = new();

    /// <summary>
    /// "&lt;name&gt; (&lt;id&gt;)" for a sector, preferring a name the server
    /// reported at runtime (covers station interiors) over the static
    /// <see cref="SectorCatalog"/>, and falling back to "sector &lt;id&gt;"
    /// when neither knows it. This is the single place that turns a raw sector
    /// id into the labelled form shown in the prompt, the character list, and
    /// the gate/undock/dock handoff lines.
    /// </summary>
    public string SectorLabel(int sectorId)
    {
        string? runtime;
        lock (_sectorNameGate) _sectorNameCache.TryGetValue(sectorId, out runtime);
        runtime ??= SectorCatalog.Name(sectorId);
        return runtime is { } n ? $"{n} ({sectorId})" : $"sector {sectorId}";
    }

    /// <summary>
    /// Capture the current sector's name from a 0x0097 GALAXY_MAP Type-4 frame
    /// (PlayerID then NUL-terminated System / Sector / Station strings -- see
    /// GalaxyMapRecord). The Station name wins when docked (the avatar is in the
    /// station interior); otherwise the Sector name is used. Keyed into the
    /// runtime cache against the current sector id. Never throws -- a name is a
    /// convenience, never a failure point.
    /// </summary>
    private void CaptureSectorName(Packet p)
    {
        if (p.Header.Opcode != 0x0097) return;
        try
        {
            var s = p.Payload.Span;
            if (s.Length < 12) return;
            if (BinaryPrimitives.ReadInt32LittleEndian(s[..4]) != 4) return; // Type 4 only
            int off = 12; // skip Type, Size, PlayerID
            string? system = ReadCString(s, ref off);
            string? sector = ReadCString(s, ref off);
            string? station = ReadCString(s, ref off);
            _ = system;
            // When docked the avatar sits in the station interior, so the
            // station's own name is the right label for the current sector;
            // in open space it is empty and the sector name applies.
            string? name = !string.IsNullOrEmpty(station) ? station : sector;
            if (string.IsNullOrEmpty(name)) return;
            if (ActiveSectorId is { } sid)
                lock (_sectorNameGate) _sectorNameCache[sid] = name;
        }
        catch { /* best-effort: the static catalog still covers space sectors */ }
    }

    private static string? ReadCString(ReadOnlySpan<byte> s, ref int off)
    {
        if (off >= s.Length) return null;
        int nul = s[off..].IndexOf((byte)0);
        int end = nul < 0 ? s.Length : off + nul;
        string str = System.Text.Encoding.ASCII.GetString(s[off..end]);
        off = end + 1;
        return str;
    }

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
                // Prefer the server-reported / catalog name; fall back to the
                // bare id. Compact form for the prompt: "who@Name" (or the raw
                // id when unknown) -- the "(id)" form is kept for the verbose
                // surfaces (list / handoff lines), not the every-keystroke prompt.
                if (ActiveSectorId is not { } sid) return who;
                string? name;
                lock (_sectorNameGate) _sectorNameCache.TryGetValue(sid, out name);
                name ??= SectorCatalog.Name(sid);
                return name is { } n ? $"{who}@{n}" : $"{who}@{sid}";
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

    /// <summary>
    /// Turns the world-model events + select inbound opcodes into one-line
    /// human notices (scanner contacts, damage, warp, sector load, system
    /// messages). Reads <see cref="GameId"/> for the "is this me?" checks.
    /// </summary>
    private EventNarrator Narrator => _narrator ??= new EventNarrator(World, () => GameId);
    private EventNarrator? _narrator;

    /// <summary>
    /// When true (the default), inbound frames are narrated as one-line notices
    /// (e.g. "Mob (L17, hostile, Mbonae) appeared in scanner range, d=...").
    /// Independent of <see cref="DumpEnabled"/>; toggled by the
    /// <c>narrate-on</c> / <c>narrate-off</c> REPL commands.
    /// </summary>
    public bool NarrateEnabled { get; set; } = true;

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
    /// arrives; the <c>group accept</c> command reads it to decide whether there
    /// is anything to accept. Cleared once accepted/left.
    /// </summary>
    public string? PendingGroupInviter { get; set; }

    /// <summary>
    /// Latest self group/formation state, decoded from the self <c>PlayerIndex</c>
    /// aux (0x001B, GameID == 0) whenever one carries GroupInfo. Drives the
    /// availability gating of the <c>group</c> and <c>formation</c> subcommands.
    /// Defaults to solo (not grouped, not leader, no formation); only a self aux
    /// that actually carries GroupInfo updates it (see
    /// <see cref="TrackSelfGroup"/>), so a credits-only diff aux never clobbers it.
    /// </summary>
    public AuxDataRecord.GroupState SelfGroup { get; private set; }

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
        var events = World.Ingest(p);
        EchoChat(p, PacketDirection.Inbound);
        Narrate(p, events);
        AnnounceGroupInvite(p);
        TrackSelfGroup(p);
        CaptureHandoff(p);
        CaptureSectorName(p);
        PrintIfEnabled(p, PacketDirection.Inbound);
    }

    /// <summary>
    /// Emit the human-readable notices for an inbound frame (scanner contacts,
    /// damage, warp, sector load, system messages) above the live prompt, the
    /// same way chat echo does. Gated by <see cref="NarrateEnabled"/>. Never
    /// throws -- a notice is a convenience, never a failure point.
    /// </summary>
    private void Narrate(Packet p, IReadOnlyList<WorldEvent> events)
    {
        if (!NarrateEnabled) return;
        try
        {
            foreach (var line in Narrator.Narrate(p, events))
            {
                lock (_chatGate)
                {
                    if (LivePrompt is { } lp && lp.TryWriteLineAbove(line)) continue;
                    ChatOutput.WriteLine(line);
                    ChatOutput.Flush();
                }
            }
        }
        catch { /* best-effort notification */ }
    }

    private TaskCompletionSource<HandoffTarget>? _handoffTcs;

    /// <summary>
    /// Arm a one-shot capture of the next inbound 0x003A SERVER_HANDOFF's
    /// sector ids. Returns a task that completes when that frame crosses the
    /// active sector connection (read cleanly by the background drain -- so no
    /// mid-frame cancellation of the RC4-stateful reader is needed). The
    /// <c>undock</c>/<c>gate</c> commands await this to learn where the server
    /// is handing the avatar off to (<see cref="HandoffTarget.ToSectorId"/>),
    /// then re-join that sector -- echoing
    /// <see cref="HandoffTarget.FromSectorId"/> back in the re-join MasterJoin
    /// so the destination sector spawns the avatar at the gate that links back
    /// to the origin (Player::SendLoginCamera -> FindGate(m_FromSectorID)).
    /// See <see cref="CaptureHandoff"/>.
    /// </summary>
    public Task<HandoffTarget> ArmHandoffCapture()
    {
        var tcs = new TaskCompletionSource<HandoffTarget>(TaskCreationOptions.RunContinuationsAsynchronously);
        _handoffTcs = tcs;
        return tcs.Task;
    }

    /// <summary>
    /// Complete an armed handoff capture with the sector ids carried by a
    /// 0x003A SERVER_HANDOFF. The inner MasterJoin-shaped struct puts
    /// ToSectorID at offset 20 and FromSectorID at offset 24, BIG-ENDIAN
    /// (SendServerHandoff writes both via ntohl -- PlayerConnection.cpp:10167 --
    /// while the rest of the struct is host LE). FromSectorID is the sector the
    /// server is handing us off FROM; the destination needs it to position the
    /// avatar at the correct back-gate. Never throws -- a malformed frame must
    /// not kill the drain.
    /// </summary>
    private void CaptureHandoff(Packet p)
    {
        if (p.Header.Opcode != OpcodeId.Known.ServerHandoff.Value) return;
        var tcs = _handoffTcs;
        if (tcs is null) return;
        try
        {
            var span = p.Payload.Span;
            if (span.Length < 28) return;
            int toSectorId   = BinaryPrimitives.ReadInt32BigEndian(span.Slice(20, 4));
            int fromSectorId = BinaryPrimitives.ReadInt32BigEndian(span.Slice(24, 4));
            _handoffTcs = null;
            tcs.TrySetResult(new HandoffTarget(toSectorId, fromSectorId));
        }
        catch { /* leave the capture armed; the awaiter will time out */ }
    }

    private const string GroupInviteSuffix = " is requesting you to join their group";

    /// <summary>
    /// Surface an inbound group invite (0x001E GROUP, Flag 0x01, message
    /// "&lt;name&gt; is requesting you to join their group") as a one-line prompt
    /// telling the operator how to accept, and remember the inviter so
    /// <c>group accept</c> has something to act on. Never throws -- a
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
                "Use `group accept` to accept.");
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
    /// Update <see cref="SelfGroup"/> from an inbound self <c>PlayerIndex</c> aux
    /// (0x001B). Only a frame that actually carries GroupInfo moves the cached
    /// state -- the server re-sends the self aux on every group/formation change
    /// (GroupManager.cpp: SetData/SendEmptyGroupAux/SetFormation/FormUp/Leave),
    /// so the gating stays current. Never throws -- state tracking is a
    /// convenience, never a failure point for the drain.
    /// </summary>
    private void TrackSelfGroup(Packet p)
    {
        if (p.Header.Opcode != 0x001B) return;
        try
        {
            if (AuxDataRecord.TryExtractGroupState(p.Payload.Span) is { } gs)
                SelfGroup = gs;
        }
        catch { /* best-effort state tracking */ }
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
        var events = World.Ingest(p);
        EchoChat(p, PacketDirection.Inbound);
        Narrate(p, events);
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

    // server/src/SectorData.h:29 -- #define PLAYER_TAG (1<<30). The retail
    // client sends the avatar's GameID with this bit cleared in the PlayerID
    // field of most request structs (see UndockCommand for the same convention).
    private const int PlayerTag = 1 << 30;

    /// <summary>
    /// Build the 8-byte 0x00B9 LOGOFF_REQUEST payload (struct LogoffRequest,
    /// common/include/net7/PacketStructures.h:583 -- <c>int32 PlayerID,
    /// int32 LogOutType</c>, both little-endian). The real client sends this
    /// when the player quits to character select; the server's
    /// <c>Player::HandleLogoffRequest</c> (PlayerConnection.cpp:7722) drops the
    /// player immediately (LeaveGroup + DropPlayerFromGalaxy) and replies
    /// 0x00BA. The server ignores both fields (the struct cast is commented
    /// out), but we reproduce the retail values for fidelity: PlayerID = our
    /// untagged GameID, LogOutType = 0.
    /// </summary>
    public static byte[] BuildLogoffRequest(int gameId)
    {
        var payload = new byte[8];
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(0), gameId & ~PlayerTag);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(4), 0); // LogOutType
        return payload;
    }

    /// <summary>
    /// Cleanly log out. First send the real client's 0x00B9 LOGOFF_REQUEST on
    /// the sector connection so the server drops the player node *now*
    /// (HandleLogoffRequest -> DropPlayerFromGalaxy) rather than waiting for the
    /// disconnect-timeout reaper -- without this the account stays "logged in"
    /// for the reaper's grace period and a fresh login is rejected with
    /// G_ERROR_ACCOUNT_IN_USE (CheckAccountInUse on the still-Active player).
    /// Then stop the keepalive / comms-alive / sector-drain loops and close both
    /// TCP planes. Best-effort: the logoff send is wrapped so a half-dead
    /// connection still tears down. Idempotent -- safe to call from the
    /// <c>quit</c> command and again from <see cref="DisposeAsync"/>.
    /// </summary>
    public async Task LogoutAsync()
    {
        // Tell the server we're leaving *before* we drop the socket, so it
        // reaps the player synchronously instead of on idle-timeout.
        if (_sector is { } sector && GameId is { } gameId)
        {
            try
            {
                await sector.SendAsync(
                    Packet.ForOpcode(OpcodeId.Known.LogoffRequest.Value, BuildLogoffRequest(gameId)),
                    CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // Connection already dead -> the disconnect reaper covers us.
                try { DumpOutput.WriteLine($"[logoff] send skipped: {ex.GetType().Name}: {ex.Message}"); } catch { }
            }
        }

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
