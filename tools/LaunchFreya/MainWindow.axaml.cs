using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LaunchFreya.Config;
using LaunchFreya.Update;
#if CHECK_FOR_UPDATES
using System.Diagnostics;
#endif
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;
using MsBoxIcon = MsBox.Avalonia.Enums.Icon;

namespace LaunchFreya
{
    // Avalonia port of LaunchNet7/FormMain.cs. Trimmed for the launcher's
    // core job:
    //   - pick a server + host
    //   - check it (TCP probe of proxy port 3809)
    //   - patch the client's ini files and authlogin.dll
    //   - launch the client (under WINE on non-Windows)
    //
    // Dropped vs. the WinForms original (documented in README):
    //   - Updater (HTTP file sync against patch.net-7.org) — the upstream
    //     host is gone; resurrecting it is its own task.
    //   - WebBrowser pane (drop, no built-in WebView in Avalonia).
    //   - Self-update via ExeUpdater (only meaningful on Windows; a
    //     Linux launcher updates itself via the OS package manager).
    public partial class MainWindow : Window
    {
        readonly LaunchSetting _setting = new LaunchSetting();
        readonly UserSettings _user;
        LauncherConfig _config;
        HostConfig _lastSelectedHost;
        Launcher _activeLauncher;

        // Monotonic token for in-flight status probes. Each new probe bumps it
        // and captures the value; a probe only writes the status label if its
        // token still matches. Without this, switching the Emulator/Server
        // selection races: an earlier probe (e.g. the stale-host one kicked
        // while FillHosts clears the list) can land AFTER the current one and
        // leave the label showing the wrong server's status.
        int _statusProbeGen;

        // Periodic liveness re-probe. The status label is otherwise a one-shot:
        // once a probe writes ONLINE/OFFLINE nothing re-checks, so a server that
        // goes down (or comes back) after the initial check leaves the label
        // frozen at a stale value. This timer re-runs the probe for the current
        // multiplayer selection on a fixed cadence. Auto re-probes never open the
        // update dialog (only a user-initiated Check / selection does) so it
        // cannot nag, and they skip while the client is running.
        DispatcherTimer _statusTimer;
        bool _clientRunning;
        static readonly TimeSpan StatusRefreshInterval = TimeSpan.FromSeconds(10);

        // Liveness-label colours, matched to the website's status dots
        // (freya/online/web/src/styles/app.css: --ok / --danger / --warm / --text-dim).
        static readonly IBrush StatusOkBrush     = new SolidColorBrush(Color.Parse("#FF5FD37F"));
        static readonly IBrush StatusDangerBrush = new SolidColorBrush(Color.Parse("#FFEC5C50"));
        static readonly IBrush StatusWarmBrush   = new SolidColorBrush(Color.Parse("#FFEFB05C"));
        static readonly IBrush StatusDimBrush    = new SolidColorBrush(Color.Parse("#FFA2ACB6"));

        // Set the server-status label text and colour it to match: green when
        // reachable/ready, red when offline, amber while checking or when an
        // update is pending. Callers have already gen-guarded the write.
        void SetServerStatus(string text)
        {
            c_ServerStatus.Text = text;
            string t = (text ?? "").ToUpperInvariant();
            c_ServerStatus.Foreground =
                t.Length == 0 ? StatusDimBrush :
                (t.Contains("ONLINE") || t.Contains("READY")) ? StatusOkBrush :
                t.Contains("OFFLINE") ? StatusDangerBrush :
                StatusWarmBrush; // CHECKING / UPDATE / UPDATE REQUIRED
        }

        public MainWindow()
        {
            InitializeComponent();
            _user = UserSettings.Load();
            Opened += OnOpened;
            Closing += OnClosing;
        }

        ServerConfig CurrentEmulator
            => c_ComboBox_Emulators.SelectedItem as ServerConfig;

        bool TryGetSelectedHost(out ServerConfig emu, out HostConfig host)
        {
            emu = CurrentEmulator;
            host = null;
            if (emu == null) return false;

            // The Server control is a free-text AutoCompleteBox: the typed text
            // is authoritative. If it matches a known host from the config we
            // reuse that entry (to carry its configured auth port); otherwise we
            // synthesize a host from the typed value + the port box.
            var name = (c_ComboBox_Servers.Text ?? "").Trim();
            if (string.IsNullOrEmpty(name))
            {
                if (_lastSelectedHost != null) { host = _lastSelectedHost; return true; }
                return false;
            }
            host = emu.Hosts.FirstOrDefault(h =>
                string.Equals(h.Hostname, name, StringComparison.OrdinalIgnoreCase));
            if (host == null)
            {
                int port = int.TryParse(c_TextBox_Port.Text, out var p) && p > 0 && p < 65536
                    ? p : 443;
                host = new HostConfig { Hostname = name, AuthenticationPort = port };
            }
            return true;
        }

        // Strip a URL scheme / path so a value like "https://enb.sigsegv.land"
        // resolves: the proxy + client speak raw DNS/TCP, not HTTP, so only the
        // bare host survives to Dns.GetHostAddresses / TcpClient.ConnectAsync.
        static string NormalizeHost(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return raw;
            var s = raw.Trim();
            int scheme = s.IndexOf("://", StringComparison.Ordinal);
            if (scheme >= 0) s = s.Substring(scheme + 3);
            int slash = s.IndexOf('/');
            if (slash >= 0) s = s.Substring(0, slash);
            return s.Trim();
        }

        // ---- lifecycle ----

