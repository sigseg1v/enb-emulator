// tests/server/db/sqlplus_wrapper_test.cpp
//
// Phase N: round-trip the libpqxx-backed sqlplus wrapper end-to-end
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
#include "db/sqlplus.h"

namespace {

struct Dsn {
    std::string host; // includes optional :port
    std::string user;
    std::string password;
    std::string dbname;
};

bool parse_kv_dsn(const char* dsn, Dsn& out) {
    if (!dsn || !*dsn)
        return false;
    std::string port;
    std::string tok;
    auto flush = [&](std::string& t) {
        auto eq = t.find('=');
        if (eq == std::string::npos)
            return;
        std::string k = t.substr(0, eq);
        std::string v = t.substr(eq + 1);
        if (k == "host")
            out.host = v;
        else if (k == "user")
            out.user = v;
        else if (k == "password")
            out.password = v;
        else if (k == "dbname")
            out.dbname = v;
        else if (k == "port")
            port = v;
        t.clear();
    };
    for (const char* p = dsn; *p; ++p) {
        if (*p == ' ')
            flush(tok);
        else
            tok.push_back(*p);
    }
    flush(tok);
    if (!port.empty()) {
        if (out.host.empty())
            out.host = "127.0.0.1";
        out.host += ":";
        out.host += port;
    }
    return !out.host.empty() && !out.dbname.empty();
}

} // namespace

TEST(SqlplusWrapper, ConnectsAndSelectsOne) {
    const char* raw = std::getenv("NET7_TEST_DB_DSN");
    if (!raw || !*raw) {
        GTEST_SKIP() << "NET7_TEST_DB_DSN not set; skipping live wrapper test";
    }
    Dsn dsn;
    ASSERT_TRUE(parse_kv_dsn(raw, dsn)) << "could not parse DSN: " << raw;

    sql_connection_c conn(const_cast<char*>(dsn.dbname.c_str()),
                          const_cast<char*>(dsn.host.c_str()),
                          dsn.user.empty() ? nullptr : const_cast<char*>(dsn.user.c_str()),
                          dsn.password.empty() ? nullptr : const_cast<char*>(dsn.password.c_str()));
    ASSERT_TRUE(conn.connected()) << "wrapper failed to connect";

    sql_query_c q(&conn);
    char sql[] = "SELECT 1";
    ASSERT_NE(q.execute(sql), 0) << "execute failed: errno=" << q.Error()
                                 << " msg=" << q.ErrorMsg();

    sql_result_c result;
    q.store(&result);
    ASSERT_EQ(result.n_rows(), 1u);
    ASSERT_EQ(result.n_fields(), 1u);

    sql_row_c row;
    result.fetch_row(&row);
    EXPECT_EQ((int)row[0], 1);
}

TEST(SqlplusWrapper, ParameterisedRoundTrip) {
    const char* raw = std::getenv("NET7_TEST_DB_DSN");
    if (!raw || !*raw) {
        GTEST_SKIP() << "NET7_TEST_DB_DSN not set; skipping live wrapper test";
    }
    Dsn dsn;
    ASSERT_TRUE(parse_kv_dsn(raw, dsn));

    sql_connection_c conn(const_cast<char*>(dsn.dbname.c_str()),
                          const_cast<char*>(dsn.host.c_str()),
                          dsn.user.empty() ? nullptr : const_cast<char*>(dsn.user.c_str()),
                          dsn.password.empty() ? nullptr : const_cast<char*>(dsn.password.c_str()));
    ASSERT_TRUE(conn.connected());

    sql_query_c q(&conn);
    // Exercise multi-column / multi-row + char* round-trip.
    char sql[] =
        "SELECT id, name FROM (VALUES (1, 'alpha'), (2, 'beta')) AS t(id, name) ORDER BY id";
    ASSERT_NE(q.execute(sql), 0) << "execute failed: errno=" << q.Error()
                                 << " msg=" << q.ErrorMsg();

    sql_result_c result;
    q.store(&result);
    ASSERT_EQ(result.n_rows(), 2u);
    ASSERT_EQ(result.n_fields(), 2u);

    sql_row_c row;
    result.fetch_row(&row);
    EXPECT_EQ((int)row[0], 1);
    EXPECT_STREQ((const char*)row[1], "alpha");

    result.fetch_row(&row);
    EXPECT_EQ((int)row[0], 2);
    EXPECT_STREQ((const char*)row[1], "beta");
}

