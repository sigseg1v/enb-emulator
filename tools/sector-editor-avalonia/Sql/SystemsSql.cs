using System;
using System.Data;

namespace N7.Sql
{
    // Parameterised port of tools/sector-editor/Sql/SystemsSql.cs.
    public class SystemsSql
    {
        private DataTable systems;

        public SystemsSql()
        {
            systems = Database.executeQuery(Database.DatabaseName.net7, "Select * from systems");
        }

        public DataTable getSystemTable() => systems;

        public DataRow[] findRowsByName(String name)
        {
            return systems.Select("name Like '" + name + "'");
        }

        public String findRowNameByID(int id)
        {
            DataRow[] foundRows = systems.Select("system_id='" + id + "'");
            return foundRows[0]["name"].ToString();
        }

        public int getIDFromName(String name)
        {
            DataRow[] foundRows = systems.Select("name Like '" + name + "'");
            return int.Parse(foundRows[0]["system_id"].ToString());
        }

        private static readonly String[] UpdateCols = new String[]
        {
            "name", "galaxy_x", "galaxy_y", "galaxy_z",
            "color_r", "color_g", "color_b", "notes"
        };

        public void updateRow(DataRow r)
        {
            String[] paramNames = new String[UpdateCols.Length + 1];
            String[] paramValues = new String[UpdateCols.Length + 1];
            String setClause = "";
            for (int i = 0; i < UpdateCols.Length; i++)
            {
                paramNames[i] = UpdateCols[i];
                paramValues[i] = r[UpdateCols[i]].ToString();
                if (setClause.Length > 0) setClause += ", ";
                setClause += UpdateCols[i] + "=?" + UpdateCols[i];
            }
            paramNames[UpdateCols.Length] = "system_id";
            paramValues[UpdateCols.Length] = r["system_id"].ToString();

            String query = "UPDATE systems SET " + setClause + " WHERE system_id=?system_id";
            Database.executeCommand(Database.DatabaseName.net7, query, paramNames, paramValues);
        }

        public void newRow(DataRow nr)
        {
            String[] paramNames = new String[UpdateCols.Length];
            String[] paramValues = new String[UpdateCols.Length];
            String setClause = "";
            for (int i = 0; i < UpdateCols.Length; i++)
            {
                paramNames[i] = UpdateCols[i];
                paramValues[i] = nr[UpdateCols[i]].ToString();
                if (setClause.Length > 0) setClause += ", ";
                setClause += UpdateCols[i] + "=?" + UpdateCols[i];
            }

            String query = "INSERT INTO systems SET " + setClause;
            Database.executeCommand(Database.DatabaseName.net7, query, paramNames, paramValues);

            nr["system_id"] = Database.lastInsertId();
        }
    }
}
