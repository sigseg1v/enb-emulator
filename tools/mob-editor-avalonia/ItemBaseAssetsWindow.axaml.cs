using System;
using System.Collections.Generic;
using System.Data;
using Avalonia.Controls;
using Avalonia.Interactivity;
using MobEditorAvalonia.SQL;

namespace MobEditorAvalonia
{
    // Avalonia port of tools/mob-editor/GUI/ItemBaseAssets.cs.
    // Original was a thumbnailed ListView grouped by item level.
    // We use a flat ListBox + a Level filter combo for the same result.
    //
    // _type is 0 = equipped, 1 = inventory — passed to the new mob_items
    // row when the user clicks Add. _mobID identifies which mob the new
    // item belongs to.
    public partial class ItemBaseAssetsWindow : Window
    {
        readonly ItemBaseSQL  _itemBase;
        readonly MobItemsSQL  _mobItems;
        readonly BaseAssetSQL _baseAssets;
        readonly int _type;
        readonly int _mobID;
        readonly List<DataRow> _itemsForRow = new();
        int _categoryCode = -2;  // -2 = none selected; -1 = "Misc. Loot"

        public DataRow NewMobItem { get; private set; }

        // Parameterless ctor for the AXAML runtime loader / designer.
        public ItemBaseAssetsWindow() : this(0, 0, null, null, null) { }

        public ItemBaseAssetsWindow(int type, int mobID,
                                    ItemBaseSQL itemBase,
                                    MobItemsSQL mobItems,
                                    BaseAssetSQL baseAssets)
        {
            _type       = type;
            _mobID      = mobID;
            _itemBase   = itemBase;
            _mobItems   = mobItems;
            _baseAssets = baseAssets;
            InitializeComponent();

            c_Category.ItemsSource = new[]
            {
                "Please Select Category",
                "Ammo", "Devices", "Engines", "Reactors", "Shields",
                "Missile Weapon", "Projectile Weapon", "Beam Weapon",
                "Components", "Ore & Resources", "Misc. Loot",
            };
            c_Category.SelectedIndex = 0;

            c_LevelFilter.ItemsSource = new[] { "All", "1", "2", "3", "4", "5", "6", "7", "8", "9" };
            c_LevelFilter.SelectedIndex = 0;
        }

        // Mapping mirrors the original switch in
        // mob-editor/GUI/ItemBaseAssets.cs:113-156.
        int CategoryCodeFor(string label) => label switch
        {
            "Ammo"              => 103,
            "Devices"           => 110,
            "Engines"           => 121,
            "Reactors"          => 120,
            "Shields"           => 122,
            "Missile Weapon"    => 102,
            "Projectile Weapon" => 101,
            "Beam Weapon"       => 100,
            "Components"        => 500,
            "Ore & Resources"   => 800,
            "Misc. Loot"        => -1,
            _                   => -2,
        };

        void OnCategoryChanged(object sender, SelectionChangedEventArgs e)
        {
            if (c_Category.SelectedItem is not string label) return;
            _categoryCode = CategoryCodeFor(label);
            RefillList();
        }

        void OnLevelChanged(object sender, SelectionChangedEventArgs e) => RefillList();

        void RefillList()
        {
            c_ItemList.Items.Clear();
            _itemsForRow.Clear();
            if (_categoryCode == -2) return;

            int wantedLevel = -1;
            if (c_LevelFilter.SelectedItem is string lvl && lvl != "All")
                int.TryParse(lvl, out wantedLevel);

            DataRow[] rows = _itemBase.getRowsByCategory(_categoryCode);
            foreach (var r in rows)
            {
                int lvi = Convert.ToInt32(r["level"]);
                if (wantedLevel != -1 && lvi != wantedLevel) continue;
                int id = Convert.ToInt32(r["id"]);
                string name = r["name"]?.ToString() ?? "";
                c_ItemList.Items.Add($"L{lvi}  [{id}]  {name}");
                _itemsForRow.Add(r);
            }
        }

        void OnAddClick(object sender, RoutedEventArgs e)
        {
            int idx = c_ItemList.SelectedIndex;
            if (idx < 0 || idx >= _itemsForRow.Count) { Close(); return; }

            int selectedItemID = Convert.ToInt32(_itemsForRow[idx]["id"]);

            var newRow = _mobItems.getMobItemsTable().NewRow();
            newRow["mob_id"]       = _mobID;
            newRow["item_base_id"] = selectedItemID;
            newRow["usage_chance"] = 0;
            newRow["drop_chance"]  = 0;
            newRow["type"]         = _type;
            newRow["qty"]          = 1;
            _mobItems.getMobItemsTable().Rows.Add(newRow);
            _mobItems.getMobItemsTable().AcceptChanges();
            _mobItems.insertRecord(newRow);

            NewMobItem = newRow;
            Close();
        }

        void OnCancelClick(object sender, RoutedEventArgs e) => Close();
    }
}
