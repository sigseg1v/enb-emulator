using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using CommonTools.Gui;

namespace DataImportAvalonia
{
    public partial class App : Application
    {
        public override void Initialize() => AvaloniaXamlLoader.Load(this);

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                // Mirror the original WinForms flow: Login first, then the
                // import form if the user authenticated. Login itself
                // populates the static LoginData / DB.Instance state.
                //
                // Avalonia idiom: keep the app alive across the Login→Main
                // handoff by switching to OnExplicitShutdown. Login serves
                // as the initial MainWindow; on its Closed event we either
                // swap in MainWindow or shut down.
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
