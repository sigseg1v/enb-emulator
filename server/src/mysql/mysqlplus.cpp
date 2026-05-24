///////////////////////////////////////////////////////////////////////////////////////
//// mysqlplus.cpp
///////////////////////////////////////////////////////////////////////////////////////
//
// Phase N: libpqxx-backed reimplementation of the 7-class wrapper that
// the entire server DAO layer consumes. The public API (sql_connection_c,
// sql_query_c, sql_result_c, sql_row_c, sql_var_c, sql_field_c, sql_query)
// is preserved byte-for-byte so callers do not change. Internals now use
// pqxx::connection / pqxx::work / pqxx::result.
//
// What this rewrite does NOT do (anti-scope per plans/14-phase-n):
//   - Rewrite DAOs (AssetDatabaseSQL.cpp, etc.) — they keep their SQL.
//   - Translate MySQL SQL dialect to Postgres dialect on the fly. Backticks
//     used by `sql_query::CreateQuery` are emitted as double-quotes
//     (Postgres identifier quoting). Stored-procedure CALLs and other
//     MySQL-isms in DAO query strings are the responsibility of a later
//     DAO-migration pass.

#include "mysqlplus.h"
#include "Net7.h"
#include <stdio.h>
#include <assert.h>
#include <string.h>
#include <stdlib.h>
#include <stdexcept>
#include <string>
#include <pqxx/pqxx>

///////////////////////////////////////////////////////////////////////////////////////
//// Internal types

struct net7_db_handle {
    pqxx::connection *conn;
    std::string       last_error;
    unsigned int      last_errno;
};

struct net7_result_holder {
    pqxx::result      result;
    my_ulonglong      affected_rows;   // for INSERT/UPDATE/DELETE
};

// Accumulator for parameterised execute_params(). Hides pqxx::params from
// the public header so callers do not need libpqxx on their include path.
struct sql_param_bag {
    pqxx::params p;
};

namespace {

// Translate caller-facing `?` placeholders to Postgres `$N` numbered
// placeholders. `?` inside single- or double-quoted SQL literals is left
// alone — though parameterised callers shouldn't be writing literals
// anyway, this keeps the translation accidentally-safe if they do.
std::string translate_placeholders(const char *sql, std::size_t &out_n)
{
    out_n = 0;
    if (!sql) return {};
    std::string out;
    out.reserve(strlen(sql) + 16);
    char in_str = 0;        // 0, '\'' or '"' depending on which literal we are inside
    for (const char *p = sql; *p; ++p) {
        char c = *p;
        if (in_str) {
            out.push_back(c);
            if (c == in_str) {
                // Postgres SQL: doubled quote escapes; peek ahead.
                if (*(p + 1) == in_str) { out.push_back(*(++p)); }
                else                     { in_str = 0; }
            }
        } else if (c == '\'' || c == '"') {
            in_str = c;
            out.push_back(c);
        } else if (c == '?') {
            ++out_n;
            out.push_back('$');
            char buf[16];
            std::snprintf(buf, sizeof(buf), "%zu", out_n);
            out.append(buf);
        } else {
            out.push_back(c);
        }
    }
    return out;
}

// Translate a MySQL-flavoured query string into one Postgres can parse.
// Limited to: backtick identifiers -> double-quoted identifiers. The
// project's queries are 99% plain SQL plus the occasional backtick from
// the sql_query INSERT builder. Stored-procedure CALLs and dialect
// differences in vendor SQL remain caller-side concerns (Phase N+).
std::string translate_dialect(const char *sql)
{
    if (!sql) return {};
    std::string out;
    out.reserve(strlen(sql) + 16);
    for (const char *p = sql; *p; ++p) {
        if (*p == '`') out.push_back('"');
        else            out.push_back(*p);
    }
    return out;
}

} // namespace

///////////////////////////////////////////////////////////////////////////////////////
//// sql_connection_c

sql_connection_c::sql_connection_c()
{
    host = 0;
    user = 0;
    password = 0;
    database = 0;
    opendbbase = 0;
    portn = 5432;       // Postgres default; overridden by host:port parsing
}

sql_connection_c::sql_connection_c(char *__database, char *__host, char *__user, char *__password)
{
    host = 0;
    user = 0;
    password = 0;
    database = 0;
    opendbbase = 0;
    portn = 5432;

    connect(__database, __host, __user, __password);
}

