using System;
using System.Collections.Generic;
using System.Data;
using Avalonia.Controls;
using Avalonia.Interactivity;
using CommonTools.Database;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;
// Window.Icon (WindowIcon) collides with MsBox.Avalonia.Enums.Icon.
using MsBoxIcon = MsBox.Avalonia.Enums.Icon;

namespace EffectEditorAvalonia
{
    // Port of tools/effect-editor/SQLBind/Form1.cs.
    //
    // Behaviour preserved:
    //  - SELECT/UPDATE/INSERT against the same item_effect_base columns
    //  - Flag1 bit-packing (TFriend<<4, TEnemy<<5, TGroupM<<6) and
    //    Flag2 RequireT pair (bit0 set + bit1 cleared when checked,
    //    inverse when unchecked — matches Form1.Save_Click verbatim).
    //  - INSERT NEW row uses the exact column list + defaults the
    //    original used (NO_STAT/BUFF_NONE/zeros), then LoadEffect on
    //    LAST_INSERT_ID().
    //  - Variable-type combo content (Not Used / Increase Value /
    //    Increase Percent / Decrease Value / Decrease Percent /
    //    Duration) preserved verbatim.
    //
    // Mechanical changes:
    //  - All SQL routed through commontools-avalonia's parameterised
    //    DB.Instance.executeQuery / executeCommand. The original built
    //    SQL by string-concat (textbook injection) — those exact strings
    //    are now parameter placeholders. Same wire semantics, no
    //    injection surface.
    //  - WinForms TFriend.Checked etc. → CheckBox.IsChecked == true
    //  - ComboBox.SelectedIndex semantics preserved (used as both
    //    code value AND lookup index).
    //  - Search dialog → EffectSearchWindow modal; Edit Items dialog
    //    → EditItemWindow modal.
    public partial class MainWindow : Window
    {
        int _effectId;
        EditItemWindow _itemEditor;

        public MainWindow()
        {
            InitializeComponent();

            // Effect type — preserved verbatim from Form1.Designer.cs.
            c_EffectTypeCbo.Items.Add("Equipable (0)");
            c_EffectTypeCbo.Items.Add("Activatable (1)");
            c_EffectTypeCbo.SelectedIndex = 0;
            c_EffectTypeCbo.SelectionChanged += (_, _) =>
            {
                c_TargetTypeGrp.IsEnabled = c_EffectTypeCbo.SelectedIndex != 0;
            };

            try
            {
                FillStats(c_VarStat1);
                FillStats(c_VarStat2);
                FillStats(c_VarStat3);
                FillStats(c_ConstStat1);
                FillStats(c_ConstStat2);

                FillVarTypes(c_VarType1);
                FillVarTypes(c_VarType2);
                FillVarTypes(c_VarType3);
                FillVarTypes(c_ConstType1);
                FillVarTypes(c_ConstType2);

                FillBuffs(c_EffectBuff);

                SelectFirst(c_VarStat1); SelectFirst(c_VarStat2); SelectFirst(c_VarStat3);
                SelectFirst(c_ConstStat1); SelectFirst(c_ConstStat2);
                SelectFirst(c_VarType1); SelectFirst(c_VarType2); SelectFirst(c_VarType3);
                SelectFirst(c_ConstType1); SelectFirst(c_ConstType2);
            }
            catch (Exception ex)
            {
                // Headless smoke ctor — no DB available. Don't crash.
                c_Status.Text = "Init: " + ex.Message;
            }
        }

        static void SelectFirst(ComboBox cbo)
        {
            if (cbo.ItemCount > 0) cbo.SelectedIndex = 0;
        }

        static void FillVarTypes(ComboBox cbo)
        {
            cbo.Items.Clear();
            cbo.Items.Add(CodeValue.Formatted(0, "Not Used"));
            cbo.Items.Add(CodeValue.Formatted(1, "Increase Value"));
            cbo.Items.Add(CodeValue.Formatted(2, "Increase Percent"));
            cbo.Items.Add(CodeValue.Formatted(3, "Decrease Value"));
            cbo.Items.Add(CodeValue.Formatted(4, "Decrease Percent"));
            cbo.Items.Add(CodeValue.Formatted(5, "Duration"));
        }

        static void FillStats(ComboBox cbo)
        {
            cbo.Items.Clear();
            var dt = DB.Instance.executeQuery("SELECT Stat_Name FROM item_effect_stats", null, null);
            if (dt == null) return;
            foreach (DataRow r in dt.Rows)
                cbo.Items.Add(r["Stat_Name"].ToString());
        }

