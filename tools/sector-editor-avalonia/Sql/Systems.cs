using System;
using System.Data;

namespace N7.Sql
{
    class Systems
    {
        private DataTable systems;

        public Systems()
        {
            systems = Database.executeQuery(Database.DatabaseName.net7, "Select * from systems");
        }

        public DataTable getSystemTable()
        {
            return systems;
        }

        public DataRow[] findRowsByName(String name)
        {
            String esc = name.Replace("'", "''");
            return systems.Select("name Like '" + esc + "'");
        }
    }
}
