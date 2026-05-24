using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;

namespace ToolsLauncherAvalonia
{
    // Avalonia port of tools/toolslauncher/GUI/ToolsLauncher.cs. The
    // original was a 250×345 launch-pad with 6 large editor buttons + a
    // Launch Net7 button + a Check For Updates button + tray icon + FTP
    // menu + IRC messenger menu.
    //
    // What this port drops vs. the original:
    //
    //  - **IRC messenger** (IRCMessenger.cs / PrivateMessage.cs /
    //    Login.cs / Meebey.SmartIrc4Net) — pointed at
    //    eservices.dyndns.org:6667 channel #test. dyndns.org's free
    //    service shut down in 2014; the channel name "#test" is a smell
    //    that this was a placeholder. Dead.
    //
    //  - **FTP browser** (FtpWindow.cs using System.Windows.Forms.Web
    //    Browser) — hardcoded credentials for net-7.org FTP, which is
    //    dead. Avalonia has no WebBrowser control anyway.
    //
    //  - **Updater** (Updateing/Updater.cs + FormUpdate.cs +
    //    ExeUpdater.exe resource) — pointed at toolspatch.net-7.org,
    //    which is the sibling of the dead patch.net-7.org we already
    //    dropped from launchnet7-avalonia (Tier 5). Same vintage,
    //    same fate.
    //
    //  - **System tray icon** — Avalonia has TrayIcon support but it
    //    requires a libnotify-style daemon on Linux and is finicky in
    //    headless mode (smoke would have to special-case it). Skipped
    //    for the first port; can be added by setting `TrayIcon.Icons`
    //    in App.OnFrameworkInitializationCompleted.
    //
    // What this port adds:
    //
    //  - Editor buttons spawn the Avalonia sibling projects via
    //    `dotnet run --project ../<name>-avalonia/` (EditorLauncher.cs)
    //    instead of launching Windows .exe files in the working dir.
    //  - Settings persisted as JSON in
    //    %APPDATA%/Net7Tools/toolslauncher-avalonia.json instead of
    //    the WinForms user.config file.
    public partial class MainWindow : Window
    {
        readonly Settings _settings = Settings.Load();

        // (display name, avalonia project name, "(not yet ported)" stub)
        readonly List<(string Label, string Project, bool Ported)> _editors = new()
        {
            ("Effect Editor",   "effect-editor-avalonia",       true),
            ("Item Editor",     "item-editor-avalonia",         false),
            ("Mission Editor",  "missioneditor-avalonia",       false),
            ("Mob Editor",      "mob-editor-avalonia",          true),
            ("Sector Editor",   "sector-editor-avalonia",       false),
            ("Station Tools",   "station-tools-avalonia",       false),
            ("Faction Editor",  "faction-editor-avalonia",      true),
            ("Talk Tree",       "talktreeeditor-avalonia",      true),
        };

        public MainWindow()
        {
            InitializeComponent();

            foreach (var (label, project, ported) in _editors)
            {
                var btn = new Button
                {
                    Content             = ported ? label : label + "  (not yet ported)",
                    Tag                 = project,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                    IsEnabled           = ported,
                };
                btn.Click += OnEditorButton;
                c_EditorStack.Children.Add(btn);
            }
        }

        void OnEditorButton(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not string project) return;
            var (ok, detail) = EditorLauncher.Launch(project, _settings);
            c_Status.Text = ok ? $"started {project}" : $"failed: {detail}";
        }

        void OnLaunchNet7(object sender, RoutedEventArgs e)
        {
            var (ok, detail) = EditorLauncher.LaunchNet7(_settings);
            c_Status.Text = ok ? $"started launchnet7 ({detail})" : $"failed: {detail}";
        }

        async void OnSettings(object sender, RoutedEventArgs e)
        {
            var dlg = new SettingsWindow(_settings);
            await dlg.ShowDialog(this);
        }

        void OnQuit(object sender, RoutedEventArgs e)
        {
            if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                desktop.Shutdown();
            else
                Close();
        }
    }
}