        void OnOpened(object sender, EventArgs e)
        {
            Title = "LaunchFreya";
            c_Status.Text = "Loading configuration...";

#if CHECK_FOR_UPDATES
            // Clear any *.old / staging leftovers from a prior self-replace
            // before this run does anything else (best-effort, never throws).
            try { new Updater(AppContext.BaseDirectory, Environment.ProcessPath, AppendLog).CleanupStaleArtifacts(); }
            catch (Exception ex) { AppendLog($"update: cleanup skipped: {ex.Message}"); }
#endif

            // Populate the mod store (<launcher-dir>/mods) from the bundled mods on
            // a fresh/offline install so the Configure Mods UI and staging both see
            // them. The self-updater refreshes our mods there when online.
            ModStore.SeedFromBundle(AppendLog);

            // Locate FreyaLauncher.cfg: prefer next to the .dll (deployed
            // alongside) and fall back to the legacy LaunchNet7 source
            // tree so a dev `dotnet run` works.
            var cfgPath = ResolveConfigPath();
            try
            {
                _config = LauncherConfig.Load(cfgPath);
                AppendLog($"Loaded config from {cfgPath} ({_config.Servers.Count} emulator(s)).");
            }
            catch (Exception ex)
            {
                // A malformed cfg used to fail silently here, leaving an empty
                // server list with no on-screen explanation. Surface it.
                AppendLog($"Config load FAILED ({cfgPath}): " + ex.Message);
                c_Status.Text = $"Config load failed: {ex.Message}";
                _config = new LauncherConfig();
            }

            // Restore user prefs
            if (!string.IsNullOrEmpty(_user.AuthenticationPort))
                c_TextBox_Port.Text = _user.AuthenticationPort;
            if (_user.FormMainPositionX > 0 && _user.FormMainPositionY > 0)
                Position = new Avalonia.PixelPoint(_user.FormMainPositionX, _user.FormMainPositionY);
            c_CheckBox_LuaMods.IsChecked = _user.UseClientMods;

            FillEmulators();
            FillClientPath();
            c_Status.Text = "Please select a server and hit Play.";

            _statusTimer = new DispatcherTimer { Interval = StatusRefreshInterval };
            _statusTimer.Tick += OnStatusTimerTick;
            _statusTimer.Start();
        }

        void OnClosing(object sender, EventArgs e)
        {
            _statusTimer?.Stop();
            _user.FormMainPositionX = Position.X;
            _user.FormMainPositionY = Position.Y;
            _user.Save();
            _activeLauncher?.Dispose();
        }

        // Re-probe the currently-selected multiplayer server so its status stays
        // fresh. No-ops for single-player (never updates) and while the client is
        // running (status is pinned to "Client running"). Silent: no CHECKING
        // flicker and no update prompt -- it only refreshes the liveness label and
        // the Play gate.
        void OnStatusTimerTick(object sender, EventArgs e)
        {
            if (_clientRunning) return;
            var emu = CurrentEmulator;
            if (emu == null || emu.IsSinglePlayer) return;
            if (!TryGetSelectedHost(out _, out var host)) return;
            int gen = ++_statusProbeGen;
            _ = KickServerProbe(NormalizeHost(host.Hostname), GetProbePort(host), gen, auto: true);
        }

        static string ResolveConfigPath()
        {
            // 1) Next to the assembly (production deployment).
            var beside = Path.Combine(AppContext.BaseDirectory, "FreyaLauncher.cfg");
            if (File.Exists(beside)) return beside;

            // 2) Sibling project (dev workflow). Walk up to the repo
            // root and look at the legacy tools/launchnet7/LaunchNet7/LaunchNet7.cfg.
            var dir = AppContext.BaseDirectory;
            for (int i = 0; i < 8 && !string.IsNullOrEmpty(dir); i++)
            {
                var candidate = Path.Combine(dir,
                    "tools", "launchnet7", "LaunchNet7", "LaunchNet7.cfg");
                if (File.Exists(candidate)) return candidate;
                dir = Path.GetDirectoryName(dir);
            }
            return beside; // returns path-that-doesn't-exist so caller logs
        }

        // ---- UI fills ----

        void FillEmulators()
        {
            c_ComboBox_Emulators.Items.Clear();
            foreach (var s in _config.Servers)
                c_ComboBox_Emulators.Items.Add(s);

            int idx = -1;
            for (int i = 0; i < _config.Servers.Count; i++)
            {
                if (string.Equals(_config.Servers[i].Name, _user.LastEmulatorName,
                    StringComparison.OrdinalIgnoreCase))
                {
                    idx = i; break;
                }
            }
            if (_config.Servers.Count > 0)
                c_ComboBox_Emulators.SelectedIndex = idx >= 0 ? idx : 0;
        }

