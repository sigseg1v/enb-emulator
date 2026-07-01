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
        UserSettings _user;
        LauncherConfig _config;
        // Guards the profile combo while we (re)populate it / swap profiles, so
        // the SelectionChanged handler does not fire a spurious profile switch or
        // re-entrantly save during setup.
        bool _loadingProfile;
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
        static readonly IBrush StatusOkBrush = new SolidColorBrush(Color.Parse("#FF5FD37F"));
        static readonly IBrush StatusDangerBrush = new SolidColorBrush(Color.Parse("#FFEC5C50"));
        static readonly IBrush StatusWarmBrush = new SolidColorBrush(Color.Parse("#FFEFB05C"));
        static readonly IBrush StatusDimBrush = new SolidColorBrush(Color.Parse("#FFA2ACB6"));

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

            // Window position is launcher-global (not per profile).
            if (_user.FormMainPositionX > 0 && _user.FormMainPositionY > 0)
                Position = new Avalonia.PixelPoint(_user.FormMainPositionX, _user.FormMainPositionY);

            FillResolutions();   // static system-resolution list (BA-4)
            FillProfiles();      // populate the profile dropdown + select the active one (BA-3)
            ApplyUserToUi();     // push the active profile's values into every control
            c_Status.Text = "Please select a server and hit Play.";

            _statusTimer = new DispatcherTimer { Interval = StatusRefreshInterval };
            _statusTimer.Tick += OnStatusTimerTick;
            _statusTimer.Start();

            MaybeAutoPlay();
        }

        // Automation hook: when FREYA_AUTOPLAY=1 is set in the environment, click
        // Play automatically once the selected server reports ONLINE/READY. This
        // lets the login-automation skill launch the client WITHOUT driving the
        // mouse onto the Play button -- the desktop may have another app holding a
        // pointer grab (a VM / fullscreen game recentering the cursor), which makes
        // a synthetic XTEST click on the launcher unreliable. No-op unless set.
        async void MaybeAutoPlay()
        {
            if (Environment.GetEnvironmentVariable("FREYA_AUTOPLAY") != "1")
                return;
            AppendLog("FREYA_AUTOPLAY=1: will click Play once the server is ONLINE.");
            // Wait up to ~30s for the status probe to confirm the server so we
            // never launch against a not-yet-ready stack. On the dev build Play is
            // never gated, but we still wait for the liveness label to flip.
            for (int i = 0; i < 60; i++)
            {
                var st = (c_ServerStatus?.Text ?? "").ToUpperInvariant();
                if (st.Contains("ONLINE") || st.Contains("READY")) break;
                await Task.Delay(500);
            }
            AppendLog("FREYA_AUTOPLAY: server status=" + (c_ServerStatus?.Text ?? "") + " -- clicking Play.");
            OnPlayClick(this, new RoutedEventArgs());
        }

        void OnClosing(object sender, EventArgs e)
        {
            _statusTimer?.Stop();
            CaptureUiToUser();   // autosave the active profile's editable fields
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
            _ = KickServerProbe(HostResolver.NormalizeHost(host.Hostname), GetProbePort(host), gen, auto: true);
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

        // ---- profiles (BA-3) ----

        // Populate the profile dropdown and select the active profile. Guarded so
        // the SelectionChanged handler does not treat this fill as a user switch.
        void FillProfiles()
        {
            _loadingProfile = true;
            try
            {
                c_ComboBox_Profiles.Items.Clear();
                foreach (var name in UserSettings.ListProfiles())
                    c_ComboBox_Profiles.Items.Add(name);
                c_ComboBox_Profiles.SelectedItem = _user.Name;
                if (c_ComboBox_Profiles.SelectedItem == null && c_ComboBox_Profiles.Items.Count > 0)
                    c_ComboBox_Profiles.SelectedIndex = 0;
            }
            finally { _loadingProfile = false; }
            c_Button_DelProfile.IsEnabled = !_user.IsDefaultProfile;
        }

        // User picked a different profile: persist the current one's edits, load the
        // chosen profile, and refresh every control from it.
        void OnProfileChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loadingProfile) return;
            var name = c_ComboBox_Profiles.SelectedItem as string;
            if (string.IsNullOrEmpty(name) ||
                string.Equals(name, _user.Name, StringComparison.OrdinalIgnoreCase))
                return;

            CaptureUiToUser();
            _user.Save();                       // keep the profile we are leaving

            _user = UserSettings.LoadProfile(name);
            _user.SetActive();                  // remember it for next launch
            ApplyUserToUi();
            c_Button_DelProfile.IsEnabled = !_user.IsDefaultProfile;
        }

        async void OnAddProfileClick(object sender, RoutedEventArgs e)
        {
            var name = await PromptForText("New Profile", "Name for the new profile:");
            if (string.IsNullOrWhiteSpace(name)) return;
            name = name.Trim();
            if (string.Equals(name, UserSettings.DefaultProfileName, StringComparison.OrdinalIgnoreCase))
            {
                await Warn("\"Default\" is reserved. Choose another name.");
                return;
            }
            if (UserSettings.ListProfiles().Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                await Warn($"A profile named \"{name}\" already exists.");
                return;
            }

            // Capture the current UI so the new profile is a copy of what is shown.
            CaptureUiToUser();
            var created = _user.CreateProfile(name);
            if (created == null) { await Warn("Could not create that profile."); return; }

            _user = created;
            FillProfiles();      // includes the new one; selects _user.Name
            ApplyUserToUi();
        }

        async void OnDeleteProfileClick(object sender, RoutedEventArgs e)
        {
            if (_user.IsDefaultProfile)
            {
                await Warn("The Default profile cannot be deleted.");
                return;
            }
            var which = _user.Name;
            var confirm = await MessageBoxManager.GetMessageBoxStandard(
                "Freya - Delete profile",
                $"Delete the profile \"{which}\"? This cannot be undone.",
                ButtonEnum.YesNo, MsBoxIcon.Warning).ShowWindowDialogAsync(this);
            if (confirm != ButtonResult.Yes) return;

            UserSettings.DeleteProfile(which);
            _user = UserSettings.LoadProfile(UserSettings.DefaultProfileName);
            _user.SetActive();
            FillProfiles();
            ApplyUserToUi();
        }

        // Minimal modal text-input dialog (MsBox.Avalonia 3.x has no input box).
        async Task<string> PromptForText(string title, string prompt)
        {
            var box = new TextBox { Width = 280 };
            var ok = new Button { Content = "OK", IsDefault = true };
            var cancel = new Button { Content = "Cancel", IsCancel = true };
            string result = null;

            var buttons = new StackPanel
            {
                Orientation = Avalonia.Layout.Orientation.Horizontal,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                Spacing = 8,
                Margin = new Avalonia.Thickness(0, 12, 0, 0),
            };
            buttons.Children.Add(cancel);
            buttons.Children.Add(ok);

            var panel = new StackPanel { Margin = new Avalonia.Thickness(14), Spacing = 8 };
            panel.Children.Add(new TextBlock { Text = prompt });
            panel.Children.Add(box);
            panel.Children.Add(buttons);

            var dlg = new Window
            {
                Title = title,
                Content = panel,
                SizeToContent = SizeToContent.WidthAndHeight,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                CanResize = false,
            };
            ok.Click += (_, __) => { result = box.Text; dlg.Close(); };
            cancel.Click += (_, __) => { result = null; dlg.Close(); };
            await dlg.ShowDialog(this);
            return result;
        }

        // ---- profile <-> UI marshalling ----

        // Push the active profile's stored values into every control. Re-fills the
        // emulator/server selection and client path (those read _user too).
        void ApplyUserToUi()
        {
            c_TextBox_Port.Text = _user.AuthenticationPort ?? "";
            c_CheckBox_LuaMods.IsChecked = _user.UseClientMods;
            c_TextBox_Username.Text = _user.AccountName ?? "";
            c_TextBox_Character.Text = _user.CharacterName ?? "";
            c_TextBox_Password.Text = "";          // never restored from disk
            c_CheckBox_Fullscreen.IsChecked = _user.Fullscreen;
            c_CheckBox_LockMouse.IsChecked = _user.LockMouseToWindow;
            c_TextBox_WindowX.Text = _user.WindowX?.ToString() ?? "";
            c_TextBox_WindowY.Text = _user.WindowY?.ToString() ?? "";
            SelectResolution(_user.Resolution);

            FillEmulators();   // reads _user.LastEmulatorName -> FillHosts reads LastServerName
            FillClientPath();  // reads _user.ClientPath
        }

        // Read every editable control back into _user (everything except the
        // password, which is never persisted).
        void CaptureUiToUser()
        {
            _user.AuthenticationPort = c_TextBox_Port.Text ?? "";
            _user.UseClientMods = c_CheckBox_LuaMods.IsChecked == true;
            _user.AccountName = (c_TextBox_Username.Text ?? "").Trim();
            _user.CharacterName = (c_TextBox_Character.Text ?? "").Trim();
            _user.Resolution = SelectedResolution();
            _user.Fullscreen = c_CheckBox_Fullscreen.IsChecked == true;
            _user.LockMouseToWindow = c_CheckBox_LockMouse.IsChecked == true;
            _user.WindowX = ParseOptionalInt(c_TextBox_WindowX.Text);
            _user.WindowY = ParseOptionalInt(c_TextBox_WindowY.Text);
            if (!string.IsNullOrEmpty(c_TextBox_Client.Text))
                _user.ClientPath = c_TextBox_Client.Text;
            if (CurrentEmulator != null) _user.LastEmulatorName = CurrentEmulator.Name;
            var srv = (c_ComboBox_Servers.Text ?? "").Trim();
            if (!string.IsNullOrEmpty(srv)) _user.LastServerName = srv;
        }

        // Parse an optional integer text box: blank / whitespace / unparseable -> null
        // (meaning "unset"), so the caller leaves the corresponding behaviour untouched.
        static int? ParseOptionalInt(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            return int.TryParse(s.Trim(), out int v) ? v : (int?)null;
        }

        // ---- resolutions (BA-4) ----

        void FillResolutions()
        {
            c_ComboBox_Resolution.Items.Clear();
            foreach (var r in EnumerateResolutions())
                c_ComboBox_Resolution.Items.Add(r);
        }

        // Distinct "WxH" options: the EnB-friendly baselines plus whatever the
        // system reports (xrandr on Linux/WINE), sorted largest-first.
        static List<string> EnumerateResolutions()
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "2560x1440", "1920x1200", "1920x1080", "1600x1200",
                "1680x1050", "1440x900", "1280x1024", "1280x960", "1024x768", "800x600",
            };
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "xrandr",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                using var p = System.Diagnostics.Process.Start(psi);
                if (p != null)
                {
                    string outp = p.StandardOutput.ReadToEnd();
                    p.WaitForExit(3000);
                    foreach (System.Text.RegularExpressions.Match m in
                        System.Text.RegularExpressions.Regex.Matches(outp, @"(\d{3,5})x(\d{3,5})"))
                        set.Add($"{m.Groups[1].Value}x{m.Groups[2].Value}");
                }
            }
            catch { /* no xrandr (e.g. native Windows) -> baselines only */ }

            return set.OrderByDescending(s =>
            {
                int x = s.IndexOf('x');
                long w = long.TryParse(s.Substring(0, x), out var ww) ? ww : 0;
                long h = long.TryParse(s.Substring(x + 1), out var hh) ? hh : 0;
                return w * h;
            }).ToList();
        }

        void SelectResolution(string res)
        {
            if (string.IsNullOrWhiteSpace(res)) { c_ComboBox_Resolution.SelectedIndex = -1; return; }
            if (!c_ComboBox_Resolution.Items.Contains(res))
                c_ComboBox_Resolution.Items.Add(res);
            c_ComboBox_Resolution.SelectedItem = res;
        }

        string SelectedResolution() => c_ComboBox_Resolution.SelectedItem as string ?? "";

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
                _ = KickServerProbe(HostResolver.NormalizeHost(host.Hostname), GetProbePort(host), gen);
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
            _ = KickServerProbe(HostResolver.NormalizeHost(host.Hostname), GetProbePort(host), gen);
        }

        // Open the Freya Online website for the currently-selected server. The
        // website is served by the freya-online service, co-located with the
        // server: on play-local it is the plain-HTTP dev endpoint (host 8088 ->
        // container 8080); on a live host freya-online terminates TLS on :443,
        // so the site is https://<host>.
        void OnWebsiteClick(object sender, RoutedEventArgs e)
        {
            OpenUrl(HostResolver.WebsiteUrlFor(c_ComboBox_Servers.Text ?? ""));
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
            _setting.Hostname = HostResolver.NormalizeHost(host.Hostname);
            _setting.LaunchName = emu.GetLaunchName();
            _setting.EnablePositionFeed = _user.UsePositionFeed;   // PB-2
            _user.UseClientMods = c_CheckBox_LuaMods.IsChecked == true;
            _setting.EnableClientMods = _user.UseClientMods;     // enbmod Lua mods
            _setting.ModStates = _user.ModStates;         // per-mod enable/disable

            // BA-4: display settings, applied to the EnB registry before launch.
            _setting.Resolution = SelectedResolution();
            _setting.Fullscreen = c_CheckBox_Fullscreen.IsChecked == true;
            _setting.LockMouseToWindow = c_CheckBox_LockMouse.IsChecked == true;

            // Optional client-window placement: only reposition when BOTH X and Y are
            // given (a half-set position is ambiguous, so we ignore it and leave the
            // window where the game opens it).
            int? winX = ParseOptionalInt(c_TextBox_WindowX.Text);
            int? winY = ParseOptionalInt(c_TextBox_WindowY.Text);
            _setting.WindowX = winX;
            _setting.WindowY = winY;

            // BA-5: optional auto-login. Push the typed credentials into the env the
            // client (enbmod's autologin.cpp) reads; clear each when blank so a prior
            // launch's value never leaks into a later plain launch. The password is
            // passed to the env for this launch but is NEVER written to disk.
            string accName = (c_TextBox_Username.Text ?? "").Trim();
            string accPass = c_TextBox_Password.Text ?? "";
            string charName = (c_TextBox_Character.Text ?? "").Trim();
            Environment.SetEnvironmentVariable("FREYA_ACC_NAME", accName.Length > 0 ? accName : null);
            Environment.SetEnvironmentVariable("FREYA_ACC_PASS", accPass.Length > 0 ? accPass : null);
            Environment.SetEnvironmentVariable("FREYA_CHARACTER", charName.Length > 0 ? charName : null);
            // A supplied username means the user wants a hands-free login, which only
            // proceeds once the EULA gate is cleared; assert it only when credentials
            // are actually present so a plain launch still shows the EULA.
            if (accName.Length > 0)
                Environment.SetEnvironmentVariable("FREYA_EULA", "ACCEPT");

            // Persist (keep the raw typed value so the box redisplays it verbatim).
            // BA-5 autosaves the account + character (NOT the password); BA-4
            // autosaves the resolution + fullscreen. All target the active profile.
            _user.AuthenticationPort = c_TextBox_Port.Text;
            _user.LastEmulatorName = emu.Name;
            _user.LastServerName = host.Hostname;
            _user.AccountName = accName;
            _user.CharacterName = charName;
            _user.Resolution = _setting.Resolution;
            _user.Fullscreen = _setting.Fullscreen;
            _user.LockMouseToWindow = _setting.LockMouseToWindow;
            _user.WindowX = winX;
            _user.WindowY = winY;
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
                _clientRunning = true;   // pause the periodic status re-probe
                c_Status.Text = "Client running. Press Play to relaunch, or Quit to tear everything down.";
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
            public string Header;
            public Func<string> ReadAll;
            public string EmptyMsg;
            public TextBox Box;
        }

        async void OnAdvancedClick(object sender, RoutedEventArgs e)
        {
            // Where the spawned children write. The proxy runs with WorkingDir =
            // <launcher>/bin and logs there as _YYYY_MM_DD.log; enbmod writes
            // enbmod.log next to the client exe (where the DLL is staged).
            string proxyDir = Path.Combine(AppContext.BaseDirectory, "bin");
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
                    ReadAll  = LauncherLogFiles.ReadLogFile(() => LauncherLogFiles.FindClientLog(clientDir)),
                    EmptyMsg = "(no Earth & Beyond client log found)",
                },
                new LogTab
                {
                    Header   = "Proxy",
                    ReadAll  = LauncherLogFiles.ReadLogFile(() => LauncherLogFiles.NewestLogFile(proxyDir, "*.log")),
                    EmptyMsg = "(no proxy log yet -- launch the game first)",
                },
                new LogTab
                {
                    Header   = "Mods",
                    ReadAll  = LauncherLogFiles.ReadLogFile(() => clientDir == null
                        ? null : LauncherLogFiles.ExistingFile(Path.Combine(clientDir, "enbmod.log"))),
                    EmptyMsg = "(no enbmod log -- enable Client Mods and launch)",
                },
            };

            var tabControl = new TabControl { Margin = new Avalonia.Thickness(0) };
            foreach (var t in tabs)
            {
                t.Box = new TextBox
                {
                    IsReadOnly = true,
                    AcceptsReturn = true,
                    TextWrapping = Avalonia.Media.TextWrapping.NoWrap,
                    FontFamily = "Cascadia Mono,Consolas,monospace",
                    Margin = new Avalonia.Thickness(0, 8, 0, 0),
                };
                tabControl.Items.Add(new TabItem { Header = t.Header, Content = t.Box });
            }

            // re-read + re-render whichever tab is selected (shared by the manual
            // Refresh button, the tab switch, and the auto-refresh timer).
            void RenderSelected()
            {
                int i = tabControl.SelectedIndex;
                if (i >= 0 && i < tabs.Length) RenderLogTab(tabs[i]);
            }

            var refresh = new Button { Content = "↻ Refresh" };   // ↻
            refresh.Click += (_, __) => RenderSelected();

            // Auto-refresh: default OFF. When checked, re-read the selected tab on a
            // fixed interval so a live log (enbmod.log, the proxy log) tails itself.
            var autoRefresh = new CheckBox
            {
                Content = "Auto-refresh",
                IsChecked = false,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                Margin = new Avalonia.Thickness(10, 0, 0, 0),
            };
            var autoTimer = new Avalonia.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(5),
            };
            autoTimer.Tick += (_, __) => RenderSelected();
            autoRefresh.IsCheckedChanged += (_, __) =>
            {
                if (autoRefresh.IsChecked == true) { RenderSelected(); autoTimer.Start(); }
                else autoTimer.Stop();
            };

            var controls = new StackPanel
            {
                Orientation = Avalonia.Layout.Orientation.Horizontal,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            };
            controls.Children.Add(refresh);
            controls.Children.Add(autoRefresh);

            tabControl.SelectionChanged += (_, __) => RenderSelected();

            var root = new DockPanel { Margin = new Avalonia.Thickness(8) };
            DockPanel.SetDock(controls, Dock.Top);
            root.Children.Add(controls);
            root.Children.Add(tabControl);   // fills the rest

            var dlg = new Window
            {
                Title = "Freya - Advanced Logs",
                Width = 820,
                Height = 560,
                Content = root,
            };
            // stop the timer when the dialog closes so it never ticks a dead window.
            dlg.Closed += (_, __) => autoTimer.Stop();

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
            var rowBg = new SolidColorBrush(Color.Parse("#0FFFFFFF"));
            var rowBgHover = new SolidColorBrush(Color.Parse("#1FFFFFFF"));
            var rowBorderClr = new SolidColorBrush(Color.Parse("#1AFFFFFF"));
            var badgeBg = new SolidColorBrush(Color.Parse("#33EC5C50"));   // ~20% red

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
                rowBorder.PointerExited += (_, __) => rowBorder.Background = rowBg;
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
            var tail = lines.Length > LogTail
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
