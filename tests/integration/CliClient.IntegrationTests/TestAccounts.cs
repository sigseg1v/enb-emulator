// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Runtime.CompilerServices;
using Npgsql;

namespace N7.CliClient.IntegrationTests;

/// <summary>
/// A test account that exists in the postgres net7_user.accounts table
/// for the lifetime of the test run.
/// </summary>
public sealed record TestAccount(int Id, string Username, string Password);

/// <summary>
/// On-demand test account provisioning.
///
/// <para>
/// Each call to <see cref="New"/> inserts a fresh row into the
/// net7_user.accounts table with a process-unique username and a
/// process-unique ID. No pre-seeded account pool; no per-test method
/// name mapping in source; no Fixtures/seed.sql.
/// </para>
///
/// <para>
/// Username shape: <c>t_&lt;8-hex&gt;_&lt;6-hex&gt;</c> where the first
/// 8 hex digits come from <see cref="ProcessUid"/> (one per test run)
/// and the last 6 come from <see cref="_counter"/> (monotonic, atomic).
/// Width = 17 chars, well under the accounts.username varchar(40)
/// limit. The per-process prefix prevents collisions if two test
/// runs accidentally share a database. The per-account counter
/// suffix makes IDs unique inside one run.
/// </para>
///
/// <para>
/// IDs start at 9_000_001 and increment per call. The dump's
/// accounts auto-increment is 15_965 so we are safely clear of any
/// real-account collision.
/// </para>
///
/// <para>
/// status defaults to 100 (ACTIVE/admin) which is what real accounts
/// use. Pass <c>status: 0</c> to provision a STRESS_TEST_CLOSED
/// account the global UDP plane will reject with G_ERROR 12 (see
/// server/src/UDP_Global.cpp:ProcessTicketInfo).
/// </para>
/// </summary>
public static class TestAccounts
{
    public const string SharedPassword = "testpw";

    // 8 hex digits drawn once per test process. The whole point is to
    // keep usernames stable across one run (so retries / log scraping
    // work) while preventing collisions across runs that race on the
    // same database -- e.g. a developer running tests locally while
    // CI runs them on a shared dev postgres.
    private static readonly string ProcessUid =
        Guid.NewGuid().ToString("N").Substring(0, 8);

    // Monotonic per-account counter. Interlocked.Increment is safe for
    // xUnit's parallel class execution.
    private static int _counter;

    /// <summary>
    /// Provision a fresh test account. Inserts a row into
    /// net7_user.accounts and returns the credentials.
    /// </summary>
    /// <param name="server">
    /// The collection fixture; used for its postgres connection string.
    /// </param>
    /// <param name="status">
    /// accounts.status: 100 = ACTIVE/admin (default), 0 =
    /// STRESS_TEST_CLOSED.
    /// </param>
    /// <param name="testName">
    /// Bound automatically via CallerMemberName. Used only for the
    /// formname column so a stray DB dump is grep-able back to the
    /// test that produced it; does not affect uniqueness.
    /// </param>
    public static TestAccount New(
        ServerFixture server,
        int status = 100,
        [CallerMemberName] string testName = "")
    {
        var slot = Interlocked.Increment(ref _counter);
        var id = 9_000_000 + slot;
        var username = $"t_{ProcessUid}_{slot:x6}";

        // Truncate formname to varchar(40). Test method names are
        // long ("StarbaseJobTerminalAction7_InNet7SolJtSector_..."),
        // so cap aggressively and prepend a short id chunk so dumps
        // can tie back even when the test name is clipped.
        var formname = (testName.Length > 32
            ? testName.Substring(0, 32)
            : testName);
        if (string.IsNullOrEmpty(formname)) formname = "unknown_test";

        using var conn = new NpgsqlConnection(server.PostgresConnectionString);
        conn.Open();
        using var cmd = new NpgsqlCommand(@"
            INSERT INTO accounts (id, username, password, status, formname, email, warn_level)
            VALUES (@id, @username,
                    UPPER(encode(digest(@password, 'md5'), 'hex')),
                    @status, @formname, @email, 0)",
            conn);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("username", username);
        cmd.Parameters.AddWithValue("password", SharedPassword);
        cmd.Parameters.AddWithValue("status", status);
        cmd.Parameters.AddWithValue("formname", formname);
        cmd.Parameters.AddWithValue("email", $"{username}@net-7.test");
        cmd.ExecuteNonQuery();

        return new TestAccount(id, username, SharedPassword);
    }
}
