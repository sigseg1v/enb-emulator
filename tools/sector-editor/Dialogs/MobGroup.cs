// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// Ported from N7.GUI.MobGroup under Net-7 Entertainment's CC BY-NC-SA 3.0;
// preservation modifications inherit under ShareAlike.
// License: LICENSES/enb-emulator

using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using N7;
using N7.Sql;
using SectorEditor.Utilities;

namespace SectorEditor.Dialogs
{
    // Two-grid mob spawn-group editor. Left grid: every mob in the
    // mob_template table. Right grid: mobs currently in the group
    // (with duplicate group_index disambiguation — the same mob can
    // appear multiple times, identified by (mob_id, group_index)).
    public class MobGroupDialog : Window
    {
        private int _id;
        private readonly MobsSQL _mobs;
        private readonly DataGrid _leftGrid;
        private readonly DataGrid _rightGrid;
        private DataTable _rightTable;

        public MobGroupDialog(MobsSQL mobs)
        {
            _mobs = mobs;

            Title = "Mob Spawn Group";
            Width = 720;
            Height = 460;

            // getMobTable() returns the table MobsSQL loaded once in its ctor, so
            // binding the left grid here is in-memory only. The DB work --
            // ResolveGroupId() and the group pre-fill -- is deferred to Opened so
            // the constructor stays cheap and the window can close while the DB is
            // slow or unreachable (AC.4).
            _leftGrid = new DataGrid
            {
                SelectionMode = DataGridSelectionMode.Single,
                IsReadOnly = true,
            };
            CommonTools.Gui.DataGridBinder.Bind(_leftGrid, _mobs.getMobTable());

            _rightTable = new DataTable();
            _rightTable.Columns.Add("id", typeof(string));
            _rightTable.Columns.Add("name", typeof(string));
            _rightGrid = new DataGrid
            {
                SelectionMode = DataGridSelectionMode.Single,
                IsReadOnly = true,
            };
            CommonTools.Gui.DataGridBinder.Bind(_rightGrid, _rightTable);

            Opened += async (_, _) => await LoadAsync();

            var add = new Button { Content = "Add ›", Width = 80 };
            add.Click += async (_, _) => await AddSelectedToGroup();
            var remove = new Button { Content = "‹ Remove", Width = 80 };
            remove.Click += async (_, _) => await RemoveSelectedFromGroup();
            var ok = new Button { Content = "OK", Width = 80 };
            ok.Click += (_, _) => Close();

            var middle = new StackPanel { Orientation = Orientation.Vertical, Spacing = 8, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8) };
            middle.Children.Add(add);
            middle.Children.Add(remove);

            var grid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,Auto,*"),
                RowDefinitions = new RowDefinitions("*,Auto"),
                Margin = new Thickness(8),
            };
            Grid.SetColumn(_leftGrid, 0); grid.Children.Add(_leftGrid);
            Grid.SetColumn(middle, 1); grid.Children.Add(middle);
            Grid.SetColumn(_rightGrid, 2); grid.Children.Add(_rightGrid);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
            buttons.Children.Add(ok);
            Grid.SetRow(buttons, 1);
            Grid.SetColumnSpan(buttons, 3);
            grid.Children.Add(buttons);

            Content = grid;
        }

        // Deferred DB load (AC.4). Resolve the group id and fetch the group's
        // current mobs off the UI thread, then populate the right grid on the UI
        // thread after the await.
        private async Task LoadAsync()
        {
            int id = await Task.Run(() => ResolveGroupId());
            _id = id;

            string mobsQuery = "SELECT * FROM mob_spawn_group where spawn_group_id='" + _id + "';";
            DataTable groupMobsTable = await Task.Run(() =>
                Database.executeQuery(Database.DatabaseName.net7, mobsQuery));

            // Populate the grid-bound table on the UI thread.
            foreach (DataRow r in groupMobsTable.Rows)
            {
                string mobId = r["mob_id"].ToString();
                foreach (DataRow mr in _mobs.getMobTable().Rows)
                {
                    if (mr["mob_id"].ToString() == mobId)
                    {
                        var nr = _rightTable.NewRow();
                        nr["id"] = mobId;
                        nr["name"] = mr["name"].ToString();
                        _rightTable.Rows.Add(nr);
                    }
                }
            }
        }

        private static int ResolveGroupId()
        {
            // Postgres has no information_schema.tables.Auto_increment; the next
            // sector_objects id to be assigned is MAX(sector_object_id)+1, the
            // same next-id convention used across the editor DAOs.
            DataTable tmp = Database.executeQuery(Database.DatabaseName.net7,
                "SELECT COALESCE(MAX(\"sector_object_id\"), 0) + 1 AS \"next\" FROM sector_objects;");
            int autoID = 0;
            foreach (DataRow z in tmp.Rows) autoID = int.Parse(z["next"].ToString());

            DataTable tmp2 = Database.executeQuery(Database.DatabaseName.net7,
                "SELECT sector_object_id FROM sector_objects where sector_object_id='" + EditorGlobals.SelectedObjectId + "';");
            int id = 0;
            foreach (DataRow z in tmp2.Rows) id = int.Parse(z["sector_object_id"].ToString());

            return id != 0 ? EditorGlobals.SelectedObjectId : autoID;
        }

        private int CountInRight(string mobId)
        {
            int n = 0;
            foreach (DataRow r in _rightTable.Rows) if (r["id"].ToString() == mobId) n++;
            return n;
        }

        private async Task AddSelectedToGroup()
        {
            // Read all control/table values needed for the query on the UI thread
            // BEFORE entering Task.Run; never touch a control inside the lambda.
            if (_leftGrid.SelectedItem is not DataRowView leftRow) return;
            string mobId = leftRow.Row["mob_id"].ToString();
            string name = leftRow.Row["name"].ToString();
            int groupIndex = CountInRight(mobId);

            string insert = "INSERT INTO mob_spawn_group (spawn_group_id, mob_id, group_index) VALUES ('" + _id +
                            "', '" + mobId + "', '" + groupIndex + "');";
            await Task.Run(() => Database.executeQuery(Database.DatabaseName.net7, insert));

            var nr = _rightTable.NewRow();
            nr["id"] = mobId;
            nr["name"] = name;
            _rightTable.Rows.Add(nr);
        }

        private async Task RemoveSelectedFromGroup()
        {
            if (_rightGrid.SelectedItem is not DataRowView rightRow) return;
            string mobId = rightRow.Row["id"].ToString();

            // Preserve the original's quirky (mob_id, group_index)
            // dedup logic — the index it removes counts duplicates
            // before and after the selected row in the visible list.
            int rowIndex = -1;
            for (int i = 0; i < _rightTable.Rows.Count; i++)
                if (_rightTable.Rows[i] == rightRow.Row) { rowIndex = i; break; }
            if (rowIndex < 0) return;

            int index = 0;
            for (int i = rowIndex + 1; i < _rightTable.Rows.Count; i++)
                if (_rightTable.Rows[i]["id"].ToString() == mobId) index++;
            for (int i = 0; i < rowIndex; i++)
                if (_rightTable.Rows[i]["id"].ToString() == mobId) index++;

            string remove = "DELETE FROM mob_spawn_group where spawn_group_id='" + _id +
                            "' and mob_id='" + mobId + "' and group_index='" + index + "';";
            await Task.Run(() => Database.executeQuery(Database.DatabaseName.net7, remove));
            _rightTable.Rows.RemoveAt(rowIndex);
        }
    }
}
