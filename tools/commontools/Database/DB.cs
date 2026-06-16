using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Dapper;
using Npgsql;
using NpgsqlTypes;
using System.Data;
using System.IO;

namespace CommonTools.Database
{
    // Routes DB error reports that the WinForms build raised via
    // System.Windows.Forms.MessageBox.Show. The library can't depend on a
    // UI toolkit, so the host app installs its own sink at startup (the
    // Avalonia Login window plugs in MsBox.Avalonia). Default sink writes
    // to stderr so headless callers still see the error.
    public static class DBErrorReporter
    {
        public static Action<string, string> Show = (title, body) =>
            Console.Error.WriteLine("[" + title + "] " + body);
    }

    public class DB : Singleton.Get<DB>
    {
        /// <summary>
        /// The name of the database. Lowercase: the live Postgres content DB is
        /// `net7` (created lowercase), and libpq does NOT case-fold the database
        /// name in a connection string -- passing "Net7" would fail to match.
        /// </summary>
        public const String DATABASE_NAME = "net7";

        private Boolean m_showExecutionTime = false;

        /// <summary>
        /// A Postgres transaction
        /// </summary>
        private NpgsqlTransaction m_transaction;

        /// <summary>
        /// The Postgres connection
        /// </summary>
        private NpgsqlConnection m_connection = null;

        /// <summary>
        ///   <para>Records mutating SQL this session issues, for export as a
        ///   re-appliable <c>.sql</c> changeset (AC.3). Off by default; the host
        ///   sets <c>ChangeTracker.Enabled = true</c> for an editing session and
        ///   calls <c>WriteSqlFile</c> to emit the changeset.</para>
        ///   <para>Capture is centralized here in the DB layer so every editor
        ///   inherits change-tracking without per-editor plumbing.</para>
        /// </summary>
        public ChangeTracker ChangeTracker { get; } = new ChangeTracker();

        /// <summary>
        /// Constructor
        /// </summary>
        public DB()
        {
        }

        /// <summary>
        /// Open a connection to the database. On failure the connection is reset
        /// to null and the error is reported (never left in a broken half-open
        /// state that wedges the next call); callers get a thrown exception they
        /// already catch, or -- in the transaction helpers -- a clean rethrow.
        /// </summary>
        public NpgsqlConnection openConnection()
        {
            if (m_connection == null)
            {
                m_connection = new NpgsqlConnection(CommonTools.Gui.LoginData.ConnStr(DATABASE_NAME));
            }

            if (m_connection.State == ConnectionState.Closed)
            {
                try
                {
                    m_connection.Open();
                }
                catch
                {
                    // Drop the dead connection object so a later retry rebuilds
                    // it from the (possibly corrected) login data instead of
                    // reusing a broken instance.
                    try { m_connection.Dispose(); } catch { }
                    m_connection = null;
                    throw;
                }
            }

            return m_connection;
        }

        public void closeConnection()
        {
            if (m_connection != null) m_connection.Close();
        }

        public void startTransaction()
        {
            // Ensure the connection is opened
            openConnection();
            m_transaction = m_connection.BeginTransaction();
        }

        public void commitTransaction()
        {
            m_transaction.Commit();
            m_transaction = null;
        }

        public void rollbackTransaction()
        {
            m_transaction.Rollback();
            m_transaction = null;
        }

