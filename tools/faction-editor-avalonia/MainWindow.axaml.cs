using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using FactionEditorAvalonia.SQL;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;
using MsBoxIcon = MsBox.Avalonia.Enums.Icon;

namespace FactionEditorAvalonia
{
    // Avalonia port of tools/faction-editor/GUI/mainFrm.cs. The control
    // names changed (toolStripButton1/2/3/5 → New/Save/Delete/Refresh)
    // and the WinForms PropertyGrid was replaced with a tiny ad-hoc
    // properties panel (BaseValue NumericUpDown + CurrentValue read-only
    // TextBox + RewardFaction CheckBox) since Avalonia has no PropertyGrid.
    //
    // Editing model preserved: changes to BaseValue/RewardFaction mutate
    // the in-memory DataRow immediately, Save flushes both factions row
    // and faction_matrix rows via the SQL wrappers.
    public partial class MainWindow : Window
    {
        FactionsSQL      _factions;
        FactionMatrixSQL _factionMatrix;
        DataRow          _facRow;

        readonly ObservableCollection<FactionRow> _gridRows = new();
        readonly Dictionary<string, FactionMatrixProps> _matrixProps = new();
        FactionMatrixProps _currentProps;
        bool _suppressEditEvents;   // guard so populateFields() doesn't
                                    // trip BaseValue/RewardFaction handlers

        public MainWindow()
        {
            InitializeComponent();
            c_FactionGrid.ItemsSource = _gridRows;
            Opened += async (_, _) => await OnLoadAsync();
        }

        async Task OnLoadAsync()
        {
            // Block long DB calls off the UI thread to keep the window
            // responsive. The smoke test stubs Opened by closing the
            // window early so this never runs in CI.
            await Task.Run(() =>
            {
                try
                {
                    _factions      = new FactionsSQL();
                    _factionMatrix = new FactionMatrixSQL();
                }
                catch (Exception ex)
                {
                    Dispatcher.UIThread.Post(async () => await Err(
                        "Could not load factions from DB:\n\n" + ex.Message));
                }
            });
            RefillGrid();
        }

        void RefillGrid()
        {
            _gridRows.Clear();
            if (_factions == null) return;
            foreach (DataRow r in _factions.getFactionTable().Rows)
            {
                _gridRows.Add(new FactionRow
                {
                    FactionID = Convert.ToInt32(r["faction_id"]),
                    Name      = r["name"]?.ToString() ?? "",
                });
            }
            c_Status.Text = $"{_gridRows.Count} factions loaded.";
        }

        // ---- event handlers ----

        void OnFactionGridSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            c_FactionList.Items.Clear();
            _matrixProps.Clear();
            _currentProps = null;
            ClearPropsPanel();

            if (c_FactionGrid.SelectedItem is FactionRow row)
                PopulateFields(row.FactionID);
        }

        void PopulateFields(int id)
        {
            _facRow = _factions.getRowByID(id);
            if (_facRow == null) return;

            _suppressEditEvents = true;
            c_NameText.Text        = _facRow["name"]?.ToString();
            c_DescriptionText.Text = _facRow["description"]?.ToString();
            c_PdaText.Text         = _facRow["PDA_text"]?.ToString();
            _suppressEditEvents = false;

            var fmRows = _factionMatrix.getRowsByID(id);
            foreach (var dr in fmRows)
            {
                int feID         = Convert.ToInt32(dr["faction_entry_id"]);
                var entryRow     = _factions.getRowByID(feID);
                if (entryRow == null) continue;

                var fmp = new FactionMatrixProps
                {
                    ID             = Convert.ToInt32(dr["id"]),
                    FactionID      = id,
                    FactionEntryID = feID,
                    BaseValue      = Convert.ToInt32(dr["base_value"]),
                    CurrentValue   = Convert.ToInt32(dr["current_value"]),
                    RewardFaction  = Convert.ToInt32(dr["reward_faction"]) != 0,
                };
                var name = entryRow["name"]?.ToString() ?? feID.ToString();
                c_FactionList.Items.Add(name);
                _matrixProps[name] = fmp;
            }
        }

        void OnFactionListSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (c_FactionList.SelectedItem is not string name) { ClearPropsPanel(); return; }
            if (!_matrixProps.TryGetValue(name, out var fmp))  { ClearPropsPanel(); return; }
            _currentProps = fmp;

