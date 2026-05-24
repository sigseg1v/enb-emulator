using System;
using Avalonia;
using Avalonia.Headless;

namespace DataImportAvalonia
{
    internal class Program
    {
        [STAThread]
        public static int Main(string[] args)
        {
            // --smoke: headless construction check used by CI/`just` to
            // catch AXAML regressions without needing a display.
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
                var w = new MainWindow();
                w.Show();
                Console.WriteLine($"smoke OK: window {w.Width}x{w.Height} title=\"{w.Title}\"");
                w.Close();
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