        /// <summary>
        ///   <para>Common procedure to execute a query.</para>
        /// </summary>
        /// <param name="query">The SQL query to execute.</param>
        /// <param name="parameter">The query parameter to fill in</param>
        /// <param name="value">The value of the query parameter to use</param>
        /// <summary>
        ///   <para>Build a query parameter bound as Postgres <c>unknown</c>.</para>
        ///   <para>Every value the tool layer hands us is a <c>string</c>. If we
        ///   let Npgsql infer the type it sends <c>text</c>, and Postgres will
        ///   NOT implicitly compare/assign <c>text</c> against an integer column:
        ///   <c>WHERE "id" = @id</c> on a bigint PK fails with
        ///   <c>operator does not exist: bigint = text</c>, which the catch
        ///   blocks here swallow -- so every UPDATE/INSERT/DELETE that keys on an
        ///   integer column silently affects 0 rows. MySQL tolerated the loose
        ///   typing; Postgres does not.</para>
        ///   <para>Sending the value as <c>unknown</c> (an untyped literal) makes
        ///   the server coerce it by context -- to bigint in a key comparison, to
        ///   integer in a numeric assignment, to text for a text column -- which
        ///   restores the loose binding the editors were written against. A null
        ///   value maps to SQL NULL.</para>
        /// </summary>
        private static NpgsqlParameter makeParameter(String name, String value)
        {
            return new NpgsqlParameter
            {
                ParameterName = name,
                NpgsqlDbType = NpgsqlDbType.Unknown,
                Value = (Object)value ?? DBNull.Value,
            };
        }

        /// <summary>
        ///   Dapper-mapped read of a single row (or default if none). For
        ///   fixed-schema reads where the result maps cleanly onto a record --
        ///   unlike the dynamic search paths, which query a runtime-chosen table
        ///   whose identifiers can't be parameterised and so must stay on the
        ///   DataTable adapter. Values bind as proper typed parameters via the
        ///   anonymous <paramref name="param"/> object (NOT the string/Unknown
        ///   coercion executeQuery needs for its all-strings call sites).
        /// </summary>
        public T queryRow<T>(String sql, Object param = null)
        {
            return openConnection().QueryFirstOrDefault<T>(sql, param, m_transaction);
        }

        /// <summary>Dapper-mapped scalar read (or default if no row).</summary>
        public T queryScalar<T>(String sql, Object param = null)
        {
            return openConnection().ExecuteScalar<T>(sql, param, m_transaction);
        }

        public DataTable executeQuery(String query, String[] parameter, String[] value)
        {
            DataTable dataTable = null;
            NpgsqlDataAdapter dataAdapter = null;
            try
            {
                dataTable = new DataTable();
                openConnection();

                dataAdapter = new NpgsqlDataAdapter(query, m_connection);
                // Add columns only -- do NOT pull primary-key / NOT-NULL info
                // from the schema. AddWithKey would copy each source column's
                // NOT-NULL + PK constraints onto the DataTable, then enforce
                // them on EnableConstraints(). That breaks every multi-table
                // LEFT JOIN here (e.g. the sector_objects + 6-subtable read):
                // an unmatched right side yields NULLs in columns the subtable
                // declares NOT NULL, so EnableConstraints throws "one or more
                // rows contain values violating non-null... constraints" even
                // though the query is correct. Nothing in these editors drives
                // DataAdapter.Update() or reads .PrimaryKey/.Constraints (all
                // writes go through executeCommand with hand-built SQL), so the
                // keys AddWithKey fetched were pure liability.
                dataAdapter.MissingSchemaAction = MissingSchemaAction.Add;

                if (parameter != null && parameter.Length != 0)
                {
                    for (int parameterIndex = 0; parameterIndex < parameter.Length; parameterIndex++)
                    {
                        dataAdapter.SelectCommand.Parameters.Add(makeParameter(parameter[parameterIndex], value[parameterIndex]));
                    }
                }

                if (m_transaction != null)
                {
                    dataAdapter.SelectCommand.Transaction = m_transaction;
                }

                DateTime start = DateTime.Now;
                dataAdapter.Fill(dataTable); // 156.245 milliseconds.
                // An INSERT ... RETURNING runs through here (it yields a row),
                // so record mutations from the query path too. IsMutating
                // filters plain SELECTs out, so this is a no-op for reads and
                // ensures RETURNING-style inserts still land in the changeset.
                ChangeTracker.Record(query, parameter, value);
                if (m_showExecutionTime)
                {
                    TimeSpan timeSpan = DateTime.Now - start;
                    System.Console.WriteLine(query + ": {0} milliseconds, {1} rows.", timeSpan.TotalMilliseconds, dataTable.Rows.Count);
                }
            }
            catch (Exception e)
            {
                String values = "";
                if (value != null)
                {
                    foreach (String val in value)
                    {
                        if (values.Length != 0)
                        {
                            values += ", ";
                        }
                        values += val;
                    }
                }
                DBErrorReporter.Show("Error within DB.executeQuery()",
                                     e.Message + "\n\n"
                                   + e.StackTrace + "\n\n"
                                   + query
                                   + "\n" + values + "\n\n"
                                   + query);
            }
            finally
            {
                if (m_connection != null)
                {
                    // Should close here but since we are keeping the connection open
                    // we won't close it here
                    //m_connection.Close();
                }
            }
            return dataTable;
        }

