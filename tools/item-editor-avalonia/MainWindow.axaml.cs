using System;
using System.Collections.ObjectModel;
using System.Data;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ItemEditorAvalonia.SQL;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;
using MsBoxIcon = MsBox.Avalonia.Enums.Icon;

namespace ItemEditorAvalonia
{
    // Avalonia port of the WinForms item editor. Layout: left = item DataGrid
    // + name search, right = scrollable detail editor over item_base.
    //
    // All DB I/O is on a background thread (AC.4) so a slow/unreachable DB
    // never wedges the window's close button. Edits are recorded by the shared
    // ChangeTracker and exportable as a re-appliable .sql changeset (AC.3).
    public partial class MainWindow : Window
    {
        ItemsSQL _items;
        DataRow _selectedItem;
        readonly ObservableCollection<ItemRow> _gridRows = new();
        bool _suppressEditEvents;

        public MainWindow()
        {
            InitializeComponent();

            // Record this session's DB edits for export as a .sql changeset.
            CommonTools.Database.DB.Instance.ChangeTracker.Enabled = true;

            c_ItemGrid.ItemsSource = _gridRows;
            c_SearchMode.ItemsSource = new[] { "Equals", "Contains" };
            c_SearchMode.SelectedIndex = 1;

            Opened += async (_, _) => await OnLoadAsync();
        }

        async Task OnLoadAsync()
        {
            // Keep the blocking DB call off the UI thread. Smoke tests close the
            // window before Opened fires so this never runs under headless CI.
            await Task.Run(() =>
            {
                try { _items = new ItemsSQL(); }
                catch (Exception ex)
                {
                    Dispatcher.UIThread.Post(async () =>
                        await Err("Could not load item_base:\n\n" + ex.Message));
                }
            });

            RefillGrid();
        }

        void RefillGrid()
        {
            _gridRows.Clear();
            if (_items == null) return;
            foreach (DataRow r in _items.getItemTable().Rows) AddGridRow(r);
            c_Status.Text = $"{_gridRows.Count} items loaded.";
        }

        void AddGridRow(DataRow r)
        {
            _gridRows.Add(new ItemRow
            {
                ItemID   = Convert.ToInt64(r["id"]),
                Name     = r["name"]?.ToString() ?? "",
                Level    = Convert.ToInt32(r["level"]),
                Category = Convert.ToInt32(r["category"]),
            });
        }

        // ---------- grid + search ----------

        void OnItemGridSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (c_ItemGrid.SelectedItem is not ItemRow row) { ClearDetails(); return; }
            _selectedItem = _items?.getRowByID(row.ItemID);
            if (_selectedItem == null) { ClearDetails(); return; }
            PopulateDetails();
        }

        void OnSearchClick(object sender, RoutedEventArgs e)
        {
            if (_items == null) return;
            string raw = c_SearchText.Text ?? "";
            // DataTable.Select filter expression; escape is doubling single quotes.
            string esc = raw.Replace("'", "''");
            string expr = c_SearchMode.SelectedIndex == 0
                ? "name = '" + esc + "'"
                : "name LIKE '%" + esc + "%'";
            _gridRows.Clear();
            foreach (var dr in _items.getRowsByNameQuery(expr)) AddGridRow(dr);
            c_Status.Text = $"{_gridRows.Count} items matched '{raw}'.";
        }

        // ---------- detail panel ----------

        void ClearDetails()
        {
            _suppressEditEvents = true;
            _selectedItem = null;
            c_IdText.Text = "";
            c_NameText.Text = "";
            c_LevelText.Text = "";
            c_TypeText.Text = "";
            c_CategoryText.Text = "";
            c_SubCategoryText.Text = "";
            c_MaxStackText.Text = "";
            c_PriceText.Text = "";
            c_ManufacturerText.Text = "";
            c_EffectIdText.Text = "";
            c_Asset2dText.Text = "";
            c_Asset3dText.Text = "";
            c_ItemBaseIdText.Text = "";
            c_StatusText.Text = "";
            c_NoTrade.IsChecked = false;
            c_NoStore.IsChecked = false;
            c_NoDestroy.IsChecked = false;
            c_NoManu.IsChecked = false;
            c_Unique.IsChecked = false;
            c_CustomFlag.IsChecked = false;
            c_DescriptionText.Text = "";
            _suppressEditEvents = false;
        }

