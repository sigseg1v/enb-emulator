using System;
using Avalonia;
using Avalonia.Headless;

namespace TalkTreeEditorAvalonia
{
    internal class Program
    {
        [STAThread]
        public static int Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "--smoke")
                return SmokeTest();

            // The original WinForms entry point in tools/talktreeeditor took
            // args[0] as the initial conversation XML; preserve that.
            if (args.Length > 0) App.InitialConversation = args[0];

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

                // No DB in this editor — no Login window to instantiate.
                var main = new MainWindow();
                main.Show();
                Console.WriteLine($"main     OK: {main.Width}x{main.Height} \"{main.Title}\"");
                main.Close();

                var branch = new Reply.BranchControl();
                Console.WriteLine($"branch   OK: ctor");
                var trade  = new Reply.TradeControl();
                Console.WriteLine($"trade    OK: ctor");
                var flag   = new Reply.FlagControl();
                Console.WriteLine($"flag     OK: ctor");

                Console.WriteLine("smoke OK: all 4 talktreeeditor-avalonia controls instantiated");
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
