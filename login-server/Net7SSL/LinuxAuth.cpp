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
// Phase N+ wave 3: switched from libmysqlclient to libpqxx. The original
// Phase J Linux port shipped with mysql_real_connect / mysql_stmt_*
// because the dev stack still ran MariaDB. Phase N migrated the game
// server (server/src/mysql/mysqlplus.cpp) to Postgres via libpqxx but
// LinuxAuth was left behind — its query templates use UPPER(MD5(?)) and
// CALL net7_user.accLogin(?, ?), neither of which Postgres speaks. The
// rewrite uses pgcrypto's digest() (encode(digest(?, 'md5'), 'hex')
// upper-cased) for the password column and drops the accLogin stored
// proc entirely (it was a non-fatal MySQL-flavoured CALL that doesn't
// exist in the Postgres schema). Parameterised pqxx::exec_params keeps
// the SQL-injection immunity that wave 2 introduced.
//
// What's ported:
//   - URL routing: /AuthLogin, /sectorserver.cgi, /touchsession.jsp,
//     /certificate.html, /who.cgi stub, and the 404 fallback. Matches
//     SSL_Connection::GetResponse() in SSL_Connection.cpp.
//   - AuthLogin handler: parses username= / password= query args,
//     validates against net7_user.accounts using libpqxx, and
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
#include "PatcherManifest.h"  // Phase AN launcher self-update hash cache
#include <net7/Mutex.h>

#include <pqxx/pqxx>
#include <sodium.h>

#include <cctype>
#include <ctime>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <memory>
#include <string>

// --------------------------------------------------------------------------
// In-memory ticket store. A ticket is the opaque string the client must
// present to the game server after a successful AuthLogin. Win32's
// AccountManager keeps a fixed pool of 5 slots and recycles on expiry;
// we do the same with a tiny linked list.
// --------------------------------------------------------------------------
namespace {

// 30 minutes. The ticket is issued at AuthLogin (account auth), but the
// per-packet DTLS auth token is not bound until the player reaches sector
// entry (game server HandleGlobalTicketRequest -> GetLoginTokenBinary). A
// first-time player creating their first character (race/class/appearance/
// name) routinely spends well over the old 5-minute window AT THE CHARACTER
// SCREEN with no packets in flight, so the row expired before sector entry,
// the token could not bind, and DTLS dropped every gameplay datagram -- the
// player hung zoning in until they alt-F4'd and relogged (fresh ticket). The
// real server did not boot players for slow character creation. Must stay in
// sync with AccountManager.h TICKET_EXPIRE_TIME (both servers share the row).
constexpr unsigned long kTicketExpireMs = 1800000; // 30 minutes

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

    // The ticket suffix is the only secret standing between a presented
    // ticket and an account takeover, so it must be a CSPRNG draw, not
    // glibc rand(). rand() is a non-cryptographic LCG seeded once from
    // time(), so its full sequence is recoverable from a single observed
    // ticket (or guessable from the boot time). libsodium is already
    // initialised by ValidateAccountLinux before any ticket is built;
    // randombytes_buf draws from the kernel CSPRNG (getrandom(2)) and
    // never fails. 16 bytes -> 32 hex chars, no '-' so the game server's
    // strtok(ticket, "-") still splits username from token cleanly.
    unsigned char token_bin[16];
    char token_hex[sizeof(token_bin) * 2 + 1];
    randombytes_buf(token_bin, sizeof(token_bin));
    sodium_bin2hex(token_hex, sizeof(token_hex), token_bin, sizeof(token_bin));

    snprintf(g_Tickets[chosen].ticket, sizeof(g_Tickets[chosen].ticket),
             "%s-%s", username, token_hex);
    strncpy(g_Tickets[chosen].username, username,
            sizeof(g_Tickets[chosen].username) - 1);
    g_Tickets[chosen].username[sizeof(g_Tickets[chosen].username) - 1] = 0;
    g_Tickets[chosen].expire_ms = now + kTicketExpireMs;

    return g_Tickets[chosen].ticket;
}