        public int executeCommand(String query, String[] parameter, String[] value)
        {
            int rowsAffected = 0;
            try
            {
                openConnection();

                NpgsqlCommand command = new NpgsqlCommand(query, m_connection);

                if (parameter != null && parameter.Length != 0)
                {
                    for (int parameterIndex = 0; parameterIndex < parameter.Length; parameterIndex++)
                    {
                        command.Parameters.Add(makeParameter(parameter[parameterIndex], value[parameterIndex]));
                    }
                }

                if (m_transaction != null)
                {
                    command.Transaction = m_transaction;
                }

                DateTime start = DateTime.Now;
                rowsAffected = command.ExecuteNonQuery();
                // Record AFTER the statement succeeds, so the changeset reflects
                // edits that actually landed (no-op unless ChangeTracker.Enabled).
                ChangeTracker.Record(query, parameter, value);
                if (m_showExecutionTime)
                {
                    TimeSpan timeSpan = DateTime.Now - start;
                    System.Console.WriteLine(query + ": {0} milliseconds, {1} rows.", timeSpan.TotalMilliseconds, rowsAffected);
                }
            }
            catch (Exception e)
            {
                String values = "";
                if (value != null)
                {
                    foreach (String val in value)
                    {
                        if (values.Length != 0)
                        {
                            values += ", ";
                        }
                        values += val;
                    }
                }
                DBErrorReporter.Show("Error within DB.executeCommand()",
                                     e.Message + "\n\n"
                                   + e.StackTrace + "\n\n"
                                   + query
                                   + "\n" + values + "\n\n"
                                   + query);
            }
            finally
            {
                if (m_connection != null)
                {
                    // Should close here but since we are keeping the connection open
                    // we won't close it here
                    //m_connection.Close();
                }
            }
            return rowsAffected;
        }

        /// <summary>
        /// Set whether the time to execute an SQL command is displayed to the console.
        /// </summary>
        /// <param name="show">Whether to show the execution time</param>
        public void showExecutionTime(Boolean show)
        {
            m_showExecutionTime = show;
        }

        public String createSelect(Enum[] field, Net7.Tables table, Enum idField, String value, Int32 queryCount)
        {
            // Columns, table and id column are schema identifiers (enum-derived);
            // the only value is bound as @<idField><queryCount>.
            String columns = "";
            foreach (Enum enumField in field)
            {
                if (columns.Length != 0) columns += ", ";
                columns += ColumnData.GetQuotedName(enumField);
            }
            return "SELECT " + columns
                 + " FROM " + table.ToString()
                 + " WHERE " + ColumnData.GetQuotedName(idField)
                 + " = @" + idField.ToString() + queryCount.ToString() + ";";
        }

        public DataTable select(Enum[] field, Net7.Tables table, Enum idField, String value)
        {
            String query = createSelect(field, table, idField, value, 0);
            return DB.Instance.executeQuery(query, new string[] { idField.ToString() + "0" }, new string[] { value });
        }

