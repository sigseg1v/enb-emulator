using System.Data;
using Avalonia.Controls;
using Avalonia.Data;

namespace CommonTools.Gui
{
    // Binds a System.Data.DataTable to an Avalonia DataGrid.
    //
    // Avalonia's DataGrid -- unlike the WinForms DataGridView it replaced --
    // has NO native understanding of DataTable/DataView. Setting
    // ItemsSource = table.DefaultView with AutoGenerateColumns=true makes the
    // grid reflect the CLR properties of each DataRowView item
    // (DataView, Item, Item, RowVersion, ...) instead of the table's data
    // columns -- so every cell renders "System.Data.DataView" and the column
    // headers are the DataRowView property names. (DlgSearch already worked
    // around this by hand-building columns; this centralizes that fix so every
    // editor grid gets it.)
    //
    // Bind() forces AutoGenerateColumns off and builds one DataGridTextColumn
    // per data column, bound by plain column name. A DataRowView column is NOT
    // resolvable by Avalonia's default binding accessors (neither "[col]" nor
    // "col" works -- you get the right headers but empty cells), so
    // DataRowViewAccessorPlugin is registered to make the "col" path read
    // drv[col]. ItemsSource stays the DefaultView, so SelectedItem remains a
    // DataRowView for every caller that pattern-matches on it.
    public static class DataGridBinder
    {
        public static void Bind(DataGrid grid, DataTable table)
        {
            DataRowViewAccessorPlugin.Register();

            grid.AutoGenerateColumns = false;
            grid.Columns.Clear();

            if (table == null)
            {
                grid.ItemsSource = null;
                return;
            }

            foreach (DataColumn col in table.Columns)
            {
                grid.Columns.Add(new DataGridTextColumn
                {
                    Header = col.ColumnName,
                    Binding = new Binding(col.ColumnName),
                });
            }

            grid.ItemsSource = table.DefaultView;
        }
    }
}
