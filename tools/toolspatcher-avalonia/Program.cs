using System;
using Avalonia;
using Avalonia.Headless;

namespace ToolsPatcherAvalonia
{
    internal class Program
    {
        // Used by the launcher's exe-update flow: original WinForms accepted
        // the launcher exe name as args[0] (default "LaunchNet7.exe").
        // We thread the same arg through to MainWindow on construction.
        public static string LauncherExe { get; private set; } = "LaunchNet7.exe";

        [STAThread]
        public static int Main(string[] args)
        {
            // --smoke runs without a display: instantiate App + MainWindow under
            // Avalonia.Headless. Validates AXAML parse and bindings, exits with
            // 0 on success / nonzero on failure. Used by CI and `just`.
            if (args.Length > 0 && args[0] == "--smoke")
                return SmokeTest();

            if (args.Length > 0 && !string.IsNullOrWhiteSpace(args[0]))
                LauncherExe = args[0];

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
                var w = new MainWindow("LaunchNet7.exe");
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
