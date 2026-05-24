using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using CommonTools.Gui;

namespace SectorEditorAvalonia
{
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
                        var main = new Windows.MainWindow();
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
