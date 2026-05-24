// tests/db/mysqlplus_wrapper_test.cpp
//
// Phase N: round-trip the libpqxx-backed mysqlplus wrapper end-to-end
// through the same public API the server uses (sql_connection_c →
// sql_query_c → sql_result_c → sql_row_c → sql_var_c).
//
// Gated by env var NET7_TEST_DB_DSN with libpq keyword/value form, e.g.:
//     NET7_TEST_DB_DSN='host=127.0.0.1 port=5433 user=net7 password=net7 dbname=net7'
//
// The wrapper takes its inputs split across (database, host[:port], user,
// password) so we parse the DSN here. Unset -> skip.

#include <gtest/gtest.h>
#include <cstdlib>
#include <cstring>
#include <string>
#include "mysql/mysqlplus.h"

namespace {

struct Dsn {
    std::string host;       // includes optional :port
    std::string user;
    std::string password;
    std::string dbname;
};

bool parse_kv_dsn(const char *dsn, Dsn &out)
{
    if (!dsn || !*dsn) return false;
    std::string port;
    std::string tok;
    auto flush = [&](std::string &t) {
        auto eq = t.find('=');
        if (eq == std::string::npos) return;
        std::string k = t.substr(0, eq);
        std::string v = t.substr(eq + 1);
        if      (k == "host")     out.host     = v;
        else if (k == "user")     out.user     = v;
        else if (k == "password") out.password = v;
        else if (k == "dbname")   out.dbname   = v;
        else if (k == "port")     port         = v;
        t.clear();
    };
    for (const char *p = dsn; *p; ++p) {
        if (*p == ' ') flush(tok);
        else           tok.push_back(*p);
    }
    flush(tok);
    if (!port.empty()) {
        if (out.host.empty()) out.host = "127.0.0.1";
        out.host += ":";
        out.host += port;
    }
    return !out.host.empty() && !out.dbname.empty();
}

} // namespace

TEST(MysqlplusWrapper, ConnectsAndSelectsOne) {
    const char *raw = std::getenv("NET7_TEST_DB_DSN");
    if (!raw || !*raw) {
        GTEST_SKIP() << "NET7_TEST_DB_DSN not set; skipping live wrapper test";
    }
    Dsn dsn;
    ASSERT_TRUE(parse_kv_dsn(raw, dsn)) << "could not parse DSN: " << raw;

    sql_connection_c conn(
        const_cast<char *>(dsn.dbname.c_str()),
        const_cast<char *>(dsn.host.c_str()),
        dsn.user.empty()     ? nullptr : const_cast<char *>(dsn.user.c_str()),
        dsn.password.empty() ? nullptr : const_cast<char *>(dsn.password.c_str()));
    ASSERT_TRUE(conn.connected()) << "wrapper failed to connect";

    sql_query_c q(&conn);
    char sql[] = "SELECT 1";
    ASSERT_NE(q.execute(sql), 0)
        << "execute failed: errno=" << q.Error() << " msg=" << q.ErrorMsg();

    sql_result_c result;
    q.store(&result);
    ASSERT_EQ(result.n_rows(), 1u);
    ASSERT_EQ(result.n_fields(), 1u);

    sql_row_c row;
    result.fetch_row(&row);
    EXPECT_EQ((int)row[0], 1);
}

TEST(MysqlplusWrapper, ParameterisedRoundTrip) {
    const char *raw = std::getenv("NET7_TEST_DB_DSN");
    if (!raw || !*raw) {
        GTEST_SKIP() << "NET7_TEST_DB_DSN not set; skipping live wrapper test";
    }
    Dsn dsn;
    ASSERT_TRUE(parse_kv_dsn(raw, dsn));

    sql_connection_c conn(
        const_cast<char *>(dsn.dbname.c_str()),
        const_cast<char *>(dsn.host.c_str()),
        dsn.user.empty()     ? nullptr : const_cast<char *>(dsn.user.c_str()),
        dsn.password.empty() ? nullptr : const_cast<char *>(dsn.password.c_str()));
    ASSERT_TRUE(conn.connected());

    sql_query_c q(&conn);
    // Exercise multi-column / multi-row + char* round-trip.
    char sql[] = "SELECT id, name FROM (VALUES (1, 'alpha'), (2, 'beta')) AS t(id, name) ORDER BY id";
    ASSERT_NE(q.execute(sql), 0)
        << "execute failed: errno=" << q.Error() << " msg=" << q.ErrorMsg();

    sql_result_c result;
    q.store(&result);
    ASSERT_EQ(result.n_rows(), 2u);
    ASSERT_EQ(result.n_fields(), 2u);

    sql_row_c row;
    result.fetch_row(&row);
    EXPECT_EQ((int)row[0], 1);
    EXPECT_STREQ((const char *)row[1], "alpha");

    result.fetch_row(&row);
    EXPECT_EQ((int)row[0], 2);
    EXPECT_STREQ((const char *)row[1], "beta");
}

TEST(MysqlplusWrapper, EscapeHostileLiteral) {
    // Smoke the escape shim — hostile single quotes / backslashes must
    // round-trip as literal data.
    const char *raw = std::getenv("NET7_TEST_DB_DSN");
    if (!raw || !*raw) {
        GTEST_SKIP() << "NET7_TEST_DB_DSN not set; skipping live wrapper test";
    }
    Dsn dsn;
    ASSERT_TRUE(parse_kv_dsn(raw, dsn));

    sql_connection_c conn(
        const_cast<char *>(dsn.dbname.c_str()),
        const_cast<char *>(dsn.host.c_str()),
        dsn.user.empty()     ? nullptr : const_cast<char *>(dsn.user.c_str()),
        dsn.password.empty() ? nullptr : const_cast<char *>(dsn.password.c_str()));
    ASSERT_TRUE(conn.connected());

    const char hostile_input[] = "'; DROP TABLE accounts; --";
    char escaped[256];
    unsigned long n = mysql_escape_string(escaped, hostile_input, std::strlen(hostile_input));
    ASSERT_GT(n, std::strlen(hostile_input));   // single quote doubled

    char sql[512];
    std::snprintf(sql, sizeof(sql), "SELECT '%s'", escaped);

    sql_query_c q(&conn);
    ASSERT_NE(q.execute(sql), 0)
        << "execute failed: errno=" << q.Error() << " msg=" << q.ErrorMsg();

    sql_result_c result;
    q.store(&result);
    ASSERT_EQ(result.n_rows(), 1u);

    sql_row_c row;
    result.fetch_row(&row);
    EXPECT_STREQ((const char *)row[0], hostile_input);
}
