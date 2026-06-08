using System;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using LaunchFreya.Config;
#if CHECK_FOR_UPDATES
using System.Diagnostics;
using LaunchFreya.Update;
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
            _activeLauncher?.AuthRelay?.Dispose();
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
                c_ServerStatus.Text = "";
                GatePlay(false);
                return;
            }

            c_TextBox_Port.Text = host.AuthenticationPort.ToString();

            if (emu.IsSinglePlayer)
            {
                _statusProbeGen++;
                c_ServerStatus.Text = "READY";
                c_Button_Check.IsEnabled = false;
                GatePlay(true);
            }
            else
            {
                int gen = ++_statusProbeGen;
                c_ServerStatus.Text = "CHECKING";
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
                    if (gen == _statusProbeGen) c_ServerStatus.Text = text;
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
                => Dispatcher.UIThread.Post(() => { if (gen == _statusProbeGen) c_ServerStatus.Text = text; });
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
                    c_ServerStatus.Text = "UPDATE REQUIRED";
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

        void OnCheckClick(object sender, RoutedEventArgs e)
        {
            if (!TryGetSelectedHost(out _, out var host)) return;
            int gen = ++_statusProbeGen;
            c_ServerStatus.Text = "CHECKING";
            GatePlay(false);
            _ = KickServerProbe(NormalizeHost(host.Hostname), GetProbePort(host), gen);
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

            // Persist (keep the raw typed value so the box redisplays it verbatim)
            _user.AuthenticationPort = c_TextBox_Port.Text;
            _user.LastEmulatorName   = emu.Name;
            _user.LastServerName     = host.Hostname;
            _user.Save();

            try
            {
                var launcher = new Launcher(_setting, AppendLog);
                launcher.Launch();
                _activeLauncher = launcher;
                _clientRunning  = true;   // pause the periodic status re-probe
                c_Status.Text       = "Client running. Hit Quit when you're done to tear everything down.";
                c_Button_Play.IsEnabled = false;
            }
            catch (Exception ex)
            {
                AppendLog("Launch failed: " + ex);
                await Err("Error launching client: " + ex.Message);
            }
        }

        async void OnAdvancedClick(object sender, RoutedEventArgs e)
        {
            // Surface the launcher's own log (config load, server probe
            // results, patch decisions, child-process spawn output). The
            // main window keeps the log pane hidden to stay compact.
            var body = string.IsNullOrEmpty(c_LogPane.Text)
                ? "(no log output yet)"
                : c_LogPane.Text;

            var dlg = new Window
            {
                Title  = "Advanced -- launcher log",
                Width  = 720,
                Height = 480,
                Content = new TextBox
                {
                    Text           = body,
                    IsReadOnly     = true,
                    AcceptsReturn  = true,
                    TextWrapping   = Avalonia.Media.TextWrapping.NoWrap,
                    FontFamily     = "Cascadia Mono,Consolas,monospace",
                    Margin         = new Avalonia.Thickness(8),
                },
            };
            await dlg.ShowDialog(this);
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
