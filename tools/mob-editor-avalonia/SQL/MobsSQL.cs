using System;
using System.Data;
using CommonTools.Database;

namespace MobEditorAvalonia.SQL
{
    // Avalonia port of tools/mob-editor/Sql/MobsSQL.cs.
    // Original mixed raw string-concat SQL with quoted-int literals and
    // a single quote-doubling pass on name. The port routes every call
    // through DB.Instance with named parameters, closing the
    // string-concat injection windows on name/ai fields.
    public sealed class MobsSQL
    {
        DataTable _mobs;

        public MobsSQL()
        {
            _mobs = DB.Instance.executeQuery(
                "SELECT * FROM \"mob_base\" ORDER BY \"name\", \"level\";", null, null);
        }

        public DataTable getMobTable() => _mobs;

        public DataRow getRowByID(int id)
        {
            var rows = _mobs.Select("mob_id = " + id);
            return rows.Length > 0 ? rows[0] : null;
        }

        public DataRow[] getRowsByNameQuery(string filterExpr)
            => _mobs.Select(filterExpr);

        public DataRow[] getRowsBetween(int level)
            => _mobs.Select("level = " + level);

        public int newRecord()
        {
            // mob_base.mob_id has NO identity/sequence; allocate it ourselves
            // and INSERT with an explicit mob_id (mirrors ItemsSQL).
            int lastID = NextId();

            DB.Instance.executeCommand(
                "INSERT INTO \"mob_base\" (\"mob_id\", \"name\", \"level\", \"type\", " +
                "\"altruism\", \"aggressiveness\", \"bravery\", \"intelligence\", " +
                "\"faction_id\", \"base_asset_id\", \"h\", \"s\", \"v\", \"scale\", \"ai\") " +
                "VALUES (@id, @n, 1, 0, 0, 0, 0, 0, 0, -1, 0, 0, 0, 1, '');",
                new[] { "id", "n" },
                new[] { lastID.ToString(), "<New Mob>" });

            var row = _mobs.NewRow();
            row["mob_id"]        = lastID;
            row["name"]          = "<New Mob>";
            row["level"]         = 1;
            row["type"]          = 0;
            row["altruism"]      = 0;
            row["aggressiveness"] = 0;
            row["bravery"]       = 0;
            row["intelligence"]  = 0;
            row["faction_id"]    = 0;
            row["base_asset_id"] = -1;
            row["h"]             = 0;
            row["s"]             = 0;
            row["v"]             = 0;
            row["scale"]         = 1;
            row["ai"]            = "";
            _mobs.Rows.Add(row);
            row.AcceptChanges();
            _mobs.AcceptChanges();
            return lastID;
        }

        public void deleteRecord(int id, DataRow dr)
        {
            DB.Instance.executeCommand(
                "DELETE FROM \"mob_base\" WHERE \"mob_id\"=@id;",
                new[] { "id" }, new[] { id.ToString() });
            _mobs.Rows.Remove(dr);
        }

        public void updateRecord(DataRow dr)
        {
            DB.Instance.executeCommand(
                "UPDATE \"mob_base\" SET \"name\"=@n, \"level\"=@lvl, \"type\"=@t, " +
                "\"altruism\"=@al, \"aggressiveness\"=@ag, \"bravery\"=@br, \"intelligence\"=@intel, " +
                "\"faction_id\"=@fid, \"base_asset_id\"=@baid, \"h\"=@h, \"s\"=@s, \"v\"=@v, \"scale\"=@sc, \"ai\"=@ai " +
                "WHERE \"mob_id\"=@id;",
                new[] { "n", "lvl", "t", "al", "ag", "br", "intel",
                        "fid", "baid", "h", "s", "v", "sc", "ai", "id" },
                new[] {
                    dr["name"].ToString(),
                    dr["level"].ToString(),
                    dr["type"].ToString(),
                    dr["altruism"].ToString(),
                    dr["aggressiveness"].ToString(),
                    dr["bravery"].ToString(),
                    dr["intelligence"].ToString(),
                    dr["faction_id"].ToString(),
                    dr["base_asset_id"].ToString(),
                    dr["h"].ToString(),
                    dr["s"].ToString(),
                    dr["v"].ToString(),
                    dr["scale"].ToString(),
                    dr["ai"].ToString(),
                    dr["mob_id"].ToString(),
                });
        }

        public int newFromRecord(DataRow src)
        {
            // mob_base.mob_id has NO identity/sequence; allocate explicitly.
            int lastID = NextId();

            DB.Instance.executeCommand(
                "INSERT INTO \"mob_base\" (\"mob_id\", \"name\", \"level\", \"type\", " +
                "\"altruism\", \"aggressiveness\", \"bravery\", \"intelligence\", " +
                "\"faction_id\", \"base_asset_id\", \"h\", \"s\", \"v\", \"scale\", \"ai\") " +
                "VALUES (@id, @n, @lvl, @t, @al, @ag, @br, @intel, " +
                "@fid, @baid, @h, @s, @v, @sc, @ai);",
                new[] { "id", "n", "lvl", "t", "al", "ag", "br", "intel",
                        "fid", "baid", "h", "s", "v", "sc", "ai" },
                new[] {
                    lastID.ToString(),
                    src["name"].ToString(),
                    src["level"].ToString(),
                    src["type"].ToString(),
                    src["altruism"].ToString(),
                    src["aggressiveness"].ToString(),
                    src["bravery"].ToString(),
                    src["intelligence"].ToString(),
                    src["faction_id"].ToString(),
                    src["base_asset_id"].ToString(),
                    src["h"].ToString(),
                    src["s"].ToString(),
                    src["v"].ToString(),
                    src["scale"].ToString(),
                    src["ai"].ToString(),
                });

            var row = _mobs.NewRow();
            foreach (DataColumn col in _mobs.Columns)
                row[col.ColumnName] = src[col.ColumnName];
            row["mob_id"] = lastID;
            _mobs.Rows.Add(row);
            row.AcceptChanges();
            _mobs.AcceptChanges();
            return lastID;
        }

        // mob_base.mob_id has no identity column, so allocate the next id as
        // MAX(mob_id)+1 (mirrors ItemsSQL.NextId for item_base."id").
        int NextId()
        {
            var dt = DB.Instance.executeQuery(
                "SELECT COALESCE(MAX(\"mob_id\"), 0) + 1 AS \"next\" FROM \"mob_base\";",
                null, null);
            return Convert.ToInt32(dt.Rows[0]["next"]);
        }
    }
}
