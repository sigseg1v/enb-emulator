using System;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace CommonTools.Gui
{
    // Avalonia port of the WinForms TableButtonHandler. The WinForms
    // version coupled ListView + 5 Buttons; here we use ListBox (its
    // SelectedItems is List-based, matching the move-up/move-down logic
    // more naturally than a DataGrid).
    public class TableButtonHandler
    {
        readonly ListBox table;
        readonly Button add;
        readonly Button delete;
        readonly Button edit;
        readonly Button up;
        readonly Button down;

        public TableButtonHandler(ListBox table, Button add, Button delete, Button edit, Button up, Button down)
        {
            this.table = table;
            this.add = add;
            this.delete = delete;
            this.edit = edit;
            this.up = up;
            this.down = down;

            table.SelectionChanged += OnSelectionChanged;
            up.Click += OnMoveUp;
            down.Click += OnMoveDown;
        }

        void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            int count = table.SelectedItems != null ? table.SelectedItems.Count : 0;
            if (delete != null) delete.IsEnabled = count != 0;
            if (edit   != null) edit.IsEnabled   = false;
            if (up     != null) up.IsEnabled     = count == 1 && table.SelectedIndex > 0;
            if (down   != null) down.IsEnabled   = count == 1 && table.SelectedIndex < (table.ItemCount - 1);
        }

        void OnMoveUp(object sender, RoutedEventArgs e)
        {
            int index = table.SelectedIndex;
            if (index <= 0) return;
            var item = table.Items[index];
            table.Items.RemoveAt(index);
            table.Items.Insert(index - 1, item);
            table.SelectedIndex = index - 1;
        }

        void OnMoveDown(object sender, RoutedEventArgs e)
        {
            int index = table.SelectedIndex;
            if (index < 0 || index >= table.ItemCount - 1) return;
            var item = table.Items[index];
            table.Items.RemoveAt(index);
            table.Items.Insert(index + 1, item);
            table.SelectedIndex = index + 1;
        }
    }
}