        /// <summary>
        /// Import the contents of a file into a database table
        /// </summary>
        /// <param name="table">The name of the table</param>
        /// <param name="valuesFile">The file name to import.  The contents of this file
        /// are expected to contain one or multiple rows, where each field of the table
        /// is present.</param>
        public void importValues(Net7.Tables table, String valuesFile)
        {
            String query;
            String row;
            StreamReader tr = new StreamReader(valuesFile);
            while (!tr.EndOfStream)
            {
                row = tr.ReadLine();
                query = "INSERT INTO "
                      + table
                      + " VALUES ("
                      + row
                      + ")";
                int rowsAffected = DB.Instance.executeCommand(query, null, null);
                if (rowsAffected == 0)
                {
                    DBErrorReporter.Show("DB.importValues()", "Error inserting the following row:\n" + row);
                }
            }
            tr.Close();
        }

        /// <summary>
        ///   <para>Convert a database structure into various enumerations.</para>
        /// </summary>
        /// <param name="databaseName">The database name for which to generate the code.</param>
        /// <remarks>This method overwrites the contents of the &lt;databaseName&gt;.cs file.
        ///          The goal of this approach is to easily handle schema changes without having
        ///          to hunt through code in order to locate strings that now point to obsolete
        ///          names.  Instead those will now generate compile-time errors.</remarks>
        public void makeDatabaseVariables()
        {
            String query;
            String tableName;
            String tableEnum;
            String columnAlignedPosition;

            // Postgres puts our tables in the `public` schema (the MySQL build
            // queried by database-name because in MySQL the schema IS the
            // database). This is a dev-only codegen path that regenerates
            // net7.cs; it is not on any runtime path.
            const String SCHEMA_NAME = "public";

            DataTable dataTable;
            dataTable = executeQuery("SELECT DISTINCT table_name "
                                   + "FROM information_schema.columns "
                                   + "WHERE table_schema = '"
                                   + SCHEMA_NAME
                                   + "'", null, null);

            System.IO.FileInfo fileInfo = new System.IO.FileInfo("..\\..\\..\\..\\CommonTools\\Database\\" + DATABASE_NAME + ".cs");
            System.IO.StreamWriter streamWriter = fileInfo.CreateText();

            streamWriter.WriteLine("// This file was automatically generated by Database.makeDatabaseVariables() on "
                                  + DateTime.Now.Year.ToString() + "/"
                                  + DateTime.Now.Month.ToString() + "/"
                                  + DateTime.Now.Day.ToString() + " "
                                  + DateTime.Now.Hour.ToString() + ":"
                                  + DateTime.Now.Minute.ToString() + ":"
                                  + DateTime.Now.Second.ToString());

            streamWriter.WriteLine("namespace CommonTools.Database");
            streamWriter.WriteLine("{");
            streamWriter.WriteLine("    public static class " + DATABASE_NAME);
            streamWriter.WriteLine("    {");

            tableEnum = "        public enum Tables { ";
            columnAlignedPosition = new String(' ', tableEnum.Length);
            streamWriter.Write(tableEnum);
            DataRow dataRow;
            for (int rowIndex = 0; rowIndex < dataTable.Rows.Count; ++rowIndex)
            {
                dataRow = dataTable.Rows[rowIndex];
                tableName = dataRow["table_name"].ToString();
                if (rowIndex != dataTable.Rows.Count - 1)
                {
                    tableName += ", ";
                }
                streamWriter.Write(tableName);
                if (rowIndex != 0 && (rowIndex % 5) == 0)
                {
                    streamWriter.WriteLine("");
                    streamWriter.Write(columnAlignedPosition);
                }
            }
            streamWriter.WriteLine(" };");
            streamWriter.WriteLine("");

            query = "SELECT table_name, column_name, data_type "
                    + "FROM information_schema.columns "
                    + "WHERE table_schema = '"
                    + SCHEMA_NAME
                    + "' "
                    + "ORDER BY table_name, ordinal_position";
            makeDatabaseEnum(streamWriter, query, columnAlignedPosition, false, true);

            // Postgres: single quotes are string literals, double quotes are
            // identifiers. Column aliases must be bare or double-quoted (MySQL's
            // 'alias' single-quote form is rejected here).
            query = "SELECT "
                  + "'item_type' as \"table_name\", "
                  + "name as \"column_name\", "
                  + "id as \"data_type\" "
                  + "FROM "
                  + Net7.Tables.item_type.ToString()
                  + " ORDER BY "
                  + ColumnData.GetName(Net7.Table_item_type._id);
            makeDatabaseEnum(streamWriter, query, columnAlignedPosition, true, true);

            streamWriter.WriteLine("    }");
            streamWriter.WriteLine("}");
            streamWriter.Close();
        }

