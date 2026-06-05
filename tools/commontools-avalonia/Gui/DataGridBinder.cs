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
    // per data column, bound to the DataRowView string indexer "[ColumnName]"
    // (which is what actually resolves to the cell value, and stays two-way for
    // editable grids). ItemsSource is then the DefaultView as before.
    public static class DataGridBinder
    {
        public static void Bind(DataGrid grid, DataTable table)
        {
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
                    // DataRowView string indexer -- this is what resolves to the
                    // cell value (same pattern DlgSearch uses for its results).
                    Binding = new Binding("[" + col.ColumnName + "]"),
                });
            }

            grid.ItemsSource = table.DefaultView;
        }
    }
}