        static void FillBuffs(ComboBox cbo)
        {
            cbo.Items.Clear();
            var dt = DB.Instance.executeQuery("SELECT buff_name FROM buffs", null, null);
            if (dt == null) return;
            foreach (DataRow r in dt.Rows)
                cbo.Items.Add(r["buff_name"].ToString());
        }

        // Port of Form1.LoadEffect(int).
        void LoadEffect(int id)
        {
            _effectId = id;
            var dt = DB.Instance.executeQuery(
                "SELECT EffectType,Name,Description,Tooltip,flag1,flag2," +
                "Constant1Value,Constant1Stat,Constant1Type," +
                "Constant2Value,Constant2Stat,Constant2Type," +
                "Var1Stat,Var1Type,Var2Stat,Var2Type,Var3Stat,Var3Type," +
                "VisualEffect,Buff_Name " +
                "FROM item_effect_base WHERE EffectID = ?eid",
                new[] { "eid" }, new[] { id.ToString() });
            if (dt == null || dt.Rows.Count == 0) return;
            DataRow r = dt.Rows[0];

            c_EffectTypeCbo.SelectedIndex = int.Parse(r["EffectType"].ToString());
            c_EffectName.Text     = r["Name"]?.ToString() ?? "";
            c_EffectToolTip.Text  = r["Tooltip"]?.ToString() ?? "";
            c_EffectDesc.Text     = r["Description"]?.ToString() ?? "";

            int flag1 = ToInt(r["flag1"]);
            int flag2 = ToInt(r["flag2"]);
            c_TFriend.IsChecked  = (flag1 & (1 << 4)) > 0;
            c_TEnemy.IsChecked   = (flag1 & (1 << 5)) > 0;
            c_TGroupM.IsChecked  = (flag1 & (1 << 6)) > 0;
            c_RequireT.IsChecked = (flag2 & 1) > 0;

            c_EffectBuff.SelectedIndex = c_EffectBuff.Items.IndexOf(r["Buff_Name"]?.ToString() ?? "");
            c_VisualEffect.Text        = r["VisualEffect"]?.ToString() ?? "0";

            c_VarStat1.SelectedIndex   = c_VarStat1.Items.IndexOf(r["Var1Stat"]?.ToString() ?? "");
            c_VarStat2.SelectedIndex   = c_VarStat2.Items.IndexOf(r["Var2Stat"]?.ToString() ?? "");
            c_VarStat3.SelectedIndex   = c_VarStat3.Items.IndexOf(r["Var3Stat"]?.ToString() ?? "");
            c_ConstStat1.SelectedIndex = c_ConstStat1.Items.IndexOf(r["Constant1Stat"]?.ToString() ?? "");
            c_ConstStat2.SelectedIndex = c_ConstStat2.Items.IndexOf(r["Constant2Stat"]?.ToString() ?? "");

            c_VarType1.SelectedIndex   = int.Parse(r["Var1Type"].ToString());
            c_VarType2.SelectedIndex   = int.Parse(r["Var2Type"].ToString());
            c_VarType3.SelectedIndex   = int.Parse(r["Var3Type"].ToString());
            c_ConstType1.SelectedIndex = int.Parse(r["Constant1Type"].ToString());
            c_ConstType2.SelectedIndex = int.Parse(r["Constant2Type"].ToString());

            c_ConstValue1.Text = r["Constant1Value"]?.ToString() ?? "";
            c_ConstValue2.Text = r["Constant2Value"]?.ToString() ?? "";

            c_Status.Text = $"Loaded EffectID {id}";
        }

        static int ToInt(object o)
        {
            if (o == null || o == DBNull.Value) return 0;
            return Convert.ToInt32(o);
        }

        async void OnSearch(object sender, RoutedEventArgs e)
        {
            var dlg = new EffectSearchWindow();
            await dlg.ShowDialog(this);
            if (dlg.SelectedEffectId >= 0)
                LoadEffect(dlg.SelectedEffectId);
        }

