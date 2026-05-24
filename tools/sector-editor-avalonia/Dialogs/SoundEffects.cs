// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// Ported from N7.GUI.SoundEffects under Net-7 Entertainment's CC BY-NC-SA 3.0;
// preservation modifications inherit under ShareAlike.
// License: LICENSES/enb-emulator

using System;
using System.Data;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using N7;
using N7.Sql;

namespace SectorEditorAvalonia.Dialogs
{
    // Sound-effect picker. Original loaded `SELECT * FROM effects
    // where effect_class='SoundEffect' order by description;` into a
    // DataGridView and returned the selected effect_id via SelectedID.
    public class SoundEffectsDialog : Window
    {
        public int SelectedID;
        public int PgID;
        private readonly DataGrid _grid;

        public SoundEffectsDialog()
        {
            Title = "Sound Effects";
            Width = 640;
            Height = 480;

            const string query = "SELECT * FROM effects where effect_class='SoundEffect' order by description;";
            DataTable effects = Database.executeQuery(Database.DatabaseName.net7, query);

            _grid = new DataGrid
            {
                ItemsSource = effects.DefaultView,
                SelectionMode = DataGridSelectionMode.Single,
                IsReadOnly = true,
                AutoGenerateColumns = true,
            };

            var ok = new Button { Content = "OK", Width = 80 };
            ok.Click += (_, _) =>
            {
                if (_grid.SelectedItem is DataRowView drv)
                {
                    SelectedID = int.Parse(drv.Row[0].ToString());
                }
                Close();
            };
            var cancel = new Button { Content = "Cancel", Width = 80 };
            cancel.Click += (_, _) => { SelectedID = PgID; Close(); };

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
            DockPanel.SetDock(buttons, Dock.Bottom);
            dock.Children.Add(buttons);
            dock.Children.Add(_grid);
            Content = dock;
        }
    }
}
