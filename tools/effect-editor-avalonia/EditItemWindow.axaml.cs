using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using CommonTools.Database;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;
using MsBoxIcon = MsBox.Avalonia.Enums.Icon;

namespace EffectEditorAvalonia
{
    // Port of tools/effect-editor/SQLBind/EditItem.cs.
    //
    // What this window does, preserved from the original:
    //  - Browse / load an item from item_base by id.
    //  - For each of (up to) 5 item_effects rows attached to that item,
    //    show an effect-name combo + 3 var textboxes + a computed string
    //    that mirrors the tooltip with %valueN.Mf% printf placeholders
    //    replaced by formatted numeric values.
    //  - On save: for each of 5 slots, INSERT a new item_effects row,
    //    UPDATE the existing one, DELETE it (if the combo was reset),
    //    or no-op. Then INSERT or UPDATE the item_effect_container row.
    //
    // Mechanical changes:
    //  - 5 rows are constructed programmatically (the original used
    //    5 hand-laid-out copies in the Designer); we keep an EffectSlot
    //    helper per row.
    //  - %valueN.Mf% printf-style format-string parser is ported verbatim
    //    from EditItem.DisplayString. It's quirky (relies on a closing
    //    "f%" sentinel rather than a regex), so the exact loop shape is
    //    preserved to keep behaviour identical.
    //  - All SQL parameterised through DB.Instance.
    public partial class EditItemWindow : Window
    {
        struct EffectRow
        {
            public int EffectId;
            public string Desc;
            public string Tooltip;
        }

        sealed class EffectSlot
        {
            public ComboBox  EffectBox;
            public TextBox   Var1, Var2, Var3;
            public TextBlock EffectString;
            // ItemEffectID column value: 0 → none yet, !=0 → existing row.
            public int CurrentItemEffectId;
            public string FormatString;
        }

        readonly List<EffectRow> _effects = new();
        readonly EffectSlot[] _slots = new EffectSlot[5];
        int _currentItem;
        int _currentContainer;

        public EditItemWindow()
        {
            InitializeComponent();

            for (int i = 0; i < 5; i++) _slots[i] = MakeRow(i);

            // Keep the blocking effect-list query off the UI thread (AC.4) so the
            // close button stays responsive while it runs.
            Opened += async (_, _) => await OnLoadAsync();
        }

        async Task OnLoadAsync()
        {
            try
            {
                await Task.Run(() => LoadEffects());
                FillCombos();
            }
            catch (Exception ex)
            {
                c_Status.Text = "Init: " + ex.Message;
            }
        }

        EffectSlot MakeRow(int idx)
        {
            var slot = new EffectSlot();
            var grid = new Grid
            {
                ColumnDefinitions = ColumnDefinitions.Parse("60,*,8,60,8,60,8,60,8,*"),
                RowDefinitions    = RowDefinitions.Parse("Auto,Auto"),
                Margin            = new Avalonia.Thickness(0, 0, 0, 6),
            };
            Add(grid, new TextBlock { Text = $"Effect {idx + 1}:", VerticalAlignment = VerticalAlignment.Center }, 0, 0);
            slot.EffectBox = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
            Add(grid, slot.EffectBox, 1, 0);

            slot.Var1 = new TextBox(); Add(grid, slot.Var1, 3, 0);
            slot.Var2 = new TextBox(); Add(grid, slot.Var2, 5, 0);
            slot.Var3 = new TextBox(); Add(grid, slot.Var3, 7, 0);
            slot.EffectString = new TextBlock
            {
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping      = Avalonia.Media.TextWrapping.Wrap,
                Foreground        = Avalonia.Media.Brushes.DimGray,
            };
            Grid.SetRow(slot.EffectString, 1);
            Grid.SetColumn(slot.EffectString, 1);
            Grid.SetColumnSpan(slot.EffectString, 9);
            grid.Children.Add(slot.EffectString);

            slot.EffectBox.SelectionChanged += (_, _) => OnEffectChanged(idx);
            slot.Var1.TextChanged += (_, _) => UpdateString(idx);
            slot.Var2.TextChanged += (_, _) => UpdateString(idx);
            slot.Var3.TextChanged += (_, _) => UpdateString(idx);

            c_EffectStack.Children.Add(grid);
            return slot;
        }

        static void Add(Grid g, Control c, int col, int row)
        {
            Grid.SetColumn(c, col); Grid.SetRow(c, row); g.Children.Add(c);
        }