sql_connection_c::~sql_connection_c()
{
    if (host)     delete [] host;
    if (user)     delete [] user;
    if (password) delete [] password;
    if (database) delete [] database;
    disconnect();
}

///////////////////////////////////////////////////////////////////////////////////////

bool sql_connection_c::connected()
{
    OPENDB *odb = grabdb();
    if (!odb) return false;

    bool ok = odb->db && odb->db->conn && odb->db->conn->is_open();
    freedb(odb);
    return ok;
}

void sql_connection_c::connect(char *__database, char *__host, char *__user, char *__password)
{
    disconnect();

    if (!__database || !__host)
    {
        printf("sql_connection_c::connect - Parameter(s) are null\n");
        return;
    }

    portn = 5432;

    char *host_buff = 0;
    char *host_tmp = 0;
    char *port_tmp = 0;
    char *next_token = 0;

    if (host)     { delete [] host;     host = 0; }
    if (database) { delete [] database; database = 0; }
    if (user)     { delete [] user;     user = 0; }
    if (password) { delete [] password; password = 0; }

    // Separate the host from the optional :port suffix.
    host_buff = new char[strlen(__host) + 1];
    strcpy_s(host_buff, strlen(__host) + 1, __host);

    host_tmp = strtok_s(host_buff, ":", &next_token);
    port_tmp = strtok_s(NULL, "", &next_token);

    if (!host_tmp)
    {
        delete [] host_buff;
        printf("sql_connection_c::connect - Invalid host: %s\n", __host);
        return;
    }

    if (port_tmp) portn = atoi(port_tmp);

    host = new char[strlen(host_tmp) + 1];
    strcpy_s(host, strlen(host_tmp) + 1, host_tmp);

    delete [] host_buff;

    database = new char[strlen(__database) + 1];
    strcpy_s(database, strlen(__database) + 1, __database);

    if (__user)
    {
        user = new char[strlen(__user) + 1];
        strcpy_s(user, strlen(__user) + 1, __user);
    }

    if (__password)
    {
        password = new char[strlen(__password) + 1];
        strcpy_s(password, strlen(__password) + 1, __password);
    }

    // Eagerly open one connection so a misconfigured DSN surfaces early.
    freedb(grabdb());
}

void sql_connection_c::disconnect()
{
    m_Mutex.Lock();

    while (opendbbase)
    {
        OPENDB *odb = opendbbase;
        opendbbase = opendbbase->next;

        if (odb->db)
        {
            delete odb->db->conn;   // pqxx::connection RAII close
            delete odb->db;
        }
        delete odb;
    }

    m_Mutex.Unlock();
}

///////////////////////////////////////////////////////////////////////////////////////

OPENDB *sql_connection_c::grabdb()
{
    OPENDB *last_db = 0;
    OPENDB *open_db = 0;

    m_Mutex.Lock();

    // See if any databases are open and not being used.
    for (OPENDB *db = opendbbase; db != 0; db = db->next)
    {
        last_db = db;
        if (!db->busy)
        {
            open_db = db;
            break;
        }
    }

    // If no open databases are found, create a new one.
    if (open_db == 0)
    {
        open_db = new OPENDB;
        open_db->next = 0;
        open_db->busy = false;
        open_db->db = 0;

        // Build a libpq keyword/value DSN. password may be NULL (peer-auth
        // setups), user may be NULL (use process owner).
        std::string dsn;
        dsn.reserve(256);
        dsn += "host="; dsn += (host ? host : "localhost");
        char portbuf[16]; snprintf(portbuf, sizeof(portbuf), " port=%d", portn);
        dsn += portbuf;
        dsn += " dbname="; dsn += (database ? database : "");
        if (user)     { dsn += " user=";     dsn += user; }
        if (password) { dsn += " password="; dsn += password; }

        try {
            net7_db_handle *h = new net7_db_handle;
            h->conn = new pqxx::connection(dsn);
            h->last_errno = 0;
            open_db->db = h;
        } catch (const pqxx::failure &e) {
            printf("sql_connection_c::grabdb - pqxx::connection failed: %s\n", e.what());
            delete open_db;
            m_Mutex.Unlock();
            return 0;
        } catch (const std::exception &e) {
            printf("sql_connection_c::grabdb - unexpected: %s\n", e.what());
            delete open_db;
            m_Mutex.Unlock();
            return 0;
        }

        open_db->busy = true;

        // Attach it to the list.
        if (last_db == 0) opendbbase     = open_db;
        else              last_db->next  = open_db;
    }
    else
    {
        open_db->busy = true;
    }

    m_Mutex.Unlock();
    return open_db;
}