TEST(SqlplusWrapper, ExecuteParamsHostileLiteral) {
    // The whole point of execute_params(): a hostile string arriving as a
    // bound parameter must round-trip as literal data, never as SQL.
    // If injection ever became possible, this query would try (and fail)
    // to drop a real table; even a syntax error would still surface here
    // as a different observation than "returned the literal back".
    const char* raw = std::getenv("NET7_TEST_DB_DSN");
    if (!raw || !*raw) {
        GTEST_SKIP() << "NET7_TEST_DB_DSN not set; skipping live wrapper test";
    }
    Dsn dsn;
    ASSERT_TRUE(parse_kv_dsn(raw, dsn));

    sql_connection_c conn(const_cast<char*>(dsn.dbname.c_str()),
                          const_cast<char*>(dsn.host.c_str()),
                          dsn.user.empty() ? nullptr : const_cast<char*>(dsn.user.c_str()),
                          dsn.password.empty() ? nullptr : const_cast<char*>(dsn.password.c_str()));
    ASSERT_TRUE(conn.connected());

    sql_query_c q(&conn);
    const char hostile[] = "'; DROP TABLE accounts; --";
    q.AddParam(42);
    q.AddParam(hostile);
    ASSERT_NE(q.execute_params("SELECT ?::int AS id, ?::text AS name"), 0)
        << "execute_params failed: errno=" << q.Error() << " msg=" << q.ErrorMsg();

    sql_result_c result;
    q.store(&result);
    ASSERT_EQ(result.n_rows(), 1u);
    ASSERT_EQ(result.n_fields(), 2u);

    sql_row_c row;
    result.fetch_row(&row);
    EXPECT_EQ((int)row[0], 42);
    EXPECT_STREQ((const char*)row[1], hostile);
}

TEST(SqlplusWrapper, ExecuteParamsMixedTypesAndNull) {
    // Exercise int / unsigned long / double / NULL via the bag, and the
    // placeholder rewriter's ability to hand out $1..$N in order.
    const char* raw = std::getenv("NET7_TEST_DB_DSN");
    if (!raw || !*raw) {
        GTEST_SKIP() << "NET7_TEST_DB_DSN not set; skipping live wrapper test";
    }
    Dsn dsn;
    ASSERT_TRUE(parse_kv_dsn(raw, dsn));

    sql_connection_c conn(const_cast<char*>(dsn.dbname.c_str()),
                          const_cast<char*>(dsn.host.c_str()),
                          dsn.user.empty() ? nullptr : const_cast<char*>(dsn.user.c_str()),
                          dsn.password.empty() ? nullptr : const_cast<char*>(dsn.password.c_str()));
    ASSERT_TRUE(conn.connected());

    sql_query_c q(&conn);
    q.AddParam(7);
    // High-bit value above 0x80000000. sql_var_c::operator unsigned long()
    // now reads via strtoul; the original (unsigned long)atoi() truncated to
    // a 32-bit (signed) int and sign-extended anything >= 0x80000000 to a
    // bogus huge unsigned long. This binds + reads back 0xFFFFFFFE to lock
    // that read-side fix in (Phase N pre-existing-bug item).
    q.AddParam((unsigned long)0xFFFFFFFEUL);
    q.AddParam(3.5);
    q.AddParamNull();
    ASSERT_NE(q.execute_params("SELECT ?::int, ?::bigint, ?::double precision, ?::text IS NULL"), 0)
        << "execute_params failed: errno=" << q.Error() << " msg=" << q.ErrorMsg();

    sql_result_c result;
    q.store(&result);
    ASSERT_EQ(result.n_rows(), 1u);

    sql_row_c row;
    result.fetch_row(&row);
    EXPECT_EQ((int)row[0], 7);
    EXPECT_EQ((unsigned long)row[1], 0xFFFFFFFEUL);
    EXPECT_DOUBLE_EQ((double)row[2], 3.5);
    EXPECT_STREQ((const char*)row[3], "t"); // Postgres bool true → "t"
}

///////////////////////////////////////////////////////////////////////////////
// Multi-statement transactions (begin/commit/rollback).
//
// These back the SaveManager atomic inventory-move fix: one logical vault move
// is two slot writes (source + destination) that MUST commit together or not at
// all, so a crash between them can neither duplicate the item (in both slots)
// nor lose it (in neither). The wrapper itself is what guarantees that, so it is
// what these pin. All work runs on ONE sql_query_c so it reuses one pooled
// connection (and one session temp table).

