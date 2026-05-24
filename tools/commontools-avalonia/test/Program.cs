using System;
using Avalonia;
using Avalonia.Headless;
using CommonTools.Database;
using CommonTools.Gui;

namespace CommonToolsAvaloniaSmoke
{
    // Headless smoke for commontools-avalonia. Instantiates each window
    // class without a real display to verify AXAML parses + windows
    // construct. Lights up enough of the library that a regression in
    // any of the 4 dialogs would surface immediately.
    public class App : Application
    {
        public override void Initialize() { }
    }

    class Program
    {
        static int Main(string[] args)
        {
            try
            {
                AppBuilder.Configure<App>()
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions())
                    .SetupWithoutStarting();

                var login = new Login();
                login.Show();
                Console.WriteLine($"login    OK: {login.Width}x{login.Height} \"{login.Title}\"");
                login.Close();

                var edit  = new DlgEditXml();
                edit.Show();
                Console.WriteLine($"editxml  OK: {edit.Width}x{edit.Height} \"{edit.Title}\"");
                edit.Close();

                var crit  = new DlgSearchCriteria();
                crit.Show();
                Console.WriteLine($"crit     OK: {crit.Width}x{crit.Height} \"{crit.Title}\"");
                crit.Close();

                var search = new DlgSearch();
                search.Show();
                Console.WriteLine($"search   OK: {search.Width}x{search.Height} \"{search.Title}\"");
                search.Close();

                // Exercise the error sink switch (Login installs an
                // Avalonia MessageBox sink in its constructor).
                DBErrorReporter.Show("smoke", "sink rerouted to avalonia by login ctor");

                Console.WriteLine("smoke OK: all 4 commontools-avalonia windows instantiated");
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
