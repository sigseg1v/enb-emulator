using System;
using System.Collections.Generic;
using System.Data;

namespace N7.Sql
{
    // Parameterised port of tools/sector-editor/Sql/SectorObjectsSql.cs — the
    // 280-LOC monster with ~70 sprintf SQL sites across sector_objects +
    // sector_nav_points + per-type subtables (mob/planets/stargates/starbases/
    // harvestable). One typed-DataRow column per parameter; every value rides
    // a ?name placeholder so MySqlConnector escapes it.
    //
    // Object-type discriminator (preserved from the WinForms original):
    //   0  → mob spawn          (sector_objects_mob       , mob_id)
    //   3  → planet             (sector_objects_planets   , planet_id)
    //   11 → stargate           (sector_objects_stargates , stargate_id)
    //   12 → starbase           (sector_objects_starbases , starbase_id)
    //   37 → (no subtable — nav-point only)
    //   38 → harvestable field  (sector_objects_harvestable , resource_id)
    public class SectorObjectsSql
    {
        private DataTable sectorObjects;
        private String sectorID;

        private const int TYPE_MOB         = 0;
        private const int TYPE_PLANET      = 3;
        private const int TYPE_STARGATE    = 11;
        private const int TYPE_STARBASE    = 12;
        private const int TYPE_NAV_ONLY    = 37;
        private const int TYPE_HARVESTABLE = 38;

        public SectorObjectsSql(String sectorName)
        {
            DataTable tmp = Database.executeQuery(Database.DatabaseName.net7,
                "SELECT sector_id FROM sectors where name=?name",
                new String[] { "name" },
                new String[] { sectorName });

            foreach (DataRow r in tmp.Rows)
            {
                sectorID = r["sector_id"].ToString();
                String soQuery =
                    "SELECT * FROM sector_objects" +
                    " left join sector_nav_points on sector_objects.sector_object_id = sector_nav_points.sector_object_id" +
                    " left join sector_objects_harvestable on sector_objects.sector_object_id = sector_objects_harvestable.resource_id" +
                    " left join sector_objects_planets on sector_objects.sector_object_id = sector_objects_planets.planet_id" +
                    " left join sector_objects_starbases on sector_objects.sector_object_id = sector_objects_starbases.starbase_id" +
                    " left join sector_objects_stargates on sector_objects.sector_object_id = sector_objects_stargates.stargate_id" +
                    " left join sector_objects_mob on sector_objects.sector_object_id = sector_objects_mob.mob_id" +
                    " where sector_objects.sector_id=?sid order by sector_objects.type;";
                sectorObjects = Database.executeQuery(Database.DatabaseName.net7,
                    soQuery,
                    new String[] { "sid" },
                    new String[] { sectorID });
            }
            tmp.Dispose();
        }

        public DataTable getSectorObject() => sectorObjects;

        // --- mutations ----------------------------------------------------

        private static readonly String[] NavPointCols = new String[]
        {
            "nav_type", "signature", "is_huge", "base_xp", "exploration_range"
        };

        private static readonly String[] SectorObjCols = new String[]
        {
            "base_asset_id", "h", "s", "v", "type", "scale",
            "position_x", "position_y", "position_z",
            "orientation_u", "orientation_v", "orientation_w", "orientation_z",
            "name", "appears_in_radar", "radar_range", "gate_to",
            "sound_effect_id", "sound_effect_range"
        };

        private static readonly String[] MobCols       = { "mob_count", "mob_spawn_radius", "respawn_time", "delayed_spawn" };
        private static readonly String[] PlanetCols    = { "orbit_id", "orbit_dist", "orbit_angle", "orbit_rate",
                                                           "rotate_angle", "rotate_rate", "tilt_angle", "is_landable" };
        private static readonly String[] StargateCols  = { "classSpecific", "faction_id" };
        private static readonly String[] StarbaseCols  = { "capShip", "dockable" };
        private static readonly String[] HarvCols      = { "level", "field", "res_count", "spawn_radius",
                                                           "pop_rock_chance", "max_field_radius" };

