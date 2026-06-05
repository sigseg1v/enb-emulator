using System;
using System.Reflection;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
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
        // FluentTheme so that a MessageBox shown on the login failure path
        // renders instead of throwing on missing styles.
        public override void Initialize() => Styles.Add(new FluentTheme());
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

                int loginErrors = LiveLoginChecks();
                if (loginErrors != 0) return 3;

                Console.WriteLine("smoke OK: all 4 commontools-avalonia windows instantiated");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("smoke FAIL: " + ex);
                return 1;
            }
        }

        // Live, DB-gated end-to-end check of the SHARED Login window against the
        // net7 container. Regression guard for the login version-nag bug:
        // getVersion/setVersion queried the versions table with unquoted
        // mixed-case identifiers (-> 42703 column "version" does not exist) and
        // the gate compared the new assembly name against legacy row keys, so an
        // "Incorrect Version" dialog fired on EVERY login. Login is now "did the
        // DB connect succeed". We drive the real Login the way each editor's
        // App.axaml.cs does: fill the credential boxes, run the real
        // AcceptedLoginInformation handler, assert the window closes VALID. If
        // any early-return dialog (the old nag included) fired, login would not
        // reach Close(): isValid() stays false -- caught here as FAIL.
        //
        // Skips cleanly (returns 0) when the container is unreachable.
        static int LiveLoginChecks()
        {
            const BindingFlags BF = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

            string host = Environment.GetEnvironmentVariable("ENB_PG_HOST") ?? "localhost";
            int port = int.TryParse(Environment.GetEnvironmentVariable("ENB_PG_PORT"), out var p) ? p : 5434;
            string user = Environment.GetEnvironmentVariable("ENB_PG_USER") ?? "net7";
            string pass = Environment.GetEnvironmentVariable("ENB_PG_PASS") ?? "net7";

            LoginData.Host = host; LoginData.Port = port;
            LoginData.User = user; LoginData.Pass = pass;

            try { DB.Instance.openConnection(); DB.Instance.closeConnection(); }
            catch (Exception ex)
            {
                Console.WriteLine($"login  SKIP: net7 not reachable on {host}:{port} ({ex.Message})");
                return 0;
            }

            int errors = 0;
            void Check(bool cond, string what)
            {
                if (cond) Console.WriteLine("login    OK: " + what);
                else { Console.Error.WriteLine("login  FAIL: " + what); errors++; }
            }

            void SetText(object win, string field, string value)
            {
                var tb = (TextBox)win.GetType().GetField(field, BF).GetValue(win);
                tb.Text = value;
            }

            bool Pump(Func<bool> until, int ms)
            {
                int end = Environment.TickCount + ms;
                while (Environment.TickCount < end)
                {
                    Dispatcher.UIThread.RunJobs();
                    if (until != null && until()) return true;
                    Thread.Sleep(20);
                }
                Dispatcher.UIThread.RunJobs();
                return until == null || until();
            }

            // Case 1: valid creds -> login closes VALID, no nag.
            {
                var login = new Login();
                bool closed = false;
                login.Closed += (_, _) => closed = true;
                login.Show();
                Pump(null, 300); // let Opened/DisplayConfiguration run
                SetText(login, "LoginUsername", user);
                SetText(login, "LoginPassword", pass);
                SetText(login, "SQLServer", host);
                SetText(login, "SQLPort", port.ToString());
                login.GetType().GetMethod("AcceptedLoginInformation", BF).Invoke(login, null);
                Pump(() => closed, 8000);
                Check(closed && login.isValid(),
                      $"valid creds -> login closed valid, no version nag (closed={closed} isValid={login.isValid()})");
                if (!closed) login.Close();
            }

            // Case 2: unreachable DB -> login must NOT validate (the connect check
            // still gates; it must not silently pass).
            {
                var login = new Login();
                login.Show();
                Pump(null, 300);
                SetText(login, "LoginUsername", user);
                SetText(login, "LoginPassword", pass);
                SetText(login, "SQLServer", host);
                SetText(login, "SQLPort", "1"); // nothing listens here
                login.GetType().GetMethod("AcceptedLoginInformation", BF).Invoke(login, null);
                Pump(null, 8000);
                Check(!login.isValid(),
                      $"unreachable DB -> login rejected (isValid={login.isValid()})");
                login.Close();
                Pump(null, 500);
            }

            return errors;
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