void sql_connection_c::freedb(OPENDB *odb)
{
    if (odb)
    {
        m_Mutex.Lock();
        odb->busy = false;
        m_Mutex.Unlock();
    }
}

///////////////////////////////////////////////////////////////////////////////////////
//// sql_query_c

sql_query_c::sql_query_c(sql_connection_c *__sql_connection)
{
    sql_connection = __sql_connection;
    odb = sql_connection->grabdb();
    res = 0;
    last_errno = 0;
    last_errmsg[0] = '\0';
    last_affected = 0;
    params = 0;
}

sql_query_c::~sql_query_c()
{
    free_result();
    if (params) { delete params; params = 0; }
    if (odb) sql_connection->freedb(odb);
}

///////////////////////////////////////////////////////////////////////////////////////

int sql_query_c::execute(char *sql)
{
    free_result();
    last_errno = 0;
    last_errmsg[0] = '\0';
    last_affected = 0;

    if (!odb || !odb->db || !odb->db->conn) {
        last_errno = 1000;
        strcpy_s(last_errmsg, sizeof(last_errmsg), "no open connection");
        return 0;
    }

    std::string translated = translate_dialect(sql);

    try {
        pqxx::work tx(*odb->db->conn);
        pqxx::result r = tx.exec(translated);
        tx.commit();

        net7_result_holder *holder = new net7_result_holder;
        holder->affected_rows = r.affected_rows();
        holder->result = std::move(r);
        res = holder;
        last_affected = holder->affected_rows;
        // Mirror legacy contract: nonzero means "execute returned something"
        // (i.e. the query ran). Callers cast to bool or to int.
        return 1;
    } catch (const pqxx::sql_error &e) {
        last_errno = 1;
        snprintf(last_errmsg, sizeof(last_errmsg), "%s", e.what());
        return 0;
    } catch (const pqxx::failure &e) {
        last_errno = 2;
        snprintf(last_errmsg, sizeof(last_errmsg), "%s", e.what());
        return 0;
    } catch (const std::exception &e) {
        last_errno = 3;
        snprintf(last_errmsg, sizeof(last_errmsg), "%s", e.what());
        return 0;
    }
}

bool sql_query_c::run_query(char *sql)
{
    if (execute(sql) == 0 && Error() > 0)
    {
        LogMySQLMsg("Error executing query:\n\n%s\n\n", sql);
        LogMySQLMsg("Error #%d: %s\n\n", Error(), ErrorMsg());
        return false;
    }
    return true;
}

///////////////////////////////////////////////////////////////////////////////////////
// Parameterised execute. Wire-bound values — injection-immune.

sql_param_bag *sql_query_c::ensure_params()
{
    if (!params) params = new sql_param_bag;
    return params;
}

void sql_query_c::AddParam(int v)            { ensure_params()->p.append(v); }
void sql_query_c::AddParam(long v)           { ensure_params()->p.append(v); }
void sql_query_c::AddParam(unsigned int v)   { ensure_params()->p.append(v); }
void sql_query_c::AddParam(unsigned long v)  { ensure_params()->p.append(v); }
void sql_query_c::AddParam(double v)         { ensure_params()->p.append(v); }
void sql_query_c::AddParam(const char *v)
{
    // pqxx::params::append(std::string_view) — copy into the bag so the
    // caller can free their buffer immediately after AddParam returns.
    ensure_params()->p.append(std::string(v ? v : ""));
}
void sql_query_c::AddParamNull()
{
    ensure_params()->p.append();   // pqxx no-arg overload = NULL
}

void sql_query_c::ClearParams()
{
    if (params) { delete params; params = 0; }
}