        void PopulateDetails()
        {
            _suppressEditEvents = true;
            c_IdText.Text           = _selectedItem["id"].ToString();
            c_NameText.Text         = _selectedItem["name"]?.ToString();
            c_LevelText.Text        = _selectedItem["level"].ToString();
            c_TypeText.Text         = _selectedItem["type"].ToString();
            c_CategoryText.Text     = _selectedItem["category"].ToString();
            c_SubCategoryText.Text  = _selectedItem["sub_category"].ToString();
            c_MaxStackText.Text     = _selectedItem["max_stack"].ToString();
            c_PriceText.Text        = _selectedItem["price"].ToString();
            c_ManufacturerText.Text = _selectedItem["manufacturer"].ToString();
            c_EffectIdText.Text     = CellText(_selectedItem["effect_id"]);
            c_Asset2dText.Text      = _selectedItem["2d_asset"].ToString();
            c_Asset3dText.Text      = _selectedItem["3d_asset"].ToString();
            c_ItemBaseIdText.Text   = CellText(_selectedItem["item_base_id"]);
            c_StatusText.Text       = _selectedItem["status"].ToString();
            c_NoTrade.IsChecked     = IsSet(_selectedItem["no_trade"]);
            c_NoStore.IsChecked     = IsSet(_selectedItem["no_store"]);
            c_NoDestroy.IsChecked   = IsSet(_selectedItem["no_destroy"]);
            c_NoManu.IsChecked      = IsSet(_selectedItem["no_manu"]);
            c_Unique.IsChecked      = IsSet(_selectedItem["unique"]);
            c_CustomFlag.IsChecked  = IsSet(_selectedItem["custom_flag"]);
            c_DescriptionText.Text  = _selectedItem["description"]?.ToString();
            _suppressEditEvents = false;
        }

        static string CellText(object o)
            => (o == null || o == DBNull.Value) ? "" : o.ToString();

        static bool IsSet(object o)
            => o != null && o != DBNull.Value && Convert.ToInt32(o) != 0;

        // Live grid sync so an edit to name/level/category shows in the list
        // without a save round-trip.
        void OnNameChanged(object sender, TextChangedEventArgs e)
        {
            if (_suppressEditEvents) return;
            if (c_ItemGrid.SelectedItem is ItemRow row) row.Name = c_NameText.Text ?? "";
        }

        void OnLevelChanged(object sender, TextChangedEventArgs e)
        {
            if (_suppressEditEvents) return;
            if (c_ItemGrid.SelectedItem is ItemRow row && int.TryParse(c_LevelText.Text, out var v))
                row.Level = v;
        }

        void OnCategoryChanged(object sender, TextChangedEventArgs e)
        {
            if (_suppressEditEvents) return;
            if (c_ItemGrid.SelectedItem is ItemRow row && int.TryParse(c_CategoryText.Text, out var v))
                row.Category = v;
        }

        // Read every detail control into the backing DataRow. Returns false (and
        // reports) if a required numeric field doesn't parse.
        bool CommitDetailsToRow()
        {
            if (_selectedItem == null) return false;

            try
            {
                _selectedItem["name"]         = c_NameText.Text ?? "";
                _selectedItem["level"]        = ParseInt(c_LevelText.Text, "Level");
                _selectedItem["type"]         = ParseLong(c_TypeText.Text, "Type");
                _selectedItem["category"]     = ParseInt(c_CategoryText.Text, "Category");
                _selectedItem["sub_category"] = ParseInt(c_SubCategoryText.Text, "Sub-category");
                _selectedItem["max_stack"]    = ParseLong(c_MaxStackText.Text, "Max stack");
                _selectedItem["price"]        = ParseLong(c_PriceText.Text, "Price");
                _selectedItem["manufacturer"] = ParseLong(c_ManufacturerText.Text, "Manufacturer");
                _selectedItem["2d_asset"]     = ParseLong(c_Asset2dText.Text, "2D asset");
                _selectedItem["3d_asset"]     = ParseLong(c_Asset3dText.Text, "3D asset");
                _selectedItem["status"]       = ParseLong(c_StatusText.Text, "Status");
                _selectedItem["effect_id"]    = ParseNullableLong(c_EffectIdText.Text);
                _selectedItem["item_base_id"] = ParseNullableLong(c_ItemBaseIdText.Text);
                _selectedItem["no_trade"]     = c_NoTrade.IsChecked == true ? 1 : 0;
                _selectedItem["no_store"]     = c_NoStore.IsChecked == true ? 1 : 0;
                _selectedItem["no_destroy"]   = c_NoDestroy.IsChecked == true ? 1 : 0;
                _selectedItem["no_manu"]      = c_NoManu.IsChecked == true ? 1 : 0;
                _selectedItem["unique"]       = c_Unique.IsChecked == true ? 1 : 0;
                _selectedItem["custom_flag"]  = c_CustomFlag.IsChecked == true ? 1 : 0;
                _selectedItem["description"]  = c_DescriptionText.Text ?? "";
            }
            catch (FormatException fe)
            {
                _ = Err(fe.Message);
                return false;
            }
            return true;
        }

        static int ParseInt(string s, string field)
        {
            if (int.TryParse(s, out var v)) return v;
            throw new FormatException($"{field} must be a whole number.");
        }