// --------------------------------------------------------------------------
// Postgres connection helpers (Phase N+ wave 3). pqxx::connection is
// reused for the life of the process; concurrent queries are serialised
// on g_DbMutex (same model the previous mysqlclient code used). A
// dropped backend connection is detected via conn->is_open() and the
// next call reconnects.
// --------------------------------------------------------------------------
Mutex g_DbMutex;
std::unique_ptr<pqxx::connection> g_DbConn;

const char *EnvOr(const char *name, const char *fallback)
{
    const char *v = getenv(name);
    return (v && *v) ? v : fallback;
}

std::string BuildDsn()
{
    // Phase N: host string may carry a :port suffix
    // (Net7Config.cfg writes "postgres:5432"). pqxx wants explicit
    // host=/port= keywords. The schema lives in two DBs:
    //   - net7        — game content
    //   - net7_user   — accounts (this file only touches this one)
    // The schema-init container creates both at startup.
    const char *host_env = EnvOr("DB_HOST", g_DB_Host[0] ? g_DB_Host : "postgres:5432");
    const char *user     = EnvOr("DB_USER", g_DB_User[0] ? g_DB_User : "net7");
    const char *pass     = EnvOr("DB_PASS", g_DB_Pass[0] ? g_DB_Pass : "net7");
    const char *db       = EnvOr("DB_NAME",   "net7_user");

    char host_buf[256];
    strncpy(host_buf, host_env, sizeof(host_buf) - 1);
    host_buf[sizeof(host_buf) - 1] = 0;
    int port = 5432;
    char *colon = strchr(host_buf, ':');
    if (colon) {
        *colon = 0;
        port = atoi(colon + 1);
        if (port <= 0) port = 5432;
    }

    char dsn_buf[1024];
    snprintf(dsn_buf, sizeof(dsn_buf),
             "host=%s port=%d user=%s password=%s dbname=%s",
             host_buf, port, user, pass, db);
    return std::string(dsn_buf);
}

// Caller must hold g_DbMutex. Returns true on success.
bool EnsureDbConnectedLocked()
{
    if (g_DbConn && g_DbConn->is_open()) return true;

    g_DbConn.reset();

    try {
        g_DbConn = std::make_unique<pqxx::connection>(BuildDsn());
    } catch (const pqxx::failure &e) {
        LogMessage("LinuxAuth: pqxx::connection failed: %s\n", e.what());
        g_DbConn.reset();
        return false;
    } catch (const std::exception &e) {
        LogMessage("LinuxAuth: connection unexpected error: %s\n", e.what());
        g_DbConn.reset();
        return false;
    }

    if (!g_DbConn->is_open()) {
        LogMessage("LinuxAuth: pqxx::connection opened but is_open()=false\n");
        g_DbConn.reset();
        return false;
    }

    LogMessage("LinuxAuth: connected to Postgres (%s)\n",
               g_DbConn->dbname());
    return true;
}

// Phase AH-9: persist the issued ticket so the game server can validate
// the suffix a client later presents on the cleartext UDP global port.
// The Win32 server received issued tickets over an SSL handoff
// (RegisterSectorServer); the Linux port dropped that, leaving
// ProcessTicketInfo unable to check the suffix at all. We restore the
// handoff through the net7_user database both processes already share:
// UPSERT (token, expires_at) keyed by the account name.
//
// `key` and `token` are split on the FIRST '-' of the issued ticket, the
// SAME rule the game server's strtok(ticket, "-") uses -- so whatever the
// validator extracts is exactly what we store, even if a username itself
// contains a '-'. Failure is non-fatal to the login (we still return the
// ticket); it just means the subsequent game-server validation will
// reject it and the player retries. We log loudly rather than swallow it.
void StoreLoginTicket(const char *key, const char *token,
                      unsigned long long expires_at_ms)
{
    g_DbMutex.Lock();
    if (!EnsureDbConnectedLocked()) {
        g_DbMutex.Unlock();
        LogMessage("LinuxAuth: StoreLoginTicket -- no DB connection, ticket "
                   "for '%s' NOT persisted (game-server login will reject)\n",
                   key);
        return;
    }

    try {
        pqxx::work tx(*g_DbConn);
        tx.exec_params(
            "INSERT INTO login_ticket (username, token, expires_at) "
            "VALUES ($1, $2, $3) "
            "ON CONFLICT (username) DO UPDATE "
            "SET token = EXCLUDED.token, expires_at = EXCLUDED.expires_at",
            key, token, static_cast<long long>(expires_at_ms));
        tx.commit();
    } catch (const pqxx::broken_connection &e) {
        LogMessage("LinuxAuth: StoreLoginTicket lost connection: %s\n", e.what());
        g_DbConn.reset();
    } catch (const std::exception &e) {
        LogMessage("LinuxAuth: StoreLoginTicket failed for '%s': %s\n",
                   key, e.what());
    }

    g_DbMutex.Unlock();
}

