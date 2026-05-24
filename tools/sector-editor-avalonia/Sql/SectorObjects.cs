using System;
using System.Data;

namespace N7.Sql
{
    class SectorObjects
    {
        private DataTable sectorObjects;
        private String sectorID;
        private DataTable specificSecObject;

        public SectorObjects(String sectorName)
        {
            DataTable tmp = Database.executeQuery(Database.DatabaseName.net7,
                "SELECT sector_id FROM sectors where name=?name",
                new String[] { "name" },
                new String[] { sectorName });

            foreach (DataRow r in tmp.Rows)
            {
                sectorID = r["sector_id"].ToString();
                String soQuery =
                    "SELECT * FROM sector_objects left join sector_nav_points " +
                    "on sector_objects.sector_object_id = sector_nav_points.sector_object_id " +
                    "where sector_objects.sector_id=?sid order by sector_objects.type;";
                sectorObjects = Database.executeQuery(Database.DatabaseName.net7,
                    soQuery,
                    new String[] { "sid" },
                    new String[] { sectorID });
            }
            tmp.Dispose();
        }

        public DataTable getSectorObject()
        {
            return sectorObjects;
        }

        public DataTable specificSectorObjectTable(int type)
        {
            switch (type)
            {
                case 11:
                    // Original (verbatim) was missing a space between FROM and the table name;
                    // I'm preserving the join shape but writing it as valid SQL because the
                    // original would have thrown a syntax error if ever exercised. No call
                    // sites reach this method in the source we audited.
                    String sosecQuery =
                        "SELECT stargate_id,classSpecific,faction_id FROM " +
                        "sector_objects_stargates left join sector_objects on " +
                        "sector_objects.sector_object_id = sector_objects_stargates.stargate_id " +
                        "where sector_objects.sector_id=?sid;";
                    specificSecObject = Database.executeQuery(Database.DatabaseName.net7,
                        sosecQuery,
                        new String[] { "sid" },
                        new String[] { sectorID });
                    break;
            }
            return specificSecObject;
        }
    }
}