        public void makeDatabaseEnum(System.IO.StreamWriter streamWriter,
                                            String query,
                                            String columnAlignedPosition,
                                            Boolean tableContent,
                                            Boolean forCSharp)
        {
            String tableName;
            String columnName;
            String columnEnum;
            String dataType;
            String tableEnum;
            String enumPrefix = tableContent ? "Enum_" : "Table_";
            DataTable dataTable;
            DataRow dataRow;

            dataTable = executeQuery(query, null, null);

            String previousTableName = null;
            for (int rowIndex = 0; rowIndex < dataTable.Rows.Count; ++rowIndex)
            {
                dataRow = dataTable.Rows[rowIndex];
                tableName = dataRow["table_name"].ToString();
                columnName = dataRow["column_name"].ToString();
                dataType = dataRow["data_type"].ToString();

                columnEnum = columnName;
                columnEnum = "_" + columnEnum; // Ensures a valid enum name
                columnEnum = columnEnum.Replace(" ", "_"); // Not sure how else to handle a space in the name
                if (tableContent)
                {
                    // Add the enum value
                    columnEnum += " = " + dataType;
                }
                else
                {
                    // Add the ColName property
                    columnEnum = "[ColName(\"" + columnName + "\")] "
                               + "[DataType(\"" + dataType + "\")] "
                               + columnEnum;
                }

                if ((previousTableName != null
                     && previousTableName.Equals(tableName))
                    || rowIndex == dataTable.Rows.Count - 1)
                {
                    streamWriter.WriteLine(",");
                    streamWriter.Write(columnAlignedPosition + columnEnum);
                }

                if (previousTableName != null
                    && (rowIndex == dataTable.Rows.Count - 1 || !previousTableName.Equals(tableName)))
                {
                    // End of a table
                    streamWriter.WriteLine(" };");
                    streamWriter.WriteLine("");
                    previousTableName = null;
                }

                if (previousTableName == null
                    && rowIndex != dataTable.Rows.Count - 1)
                {
                    // Start of a new table
                    previousTableName = tableName;
                    tableEnum = "        "
                                + (forCSharp ? "public " : "")
                                + "enum "
                                + enumPrefix
                                + tableName
                                + " { ";
                    columnAlignedPosition = new String(' ', tableEnum.Length);
                    streamWriter.Write(tableEnum + columnEnum);
                }
            }
        }

        public static void printDataSetContents(DataSet dataSet, String title)
        {
            /*
            System.Console.WriteLine("\n" + title + ": " + dataSet.Tables.Count + " tables in the dataset\n" + new String('=', 20));
            foreach (DataTable dataTable in dataSet.Tables)
            {
                System.Console.WriteLine(dataTable.TableName + ": " + dataTable.Rows.Count.ToString() + " rows");
            }
            System.Console.WriteLine("");
            */


            int maxRows;
            System.Console.WriteLine("\n" + title + ": " + dataSet.Tables.Count + " tables in the dataset\n" + new String('=', 20));
            foreach (DataTable dataTable in dataSet.Tables)
            {
                System.Console.WriteLine(dataTable.TableName + ": " + dataTable.Rows.Count.ToString() + " rows");
                System.Console.WriteLine(new String('-', dataTable.TableName.Length));
                maxRows = 10;
                foreach (DataRow dataRow in dataTable.Rows)
                {
                    if (--maxRows == 0)
                    {
                        break;
                    }
                    System.Console.WriteLine(dataRow[0].ToString() + "\t" + dataRow[1].ToString());
                }
                System.Console.WriteLine("");
            }

        }

    }
}
