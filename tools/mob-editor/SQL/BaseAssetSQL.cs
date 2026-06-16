using System.Data;
using CommonTools.Database;

namespace MobEditor.SQL
{
    public sealed class BaseAssetSQL
    {
        DataTable _assets;

        public BaseAssetSQL()
        {
            _assets = DB.Instance.executeQuery(
                "SELECT * FROM assets;", null, null);
        }

        public DataTable getAssetsTable() => _assets;

        public DataRow[] getRowsbyCategory(string mainCat)
            => _assets.WhereTextEquals("main_cat", mainCat);

        // Reproduces the original's filename-mangling: file.w3d → file.jpg,
        // anything else → "null". Used for thumbnail lookups beside the
        // editor binary (which we no longer ship).
        public string getFileNameByID(int id)
        {
            var rows = _assets.WhereIntEquals("base_id", id);
            if (rows.Length == 0) return "null";
            string name = rows[0]["filename"]?.ToString() ?? "";
            if (name.Length < 4) return "null";
            string lower = name.ToLowerInvariant();
            if (lower.EndsWith(".w3d") || lower.EndsWith(".tga"))
                return name.Substring(0, name.Length - 4) + ".jpg";
            return "null";
        }
    }
}
