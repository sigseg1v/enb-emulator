using System;
using System.Data;

namespace N7.Sql
{
    public class FactionSql
    {
        private DataTable factions;

        public FactionSql()
        {
            factions = Database.executeQuery(Database.DatabaseName.net7, "SELECT * FROM factions;");
        }

        public String findNameByID(int factionID)
        {
            if (factionID <= 0) return "None";
            DataRow[] foundRows = factions.Select("faction_id = '" + factionID + "'");
            if (foundRows.Length == 0) return "None";
            return foundRows[0]["name"].ToString();
        }

        public int findIDbyName(String name)
        {
            if (name == "None") return -1;
            String esc = name.Replace("'", "''");
            DataRow[] foundRows = factions.Select("name LIKE '" + esc + "'");
            if (foundRows.Length == 0) return -1;
            return int.Parse(foundRows[0]["faction_id"].ToString());
        }

        public DataTable getFactionTable()
        {
            return factions;
        }
    }
}
