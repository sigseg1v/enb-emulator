///////////////////////////////////////////////////////////////////////////////////////
//// mysqlplus.h
///////////////////////////////////////////////////////////////////////////////////////
//
// Phase N: this header used to be a thin facade over libmysqlclient. The
// rewrite swaps the backend to libpqxx (Postgres) while preserving the 7
// public classes (`sql_connection_c`, `sql_query_c`, `sql_result_c`,
// `sql_row_c`, `sql_var_c`, `sql_field_c`, `sql_query`) and their method
// signatures, so every caller in server/src/ tracks transparently.
//
// The file name and class names are kept for source-stability reasons.
// "mysql"plus is a historical misnomer at this point — the wrapper now
// speaks Postgres.

#ifndef __MYSQLPLUS_H__
#define __MYSQLPLUS_H__

#include <net7/Mutex.h>
#include <cstdint>

#define _CRT_SECURE_NO_WARNINGS 1		// Disable Warning messages about new Secure Functions in VS2008

// Forward declarations of the internal libpqxx-backed state. Defined in
// mysqlplus.cpp; callers see them as opaque pointers only.
struct net7_db_handle;        // wraps pqxx::connection
struct net7_result_holder;    // wraps pqxx::result
struct sql_param_bag;         // wraps pqxx::params (parameterised execute)

// my_ulonglong used to be a MySQL typedef; preserve the name for the few
// callers that still spell it.
typedef std::uint64_t my_ulonglong;

// Legacy escape shim. The old mysqlplus pulled `mysql_escape_string` from
// libmysqlclient; a handful of DAO call sites (SaveManager.cpp) still call
// it directly. Defined inline so we don't drag the wrapper TU into every
// includer. Standard single-quote-doubling escape — correct for Postgres
// with standard_conforming_strings=on (default since 9.1).
#include <string.h>
static inline unsigned long mysql_escape_string(char *to, const char *from, unsigned long length)
{
    char *dst = to;
    for (unsigned long i = 0; i < length; ++i) {
        if (from[i] == '\'' || from[i] == '\\') *dst++ = from[i];
        *dst++ = from[i];
    }
    *dst = '\0';
    return (unsigned long)(dst - to);
}

///////////////////////////////////////////////////////////////////////////////////////
//// opendbstruct - database connection handle

typedef struct opendbstruct
{
   struct opendbstruct *next;
   net7_db_handle *db;
   bool busy;
} OPENDB;

///////////////////////////////////////////////////////////////////////////////////////
//// sql_connection_c

class sql_connection_c
{
public:
    sql_connection_c();
    sql_connection_c(char *database, char *host, char *user = 0, char *password = 0);
    ~sql_connection_c();

    bool connected();
    void connect(char *database, char *host, char *user = 0, char *password = 0);
    void disconnect();

    OPENDB *grabdb();
    void freedb(OPENDB *odb);

private:
    char *host;
    char *user;
    char *password;
    char *database;
    int	portn;
    OPENDB *opendbbase;

    Mutex m_Mutex;
};

///////////////////////////////////////////////////////////////////////////////////////
//// sql_query_c

class sql_result_c;

class sql_query_c
{
public:
    sql_query_c(sql_connection_c *sql_connection);
    ~sql_query_c();

    int execute(char *sql);
    bool run_query(char *sql);

    // Parameterised execute. The wire protocol handles literal escaping,
    // so SQL injection is structurally impossible — the bound value can
    // never be re-parsed as SQL syntax. Placeholders in the query use
    // `?` (translated to Postgres `$N` numbered placeholders internally).
    //
    // Usage:
    //     sql_query_c q(&conn);
    //     q.AddParam(account_id);
    //     q.AddParam(username);            // const char *
    //     q.execute_params("SELECT * FROM accounts WHERE id=? AND name=?");
    //
    // Parameter state is cleared on success/failure. To clear without
    // executing, call ClearParams().
    void AddParam(int v);
    void AddParam(long v);
    void AddParam(unsigned int v);
    void AddParam(unsigned long v);
    void AddParam(double v);
    void AddParam(const char *v);
    void AddParamNull();
    void ClearParams();
    int  execute_params(const char *sql);
    // Same as execute_params(), but logs error on failure (parity with
    // run_query() vs execute()). Returns true on success.
    bool run_query_params(const char *sql);