        public void updateRow(DataRow r)
        {
            int type = int.Parse(r["type"].ToString());

            // sector_nav_points UPDATE
            execUpdate("sector_nav_points", NavPointCols, "sector_object_id",
                       r, "sector_object_id");

            // sector_objects UPDATE
            execUpdate("sector_objects", SectorObjCols, "sector_object_id",
                       r, "sector_object_id");

            // per-type subtable UPDATE
            switch (type)
            {
                case TYPE_MOB:
                    execUpdate("sector_objects_mob", MobCols, "mob_id", r, "mob_id");
                    break;
                case TYPE_PLANET:
                    execUpdate("sector_objects_planets", PlanetCols, "planet_id", r, "planet_id");
                    break;
                case TYPE_STARGATE:
                    execUpdate("sector_objects_stargates", StargateCols, "stargate_id", r, "stargate_id");
                    break;
                case TYPE_STARBASE:
                    execUpdate("sector_objects_starbases", StarbaseCols, "starbase_id", r, "starbase_id");
                    break;
                case TYPE_HARVESTABLE:
                    execUpdate("sector_objects_harvestable", HarvCols, "resource_id", r, "resource_id");
                    break;
                case TYPE_NAV_ONLY:
                    // nav-point only, nothing else to update
                    break;
            }
        }

        public void deleteRow(int id, int type)
        {
            String idParam = "id";
            String[] paramNames = new String[] { idParam };
            String[] paramValues = new String[] { id.ToString() };

            // Per-type subtable deletes (do these first to keep FK constraints happy).
            switch (type)
            {
                case TYPE_MOB:
                    Database.executeCommand(Database.DatabaseName.net7,
                        "DELETE FROM mob_spawn_group where spawn_group_id=?" + idParam, paramNames, paramValues);
                    Database.executeCommand(Database.DatabaseName.net7,
                        "DELETE FROM sector_objects_mob where mob_id=?" + idParam, paramNames, paramValues);
                    break;
                case TYPE_PLANET:
                    Database.executeCommand(Database.DatabaseName.net7,
                        "DELETE FROM sector_objects_planets where planet_id=?" + idParam, paramNames, paramValues);
                    break;
                case TYPE_STARGATE:
                    Database.executeCommand(Database.DatabaseName.net7,
                        "DELETE FROM sector_objects_stargates where stargate_id=?" + idParam, paramNames, paramValues);
                    break;
                case TYPE_STARBASE:
                    Database.executeCommand(Database.DatabaseName.net7,
                        "DELETE FROM sector_objects_starbases where starbase_id=?" + idParam, paramNames, paramValues);
                    break;
                case TYPE_HARVESTABLE:
                    Database.executeCommand(Database.DatabaseName.net7,
                        "DELETE FROM mob_spawn_group where spawn_group_id=?" + idParam, paramNames, paramValues);
                    Database.executeCommand(Database.DatabaseName.net7,
                        "DELETE FROM sector_objects_harvestable_restypes where group_id=?" + idParam, paramNames, paramValues);
                    Database.executeCommand(Database.DatabaseName.net7,
                        "DELETE FROM sector_objects_harvestable where resource_id=?" + idParam, paramNames, paramValues);
                    break;
                case TYPE_NAV_ONLY:
                    break;
            }

            // Always clean up the nav-point row and the parent sector_objects row.
            Database.executeCommand(Database.DatabaseName.net7,
                "DELETE FROM sector_nav_points where sector_object_id=?" + idParam, paramNames, paramValues);
            Database.executeCommand(Database.DatabaseName.net7,
                "DELETE FROM sector_objects where sector_object_id=?" + idParam, paramNames, paramValues);
        }

