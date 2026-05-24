using System;
using System.Data;
using CommonTools.Database;

namespace FactionEditorAvalonia.SQL
{
    // Avalonia port of tools/faction-editor/SQL/FactionsSQL.cs. The
    // single change from the original is that we go through
    // commontools-avalonia's DB.Instance (MySql.Data wrapped with
    // parameterised executeQuery/executeCommand) instead of the
    // per-tool Database wrapper, AND we replace the string-concat SQL
    // with parameterised placeholders — which closes the obvious
    // SQL-injection holes the original had on every name/description
    // field.
    public sealed class FactionsSQL
    {
        DataTable _factions;

        public FactionsSQL()
        {
            _factions = DB.Instance.executeQuery(
                "SELECT * FROM factions ORDER BY name;", null, null);
        }

        public DataTable getFactionTable() => _factions;

        public DataRow getRowByID(int id)
        {
            var rows = _factions.Select("faction_id = " + id);
            return rows.Length > 0 ? rows[0] : null;
        }

        public DataRow newRecord()
        {
            DB.Instance.executeCommand(
                "INSERT INTO factions SET name=?name, description=?desc;",
                new[] { "name", "desc" },
                new[] { "<New Faction>", "" });

            var lastInsert = DB.Instance.executeQuery(
                "SELECT LAST_INSERT_ID() AS id;", null, null);
            int lastInsertID = Convert.ToInt32(lastInsert.Rows[0]["id"]);

            var newRow = _factions.NewRow();
            newRow["faction_id"] = lastInsertID;
            newRow["name"]       = "<New Faction>";
            newRow["description"] = "";
            newRow["PDA_text"]    = "";
            _factions.Rows.Add(newRow);
            newRow.AcceptChanges();
            _factions.AcceptChanges();
            return newRow;
        }

        public void deleteRecord(int id, DataRow dr)
        {
            DB.Instance.executeCommand(
                "DELETE FROM factions WHERE faction_id=?id;",
                new[] { "id" },
                new[] { id.ToString() });
            _factions.Rows.Remove(dr);
        }

        public void updateRecord(DataRow dr)
        {
            int factionID = Convert.ToInt32(dr["faction_id"]);
            DB.Instance.executeCommand(
                "UPDATE factions SET name=?name, description=?desc, PDA_text=?pda WHERE faction_id=?id;",
                new[] { "name", "desc", "pda", "id" },
                new[] {
                    dr["name"].ToString(),
                    dr["description"].ToString(),
                    dr["PDA_text"].ToString(),
                    factionID.ToString(),
                });
        }
    }
}
