using System;
using System.Data;
using CommonTools.Database;

namespace FactionEditorAvalonia.SQL
{
    // Avalonia port of tools/faction-editor/SQL/FactionMatrixSQL.cs.
    // Same migration as FactionsSQL: parameterised through
    // commontools-avalonia's DB wrapper.
    public sealed class FactionMatrixSQL
    {
        DataTable _matrix;

        public FactionMatrixSQL()
        {
            _matrix = DB.Instance.executeQuery(
                "SELECT * FROM faction_matrix ORDER BY faction_id, faction_entry_id;",
                null, null);
        }

        public DataTable getFactionMatrixTable() => _matrix;

        public DataRow[] getRowsByID(int factionId)
            => _matrix.Select("faction_id = " + factionId);

        public DataRow getRowByID(int id)
        {
            var rows = _matrix.Select("id = " + id);
            return rows.Length > 0 ? rows[0] : null;
        }

        public DataRow newRecord(int newID, int existingID)
        {
            DB.Instance.executeCommand(
                "INSERT INTO faction_matrix SET faction_id=?fid, faction_entry_id=?eid, base_value='0', reward_faction=0;",
                new[] { "fid", "eid" },
                new[] { newID.ToString(), existingID.ToString() });

            var lastInsert = DB.Instance.executeQuery(
                "SELECT LAST_INSERT_ID() AS id;", null, null);
            int lastInsertID = Convert.ToInt32(lastInsert.Rows[0]["id"]);

            var newRow = _matrix.NewRow();
            newRow["id"]               = lastInsertID;
            newRow["faction_id"]       = newID;
            newRow["faction_entry_id"] = existingID;
            newRow["base_value"]       = 0;
            newRow["current_value"]    = 0;
            newRow["reward_faction"]   = 0;
            _matrix.Rows.Add(newRow);
            newRow.AcceptChanges();
            _matrix.AcceptChanges();
            return newRow;
        }

        public void deleteRecord(int factionId)
        {
            DB.Instance.executeCommand(
                "DELETE FROM faction_matrix WHERE faction_id=?fid;",
                new[] { "fid" },
                new[] { factionId.ToString() });

            foreach (var row in getRowsByID(factionId))
                _matrix.Rows.Remove(row);
        }

        public void updateRecord(int factionId)
        {
            foreach (var r in getRowsByID(factionId))
            {
                DB.Instance.executeCommand(
                    "UPDATE faction_matrix SET faction_id=?fid, faction_entry_id=?eid, base_value=?bv, current_value=?cv, reward_faction=?rf WHERE id=?id;",
                    new[] { "fid", "eid", "bv", "cv", "rf", "id" },
                    new[] {
                        r["faction_id"].ToString(),
                        r["faction_entry_id"].ToString(),
                        r["base_value"].ToString(),
                        r["current_value"].ToString(),
                        Convert.ToInt32(r["reward_faction"]).ToString(),
                        r["id"].ToString(),
                    });
            }
        }
    }
}