        void LoadEffects()
        {
            _effects.Clear();
            // Match the original's column list exactly.
            var dt = DB.Instance.executeQuery(
                "SELECT \"Description\",\"Tooltip\",\"Constant1Value\",\"Constant2Value\",\"EffectID\" FROM item_effect_base",
                null, null);
            if (dt == null) return;

            // The original prepends nothing; index 0 is the first real row.
            // We need a "(none)" sentinel at index 0 so combo SelectedIndex==0
            // means "delete this slot". Add it explicitly here.
            _effects.Add(new EffectRow { EffectId = -1, Desc = "(none)", Tooltip = "" });
            foreach (DataRow r in dt.Rows)
            {
                _effects.Add(new EffectRow
                {
                    EffectId = Convert.ToInt32(r["EffectID"]),
                    Desc     = r["Description"]?.ToString() ?? "",
                    Tooltip  = r["Tooltip"]?.ToString() ?? "",
                });
            }
        }

        void FillCombos()
        {
            foreach (var slot in _slots)
            {
                slot.EffectBox.Items.Clear();
                foreach (var e in _effects)
                    slot.EffectBox.Items.Add(e.Desc);
                slot.EffectBox.SelectedIndex = 0;
            }
        }

        int FindEffectIndex(int effectId)
        {
            for (int i = 0; i < _effects.Count; i++)
                if (_effects[i].EffectId == effectId) return i;
            return 0;
        }

        async void OnBrowse(object sender, RoutedEventArgs e)
        {
            var dlg = new ItemBrowseWindow();
            await dlg.ShowDialog(this);
            if (dlg.SelectedItemBaseId > 0)
            {
                _currentItem = dlg.SelectedItemBaseId;
                c_ItemIdBox.Text = _currentItem.ToString();
                await LoadItemDataAsync();
            }
        }

        async void OnLoad(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(c_ItemIdBox.Text, out _currentItem)) return;
            await LoadItemDataAsync();
        }

        void OnClose(object sender, RoutedEventArgs e) => Close();

        async Task LoadItemDataAsync()
        {
            // _currentItem is a field (not a control); capture it for the
            // background queries, then apply the results back to controls after
            // the await resumes on the UI context (AC.4).
            string id = _currentItem.ToString();
            DataTable nameDt = null, eff = null, con = null;
            await Task.Run(() =>
            {
                // Item name lookup.
                nameDt = DB.Instance.executeQuery(
                    "SELECT name FROM item_base WHERE id = @id",
                    new[] { "id" }, new[] { id });

                eff = DB.Instance.executeQuery(
                    "SELECT \"ItemEffectID\",\"item_effect_base_id\",\"Var1Data\",\"Var2Data\",\"Var3Data\" FROM item_effects WHERE \"ItemID\" = @id",
                    new[] { "id" }, new[] { id });

                con = DB.Instance.executeQuery(
                    "SELECT \"EffectContainerID\",\"RechargeTime\",\"_Range\",\"EnergyUse\" " +
                    // EquipEffect is bytea (MySQL binary(1)); it stores the ASCII
                    // byte '1' (0x31) / '0' (0x30), never an integer. Compare with
                    // the string literal '1' (coerces to bytea 0x31) -- "= 1"
                    // throws "operator does not exist: bytea = integer".
                    "FROM item_effect_container WHERE \"EquipEffect\" = '1' AND \"ItemID\" = @id",
                    new[] { "id" }, new[] { id });
            });

            c_ItemName.Text = (nameDt != null && nameDt.Rows.Count > 0)
                ? nameDt.Rows[0][0]?.ToString() ?? ""
                : "(item not found)";

            // Reset slots.
            foreach (var slot in _slots)
            {
                slot.CurrentItemEffectId = 0;
                slot.EffectBox.SelectedIndex = 0;
                slot.Var1.Text = ""; slot.Var2.Text = ""; slot.Var3.Text = "";
            }

            int x = 0;
            if (eff != null)
            {
                foreach (DataRow r in eff.Rows)
                {
                    if (x >= _slots.Length) break;
                    var slot = _slots[x];
                    slot.CurrentItemEffectId = Convert.ToInt32(r["ItemEffectID"]);
                    int effId = Convert.ToInt32(r["item_effect_base_id"]);
                    slot.EffectBox.SelectedIndex = FindEffectIndex(effId);
                    slot.Var1.Text = r["Var1Data"]?.ToString() ?? "";
                    slot.Var2.Text = r["Var2Data"]?.ToString() ?? "";
                    slot.Var3.Text = r["Var3Data"]?.ToString() ?? "";
                    x++;
                }
            }

            _currentContainer = 0;
            if (con != null && con.Rows.Count > 0)
            {
                DataRow r = con.Rows[0];
                _currentContainer  = Convert.ToInt32(r["EffectContainerID"]);
                c_Range.Text       = r["_Range"]?.ToString() ?? "0";
                c_CoolDown.Text    = r["RechargeTime"]?.ToString() ?? "0";
                c_EnergyUse.Text   = r["EnergyUse"]?.ToString() ?? "0";
            }
            else
            {
                c_Range.Text = "0"; c_CoolDown.Text = "0"; c_EnergyUse.Text = "0";
            }

            c_Status.Text = $"Loaded item {_currentItem}";
        }

