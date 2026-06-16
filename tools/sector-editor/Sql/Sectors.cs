using System;
using System.Data;
using CommonTools.Database;

namespace N7.Sql
{
    class Sectors
    {
        private DataTable sectors;

        public Sectors()
        {
            sectors = Database.executeQuery(Database.DatabaseName.net7,
                "SELECT * FROM sectors order by system_id, name");
        }

        public DataTable getSectorTable()
        {
            return sectors;
        }

        public DataRow[] findRowsByName(String name)
            => sectors.WhereTextEquals("name", name);

        public DataRow[] getRowsBySystemID(String systemID)
            => sectors.WhereIntEquals("system_id", long.Parse(systemID));
    }
}