        async void OnSave(object sender, RoutedEventArgs e)
        {
            if (_effectId == 0)
            {
                await Show("No effect loaded", "Load or create an effect first.", MsBoxIcon.Warning);
                return;
            }

            int flag1 = 0, flag2 = 0;
            if (c_TFriend.IsChecked == true) flag1 |= 1 << 4;
            if (c_TEnemy.IsChecked  == true) flag1 |= 1 << 5;
            if (c_TGroupM.IsChecked == true) flag1 |= 1 << 6;

            // Verbatim from Form1.Save_Click — same surprising "both bits"
            // encoding when checked vs. unchecked.
            if (c_RequireT.IsChecked == true) flag2 |= 1;
            else                              flag2 |= 1 << 1;

            if (string.IsNullOrEmpty(c_VisualEffect.Text)) c_VisualEffect.Text = "0";

            string sql =
                "UPDATE item_effect_base SET " +
                "EffectType=?et,Name=?nm,Tooltip=?tt,Description=?ds," +
                "Flag1=?f1,Flag2=?f2,Buff_Name=?bn,VisualEffect=?ve," +
                "Var1Stat=?vs1,Var2Stat=?vs2,Var3Stat=?vs3," +
                "Constant1Stat=?cs1,Constant2Stat=?cs2," +
                "Var1Type=?vt1,Var2Type=?vt2,Var3Type=?vt3," +
                "Constant1Type=?ct1,Constant2Type=?ct2," +
                "Constant1Value=?cv1,Constant2Value=?cv2 " +
                "WHERE EffectID=?eid";

            string[] keys = {
                "et","nm","tt","ds","f1","f2","bn","ve",
                "vs1","vs2","vs3","cs1","cs2",
                "vt1","vt2","vt3","ct1","ct2",
                "cv1","cv2","eid",
            };
            string[] vals = {
                c_EffectTypeCbo.SelectedIndex.ToString(),
                c_EffectName.Text ?? "", c_EffectToolTip.Text ?? "", c_EffectDesc.Text ?? "",
                flag1.ToString(), flag2.ToString(),
                c_EffectBuff.SelectedItem?.ToString() ?? "BUFF_NONE",
                c_VisualEffect.Text ?? "0",
                c_VarStat1.SelectedItem?.ToString() ?? "NO_STAT",
                c_VarStat2.SelectedItem?.ToString() ?? "NO_STAT",
                c_VarStat3.SelectedItem?.ToString() ?? "NO_STAT",
                c_ConstStat1.SelectedItem?.ToString() ?? "NO_STAT",
                c_ConstStat2.SelectedItem?.ToString() ?? "NO_STAT",
                c_VarType1.SelectedIndex.ToString(),
                c_VarType2.SelectedIndex.ToString(),
                c_VarType3.SelectedIndex.ToString(),
                c_ConstType1.SelectedIndex.ToString(),
                c_ConstType2.SelectedIndex.ToString(),
                c_ConstValue1.Text ?? "0", c_ConstValue2.Text ?? "0",
                _effectId.ToString(),
            };

            int n = DB.Instance.executeCommand(sql, keys, vals);
            c_Status.Text = $"Saved EffectID {_effectId} ({n} row{(n == 1 ? "" : "s")})";
        }

        async void OnNew(object sender, RoutedEventArgs e)
        {
            // Column list + defaults verbatim from Form1.NewEffect_Click.
            const string insert =
                "INSERT INTO item_effect_base (" +
                "EffectType,Name,Description,Tooltip,flag1,flag2," +
                "Constant1Value,Constant1Stat,Constant1Type," +
                "Constant2Value,Constant2Stat,Constant2Type," +
                "Var1Stat,Var1Type,Var2Stat,Var2Type,Var3Stat,Var3Type,Buff_Name) " +
                "VALUES (0,'none','none','none',0,0," +
                "0,'NO_STAT',0,0,'NO_STAT',0,'NO_STAT',0,'NO_STAT',0,'NO_STAT',0,'BUFF_NONE')";
            DB.Instance.executeCommand(insert, null, null);

            var dt = DB.Instance.executeQuery("SELECT LAST_INSERT_ID()", null, null);
            if (dt != null && dt.Rows.Count > 0)
                LoadEffect(Convert.ToInt32(dt.Rows[0][0]));
            else
                await Show("Insert failed", "Could not retrieve new EffectID.", MsBoxIcon.Error);
        }

        async void OnEditItems(object sender, RoutedEventArgs e)
        {
            _itemEditor ??= new EditItemWindow();
            await _itemEditor.ShowDialog(this);
        }

        async System.Threading.Tasks.Task Show(string title, string body, MsBoxIcon icon)
        {
            var box = MessageBoxManager.GetMessageBoxStandard(title, body, ButtonEnum.Ok, icon);
            await box.ShowWindowDialogAsync(this);
        }
    }
}
