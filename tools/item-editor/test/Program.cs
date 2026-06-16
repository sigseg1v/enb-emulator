using System;
using System.Collections;
using System.Reflection;
using System.Threading;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.Controls;
using CommonTools.Database;
using CommonTools.Gui;
using ItemEditor;

namespace ItemEditorSmoke
{
    // Headless end-to-end check of the Item Editor's real GUI save path against
    // the live `net7` Postgres container. Launches the actual MainWindow under
    // Avalonia's headless platform, selects a row, types into the detail panel,
    // clicks Save (the real OnSaveClick handler), and verifies the edit landed
    // in the DB -- then restores it. This is the regression guard for the
    // "params bound as text -> bigint = text -> silent no-op write" bug
    // (DB.makeParameter binds parameters as Postgres `unknown`); a regression
    // re-breaks this smoke.
    //
    // DB-gated: if the net7 container is not reachable it prints SKIP and
    // returns 0, so it is safe to run anywhere the stack is not up. Run it with
    // the stack up via `just verify-item-editor`.
    public class App : Application { public override void Initialize() { } }

    static class Program
    {
        const BindingFlags BF = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        static object Field(object o, string name) => o.GetType().GetField(name, BF).GetValue(o);

        // Pump the headless dispatcher (there is no running UI loop) until the
        // predicate holds or the timeout elapses.
        static bool Pump(Func<bool> until, int ms)
        {
            int end = Environment.TickCount + ms;
            while (Environment.TickCount < end)
            {
                Dispatcher.UIThread.RunJobs();
                if (until == null) { Thread.Sleep(20); continue; }
                if (until()) return true;
                Thread.Sleep(20);
            }
            Dispatcher.UIThread.RunJobs();
            return until == null || until();
        }

        static int Main()
        {
            // Defaults match the docker-compose net7 content DB; overridable so
            // CI (or a non-default stack) can point the smoke elsewhere.
            LoginData.Host = Environment.GetEnvironmentVariable("ENB_PG_HOST") ?? "localhost";
            LoginData.Port = int.TryParse(Environment.GetEnvironmentVariable("ENB_PG_PORT"), out var p) ? p : 5434;
            LoginData.User = Environment.GetEnvironmentVariable("ENB_PG_USER") ?? "net7";
            LoginData.Pass = Environment.GetEnvironmentVariable("ENB_PG_PASS") ?? "net7";

            // DB-gated: probe the container, skip cleanly if it is not up.
            try { DB.Instance.openConnection(); }
            catch (Exception ex)
            {
                Console.WriteLine("SKIP: net7 container not reachable on " +
                                  LoginData.Host + ":" + LoginData.Port + " (" + ex.Message + ")");
                return 0;
            }

            AppBuilder.Configure<App>()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions())
                .SetupWithoutStarting();

            // Pick the lowest id so the smoke is deterministic across DB states.
            var idRow = DB.Instance.executeQuery(
                "SELECT id FROM item_base ORDER BY id LIMIT 1", null, null);
            if (idRow == null || idRow.Rows.Count == 0)
            {
                Console.WriteLine("SKIP: item_base is empty");
                return 0;
            }
            long id = Convert.ToInt64(idRow.Rows[0]["id"]);

            var pre = DB.Instance.executeQuery(
                "SELECT name, level FROM item_base WHERE id = @id", new[] { "id" }, new[] { id.ToString() });
            string origName = pre.Rows[0]["name"].ToString();
            string origLevel = pre.Rows[0]["level"].ToString();
            Console.WriteLine($"target id {id}: original name='{origName}' level={origLevel}");

            const string newName = "ItemEditorSmokeEdit";
            const string newLevel = "33";
            int rc = 0;

            var w = new MainWindow();
            w.Show();

            // Opened -> OnLoadAsync -> ItemsSQL load (background) -> RefillGrid.
            if (!Pump(() => Field(w, "_items") != null
                            && ((ICollection)Field(w, "_gridRows")).Count > 0, 30000))
            {
                Console.Error.WriteLine("FAIL: grid never loaded");
                return 1;
            }
            var grid = (DataGrid)Field(w, "c_ItemGrid");
            var rows = (IEnumerable)Field(w, "_gridRows");
            Console.WriteLine($"grid loaded: {((ICollection)rows).Count} rows");

            // Select the target row -> OnItemGridSelectionChanged -> PopulateDetails.
            object target = null;
            foreach (var r in rows)
                if ((long)r.GetType().GetProperty("ItemID").GetValue(r) == id) { target = r; break; }
            if (target == null) { Console.Error.WriteLine("FAIL: target row not in grid"); return 1; }
            grid.SelectedItem = target;
            Pump(() => Field(w, "_selectedItem") != null, 3000);

            // Type into the detail panel as a user would.
            ((TextBox)Field(w, "c_NameText")).Text = newName;
            ((TextBox)Field(w, "c_LevelText")).Text = newLevel;
            Pump(null, 200);

            // Click Save (real private async-void handler).
            w.GetType().GetMethod("OnSaveClick", BF).Invoke(w, new object[] { null, null });

            // The save runs on a background thread over the single shared
            // connection -- do NOT touch the DB while it is in flight. Wait on
            // the editor's own status label, then read the DB once.
            var status = (TextBlock)Field(w, "c_Status");
            Pump(() => (status.Text ?? "").StartsWith("Saved item"), 8000);

            var chk = DB.Instance.executeQuery(
                "SELECT name, level FROM item_base WHERE id = @id", new[] { "id" }, new[] { id.ToString() });
            string seenName = chk.Rows[0]["name"].ToString();
            string seenLevel = chk.Rows[0]["level"].ToString();
            bool persisted = seenName == newName && seenLevel == newLevel;
            Console.WriteLine($"after Save: status='{status.Text}' db name='{seenName}' level={seenLevel} -> "
                              + (persisted ? "PERSISTED" : "NOT PERSISTED"));
            if (!persisted) rc = 1;

            // Restore regardless of outcome so the DB is left clean.
            DB.Instance.executeCommand(
                "UPDATE \"item_base\" SET \"name\" = @n, \"level\" = @l WHERE \"id\" = @id",
                new[] { "n", "l", "id" }, new[] { origName, origLevel, id.ToString() });
            var post = DB.Instance.executeQuery(
                "SELECT name FROM item_base WHERE id = @id", new[] { "id" }, new[] { id.ToString() });
            Console.WriteLine($"restored: name='{post.Rows[0]["name"]}'");

            Console.WriteLine(rc == 0 ? "item-editor headless GUI smoke OK" : "item-editor headless GUI smoke FAILED");
            return rc;
        }
    }
}
