using System;
using System.Data;
using CommonTools.Database;

namespace ItemEditor.SQL
{
    // Data-access for item_base. Avalonia port of the WinForms item editor's
    // item record manager.
    //
    // Written against Postgres from the start (the older ported DAOs still
    // carry MySQL-isms -- INSERT...SET / LAST_INSERT_ID() / ?n placeholders --
    // and are broken against net7; this one is not):
    //   * every identifier is double-quoted, so reserved-word columns
    //     ("unique", "type") and digit-leading columns ("2d_asset", "3d_asset")
    //     are all valid;
    //   * parameters use Npgsql @name binding (the DB layer adds them by bare
    //     name);
    //   * item_base."id" is a plain bigint PK with NO sequence/identity, so a
    //     new row's id is allocated with COALESCE(MAX("id"),0)+1 rather than
    //     any LAST_INSERT_ID()/RETURNING-of-a-default dance.
    public sealed class ItemsSQL
    {
        DataTable _items;

        // The full editable column set, in a single place so SELECT / UPDATE /
        // INSERT stay in lockstep. "id" is handled separately (PK, not updated).
        static readonly string[] s_Columns =
        {
            "level", "category", "sub_category", "type", "max_stack",
            "name", "description", "manufacturer", "2d_asset", "3d_asset",
            "no_trade", "no_store", "no_destroy", "no_manu", "unique",
            "item_base_id", "custom_flag", "status", "effect_id", "price",
        };

        public ItemsSQL()
        {
            _items = DB.Instance.executeQuery(
                "SELECT * FROM \"item_base\" ORDER BY \"name\", \"id\";", null, null);
        }

        public DataTable getItemTable() => _items;

        public DataRow getRowByID(long id)
        {
            var rows = _items.WhereIntEquals("id", id);
            return rows.Length > 0 ? rows[0] : null;
        }

        // exact == case-insensitive equality; otherwise case-insensitive
        // substring. The search text is matched as a captured value, not spliced
        // into a filter expression.
        public DataRow[] searchByName(string text, bool exact)
            => exact ? _items.WhereTextEquals("name", text)
                     : _items.WhereTextContains("name", text);

        // --- writes ----------------------------------------------------------

        public void updateRecord(DataRow dr)
        {
            var setClause = new System.Text.StringBuilder();
            foreach (string col in s_Columns)
            {
                if (setClause.Length != 0) setClause.Append(", ");
                setClause.Append('"').Append(col).Append("\" = @").Append(ParamName(col));
            }

            string query = "UPDATE \"item_base\" SET " + setClause +
                           " WHERE \"id\" = @id;";

            var (names, values) = BindColumns(dr, includeId: true);
            DB.Instance.executeCommand(query, names, values);
        }

        public long newRecord()
        {
            long id = NextId();

            var row = _items.NewRow();
            row["id"]           = id;
            row["level"]        = 0;
            row["category"]     = 0;
            row["sub_category"] = 0;
            row["type"]         = 0;
            row["max_stack"]    = 1;
            row["name"]         = "<New Item>";
            row["description"]  = "";
            row["manufacturer"] = 0;
            row["2d_asset"]     = 0;
            row["3d_asset"]     = 0;
            row["no_trade"]     = 0;
            row["no_store"]     = 0;
            row["no_destroy"]   = 0;
            row["no_manu"]      = 0;
            row["unique"]       = 0;
            row["item_base_id"] = DBNull.Value;
            row["custom_flag"]  = 0;
            row["status"]       = 0;
            row["effect_id"]    = 0;
            row["price"]        = 0;

            InsertRow(row);
            _items.Rows.Add(row);
            row.AcceptChanges();
            _items.AcceptChanges();
            return id;
        }

        public long newFromRecord(DataRow src)
        {
            long id = NextId();

            var row = _items.NewRow();
            foreach (DataColumn col in _items.Columns)
                row[col.ColumnName] = src[col.ColumnName];
            row["id"] = id;

            InsertRow(row);
            _items.Rows.Add(row);
            row.AcceptChanges();
            _items.AcceptChanges();
            return id;
        }

        public void deleteRecord(long id, DataRow dr)
        {
            DB.Instance.executeCommand(
                "DELETE FROM \"item_base\" WHERE \"id\" = @id;",
                new[] { "id" }, new[] { id.ToString() });
            _items.Rows.Remove(dr);
        }

        // --- helpers ---------------------------------------------------------

        long NextId()
        {
            var dt = DB.Instance.executeQuery(
                "SELECT COALESCE(MAX(\"id\"), 0) + 1 AS \"next\" FROM \"item_base\";",
                null, null);
            return Convert.ToInt64(dt.Rows[0]["next"]);
        }

        void InsertRow(DataRow row)
        {
            var cols = new System.Text.StringBuilder("\"id\"");
            var vals = new System.Text.StringBuilder("@id");
            foreach (string col in s_Columns)
            {
                cols.Append(", \"").Append(col).Append('"');
                vals.Append(", @").Append(ParamName(col));
            }

            string query = "INSERT INTO \"item_base\" (" + cols + ") VALUES (" + vals + ");";
            var (names, values) = BindColumns(row, includeId: true);
            DB.Instance.executeCommand(query, names, values);
        }

        // Build the (parameter-name[], value[]) pair for the editable columns,
        // optionally including the id. NULL DataRow cells map to a null value
        // string so the DB layer / change-tracker emit SQL NULL.
        static (string[], string[]) BindColumns(DataRow dr, bool includeId)
        {
            int n = s_Columns.Length + (includeId ? 1 : 0);
            var names = new string[n];
            var values = new string[n];

            int i = 0;
            if (includeId)
            {
                names[i] = "id";
                values[i] = CellToString(dr["id"]);
                i++;
            }
            foreach (string col in s_Columns)
            {
                names[i] = ParamName(col);
                values[i] = CellToString(dr[col]);
                i++;
            }
            return (names, values);
        }

        static string CellToString(object cell)
            => (cell == null || cell == DBNull.Value) ? null : cell.ToString();

        // Map a column name to a parameter name. Postgres quoting handles the
        // column name itself; the parameter name just has to be a valid
        // identifier, so strip the leading digit on 2d_asset / 3d_asset.
        static string ParamName(string column)
        {
            if (column == "2d_asset") return "asset2d";
            if (column == "3d_asset") return "asset3d";
            return column;
        }
    }
}