TEST(SqlplusWrapperTx, CommitPersistsBothWrites) {
    const char* raw = std::getenv("NET7_TEST_DB_DSN");
    if (!raw || !*raw)
        GTEST_SKIP() << "NET7_TEST_DB_DSN not set";
    Dsn dsn;
    ASSERT_TRUE(parse_kv_dsn(raw, dsn));
    sql_connection_c conn(const_cast<char*>(dsn.dbname.c_str()),
                          const_cast<char*>(dsn.host.c_str()),
                          dsn.user.empty() ? nullptr : const_cast<char*>(dsn.user.c_str()),
                          dsn.password.empty() ? nullptr : const_cast<char*>(dsn.password.c_str()));
    ASSERT_TRUE(conn.connected());

    sql_query_c q(&conn);
    char create[] = "CREATE TEMP TABLE tx_commit (k int, v int)";
    ASSERT_NE(q.execute(create), 0) << q.ErrorMsg();

    ASSERT_TRUE(q.begin()) << q.ErrorMsg();
    EXPECT_TRUE(q.in_transaction());
    q.AddParam(1);
    q.AddParam(10);
    ASSERT_NE(q.execute_params("INSERT INTO tx_commit VALUES (?, ?)"), 0) << q.ErrorMsg();
    q.AddParam(2);
    q.AddParam(20);
    ASSERT_NE(q.execute_params("INSERT INTO tx_commit VALUES (?, ?)"), 0) << q.ErrorMsg();
    ASSERT_TRUE(q.commit()) << q.ErrorMsg();
    EXPECT_FALSE(q.in_transaction());

    char count[] = "SELECT count(*) FROM tx_commit";
    ASSERT_NE(q.execute(count), 0);
    sql_result_c r;
    q.store(&r);
    sql_row_c row;
    r.fetch_row(&row);
    EXPECT_EQ((int)row[0], 2) << "both writes must survive a commit";
}

TEST(SqlplusWrapperTx, RollbackDiscardsWrites) {
    const char* raw = std::getenv("NET7_TEST_DB_DSN");
    if (!raw || !*raw)
        GTEST_SKIP() << "NET7_TEST_DB_DSN not set";
    Dsn dsn;
    ASSERT_TRUE(parse_kv_dsn(raw, dsn));
    sql_connection_c conn(const_cast<char*>(dsn.dbname.c_str()),
                          const_cast<char*>(dsn.host.c_str()),
                          dsn.user.empty() ? nullptr : const_cast<char*>(dsn.user.c_str()),
                          dsn.password.empty() ? nullptr : const_cast<char*>(dsn.password.c_str()));
    ASSERT_TRUE(conn.connected());

    sql_query_c q(&conn);
    char create[] = "CREATE TEMP TABLE tx_rollback (k int, v int)";
    ASSERT_NE(q.execute(create), 0) << q.ErrorMsg();

    ASSERT_TRUE(q.begin()) << q.ErrorMsg();
    q.AddParam(1);
    q.AddParam(10);
    ASSERT_NE(q.execute_params("INSERT INTO tx_rollback VALUES (?, ?)"), 0) << q.ErrorMsg();
    q.rollback();
    EXPECT_FALSE(q.in_transaction());

    char count[] = "SELECT count(*) FROM tx_rollback";
    ASSERT_NE(q.execute(count), 0) << q.ErrorMsg(); // connection is usable again
    sql_result_c r;
    q.store(&r);
    sql_row_c row;
    r.fetch_row(&row);
    EXPECT_EQ((int)row[0], 0) << "a rolled-back write must leave nothing";
}

TEST(SqlplusWrapperTx, FailedSecondWriteLeavesNothing) {
    // The exact dup/loss scenario: first slot write succeeds, second fails. The
    // whole move must roll back so the DB never shows a half-applied move.
    const char* raw = std::getenv("NET7_TEST_DB_DSN");
    if (!raw || !*raw)
        GTEST_SKIP() << "NET7_TEST_DB_DSN not set";
    Dsn dsn;
    ASSERT_TRUE(parse_kv_dsn(raw, dsn));
    sql_connection_c conn(const_cast<char*>(dsn.dbname.c_str()),
                          const_cast<char*>(dsn.host.c_str()),
                          dsn.user.empty() ? nullptr : const_cast<char*>(dsn.user.c_str()),
                          dsn.password.empty() ? nullptr : const_cast<char*>(dsn.password.c_str()));
    ASSERT_TRUE(conn.connected());

    sql_query_c q(&conn);
    char create[] = "CREATE TEMP TABLE tx_fail (k int, v int)";
    ASSERT_NE(q.execute(create), 0) << q.ErrorMsg();

    ASSERT_TRUE(q.begin()) << q.ErrorMsg();
    q.AddParam(1);
    q.AddParam(10);
    ASSERT_NE(q.execute_params("INSERT INTO tx_fail VALUES (?, ?)"), 0) << q.ErrorMsg();
    // Second write is a type error -- it aborts the backend transaction.
    q.AddParam(2);
    EXPECT_EQ(q.execute_params("INSERT INTO tx_fail VALUES (?, 'not-an-int')"), 0);
    EXPECT_GT(q.Error(), 0u);
    q.rollback();

    char count[] = "SELECT count(*) FROM tx_fail";
    ASSERT_NE(q.execute(count), 0) << q.ErrorMsg();
    sql_result_c r;
    q.store(&r);
    sql_row_c row;
    r.fetch_row(&row);
    EXPECT_EQ((int)row[0], 0) << "a half-failed move must persist neither slot";
}

