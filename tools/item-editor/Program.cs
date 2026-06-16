using System;
using Avalonia;
using Avalonia.Headless;

namespace ItemEditor
{
    internal class Program
    {
        [STAThread]
        public static int Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "--smoke")
                return SmokeTest();

            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            return 0;
        }

        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .WithInterFont()
                .LogToTrace();

        static int SmokeTest()
        {
            try
            {
                AppBuilder.Configure<App>()
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions())
                    .SetupWithoutStarting();

                // Instantiate every window so AXAML compile, control wiring, and
                // event-handler signatures are all validated. Don't trigger
                // Opened -- that would try to hit the DB.
                var login = new CommonTools.Gui.Login();
                login.Show();
                Console.WriteLine($"login    OK: {login.Width}x{login.Height} \"{login.Title}\"");
                login.Close();

                var main = new MainWindow();
                main.Show();
                Console.WriteLine($"main     OK: {main.Width}x{main.Height} \"{main.Title}\"");
                main.Close();

                var about = new AboutBox();
                about.Show();
                Console.WriteLine($"about    OK: {about.Width}x{about.Height} \"{about.Title}\"");
                about.Close();

                Console.WriteLine("smoke OK: all 3 item-editor windows instantiated");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("smoke FAIL: " + ex);
                return 1;
            }
        }
    }
}
