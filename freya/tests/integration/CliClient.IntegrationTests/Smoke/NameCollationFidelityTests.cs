// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using Npgsql;
using NpgsqlTypes;
using Xunit;

namespace N7.CliClient.IntegrationTests.Smoke;

/// <summary>
/// Case-insensitive name-collation fidelity guard.
///
/// <para>
/// The source net7_user dump declared every table latin1 with no
/// per-column override, so all string columns collated
/// latin1_swedish_ci -- case-INSENSITIVE. The MySQL->Postgres conversion
/// (db/postgres/convert.sh) strips every COLLATE/CHARSET clause and
/// net7_user is created LC_COLLATE 'C.UTF-8' (byte-exact / case-SENSITIVE),
/// which silently flipped three behaviours: account-username login lookup,
/// character-name uniqueness (AccountManager::IsUsernameUnique), and the
/// forbidden-name filter + PRIMARY KEY (AccountManager::IsForbiddenName) --
/// the last lets a banned name through by recapitalising one letter.
/// </para>
///
/// <para>
/// convert.sh now restores _ci semantics by making the three columns
/// <c>citext</c>. These tests fail the build if a future seed regeneration
/// drops that block (type guard) or if citext stops folding case on the
/// behaviourally load-bearing PRIMARY KEY (behaviour guard). See
/// plans/14-phase-n-libpqxx-rewrite.md Wave 3 and
/// plans/99-decisions-log.md (2026-06-02 addendum 2).
/// </para>
/// </summary>
[Collection(ServerCollection.Name)]
public sealed class NameCollationFidelityTests
{
    private readonly ServerFixture _fx;

    public NameCollationFidelityTests(ServerFixture fx) => _fx = fx;

    [Theory]
    [InlineData("accounts", "username")]
    [InlineData("avatar_data", "first_name")]
    [InlineData("forbidden_names", "nickname")]
    public async Task NameColumn_IsCitext(string table, string column)
    {
        await using var conn = new NpgsqlConnection(_fx.PostgresConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT udt_name FROM information_schema.columns "
          + "WHERE table_name = @t AND column_name = @c", conn);
        cmd.Parameters.AddWithValue("t", table);
        cmd.Parameters.AddWithValue("c", column);
        var udt = (string?)await cmd.ExecuteScalarAsync();

        Assert.Equal("citext", udt);
    }

    [Fact]
    public async Task ForbiddenName_PrimaryKey_IsCaseInsensitive()
    {
        // Process-unique base so concurrent/repeat runs never collide with
        // each other or with real seed data.
        var baseName = $"znt_{Guid.NewGuid():N}"[..16];
        var lower = baseName.ToLowerInvariant();
        var upper = baseName.ToUpperInvariant();

        await using var conn = new NpgsqlConnection(_fx.PostgresConnectionString);
        await conn.OpenAsync();
        try
        {
            await using (var insLower = new NpgsqlCommand(
                "INSERT INTO forbidden_names (nickname) VALUES (@n)", conn))
            {
                insLower.Parameters.AddWithValue("n", lower);
                await insLower.ExecuteNonQueryAsync();
            }

            // The recapitalised duplicate must be rejected by the PK, just as
            // latin1_swedish_ci rejected it on the real server. 23505 ==
            // unique_violation.
            await using var insUpper = new NpgsqlCommand(
                "INSERT INTO forbidden_names (nickname) VALUES (@n)", conn);
            insUpper.Parameters.AddWithValue("n", upper);
            var ex = await Assert.ThrowsAsync<PostgresException>(
                () => insUpper.ExecuteNonQueryAsync());
            Assert.Equal("23505", ex.SqlState);

            // And a differing-case lookup finds the stored row (the
            // IsForbiddenName / IsUsernameUnique path), so the filter is not
            // bypassable by recapitalising a letter.
            //
            // The parameter MUST go out untyped (oid 0). The server talks to
            // Postgres through libpqxx, whose pqxx::params sends every value
            // untyped, so `nickname = $1` resolves against the citext column
            // as citext=citext -- case-INSENSITIVE. Npgsql's default text
            // typing (AddWithValue) would instead force citext=text, which
            // resolves case-SENSITIVE and silently misses the row; that is the
            // exact trap a naive port reintroduces. NpgsqlDbType.Unknown
            // reproduces the server's untyped binding, so this assertion holds
            // only while the column is genuinely citext -- making it a real
            // guard against a silent type revert, not a tautology (a ::citext
            // cast would fold case even if the column reverted to text).
            await using var lookup = new NpgsqlCommand(
                "SELECT count(*) FROM forbidden_names WHERE nickname = @n", conn);
            lookup.Parameters.Add(new NpgsqlParameter("n", NpgsqlDbType.Unknown) { Value = upper });
            Assert.Equal(1L, (long)(await lookup.ExecuteScalarAsync())!);
        }
        finally
        {
            await using var cleanup = new NpgsqlCommand(
                "DELETE FROM forbidden_names WHERE nickname = @n", conn);
            cleanup.Parameters.AddWithValue("n", lower);
            await cleanup.ExecuteNonQueryAsync();
        }
    }
}