        void FillHosts()
        {
            var emu = CurrentEmulator;
            if (emu == null)
            {
                c_ComboBox_Servers.ItemsSource = null;
                c_ComboBox_Servers.Text = "";
                return;
            }

            // Drop blank placeholder hosts (some emulators ship a single empty
            // <host/> as a "type your own" stub) so the dropdown never offers an
            // empty row.
            var hostnames = emu.Hosts.Select(h => h.Hostname)
                .Where(h => !string.IsNullOrWhiteSpace(h)).ToList();
            c_ComboBox_Servers.ItemsSource = hostnames;

            // Default the editable box. When this is the emulator the user last
            // launched, restore the exact server they used -- even if it isn't a
            // predefined <host> entry (the box is free-text). This is what
            // play-online relies on: it pre-writes LastEmulatorName=Net7MP +
            // LastServerName=<cloud host>. Otherwise (a fresh emulator switch, or
            // no saved server at all) default to the emulator's first real host
            // -- Net7MP ships enb.sigsegv.land, so multiplayer autofills there.
            string def;
            if (string.Equals(emu.Name, _user.LastEmulatorName, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(_user.LastServerName))
            {
                def = _user.LastServerName;
            }
            else
            {
                def = hostnames.Count > 0 ? hostnames[0] : "";
            }

            // Push the default into the editable box. A bare .Text assignment
            // made mid-fill is swallowed by AutoCompleteBox before its template
            // settles -- that left the Server box blank even when play-online had
            // written LastServerName. Re-post it on the next UI tick so it sticks,
            // and kick the status probe from inside the post so it sees the final
            // text. (Setting .Text fires OnServerTextChanged, which clears the
            // stale status; the probe below re-establishes it.)
            c_ComboBox_Servers.Text = def;
            Dispatcher.UIThread.Post(() =>
            {
                c_ComboBox_Servers.Text = def;
                UpdateForSelectedHost();
            });
        }

        void FillClientPath()
        {
            string path = _user.ClientPath;
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                path = null;
                // Best-effort default to the EnB installer's location. The Linux
                // WINE installer drops the client at
                //   C:\Program Files\EA GAMES\Earth & Beyond\release\client.exe
                // (non-x86; see client/linux-installer/install-enb-linux.sh).
                // We also probe Program Files (x86) since a native-Windows EA
                // installer may land a 32-bit title there.
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    foreach (var pf in new[]
                    {
                        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                    })
                    {
                        if (string.IsNullOrEmpty(pf)) continue;
                        var cand = Path.Combine(pf, "EA GAMES", "Earth & Beyond", "release", "client.exe");
                        if (File.Exists(cand)) { path = cand; break; }
                    }
                }
            }
            if (!string.IsNullOrEmpty(path))
            {
                c_TextBox_Client.Text = path;
                _setting.ClientPath = path;
                _user.ClientPath = path;
                _user.Save();
            }
        }

        // ---- event handlers ----

        void OnEmulatorsChanged(object sender, SelectionChangedEventArgs e)
            => FillHosts();

        // User typed in (or picked from) the editable Server box. Wipe any stale
        // status immediately so a previously-probed "ONLINE" never lingers on a
        // different server; the actual probe fires on commit (focus-leave) or
        // the Check button, not per keystroke.
        void OnServerTextChanged(object sender, TextChangedEventArgs e)
        {
            _statusProbeGen++;          // invalidate any in-flight probe
            if (CurrentEmulator?.IsSinglePlayer == true)
            {
                c_ServerStatus.Text = "READY";
                c_Button_Check.IsEnabled = false;
                GatePlay(true);         // single-player never updates
            }
            else
            {
                c_ServerStatus.Text = "";
                c_Button_Check.IsEnabled = true;
                GatePlay(false);        // re-gate until the next probe confirms
            }
        }

        // Picked a known host from the dropdown -> probe it immediately.
        void OnServerSelected(object sender, SelectionChangedEventArgs e)
            => UpdateForSelectedHost();

        // Typed a custom value and moved focus away -> probe on commit.
        void OnServerCommitted(object sender, RoutedEventArgs e)
            => UpdateForSelectedHost();

        void UpdateForSelectedHost()
        {
            if (!TryGetSelectedHost(out var emu, out var host))
            {
                _statusProbeGen++;
                SetServerStatus("");
                GatePlay(false);
                return;
            }

            c_TextBox_Port.Text = host.AuthenticationPort.ToString();

            if (emu.IsSinglePlayer)
            {
                _statusProbeGen++;
                SetServerStatus("READY");
                c_Button_Check.IsEnabled = false;
                GatePlay(true);
            }
            else
            {
                int gen = ++_statusProbeGen;
                SetServerStatus("CHECKING");
                c_Button_Check.IsEnabled = true;
                GatePlay(false);
                _ = KickServerProbe(NormalizeHost(host.Hostname), GetProbePort(host), gen);
            }
            _lastSelectedHost = host;
        }

        int GetProbePort(HostConfig host)
        {
            // Probe the upstream auth port (Net7SSL's TLS endpoint).
            // Reachability here means "the login server is up and the client
            // can talk to it" — the canonical liveness signal for the dev
            // stack and any remote server.
            if (int.TryParse(c_TextBox_Port.Text, out var p) && p > 0 && p < 65536)
                return p;
            return host.AuthenticationPort;
        }

        async Task CheckServerStatusAsync(string host, int port, int gen)
        {
            // TCP connect probe of the auth port. We don't speak TLS here —
            // just verify the port accepts connections. `gen` is the probe token
            // captured when this probe was kicked: if the selection has changed
            // since (gen != _statusProbeGen), this result is stale and we drop it
            // rather than stomp the label for the now-current server.
            void Write(string text)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (gen == _statusProbeGen) SetServerStatus(text);
                });
            }

            try
            {
                using var client = new TcpClient();
                var connectTask = client.ConnectAsync(host, port);
                var timeoutTask = Task.Delay(5000);
                var finished = await Task.WhenAny(connectTask, timeoutTask);
                if (finished == timeoutTask)
                {
                    Write("OFFLINE");
                    return;
                }
                await connectTask;
                Write("ONLINE");
            }
            catch (Exception ex)
            {
                Write("OFFLINE");
                AppendLog($"Server probe {host}:{port} failed: {ex.Message}");
            }
        }

        // Dispatch the multiplayer liveness probe. On the self-updater build the
        // /updateCheck POST IS the probe (a reachable login server answers it)
        // and also gates Play on version match; on the Linux dev build it's a
        // plain TCP connect to the auth port.
        // `auto` marks a periodic background refresh (the status timer): it still
        // updates the liveness label + Play gate but never opens the update
        // dialog, so the timer can't nag. User-initiated probes pass auto:false.
        Task KickServerProbe(string host, int port, int gen, bool auto = false)
        {
#if CHECK_FOR_UPDATES
            return RunUpdateProbeAsync(host, port, gen, auto);
#else
            return CheckServerStatusAsync(host, port, gen);
#endif
        }

        // Enable/disable Play. Only meaningful on the self-updater build, where
        // Play stays gated until a probe confirms an up-to-date multiplayer
        // server (or single-player, which never updates). On the dev build this
        // is a deliberate no-op so Play is always available.
        void GatePlay(bool enabled)
        {
#if CHECK_FOR_UPDATES
            c_Button_Play.IsEnabled = enabled;
#endif
        }

