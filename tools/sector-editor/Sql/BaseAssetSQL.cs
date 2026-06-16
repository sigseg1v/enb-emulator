using System;
using System.Data;
using CommonTools.Database;

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
            => baseAssets.WhereTextEquals("main_cat", name);
    }
}
