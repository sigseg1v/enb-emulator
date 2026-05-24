using System;
using Avalonia;
using Avalonia.Headless;
using MobEditorAvalonia.SQL;

namespace MobEditorAvalonia
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

                // Instantiate every window in the editor so the AXAML
                // compile, control wiring, and event-handler signatures
                // are all validated. Don't trigger Opened — that would
                // try to hit the DB.
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

                // The two modal pickers take real SQL wrappers as ctor
                // args. Use throwaway placeholder objects — we're only
                // exercising AXAML load + control wiring here.
                var mobAssetsWin = new MobBaseAssetsWindow(SmokePlaceholders.BaseAssets());
                mobAssetsWin.Show();
                Console.WriteLine($"mobAssets OK: {mobAssetsWin.Width}x{mobAssetsWin.Height} \"{mobAssetsWin.Title}\"");
                mobAssetsWin.Close();

                var itemAssetsWin = new ItemBaseAssetsWindow(
                    0, 0,
                    SmokePlaceholders.ItemBase(),
                    SmokePlaceholders.MobItems(),
                    SmokePlaceholders.BaseAssets());
                itemAssetsWin.Show();
                Console.WriteLine($"itemAssets OK: {itemAssetsWin.Width}x{itemAssetsWin.Height} \"{itemAssetsWin.Title}\"");
                itemAssetsWin.Close();

                Console.WriteLine("smoke OK: all 5 mob-editor-avalonia windows instantiated");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("smoke FAIL: " + ex);
                return 1;
            }
        }
    }

    // SQL wrappers without their ctors firing — we just need typed
    // references. The pickers only read the wrappers when the user
    // interacts; smoke instantiates the window and closes it before
    // any handler runs. Use System.Runtime.Serialization to skip the
    // ctor so we don't hit the DB.
    static class SmokePlaceholders
    {
        static T Uninitialized<T>() =>
            (T)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(T));
        public static BaseAssetSQL BaseAssets() => Uninitialized<BaseAssetSQL>();
        public static ItemBaseSQL  ItemBase()   => Uninitialized<ItemBaseSQL>();
        public static MobItemsSQL  MobItems()   => Uninitialized<MobItemsSQL>();
    }
}
