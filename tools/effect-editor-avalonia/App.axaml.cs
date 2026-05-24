using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using CommonTools.Gui;

namespace EffectEditorAvalonia
{
    // Port of tools/effect-editor/SQLBind/Program.cs's startup flow.
    // The original drove a custom Login form prepopulated from
    // Config.xml (defaults: net-7.org:3307); we use commontools-avalonia's
    // shared Login dialog instead, which writes back to LoginData and
    // is the same dialog every other MySQL-backed editor in this tree
    // uses (faction, mob, item, mission). Config.xml roundtrip is
    // dropped — commontools' Login already persists credentials.
    public partial class App : Application
    {
        public override void Initialize() => AvaloniaXamlLoader.Load(this);

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

                var login = new Login();
                login.Closed += (_, _) =>
                {
                    if (login.isValid())
                    {
                        var main = new MainWindow();
                        main.Closed += (_, _) => desktop.Shutdown(0);
                        desktop.MainWindow = main;
                        main.Show();
                    }
                    else
                    {
                        desktop.Shutdown(0);
                    }
                };
                desktop.MainWindow = login;
            }

            base.OnFrameworkInitializationCompleted();
        }
    }
}