int sql_query_c::execute_params(const char *sql)
{
    free_result();
    last_errno = 0;
    last_errmsg[0] = '\0';
    last_affected = 0;

    if (!odb || !odb->db || !odb->db->conn) {
        last_errno = 1000;
        strcpy_s(last_errmsg, sizeof(last_errmsg), "no open connection");
        ClearParams();
        return 0;
    }

    std::size_t n_placeholders = 0;
    std::string translated_q = translate_placeholders(sql, n_placeholders);
    std::string translated   = translate_dialect(translated_q.c_str());

    try {
        pqxx::work tx(*odb->db->conn);
        // pqxx::exec_params(zview, Args&&...) builds a fresh pqxx::params
        // pack from each variadic arg; pqxx::params has an append(params)
        // overload, so passing our accumulator flattens it correctly.
        pqxx::result r = params
            ? tx.exec_params(translated, params->p)
            : tx.exec(translated);
        tx.commit();

        net7_result_holder *holder = new net7_result_holder;
        holder->affected_rows = r.affected_rows();
        holder->result = std::move(r);
        res = holder;
        last_affected = holder->affected_rows;
        ClearParams();
        return 1;
    } catch (const pqxx::sql_error &e) {
        last_errno = 1;
        snprintf(last_errmsg, sizeof(last_errmsg), "%s", e.what());
        ClearParams();
        return 0;
    } catch (const pqxx::failure &e) {
        last_errno = 2;
        snprintf(last_errmsg, sizeof(last_errmsg), "%s", e.what());
        ClearParams();
        return 0;
    } catch (const std::exception &e) {
        last_errno = 3;
        snprintf(last_errmsg, sizeof(last_errmsg), "%s", e.what());
        ClearParams();
        return 0;
    }
}

my_ulonglong sql_query_c::n_rows()
{
    // Legacy: returns mysql_affected_rows(conn) — the count from the most
    // recent statement. We capture that at execute() time.
    return last_affected;
}

void sql_query_c::store(sql_result_c *result)
{
    if (!res) return;
    result->take(res);  // transfer ownership
    res = 0;
}

void sql_query_c::free_result()
{
    if (res) { delete res; res = 0; }
}

unsigned int sql_query_c::Error()
{
    return last_errno;
}

char * sql_query_c::ErrorMsg()
{
    return last_errmsg;
}

///////////////////////////////////////////////////////////////////////////////////////
//// sql_var_c

sql_var_c::sql_var_c()                  { value = 0; }
sql_var_c::sql_var_c(char *s)           { value = s; }
sql_var_c::~sql_var_c()                 { }

///////////////////////////////////////////////////////////////////////////////////////

sql_var_c::operator short ()
{
    return value ? (short)atoi(value) : 0;
}

sql_var_c::operator int ()
{
    return value ? atoi(value) : 0;
}

sql_var_c::operator long ()
{
    return value ? (long)atoi(value) : 0;
}

sql_var_c::operator unsigned long ()
{
    return value ? (unsigned long)atoi(value) : 0;
}

#ifndef WIN32
sql_var_c::operator unsigned int ()
{
    return value ? (unsigned int)strtoul(value, nullptr, 10) : 0;
}

sql_var_c::operator unsigned short ()
{
    return value ? (unsigned short)atoi(value) : 0;
}
#endif

sql_var_c::operator double ()
{
    return value ? atof(value) : 0;
}

sql_var_c::operator float ()
{
    return value ? (float)atof(value) : 0;
}

//This no longer returns an ascii character, but rather a number
sql_var_c::operator char ()
{
    return value ? (char)atoi(value) : 0;
}

sql_var_c::operator char * ()
{
    return value;
}

sql_var_c::operator const char * ()
{
    return (const char *)value;
}

///////////////////////////////////////////////////////////////////////////////////////
//// sql_row_c

sql_row_c::sql_row_c()
{
    result = 0;
    row_index = 0;
    field_count = 0;
    __allow_null = 1;
    row_strings = 0;
    row_strings_count = 0;
}

sql_row_c::~sql_row_c()
{
    free_row_strings();
}

void sql_row_c::free_row_strings()
{
    if (row_strings) {
        for (int i = 0; i < row_strings_count; ++i) {
            if (row_strings[i]) free(row_strings[i]);
        }
        delete [] row_strings;
        row_strings = 0;
        row_strings_count = 0;
    }
}

