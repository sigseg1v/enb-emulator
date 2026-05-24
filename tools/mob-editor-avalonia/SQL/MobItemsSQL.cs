using System.Data;
using CommonTools.Database;

namespace MobEditorAvalonia.SQL
{
    public sealed class MobItemsSQL
    {
        DataTable _mobItems;

        public MobItemsSQL()
        {
            _mobItems = DB.Instance.executeQuery(
                "SELECT * FROM mob_items;", null, null);
        }

        public DataTable getMobItemsTable() => _mobItems;

        public DataRow[] getRowsByID(int mobID)
            => _mobItems.Select("mob_id = " + mobID);

        public void deleteRecord(DataRow dr)
        {
            DB.Instance.executeCommand(
                "DELETE FROM mob_items WHERE mob_id=?mid AND item_base_id=?ibid;",
                new[] { "mid", "ibid" },
                new[] { dr["mob_id"].ToString(), dr["item_base_id"].ToString() });
            _mobItems.Rows.Remove(dr);
        }

        public void updateRecord(DataRow dr)
        {
            DB.Instance.executeCommand(
                "UPDATE mob_items SET drop_chance=?dc, usage_chance=?uc, qty=?q " +
                "WHERE mob_id=?mid AND item_base_id=?ibid;",
                new[] { "dc", "uc", "q", "mid", "ibid" },
                new[] {
                    dr["drop_chance"].ToString(),
                    dr["usage_chance"].ToString(),
                    dr["qty"].ToString(),
                    dr["mob_id"].ToString(),
                    dr["item_base_id"].ToString(),
                });
        }

        public void insertRecord(DataRow dr)
        {
            DB.Instance.executeCommand(
                "INSERT INTO mob_items SET mob_id=?mid, drop_chance=?dc, " +
                "usage_chance=?uc, qty=?q, item_base_id=?ibid, type=?t;",
                new[] { "mid", "dc", "uc", "q", "ibid", "t" },
                new[] {
                    dr["mob_id"].ToString(),
                    dr["drop_chance"].ToString(),
                    dr["usage_chance"].ToString(),
                    dr["qty"].ToString(),
                    dr["item_base_id"].ToString(),
                    dr["type"].ToString(),
                });
        }
    }
}
