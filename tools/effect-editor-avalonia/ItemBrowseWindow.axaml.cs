using System;
using System.Collections.Generic;
using System.Data;
using Avalonia.Controls;
using Avalonia.Interactivity;
using CommonTools.Database;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;
using MsBoxIcon = MsBox.Avalonia.Enums.Icon;

namespace EffectEditorAvalonia
{
    // Port of tools/effect-editor/SQLBind/ItemBrowse.cs.
    //
    // Behaviour preserved:
    //  - Item type combo: same labels in the same order as the original
    //    (System/Weapon/.../All) so SelectedIndex-1 still maps to the
    //    database `type` value.
    //  - "You need to specify at least one criterion" gating preserved.
    //  - SELECT id,level,category,sub_category,type,price,max_stack,name,
    //    description,manufacturer FROM item_base WHERE ... AND ... AND ...
    //  - SelectedItemBaseId publicly read by EditItemWindow's Browse flow.
    //
    // Mechanical changes:
    //  - LIKE pattern bound through parameter (was concatenated). MySQL
    //    Connector treats placeholders inside LIKE arguments correctly.
    //  - DataGridView → Avalonia DataGrid AutoGenerateColumns from the
    //    DataTable returned by executeQuery.
    public partial class ItemBrowseWindow : Window
    {
        public int SelectedItemBaseId { get; private set; } = -1;

        static readonly string[] _itemTypes =
        {
            "(any)", "System","Weapon","Shields","Sensor","Ejector","Turret","Engine","Reactor","Controler",
            "Robot","Ammo","Devices","System","Base","Beam Weapon","Missile Launcher","Projectile Weapon",
            "Countermesure","Over Rides","All",
        };

        public ItemBrowseWindow()
        {
            InitializeComponent();

            c_ItemLevel.Items.Add("(any)");
            for (int i = 1; i <= 9; i++) c_ItemLevel.Items.Add(i.ToString());
            c_ItemLevel.SelectedIndex = 0;

            foreach (var t in _itemTypes) c_ItemType.Items.Add(t);
            c_ItemType.SelectedIndex = 0;
        }

        async void OnSearch(object sender, RoutedEventArgs e)
        {
            string sql = "SELECT id,level,category,sub_category,type,price,max_stack,name,description,manufacturer " +
                         "FROM item_base WHERE ";
            var keys = new List<string>();
            var vals = new List<string>();
            bool any = false;

            if (!string.IsNullOrEmpty(c_ItemName.Text))
            {
                sql += "name LIKE ?nm";
                keys.Add("nm"); vals.Add("%" + c_ItemName.Text + "%");
                any = true;
            }
            if (c_ItemLevel.SelectedIndex > 0)
            {
                if (any) sql += " AND ";
                sql += "level = ?lvl";
                keys.Add("lvl"); vals.Add(c_ItemLevel.SelectedIndex.ToString());
                any = true;
            }
            if (c_ItemType.SelectedIndex > 0)
            {
                if (any) sql += " AND ";
                sql += "type = ?ty";
                keys.Add("ty"); vals.Add((c_ItemType.SelectedIndex - 1).ToString());
                any = true;
            }

            if (!any)
            {
                await Show("Error", "You need to specify at least one criterion to search on!", MsBoxIcon.Error);
                return;
            }

            DataTable dt;
            try
            {
                dt = DB.Instance.executeQuery(sql, keys.ToArray(), vals.ToArray());
            }
            catch (Exception ex)
            {
                await Show("Search failed", ex.Message, MsBoxIcon.Error);
                return;
            }

            c_Grid.ItemsSource = dt?.DefaultView;
            c_RowCount.Text = "Results: " + (dt?.Rows.Count ?? 0);
        }

        void OnAdd(object sender, RoutedEventArgs e)
        {
            if (c_Grid.SelectedItem is DataRowView row)
            {
                SelectedItemBaseId = Convert.ToInt32(row.Row["id"]);
                c_Desc.Text = row.Row["description"]?.ToString() ?? "";
                Close();
            }
        }

        void OnCancel(object sender, RoutedEventArgs e)
        {
            SelectedItemBaseId = -1;
            Close();
        }

        async System.Threading.Tasks.Task Show(string title, string body, MsBoxIcon icon)
        {
            var box = MessageBoxManager.GetMessageBoxStandard(title, body, ButtonEnum.Ok, icon);
            await box.ShowWindowDialogAsync(this);
        }
    }
}
