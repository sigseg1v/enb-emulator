using System;
using System.Data;

namespace N7.Sql
{
    class Navs
    {
        private DataTable navs;

        public Navs()
        {
            navs = Database.executeQuery(Database.DatabaseName.net7,
                "SELECT * FROM sectors order by system_id, name");
        }

        public DataTable getSectorTable()
        {
            return navs;
        }
    }
}
