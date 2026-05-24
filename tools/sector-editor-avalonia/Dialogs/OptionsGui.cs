// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// Ported from N7.GUI.OptionsGui under Net-7 Entertainment's CC BY-NC-SA 3.0;
// preservation modifications inherit under ShareAlike.
// License: LICENSES/enb-emulator

using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using SectorEditorAvalonia.Windows;

namespace SectorEditorAvalonia.Dialogs
{
    // Per-category layer-visibility / overlay / range toggles. The
    // original was 749 LOC of identical-shape CheckBox handlers — 6
    // categories × 9 toggles = 54 handlers, each a 4-line if/else
    // calling one of 9 paired sectorWindow.*On / *Off methods. The
    // Avalonia port collapses that into one Categories list × one
    // Toggles list × a single CheckBox handler that dispatches via
    // a small action table.
    //
    // Surfaced as a UserControl (matching the original) so the host
    // window can dock it anywhere. The MainWindow installs the active
    // SectorWindow via SetSectorWindow(); LoadAll() flushes all current
    // CheckBox states through to the SectorWindow once after install.
    public class OptionsGui : UserControl
    {
        private SectorWindow _sw;

        // (label, sector-object-type-id). Order matches the original's
        // ListView ordering.
        private static readonly (string Label, int Type)[] Categories =
        {
            ("Mobs",         0),
            ("Planets",      3),
            ("Stargates",    11),
            ("Starbases",    12),
            ("Decorations",  37),
            ("Harvestables", 38),
        };

        // (column header, on-method, off-method). The 9 toggles per
        // category — collapsed into one table consumed by all 6.
        private sealed class Toggle
        {
            public string Header = "";
            public System.Action<SectorWindow, int> On = (_, _) => { };
            public System.Action<SectorWindow, int> Off = (_, _) => { };
        }

        private static readonly Toggle[] Toggles =
        {
            new() { Header = "Layer",        On = (s, t) => s.showLayer(t),         Off = (s, t) => s.hideLayer(t) },
            new() { Header = "Text",         On = (s, t) => s.turnOnText(t),        Off = (s, t) => s.turnOffText(t) },
            new() { Header = "RadarRange",   On = (s, t) => s.radarRangeOn(t),      Off = (s, t) => s.radarRangeOff(t) },
            new() { Header = "Signature",    On = (s, t) => s.SignatureOn(t),       Off = (s, t) => s.SignatureOff(t) },
            new() { Header = "NavType 0",    On = (s, t) => s.navTypeZeroOn(t),     Off = (s, t) => s.navTypeZeroOff(t) },
            new() { Header = "NavType 1",    On = (s, t) => s.navTypeOneOn(t),      Off = (s, t) => s.navTypeOneOff(t) },
            new() { Header = "NavType 2",    On = (s, t) => s.navTypeTwoOn(t),      Off = (s, t) => s.navTypeTwoOff(t) },
            new() { Header = "AppearsRadar", On = (s, t) => s.appearsInRadarOn(t),  Off = (s, t) => s.appearsInRadarOff(t) },
            new() { Header = "ExpRange",     On = (s, t) => s.explorationRangeOn(t), Off = (s, t) => s.explorationRangeOff(t) },
        };

        // Indexed (type, toggleIndex) → CheckBox so LoadAll can drive
        // them through identically to the original.
        private readonly Dictionary<(int Type, int ToggleIx), CheckBox> _boxes = new();

        public OptionsGui(SectorWindow sw)
        {
            _sw = sw;
            Build();
        }

        public void SetSectorWindow(SectorWindow sw) => _sw = sw;

        // Original loadAll() walked every CheckedChanged handler with
        // (null, null) to flush state — Avalonia equivalent re-fires
        // the dispatch from each CheckBox's current IsChecked.
        public void LoadAll()
        {
            if (_sw == null) return;
            foreach (var ((type, ix), cb) in _boxes)
            {
                var t = Toggles[ix];
                if (cb.IsChecked == true) t.On(_sw, type);
                else t.Off(_sw, type);
            }
        }

        private void Build()
        {
            // Row 0: header strip ("Category" + each toggle's name).
            // Rows 1..N: one row per category, with N+1 columns
            // (label + 9 CheckBoxes).
            var grid = new Grid { Margin = new Thickness(8) };
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            for (int i = 0; i < Toggles.Length; i++)
                grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            for (int i = 0; i < Categories.Length; i++)
                grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

            // Header.
            grid.Children.Add(Cell(new TextBlock { Text = "Category", FontWeight = Avalonia.Media.FontWeight.Bold, Margin = new Thickness(0, 0, 12, 4) }, 0, 0));
            for (int c = 0; c < Toggles.Length; c++)
                grid.Children.Add(Cell(new TextBlock { Text = Toggles[c].Header, FontWeight = Avalonia.Media.FontWeight.Bold, Margin = new Thickness(8, 0, 8, 4) }, 0, c + 1));

            // Rows.
            for (int r = 0; r < Categories.Length; r++)
            {
                var (label, type) = Categories[r];
                grid.Children.Add(Cell(new TextBlock { Text = label, Margin = new Thickness(0, 4, 12, 4), VerticalAlignment = VerticalAlignment.Center }, r + 1, 0));
                for (int c = 0; c < Toggles.Length; c++)
                {
                    var cb = new CheckBox { IsChecked = true, Margin = new Thickness(8, 2, 8, 2) };
                    int toggleIx = c;
                    int cellType = type;
                    cb.IsCheckedChanged += (_, _) =>
                    {
                        if (_sw == null) return;
                        var t = Toggles[toggleIx];
                        if (cb.IsChecked == true) t.On(_sw, cellType);
                        else t.Off(_sw, cellType);
                    };
                    _boxes[(cellType, toggleIx)] = cb;
                    grid.Children.Add(Cell(cb, r + 1, c + 1));
                }
            }

            Content = grid;
        }

        private static Control Cell(Control c, int row, int col)
        {
            Grid.SetRow(c, row);
            Grid.SetColumn(c, col);
            return c;
        }
    }
}
