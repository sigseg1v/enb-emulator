using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using MobEditorAvalonia.SQL;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;
using MsBoxIcon = MsBox.Avalonia.Enums.Icon;

namespace MobEditorAvalonia
{
    // Avalonia port of tools/mob-editor/GUI/mainFrm.cs.
    // Layout: left = mob DataGrid + level filter + name search,
    //         right = TabControl (General Details / Equipped / Inventory).
    // The Equipped and Inventory tabs each have a ListBox on the left and a
    // tiny ad-hoc properties panel on the right replacing the original
    // WinForms PropertyGrid (Avalonia has none). The grouped ListView with
    // thumbnails is collapsed to a flat ListBox with text labels of the
    // form "[id] <name>" because the editor's images/ tree was never
    // shipped in this repo.
    public partial class MainWindow : Window
    {
        MobsSQL       _mobs;
        MobItemsSQL   _mobItems;
        FactionSql    _factions;
        BaseAssetSQL  _baseAssets;
        ItemBaseSQL   _itemBase;

        DataRow _selectedMob;
        readonly ObservableCollection<MobRow> _gridRows = new();

        // Order-preserving parallel lists. ListBox stores display strings;
        // _equippedRows[i] and _inventoryRows[i] hold the backing DataRows.
        readonly List<DataRow> _equippedRows  = new();
        readonly List<DataRow> _inventoryRows = new();
        DataRow _selectedEquipped;
        DataRow _selectedInventory;

        bool _suppressEditEvents;

        // Mirror of the original typeCombo.SelectedIndex → mob_base.type
        // mapping. Order matters — these get added to c_TypeCombo in this
        // exact sequence so SelectedIndex is the numeric type.
        static readonly string[] s_Types =
        {
            "Cybernetic", "Structural", "Organic_Red", "Organic_Green",
            "Crystalline", "Energy", "Rock Based",
        };

        public MainWindow()
        {
            InitializeComponent();
            c_MobGrid.ItemsSource = _gridRows;
            Opened += async (_, _) => await OnLoadAsync();
        }

        async Task OnLoadAsync()
        {
            // Keep blocking DB calls off the UI thread so a slow connection
            // doesn't lock the window. Smoke tests close the window before
            // Opened fires so this never runs under headless CI.
            await Task.Run(() =>
            {
                try
                {
                    _mobs       = new MobsSQL();
                    _mobItems   = new MobItemsSQL();
                    _factions   = new FactionSql();
                    _baseAssets = new BaseAssetSQL();
                    _itemBase   = new ItemBaseSQL();
                }
                catch (Exception ex)
                {
                    Dispatcher.UIThread.Post(async () => await Err(
                        "Could not load mob editor tables:\n\n" + ex.Message));
                }
            });

            PopulateCombos();
            RefillGrid();
        }

        void PopulateCombos()
        {
            _suppressEditEvents = true;

            var levels = new List<object> { "All" };
            for (int i = -1; i < 67; i++) levels.Add(i);
            c_LevelFilterCombo.ItemsSource = levels;
            c_LevelFilterCombo.SelectedIndex = 0;

            var levelChoices = new List<object>();
            for (int i = 0; i < 67; i++) levelChoices.Add(i);
            c_LevelCombo.ItemsSource = levelChoices;

            c_TypeCombo.ItemsSource = s_Types;

            var factionItems = new List<string> { "Please Make A Selection" };
            if (_factions != null)
                foreach (DataRow fr in _factions.getFactionTable().Rows)
                    factionItems.Add(fr["name"]?.ToString() ?? "");
            c_FactionCombo.ItemsSource = factionItems;

            c_SearchMode.ItemsSource = new[] { "Equals", "Contains" };
            c_SearchMode.SelectedIndex = 0;

            _suppressEditEvents = false;
        }

        void RefillGrid()
        {
            _gridRows.Clear();
            if (_mobs == null) return;

            foreach (DataRow r in _mobs.getMobTable().Rows)
                AddGridRow(r);

            c_Status.Text = $"{_gridRows.Count} mobs loaded.";
        }