void sql_row_c::init(sql_result_c *__result, my_ulonglong __row_index)
{
    free_row_strings();
    result = __result;
    row_index = __row_index;
    field_count = (int)(result ? result->n_fields() : 0);
    __allow_null = 1;

    if (!result || !result->get_holder()) return;

    net7_result_holder *h = result->get_holder();
    if (row_index >= (my_ulonglong)h->result.size()) return;

    pqxx::row row = h->result[row_index];

    row_strings = new char *[field_count];
    row_strings_count = field_count;
    for (int i = 0; i < field_count; ++i) {
        pqxx::field f = row[i];
        if (f.is_null()) {
            row_strings[i] = 0;
        } else {
            const char *raw = f.c_str();
            row_strings[i] = strdup(raw ? raw : "");
        }
    }
}

void sql_row_c::allow_null(int allow)
{
    __allow_null = allow;
}

///////////////////////////////////////////////////////////////////////////////////////

sql_var_c sql_row_c::operator [] (char *name)
{
    char compound[100];

    if (!result) return sql_var_c();

    for (int i = 0; i < field_count; i++)
    {
        if (strcmp(result->field(i), name) == 0)
            return (*this)[i];

        // see if this is a compound name table.field
        snprintf(compound, sizeof(compound), "%s.%s", result->table(i), result->field(i));
        if (strcmp(compound, name) == 0)
            return (*this)[i];
    }

    printf("Field `%s` does not exist in this table '%s'\n", name,
           result ? result->table(0) : "<null>");
    return sql_var_c();
}

sql_var_c sql_row_c::operator [] (int idx)
{
    if (!row_strings || idx < 0 || idx >= row_strings_count)
        return sql_var_c(__allow_null ? (char *)0 : (char *)"");

    char *v = row_strings[idx];
    if (!v) return sql_var_c(__allow_null ? (char *)0 : (char *)"");
    return sql_var_c(v);
}

///////////////////////////////////////////////////////////////////////////////////////
//// sql_field_c

sql_field_c::sql_field_c()
{
    res = 0;
    index = 0;
}

sql_field_c::sql_field_c(net7_result_holder *__res, unsigned int __index)
{
    res = __res;
    index = __index;
}

///////////////////////////////////////////////////////////////////////////////////////

char *sql_field_c::get_name()
{
    if (!res) return (char *)"";
    // pqxx::result::column_name returns char const *; cast away const for
    // legacy callers (none mutate the return).
    return const_cast<char *>(res->result.column_name(index));
}

char *sql_field_c::get_default_value()
{
    // libpqxx does not expose column defaults; legacy callers don't use this.
    return (char *)"";
}

unsigned int sql_field_c::get_type()
{
    // Return the column's Postgres oid. The legacy MySQL enum_field_types
    // mapping is gone; no caller in the server currently inspects this.
    if (!res) return 0;
    return (unsigned int)res->result.column_type(index);
}

unsigned int sql_field_c::get_max_length()
{
    // Not available without scanning every row; legacy callers don't use this.
    return 0;
}

///////////////////////////////////////////////////////////////////////////////////////
//// sql_result_c

sql_result_c::sql_result_c()
{
    res = 0;
    cursor = 0;
}

sql_result_c::~sql_result_c()
{
    if (res) delete res;
}

///////////////////////////////////////////////////////////////////////////////////////

void sql_result_c::take(net7_result_holder *__holder)
{
    if (res) delete res;
    res = __holder;
    cursor = 0;
}

my_ulonglong sql_result_c::n_rows()
{
    return res ? (my_ulonglong)res->result.size() : 0;
}

void sql_result_c::fetch_row(sql_row_c *row)
{
    if (!res || !row) return;
    if (cursor >= (my_ulonglong)res->result.size()) {
        row->init(this, (my_ulonglong)res->result.size());  // empty row
        return;
    }
    row->init(this, cursor);
    ++cursor;
}

///////////////////////////////////////////////////////////////////////////////////////

unsigned int sql_result_c::n_fields()
{
    return res ? (unsigned int)res->result.columns() : 0;
}

sql_field_c sql_result_c::fetch_field(unsigned int index)
{
    return res ? sql_field_c(res, index) : sql_field_c();
}

char * sql_result_c::field(int index)
{
    if (!res) return (char *)"";
    return const_cast<char *>(res->result.column_name(index));
}

