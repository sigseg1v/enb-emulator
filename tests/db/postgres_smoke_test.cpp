// tests/db/postgres_smoke_test.cpp
//
// Phase G scaffold. Connects to Postgres and runs SELECT 1.
//
// Gated by env var NET7_TEST_DB_DSN, e.g.:
//
//     NET7_TEST_DB_DSN='host=127.0.0.1 user=net7 password=net7 dbname=net7' \
//         ctest --test-dir build/tests --output-on-failure
//
// If the env var is unset the test reports SKIPPED — keeps offline builds
// green. If the env var is set but the connection fails, the test fails.

#include <gtest/gtest.h>
#include <cstdlib>
#include <libpq-fe.h>

TEST(PostgresSmoke, ConnectsAndSelectsOne) {
    const char *dsn = std::getenv("NET7_TEST_DB_DSN");
    if (!dsn || !*dsn) {
        GTEST_SKIP() << "NET7_TEST_DB_DSN not set; skipping live DB smoke test";
    }

    PGconn *conn = PQconnectdb(dsn);
    ASSERT_NE(conn, nullptr);
    ASSERT_EQ(PQstatus(conn), CONNECTION_OK)
        << "PQconnectdb failed: " << PQerrorMessage(conn);

    PGresult *res = PQexec(conn, "SELECT 1");
    ASSERT_EQ(PQresultStatus(res), PGRES_TUPLES_OK)
        << "SELECT 1 failed: " << PQerrorMessage(conn);
    ASSERT_EQ(PQntuples(res), 1);
    ASSERT_EQ(PQnfields(res), 1);
    EXPECT_STREQ(PQgetvalue(res, 0, 0), "1");

    PQclear(res);
    PQfinish(conn);
}