            _suppressEditEvents = true;
            c_BaseValue.Value        = fmp.BaseValue;
            c_CurrentValue.Text      = fmp.CurrentValue.ToString();
            c_RewardFaction.IsChecked = fmp.RewardFaction;
            _suppressEditEvents = false;
        }

        void ClearPropsPanel()
        {
            _suppressEditEvents = true;
            c_BaseValue.Value        = 0;
            c_CurrentValue.Text      = "";
            c_RewardFaction.IsChecked = false;
            _suppressEditEvents = false;
        }

        void OnBaseValueChanged(object sender, NumericUpDownValueChangedEventArgs e)
        {
            if (_suppressEditEvents || _currentProps == null) return;
            _currentProps.BaseValue = (int)(e.NewValue ?? 0);
            var dr = _factionMatrix.getRowByID(_currentProps.ID);
            if (dr != null) dr["base_value"] = _currentProps.BaseValue;
        }

        void OnRewardFactionChanged(object sender, RoutedEventArgs e)
        {
            if (_suppressEditEvents || _currentProps == null) return;
            _currentProps.RewardFaction = c_RewardFaction.IsChecked == true;
            var dr = _factionMatrix.getRowByID(_currentProps.ID);
            if (dr != null) dr["reward_faction"] = _currentProps.RewardFaction ? 1 : 0;
        }

        // ---- toolbar / menu actions ----

        void OnRefreshClick(object sender, RoutedEventArgs e)
        {
            _factions = new FactionsSQL();
            RefillGrid();
        }

        async void OnNewClick(object sender, RoutedEventArgs e)
        {
            try
            {
                var tmp     = _factions.newRecord();
                int newId   = Convert.ToInt32(tmp["faction_id"]);

                // Mirror the original: create a faction_matrix row for
                // every other faction so the new faction has a relation
                // entry against each existing one.
                foreach (DataRow r in _factions.getFactionTable().Rows)
                {
                    int existingId = Convert.ToInt32(r["faction_id"]);
                    if (existingId == newId) continue;
                    _factionMatrix.newRecord(newId, existingId);
                }

                _gridRows.Add(new FactionRow
                {
                    FactionID = newId,
                    Name      = tmp["name"]?.ToString() ?? "",
                });
                c_FactionGrid.SelectedItem = _gridRows[^1];
                c_FactionGrid.ScrollIntoView(_gridRows[^1], null);
                c_Status.Text = $"Created faction {newId}.";
            }
            catch (Exception ex) { await Err("Failed to create faction: " + ex.Message); }
        }

        async void OnSaveClick(object sender, RoutedEventArgs e)
        {
            if (_facRow == null) return;
            try
            {
                _facRow["name"]        = c_NameText.Text        ?? "";
                _facRow["description"] = c_DescriptionText.Text ?? "";
                _facRow["PDA_text"]    = c_PdaText.Text         ?? "";

                _factions.updateRecord(_facRow);
                int factionID = Convert.ToInt32(_facRow["faction_id"]);
                _factionMatrix.updateRecord(factionID);

                // Reflect any name change in the grid row
                if (c_FactionGrid.SelectedItem is FactionRow row)
                    row.Name = _facRow["name"]?.ToString() ?? "";
                c_Status.Text = $"Saved faction {factionID}.";
            }
            catch (Exception ex) { await Err("Save failed: " + ex.Message); }
        }

        async void OnDeleteClick(object sender, RoutedEventArgs e)
        {
            if (_facRow == null) return;
            var result = await MessageBoxManager
                .GetMessageBoxStandard("Record Deletion",
                                       "Are you sure you want to delete this record?",
                                       ButtonEnum.YesNo, MsBoxIcon.Warning)
                .ShowWindowDialogAsync(this);
            if (result != ButtonResult.Yes) return;

            try
            {
                int id = Convert.ToInt32(_facRow["faction_id"]);
                _factions.deleteRecord(id, _facRow);
                _factionMatrix.deleteRecord(id);

                if (c_FactionGrid.SelectedItem is FactionRow row)
                    _gridRows.Remove(row);
                _facRow = null;
                c_Status.Text = $"Deleted faction {id}.";
            }
            catch (Exception ex) { await Err("Delete failed: " + ex.Message); }
        }

        void OnExitClick(object sender, RoutedEventArgs e) => Close();

        async void OnAboutClick(object sender, RoutedEventArgs e)
        {
            var about = new AboutBox();
            await about.ShowDialog(this);
        }

        Task Err(string msg) =>
            MessageBoxManager.GetMessageBoxStandard("Faction Editor - Error", msg,
                ButtonEnum.Ok, MsBoxIcon.Error).ShowWindowDialogAsync(this);
    }
}
