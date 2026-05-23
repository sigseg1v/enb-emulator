# MySQL → Postgres migration pattern (Phase C)

## The good news

Net-7 already abstracts DB access behind the `sql_connection_c` / `sql_query_c` / `sql_result_c` / `sql_row_c` / `sql_var_c` classes (see `server/src/mysql/mysqlplus.h`). The raw `libmysqlclient` C API only appears inside `server/src/mysql/mysqlplus.cpp` (733 lines). Migrating the server to Postgres therefore reduces to **rewriting one file**, with the rest of the codebase tracking transparently.

## The bad news

mysqlplus.cpp exposes MySQL-specific result-set semantics — `MYSQL_RES *`, `MYSQL_ROW`, `MYSQL_FIELD` — through the wrapper's public API in places. Those will need to be papered over with libpqxx-shaped equivalents.

## Strategy: drop-in libpqxx rewrite of the wrapper

Replace `mysqlplus.cpp` internals with `libpqxx` calls, **preserving the existing class API** so no caller changes. The translation table:

| MySQL C API                          | libpqxx equivalent                                          |
|--------------------------------------|-------------------------------------------------------------|
| `MYSQL`, `mysql_init()`              | `pqxx::connection` (RAII)                                   |
| `mysql_real_connect(host, user, …)`  | construct `pqxx::connection("host=… user=… dbname=…")`      |
| `mysql_query(conn, "SELECT …")`      | `pqxx::work tx(conn); tx.exec("SELECT …")`                  |
| `MYSQL_RES *res = mysql_store_result(conn)` | `pqxx::result res = tx.exec("…")`                    |
| `mysql_num_rows(res)`                | `res.size()`                                                |
| `mysql_num_fields(res)`              | `res.columns()`                                             |
| `mysql_fetch_row(res)`               | iterate `for (auto row : res)`                              |
| `row[i]`                             | `row[i].c_str()` (or `.as<T>()` for typed access)           |
| `mysql_fetch_field_direct(res, i)`   | `res.column_name(i)`, `res.column_type(i)`                  |
| `mysql_errno(conn)` / `mysql_error()`| catch `pqxx::sql_error` / `pqxx::failure`                   |
| `mysql_escape_string(buf, s, len)`   | `pqxx::work::esc(s)`                                        |
| `mysql_affected_rows(conn)`          | `tx.exec_params(…).affected_rows()`                         |

## Worked example: `sql_connection_c::connect`

### Before (MySQL)

```cpp
void sql_connection_c::connect(char *database, char *host, char *user, char *password) {
    this->host     = strdup(host);
    this->user     = strdup(user);
    this->password = strdup(password);
    this->database = strdup(database);
}

OPENDB *sql_connection_c::grabdb() {
    m_Mutex.Lock();
    // walk the linked list looking for a free OPENDB
    OPENDB *odb = opendbbase;
    while (odb && odb->busy) odb = odb->next;
    if (!odb) {
        odb = new OPENDB; odb->next = opendbbase; opendbbase = odb;
        mysql_init(&odb->mysql);
        my_bool reconnect = 1;
        mysql_options(&odb->mysql, MYSQL_OPT_RECONNECT, &reconnect);
        mysql_real_connect(&odb->mysql, host, user, password,
                           database, portn, NULL, 0);
    }
    odb->busy = true;
    m_Mutex.Unlock();
    return odb;
}
```

### After (Postgres / libpqxx)

```cpp
#include <pqxx/pqxx>

void sql_connection_c::connect(char *database, char *host, char *user, char *password) {
    char conn_str[1024];
    snprintf(conn_str, sizeof(conn_str),
             "host=%s user=%s password=%s dbname=%s",
             host, user, password ? password : "", database);
    // Stash the string; actual connection objects are created on grab.
    this->conn_str = strdup(conn_str);
}

OPENDB *sql_connection_c::grabdb() {
    m_Mutex.Lock();
    OPENDB *odb = opendbbase;
    while (odb && odb->busy) odb = odb->next;
    if (!odb) {
        odb = new OPENDB;
        odb->next = opendbbase; opendbbase = odb;
        try {
            odb->conn = new pqxx::connection(conn_str);  // RAII
        } catch (const pqxx::failure &e) {
            LogMessage("Postgres connect failed: %s\n", e.what());
            delete odb;
            m_Mutex.Unlock();
            return nullptr;
        }
    }
    odb->busy = true;
    m_Mutex.Unlock();
    return odb;
}
```

The OPENDB struct becomes:
```cpp
typedef struct opendbstruct {
    struct opendbstruct *next;
    pqxx::connection    *conn;   // was MYSQL mysql;
    bool busy;
} OPENDB;
```

## Per-class scope

| Class             | Lines | Complexity | Notes                                     |
|-------------------|-------|------------|-------------------------------------------|
| `sql_connection_c`| 80    | low        | swap `MYSQL` for `pqxx::connection`       |
| `sql_query_c`     | 90    | medium     | `tx.exec()` instead of `mysql_query`      |
| `sql_result_c`    | 65    | medium     | wrap `pqxx::result`, expose row count/access |
| `sql_row_c`       | 90    | medium     | wrap `pqxx::row`, indexed/named access    |
| `sql_var_c`       | 110   | low        | `pqxx::field::as<T>()` for each conversion |
| `sql_field_c`     | 35    | low        | `res.column_name(i)`, `res.column_type(i)`|
| `sql_query` (no _c)| 200  | low        | INSERT builder; just rewrite escape       |

Estimate: 2–3 dev days for a fluent libpqxx user; 1 week for someone learning libpqxx as they go. Phase C continuation should track this as the next big item.

## Build wiring

`server/CMakeLists.txt` already has `find_package(PostgreSQL QUIET)` and `find_library(PQXX_LIB pqxx)` via Phase A scaffolding. The build image already installs `libpqxx-dev`. Once the rewrite lands:

1. Replace `target_link_libraries(net7 PRIVATE ${MYSQLCLIENT_LIB})` with `target_link_libraries(net7 PRIVATE pqxx pq)`.
2. Drop `libmysqlclient-dev` from `server/Dockerfile`.
3. Drop the `find_library(MYSQLCLIENT_LIB …)` block from CMakeLists.

## Schema status (Phase A wrote this; Phase C validated it)

`db/postgres/schema.sql` creates **71 of 71 tables cleanly** on Postgres 16. The only conversion residuals are the two MySQL stored functions `isAccLoggedIn` and `isAvaLoggedIn` (DELIMITER/DEFINER/IF-THEN syntax). These are tracked in `db/postgres/README.md` and rewrite as PL/pgSQL is a Phase C continuation item.