TEST(SqlplusWrapperTx, DestructorRollsBackOpenTransactionSoPoolStaysClean) {
    // A begin() with no commit()/rollback() (an early-return bug) must not hand
    // a connection back to the pool mid-transaction. The query destructor rolls
    // it back; the next borrower of that pooled connection must work normally.
    const char* raw = std::getenv("NET7_TEST_DB_DSN");
    if (!raw || !*raw)
        GTEST_SKIP() << "NET7_TEST_DB_DSN not set";
    Dsn dsn;
    ASSERT_TRUE(parse_kv_dsn(raw, dsn));
    sql_connection_c conn(const_cast<char*>(dsn.dbname.c_str()),
                          const_cast<char*>(dsn.host.c_str()),
                          dsn.user.empty() ? nullptr : const_cast<char*>(dsn.user.c_str()),
                          dsn.password.empty() ? nullptr : const_cast<char*>(dsn.password.c_str()));
    ASSERT_TRUE(conn.connected());

    {
        sql_query_c q(&conn);
        ASSERT_TRUE(q.begin()) << q.ErrorMsg();
        ASSERT_NE(q.execute(const_cast<char*>("SELECT 1")), 0) << q.ErrorMsg();
        // q goes out of scope WITHOUT commit/rollback: destructor must clean up.
    }

    // Reuses the same pooled connection -- it must not be stuck in a transaction.
    sql_query_c q2(&conn);
    EXPECT_FALSE(q2.in_transaction());
    ASSERT_NE(q2.execute(const_cast<char*>("SELECT 2")), 0)
        << "pooled connection was left mid-transaction: " << q2.ErrorMsg();
    sql_result_c r;
    q2.store(&r);
    sql_row_c row;
    r.fetch_row(&row);
    EXPECT_EQ((int)row[0], 2);
}

///////////////////////////////////////////////////////////////////////////////
// Variable-length (N-slot) atomic move.
//
// A vault->cargo auto-stack can spread ONE vault stack across several cargo
// slots, so the move is one source write plus N destination writes -- not a
// fixed pair. SaveManager::HandleMoveInventory commits all N in one transaction
// (Player::SaveInventoryMoveSlots emits them as one SAVE_CODE_MOVE_INVENTORY of
// N back-to-back 86-byte records). These pin the all-or-nothing property for an
// arbitrary record count, which is what keeps a wide move from half-applying.

TEST(SqlplusWrapperTx, MultiSlotMoveCommitsAllOrNothing) {
    const char* raw = std::getenv("NET7_TEST_DB_DSN");
    if (!raw || !*raw)
        GTEST_SKIP() << "NET7_TEST_DB_DSN not set";
    Dsn dsn;
    ASSERT_TRUE(parse_kv_dsn(raw, dsn));
    sql_connection_c conn(const_cast<char*>(dsn.dbname.c_str()),
                          const_cast<char*>(dsn.host.c_str()),
                          dsn.user.empty() ? nullptr : const_cast<char*>(dsn.user.c_str()),
                          dsn.password.empty() ? nullptr : const_cast<char*>(dsn.password.c_str()));
    ASSERT_TRUE(conn.connected());

    sql_query_c q(&conn);
    char create[] = "CREATE TEMP TABLE tx_multi (k int, v int)";
    ASSERT_NE(q.execute(create), 0) << q.ErrorMsg();

    // One source slot + four destination slots == a 5-record move.
    const int kRecords = 5;
    ASSERT_TRUE(q.begin()) << q.ErrorMsg();
    for (int i = 0; i < kRecords; i++) {
        q.AddParam(i);
        q.AddParam(i * 10);
        ASSERT_NE(q.execute_params("INSERT INTO tx_multi VALUES (?, ?)"), 0)
            << "record " << i << ": " << q.ErrorMsg();
    }
    ASSERT_TRUE(q.commit()) << q.ErrorMsg();

    char count[] = "SELECT count(*) FROM tx_multi";
    ASSERT_NE(q.execute(count), 0);
    sql_result_c r;
    q.store(&r);
    sql_row_c row;
    r.fetch_row(&row);
    EXPECT_EQ((int)row[0], kRecords) << "every slot of an N-slot move must survive a commit";
}

