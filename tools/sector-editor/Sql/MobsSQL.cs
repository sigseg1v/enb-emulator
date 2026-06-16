using System;
using System.Data;

namespace N7.Sql
{
    public class MobsSQL
    {
        private DataTable mobs;

        public MobsSQL()
        {
            mobs = Database.executeQuery(Database.DatabaseName.net7,
                "SELECT * FROM mob_base order by name, level;");
        }

        public DataTable getMobTable()
        {
            return mobs;
        }
    }
}