        void AddGridRow(DataRow r)
        {
            _gridRows.Add(new MobRow
            {
                MobID = Convert.ToInt32(r["mob_id"]),
                Name  = r["name"]?.ToString() ?? "",
                Level = Convert.ToInt32(r["level"]),
            });
        }

        // ---------- mob grid + filters ----------

        void OnMobGridSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (c_MobGrid.SelectedItem is not MobRow row) { ClearMobDetails(); return; }
            _selectedMob = _mobs.getRowByID(row.MobID);
            if (_selectedMob == null) { ClearMobDetails(); return; }
            PopulateMobDetails();
        }

        void OnLevelFilterChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressEditEvents || _mobs == null) return;
            if (c_LevelFilterCombo.SelectedItem is not int level) { RefillGrid(); return; }
            _gridRows.Clear();
            foreach (var dr in _mobs.getRowsBetween(level)) AddGridRow(dr);
            c_Status.Text = $"{_gridRows.Count} mobs at level {level}.";
        }

        void OnSearchClick(object sender, RoutedEventArgs e)
        {
            if (_mobs == null) return;
            string raw = c_SearchText.Text ?? "";
            // DataTable.Select takes a filter expression. We don't reach
            // SQL here so the escape is just doubling single quotes.
            string esc = raw.Replace("'", "''");
            string expr = c_SearchMode.SelectedIndex == 1
                ? "name LIKE '%" + esc + "%'"
                : "name = '" + esc + "'";
            _gridRows.Clear();
            foreach (var dr in _mobs.getRowsByNameQuery(expr)) AddGridRow(dr);
            c_Status.Text = $"{_gridRows.Count} mobs matched '{raw}'.";
        }

        // ---------- General Details ----------

        void ClearMobDetails()
        {
            _suppressEditEvents = true;
            _selectedMob = null;
            c_NameText.Text         = "";
            c_LevelCombo.SelectedIndex = -1;
            c_TypeCombo.SelectedIndex  = -1;
            c_FactionCombo.SelectedIndex = 0;
            c_BaseAssetText.Text    = "";
            c_ScaleText.Text        = "";
            c_AiText.Text           = "";
            c_HueValue.Value        = 0;
            c_SaturationValue.Value = 0;
            c_LightnessValue.Value  = 0;
            UpdateTintSwatch();
            _equippedRows.Clear();   c_EquippedList.Items.Clear();
            _inventoryRows.Clear();  c_InventoryList.Items.Clear();
            ClearEquippedProps();
            ClearInventoryProps();
            _suppressEditEvents = false;
        }

        void PopulateMobDetails()
        {
            _suppressEditEvents = true;

            c_NameText.Text      = _selectedMob["name"]?.ToString();
            int level = Convert.ToInt32(_selectedMob["level"]);
            c_LevelCombo.SelectedItem = level;
            int type  = Convert.ToInt32(_selectedMob["type"]);
            if (type >= 0 && type < s_Types.Length)
                c_TypeCombo.SelectedItem = s_Types[type];
            else c_TypeCombo.SelectedIndex = -1;

            int factionID = Convert.ToInt32(_selectedMob["faction_id"]);
            string factionName = _factions != null ? _factions.findNameByID(factionID) : "None";
            c_FactionCombo.SelectedItem = factionName == "None"
                ? "Please Make A Selection"
                : factionName;

            c_BaseAssetText.Text = _selectedMob["base_asset_id"].ToString();
            c_ScaleText.Text     = _selectedMob["scale"].ToString();
            c_AiText.Text        = _selectedMob["ai"]?.ToString();

            c_HueValue.Value        = ToDecimal(_selectedMob["h"]);
            c_SaturationValue.Value = ToDecimal(_selectedMob["s"]);
            c_LightnessValue.Value  = ToDecimal(_selectedMob["v"]);
            UpdateTintSwatch();

            PopulateMobItems(Convert.ToInt32(_selectedMob["mob_id"]));

            _suppressEditEvents = false;
        }

        static decimal ToDecimal(object o)
            => decimal.TryParse(o?.ToString(), out var d) ? d : 0m;

        void PopulateMobItems(int mobID)
        {
            _equippedRows.Clear();
            _inventoryRows.Clear();
            c_EquippedList.Items.Clear();
            c_InventoryList.Items.Clear();
            ClearEquippedProps();
            ClearInventoryProps();

            foreach (var dr in _mobItems.getRowsByID(mobID))
            {
                int ibID = Convert.ToInt32(dr["item_base_id"]);
                int t    = Convert.ToInt32(dr["type"]);
                var ibRow = _itemBase.getRowByID(ibID);
                string label = ibRow != null
                    ? $"[{ibID}]  {ibRow["name"]}  (cat {ibRow["sub_category"]})"
                    : $"[{ibID}]  <unknown>";
                if (t == 0)
                {
                    c_EquippedList.Items.Add(label);
                    _equippedRows.Add(dr);
                }
                else
                {
                    c_InventoryList.Items.Add(label);
                    _inventoryRows.Add(dr);
                }
            }
        }

        void OnNameChanged(object sender, TextChangedEventArgs e)
        {
            if (_suppressEditEvents || _selectedMob == null) return;
            string name = c_NameText.Text ?? "";
            _selectedMob["name"] = name;
            if (c_MobGrid.SelectedItem is MobRow row) row.Name = name;
        }

        void OnLevelChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressEditEvents || _selectedMob == null) return;
            if (c_LevelCombo.SelectedItem is int lvl)
            {
                _selectedMob["level"] = lvl;
                if (c_MobGrid.SelectedItem is MobRow row) row.Level = lvl;
            }
        }

        void OnTypeChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressEditEvents || _selectedMob == null) return;
            _selectedMob["type"] = c_TypeCombo.SelectedIndex;
        }

        void OnFactionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressEditEvents || _selectedMob == null) return;
            if (c_FactionCombo.SelectedItem is string s)
                _selectedMob["faction_id"] = _factions.findIDbyName(s);
        }

        void OnBaseAssetChanged(object sender, TextChangedEventArgs e)
        {
            if (_suppressEditEvents || _selectedMob == null) return;
            if (int.TryParse(c_BaseAssetText.Text, out var id))
                _selectedMob["base_asset_id"] = id;
        }

        void OnScaleChanged(object sender, TextChangedEventArgs e)
        {
            if (_suppressEditEvents || _selectedMob == null) return;
            if (float.TryParse(c_ScaleText.Text, out var f))
                _selectedMob["scale"] = f;
        }

        void OnAiChanged(object sender, TextChangedEventArgs e)
        {
            if (_suppressEditEvents || _selectedMob == null) return;
            _selectedMob["ai"] = c_AiText.Text ?? "";
        }

        void OnHueChanged(object sender, NumericUpDownValueChangedEventArgs e)
        {
            if (_suppressEditEvents || _selectedMob == null) return;
            _selectedMob["h"] = (float)(e.NewValue ?? 0);
            UpdateTintSwatch();
        }

        void OnSaturationChanged(object sender, NumericUpDownValueChangedEventArgs e)
        {
            if (_suppressEditEvents || _selectedMob == null) return;
            _selectedMob["s"] = (float)(e.NewValue ?? 0);
            UpdateTintSwatch();
        }

        void OnLightnessChanged(object sender, NumericUpDownValueChangedEventArgs e)
        {
            if (_suppressEditEvents || _selectedMob == null) return;
            _selectedMob["v"] = (float)(e.NewValue ?? 0);
            UpdateTintSwatch();
        }

        void UpdateTintSwatch()
        {
            double h = (double)(c_HueValue.Value ?? 0);
            double s = (double)(c_SaturationValue.Value ?? 0);
            double l = (double)(c_LightnessValue.Value ?? 0);
            c_TintSwatch.Fill = new SolidColorBrush(HslConvert.HslToRgb(h, s, l));
        }

        // ---------- Equipped + Inventory item edits ----------

        void OnEquippedSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            int idx = c_EquippedList.SelectedIndex;
            if (idx < 0 || idx >= _equippedRows.Count) { ClearEquippedProps(); return; }
            _selectedEquipped = _equippedRows[idx];
            _suppressEditEvents = true;
            c_EquippedUsage.Value = ToDecimal(_selectedEquipped["usage_chance"]);
            c_EquippedDrop.Value  = ToDecimal(_selectedEquipped["drop_chance"]);
            c_EquippedQty.Value   = ToDecimal(_selectedEquipped["qty"]);
            _suppressEditEvents = false;
        }

        void ClearEquippedProps()
        {
            _suppressEditEvents = true;
            _selectedEquipped = null;
            c_EquippedUsage.Value = 0;
            c_EquippedDrop.Value  = 0;
            c_EquippedQty.Value   = 0;
            _suppressEditEvents = false;
        }

        void OnEquippedUsageChanged(object sender, NumericUpDownValueChangedEventArgs e)
        {
            if (_suppressEditEvents || _selectedEquipped == null) return;
            _selectedEquipped["usage_chance"] = (int)(e.NewValue ?? 0);
        }

        void OnEquippedDropChanged(object sender, NumericUpDownValueChangedEventArgs e)
        {
            if (_suppressEditEvents || _selectedEquipped == null) return;
            _selectedEquipped["drop_chance"] = (int)(e.NewValue ?? 0);
        }

        void OnEquippedQtyChanged(object sender, NumericUpDownValueChangedEventArgs e)
        {
            if (_suppressEditEvents || _selectedEquipped == null) return;
            _selectedEquipped["qty"] = (int)(e.NewValue ?? 0);
        }

        void OnInventorySelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            int idx = c_InventoryList.SelectedIndex;
            if (idx < 0 || idx >= _inventoryRows.Count) { ClearInventoryProps(); return; }
            _selectedInventory = _inventoryRows[idx];
            _suppressEditEvents = true;
            c_InventoryDrop.Value = ToDecimal(_selectedInventory["drop_chance"]);
            c_InventoryQty.Value  = ToDecimal(_selectedInventory["qty"]);
            _suppressEditEvents = false;
        }

        void ClearInventoryProps()
        {
            _suppressEditEvents = true;
            _selectedInventory = null;
            c_InventoryDrop.Value = 0;
            c_InventoryQty.Value  = 0;
            _suppressEditEvents = false;
        }

        void OnInventoryDropChanged(object sender, NumericUpDownValueChangedEventArgs e)
        {
            if (_suppressEditEvents || _selectedInventory == null) return;
            _selectedInventory["drop_chance"] = (int)(e.NewValue ?? 0);
        }

        void OnInventoryQtyChanged(object sender, NumericUpDownValueChangedEventArgs e)
        {
            if (_suppressEditEvents || _selectedInventory == null) return;
            _selectedInventory["qty"] = (int)(e.NewValue ?? 0);
        }

        // ---------- modal pickers ----------

        async void OnPickBaseAssetClick(object sender, RoutedEventArgs e)
        {
            if (_selectedMob == null || _baseAssets == null) return;
            var win = new MobBaseAssetsWindow(_baseAssets);
            await win.ShowDialog(this);
            if (win.SelectedID is int id)
            {
                _suppressEditEvents = true;
                c_BaseAssetText.Text = id.ToString();
                _suppressEditEvents = false;
                _selectedMob["base_asset_id"] = id;
            }
        }

        async void OnAddEquippedClick(object sender, RoutedEventArgs e)
            => await AddMobItem(0);

        async void OnAddInventoryClick(object sender, RoutedEventArgs e)
            => await AddMobItem(1);

        async Task AddMobItem(int type)
        {
            if (_selectedMob == null || _itemBase == null) return;
            int mobID = Convert.ToInt32(_selectedMob["mob_id"]);
            var win = new ItemBaseAssetsWindow(type, mobID,
                                               _itemBase, _mobItems, _baseAssets);
            await win.ShowDialog(this);
            if (win.NewMobItem == null) return;

            var ibRow = _itemBase.getRowByID(Convert.ToInt32(win.NewMobItem["item_base_id"]));
            string label = ibRow != null
                ? $"[{win.NewMobItem["item_base_id"]}]  {ibRow["name"]}  (cat {ibRow["sub_category"]})"
                : $"[{win.NewMobItem["item_base_id"]}]  <unknown>";

            if (type == 0)
            {
                c_EquippedList.Items.Add(label);
                _equippedRows.Add(win.NewMobItem);
            }
            else
            {
                c_InventoryList.Items.Add(label);
                _inventoryRows.Add(win.NewMobItem);
            }
        }

        void OnRemoveEquippedClick(object sender, RoutedEventArgs e)
            => RemoveSelectedItem(c_EquippedList, _equippedRows);

        void OnRemoveInventoryClick(object sender, RoutedEventArgs e)
            => RemoveSelectedItem(c_InventoryList, _inventoryRows);

        void RemoveSelectedItem(ListBox listBox, List<DataRow> backing)
        {
            int idx = listBox.SelectedIndex;
            if (idx < 0 || idx >= backing.Count) return;
            try
            {
                _mobItems.deleteRecord(backing[idx]);
            }
            catch (Exception ex)
            {
                _ = Err("Delete failed: " + ex.Message);
                return;
            }
            backing.RemoveAt(idx);
            listBox.Items.RemoveAt(idx);
        }

        // ---------- toolbar actions ----------

        async void OnNewClick(object sender, RoutedEventArgs e)
        {
            if (_mobs == null) return;
            try
            {
                int id = _mobs.newRecord();
                // Append the just-created row to the grid. getRowByID
                // is authoritative because newRecord() pushed it into _mobs.
                var dr = _mobs.getRowByID(id);
                if (dr != null) AddGridRow(dr);
                c_MobGrid.SelectedItem = _gridRows[^1];
                c_MobGrid.ScrollIntoView(_gridRows[^1], null);
                c_Status.Text = $"Created mob {id}.";
            }
            catch (Exception ex) { await Err("Create failed: " + ex.Message); }
        }

        async void OnNewFromClick(object sender, RoutedEventArgs e)
        {
            if (_mobs == null || _selectedMob == null) return;
            try
            {
                int id = _mobs.newFromRecord(_selectedMob);
                var dr = _mobs.getRowByID(id);
                if (dr != null) AddGridRow(dr);
                c_MobGrid.SelectedItem = _gridRows[^1];
                c_MobGrid.ScrollIntoView(_gridRows[^1], null);
                c_Status.Text = $"Copied mob to {id}.";
            }
            catch (Exception ex) { await Err("Copy failed: " + ex.Message); }
        }

        async void OnSaveClick(object sender, RoutedEventArgs e)
        {
            if (_mobs == null || _selectedMob == null) return;
            try
            {
                _mobs.updateRecord(_selectedMob);
                foreach (var dr in _equippedRows)  _mobItems.updateRecord(dr);
                foreach (var dr in _inventoryRows) _mobItems.updateRecord(dr);
                c_Status.Text = $"Saved mob {_selectedMob["mob_id"]}.";
            }
            catch (Exception ex) { await Err("Save failed: " + ex.Message); }
        }

        async void OnDeleteClick(object sender, RoutedEventArgs e)
        {
            if (_mobs == null || _selectedMob == null) return;
            var result = await MessageBoxManager
                .GetMessageBoxStandard("Record Deletion",
                                       "Are you sure you want to delete this record?",
                                       ButtonEnum.YesNo, MsBoxIcon.Warning)
                .ShowWindowDialogAsync(this);
            if (result != ButtonResult.Yes) return;

            try
            {
                int id = Convert.ToInt32(_selectedMob["mob_id"]);
                _mobs.deleteRecord(id, _selectedMob);
                if (c_MobGrid.SelectedItem is MobRow row) _gridRows.Remove(row);
                _selectedMob = null;
                ClearMobDetails();
                c_Status.Text = $"Deleted mob {id}.";
            }
            catch (Exception ex) { await Err("Delete failed: " + ex.Message); }
        }

        void OnRefreshClick(object sender, RoutedEventArgs e)
        {
            try
            {
                _mobs = new MobsSQL();
                RefillGrid();
            }
            catch (Exception ex) { _ = Err("Refresh failed: " + ex.Message); }
        }

        void OnExitClick(object sender, RoutedEventArgs e) => Close();

        async void OnAboutClick(object sender, RoutedEventArgs e)
        {
            var about = new AboutBox();
            await about.ShowDialog(this);
        }

        Task Err(string msg) =>
            MessageBoxManager.GetMessageBoxStandard("Mob Editor - Error", msg,
                ButtonEnum.Ok, MsBoxIcon.Error).ShowWindowDialogAsync(this);
    }
}