#if CHECK_FOR_UPDATES
        // Multiplayer probe + version gate. Mirrors CheckServerStatusAsync's
        // stale-token discipline: every UI write is guarded by `gen` so a probe
        // whose selection has since changed never stomps the current one.
        async Task RunUpdateProbeAsync(string host, int port, int gen, bool auto = false)
        {
            void WriteStatus(string text)
                => Dispatcher.UIThread.Post(() => { if (gen == _statusProbeGen) SetServerStatus(text); });
            void SetPlay(bool enabled)
                => Dispatcher.UIThread.Post(() => { if (gen == _statusProbeGen) c_Button_Play.IsEnabled = enabled; });

            SetPlay(false);   // gated until we confirm up-to-date
            string url = $"https://{host}:{port}/updateCheck";
            var updater = new Updater(AppContext.BaseDirectory, Environment.ProcessPath, AppendLog);

            UpdateCheckResponse resp = await updater.CheckAsync(url);
            if (gen != _statusProbeGen) return;   // selection changed; drop result

            // Fail closed: an unreachable / not-ready server, an unparseable
            // reply, or any unexpected status leaves Play disabled.
            if (resp == null)
            {
                WriteStatus("OFFLINE");
                SetPlay(false);
                return;
            }

            // Reconcile OUR Lua mods against the server's published set whenever
            // the user has client mods enabled. This is orthogonal to the binary
            // version gate below (mods never gate Play) and best-effort: a mod
            // download/extract failure is logged, never fatal. Skipped on the
            // background auto-refresh so a timer tick never downloads.
            if (!auto && _user.UseClientMods)
            {
                try
                {
                    int n = await updater.ReconcileModsAsync(resp,
                        msg => Dispatcher.UIThread.Post(() => { if (gen == _statusProbeGen) c_Status.Text = msg; }));
                    if (gen != _statusProbeGen) return;   // selection changed mid-reconcile
                    if (n > 0) AppendLog($"mods: {n} mod(s) updated from the server.");
                }
                catch (Exception ex) { AppendLog($"mods: reconcile error: {ex.Message}"); }
            }

            // Reconcile operator-supplied game-data patches (e.g. enb-patch.exe)
            // against the EnB game install. Independent of the mods toggle -- a
            // patch is game data, not optional content -- but it needs a resolved
            // EnB install root, and is skipped on the background auto-refresh so a
            // timer tick never downloads/runs the ~200MB executable.
            if (!auto && resp.Patches != null && resp.Patches.Count > 0
                && !string.IsNullOrEmpty(_setting.BaseFolder))
            {
                try
                {
                    int n = await updater.ReconcilePatchesAsync(resp, _setting.BaseFolder,
                        msg => Dispatcher.UIThread.Post(() => { if (gen == _statusProbeGen) c_Status.Text = msg; }));
                    if (gen != _statusProbeGen) return;   // selection changed mid-reconcile
                    if (n > 0) AppendLog($"patches: {n} patch(es) applied to the EnB install.");
                }
                catch (Exception ex) { AppendLog($"patches: reconcile error: {ex.Message}"); }
            }

            if (string.Equals(resp.Status, UpdateStatus.UpToDate, StringComparison.OrdinalIgnoreCase))
            {
                WriteStatus("ONLINE");
                SetPlay(true);
                return;
            }
            if (string.Equals(resp.Status, UpdateStatus.UpdateNeeded, StringComparison.OrdinalIgnoreCase))
            {
                WriteStatus("UPDATE");
                SetPlay(false);
                // A background refresh reports the available update but does not
                // pop the dialog every tick; the user applies it via Check or by
                // re-selecting the server.
                if (!auto)
                    await PromptAndApplyUpdateAsync(updater, resp, gen);
                return;
            }
            AppendLog($"update probe: unexpected status '{resp.Status}'");
            WriteStatus("OFFLINE");
            SetPlay(false);
        }

        // Prompt to apply a needed update. Decline keeps Play gated (a stale
        // build cannot connect). Accept downloads + verifies everything before
        // touching the install; on a self-replace the new image is relaunched
        // and this one exits.
        async Task PromptAndApplyUpdateAsync(Updater updater, UpdateCheckResponse resp, int gen)
        {
            int n = resp.Files?.Count ?? 0;
            var choice = await MessageBoxManager.GetMessageBoxStandard(
                "Freya - Update available",
                $"An update is available ({n} file(s)). Download and apply now?",
                ButtonEnum.OkCancel, MsBoxIcon.Question).ShowWindowDialogAsync(this);
            if (choice != ButtonResult.Ok)
            {
                if (gen == _statusProbeGen)
                {
                    SetServerStatus("UPDATE REQUIRED");
                    c_Button_Play.IsEnabled = false;
                }
                return;
            }

            c_UpdateSpinner.IsVisible = true;
            c_Button_Play.IsEnabled = false;
            c_Button_Check.IsEnabled = false;
            try
            {
                await updater.DownloadAndApplyAsync(resp,
                    msg => Dispatcher.UIThread.Post(() => c_Status.Text = msg));
            }
            catch (UpdateException ex)
            {
                c_UpdateSpinner.IsVisible = false;
                c_Button_Check.IsEnabled = true;
                AppendLog($"update failed: {ex.Message}");
                await Err($"Update failed: {ex.Message}\nThe install was left unchanged; try again.");
                return;
            }
            c_UpdateSpinner.IsVisible = false;

            if (updater.SelfReplaced)
            {
                // The running launcher was renamed aside (Windows rename-self):
                // relaunch the freshly-installed image and exit so the user lands
                // on the updated build, which deletes the *.old on startup.
                try
                {
                    string exe = Environment.ProcessPath;
                    if (!string.IsNullOrEmpty(exe))
                        Process.Start(new ProcessStartInfo { FileName = exe, UseShellExecute = false });
                }
                catch (Exception ex) { AppendLog($"relaunch failed: {ex.Message}"); }
                Close();
                return;
            }

            // Only non-self files changed (e.g. just FreyaProxy.exe). No relaunch
            // needed -- re-probe to confirm we're now up to date and re-gate Play.
            c_Status.Text = "Update applied.";
            UpdateForSelectedHost();
        }