    void store(sql_result_c *result);

    unsigned int Error();
    char * ErrorMsg();

    my_ulonglong n_rows();

private:
    void free_result();
    sql_param_bag *ensure_params();

private:
    net7_result_holder *res;       // owned; replaces MYSQL_RES*
    sql_connection_c *sql_connection;
    OPENDB *odb;
    unsigned int last_errno;
    char last_errmsg[256];
    my_ulonglong last_affected;
    sql_param_bag *params;         // owned; lazily allocated by AddParam*
};

///////////////////////////////////////////////////////////////////////////////////////
//// sql_var_c

class sql_var_c
{
public:
    sql_var_c();
    sql_var_c(char *s);
    ~sql_var_c();

    operator short ();
    operator int ();
    operator long ();
    operator unsigned long ();
#ifndef WIN32
    // See note on sql_query::AddData above: needed on LP64 to disambiguate
    // implicit conversion to uint32_t / uint16_t.
    operator unsigned int ();
    operator unsigned short ();
#endif
    operator double ();
    operator float ();
    operator const char * ();
    operator char * ();
    operator char ();

private:
    char *value;
};


///////////////////////////////////////////////////////////////////////////////////////
//// sql_field_c

class sql_field_c
{
public:
    sql_field_c();
    sql_field_c(net7_result_holder *res, unsigned int index);

    char *get_name();
    char *get_default_value();
    unsigned int get_type();        // returns pqxx column oid (was enum_field_types)
    unsigned int get_max_length();

private:
    net7_result_holder *res;
    unsigned int index;
};

///////////////////////////////////////////////////////////////////////////////////////
//// sql_result_c
class sql_row_c;

class sql_result_c
{
public:
    sql_result_c();
    ~sql_result_c();

    void take(net7_result_holder *holder);  // transfers ownership

    my_ulonglong n_rows();
    void fetch_row(sql_row_c *row);

    unsigned int n_fields();
    sql_field_c fetch_field(unsigned int index);

    char * field(int index);		// return field name
	char * table(int index);		// return table name

    // exposed for sql_row_c
    net7_result_holder *get_holder() { return res; }

private:
    net7_result_holder *res;        // owned
    my_ulonglong cursor;            // next row index for fetch_row()
};

///////////////////////////////////////////////////////////////////////////////////////
//// sql_row_c

class sql_row_c
{
public:
    sql_row_c();
    ~sql_row_c();

    void init(sql_result_c *result, my_ulonglong row_index);

    // Null values are returned as emprty strings if allow = 0
    void allow_null(int allow = 1);

    sql_var_c operator [] (int idx);
    sql_var_c operator [] (char *name); //USE SPARINGLY

private:
    sql_result_c *result;
    my_ulonglong row_index;
    int __allow_null;
    int field_count;
    // Per-row cached column values (owned strings, stable char* for
    // sql_var_c lifetime).
    char **row_strings;
    int row_strings_count;
    void free_row_strings();
};

///////////////////////////////////////////////////////////////////////////////////////
//// sql_query

class sql_query
{
public:
    sql_query();

    void AddData(char *Field, int Value);
#ifndef WIN32
    // On LP64 (Linux x86_64) unsigned long is 64-bit, so uint32_t (== unsigned int)
    // and uint16_t don't have an unambiguous integer conversion. On LLP64 (Win64)
    // unsigned long is 32-bit and matches uint32_t directly, so we don't add
    // these there or we'd create the opposite ambiguity.
    void AddData(char *Field, unsigned int Value);
    void AddData(char *Field, unsigned short Value);
#endif
    void AddData(char *Field, unsigned long Value);
    void AddData(char *Field, long Value);
    void AddData(char *Field, float Value);
    void AddData(char *Field, double Value);
    void AddData(char *Field, char Value);
    void AddData(char *Field, char * Value);
	void AddDataNQ(char * Field, char * Value);
    void SetTable(char *Table);
    char * CreateQuery();
    void Clear();

private:
    void AddField(char *Field);

private:
    char m_Table[64];
    char m_Fields[2048];
    char m_Values[6144];
    char m_Buffer[8192];
};

#endif
