// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// Ported from N7.GUI.BaseAssets under Net-7 Entertainment's CC BY-NC-SA 3.0;
// preservation modifications inherit under ShareAlike.
// License: LICENSES/enb-emulator

using System.Collections.Generic;
using System.Data;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using N7;
using N7.Sql;

namespace SectorEditorAvalonia.Dialogs
{
    // Base-asset picker. Original used a WinForms ListView with one
    // ListViewGroup per category — 23 categories spelled out explicitly
    // (including the typo "Under Contruction", preserved verbatim). The
    // Avalonia port collapses the original's ~150 LOC of per-group
    // boilerplate into one Categories list + a ListBox showing the
    // active category's rows.
    public class BaseAssetsDialog : Window
    {
        // Order matches the original's combobox.
        private static readonly string[] Categories =
        {
            "Asteroids", "Backgrounds", "Capital Ships", "Decorations",
            "Drones", "Destroyed", "Hulks", "Jenquai", "Landmarks", "Moons",
            "Mobs", "NavBuoy", "Planets", "Progen", "Pvp", "Resources",
            "Shipyards", "Starbases", "Stargates", "Stargates Deco",
            "Terran", "Turrets", "Under Contruction",
        };

        public int SelectedID;

        private readonly BaseAssetSQL _assets;
        private readonly ComboBox _categoryCombo;
        private readonly ListBox _list = new ListBox();

        public BaseAssetsDialog(BaseAssetSQL assets)
        {
            _assets = assets;

            Title = "Base Assets";
            Width = 540;
            Height = 460;

            _categoryCombo = new ComboBox();
            _categoryCombo.Items.Add("Please Make a Selection");
            foreach (var c in Categories) _categoryCombo.Items.Add(c);
            _categoryCombo.SelectedIndex = 0;
            _categoryCombo.SelectionChanged += (_, _) => ReloadCategory();

            var top = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8), Spacing = 8 };
            top.Children.Add(new TextBlock { Text = "Category:", VerticalAlignment = VerticalAlignment.Center });
            top.Children.Add(_categoryCombo);

            var ok = new Button { Content = "OK", Width = 80 };
            ok.Click += (_, _) =>
            {
                if (_list.SelectedItem is AssetEntry e) SelectedID = e.Id;
                Close();
            };
            var cancel = new Button { Content = "Cancel", Width = 80 };
            cancel.Click += (_, _) => Close();

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
            dock.Children.Add(_list);
            Content = dock;
        }

        private void ReloadCategory()
        {
            _list.Items.Clear();
            var sel = _categoryCombo.SelectedItem?.ToString();
            if (string.IsNullOrEmpty(sel) || sel == "Please Make a Selection") return;

            DataRow[] rows = _assets.getRowsbyCategory(sel);
            foreach (var r in rows)
            {
                int id = int.Parse(r["base_id"].ToString());
                string name = r["filename"].ToString();
                _list.Items.Add(new AssetEntry { Id = id, Label = "ID: " + id + "   " + name });
            }
        }

        public sealed class AssetEntry
        {
            public int Id { get; set; }
            public string Label { get; set; }
            public override string ToString() => Label;
        }
    }
}
