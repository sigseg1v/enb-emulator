// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// Ported from N7.GUI.Settings under Net-7 Entertainment's CC BY-NC-SA 3.0;
// preservation modifications inherit under ShareAlike.
// License: LICENSES/enb-emulator

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using SectorEditorAvalonia.Utilities;

namespace SectorEditorAvalonia.Dialogs
{
    // Two-combo settings dialog matching the original — visualization
    // mode (reset-each-load vs. remembered) and zoom mode (default vs.
    // cursor-anchored). Original used Properties.Settings.Default; the
    // Avalonia port persists through EditorSettings (a small JSON file
    // beside the binary; saves on every change to match the original
    // which called `.Save()` inside each handler).
    public class SettingsDialog : Window
    {
        public SettingsDialog()
        {
            Title = "Settings";
            Width = 360;
            Height = 200;
            CanResize = false;

            var grid = new Grid
            {
                Margin = new Thickness(16),
                ColumnDefinitions = new ColumnDefinitions("Auto,*"),
                RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"),
            };

            grid.Children.Add(new TextBlock { Text = "Visualization:", Margin = new Thickness(0, 4, 8, 4) });
            var visCombo = new ComboBox { Margin = new Thickness(0, 0, 0, 8) };
            visCombo.Items.Add("Reset Each Load");
            visCombo.Items.Add("Remember My Setting");
            visCombo.SelectedIndex = EditorSettings.KeepVisualization ? 1 : 0;
            visCombo.SelectionChanged += (_, _) =>
            {
                EditorSettings.KeepVisualization = visCombo.SelectedIndex == 1;
                EditorSettings.Save();
            };
            Grid.SetColumn(visCombo, 1);
            grid.Children.Add(visCombo);

            grid.Children.Add(new TextBlock { Text = "Zoom Mode:", Margin = new Thickness(0, 4, 8, 4) }.Apply(b => Grid.SetRow(b, 1)));
            var zoomCombo = new ComboBox();
            zoomCombo.Items.Add("Default");
            zoomCombo.Items.Add("Cursor Anchored");
            zoomCombo.SelectedIndex = EditorSettings.ZoomSelection;
            zoomCombo.SelectionChanged += (_, _) =>
            {
                EditorSettings.ZoomSelection = zoomCombo.SelectedIndex;
                EditorSettings.Save();
            };
            Grid.SetRow(zoomCombo, 1);
            Grid.SetColumn(zoomCombo, 1);
            grid.Children.Add(zoomCombo);

            var ok = new Button { Content = "Close", Width = 80, HorizontalAlignment = HorizontalAlignment.Right };
            ok.Click += (_, _) => Close();
            Grid.SetRow(ok, 3);
            Grid.SetColumn(ok, 1);
            grid.Children.Add(ok);

            Content = grid;
        }
    }

    internal static class GridChildEx
    {
        public static T Apply<T>(this T self, System.Action<T> a) { a(self); return self; }
    }
}
