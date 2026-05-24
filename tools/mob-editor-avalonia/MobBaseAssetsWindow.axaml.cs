using System;
using System.Collections.Generic;
using System.Data;
using Avalonia.Controls;
using Avalonia.Interactivity;
using MobEditorAvalonia.SQL;

namespace MobEditorAvalonia
{
    // Avalonia port of tools/mob-editor/GUI/MobBaseAssets.cs. The original
    // used a WinForms ListView with image thumbnails grouped by category;
    // since the editor binary stripped its `images/` tree in our repo
    // (and Avalonia has no native grouped-thumbnail ListView), the port
    // shows a flat list of "<base_id>: <description> (<filename>)".
    public partial class MobBaseAssetsWindow : Window
    {
        readonly BaseAssetSQL _baseAssets;
        readonly List<int> _idsForRow = new();
        public int? SelectedID { get; private set; }

        // Parameterless ctor only for the AXAML runtime loader (and the
        // designer preview). Production callers must pass a real SQL
        // wrapper.
        public MobBaseAssetsWindow() : this(null) { }

        public MobBaseAssetsWindow(BaseAssetSQL baseAssets)
        {
            _baseAssets = baseAssets;
            InitializeComponent();

            // Same hard-coded category list as the original — these match
            // assets.main_cat values curated by the upstream content team.
            c_Category.ItemsSource = new[]
            {
                "Please Make a Selection",
                "Capital Ships", "Drones", "Jenquai", "Mobs",
                "NPC Ship Hulls", "Progen", "Pvp", "Terran", "Turrets",
            };
            c_Category.SelectedIndex = 0;
        }

        void OnCategoryChanged(object sender, SelectionChangedEventArgs e)
        {
            c_AssetList.Items.Clear();
            _idsForRow.Clear();
            if (c_Category.SelectedItem is not string cat || cat == "Please Make a Selection")
                return;

            DataRow[] rows = _baseAssets.getRowsbyCategory(cat);
            foreach (var r in rows)
            {
                int id = Convert.ToInt32(r["base_id"]);
                string fn = r["filename"]?.ToString() ?? "";
                string descr = r["descr"]?.ToString() ?? "";
                c_AssetList.Items.Add($"{id}: {descr}  ({fn})");
                _idsForRow.Add(id);
            }
        }

        void OnSelectClick(object sender, RoutedEventArgs e)
        {
            int idx = c_AssetList.SelectedIndex;
            if (idx >= 0 && idx < _idsForRow.Count)
                SelectedID = _idsForRow[idx];
            Close();
        }

        void OnCancelClick(object sender, RoutedEventArgs e) => Close();
    }
}
