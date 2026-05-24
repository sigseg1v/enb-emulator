// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Data;
using Avalonia.Controls;

namespace SectorEditorAvalonia.Utilities
{
    // Backs the SectorWindow canvas operations with the MainWindow's
    // Objects-tab DataGrid. The original used a DataGridView bound to
    // the sector_objects DataTable; here we keep the same shape — one
    // DataTable, the DataGrid's ItemsSource is its DefaultView — so
    // sprite-emitted Append/Remove/Select calls land on the same rows
    // the user is editing in the grid.
    public sealed class DataGridSyncSink : IGridSyncSink
    {
        private readonly DataGrid _grid;
        private readonly DataTable _table;

        public DataGridSyncSink(DataGrid grid, DataTable table)
        {
            _grid = grid;
            _table = table;
            _grid.ItemsSource = _table.DefaultView;
        }

        public void OnCellChanged(string columnName, object newValue)
        {
            // Sprite-side edits already wrote into the underlying DataRow;
            // the DataView surfaces the change automatically. Nothing to do.
        }

        public void RemoveRowById(int id)
        {
            for (int i = 0; i < _table.Rows.Count; i++)
            {
                if (RowMatches(_table.Rows[i], id)) { _table.Rows.RemoveAt(i); return; }
            }
        }

        public void AppendRow(int sectorObjectId, string name, int baseAssetId, int type)
        {
            var r = _table.NewRow();
            r["sector_object_id"] = sectorObjectId;
            r["name"] = name;
            r["base_asset_id"] = baseAssetId;
            r["type"] = type;
            _table.Rows.Add(r);
        }

        public void SelectRowById(int id)
        {
            for (int i = 0; i < _table.Rows.Count; i++)
            {
                if (RowMatches(_table.Rows[i], id)) { _grid.SelectedIndex = i; return; }
            }
        }

        private static bool RowMatches(DataRow r, int id)
        {
            if (!r.Table.Columns.Contains("sector_object_id")) return false;
            var v = r["sector_object_id"];
            return v != null && v != System.DBNull.Value && System.Convert.ToInt32(v) == id;
        }
    }
}