        // ----------------------------------------------------------------------
        // %valueN.Mf% → {0:000.000} parser, ported verbatim from
        // EditItem.DisplayString. The shape is preserved on purpose; this
        // is the one piece of original logic users rely on for in-tool
        // tooltip preview.
        void UpdateString(int idx) => Render(idx);

        void OnEffectChanged(int idx)
        {
            var slot = _slots[idx];
            if (slot.EffectBox.SelectedIndex < 0) return;
            int realIndex = slot.EffectBox.SelectedIndex;
            if (realIndex >= _effects.Count) return;
            slot.FormatString = null;
            Render(idx);
        }

        void Render(int idx)
        {
            var slot = _slots[idx];
            if (slot.EffectBox.SelectedIndex < 0) return;
            int realIndex = slot.EffectBox.SelectedIndex;
            if (realIndex < 0 || realIndex >= _effects.Count) return;
            if (_effects[realIndex].EffectId == -1)
            {
                slot.Var1.IsEnabled = slot.Var2.IsEnabled = slot.Var3.IsEnabled = false;
                slot.EffectString.Text = "";
                return;
            }

            if (string.IsNullOrEmpty(slot.Var1.Text)) slot.Var1.Text = "0";
            if (string.IsNullOrEmpty(slot.Var2.Text)) slot.Var2.Text = "0";
            if (string.IsNullOrEmpty(slot.Var3.Text)) slot.Var3.Text = "0";

            float v1 = ParseF(slot.Var1.Text);
            float v2 = ParseF(slot.Var2.Text);
            float v3 = ParseF(slot.Var3.Text);

            string data = _effects[realIndex].Tooltip;
            string output = "";
            int varNum = 0;
            for (int x = 0; x < data.Length; x++)
            {
                string formatStr = "";
                if (x + 6 <= data.Length && data.Substring(x, 6) == "%value")
                {
                    x += 6;
                    for (int z = x; z < data.Length - 1; z++)
                    {
                        if (data.Substring(z, 2) == "f%")
                        {
                            formatStr = data.Substring(x, z - x);
                            x += z - x;
                            break;
                        }
                    }
                    for (int y = 0; y < formatStr.Length; y++)
                    {
                        if (formatStr.Substring(y, 1) == ".")
                        {
                            output += "{" + varNum + ":";
                            int before = int.Parse(formatStr.Substring(0, y), CultureInfo.InvariantCulture);
                            int after  = int.Parse(formatStr.Substring(y + 1, formatStr.Length - y - 1), CultureInfo.InvariantCulture);
                            for (int j = 0; j < before; j++) output += "0";
                            output += ".";
                            for (int j = 0; j < after;  j++) output += "0";
                            break;
                        }
                    }
                    output += "}";
                    x += 2;
                    varNum++;
                }
                else
                {
                    output += data.Substring(x, 1);
                }
            }

            slot.FormatString = output;
            slot.Var1.IsEnabled = varNum > 0;
            slot.Var2.IsEnabled = varNum > 1;
            slot.Var3.IsEnabled = varNum > 2;
            try { slot.EffectString.Text = string.Format(CultureInfo.InvariantCulture, output, v1, v2, v3); }
            catch { slot.EffectString.Text = output; }
        }

        static float ParseF(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : 0;
        }

        // ----------------------------------------------------------------------