// Phase X: sodium_init() is idempotent across threads -- returns 0 on
// first success, 1 if already initialized, -1 on failure. Called once
// per process from the first ValidateAccountLinux invocation under the
// same mutex that guards the connection pool. Wrapping a static bool
// inside the lock keeps the success/failure check race-free.
bool g_SodiumReady = false;

static bool EnsureSodiumReadyLocked()
{
    if (g_SodiumReady) return true;
    if (sodium_init() < 0) {
        LogMessage("LinuxAuth: sodium_init() failed -- password "
                   "verification cannot proceed\n");
        return false;
    }
    g_SodiumReady = true;
    return true;
}

// Returns true if (username, password) verifies against the stored
// Argon2id PHC string in net7_user.accounts. Phase X replaced the
// `digest('md5')` SQL comparison: the plaintext now never leaves the
// login-server process address space -- the query reads back the stored
// PHC string and libsodium's crypto_pwhash_str_verify() does the
// constant-time comparison locally. Hostile input still binds as a
// parameter; pqxx + libpqxx prevent SQL injection. The username
// existence (positive result row) is intentionally not distinguished
// from a password mismatch in the return value, to deny user-enumeration
// via timing differences.
bool ValidateAccountLinux(const char *username, const char *password)
{
    if (!username || !password || !*username || !*password) return false;

    bool ok = false;
    std::string stored_phc;
    bool have_row = false;

    g_DbMutex.Lock();

    if (!EnsureSodiumReadyLocked()) {
        g_DbMutex.Unlock();
        return false;
    }
    if (!EnsureDbConnectedLocked()) {
        g_DbMutex.Unlock();
        return false;
    }

    try {
        pqxx::work tx(*g_DbConn);

        pqxx::result r = tx.exec_params(
            "SELECT password_phc FROM accounts WHERE username = $1",
            username);

        if (!r.empty()) {
            stored_phc = r[0][0].as<std::string>();
            have_row = true;
        }
        tx.commit();
    } catch (const pqxx::broken_connection &e) {
        LogMessage("LinuxAuth: ValidateAccount lost connection: %s\n", e.what());
        g_DbConn.reset(); // force reconnect on next call
        g_DbMutex.Unlock();
        return false;
    } catch (const pqxx::sql_error &e) {
        LogMessage("LinuxAuth: ValidateAccount SQL error: %s [query=%s]\n",
                   e.what(), e.query().c_str());
        g_DbMutex.Unlock();
        return false;
    } catch (const std::exception &e) {
        LogMessage("LinuxAuth: ValidateAccount unexpected: %s\n", e.what());
        g_DbMutex.Unlock();
        return false;
    }

    g_DbMutex.Unlock();

    if (!have_row) return false;
    if (stored_phc.empty()) return false;

    // crypto_pwhash_str_verify returns 0 on success. The stored string
    // is NUL-terminated PHC; libsodium parses the algorithm + params out
    // of the string itself, so future scheme bumps don't need new code
    // paths here.
    ok = crypto_pwhash_str_verify(stored_phc.c_str(),
                                  password,
                                  strlen(password)) == 0;
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

        // Prepared-statement bind makes SQL injection structurally impossible;
        // no whitelist gate needed before passing the raw fields through.
        if (ValidateAccountLinux(u, p)) {
            g_TicketMutex.Lock();
            const char *ticket = BuildTicketLocked(u);
            char issued[128];
            strncpy(issued, ticket, sizeof(issued) - 1);
            issued[sizeof(issued) - 1] = 0;
            g_TicketMutex.Unlock();

            // AH-9: persist the suffix so the game server can validate it.
            // Split on the FIRST '-' (the game server's strtok rule): key =
            // before, token = after. `issued` always contains a '-' because
            // BuildTicketLocked formats "%s-%s".
            char persisted[128];
            strncpy(persisted, issued, sizeof(persisted) - 1);
            persisted[sizeof(persisted) - 1] = 0;
            char *dash = strchr(persisted, '-');
            if (dash) {
                *dash = 0;
                const char *key = persisted;
                const char *token = dash + 1;
                unsigned long long now_ms =
                    static_cast<unsigned long long>(time(nullptr)) * 1000ULL;
                StoreLoginTicket(key, token, now_ms + kTicketExpireMs);
            }

            snprintf(info, sizeof(info), "Valid=TRUE\r\nTicket=%s\r\n", issued);
            LogMessage("LinuxAuth: ticket issued for '%s'\n", u);
        } else {
            LogMessage("LinuxAuth: ValidateAccount failed for '%s'\n", u);
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

// --------------------------------------------------------------------------
// Phase AN: /updateCheck launcher self-update gate.
// --------------------------------------------------------------------------

// Case-insensitive hex equality for SHA-512 strings. Empty never matches (a
// missing local hash from the client must read as a mismatch, not a match).
bool HashEq(const std::string &a, const std::string &b)
{
    if (a.empty() || b.empty() || a.size() != b.size()) return false;
    for (size_t i = 0; i < a.size(); ++i)
        if (tolower((unsigned char)a[i]) != tolower((unsigned char)b[i]))
            return false;
    return true;
}

// Extract a JSON string field from the POST body. The launcher sends a fixed,
// self-describing contract {"launcherHash":"...","proxyHash":"..."}; a full
// JSON parser is unwarranted, but bounds are checked so a malformed body yields
// "" (-> a mismatch -> UPDATE_NEEDED) rather than a crash. The extracted value
// is ONLY ever compared, never reflected into the response.
std::string JsonField(const char *text, const char *field)
{
    std::string needle = std::string("\"") + field + "\"";
    const char *p = strstr(text, needle.c_str());
    if (!p) return "";
    p = strchr(p + needle.size(), ':');
    if (!p) return "";
    ++p;
    while (*p == ' ' || *p == '\t' || *p == '\n' || *p == '\r') ++p;
    if (*p != '"') return "";
    ++p;
    const char *end = strchr(p, '"');
    if (!end) return "";
    return std::string(p, (size_t)(end - p));
}

char *MakeServiceUnavailable(size_t *out_len)
{
    static const char *kErr503 =
        "HTTP/1.1 503 Service Unavailable\r\n"
        "Server: AuthServer/2.5\r\n"
        "Content-Length: 0\r\n"
        "Connection: Keep-Alive\r\n"
        "\r\n";
    size_t n = strlen(kErr503);
    char *r = new char[n + 1];
    memcpy(r, kErr503, n + 1);
    *out_len = n;
    return r;
}

// POST /updateCheck  body:
//   {"launcherHash","proxyHash","posFeedHash","injectHash","enbmodHash"}
// Compares the client's local hashes to the manifest cache and replies:
//   - UP_TO_DATE when all match;
//   - UPDATE_NEEDED with a conditional file list otherwise -- a launcher
//     mismatch ships FreyaLauncher.exe + FreyaLauncher.cfg (the cfg has no
//     independent hash and rides with the launcher). The proxy, each MVAS
//     injection binary (bin/FreyaPosFeed.dll, bin/FreyaInject.exe), and the Lua
//     mod runtime (bin/enbmod.dll) are hashed INDEPENDENTLY and shipped on their
//     own mismatch, so a feed-only or runtime-only patch reaches existing
//     installs without a launcher bump. Each optional file ships only when the
//     manifest carries a hash for it (optional add-on).
// Cache miss (manifest not loaded) -> 503, which the launcher reads as the
// server being DOWN (fail-closed: no update decision on an empty cache).
char *HandleUpdateCheck(size_t *out_len, char *recv_buffer)
{
    PatcherManifest &mf = PatcherManifest::Instance();
    // Lazily re-fetch (TTL-bounded) so a client-only patch -- which never
    // changes the login image and so never triggers a login restart on deploy
    // -- is still picked up here without a manual bounce.
    mf.RefreshIfStale();
    if (mf.Empty())
        return MakeServiceUnavailable(out_len);

    const char *body = strstr(recv_buffer, "\r\n\r\n");
    body = body ? body + 4 : "";

    std::string clientLauncher = JsonField(body, "launcherHash");
    std::string clientProxy    = JsonField(body, "proxyHash");
    std::string clientPosFeed  = JsonField(body, "posFeedHash");
    std::string clientInject   = JsonField(body, "injectHash");
    std::string clientEnbmod   = JsonField(body, "enbmodHash");

    const std::string posFeedSrv = mf.PosFeedDllHash();   // "" when not published
    const std::string injectSrv  = mf.InjectExeHash();    // "" when not published
    const std::string enbmodSrv  = mf.EnbmodDllHash();    // "" when not published

    bool launcherOk = HashEq(clientLauncher, mf.LauncherExeHash());
    bool proxyOk    = HashEq(clientProxy, mf.ProxyExeHash());
    // The MVAS pair and enbmod.dll are checked INDEPENDENTLY, like the proxy. An
    // unpublished file (empty server hash) is treated as up-to-date: there is
    // nothing to ship and we never emit a file the manifest does not vouch for.
    bool posFeedOk  = posFeedSrv.empty() || HashEq(clientPosFeed, posFeedSrv);
    bool injectOk   = injectSrv.empty()  || HashEq(clientInject, injectSrv);
    bool enbmodOk   = enbmodSrv.empty()  || HashEq(clientEnbmod, enbmodSrv);

    std::string json;
    if (launcherOk && proxyOk && posFeedOk && injectOk && enbmodOk)
    {
        json = "{\"status\":\"UP_TO_DATE\"}";
    }
    else
    {
        const std::string base = mf.DlBase();
        std::string files;
        auto add = [&](const char *rel, const std::string &url, const std::string &hash) {
            if (!files.empty()) files += ",";
            files += "{\"relativePath\":\"";
            files += rel;
            files += "\",\"url\":\"";
            files += url;
            files += "\",\"hash\":\"";
            files += hash;
            files += "\"}";
        };
        if (!launcherOk)
        {
            add("FreyaLauncher.exe", base + "/FreyaLauncher.exe", mf.LauncherExeHash());
            // The cfg has no independent hash and rides with the launcher.
            add("FreyaLauncher.cfg", base + "/FreyaLauncher.cfg", mf.LauncherCfgHash());
        }
        if (!proxyOk)
        {
            add("bin/FreyaProxy.exe", base + "/FreyaProxy.exe", mf.ProxyExeHash());
        }
        if (!posFeedOk)
        {
            add("bin/FreyaPosFeed.dll", base + "/FreyaPosFeed.dll", posFeedSrv);
        }
        if (!injectOk)
        {
            add("bin/FreyaInject.exe", base + "/FreyaInject.exe", injectSrv);
        }
        if (!enbmodOk)
        {
            add("bin/enbmod.dll", base + "/enbmod.dll", enbmodSrv);
        }
        json = "{\"status\":\"UPDATE_NEEDED\",\"files\":[" + files + "]}";
    }

    return HttpResult(out_len, json.c_str(), "application/json");
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
    if (strstr(recv_buffer, "/updateCheck")) {
        return HandleUpdateCheck(out_len, recv_buffer);
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
