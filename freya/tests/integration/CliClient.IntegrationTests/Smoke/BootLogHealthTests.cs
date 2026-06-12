// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya

using System.Text.RegularExpressions;
using Xunit;

namespace N7.CliClient.IntegrationTests.Smoke;

/// <summary>
/// Boot-log-health guard.
///
/// <para>
/// Three Postgres-migration SQL regressions shipped silently (commits
/// e1976506: npc_Id case-fold breaking ALL starbase NPC loading + a
/// cross-database respawn read; 8f36e7b1: the libpqxx wrapper's compound
/// "table.field" row lookup returning empty for every item's
/// manufacturer/ammo field). They were invisible to this suite because it
/// only asserted opcode round-trips and never that the server booted
/// without SQL errors -- each spammed thousands of error lines per boot
/// while the round-trip tests stayed green.
/// </para>
///
/// <para>
/// A clean volume-wiped boot logs ZERO of the signatures below, so these
/// assertions have a true-zero baseline (verified 2026-06-02). They fail
/// the build if any recur. See plans/14-phase-n-libpqxx-rewrite.md Wave 4
/// and plans/99-decisions-log.md (2026-06-02).
/// </para>
///
/// <para>
/// NB the postgres assertion anchors on the <c>ERROR:</c> severity tag so
/// it does not trip on schema-init's benign
/// <c>NOTICE: ... does not exist, skipping</c> from <c>DROP IF EXISTS</c>.
/// It is further scoped to lines carrying <c>app=net7-server</c> -- the
/// C++ server tags its libpqxx connections <c>application_name=net7-server</c>
/// and docker-compose puts <c>%a</c> in the postgres log_line_prefix -- so
/// ad-hoc <c>psql</c> or pgadmin sharing a dev stack cannot trip it; only
/// the server's own SQL counts. The server assertion matches the exact
/// wrapper/DAO failure strings, NOT the unrelated mission-data-quality
/// lines (which use the contraction "doesn't exist" and are a separate,
/// documented content gap).
/// </para>
/// </summary>
[Collection(ServerCollection.Name)]
public sealed class BootLogHealthTests
{
    private readonly ServerFixture _fx;

    public BootLogHealthTests(ServerFixture fx) => _fx = fx;

    [Fact]
    public async Task Postgres_HasNoSchemaDoesNotExistErrors()
    {
        var log = await _fx.CaptureServiceLogsAsync("postgres");

        var offenders = log
            .Split('\n')
            .Where(l => l.Contains("app=net7-server", StringComparison.Ordinal)
                     && l.Contains("ERROR:", StringComparison.Ordinal)
                     && Regex.IsMatch(l, @"(relation|column)\b.*does not exist",
                                      RegexOptions.IgnoreCase))
            .ToList();

        Assert.True(offenders.Count == 0,
            $"Postgres logged {offenders.Count} schema 'does not exist' ERROR(s) attributed to "
          + "the server (app=net7-server) -- a SQL regression of the case-fold / cross-database "
          + "/ wrong-identifier class (unquoted mixed-case column folds to lowercase; a net7_user "
          + "table read on a net7 connection; a renamed column). First offenders:\n"
          + string.Join("\n", offenders.Take(10)));
    }

    [Fact]
    public async Task Server_HasNoSqlReadFailures()
    {
        var log = await _fx.CaptureServiceLogsAsync("server");

        var offenders = log
            .Split('\n')
            .Where(l => l.Contains("does not exist in this table", StringComparison.OrdinalIgnoreCase)
                     || l.Contains("Error reading with MySQL", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.True(offenders.Count == 0,
            $"Server logged {offenders.Count} SQL read failure(s) -- a libpqxx-wrapper or DAO "
          + "regression. 'does not exist in this table' == the compound table.field row lookup "
          + "broke (sql_result_c::table() not resolving the column's source table); "
          + "'Error reading with MySQL' == a DAO SELECT failed outright. First offenders:\n"
          + string.Join("\n", offenders.Take(10)));
    }
}
