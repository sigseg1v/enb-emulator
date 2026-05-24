using System;
using System.Data;
using CommonTools.Database;

namespace N7
{
    // Facade preserving the original tools/sector-editor `Database.executeQuery(DatabaseName, query)`
    // entry point so the sprite/window port in Tier 12e reads as a near-1:1 translation,
    // but routing every query through commontools-avalonia's DB.Instance — which (a) shares
    // the connection pool with the rest of the Avalonia tool suite and (b) exposes the
    // parameterised executeQuery/executeCommand overloads that kill the sprintf SQL-injection
    // holes the WinForms editor was riddled with.
    //
    // The original had two DatabaseName values (net7, net7_db). Only `net7` is reachable
    // from any of the in-use call sites; the second was an artefact of MobConvertSQL which
    // pulled rows from a defunct `tmp_enbemulator` database. That migration script is dead
    // code and is not ported. The enum is kept for API compatibility but only `net7` is
    // honoured — passing anything else throws so divergence is loud.
    static class Database
    {
        public enum DatabaseName { net7, net7_db }

        public static DataTable executeQuery(DatabaseName databaseName, String query)
        {
            require(databaseName);
            return DB.Instance.executeQuery(query, null, null);
        }

        public static DataTable executeQuery(DatabaseName databaseName, String query, String[] parameter, String[] value)
        {
            require(databaseName);
            return DB.Instance.executeQuery(query, parameter, value);
        }

        public static int executeCommand(DatabaseName databaseName, String query, String[] parameter, String[] value)
        {
            require(databaseName);
            return DB.Instance.executeCommand(query, parameter, value);
        }

        public static int executeCommand(DatabaseName databaseName, String query)
        {
            require(databaseName);
            return DB.Instance.executeCommand(query, null, null);
        }

        public static long lastInsertId()
        {
            DataTable t = DB.Instance.executeQuery("SELECT LAST_INSERT_ID()", null, null);
            if (t == null || t.Rows.Count == 0) return 0;
            return Convert.ToInt64(t.Rows[0][0]);
        }

        private static void require(DatabaseName databaseName)
        {
            if (databaseName != DatabaseName.net7)
            {
                throw new InvalidOperationException(
                    "Only DatabaseName.net7 is supported in the Avalonia port; "
                    + "the second database was only referenced by the dead MobConvertSQL "
                    + "migration script and is not carried over.");
            }
        }
    }
}
