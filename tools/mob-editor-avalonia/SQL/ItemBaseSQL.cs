using System.Data;
using CommonTools.Database;

namespace MobEditorAvalonia.SQL
{
    public sealed class ItemBaseSQL
    {
        DataTable _itemBase;

        public ItemBaseSQL()
        {
            // Quote 2d_asset because it starts with a digit.
            _itemBase = DB.Instance.executeQuery(
                "SELECT id, level, name, sub_category, `2d_asset` FROM item_base ORDER BY name;",
                null, null);
        }

        public DataTable getItemBaseTable() => _itemBase;

        public DataRow getRowByID(int id)
        {
            var rows = _itemBase.Select("id = " + id);
            return rows.Length > 0 ? rows[0] : null;
        }

        public DataRow[] getRowsByCategory(int subcatID)
            => _itemBase.Select("sub_category = " + subcatID);
    }
}
