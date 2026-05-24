// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// Ported from N7.GUI.HarvestableResTypes under Net-7 Entertainment's
// CC BY-NC-SA 3.0; preservation modifications inherit under ShareAlike.
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
    // Two-list editor: 16 hard-coded resource types on the left
    // (unselected), the field's currently-selected types on the right.
    // Add/remove buttons move items and write rows into
    // sector_objects_harvestable_restypes.
    //
    // Original's `else if (item == "Glowing Asteroid 2") { type = 1823; }`
    // is preserved verbatim — that single arm uses 1823 (decimal) instead
    // of 0x71F (1823 decimal == 0x71F, so the two values are equal). The
    // other arms use hex. Behaviour-identical; left as-is.
    public class HarvestableResTypesDialog : Window
    {
        // (name, type-id) pairs, in original-listed order.
        private static readonly (string Name, int Type)[] Catalog =
        {
            ("Glowing Asteroid 1",      0x71E),
            ("Glowing Asteroid 2",      0x71F),
            ("Glowing Asteroid 3",      0x720),
            ("Asteroid 1",              0x721),
            ("Asteroid 2",              0x722),
            ("Asteroid 3",              0x723),
            ("Hydrocarbon Deposit 1",   0x724),
            ("Hydrocarbon Deposit 2",   0x725),
            ("Hydrocarbon Deposit 3",   0x726),
            ("Crystalline Asteroid 1",  0x727),
            ("Crystalline Asteroid 2",  0x728),
            ("Crystalline Asteroid 3",  0x729),
            ("Gas Cloud",               0x72A),
            ("Inorganic Hulk 01",       1131),
            ("Organic Hulk 01",         1132),
        };

        private readonly int _id;
        private readonly ListBox _left = new ListBox();
        private readonly ListBox _right = new ListBox();

        public HarvestableResTypesDialog()
        {
            // Original derived _id either from the currently-selected
            // sector object (if it exists) or from sector_objects'
            // Auto_increment. Avalonia port preserves the same logic.
            _id = ResolveGroupId();

            Title = "Harvestable Resource Types";
            Width = 560;
            Height = 380;

            foreach (var (name, _) in Catalog) _left.Items.Add(name);

            // Pre-fill the right list from the DB.
            string q = "SELECT * FROM sector_objects_harvestable_restypes where group_id='" + _id + "';";
            DataTable loadTypes = Database.executeQuery(Database.DatabaseName.net7, q);
            foreach (DataRow i in loadTypes.Rows)
            {
                int t = int.Parse(i["type"].ToString());
                string name = NameFor(t);
                if (name != null)
                {
                    _left.Items.Remove(name);
                    _right.Items.Add(name);
                }
            }

            var add = new Button { Content = "Add ›", Width = 80 };
            add.Click += (_, _) => MoveLeftToRight();
            var remove = new Button { Content = "‹ Remove", Width = 80 };
            remove.Click += (_, _) => MoveRightToLeft();
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
            Grid.SetColumn(_left, 0); grid.Children.Add(_left);
            Grid.SetColumn(middle, 1); grid.Children.Add(middle);
            Grid.SetColumn(_right, 2); grid.Children.Add(_right);

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

        private static string NameFor(int type)
        {
            foreach (var (n, t) in Catalog) if (t == type) return n;
            return null;
        }

        private static int TypeFor(string name)
        {
            foreach (var (n, t) in Catalog) if (n == name) return t;
            return 1823; // Original default fallback.
        }

        private void MoveLeftToRight()
        {
            if (_left.SelectedItem == null) return;
            string item = _left.SelectedItem.ToString();
            int type = TypeFor(item);
            _right.Items.Add(item);
            _left.Items.Remove(item);
            Database.executeQuery(Database.DatabaseName.net7,
                "INSERT INTO sector_objects_harvestable_restypes SET group_id='" + _id + "', type='" + type + "';");
        }

        private void MoveRightToLeft()
        {
            if (_right.SelectedItem == null) return;
            string item = _right.SelectedItem.ToString();
            int type = TypeFor(item);
            _left.Items.Add(item);
            _right.Items.Remove(item);
            Database.executeQuery(Database.DatabaseName.net7,
                "DELETE FROM sector_objects_harvestable_restypes where group_id='" + _id + "' and type='" + type + "';");
        }
    }
}