        async void OnSave(object sender, RoutedEventArgs e)
        {
            if (_currentItem == 0)
            {
                await Show("No item loaded", "Browse or Load an item first.", MsBoxIcon.Warning);
                return;
            }

            for (int x = 0; x < 5; x++) await SaveEffectSlotAsync(x);

            // Read the container controls on the UI thread before the DB write
            // (AC.4) -- Avalonia controls are UI-thread-only.
            string itemId   = _currentItem.ToString();
            string coolDown = c_CoolDown.Text ?? "0";
            string energy   = c_EnergyUse.Text ?? "0";
            string range    = c_Range.Text ?? "0";

            // Container row.
            if (_currentContainer == 0)
            {
                // EffectContainerID is GENERATED BY DEFAULT AS IDENTITY --
                // omit it and read the new id back via RETURNING (executeQuery).
                var idDt = await Task.Run(() => DB.Instance.executeQuery(
                    "INSERT INTO item_effect_container (\"ItemID\",\"RechargeTime\",\"EnergyUse\",\"_Range\",\"EquipEffect\") " +
                    "VALUES (@id,@rt,@eu,@rg,'1') " +
                    "RETURNING \"EffectContainerID\" AS id",
                    new[] { "id", "rt", "eu", "rg" },
                    new[] { itemId, coolDown, energy, range }));
                if (idDt != null && idDt.Rows.Count > 0)
                    _currentContainer = Convert.ToInt32(idDt.Rows[0]["id"]);
            }
            else
            {
                string containerId = _currentContainer.ToString();
                await Task.Run(() => DB.Instance.executeCommand(
                    "UPDATE item_effect_container SET \"RechargeTime\"=@rt,\"EnergyUse\"=@eu,\"_Range\"=@rg,\"EquipEffect\"='1' " +
                    "WHERE \"ItemID\"=@id AND \"EffectContainerID\"=@cid",
                    new[] { "rt", "eu", "rg", "id", "cid" },
                    new[] { coolDown, energy, range, itemId, containerId }));
            }

            c_Status.Text = $"Saved item {_currentItem}.";
        }

        async Task SaveEffectSlotAsync(int slotIndex)
        {
            var slot = _slots[slotIndex];
            int comboIdx = slot.EffectBox.SelectedIndex;
            if (comboIdx < 0) return;

            // DELETE: had a row, combo reset to "(none)".
            if (slot.CurrentItemEffectId != 0 && comboIdx == 0)
            {
                string delEid = slot.CurrentItemEffectId.ToString();
                await Task.Run(() => DB.Instance.executeCommand(
                    "DELETE FROM item_effects WHERE \"ItemEffectID\" = @eid",
                    new[] { "eid" }, new[] { delEid }));
                slot.CurrentItemEffectId = 0;
                return;
            }

            // No-op: combo is "(none)" and was never set.
            if (comboIdx == 0) return;

            if (string.IsNullOrEmpty(slot.Var1.Text)) slot.Var1.Text = "0";
            if (string.IsNullOrEmpty(slot.Var2.Text)) slot.Var2.Text = "0";
            if (string.IsNullOrEmpty(slot.Var3.Text)) slot.Var3.Text = "0";

            int effId = _effects[comboIdx].EffectId;

            // Read every control value on the UI thread before going off-thread
            // for the DB write (AC.4) -- Avalonia controls are UI-thread-only.
            string itemId = _currentItem.ToString();
            string effIdStr = effId.ToString();
            string v1 = slot.Var1.Text, v2 = slot.Var2.Text, v3 = slot.Var3.Text;

            if (slot.CurrentItemEffectId == 0)
            {
                // ItemEffectID is GENERATED BY DEFAULT AS IDENTITY -- omit it
                // and read the new id back via RETURNING (executeQuery).
                var idDt = await Task.Run(() => DB.Instance.executeQuery(
                    "INSERT INTO item_effects (\"ItemID\",\"item_effect_base_id\",\"Var1Data\",\"Var2Data\",\"Var3Data\") " +
                    "VALUES (@id,@eb,@v1,@v2,@v3) " +
                    "RETURNING \"ItemEffectID\" AS id",
                    new[] { "id", "eb", "v1", "v2", "v3" },
                    new[] { itemId, effIdStr, v1, v2, v3 }));
                if (idDt != null && idDt.Rows.Count > 0)
                    slot.CurrentItemEffectId = Convert.ToInt32(idDt.Rows[0]["id"]);
            }
            else
            {
                string curEid = slot.CurrentItemEffectId.ToString();
                await Task.Run(() => DB.Instance.executeCommand(
                    "UPDATE item_effects SET \"item_effect_base_id\"=@eb,\"Var1Data\"=@v1,\"Var2Data\"=@v2,\"Var3Data\"=@v3 " +
                    "WHERE \"ItemID\"=@id AND \"ItemEffectID\"=@eid",
                    new[] { "eb", "v1", "v2", "v3", "id", "eid" },
                    new[] { effIdStr, v1, v2, v3, itemId, curEid }));
            }
        }

        async System.Threading.Tasks.Task Show(string title, string body, MsBoxIcon icon)
        {
            var box = MessageBoxManager.GetMessageBoxStandard(title, body, ButtonEnum.Ok, icon);
            await box.ShowWindowDialogAsync(this);
        }
    }
}
