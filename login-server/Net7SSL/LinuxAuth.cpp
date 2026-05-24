// LinuxAuth.cpp
//
// Copyright (c) 2010 Net-7 Entertainment, Ltd.
//
// This file is licensed under CC BY-NC-SA 3.0 — see LICENSES/ in the
// repository root. Although LinuxAuth.cpp is new code authored for the
// Phase J Linux port, it is a direct functional port of the Win32 HTTP
// auth path that lives (#ifdef WIN32-walled) in SSL_Connection.cpp and
// AccountManager.cpp in this directory; the license inherits from the
// upstream Net-7 source per the CC BY-NC-SA 3.0 share-alike clause.
//
// Phase J (Linux port): the Win32 SSL_Connection class drives a
// per-connection thread that does SSL_accept -> SSL_read -> dispatch ->
// SSL_write. For the dev stack we don't need any of that scaffolding;
// the login flow is a single request-response per TLS connection. This
// TU exposes a single entry point — HandleHttpsRequest() — that takes a
// recv buffer (already read off the socket) and returns a freshly-
// allocated response string. SSL_Listener.cpp's Linux accept branch
// calls it inline.
//
// What's ported:
//   - URL routing: /AuthLogin, /sectorserver.cgi, /touchsession.jsp,
//     /certificate.html, /who.cgi stub, and the 404 fallback. Matches
//     SSL_Connection::GetResponse() in SSL_Connection.cpp.
//   - AuthLogin handler: parses username= / password= query args,
//     validates against net7_user.accounts using libmysqlclient, and
//     issues a ticket on success. Matches SSL_Connection::AuthLogin()
//     + AccountManager::IssueTicket() + ValidateAccount() + BuildTicket().
//   - SectorServer handler: returns Success=TRUE if the version matches
//     and the port is sane. Matches SSL_Connection::SectorServer().
//   - TouchSession handler: returns the chunked "Success" body if any
//     lkey= query arg is present. Matches SSL_Connection::TouchSession().
//
// What's intentionally NOT ported:
//   - Ticket -> server handoff via UDP/mailslot. Win32 RegisterSectorServer
//     used SSL_write to push the ticket to the game server over its own
//     SSL_LOCALCERT_LOGIN_PORT listener. For Phase J the ticket is built
//     and returned to the client; the client then presents it to the game
//     server, which currently has no AccountManager either — that's the
//     next pass.
//   - HTTP request body parsing. The client only ever sends GET requests
//     with query strings, never POST bodies, so the WIN32 code's "throw
//     away everything after the first line" pattern is preserved.
//   - HTML file serving. The Win32 code can serve .html / .css / .gif
//     files from SERVER_HTML_PATH if the path matches a legal extension.
//     For the auth flow none of these are reachable; left as a TODO.

#ifndef WIN32

#include "Net7SSL.h"
#include "SSL_Connection.h"   // For *_TAG macros (USERNAME_TAG etc.)
#include <net7/Mutex.h>

#include <mysql/mysql.h>

#include <ctime>
#include <cstdio>
#include <cstdlib>
#include <cstring>

