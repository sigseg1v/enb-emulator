using System;
using System.Data;

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
        {
            String esc = name.Replace("'", "''");
            return sectors.Select("name Like '" + esc + "'");
        }

        public DataRow[] getRowsBySystemID(String systemID)
        {
            return sectors.Select("system_id = '" + systemID + "'");
        }
    }
}