char * sql_result_c::table(int index)
{
    // libpqxx does not surface per-column table names without a round-trip
    // to pg_class (column_table returns an oid). Legacy callers only use
    // this in the row[char*] compound-name fallback path; returning the
    // empty string keeps that path safe (compound "" "." field_name will
    // not match any user-provided name).
    (void)index;
    return (char *)"";
}

/////////////////////////////////////////////////////////////////////////////////////////
//// sql_query — INSERT-statement builder, syntax-only (no DB I/O).

sql_query::sql_query()
{
    Clear();
}

void sql_query::Clear()
{
    m_Table[0] = 0;
    m_Buffer[0] = 0;
    m_Fields[0] = 0;
    m_Values[0] = 0;
}

void sql_query::AddData(char * Field, int Value)
{
    sprintf_s(m_Values, sizeof(m_Values), "%s, '%d'", m_Values, Value);
    AddField(Field);
}

#ifndef WIN32
void sql_query::AddData(char * Field, unsigned int Value)
{
    sprintf_s(m_Values, sizeof(m_Values), "%s, '%u'", m_Values, Value);
    AddField(Field);
}

void sql_query::AddData(char * Field, unsigned short Value)
{
    sprintf_s(m_Values, sizeof(m_Values), "%s, '%u'", m_Values, (unsigned int)Value);
    AddField(Field);
}
#endif

void sql_query::AddData(char * Field, unsigned long Value)
{
    sprintf_s(m_Values, sizeof(m_Values), "%s, '%lu'", m_Values, Value);
    AddField(Field);
}

void sql_query::AddData(char * Field, long Value)
{
    sprintf_s(m_Values, sizeof(m_Values), "%s, '%ld'", m_Values, Value);
    AddField(Field);
}

void sql_query::AddData(char * Field, float Value)
{
    sprintf_s(m_Values, sizeof(m_Values), "%s, '%f'", m_Values, Value);
    AddField(Field);
}

void sql_query::AddData(char * Field, double Value)
{
    sprintf_s(m_Values, sizeof(m_Values), "%s, '%f'", m_Values, Value);
    AddField(Field);
}

//The char is now interpreted as a numerical value
void sql_query::AddData(char * Field, char Value)
{
    sprintf_s(m_Values, sizeof(m_Values), "%s, '%d'", m_Values, (int)Value);
    AddField(Field);
}

void sql_query::AddData(char * Field, char * Value)
{
    // Phase N: escape via the libpq C escape API (no live connection
    // available here — sql_query is a syntax-builder, no DB handle). The
    // C API uses naïve single-quote doubling which is correct for any
    // server with standard_conforming_strings=on (Postgres default since
    // 9.1). For dialect parity with the legacy MySQL builder, we also
    // escape backslashes the same way.
    std::string esc;
    esc.reserve(strlen(Value) * 2 + 1);
    for (char *p = Value; *p; ++p) {
        if (*p == '\'' || *p == '\\') esc.push_back(*p);
        esc.push_back(*p);
    }
    sprintf_s(m_Values, sizeof(m_Values), "%s, '%s'", m_Values, esc.c_str());
    AddField(Field);
}

// Do not add a quote to the data
void sql_query::AddDataNQ(char * Field, char * Value)
{
    sprintf_s(m_Values, sizeof(m_Values), "%s, %s", m_Values, Value);
    AddField(Field);
}

void sql_query::AddField(char * Field)
{
    // Postgres uses double-quotes for identifier quoting (vs. MySQL's
    // backticks). Emit double-quotes directly.
    sprintf_s(m_Fields, sizeof(m_Fields), "%s, \"%s\"", m_Fields, Field);
    assert(strlen(m_Fields) < sizeof(m_Fields));
    assert(strlen(m_Values) < sizeof(m_Values));
}

void sql_query::SetTable(char * Table)
{
    assert(strlen(Table) < sizeof(m_Table));
    strcpy_s(m_Table, sizeof(m_Table), Table);
}

char * sql_query::CreateQuery()
{
    static char InsertSQL[] = "INSERT INTO \"%s\" (%s) VALUES (%s)";

    assert(strlen(InsertSQL) + strlen(m_Fields) + strlen(m_Values) + strlen(m_Table) < sizeof(m_Buffer));
    sprintf_s(m_Buffer, sizeof(m_Buffer), InsertSQL, m_Table, &m_Fields[1], &m_Values[1]);

    return m_Buffer;
}
