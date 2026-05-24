using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace TalkTreeEditorAvalonia
{
    public partial class App : Application
    {
        // Optional initial conversation XML, passed via argv[0] like the
        // original WinForms entry point. Empty -> editor starts with a
        // single empty node.
        public static string InitialConversation { get; set; } = "";

        public override void Initialize() => AvaloniaXamlLoader.Load(this);

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                var main = new MainWindow();
                main.SetConversation(InitialConversation);
                desktop.MainWindow = main;
            }
            base.OnFrameworkInitializationCompleted();
        }
    }
}
