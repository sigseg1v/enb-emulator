using System;
using Avalonia;
using Avalonia.Headless;

namespace MissionEditorAvalonia
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

                var login = new CommonTools.Gui.Login();
                login.Show();
                Console.WriteLine($"login       OK: {login.Width}x{login.Height} \"{login.Title}\"");
                login.Close();

                var main = new MainWindow();
                main.Show();
                Console.WriteLine($"main        OK: {main.Width}x{main.Height} \"{main.Title}\"");
                main.Close();

                var dlgCond = new DlgConditionsWindow();
                dlgCond.Show();
                Console.WriteLine($"conditions  OK: {dlgCond.Width}x{dlgCond.Height} \"{dlgCond.Title}\"");
                dlgCond.Close();

                var dlgStg = new DlgStagesWindow();
                dlgStg.Show();
                Console.WriteLine($"stages      OK: {dlgStg.Width}x{dlgStg.Height} \"{dlgStg.Title}\"");
                dlgStg.Close();

                var dlgComp = new DlgCompletionsWindow();
                dlgComp.Show();
                Console.WriteLine($"completions OK: {dlgComp.Width}x{dlgComp.Height} \"{dlgComp.Title}\"");
                dlgComp.Close();

                var dlgRew = new DlgRewardsWindow();
                dlgRew.Show();
                Console.WriteLine($"rewards     OK: {dlgRew.Width}x{dlgRew.Height} \"{dlgRew.Title}\"");
                dlgRew.Close();

                var dlgRep = new DlgReportWindow();
                dlgRep.Show();
                Console.WriteLine($"report      OK: {dlgRep.Width}x{dlgRep.Height} \"{dlgRep.Title}\"");
                dlgRep.Close();

                Console.WriteLine("smoke OK: all 7 missioneditor-avalonia windows instantiated");
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
