using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using CommonTools.Database;
using MsBox.Avalonia;
using MsBoxIcon = MsBox.Avalonia.Enums.Icon;

namespace StationToolsAvalonia
{
    // Avalonia port of tools/station-tools/VenderTab.cs.
    // Manages starbase_vender_groups (group metadata) and
    // starbase_vender_inventory (per-group item list). Behaviour is the
    // same as the original; SQL is parameterised through DB.Instance.
    public partial class VenderTabControl : UserControl
    {
        struct VenderGroup { public int GroupID; public string GroupName; }

        readonly List<VenderGroup> VGroups = new();
        int VenderGroupID = 0;
        int CurrentEditingItem = -1;
        bool VenderItemUpdate = false;

        DataTable _itemsTable;

        public VenderTabControl()
        {
            InitializeComponent();
            try { SqlLoadVenders(); }
            catch (Exception ex) { Console.Error.WriteLine("VenderTab DB load failed: " + ex.Message); }
        }

        void SqlLoadVenders()
        {
            var dt = DB.Instance.executeQuery(
                "SELECT `GroupID`, `GroupName` FROM `starbase_vender_groups`",
                new string[0], new string[0]);

            c_SelectGroup.Items.Clear();
            VGroups.Clear();

            if (dt == null) return;

            foreach (DataRow r in dt.Rows)
            {
                VGroups.Add(new VenderGroup
                {
                    GroupID = Convert.ToInt32(r["GroupID"]),
                    GroupName = r["GroupName"].ToString() ?? ""
                });
                c_SelectGroup.Items.Add(r["GroupName"].ToString());
            }
        }

        bool ValidateItem()
        {
            if (string.IsNullOrEmpty(c_NewItemID.Text))
            {
                Error("You need to fill in an Item ID");
                return false;
            }
            if (string.IsNullOrEmpty(c_Quanity.Text))   c_Quanity.Text   = "-1";
            if (string.IsNullOrEmpty(c_SellPrice.Text)) c_SellPrice.Text = "-1";
            if (string.IsNullOrEmpty(c_BuyPrice.Text))  c_BuyPrice.Text  = "-1";
            return true;
        }

        void OnSaveItem(object sender, RoutedEventArgs e)
        {
            if (!ValidateItem()) return;
            SaveItems();
        }

        void SaveItems()
        {
            if (VenderGroupID == 0) { Error("You must select a group!"); return; }
            if (CurrentEditingItem <= 0) return;

            DB.Instance.executeCommand(
                "UPDATE `starbase_vender_inventory` SET `itemid` = @i, `sell_price` = @s, `buy_price` = @b, `quanity` = @q WHERE `id` = @id",
                new[] { "@i", "@s", "@b", "@q", "@id" },
                new[] { c_NewItemID.Text, c_SellPrice.Text, c_BuyPrice.Text, c_Quanity.Text, CurrentEditingItem.ToString() });

            VenderItemUpdate = false;
            LoadGroupItems();
        }

        void OnEditItem(object sender, RoutedEventArgs e)
        {
            if (VenderGroupID == 0) return;
            if (c_ItemLists.SelectedItem is not DataRowView drv) return;

            CurrentEditingItem = Convert.ToInt32(drv.Row["ID"]);
            c_NewItemID.Text   = drv.Row["ItemID"].ToString();
            c_SellPrice.Text   = drv.Row["SellPrice"].ToString();
            c_BuyPrice.Text    = drv.Row["BuyPrice"].ToString();
            c_Quanity.Text     = drv.Row["Qty"].ToString();
            VenderItemUpdate = false;
        }

        void OnDelItem(object sender, RoutedEventArgs e)
        {
            if (VenderGroupID == 0) return;
            if (c_ItemLists.SelectedItems == null || c_ItemLists.SelectedItems.Count == 0) return;

            foreach (var sel in new List<object>(c_ItemLists.SelectedItems.Cast<object>()))
            {
                if (sel is DataRowView drv)
                {
                    DB.Instance.executeCommand(
                        "DELETE FROM `starbase_vender_inventory` WHERE `id` = @id",
                        new[] { "@id" }, new[] { drv.Row["ID"].ToString() });
                }
            }
            LoadGroupItems();
        }

