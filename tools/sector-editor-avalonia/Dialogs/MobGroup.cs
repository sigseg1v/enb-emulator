// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// Ported from N7.GUI.MobGroup under Net-7 Entertainment's CC BY-NC-SA 3.0;
// preservation modifications inherit under ShareAlike.
// License: LICENSES/enb-emulator

using System.Collections.Generic;
using System.Data;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using N7;
using N7.Sql;
using SectorEditorAvalonia.Utilities;

namespace SectorEditorAvalonia.Dialogs
{
    // Two-grid mob spawn-group editor. Left grid: every mob in the
    // mob_template table. Right grid: mobs currently in the group
    // (with duplicate group_index disambiguation — the same mob can
    // appear multiple times, identified by (mob_id, group_index)).
    public class MobGroupDialog : Window
    {
        private readonly int _id;
        private readonly MobsSQL _mobs;
        private readonly DataGrid _leftGrid;
        private readonly DataGrid _rightGrid;
        private DataTable _rightTable;

        public MobGroupDialog(MobsSQL mobs)
        {
            _mobs = mobs;
            _id = ResolveGroupId();

            Title = "Mob Spawn Group";
            Width = 720;
            Height = 460;

            _leftGrid = new DataGrid
            {
                ItemsSource = _mobs.getMobTable().DefaultView,
                SelectionMode = DataGridSelectionMode.Single,
                IsReadOnly = true,
                AutoGenerateColumns = true,
            };

            _rightTable = new DataTable();
            _rightTable.Columns.Add("id", typeof(string));
            _rightTable.Columns.Add("name", typeof(string));
            _rightGrid = new DataGrid
            {
                ItemsSource = _rightTable.DefaultView,
                SelectionMode = DataGridSelectionMode.Single,
                IsReadOnly = true,
                AutoGenerateColumns = true,
            };
            PopulateRightFromDb();

            var add = new Button { Content = "Add ›", Width = 80 };
            add.Click += (_, _) => AddSelectedToGroup();
            var remove = new Button { Content = "‹ Remove", Width = 80 };
            remove.Click += (_, _) => RemoveSelectedFromGroup();
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

        private static int ResolveGroupId()
        {
            DataTable tmp = Database.executeQuery(Database.DatabaseName.net7,
                "SELECT Auto_increment FROM information_schema.tables WHERE table_name='sector_objects' and table_schema='net7';");
            int autoID = 0;
            foreach (DataRow z in tmp.Rows) autoID = int.Parse(z["Auto_increment"].ToString());

            DataTable tmp2 = Database.executeQuery(Database.DatabaseName.net7,
                "SELECT sector_object_id FROM sector_objects where sector_object_id='" + EditorGlobals.SelectedObjectId + "';");
            int id = 0;
            foreach (DataRow z in tmp2.Rows) id = int.Parse(z["sector_object_id"].ToString());

            return id != 0 ? EditorGlobals.SelectedObjectId : autoID;
        }

        private void PopulateRightFromDb()
        {
            string mobsQuery = "SELECT * FROM mob_spawn_group where spawn_group_id='" + _id + "';";
            DataTable groupMobsTable = Database.executeQuery(Database.DatabaseName.net7, mobsQuery);
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

        private int CountInRight(string mobId)
        {
            int n = 0;
            foreach (DataRow r in _rightTable.Rows) if (r["id"].ToString() == mobId) n++;
            return n;
        }

        private void AddSelectedToGroup()
        {
            if (_leftGrid.SelectedItem is not DataRowView leftRow) return;
            string mobId = leftRow.Row["mob_id"].ToString();
            string name = leftRow.Row["name"].ToString();
            int groupIndex = CountInRight(mobId);

            string insert = "INSERT INTO mob_spawn_group SET spawn_group_id='" + _id +
                            "', mob_id='" + mobId + "', group_index='" + groupIndex + "';";
            Database.executeQuery(Database.DatabaseName.net7, insert);

            var nr = _rightTable.NewRow();
            nr["id"] = mobId;
            nr["name"] = name;
            _rightTable.Rows.Add(nr);
        }

        private void RemoveSelectedFromGroup()
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
            Database.executeQuery(Database.DatabaseName.net7, remove);
            _rightTable.Rows.RemoveAt(rowIndex);
        }
    }
}
