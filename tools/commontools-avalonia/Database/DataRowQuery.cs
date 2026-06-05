using System;
using System.Data;
using System.Linq;

namespace CommonTools.Database
{
    // In-memory row filters for the cached editor DataTables.
    //
    // These replace the old `DataTable.Select("col = " + value)` /
    // `Select("name LIKE '" + value + "'")` calls. DataTable.Select is not a
    // database query -- it has no parameter channel -- so the fix is not to
    // "parameterise" it but to stop building a filter string from a value at
    // all: the value is captured as a plain C# variable in the predicate, never
    // concatenated into an expression the filter engine re-parses. That removes
    // the whole class of quoting/escaping fragility (a name containing a quote,
    // %, *, or [ ] could previously alter the filter).
    public static class DataRowQuery
    {
        // Numeric equality. Compared as Int64 so the column's concrete integer
        // width (int / bigint) doesn't matter; NULL cells never match.
        public static DataRow[] WhereIntEquals(this DataTable table, string column, long value)
            => table.Rows.Cast<DataRow>()
                    .Where(r => r[column] != DBNull.Value && Convert.ToInt64(r[column]) == value)
                    .ToArray();

        // Case-insensitive exact text match (the old `LIKE 'name'` with no
        // wildcard, which DataTable.Select treated case-insensitively).
        public static DataRow[] WhereTextEquals(this DataTable table, string column, string value)
            => table.Rows.Cast<DataRow>()
                    .Where(r => string.Equals(r[column]?.ToString() ?? "", value,
                                              StringComparison.OrdinalIgnoreCase))
                    .ToArray();

        // Case-insensitive substring match (the old `LIKE '%name%'`).
        public static DataRow[] WhereTextContains(this DataTable table, string column, string value)
            => table.Rows.Cast<DataRow>()
                    .Where(r => (r[column]?.ToString() ?? "")
                                .Contains(value ?? "", StringComparison.OrdinalIgnoreCase))
                    .ToArray();
    }
}
