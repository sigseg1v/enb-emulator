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

                int trackerErrors = ChangeTrackerChecks();
                if (trackerErrors != 0) return 2;

                Console.WriteLine("smoke OK: all 4 commontools-avalonia windows instantiated");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("smoke FAIL: " + ex);
                return 1;
            }
        }

        // Deterministic checks of the change-tracking changeset logic (AC.3).
        // No DB needed: drive the tracker directly and pin its output.
        static int ChangeTrackerChecks()
        {
            int errors = 0;
            void Check(bool cond, string what)
            {
                if (cond) { Console.WriteLine("tracker  OK: " + what); }
                else { Console.Error.WriteLine("tracker FAIL: " + what); errors++; }
            }

            var t = new ChangeTracker();

            // Disabled by default -> records nothing.
            t.Record("UPDATE npc SET hp = @hp WHERE id = @id",
                     new[] { "hp", "id" }, new[] { "100", "5" });
            Check(t.Count == 0, "disabled tracker records nothing");

            t.Enabled = true;

            // SELECT is not a mutation -> ignored.
            t.Record("SELECT * FROM npc WHERE id = @id", new[] { "id" }, new[] { "5" });
            Check(t.Count == 0, "SELECT ignored");

            // UPDATE -> recorded, params inlined as escaped literals, ;-terminated.
            t.Record("UPDATE npc SET name = @name WHERE id = @id",
                     new[] { "name", "id" }, new[] { "O'Brien", "5" });
            string script = t.BuildScript("unit");
            Check(t.Count == 1, "UPDATE recorded");
            Check(script.Contains("'O''Brien'"), "single-quote escaped in value");
            Check(script.Contains("WHERE id = '5';"), "param inlined + statement terminated");
            Check(script.Contains("BEGIN;") && script.Contains("COMMIT;"), "wrapped in a transaction");

            // Prefix-collision: @id must not clobber @id10.
            string inlined = ChangeTracker.InlineParameters(
                "DELETE FROM t WHERE a = @id AND b = @id10",
                new[] { "id", "id10" }, new[] { "1", "99" });
            Check(inlined == "DELETE FROM t WHERE a = '1' AND b = '99'",
                  "longer param name not clobbered by shorter prefix");

            // NULL value -> SQL NULL, not quoted.
            string nul = ChangeTracker.InlineParameters(
                "UPDATE t SET x = @x", new[] { "x" }, new string[] { null });
            Check(nul == "UPDATE t SET x = NULL", "null value -> SQL NULL");

            // Clear empties it.
            t.Clear();
            Check(t.Count == 0, "Clear empties the tracker");

            return errors;
        }
    }
}