#endif

#if !CHECK_FOR_UPDATES
        // Ensure the EnB install is patched to retail before a local (play-local)
        // launch. Returns true if it is safe to launch; false if launch must be
        // blocked (the user was shown why). Online builds gate this through the
        // /updateCheck patch reconcile instead.
        async Task<bool> EnsureLocalPatchAsync()
        {
            // No resolved install root: let the normal launch path report the
            // missing/invalid client.exe rather than second-guessing it here.
            if (string.IsNullOrEmpty(_setting.BaseFolder))
                return true;

            var updater = new Updater(AppContext.BaseDirectory, Environment.ProcessPath, AppendLog);
            string localPatch = FindLocalEnbPatch();
            var result = await updater.ReconcileLocalPatchAsync(_setting.BaseFolder, localPatch,
                msg => Dispatcher.UIThread.Post(() => c_Status.Text = msg));

            switch (result)
            {
                case Updater.LocalPatchResult.AlreadyPatched:
                case Updater.LocalPatchResult.Applied:
                    return true;

                case Updater.LocalPatchResult.MissingPatch:
                    await Err(
                        "Your Earth & Beyond install is NOT patched to retail.\n\n" +
                        "The launcher could not find enb-patch.exe at:\n\n" +
                        "    deploy/do/patches/enb-patch.exe\n\n" +
                        "You must patch your client to retail before playing the game.\n" +
                        "Place the retail enb-patch.exe at that path in the repo (it is\n" +
                        "intentionally not committed) and press Play again.");
                    return false;

                default:
                    await Err(
                        "Patching your Earth & Beyond install to retail failed.\n\n" +
                        "See the log (Advanced) for details. The install was left as-is; " +
                        "fix the problem and press Play again.");
                    return false;
            }
        }

        // Locate the operator's on-disk enb-patch.exe for a local launch. The repo
        // keeps it (gitignored, ~200MB) at deploy/do/patches/enb-patch.exe; walk up
        // from the launcher's run dir to find the repo and that file. Returns null
        // if no such file is found (-> the caller shows the missing-patch warning).
        static string FindLocalEnbPatch()
        {
            try
            {
                for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
                {
                    string cand = Path.Combine(dir.FullName, "deploy", "do", "patches", "enb-patch.exe");
                    if (File.Exists(cand)) return cand;
                }
            }
            catch { /* best-effort: treat as not found */ }
            return null;
        }
#endif

        void OnCheckClick(object sender, RoutedEventArgs e)
        {
            if (!TryGetSelectedHost(out _, out var host)) return;
            int gen = ++_statusProbeGen;
            c_ServerStatus.Text = "CHECKING";
            GatePlay(false);
            _ = KickServerProbe(NormalizeHost(host.Hostname), GetProbePort(host), gen);
        }

        // Open the Freya Online website for the currently-selected server. The
        // website is served by the freya-online service, co-located with the
        // server: on play-local it is the plain-HTTP dev endpoint (host 8088 ->
        // container 8080); on a live host freya-online terminates TLS on :443,
        // so the site is https://<host>.
        void OnWebsiteClick(object sender, RoutedEventArgs e)
        {
            OpenUrl(WebsiteUrlFor(c_ComboBox_Servers.Text ?? ""));
        }

        // Map a server hostname/URL to its website URL. Empty or loopback ->
        // the play-local dev site; anything else -> https on the same host.
        static string WebsiteUrlFor(string rawServer)
        {
            var host = NormalizeHost(rawServer);
            int colon = host.IndexOf(':');          // drop a typed-in :port
            if (colon >= 0) host = host.Substring(0, colon);

            if (string.IsNullOrEmpty(host) ||
                host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
                host == "127.0.0.1")
            {
                return "http://localhost:8088";
            }
            return "https://" + host;
        }

        static void OpenUrl(string url)
        {
            // Fully-qualified: the `using System.Diagnostics` above is gated
            // behind #if CHECK_FOR_UPDATES, so don't depend on it here.
            try
            {
                System.Diagnostics.Process.Start(
                    new System.Diagnostics.ProcessStartInfo { FileName = url, UseShellExecute = true });
            }
            catch
            {
                // UseShellExecute=true may not resolve a handler on some Linux
                // setups; fall back to xdg-open directly.
                try { System.Diagnostics.Process.Start("xdg-open", url); } catch { /* nothing else to try */ }
            }
        }

        async void OnBrowseClient(object sender, RoutedEventArgs e)
        {
            var top = TopLevel.GetTopLevel(this);
            if (top == null) return;
            var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Select Earth & Beyond Client",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("Earth & Beyond Client") { Patterns = new[] { "client.exe" } },
                    new FilePickerFileType("All Files")              { Patterns = new[] { "*" } },
                },
            });
            if (files.Count == 0) return;
            var path = files[0].Path.LocalPath;
            c_TextBox_Client.Text = path;
            _user.ClientPath = path;
            _user.Save();
            _setting.ClientPath = path;
        }

        async void OnPlayClick(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(c_TextBox_Client.Text))
            {
                await Info("Locate the client.exe first using Browse.");
                return;
            }
            _setting.ClientPath = c_TextBox_Client.Text;

            if (!int.TryParse(c_TextBox_Port.Text, out int port) || port < 1 || port > ushort.MaxValue)
            {
                await Warn("Enter a valid authentication port (1–65535).");
                c_TextBox_Port.Focus();
                return;
            }

            if (!TryGetSelectedHost(out var emu, out var host))
            {
                await Warn("Select an emulator and a server first.");
                return;
            }

            _setting.AuthenticationPort = port;
            // Normalize away any scheme/path the user typed (https://host -> host)
            // so the raw DNS/TCP paths in Launcher resolve it.
            _setting.Hostname           = NormalizeHost(host.Hostname);
            _setting.LaunchName         = emu.GetLaunchName();
            _setting.EnablePositionFeed = _user.UsePositionFeed;   // PB-2
            _user.UseClientMods         = c_CheckBox_LuaMods.IsChecked == true;
            _setting.EnableClientMods   = _user.UseClientMods;     // enbmod Lua mods
            _setting.ModStates          = _user.ModStates;         // per-mod enable/disable

            // Persist (keep the raw typed value so the box redisplays it verbatim)
            _user.AuthenticationPort = c_TextBox_Port.Text;
            _user.LastEmulatorName   = emu.Name;
            _user.LastServerName     = host.Hostname;
            _user.Save();

