using System.Data;
using CommonTools.Database;

namespace MobEditorAvalonia.SQL
{
    public sealed class FactionSql
    {
        DataTable _factions;

        public FactionSql()
        {
            _factions = DB.Instance.executeQuery(
                "SELECT * FROM factions;", null, null);
        }

        public DataTable getFactionTable() => _factions;

        public string findNameByID(int factionID)
        {
            if (factionID <= 0) return "None";
            var rows = _factions.Select("faction_id = " + factionID);
            return rows.Length > 0 ? rows[0]["name"].ToString() : "None";
        }

        public int findIDbyName(string name)
        {
            if (name == "None") return -1;
            var rows = _factions.Select("name LIKE '" + name.Replace("'", "''") + "'");
            return rows.Length > 0 ? System.Convert.ToInt32(rows[0]["faction_id"]) : -1;
        }
    }
}