// --------------------------------------------------------------------------
// In-memory ticket store. A ticket is the opaque string the client must
// present to the game server after a successful AuthLogin. Win32's
// AccountManager keeps a fixed pool of 5 slots and recycles on expiry;
// we do the same with a tiny linked list.
// --------------------------------------------------------------------------
namespace {

constexpr unsigned long kTicketExpireMs = 300000; // 5 minutes — matches AccountManager.h TICKET_EXPIRE_TIME.

struct LinuxTicket {
    char username[64];
    char ticket[128];
    unsigned long expire_ms;
};

Mutex g_TicketMutex;
constexpr int kMaxTickets = 32;
LinuxTicket g_Tickets[kMaxTickets] = {};

// Returns a pointer into a static buffer; caller must copy.
const char *BuildTicketLocked(const char *username)
{
    unsigned long now = GetNet7TickCount();

    // Find an expired or empty slot, else the oldest.
    int chosen = -1;
    unsigned long oldest = ~0UL;
    for (int i = 0; i < kMaxTickets; i++) {
        if (g_Tickets[i].username[0] == 0 || g_Tickets[i].expire_ms < now) {
            chosen = i;
            break;
        }
        if (g_Tickets[i].expire_ms < oldest) {
            oldest = g_Tickets[i].expire_ms;
            chosen = i;
        }
    }
    if (chosen < 0) chosen = 0;

    // Seed once per call — same pattern as Win32 BuildTicket().
    static bool seeded = false;
    if (!seeded) { srand((unsigned int)time(nullptr)); seeded = true; }

    snprintf(g_Tickets[chosen].ticket, sizeof(g_Tickets[chosen].ticket),
             "%s-%d", username, rand());
    strncpy(g_Tickets[chosen].username, username,
            sizeof(g_Tickets[chosen].username) - 1);
    g_Tickets[chosen].username[sizeof(g_Tickets[chosen].username) - 1] = 0;
    g_Tickets[chosen].expire_ms = now + kTicketExpireMs;

    return g_Tickets[chosen].ticket;
}

// --------------------------------------------------------------------------
// MySQL connection helpers. Connect lazily on first use, hold the
// connection for the life of the process. libmysqlclient is thread-safe
// in single-connection mode as long as concurrent queries are
// serialised; we serialise on g_DbMutex.
// --------------------------------------------------------------------------
Mutex g_DbMutex;
MYSQL *g_DbConn = nullptr;
bool g_DbInitTried = false;

const char *EnvOr(const char *name, const char *fallback)
{
    const char *v = getenv(name);
    return (v && *v) ? v : fallback;
}

bool EnsureDbConnected()
{
    g_DbMutex.Lock();
    if (g_DbConn) {
        // Reconnect-on-ping in case the server bounced.
        if (mysql_ping(g_DbConn) == 0) {
            g_DbMutex.Unlock();
            return true;
        }
        mysql_close(g_DbConn);
        g_DbConn = nullptr;
    }

    g_DbConn = mysql_init(nullptr);
    if (!g_DbConn) {
        g_DbMutex.Unlock();
        LogMessage("LinuxAuth: mysql_init failed\n");
        return false;
    }

    // Auto-reconnect.
    bool reconnect = true;
    mysql_options(g_DbConn, MYSQL_OPT_RECONNECT, &reconnect);

    // libmysqlclient 8.0 defaults to SSL_MODE_PREFERRED which produces
    // "Unable to get certificate from ''" against the latin1 dev
    // mysql container. Match the workaround in server/src/mysql/mysqlplus.cpp.
    {
        unsigned int ssl_mode = 1; // SSL_MODE_DISABLED
        mysql_options(g_DbConn, MYSQL_OPT_SSL_MODE, &ssl_mode);
    }

    // Parse host:port — config / env may include the port suffix.
    const char *host_env = EnvOr("MYSQL_HOST", g_MySQL_Host[0] ? g_MySQL_Host : "mysql:3306");
    const char *user     = EnvOr("MYSQL_USER", g_MySQL_User[0] ? g_MySQL_User : "net7");
    const char *pass     = EnvOr("MYSQL_PASS", g_MySQL_Pass[0] ? g_MySQL_Pass : "net7");
    const char *db       = EnvOr("MYSQL_DB",   "net7_user");

    char host_buf[256];
    strncpy(host_buf, host_env, sizeof(host_buf) - 1);
    host_buf[sizeof(host_buf) - 1] = 0;
    int port = 3306;
    char *colon = strchr(host_buf, ':');
    if (colon) {
        *colon = 0;
        port = atoi(colon + 1);
        if (port <= 0) port = 3306;
    }

    if (!mysql_real_connect(g_DbConn, host_buf, user, pass, db, port, nullptr,
                            CLIENT_REMEMBER_OPTIONS)) {
        LogMessage("LinuxAuth: mysql_real_connect to %s:%d/%s as %s failed: %s\n",
                   host_buf, port, db, user, mysql_error(g_DbConn));
        mysql_close(g_DbConn);
        g_DbConn = nullptr;
        g_DbMutex.Unlock();
        return false;
    }

    LogMessage("LinuxAuth: connected to MySQL %s:%d/%s as %s\n",
               host_buf, port, db, user);
    g_DbMutex.Unlock();
    return true;
}

// PHASE-N TODO: delete this function. It exists because the legacy
// mysqlplus wrapper has no prepared-statement support — the only way
// to talk to MySQL is mysql_query(const char *), so every query is
// string-built and every dynamic value is a potential injection. The
// right fix is parameterised statements (see plans/14-phase-n-libpqxx-
// rewrite.md). Until that lands, this whitelist (`[A-Za-z0-9_.-]`)
// keeps the auth path's injection surface to zero. Anyone adding a
// new query in Linux code is expected to also call this — which is
// exactly the brittleness Phase N gets rid of.
bool SafeUsername(const char *in, char *out, size_t outsz)
{
    if (!in || outsz < 2) return false;
    size_t i = 0;
    for (; in[i] && i < outsz - 1; i++) {
        char c = in[i];
        bool ok = (c >= 'A' && c <= 'Z') ||
                  (c >= 'a' && c <= 'z') ||
                  (c >= '0' && c <= '9') ||
                  c == '_' || c == '.' || c == '-';
        if (!ok) return false;
        out[i] = c;
    }
    if (i == 0) return false;
    out[i] = 0;
    return true;
}

// PHASE-N TODO: delete (see SafeUsername above). The fact that this
// rejects characters which are *legal* in a password (`'`, `"`, `\`, `%`)
// is the smoking gun that this is a SQL-shape constraint masquerading
// as a password policy. Parameterised statements remove the rationale.
bool SafePassword(const char *in, char *out, size_t outsz)
{
    if (!in || outsz < 2) return false;
    size_t i = 0;
    for (; in[i] && i < outsz - 1; i++) {
        unsigned char c = (unsigned char)in[i];
        if (c < 0x20 || c > 0x7e) return false;
        if (c == '\'' || c == '"' || c == '\\' || c == '%') return false;
        out[i] = (char)c;
    }
    if (i == 0) return false;
    out[i] = 0;
    return true;
}

// Returns true if (username, MD5(password)) matches a row in
// net7_user.accounts. Matches AccountManager::ValidateAccount.
bool ValidateAccountLinux(const char *username, const char *password)
{
    if (!EnsureDbConnected()) return false;

    char query[512];
    snprintf(query, sizeof(query),
             "SELECT `id` FROM `accounts` "
             "WHERE `username` = '%s' AND `password` = UPPER(MD5('%s'))",
             username, password);

    g_DbMutex.Lock();
    bool ok = false;
    long account_id = -1;
    if (mysql_query(g_DbConn, query) == 0) {
        MYSQL_RES *res = mysql_store_result(g_DbConn);
        if (res) {
            if (mysql_num_rows(res) > 0) {
                MYSQL_ROW row = mysql_fetch_row(res);
                if (row && row[0]) {
                    account_id = atol(row[0]);
                    ok = true;
                }
            }
            mysql_free_result(res);
        }
    } else {
        LogMessage("LinuxAuth: ValidateAccount query failed: %s\n",
                   mysql_error(g_DbConn));
    }
    g_DbMutex.Unlock();

    if (ok) {
        // Match the Win32 UpdateLoginTime() — call accLogin stored proc.
        char ts[32];
        time_t now;
        time(&now);
        struct tm gmt;
        gmtime_r(&now, &gmt);
        strftime(ts, sizeof(ts), "%Y/%m/%d %H:%M:%S", &gmt);

        char update[256];
        snprintf(update, sizeof(update),
                 "CALL net7_user.accLogin(%ld, '%s')", account_id, ts);
        g_DbMutex.Lock();
        if (mysql_query(g_DbConn, update) != 0) {
            // Non-fatal — the proc may not exist on stripped schemas.
            LogMessage("LinuxAuth: accLogin update failed (non-fatal): %s\n",
                       mysql_error(g_DbConn));
        } else {
            // Drain any result so the connection is reusable.
            MYSQL_RES *res = mysql_store_result(g_DbConn);
            if (res) mysql_free_result(res);
            while (mysql_next_result(g_DbConn) == 0) {
                MYSQL_RES *r2 = mysql_store_result(g_DbConn);
                if (r2) mysql_free_result(r2);
            }
        }
        g_DbMutex.Unlock();
    }

    return ok;
}

// --------------------------------------------------------------------------
// HTTP response helpers — mirror SSL_Connection::HttpResult().
// --------------------------------------------------------------------------
char *HttpResult(size_t *out_len, const char *body,
                 const char *content_type = "text/html")
{
    size_t body_len = strlen(body);
    char header[256];
    int n = snprintf(header, sizeof(header),
                     "HTTP/1.1 200 OK\r\n"
                     "Content-Type: %s\r\n"
                     "Server: AuthServer/2.5\r\n"
                     "Content-Length: %zu\r\n"
                     "\r\n",
                     content_type, body_len);
    if (n < 0) n = 0;
    size_t header_len = (size_t)n;
    char *response = new char[header_len + body_len + 1];
    memcpy(response, header, header_len);
    memcpy(response + header_len, body, body_len);
    response[header_len + body_len] = 0;
    *out_len = header_len + body_len;
    return response;
}

char *MakeNotFound(size_t *out_len)
{
    static const char *kError404 =
        "HTTP/1.1 404 File Not Found\r\n"
        "Server: AuthServer/2.5\r\n"
        "Keep-Alive: timeout=15, max=100\r\n"
        "Connection: Keep-Alive\r\n"
        "Content-Length: 2\r\n"
        "Content-type: text/plain\r\n"
        "\r\n"
        "\r\n";
    size_t n = strlen(kError404);
    char *response = new char[n + 1];
    memcpy(response, kError404, n + 1);
    *out_len = n;
    return response;
}

// --------------------------------------------------------------------------
// Endpoint handlers — direct port of SSL_Connection's WIN32 functions.
// --------------------------------------------------------------------------

// /AuthLogin?username=X&password=Y&serviceID=Z&version=V
char *HandleAuthLogin(size_t *out_len, char *recv_buffer, unsigned long client_ip)
{
    char info[256] = "Valid=False\r\n";

    char *u = strstr(recv_buffer, USERNAME_TAG);
    char *p = strstr(recv_buffer, PASSWORD_TAG);

    if (u && p) {
        u += strlen(USERNAME_TAG);
        p += strlen(PASSWORD_TAG);
        // The client's query string terminates each field with '&' or ' '.
        // Win32 uses strtok in-place; we do the same.
        strtok(u, "& \r\n");
        strtok(p, "& \r\n");

        unsigned char *ip = (unsigned char *)&client_ip;
        LogMessage("LinuxAuth: AuthLogin '%s' from %u.%u.%u.%u\n",
                   u, ip[0], ip[1], ip[2], ip[3]);

        char safe_u[64], safe_p[128];
        if (!SafeUsername(u, safe_u, sizeof(safe_u))) {
            LogMessage("LinuxAuth: rejecting unsafe username\n");
        } else if (!SafePassword(p, safe_p, sizeof(safe_p))) {
            LogMessage("LinuxAuth: rejecting unsafe password\n");
        } else if (ValidateAccountLinux(safe_u, safe_p)) {
            g_TicketMutex.Lock();
            const char *ticket = BuildTicketLocked(safe_u);
            char issued[128];
            strncpy(issued, ticket, sizeof(issued) - 1);
            issued[sizeof(issued) - 1] = 0;
            g_TicketMutex.Unlock();
            snprintf(info, sizeof(info), "Valid=TRUE\r\nTicket=%s\r\n", issued);
            LogMessage("LinuxAuth: ticket issued for '%s'\n", safe_u);
        } else {
            LogMessage("LinuxAuth: ValidateAccount failed for '%s'\n", safe_u);
        }
    }

    return HttpResult(out_len, info, "text/plain");
}

// /touchsession.jsp?lkey=... — Win32 just returns the chunked "Success"
// body if any lkey is present; we do the same.
char *HandleTouchSession(size_t *out_len, char *recv_buffer)
{
    char *lkey = strstr(recv_buffer, LKEY_TAG);
    if (!lkey) return nullptr;
    lkey += strlen(LKEY_TAG);
    strtok(lkey, "&\r\n ");

    static const char *kInfo =
        "HTTP/1.1 200 \r\n"
        "Server: AuthServer/2.5\r\n"
        "Content-Type: text/plain; charset=ISO-8859-1\r\n"
        "Transfer-Encoding: chunked\r\n"
        "\r\n"
        "7\r\n"
        "Success\r\n"
        "0\r\n"
        "\r\n";
    size_t n = strlen(kInfo);
    char *response = new char[n + 1];
    memcpy(response, kInfo, n + 1);
    *out_len = n;
    return response;
}

// /sectorserver.cgi?username=X&port=P&max_sectors=M&version=V
char *HandleSectorServer(size_t *out_len, char *recv_buffer)
{
    char info[256] = "Success=FALSE\r\n";

    char *username    = strstr(recv_buffer, USERNAME_TAG);
    char *port        = strstr(recv_buffer, PORT_TAG);
    char *max_sectors = strstr(recv_buffer, MAX_SECTORS_TAG);
    char *version     = strstr(recv_buffer, VERSION_TAG);

    if (username && port && max_sectors && version) {
        username    += strlen(USERNAME_TAG);
        port        += strlen(PORT_TAG);
        max_sectors += strlen(MAX_SECTORS_TAG);
        version     += strlen(VERSION_TAG);

        strtok(username,    "& \r\n");
        strtok(port,        "& \r\n");
        strtok(max_sectors, "& \r\n");
        strtok(version,     "& \r\n");

        char expected[16];
        snprintf(expected, sizeof(expected), "%d.%d",
                 SECTOR_SERVER_MAJOR_VERSION, SECTOR_SERVER_MINOR_VERSION);

        if (strcmp(version, expected) == 0) {
            int port_num = atoi(port);
            int n_sectors = atoi(max_sectors);
            if (port_num >= 3500 && n_sectors > 0) {
                char tmp[32];
                snprintf(tmp, sizeof(tmp), "%d", port_num);
                if (strcmp(tmp, port) == 0) {
                    LogMessage("LinuxAuth: RegisterSectorServer user=%s port=%d\n",
                               username, port_num);
                    snprintf(info, sizeof(info), "Success=TRUE\r\n");
                } else {
                    strncat(info, "Invalid port number\r\n",
                            sizeof(info) - strlen(info) - 1);
                }
            } else {
                strncat(info, "Port number must be 3500 or above\r\n",
                        sizeof(info) - strlen(info) - 1);
            }
        } else {
            char tail[64];
            snprintf(tail, sizeof(tail),
                     "Expected Sector Server version is %s\r\n", expected);
            strncat(info, tail, sizeof(info) - strlen(info) - 1);
        }
    } else {
        strncat(info, "Invalid parameters\r\n",
                sizeof(info) - strlen(info) - 1);
    }

    return HttpResult(out_len, info, "text/plain");
}

// /certificate.html — informational page the client opens after the
// "trust this cert" dance. Same content the Win32 path returns.
char *HandleCertificate(size_t *out_len)
{
    char body[512];
    snprintf(body, sizeof(body),
        "<html>\r\n"
        "<head>\r\n"
        "<META HTTP-EQUIV=\"Pragma\" CONTENT=\"no-cache\">\r\n"
        "</head>\r\n"
        "<body>\r\n"
        "<h3><tt>%s certificate successfully installed!</tt></h3>\r\n"
        "<h2>Please close the browser to continue<h2>\r\n"
        "</body>\r\n"
        "</html>\r\n",
        g_DomainName);
    return HttpResult(out_len, body);
}

} // namespace

