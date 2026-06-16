using System;
using System.Data;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using CommonTools.Database;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;
using MsBoxIcon = MsBox.Avalonia.Enums.Icon;

namespace EffectEditor
{
    // Port of tools/effect-editor/SQLBind/EffectSearch.cs.
    //
    // Behaviour preserved:
    //  - Same query: SELECT EffectID,Description,Tooltip FROM item_effect_base
    //  - OK returns SelectedEffectId; Cancel/close returns -1.
    //  - DELETE prompt with effect description, then
    //    DELETE FROM item_effect_base WHERE EffectID = ? (parameterised
    //    rather than concatenated).
    //
    // Mechanical changes:
    //  - WinForms DataGridView right-click ContextMenu → a single
    //    "Delete..." button at the bottom. Avalonia DataGrid contextmenu
    //    handling is more involved than its value here justifies, and a
    //    button has the same semantics with less moving parts.
    public partial class EffectSearchWindow : Window
    {
        public int SelectedEffectId { get; private set; } = -1;

        public EffectSearchWindow()
        {
            InitializeComponent();
            // Keep the blocking DB call off the UI thread (AC.4) so the window's
            // close button stays responsive while the query runs.
            Opened += async (_, _) => await ReloadAsync();
        }

        async Task ReloadAsync()
        {
            try
            {
                var dt = await Task.Run(() => DB.Instance.executeQuery(
                    "SELECT \"EffectID\",\"Description\",\"Tooltip\" FROM item_effect_base",
                    null, null));
                CommonTools.Gui.DataGridBinder.Bind(c_Grid, dt);
            }
            catch (Exception ex)
            {
                Title = "Effect Search (no DB: " + ex.Message + ")";
            }
        }

        async void OnDelete(object sender, RoutedEventArgs e)
        {
            if (c_Grid.SelectedItem is not DataRowView row)
            {
                await Show("No row selected", "Pick an effect row first.", MsBoxIcon.Warning);
                return;
            }
            int eid = Convert.ToInt32(row.Row["EffectID"]);
            string d = row.Row["Description"]?.ToString() ?? "";
            var box = MessageBoxManager.GetMessageBoxStandard(
                "Delete Confirmation",
                $"Are you sure you want to delete '{d}' (EffectID {eid})?",
                ButtonEnum.YesNo, MsBoxIcon.Warning);
            var res = await box.ShowWindowDialogAsync(this);
            if (res != ButtonResult.Yes) return;

            string eidStr = eid.ToString();
            await Task.Run(() => DB.Instance.executeCommand(
                "DELETE FROM item_effect_base WHERE \"EffectID\" = @eid",
                new[] { "eid" }, new[] { eidStr }));
            await ReloadAsync();
        }

        async void OnOk(object sender, RoutedEventArgs e)
        {
            if (c_Grid.SelectedItem is not DataRowView row)
            {
                await Show("No row selected", "Pick an effect row first.", MsBoxIcon.Warning);
                return;
            }
            SelectedEffectId = Convert.ToInt32(row.Row["EffectID"]);
            Close();
        }

        void OnCancel(object sender, RoutedEventArgs e)
        {
            SelectedEffectId = -1;
            Close();
        }

        async System.Threading.Tasks.Task Show(string title, string body, MsBoxIcon icon)
        {
            var box = MessageBoxManager.GetMessageBoxStandard(title, body, ButtonEnum.Ok, icon);
            await box.ShowWindowDialogAsync(this);
        }
    }
}