#if !CHECK_FOR_UPDATES
            // play-local (dev) builds have no online /updateCheck, so the
            // server-driven patch reconcile never runs. Still make sure the EnB
            // install is patched to retail: apply the repo's on-disk enb-patch.exe
            // if present, or block launch with a clear warning if it is missing.
            if (!await EnsureLocalPatchAsync())
                return;
#endif

            // Relaunch support: the launcher cannot reliably observe the client's
            // exit (in the PB-2 injector mode FreyaInject.exe resumes client.exe
            // and immediately returns, and under WINE the `wine` wrapper we start
            // exits with it -- so the process we Start is never the client). We
            // therefore do NOT lock Play after a launch. Instead Play is
            // idempotent: each click first tears down the PREVIOUS session's auth
            // relay (freeing its loopback port) AND the still-running FreyaProxy
            // (which otherwise keeps the loopback listen ports bound) so a second
            // launch is clean rather than colliding with the first. This fixes
            // "can't relaunch without closing the launcher entirely" without
            // fragile PID tracking.
            if (_activeLauncher != null)
            {
                AppendLog("Relaunch: closing the previous proxy + session before launching again.");
                _activeLauncher.Dispose();
                _activeLauncher = null;
            }

            try
            {
                var launcher = new Launcher(_setting, AppendLog);
                launcher.Launch();
                _activeLauncher = launcher;
                _clientRunning  = true;   // pause the periodic status re-probe
                c_Status.Text   = "Client running. Press Play to relaunch, or Quit to tear everything down.";
            }
            catch (Exception ex)
            {
                AppendLog("Launch failed: " + ex);
                await Err("Error launching client: " + ex.Message);
            }
        }

        // How many lines of the tail of each log to show. Refresh re-reads the
        // file and re-renders the last LogTail lines (it does NOT accumulate).
        const int LogTail = 500;

        // One Advanced-viewer tab: a named source and the read-only box that
        // renders the tail of it. ReadAll returns the FULL log text (or null when
        // the source does not exist yet); Render tails it to the last LogTail.
        sealed class LogTab
        {
            public string         Header;
            public Func<string>   ReadAll;
            public string         EmptyMsg;
            public TextBox        Box;
        }

        async void OnAdvancedClick(object sender, RoutedEventArgs e)
        {
            // Where the spawned children write. The proxy runs with WorkingDir =
            // <launcher>/bin and logs there as _YYYY_MM_DD.log; enbmod writes
            // enbmod.log next to the client exe (where the DLL is staged).
            string proxyDir  = Path.Combine(AppContext.BaseDirectory, "bin");
            string clientDir = string.IsNullOrEmpty(_setting.ClientPath)
                ? null : Path.GetDirectoryName(_setting.ClientPath);

            var tabs = new[]
            {
                new LogTab
                {
                    Header   = "Launcher",
                    // The launcher's own log is the in-memory pane (config load,
                    // server probe, patch decisions, spawn output) -- not a file.
                    ReadAll  = () => c_LogPane.Text,
                    EmptyMsg = "(no launcher log output yet)",
                },
                new LogTab
                {
                    Header   = "E&B",
                    ReadAll  = ReadLogFile(() => FindClientLog(clientDir)),
                    EmptyMsg = "(no Earth & Beyond client log found)",
                },
                new LogTab
                {
                    Header   = "Proxy",
                    ReadAll  = ReadLogFile(() => NewestLogFile(proxyDir, "*.log")),
                    EmptyMsg = "(no proxy log yet -- launch the game first)",
                },
                new LogTab
                {
                    Header   = "Mods",
                    ReadAll  = ReadLogFile(() => clientDir == null
                        ? null : ExistingFile(Path.Combine(clientDir, "enbmod.log"))),
                    EmptyMsg = "(no enbmod log -- enable Client Mods and launch)",
                },
            };

            var tabControl = new TabControl { Margin = new Avalonia.Thickness(0) };
            foreach (var t in tabs)
            {
                t.Box = new TextBox
                {
                    IsReadOnly    = true,
                    AcceptsReturn = true,
                    TextWrapping  = Avalonia.Media.TextWrapping.NoWrap,
                    FontFamily    = "Cascadia Mono,Consolas,monospace",
                    Margin        = new Avalonia.Thickness(0, 8, 0, 0),
                };
                tabControl.Items.Add(new TabItem { Header = t.Header, Content = t.Box });
            }

            var refresh = new Button
            {
                Content             = "↻ Refresh",   // ↻
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            };
            refresh.Click += (_, __) =>
            {
                int i = tabControl.SelectedIndex;
                if (i < 0 || i >= tabs.Length) return;
                RenderLogTab(tabs[i]);   // re-read the file, show the last LogTail lines
            };

            tabControl.SelectionChanged += (_, __) =>
            {
                int i = tabControl.SelectedIndex;
                if (i >= 0 && i < tabs.Length) RenderLogTab(tabs[i]);
            };

            var root = new DockPanel { Margin = new Avalonia.Thickness(8) };
            DockPanel.SetDock(refresh, Dock.Top);
            root.Children.Add(refresh);
            root.Children.Add(tabControl);   // fills the rest

            var dlg = new Window
            {
                Title   = "Freya - Advanced Logs",
                Width   = 820,
                Height  = 560,
                Content = root,
            };

            tabControl.SelectedIndex = 0;
            RenderLogTab(tabs[0]);   // SelectionChanged does not fire for the initial index

            await dlg.ShowDialog(this);
        }

        // "Configure Mods..." -- a scrollable popup listing every enbmod mod found
        // under the source scripts/mods/ tree. Each row is an enable/disable
        // checkbox + the mod name + its author, with the mod description as a hover
        // tooltip. Toggling a row records the state in _user.ModStates; a disabled
        // mod is not staged next to the client and so is never loaded or injected.
        async void OnConfigureModsClick(object sender, RoutedEventArgs e)
        {
            var mods = ModCatalog.Scan();

            var list = new StackPanel { Spacing = 6, Margin = new Avalonia.Thickness(0) };
            if (mods.Count == 0)
            {
                list.Children.Add(new TextBlock
                {
                    Text = "No mods found. Build them with `just build-enbmod`.",
                    Foreground = StatusDimBrush,
                    Margin = new Avalonia.Thickness(4),
                });
            }

            // Row card backgrounds (faint white tint; brighter on hover).
            var rowBg      = new SolidColorBrush(Color.Parse("#0FFFFFFF"));
            var rowBgHover = new SolidColorBrush(Color.Parse("#1FFFFFFF"));
            var rowBorderClr = new SolidColorBrush(Color.Parse("#1AFFFFFF"));
            var badgeBg    = new SolidColorBrush(Color.Parse("#33EC5C50"));   // ~20% red

            // Per-row widgets we restyle when dependency state changes.
            var rowNames = new List<(ModInfo Mod, TextBlock Name, Border Badge, TextBlock BadgeText)>();

            void RefreshDepStatus()
            {
                foreach (var (mod, name, badge, badgeText) in rowNames)
                {
                    var unmet = ModCatalog.UnmetDependencies(_user.ModStates, mods, mod);
                    if (unmet.Count > 0)
                    {
                        name.Foreground = StatusDangerBrush;
                        badgeText.Text = "requires " + string.Join(", ", unmet);
                        badge.IsVisible = true;
                    }
                    else
                    {
                        // Clear the LOCAL value so the theme's foreground setter wins
                        // again. Setting it to null instead would paint the text with a
                        // null brush -> invisible.
                        name.ClearValue(TextBlock.ForegroundProperty);
                        badge.IsVisible = false;
                    }
                }
            }

            foreach (var m in mods)
            {
                var id = m.Id;
                var cb = new CheckBox
                {
                    IsChecked = ModCatalog.IsEnabled(_user.ModStates, id),
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                    Margin = new Avalonia.Thickness(0, 0, 12, 0),
                };
                // Toggling ANY mod can change another mod's dependency status, so
                // re-evaluate every row after each change, not just this one.
                cb.IsCheckedChanged += (_, __) =>
                {
                    _user.ModStates[id] = cb.IsChecked == true;
                    RefreshDepStatus();
                };

                // Two-line text block: name on top, "by Author" beneath. Authors
                // sit under their names, so they are all left-aligned -- no fragile
                // column alignment, and it reads as a clean list.
                var nameText = new TextBlock
                {
                    Text = m.Name,
                    FontWeight = FontWeight.SemiBold,
                    FontSize = 14,
                };
                var authorText = new TextBlock
                {
                    Text = "by " + m.Author,
                    Foreground = StatusDimBrush,
                    FontSize = 11,
                    Margin = new Avalonia.Thickness(0, 1, 0, 0),
                };
                var textStack = new StackPanel
                {
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                };
                textStack.Children.Add(nameText);
                textStack.Children.Add(authorText);

                // Red pill, right-aligned, shown only when a dependency is unmet.
                var badgeText = new TextBlock
                {
                    Foreground = StatusDangerBrush,
                    FontSize = 11,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                };
                var badge = new Border
                {
                    Child = badgeText,
                    Background = badgeBg,
                    BorderBrush = StatusDangerBrush,
                    BorderThickness = new Avalonia.Thickness(1),
                    CornerRadius = new Avalonia.CornerRadius(10),
                    Padding = new Avalonia.Thickness(9, 2, 9, 3),
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                    Margin = new Avalonia.Thickness(12, 0, 0, 0),
                    IsVisible = false,
                };
                rowNames.Add((m, nameText, badge, badgeText));

                // Grid columns: checkbox | text (fills) | status pill (right).
                var row = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions
                    {
                        new ColumnDefinition(GridLength.Auto),
                        new ColumnDefinition(GridLength.Star),
                        new ColumnDefinition(GridLength.Auto),
                    },
                };
                Grid.SetColumn(cb, 0);
                Grid.SetColumn(textStack, 1);
                Grid.SetColumn(badge, 2);
                row.Children.Add(cb);
                row.Children.Add(textStack);
                row.Children.Add(badge);

                // Rounded card per row; brightens on hover. Carries the description
                // tooltip and makes the whole row hit-testable.
                var rowBorder = new Border
                {
                    Child = row,
                    Background = rowBg,
                    BorderBrush = rowBorderClr,
                    BorderThickness = new Avalonia.Thickness(1),
                    CornerRadius = new Avalonia.CornerRadius(6),
                    Padding = new Avalonia.Thickness(12, 9, 12, 9),
                };
                rowBorder.PointerEntered += (_, __) => rowBorder.Background = rowBgHover;
                rowBorder.PointerExited  += (_, __) => rowBorder.Background = rowBg;
                if (!string.IsNullOrEmpty(m.Description))
                    ToolTip.SetTip(rowBorder, m.Description);
                list.Children.Add(rowBorder);
            }

            RefreshDepStatus();   // initial paint reflects the persisted enable states

            var scroller = new ScrollViewer
            {
                Content = list,
                HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            };

            var closeBtn = new Button
            {
                Content = "Close",
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                Margin = new Avalonia.Thickness(0, 8, 0, 0),
            };

            var root = new DockPanel { Margin = new Avalonia.Thickness(10) };
            var header = new TextBlock
            {
                Text = "Enable or disable individual Lua mods. Hover a row for its description.",
                Foreground = new SolidColorBrush(Colors.Gray),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Avalonia.Thickness(0, 0, 0, 8),
            };
            DockPanel.SetDock(header, Dock.Top);
            DockPanel.SetDock(closeBtn, Dock.Bottom);
            root.Children.Add(header);
            root.Children.Add(closeBtn);
            root.Children.Add(scroller);   // fills the rest

            var dlg = new Window
            {
                Title = "Configure Mods",
                Width = 480,
                Height = 420,
                Content = root,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
            };
            closeBtn.Click += (_, __) => dlg.Close();

            await dlg.ShowDialog(this);

            // Persist the choices so they survive across launches.
            _user.Save();
        }

        // Render a tab: read its source, keep only the last `Lines` lines, and
        // always pin the scrollbar to the bottom (newest output).
        void RenderLogTab(LogTab t)
        {
            string full;
            try { full = t.ReadAll(); }
            catch (Exception ex) { full = "(error reading log: " + ex.Message + ")"; }

            if (string.IsNullOrEmpty(full))
            {
                t.Box.Text = t.EmptyMsg;
                ScrollBoxToEnd(t.Box);
                return;
            }

            var lines = full.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            var tail  = lines.Length > LogTail
                ? lines.Skip(lines.Length - LogTail)
                : lines;
            t.Box.Text = string.Join("\n", tail);
            ScrollBoxToEnd(t.Box);
        }

        // Pin a TextBox's scrollbar to the bottom. The inner ScrollViewer is not
        // realized until after a layout pass, so we also retry at Loaded priority.
        void ScrollBoxToEnd(TextBox box)
        {
            void Pin()
            {
                box.CaretIndex = box.Text?.Length ?? 0;
                box.FindDescendantOfType<ScrollViewer>()?.ScrollToEnd();
            }
            Pin();
            Dispatcher.UIThread.Post(Pin, DispatcherPriority.Loaded);
        }

        // Wrap a path resolver into a full-text reader. Opens with FileShare
        // ReadWrite because the proxy / enbmod hold the file open for appending.
        static Func<string> ReadLogFile(Func<string> resolve) => () =>
        {
            string path;
            try { path = resolve(); } catch { path = null; }
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            try
            {
                using var fs = new FileStream(
                    path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var sr = new StreamReader(fs);
                return sr.ReadToEnd();
            }
            catch (Exception ex)
            {
                return "(could not read " + Path.GetFileName(path) + ": " + ex.Message + ")";
            }
        };

        static string ExistingFile(string path)
            => File.Exists(path) ? path : null;

        // Newest file matching `pattern` in `dir` by last-write time. The proxy's
        // daily log is _YYYY_MM_DD.log, so "most recent" == newest mtime.
        static string NewestLogFile(string dir, string pattern)
        {
            try
            {
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return null;
                return new DirectoryInfo(dir).GetFiles(pattern)
                    .OrderByDescending(f => f.LastWriteTimeUtc)
                    .Select(f => f.FullName)
                    .FirstOrDefault();
            }
            catch { return null; }
        }

        // The EnB client has no standard text log; probe the names it might use
        // and report none rather than mis-grabbing an unrelated *.log (enbmod's
        // log lives in the same folder).
        static string FindClientLog(string clientDir)
        {
            if (string.IsNullOrEmpty(clientDir)) return null;
            foreach (var name in new[] { "client.log", "Net7.log", "clientlog.txt", "eb.log" })
            {
                var p = Path.Combine(clientDir, name);
                if (File.Exists(p)) return p;
            }
            return null;
        }

        void OnCancelClick(object sender, RoutedEventArgs e) => Close();

        // ---- helpers ----

        void AppendLog(string s)
        {
            void Append()
            {
                var sb = new StringBuilder(c_LogPane.Text ?? "");
                if (sb.Length > 0) sb.AppendLine();
                sb.Append(s);
                c_LogPane.Text = sb.ToString();
            }
            if (Dispatcher.UIThread.CheckAccess()) Append();
            else Dispatcher.UIThread.Post(Append);
        }

        Task Info(string m) =>
            MessageBoxManager.GetMessageBoxStandard("Freya - Information", m, ButtonEnum.Ok, MsBoxIcon.Info)
                             .ShowWindowDialogAsync(this);
        Task Warn(string m) =>
            MessageBoxManager.GetMessageBoxStandard("Freya - Warning", m, ButtonEnum.Ok, MsBoxIcon.Warning)
                             .ShowWindowDialogAsync(this);
        Task Err(string m) =>
            MessageBoxManager.GetMessageBoxStandard("Freya - Error", m, ButtonEnum.Ok, MsBoxIcon.Error)
                             .ShowWindowDialogAsync(this);
    }
}
