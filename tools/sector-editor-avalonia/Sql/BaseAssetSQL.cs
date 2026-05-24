using System;
using System.Data;

namespace N7.Sql
{
    public class BaseAssetSQL
    {
        private DataTable baseAssets;

        public BaseAssetSQL()
        {
            baseAssets = Database.executeQuery(Database.DatabaseName.net7, "SELECT * FROM assets;");
        }

        public DataTable getAssetsTable()
        {
            return baseAssets;
        }

        public DataRow[] getRowsbyCategory(String name)
        {
            // DataRow.Select takes a DataTable filter expression — apostrophes need
            // doubling per System.Data.Select syntax (this is not SQL, so the
            // commontools parameterised path doesn't apply here).
            String esc = name.Replace("'", "''");
            return baseAssets.Select("main_cat LIKE '" + esc + "'");
        }
    }
}
