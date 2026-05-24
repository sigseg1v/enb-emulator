using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace ToolsLauncherAvalonia
{
    // Port of tools/toolslauncher/GUI/SettingsFrm.cs. The original used
    // FolderBrowserDialog and Properties.Settings.Default; we use
    // Avalonia's IStorageProvider for folder picking and our own
    // JSON-backed Settings.
    public partial class SettingsWindow : Window
    {
        readonly Settings _settings;

        // Parameterless ctor for the Avalonia XAML toolchain (AVLN3001).
        public SettingsWindow() : this(new Settings()) { }

        public SettingsWindow(Settings settings)
        {
            _settings = settings;
            InitializeComponent();
            c_LaunchNet7Txt.Text = _settings.LaunchNet7Path;
            c_EditorsRootTxt.Text = _settings.EditorsCheckoutRoot;
        }

        async void OnBrowseLaunchNet7(object sender, RoutedEventArgs e)
        {
            var folder = await PickFolderAsync("Select LaunchNet7 directory");
            if (folder != null) c_LaunchNet7Txt.Text = folder;
        }

        async void OnBrowseEditorsRoot(object sender, RoutedEventArgs e)
        {
            var folder = await PickFolderAsync("Select editors checkout root (the tools/ directory)");
            if (folder != null) c_EditorsRootTxt.Text = folder;
        }

        async System.Threading.Tasks.Task<string> PickFolderAsync(string title)
        {
            var picked = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                AllowMultiple = false,
                Title         = title,
            });
            if (picked == null || picked.Count == 0) return null;
            return picked[0].TryGetLocalPath();
        }

        void OnSave(object sender, RoutedEventArgs e)
        {
            _settings.LaunchNet7Path       = c_LaunchNet7Txt.Text ?? "";
            _settings.EditorsCheckoutRoot  = c_EditorsRootTxt.Text ?? "";
            _settings.Save();
            Close();
        }

        void OnCancel(object sender, RoutedEventArgs e) => Close();
    }
}
