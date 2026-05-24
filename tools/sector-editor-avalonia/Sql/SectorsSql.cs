using System;
using System.Data;

namespace N7.Sql
{
    // Parameterised port of tools/sector-editor/Sql/SectorsSql.cs. The original
    // built UPDATE/INSERT strings with 30+ "'" + r["col"] + "'" concatenations
    // per call — every one a SQL-injection sink. Here every column rides on a
    // ?param placeholder so MySqlConnector escapes it.
    public class SectorsSql
    {
        private DataTable sectors;

        public SectorsSql()
        {
            sectors = Database.executeQuery(Database.DatabaseName.net7,
                "SELECT * FROM sectors order by system_id, name");
        }

        public DataTable getSectorTable() => sectors;

        public DataRow[] findRowsByName(String name)
        {
            String esc = name.Replace("'", "''");
            return sectors.Select("name Like '" + esc + "'");
        }

        public DataRow[] getRowsBySystemID(String systemID)
        {
            return sectors.Select("system_id = '" + systemID + "'");
        }

        public DataTable queryBySystemID(String systemID)
        {
            return Database.executeQuery(Database.DatabaseName.net7,
                "SELECT * FROM sectors where system_id=?sid order by name",
                new String[] { "sid" },
                new String[] { systemID });
        }

        public int getIDFromName(String name)
        {
            DataRow[] foundRows = sectors.Select("name Like '" + name + "'");
            return int.Parse(foundRows[0]["sector_id"].ToString());
        }

        private static readonly String[] AllCols = new String[]
        {
            "name", "x_min", "x_max", "y_min", "y_max", "z_min", "z_max",
            "grid_x", "grid_y", "grid_z", "fog_near", "fog_far", "debris_mode",
            "light_backdrop", "fog_backdrop", "swap_backdrop",
            "backdrop_fog_near", "backdrop_fog_far", "max_tilt", "auto_level",
            "impulse_rate", "decay_velocity", "decay_spin", "backdrop_asset",
            "greetings", "notes", "system_id", "galaxy_x", "galaxy_y",
            "galaxy_z", "sector_type"
        };

        public void updateRow(DataRow r)
        {
            String[] paramNames = new String[AllCols.Length + 1];
            String[] paramValues = new String[AllCols.Length + 1];
            String setClause = "";
            for (int i = 0; i < AllCols.Length; i++)
            {
                paramNames[i] = AllCols[i];
                paramValues[i] = r[AllCols[i]].ToString();
                if (setClause.Length > 0) setClause += ", ";
                setClause += AllCols[i] + "=?" + AllCols[i];
            }
            paramNames[AllCols.Length] = "sector_id";
            paramValues[AllCols.Length] = r["sector_id"].ToString();

            String query = "UPDATE sectors SET " + setClause + " WHERE sector_id=?sector_id";
            Database.executeCommand(Database.DatabaseName.net7, query, paramNames, paramValues);
        }

        public void newRow(DataRow r)
        {
            String[] colsWithId = new String[AllCols.Length + 1];
            colsWithId[0] = "sector_id";
            Array.Copy(AllCols, 0, colsWithId, 1, AllCols.Length);

            String[] paramNames = new String[colsWithId.Length];
            String[] paramValues = new String[colsWithId.Length];
            String setClause = "";
            for (int i = 0; i < colsWithId.Length; i++)
            {
                paramNames[i] = colsWithId[i];
                paramValues[i] = r[colsWithId[i]].ToString();
                if (setClause.Length > 0) setClause += ", ";
                setClause += colsWithId[i] + "=?" + colsWithId[i];
            }

            String query = "INSERT INTO sectors SET " + setClause;
            Database.executeCommand(Database.DatabaseName.net7, query, paramNames, paramValues);
        }
    }
}
