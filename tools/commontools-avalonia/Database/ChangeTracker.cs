using System;
using System.Collections.Generic;
using System.Text;

namespace CommonTools.Database
{
    /// <summary>
    ///   <para>Records the mutating SQL an editing session issues so it can be
    ///   exported as a reviewable, re-appliable <c>.sql</c> changeset.</para>
    ///   <para>Capture happens at the single DB-write boundary
    ///   (<see cref="DB.executeCommand(string, string[], string[])"/>): every
    ///   INSERT / UPDATE / DELETE the editor runs is recorded with its parameter
    ///   values inlined as escaped SQL literals, so the emitted file is
    ///   standalone Postgres that re-applies the same edits against <c>net7</c>.
    ///   Because the hook is in the shared DB layer, ALL editors inherit
    ///   change-tracking for free -- no per-editor plumbing.</para>
    /// </summary>
    public sealed class ChangeTracker
    {
        readonly List<string> m_statements = new List<string>();
        readonly object m_lock = new object();

        /// <summary>
        /// When false (default) nothing is recorded. The host turns this on for
        /// the duration of an editing session.
        /// </summary>
        public bool Enabled { get; set; }

        /// <summary>Number of statements recorded so far.</summary>
        public int Count
        {
            get { lock (m_lock) { return m_statements.Count; } }
        }

        /// <summary>Discard everything recorded so far.</summary>
        public void Clear()
        {
            lock (m_lock) { m_statements.Clear(); }
        }

        /// <summary>
        /// Record one statement. Called by the DB layer for every command it
        /// executes; non-mutating statements (SELECT, SET, BEGIN, ...) are
        /// ignored so the changeset is purely the edits. No-op when disabled.
        /// </summary>
        public void Record(string query, string[] parameters, string[] values)
        {
            if (!Enabled || string.IsNullOrWhiteSpace(query)) return;
            if (!IsMutating(query)) return;

            string sql = InlineParameters(query, parameters, values).TrimEnd();
            if (!sql.EndsWith(";")) sql += ";";

            lock (m_lock) { m_statements.Add(sql); }
        }

        /// <summary>
        /// The recorded statements, in order, as a single SQL script with a
        /// header. Empty (header only) when nothing was recorded.
        /// </summary>
        public string BuildScript(string toolName)
        {
            var sb = new StringBuilder();
            sb.Append("-- Earth & Beyond editor changeset\n");
            sb.Append("-- tool: ").Append(string.IsNullOrEmpty(toolName) ? "(unknown)" : toolName).Append('\n');
            // NOTE: no wall-clock stamp here -- the caller stamps the filename.
            sb.Append("-- target DB: ").Append(DB.DATABASE_NAME).Append(" (Postgres content DB)\n");
            sb.Append("--\n");
            sb.Append("-- Re-appliable: each statement is standalone Postgres with\n");
            sb.Append("-- parameter values inlined as escaped literals. Review before applying.\n\n");

            lock (m_lock)
            {
                if (m_statements.Count == 0)
                {
                    sb.Append("-- (no changes recorded this session)\n");
                }
                else
                {
                    sb.Append("BEGIN;\n\n");
                    foreach (string s in m_statements)
                    {
                        sb.Append(s).Append('\n');
                    }
                    sb.Append("\nCOMMIT;\n");
                }
            }
            return sb.ToString();
        }

        /// <summary>Write <see cref="BuildScript"/> to <paramref name="path"/>.</summary>
        public void WriteSqlFile(string path, string toolName)
        {
            System.IO.File.WriteAllText(path, BuildScript(toolName));
        }

        // --- helpers -----------------------------------------------------------

        static bool IsMutating(string query)
        {
            string q = query.TrimStart();
            return StartsWithWord(q, "INSERT")
                || StartsWithWord(q, "UPDATE")
                || StartsWithWord(q, "DELETE")
                || StartsWithWord(q, "REPLACE"); // legacy MySQL-ism; record if it slips through
        }

        static bool StartsWithWord(string s, string word)
        {
            if (s.Length < word.Length) return false;
            if (string.Compare(s, 0, word, 0, word.Length, StringComparison.OrdinalIgnoreCase) != 0)
                return false;
            // Must be a whole word: end of string or a non-identifier char follows.
            if (s.Length == word.Length) return true;
            char next = s[word.Length];
            return !(char.IsLetterOrDigit(next) || next == '_');
        }

        /// <summary>
        /// Replace each <c>@name</c> placeholder with its value as an escaped SQL
        /// literal. Values arrive as strings (the DB layer passes string[]);
        /// every value is emitted as a quoted literal so Postgres casts it in
        /// assignment context -- a quoted '5' is accepted for an int column.
        /// Longest parameter names are substituted first so <c>@id</c> does not
        /// clobber <c>@id10</c>.
        /// </summary>
        public static string InlineParameters(string query, string[] parameters, string[] values)
        {
            if (parameters == null || parameters.Length == 0) return query;

            // Index by length descending to avoid prefix collisions.
            var order = new List<int>();
            for (int i = 0; i < parameters.Length; i++) order.Add(i);
            order.Sort((a, b) =>
            {
                int la = parameters[a] == null ? 0 : parameters[a].Length;
                int lb = parameters[b] == null ? 0 : parameters[b].Length;
                return lb.CompareTo(la);
            });

            string result = query;
            foreach (int i in order)
            {
                if (string.IsNullOrEmpty(parameters[i])) continue;
                string token = "@" + parameters[i];
                string literal = ToSqlLiteral(values != null && i < values.Length ? values[i] : null);
                result = ReplaceToken(result, token, literal);
            }
            return result;
        }

        static string ToSqlLiteral(string value)
        {
            if (value == null) return "NULL";
            return "'" + value.Replace("'", "''") + "'";
        }

        /// <summary>
        /// Replace every occurrence of <paramref name="token"/> in
        /// <paramref name="text"/> that is not immediately followed by an
        /// identifier char (so <c>@id</c> won't match inside <c>@id10</c>).
        /// </summary>
        static string ReplaceToken(string text, string token, string replacement)
        {
            var sb = new StringBuilder();
            int idx = 0;
            while (true)
            {
                int hit = text.IndexOf(token, idx, StringComparison.Ordinal);
                if (hit < 0)
                {
                    sb.Append(text, idx, text.Length - idx);
                    break;
                }
                int after = hit + token.Length;
                bool boundary = after >= text.Length
                                || !(char.IsLetterOrDigit(text[after]) || text[after] == '_');
                sb.Append(text, idx, hit - idx);
                if (boundary) sb.Append(replacement);
                else sb.Append(token); // part of a longer name; leave as-is
                idx = after;
            }
            return sb.ToString();
        }
    }
}
