using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using CommonTools.Gui;

namespace MobEditorAvalonia
{
    public partial class App : Application
    {
        public override void Initialize() => AvaloniaXamlLoader.Load(this);

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                // Same login-handoff dance as faction-editor-avalonia and
                // dataimport-avalonia. Avoid ShowDialog from a lifecycle
                // callback — that deadlocks the dispatcher.
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
