using System;
using Avalonia;
using Avalonia.Headless;

namespace SectorEditorAvalonia
{
    static class Program
    {
        [STAThread]
        public static int Main(string[] args)
        {
            if (Array.IndexOf(args, "--smoke") >= 0)
                return RunSmoke();

            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }

        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                         .UsePlatformDetect()
                         .WithInterFont()
                         .LogToTrace();

        static int RunSmoke()
        {
            try
            {
                AppBuilder.Configure<App>()
                          .UseHeadless(new AvaloniaHeadlessPlatformOptions())
                          .SetupWithoutStarting();

                var login = new CommonTools.Gui.Login();
                Console.WriteLine("login: " + login.GetType().Name + " ok");

                var main = new Windows.MainWindow();
                Console.WriteLine("main: " + main.GetType().Name + " ok");

                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("smoke failed: " + ex);
                return 1;
            }
        }
    }
}
