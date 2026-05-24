using System;
using Avalonia;
using Avalonia.Headless;

namespace EffectEditorAvalonia
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
                Console.WriteLine($"login    OK: {login.Width}x{login.Height} \"{login.Title}\"");
                login.Close();

                var main = new MainWindow();
                main.Show();
                Console.WriteLine($"main     OK: {main.Width}x{main.Height} \"{main.Title}\"");
                main.Close();

                var search = new EffectSearchWindow();
                search.Show();
                Console.WriteLine($"search   OK: {search.Width}x{search.Height} \"{search.Title}\"");
                search.Close();

                var browse = new ItemBrowseWindow();
                browse.Show();
                Console.WriteLine($"browse   OK: {browse.Width}x{browse.Height} \"{browse.Title}\"");
                browse.Close();

                var edit = new EditItemWindow();
                edit.Show();
                Console.WriteLine($"edit     OK: {edit.Width}x{edit.Height} \"{edit.Title}\"");
                edit.Close();

                Console.WriteLine("smoke OK: all 5 effect-editor-avalonia windows instantiated");
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