TEST(SqlplusWrapperTx, MultiSlotMovePartialFailureRollsBackEverySlot) {
    // A wide move where the LAST destination write fails (e.g. a constraint or
    // type error on one slot) must leave the DB with zero slots applied -- never
    // the source emptied with only some destinations written, which would lose
    // part of the stack.
    const char* raw = std::getenv("NET7_TEST_DB_DSN");
    if (!raw || !*raw)
        GTEST_SKIP() << "NET7_TEST_DB_DSN not set";
    Dsn dsn;
    ASSERT_TRUE(parse_kv_dsn(raw, dsn));
    sql_connection_c conn(const_cast<char*>(dsn.dbname.c_str()),
                          const_cast<char*>(dsn.host.c_str()),
                          dsn.user.empty() ? nullptr : const_cast<char*>(dsn.user.c_str()),
                          dsn.password.empty() ? nullptr : const_cast<char*>(dsn.password.c_str()));
    ASSERT_TRUE(conn.connected());

    sql_query_c q(&conn);
    char create[] = "CREATE TEMP TABLE tx_multi_fail (k int, v int)";
    ASSERT_NE(q.execute(create), 0) << q.ErrorMsg();

    ASSERT_TRUE(q.begin()) << q.ErrorMsg();
    for (int i = 0; i < 3; i++) {
        q.AddParam(i);
        q.AddParam(i * 10);
        ASSERT_NE(q.execute_params("INSERT INTO tx_multi_fail VALUES (?, ?)"), 0)
            << "record " << i << ": " << q.ErrorMsg();
    }
    // Fourth slot write is a type error -- aborts the backend transaction.
    q.AddParam(3);
    EXPECT_EQ(q.execute_params("INSERT INTO tx_multi_fail VALUES (?, 'not-an-int')"), 0);
    EXPECT_GT(q.Error(), 0u);
    q.rollback();

    char count[] = "SELECT count(*) FROM tx_multi_fail";
    ASSERT_NE(q.execute(count), 0) << q.ErrorMsg();
    sql_result_c r;
    q.store(&r);
    sql_row_c row;
    r.fetch_row(&row);
    EXPECT_EQ((int)row[0], 0) << "a partly-failed N-slot move must persist no slot at all";
}

TEST(SqlplusWrapperTx, WidestMoveRecordCountCommitsInOneTransaction) {
    // The cap-sized batch (SAVE_MOVE_MAX_RECORDS == 15) must be a valid single
    // transaction; anything wider falls back to per-slot saves at the call site.
    const char* raw = std::getenv("NET7_TEST_DB_DSN");
    if (!raw || !*raw)
        GTEST_SKIP() << "NET7_TEST_DB_DSN not set";
    Dsn dsn;
    ASSERT_TRUE(parse_kv_dsn(raw, dsn));
    sql_connection_c conn(const_cast<char*>(dsn.dbname.c_str()),
                          const_cast<char*>(dsn.host.c_str()),
                          dsn.user.empty() ? nullptr : const_cast<char*>(dsn.user.c_str()),
                          dsn.password.empty() ? nullptr : const_cast<char*>(dsn.password.c_str()));
    ASSERT_TRUE(conn.connected());

    sql_query_c q(&conn);
    char create[] = "CREATE TEMP TABLE tx_widest (k int)";
    ASSERT_NE(q.execute(create), 0) << q.ErrorMsg();

    const int kMax = 15; // keep in lockstep with SAVE_MOVE_MAX_RECORDS
    ASSERT_TRUE(q.begin()) << q.ErrorMsg();
    for (int i = 0; i < kMax; i++) {
        q.AddParam(i);
        ASSERT_NE(q.execute_params("INSERT INTO tx_widest VALUES (?)"), 0)
            << "record " << i << ": " << q.ErrorMsg();
    }
    ASSERT_TRUE(q.commit()) << q.ErrorMsg();

    char count[] = "SELECT count(*) FROM tx_widest";
    ASSERT_NE(q.execute(count), 0);
    sql_result_c r;
    q.store(&r);
    sql_row_c row;
    r.fetch_row(&row);
    EXPECT_EQ((int)row[0], kMax) << "the widest permitted move must commit as one unit";
}