// --------------------------------------------------------------------------
// Public entry point: called from SSL_Listener.cpp's Linux accept loop
// after SSL_accept() succeeds and a single SSL_read() pulled the HTTP
// request line into recv_buffer.
//
// Returns a newly-allocated response (caller must `delete[]`) and writes
// its length to *out_len. Always returns non-null — falls back to 404.
// --------------------------------------------------------------------------
// C++ linkage — call site in SSL_Listener.cpp declares it the same way.
char *HandleHttpsRequest(char *recv_buffer, size_t *out_len,
                         unsigned long client_ip)
{
    *out_len = 0;
    if (!recv_buffer) return MakeNotFound(out_len);

    // Dispatch on substring match, same order as Win32's GetResponse.
    if (strstr(recv_buffer, "/AuthLogin")) {
        return HandleAuthLogin(out_len, recv_buffer, client_ip);
    }
    if (strstr(recv_buffer, "/touchsession.jsp")) {
        char *r = HandleTouchSession(out_len, recv_buffer);
        if (r) return r;
    }
    if (strstr(recv_buffer, "/sectorserver.cgi")) {
        return HandleSectorServer(out_len, recv_buffer);
    }
    if (strstr(recv_buffer, "certificate.html")) {
        return HandleCertificate(out_len);
    }
    if (strstr(recv_buffer, "/who.cgi")) {
        // Linux no-op by design: the upstream Win32 implementation also
        // never had a real `who.cgi` handler — WhoHtml was declared but
        // never defined and any production hit 404'd through the same
        // `MakeNotFound` fall-through below. Keep the explicit branch so
        // a future implementer has a marker to hook into.
    }

    return MakeNotFound(out_len);
}

#endif // !WIN32
