using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
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

                int gridErrors = DataGridBinderChecks();
                if (gridErrors != 0) return 4;

                int queryErrors = LiveQueryChecks();
                if (queryErrors != 0) return 5;

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

        // Deterministic (no DB) check of DataGridBinder. Regression guard for the
        // "every cell says System.Data.DataView" bug: binding a DataGrid to a
        // DataTable.DefaultView with AutoGenerateColumns=true makes Avalonia
        // reflect the CLR properties of DataRowView (DataView, Item, Item,
        // RowVersion) instead of the data columns. DataGridBinder.Bind must
        // produce one column per DATA column, bound to the "[name]" indexer, and
        // never a DataRowView property column.
        static int DataGridBinderChecks()
        {
            int errors = 0;
            void Check(bool cond, string what)
            {
                if (cond) Console.WriteLine("grid     OK: " + what);
                else { Console.Error.WriteLine("grid   FAIL: " + what); errors++; }
            }

            var table = new DataTable();
            table.Columns.Add("id", typeof(int));
            table.Columns.Add("name", typeof(string));
            var r = table.NewRow(); r["id"] = 7; r["name"] = "Skeletor"; table.Rows.Add(r);

            var grid = new DataGrid();
            DataGridBinder.Bind(grid, table);

            Check(grid.AutoGenerateColumns == false, "AutoGenerateColumns forced off");
            Check(grid.Columns.Count == 2, $"one column per data column (got {grid.Columns.Count})");

            var headers = grid.Columns.Select(c => c.Header?.ToString()).ToList();
            Check(headers.SequenceEqual(new[] { "id", "name" }),
                  "headers are the data columns [" + string.Join(",", headers) + "]");
            // The bug's fingerprint: a DataRowView property leaking in as a column.
            Check(!headers.Contains("DataView") && !headers.Contains("RowVersion"),
                  "no DataRowView property columns leaked in");

            var b0 = (grid.Columns[0] as DataGridTextColumn)?.Binding as Avalonia.Data.Binding;
            Check(b0 != null && b0.Path == "[id]", "column 0 bound to the [id] indexer (path=" + b0?.Path + ")");

            // Bind(null) clears columns + source without throwing.
            DataGridBinder.Bind(grid, null);
            Check(grid.Columns.Count == 0 && grid.ItemsSource == null, "Bind(null) clears the grid");

            return errors;
        }

        // Live, DB-gated check of the multi-table LEFT JOIN read path. The exact
        // query SectorObjectsSql issues when a sector is clicked (sector_objects +
        // 6 subtables) used to fail with "Failed to enable constraints..." because
        // executeQuery filled with MissingSchemaAction.AddWithKey -- which copies
        // the source NOT-NULL/PK constraints onto the DataTable, then enforces
        // them, and the LEFT JOINs produce NULLs in unmatched subtable key
        // columns. The fix drops back to MissingSchemaAction.Add: a DataTable with
        // no constraints cannot raise EnableConstraints at all, so that exception
        // class is eliminated by construction. This sweeps EVERY populated sector
        // and asserts each fills -- the editor's real read path, end to end.
        //
        // Skips cleanly (returns 0) when the container is unreachable.
        static int LiveQueryChecks()
        {
            string host = Environment.GetEnvironmentVariable("ENB_PG_HOST") ?? "localhost";
            int port = int.TryParse(Environment.GetEnvironmentVariable("ENB_PG_PORT"), out var p) ? p : 5434;
            string user = Environment.GetEnvironmentVariable("ENB_PG_USER") ?? "net7";
            string pass = Environment.GetEnvironmentVariable("ENB_PG_PASS") ?? "net7";

            LoginData.Host = host; LoginData.Port = port;
            LoginData.User = user; LoginData.Pass = pass;

            try { DB.Instance.openConnection(); DB.Instance.closeConnection(); }
            catch (Exception ex)
            {
                Console.WriteLine($"query  SKIP: net7 not reachable on {host}:{port} ({ex.Message})");
                return 0;
            }

            int errors = 0;
            void Check(bool cond, string what)
            {
                if (cond) Console.WriteLine("query    OK: " + what);
                else { Console.Error.WriteLine("query  FAIL: " + what); errors++; }
            }

            var sids = new List<string>();
            try
            {
                var pick = DB.Instance.executeQuery(
                    "SELECT DISTINCT sector_id FROM sector_objects ORDER BY sector_id", null, null);
                foreach (DataRow row in pick.Rows) sids.Add(row["sector_id"].ToString());
            }
            catch (Exception ex)
            {
                Check(false, "enumerate populated sectors (" + ex.Message + ")");
                return errors;
            }

            if (sids.Count == 0)
            {
                Console.WriteLine("query  SKIP: no rows in sector_objects to join against");
                return 0;
            }

            string soQuery =
                "SELECT * FROM sector_objects" +
                " left join sector_nav_points on sector_objects.sector_object_id = sector_nav_points.sector_object_id" +
                " left join sector_objects_harvestable on sector_objects.sector_object_id = sector_objects_harvestable.resource_id" +
                " left join sector_objects_planets on sector_objects.sector_object_id = sector_objects_planets.planet_id" +
                " left join sector_objects_starbases on sector_objects.sector_object_id = sector_objects_starbases.starbase_id" +
                " left join sector_objects_stargates on sector_objects.sector_object_id = sector_objects_stargates.stargate_id" +
                " left join sector_objects_mob on sector_objects.sector_object_id = sector_objects_mob.mob_id" +
                " where sector_objects.sector_id=@sid order by sector_objects.type;";

            int ok = 0;
            string firstFail = null;
            foreach (string sid in sids)
            {
                try { DB.Instance.executeQuery(soQuery, new[] { "sid" }, new[] { sid }); ok++; }
                catch (Exception ex) { if (firstFail == null) firstFail = $"sector {sid}: {ex.Message}"; }
            }
            Check(firstFail == null,
                  firstFail == null
                      ? $"sector_objects 6-way LEFT JOIN fills for ALL {sids.Count} populated sectors"
                      : $"sector_objects 6-way LEFT JOIN threw ({ok}/{sids.Count} ok); first -- {firstFail}");

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