        void OnNewItem(object sender, RoutedEventArgs e)
        {
            if (VenderGroupID == 0) { Error("You must select a group!"); return; }

            DB.Instance.executeCommand(
                "INSERT INTO `starbase_vender_inventory` (`groupid`, `itemid`, `sell_price`, `buy_price`, `quanity`) VALUES (@g, '0', '0', '0', '0')",
                new[] { "@g" }, new[] { VenderGroupID.ToString() });

            LoadGroupItems();
        }

        void OnMultiAdd(object sender, RoutedEventArgs e)
        {
            if (VenderGroupID == 0) { Error("You must select a group!"); return; }

            var browse = new ItemBrowseWindow();
            browse.SetMultiSelect(true);
            var owner = TopLevel.GetTopLevel(this) as Window;
            if (owner == null) return;

            browse.ShowDialog(owner).ContinueWith(_ =>
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    int n = browse.GetNumSelected();
                    for (int i = 0; i < n; i++)
                    {
                        int itemId = browse.GetSelectedItemID(i);
                        DB.Instance.executeCommand(
                            "INSERT INTO `starbase_vender_inventory` (`groupid`, `itemid`, `sell_price`, `buy_price`, `quanity`) VALUES (@g, @i, '0', '0', '-1')",
                            new[] { "@g", "@i" }, new[] { VenderGroupID.ToString(), itemId.ToString() });
                    }
                    LoadGroupItems();
                });
            });
        }

        void OnBrowseItems(object sender, RoutedEventArgs e)
        {
            var browse = new ItemBrowseWindow();
            browse.SetMultiSelect(false);
            var owner = TopLevel.GetTopLevel(this) as Window;
            if (owner == null) return;

            browse.ShowDialog(owner).ContinueWith(_ =>
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    int id = browse.GetItemBase();
                    if (id > 0) c_NewItemID.Text = id.ToString();
                });
            });
        }

        void OnUnlimitedChanged(object sender, RoutedEventArgs e)
        {
            c_Quanity.IsEnabled = !(c_QuanityUnlimited.IsChecked ?? false);
            c_Quanity.Text = (c_QuanityUnlimited.IsChecked ?? false) ? "-1" : "0";
        }

        void OnItemUpdate(object sender, TextChangedEventArgs e)
        {
            if (VenderGroupID == 0) return;
            VenderItemUpdate = true;
        }

        void OnAddGroup(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(c_GroupName.Text)) { Error("You must specify a group name!"); return; }
            if (string.IsNullOrEmpty(c_SellMult.Text))  c_SellMult.Text = "0";
            if (string.IsNullOrEmpty(c_BuyMult.Text))   c_BuyMult.Text  = "0";

            DB.Instance.executeCommand(
                "INSERT INTO `starbase_vender_groups` (`GroupName`, `SellMultiplyer`, `BuyMultiplyer`, `BuyOnlyList`) VALUES (@n, @s, @b, @o)",
                new[] { "@n", "@s", "@b", "@o" },
                new[] { c_GroupName.Text, c_SellMult.Text, c_BuyMult.Text, (c_BuyList.IsChecked ?? false) ? "1" : "0" });

            SqlLoadVenders();
        }

        void OnUpdateName(object sender, RoutedEventArgs e)
        {
            if (VenderGroupID == 0) return;
            if (string.IsNullOrEmpty(c_GroupName.Text)) { Error("You must specify a group name!"); return; }
            if (string.IsNullOrEmpty(c_SellMult.Text))  c_SellMult.Text = "0";
            if (string.IsNullOrEmpty(c_BuyMult.Text))   c_BuyMult.Text  = "0";

            DB.Instance.executeCommand(
                "UPDATE `starbase_vender_groups` SET `GroupName` = @n, `SellMultiplyer` = @s, `BuyMultiplyer` = @b, `BuyOnlyList` = @o WHERE `GroupID` = @id",
                new[] { "@n", "@s", "@b", "@o", "@id" },
                new[] { c_GroupName.Text, c_SellMult.Text, c_BuyMult.Text, (c_BuyList.IsChecked ?? false) ? "1" : "0", VenderGroupID.ToString() });

            SqlLoadVenders();
        }

        void OnDeleteGroup(object sender, RoutedEventArgs e)
        {
            if (VenderGroupID == 0) return;
            if (c_SelectGroup.SelectedIndex < 0) return;

            int gid = VGroups[c_SelectGroup.SelectedIndex].GroupID;

            DB.Instance.executeCommand("DELETE FROM `starbase_vender_groups`    WHERE `GroupID` = @g",
                new[] { "@g" }, new[] { gid.ToString() });
            DB.Instance.executeCommand("DELETE FROM `starbase_vender_inventory` WHERE `groupid` = @g",
                new[] { "@g" }, new[] { gid.ToString() });
            DB.Instance.executeCommand("UPDATE `starbase_vendors` SET `groupid` = '-1' WHERE `groupid` = @g",
                new[] { "@g" }, new[] { gid.ToString() });

            SqlLoadVenders();
        }

        void OnLoadGroup(object sender, RoutedEventArgs e)
        {
            if (c_SelectGroup.SelectedIndex < 0) return;
            VenderGroupID = VGroups[c_SelectGroup.SelectedIndex].GroupID;

            var grp = DB.Instance.executeQuery(
                "SELECT `GroupName`, `SellMultiplyer`, `BuyMultiplyer`, `BuyOnlyList` FROM `starbase_vender_groups` WHERE `GroupID` = @g",
                new[] { "@g" }, new[] { VenderGroupID.ToString() });

            if (grp != null && grp.Rows.Count > 0)
            {
                var r = grp.Rows[0];
                c_GroupName.Text = r["GroupName"].ToString();
                c_SellMult.Text  = r["SellMultiplyer"].ToString();
                c_BuyMult.Text   = r["BuyMultiplyer"].ToString();
                c_BuyList.IsChecked = Convert.ToInt32(r["BuyOnlyList"]) == 1;
            }

            LoadGroupItems();
        }

        void LoadGroupItems()
        {
            if (VenderGroupID == 0) { c_ItemLists.ItemsSource = null; return; }

            _itemsTable = DB.Instance.executeQuery(
                "SELECT `starbase_vender_inventory`.`id` AS ID, " +
                "       `item_base`.`name`              AS Name, " +
                "       `itemid`                        AS ItemID, " +
                "       `sell_price`                    AS SellPrice, " +
                "       `buy_price`                     AS BuyPrice, " +
                "       `quanity`                       AS Qty " +
                "FROM   `starbase_vender_inventory` " +
                "INNER JOIN `item_base` ON `starbase_vender_inventory`.`itemid` = `item_base`.`id` " +
                "WHERE  `groupid` = @g",
                new[] { "@g" }, new[] { VenderGroupID.ToString() });

            c_ItemLists.ItemsSource = _itemsTable?.DefaultView;
        }

        void OnReloadGroup(object sender, RoutedEventArgs e)
        {
            VenderGroupID = 0;
            CurrentEditingItem = -1;
            VenderItemUpdate = false;
            c_ItemLists.ItemsSource = null;
            SqlLoadVenders();
        }

        static void Error(string msg)
        {
            try { MessageBoxManager.GetMessageBoxStandard("Vender", msg, MsBox.Avalonia.Enums.ButtonEnum.Ok, MsBoxIcon.Error).ShowAsync(); }
            catch { Console.Error.WriteLine("VENDER ERROR: " + msg); }
        }
    }
}
