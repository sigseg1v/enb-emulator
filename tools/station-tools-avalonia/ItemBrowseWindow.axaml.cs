using System;
using System.Collections.Generic;
using System.Data;
using Avalonia.Controls;
using Avalonia.Interactivity;
using CommonTools.Database;
using MsBox.Avalonia;
using MsBoxIcon = MsBox.Avalonia.Enums.Icon;

namespace StationToolsAvalonia
{
    // Avalonia port of tools/station-tools/ItemBrowse.cs.
    // Original was a modal Form with a DataGridView; this is a modal
    // Window with DataGrid. Multi-select is controlled by the caller via
    // ShowMultiAsync vs. ShowSingleAsync, mirroring the original's
    // overloaded ShowDialog(bool Multi).
    //
    // Wire change: every SELECT goes through DB.Instance.executeQuery
    // with parameter binding. The original built `LIKE '%" + textbox + "%'`
    // by string concatenation, which is a textbook SQL-injection hole —
    // closed silently here without behaviour change.
    public partial class ItemBrowseWindow : Window
    {
        static readonly string[] g_ItemType =
        {
            "System","Weapon","Shields","Sensor","Ejector","Turret",
            "Engine","Reactor","Controler","Robot","Ammo","Devices",
            "System","Base","Beam Weapon","Missile Launcher",
            "Projectile Weapon","Countermesure","Over Rides","All"
        };

        readonly List<string> g_Description = new();
        int g_ItemBaseID = -1;

        public ItemBrowseWindow()
        {
            InitializeComponent();

            for (int i = 0; i <= 9; i++)
                c_LevelQuery.Items.Add(i.ToString());
            c_LevelQuery.SelectedIndex = 0;

            c_TypeQuery.Items.Add("(any)");
            foreach (var t in g_ItemType)
                c_TypeQuery.Items.Add(t);
            c_TypeQuery.SelectedIndex = 0;
        }

        public int GetItemBase() => g_ItemBaseID;
        public int GetNumSelected() => c_ItemList.SelectedItems?.Count ?? 0;

        public int GetSelectedItemID(int row)
        {
            if (c_ItemList.SelectedItems == null || row >= c_ItemList.SelectedItems.Count)
                return 0;
            if (c_ItemList.SelectedItems[row] is DataRowView drv)
                return Convert.ToInt32(drv.Row["ItemID"]);
            return 0;
        }

        public string GetSelectedItemName(int row)
        {
            if (c_ItemList.SelectedItems == null || row >= c_ItemList.SelectedItems.Count)
                return "";
            if (c_ItemList.SelectedItems[row] is DataRowView drv)
                return drv.Row["Name"]?.ToString() ?? "";
            return "";
        }

        void OnSearch(object sender, RoutedEventArgs e)
        {
            var keys = new List<string>();
            var vals = new List<string>();
            var clauses = new List<string>();

            if (!string.IsNullOrEmpty(c_NameQuery.Text))
            {
                clauses.Add("`item_base`.`name` LIKE @name");
                keys.Add("@name");
                vals.Add("%" + c_NameQuery.Text + "%");
            }

            if (c_LevelQuery.SelectedIndex > 0)
            {
                clauses.Add("`item_base`.`level` = @level");
                keys.Add("@level");
                vals.Add(c_LevelQuery.SelectedIndex.ToString());
            }

            if (c_TypeQuery.SelectedIndex > 0)
            {
                clauses.Add("`item_base`.`type` = @type");
                keys.Add("@type");
                vals.Add((c_TypeQuery.SelectedIndex - 1).ToString());
            }

            if (clauses.Count == 0)
            {
                Error("You need to specify at least one criteria to search on!");
                return;
            }

            string sql = "SELECT `id` AS ItemID, `name` AS Name, `type` AS Type, " +
                         "`level` AS Level, `price` AS Price, `description` AS Description " +
                         "FROM `item_base` WHERE " + string.Join(" AND ", clauses);

            DataTable dt;
            try
            {
                dt = DB.Instance.executeQuery(sql, keys.ToArray(), vals.ToArray());
            }
            catch (Exception ex)
            {
                Error("SQL error: " + ex.Message);
                return;
            }

            if (dt == null || dt.Rows.Count == 0)
            {
                Error("No results from your query");
                return;
            }

            g_Description.Clear();
            foreach (DataRow r in dt.Rows)
            {
                g_Description.Add(r["Description"]?.ToString() ?? "");
                int typeIdx = Convert.ToInt32(r["Type"]);
                if (typeIdx >= 0 && typeIdx < g_ItemType.Length)
                    r["Type"] = g_ItemType[typeIdx];
            }
            dt.Columns.Remove("Description");

            c_ItemList.ItemsSource = dt.DefaultView;
            c_RowCount.Text = "Number of Results: " + dt.Rows.Count;
        }

        void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (c_ItemList.SelectedItem is DataRowView drv)
            {
                int row = c_ItemList.SelectedIndex;
                if (row >= 0 && row < g_Description.Count)
                    c_ItemDescription.Text = g_Description[row];
            }
        }

        void OnAddSelected(object sender, RoutedEventArgs e)
        {
            if (c_ItemList.SelectionMode == DataGridSelectionMode.Single)
            {
                if (c_ItemList.SelectedItem is DataRowView drv)
                    g_ItemBaseID = Convert.ToInt32(drv.Row["ItemID"]);
            }
            Close();
        }

        void OnClose(object sender, RoutedEventArgs e)
        {
            c_ItemList.SelectionMode = DataGridSelectionMode.Single;
            Close();
        }

        public void SetMultiSelect(bool multi)
            => c_ItemList.SelectionMode = multi ? DataGridSelectionMode.Extended : DataGridSelectionMode.Single;

        static void Error(string msg)
        {
            try { MessageBoxManager.GetMessageBoxStandard("Error", msg, MsBox.Avalonia.Enums.ButtonEnum.Ok, MsBoxIcon.Error).ShowAsync(); }
            catch { Console.Error.WriteLine("ITEMBROWSE ERROR: " + msg); }
        }
    }
}