        static long ParseLong(string s, string field)
        {
            if (long.TryParse(s, out var v)) return v;
            throw new FormatException($"{field} must be a whole number.");
        }

        static object ParseNullableLong(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return DBNull.Value;
            if (long.TryParse(s, out var v)) return v;
            throw new FormatException("Effect id / Base item id must be a whole number or blank.");
        }

        // ---------- toolbar actions ----------

        async void OnNewClick(object sender, RoutedEventArgs e)
        {
            if (_items == null) return;
            try
            {
                long id = await Task.Run(() => _items.newRecord());
                var dr = _items.getRowByID(id);
                if (dr != null) AddGridRow(dr);
                c_ItemGrid.SelectedItem = _gridRows[^1];
                c_ItemGrid.ScrollIntoView(_gridRows[^1], null);
                c_Status.Text = $"Created item {id}.";
            }
            catch (Exception ex) { await Err("Create failed: " + ex.Message); }
        }

        async void OnNewFromClick(object sender, RoutedEventArgs e)
        {
            if (_items == null || _selectedItem == null) return;
            try
            {
                var src = _selectedItem;
                long id = await Task.Run(() => _items.newFromRecord(src));
                var dr = _items.getRowByID(id);
                if (dr != null) AddGridRow(dr);
                c_ItemGrid.SelectedItem = _gridRows[^1];
                c_ItemGrid.ScrollIntoView(_gridRows[^1], null);
                c_Status.Text = $"Copied item to {id}.";
            }
            catch (Exception ex) { await Err("Copy failed: " + ex.Message); }
        }

        async void OnSaveClick(object sender, RoutedEventArgs e)
        {
            if (_items == null || _selectedItem == null) return;
            if (!CommitDetailsToRow()) return;
            try
            {
                var dr = _selectedItem;
                await Task.Run(() => _items.updateRecord(dr));
                c_Status.Text = $"Saved item {_selectedItem["id"]}.";
            }
            catch (Exception ex) { await Err("Save failed: " + ex.Message); }
        }

        async void OnDeleteClick(object sender, RoutedEventArgs e)
        {
            if (_items == null || _selectedItem == null) return;
            var result = await MessageBoxManager
                .GetMessageBoxStandard("Record Deletion",
                                       "Are you sure you want to delete this record?",
                                       ButtonEnum.YesNo, MsBoxIcon.Warning)
                .ShowWindowDialogAsync(this);
            if (result != ButtonResult.Yes) return;

            try
            {
                long id = Convert.ToInt64(_selectedItem["id"]);
                var dr = _selectedItem;
                await Task.Run(() => _items.deleteRecord(id, dr));
                if (c_ItemGrid.SelectedItem is ItemRow row) _gridRows.Remove(row);
                _selectedItem = null;
                ClearDetails();
                c_Status.Text = $"Deleted item {id}.";
            }
            catch (Exception ex) { await Err("Delete failed: " + ex.Message); }
        }

        void OnRefreshClick(object sender, RoutedEventArgs e)
        {
            try { _items = new ItemsSQL(); RefillGrid(); ClearDetails(); }
            catch (Exception ex) { _ = Err("Refresh failed: " + ex.Message); }
        }

        async void OnExportClick(object sender, RoutedEventArgs e)
        {
            var tracker = CommonTools.Database.DB.Instance.ChangeTracker;
            if (tracker.Count == 0)
            {
                c_Status.Text = "No DB changes recorded this session.";
                return;
            }

            var picker = await StorageProvider.SaveFilePickerAsync(
                new Avalonia.Platform.Storage.FilePickerSaveOptions
                {
                    Title = "Export changes to SQL",
                    SuggestedFileName = "item-editor-changeset.sql",
                    DefaultExtension = "sql",
                    FileTypeChoices = new[]
                    {
                        new Avalonia.Platform.Storage.FilePickerFileType("SQL script")
                        {
                            Patterns = new[] { "*.sql" }
                        }
                    }
                });
            if (picker == null) return;

            try
            {
                tracker.WriteSqlFile(picker.Path.LocalPath, "Item Editor (Avalonia)");
                c_Status.Text = "Wrote " + tracker.Count + " statement(s) to " + picker.Path.LocalPath;
            }
            catch (Exception ex) { c_Status.Text = "Export failed: " + ex.Message; }
        }

        void OnExitClick(object sender, RoutedEventArgs e) => Close();

        async void OnAboutClick(object sender, RoutedEventArgs e)
            => await new AboutBox().ShowDialog(this);

        Task Err(string msg) =>
            MessageBoxManager.GetMessageBoxStandard("Item Editor - Error", msg,
                ButtonEnum.Ok, MsBoxIcon.Error).ShowWindowDialogAsync(this);
    }
}