        public void newRow(DataRow r)
        {
            int type = int.Parse(r["type"].ToString());

            // INSERT into sector_objects with the sector linkage.
            String[] objCols = new String[SectorObjCols.Length + 1];
            objCols[0] = "sector_id";
            Array.Copy(SectorObjCols, 0, objCols, 1, SectorObjCols.Length);
            execInsert("sector_objects", objCols, r);

            long lastInsertID = Database.lastInsertId();

            // INSERT into sector_nav_points using the freshly-minted id.
            String[] navColsWithLinks = new String[NavPointCols.Length + 2];
            navColsWithLinks[0] = "sector_object_id";
            navColsWithLinks[1] = "sector_id";
            Array.Copy(NavPointCols, 0, navColsWithLinks, 2, NavPointCols.Length);

            List<String> navParamNames = new List<String> { "sector_object_id", "sector_id" };
            List<String> navParamValues = new List<String> { lastInsertID.ToString(), r["sector_id"].ToString() };
            String navSet = "sector_object_id=?sector_object_id, sector_id=?sector_id";
            foreach (String c in NavPointCols)
            {
                navParamNames.Add(c);
                navParamValues.Add(r[c].ToString());
                navSet += ", " + c + "=?" + c;
            }
            Database.executeCommand(Database.DatabaseName.net7,
                "INSERT INTO sector_nav_points SET " + navSet,
                navParamNames.ToArray(), navParamValues.ToArray());

            // INSERT into the per-type subtable.
            switch (type)
            {
                case TYPE_MOB:
                    execInsertWithId("sector_objects_mob", "mob_id", lastInsertID, MobCols, r);
                    break;
                case TYPE_PLANET:
                    execInsertWithId("sector_objects_planets", "planet_id", lastInsertID, PlanetCols, r);
                    break;
                case TYPE_STARGATE:
                    execInsertWithId("sector_objects_stargates", "stargate_id", lastInsertID, StargateCols, r);
                    break;
                case TYPE_STARBASE:
                    execInsertWithId("sector_objects_starbases", "starbase_id", lastInsertID, StarbaseCols, r);
                    break;
                case TYPE_HARVESTABLE:
                    execInsertWithId("sector_objects_harvestable", "resource_id", lastInsertID, HarvCols, r);
                    break;
                case TYPE_NAV_ONLY:
                    break;
            }

            r["sector_object_id"] = lastInsertID;
            r.AcceptChanges();
        }

        // --- private helpers ---------------------------------------------

        private static void execUpdate(String table, String[] cols, String whereCol, DataRow r, String whereSource)
        {
            String[] paramNames = new String[cols.Length + 1];
            String[] paramValues = new String[cols.Length + 1];
            String setClause = "";
            for (int i = 0; i < cols.Length; i++)
            {
                paramNames[i] = cols[i];
                paramValues[i] = r[cols[i]].ToString();
                if (setClause.Length > 0) setClause += ", ";
                setClause += cols[i] + "=?" + cols[i];
            }
            paramNames[cols.Length] = whereCol;
            paramValues[cols.Length] = r[whereSource].ToString();

            String query = "UPDATE " + table + " SET " + setClause + " WHERE " + whereCol + "=?" + whereCol;
            Database.executeCommand(Database.DatabaseName.net7, query, paramNames, paramValues);
        }

        private static void execInsert(String table, String[] cols, DataRow r)
        {
            String[] paramNames = new String[cols.Length];
            String[] paramValues = new String[cols.Length];
            String setClause = "";
            for (int i = 0; i < cols.Length; i++)
            {
                paramNames[i] = cols[i];
                paramValues[i] = r[cols[i]].ToString();
                if (setClause.Length > 0) setClause += ", ";
                setClause += cols[i] + "=?" + cols[i];
            }
            String query = "INSERT INTO " + table + " SET " + setClause;
            Database.executeCommand(Database.DatabaseName.net7, query, paramNames, paramValues);
        }

        private static void execInsertWithId(String table, String idCol, long idValue, String[] cols, DataRow r)
        {
            String[] paramNames = new String[cols.Length + 1];
            String[] paramValues = new String[cols.Length + 1];
            paramNames[0] = idCol;
            paramValues[0] = idValue.ToString();
            String setClause = idCol + "=?" + idCol;
            for (int i = 0; i < cols.Length; i++)
            {
                paramNames[i + 1] = cols[i];
                paramValues[i + 1] = r[cols[i]].ToString();
                setClause += ", " + cols[i] + "=?" + cols[i];
            }
            String query = "INSERT INTO " + table + " SET " + setClause;
            Database.executeCommand(Database.DatabaseName.net7, query, paramNames, paramValues);
        }
    }
}
