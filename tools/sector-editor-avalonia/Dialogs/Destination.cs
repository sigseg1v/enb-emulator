// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// Ported from N7.GUI.Destination under Net-7 Entertainment's CC BY-NC-SA 3.0;
// preservation modifications inherit under ShareAlike.
// License: LICENSES/enb-emulator

using System.Data;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using N7;
using N7.Sql;
using SectorEditorAvalonia.Utilities;

namespace SectorEditorAvalonia.Dialogs
{
    // Stargate/jump destination picker. Two modes selectable via
    // combobox: sector (id 0) or sector-object within the currently
    // selected sector (id 1). The original used mainFrm.sectorID for
    // the second query; the Avalonia port reads EditorGlobals.SectorID
    // (which the SectorWindow sets when one becomes active).
    public class DestinationDialog : Window
    {
        public int SelectedID;
        public int PgID;

        private readonly DataGrid _grid;
        private readonly ComboBox _typeCombo;

        public DestinationDialog()
        {
            Title = "Pick Destination";
            Width = 640;
            Height = 480;

            _typeCombo = new ComboBox();
            _typeCombo.Items.Add("Sector");
            _typeCombo.Items.Add("Sector Object (in current sector)");
            _typeCombo.SelectionChanged += (_, _) => ReloadGrid();

            _grid = new DataGrid
            {
                SelectionMode = DataGridSelectionMode.Single,
                IsReadOnly = true,
                AutoGenerateColumns = true,
            };

            var ok = new Button { Content = "OK", Width = 80 };
            ok.Click += (_, _) =>
            {
                if (_grid.SelectedItem is DataRowView drv)
                    SelectedID = int.Parse(drv.Row[0].ToString());
                Close();
            };
            var cancel = new Button { Content = "Cancel", Width = 80 };
            cancel.Click += (_, _) => { SelectedID = PgID; Close(); };

            var top = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8), Spacing = 8 };
            top.Children.Add(new TextBlock { Text = "Type:", VerticalAlignment = VerticalAlignment.Center });
            top.Children.Add(_typeCombo);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 8,
                Margin = new Thickness(8),
            };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);

            var dock = new DockPanel();
            DockPanel.SetDock(top, Dock.Top);
            DockPanel.SetDock(buttons, Dock.Bottom);
            dock.Children.Add(top);
            dock.Children.Add(buttons);
            dock.Children.Add(_grid);
            Content = dock;
        }

        private void ReloadGrid()
        {
            string query = _typeCombo.SelectedIndex switch
            {
                0 => "SELECT sector_id, name FROM sectors order by name",
                1 => "SELECT sector_object_id, name FROM sector_objects " +
                     "where sector_id='" + EditorGlobals.SectorID + "' order by name",
                _ => "",
            };
            if (string.IsNullOrEmpty(query)) return;
            DataTable t = Database.executeQuery(Database.DatabaseName.net7, query);
            _grid.ItemsSource = t.DefaultView;
        }
    }
}
