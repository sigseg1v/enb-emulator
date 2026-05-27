// Net7SSL.cpp
//
// Phase M: deleted the legacy Win32 main(), instance-mutex, MySQL sector
// table loader, and the original "register-with-auth-server" helper.
// Server-native build is Linux-only; the Linux Phase J entry point below
// is the only main() the binary needs. Net7SSL.h still re-declares some
// helpers (LogMessage, GetCurrentDirectory, ...) that have small Linux
// implementations at the bottom of this file.
//
// This mirrors proxy/Net7.cpp's Linux-only shape.

#include "Net7SSL.h"
#include "SSL_Listener.h"
#include "MailslotManager.h"
#include <net7/SingleInstance.h>

#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <cstdarg>
#include <ctime>
#include <unistd.h>
#include <signal.h>
#include <arpa/inet.h>

// Phase J IPC: AF_UNIX SOCK_DGRAM replaces the Win32 mailslot pair. The
// server side (server/src/Net7.cpp) binds the inverse mapping. Both sides
// need a writable directory at /run/net7-ipc/ — docker-compose mounts a
// shared named volume there.
const char *g_OutputSlot = "/run/net7-ipc/net7.sock";
const char *g_InputSlot  = "/run/net7-ipc/net7SSL.sock";
const char *g_EventName  = "Net7SSLSlot";

// Globals declared extern in Net7SSL.h. We define just enough here to make
// the Linux build link. WIN32-walled translation units in this directory
// would otherwise own these symbols.
char g_LogFilename[MAX_PATH]   = {0};
char g_InternalIP[MAX_PATH]    = {0};
char g_DomainName[MAX_PATH]    = "localhost";
char g_MySQL_User[MAX_PATH]    = {0};
char g_MySQL_Pass[MAX_PATH]    = {0};
char g_MySQL_Host[MAX_PATH]    = {0};
char g_Galaxy_Name[MAX_PATH]   = {0};
int  g_DASE                    = 0;
unsigned long g_receive_time   = 0;
long g_PlayerCount             = 0;
unsigned long g_cumulative_mem = 0;
long g_MaxPlayerCount          = 0;

bool g_Debug                   = false;
bool g_ServerShutdown          = false;
bool g_LoggedIn                = false;
bool g_ShuttingDown            = false;

// Manager singletons — Linux build does not instantiate the heavy ones.
class GMemoryHandler;
class ServerManager;
class PlayerManager;
class StringManager;
class ItemBaseManager;
class AccountManager;
class SaveManager;
class SSL_DenyList;
class ConnectionManager;

GMemoryHandler   * g_GlobMemMgr   = nullptr;
ServerManager    * g_ServerMgr    = nullptr;
PlayerManager    * g_PlayerMgr    = nullptr;
StringManager    * g_StringMgr    = nullptr;
ItemBaseManager  * g_ItemBaseMgr  = nullptr;
AccountManager   * g_AccountMgr   = nullptr;
SaveManager      * g_SaveMgr      = nullptr;
SSL_DenyList     * g_SSL_Deny_List = nullptr;
ConnectionManager* g_ConnectionMgr = nullptr;

static unsigned long s_StartTick = 0;

// LogMessage is referenced from WestwoodRSA.cpp and other minimally-ported
// TUs. Match the variadic signature declared in Net7SSL.h.
void LogMessage(const char *format, ...)
{
    char buffer[8192];
    char timestr[20];
    time_t rawtime;
    struct tm * timeinfo;

    va_list args;
    va_start(args, format);
    vsnprintf(buffer, sizeof(buffer), format, args);
    va_end(args);

    time(&rawtime);
    timeinfo = localtime(&rawtime);
    strftime(timestr, sizeof(timestr), "%d/%m/%y %H:%M:%S", timeinfo);

    fprintf(stdout, "%s SSL:%s", timestr, buffer);
    fflush(stdout);
}

// Linux stubs for the Win32 directory helpers prototyped in Net7SSL.h.
unsigned long GetCurrentDirectory(unsigned long size, char *path)
{
    if (!path || size == 0) return 0;
    if (getcwd(path, size) == nullptr) return 0;
    return strlen(path);
}

int SetCurrentDirectory(const char *path)
{
    if (!path) return 0;
    return chdir(path) == 0 ? 1 : 0;
}

bool DeleteFile(const char *filename)
{
    if (!filename) return false;
    return unlink(filename) == 0;
}

unsigned long GetNet7TickCount()
{
    struct timespec ts;
    clock_gettime(CLOCK_MONOTONIC, &ts);
    unsigned long now = (unsigned long)(ts.tv_sec * 1000UL + ts.tv_nsec / 1000000UL);
    return now - s_StartTick;
}

void LockMessageQueue()   {}
void UnlockMessageQueue() {}
void LogDebug(char * /*format*/, ...)    {}
void LogChatMsg(char * /*format*/, ...)  {}
void LogMySQLMsg(char * /*format*/, ...) {}
void DumpBuffer(unsigned char * /*b*/, int /*l*/) {}
void DumpBufferToFile(unsigned char * /*b*/, int /*l*/, char * /*f*/, bool /*r*/) {}
char* GetSectorName(long /*sector_id*/) { return nullptr; }
void LoadSectorData() {}

static volatile sig_atomic_t s_shutdown_requested = 0;
static void handle_signal(int /*sig*/) { s_shutdown_requested = 1; g_ServerShutdown = true; }

int main(int argc, char **argv)
{
    // Line-buffer stdout so docker logs show output promptly.
    setvbuf(stdout, nullptr, _IOLBF, 0);
    setvbuf(stderr, nullptr, _IOLBF, 0);

    struct timespec ts0;
    clock_gettime(CLOCK_MONOTONIC, &ts0);
    s_StartTick = (unsigned long)(ts0.tv_sec * 1000UL + ts0.tv_nsec / 1000000UL);

    signal(SIGINT,  handle_signal);
    signal(SIGTERM, handle_signal);
    signal(SIGPIPE, SIG_IGN);

    // Default bind address: 0.0.0.0 (any). Allow override via env / argv[1].
    const char *bind_addr = "0.0.0.0";
    if (argc > 1) bind_addr = argv[1];
    if (const char *env = getenv("NET7SSL_BIND_ADDR")) bind_addr = env;

    unsigned long ip_address_internal = inet_addr(bind_addr);
    if (ip_address_internal == INADDR_NONE) {
        ip_address_internal = htonl(INADDR_ANY);
    }

    LogMessage("Net7SSL (Linux Phase J) starting — bind %s:%d\n", bind_addr, SSL_PORT);

    // Single-instance guard (flock on /run/enb-emulator/Net7SSL_Instance.pid).
    // Replaces the Win32 CreateMutex(NULL, TRUE, "Net7SSL Instance") guard.
    net7::SingleInstance instance_guard;
    if (!instance_guard.Acquire(SSL_INSTANCE_MUTEX_NAME))
    {
        fprintf(stderr, "Net7SSL: another instance is already running.\n");
        return 1;
    }

    // Stand up the SSL listener. Mirrors the SSL_Listener constructor used
    // by Win32 main_prog() above. The listener spawns its own accept loop
    // thread on Linux (see SSL_Listener.cpp).
    SSL_Listener *listener = new SSL_Listener(ip_address_internal, SSL_PORT);
    (void)listener;

    // Mailslot IPC peer (server <-> login keepalive). Same wiring as Win32
    // main_prog(): every ~10s send "Ping" to the server's recv socket; if
    // we don't hear from the peer for ~60s, declare it dead and exit.
    MailManager *MailMgr = new MailManager();

    LogMessage("Net7SSL listener up; entering main loop\n");

    unsigned long send_check  = GetNet7TickCount() + 60 * 1000;
    g_receive_time            = GetNet7TickCount() + 60 * 1000;

    while (!g_ServerShutdown) {
        usleep(500 * 1000); // 500ms — matches Win32 usleep(500 * 1000)
        unsigned long current_tick = GetNet7TickCount();

        MailMgr->CheckMessages();

        if (current_tick > (send_check + 10000)) {
            MailMgr->WriteMessage(const_cast<char *>("Ping"));
            send_check = current_tick;
        }

        if (current_tick > (g_receive_time + 60000)) {
            LogMessage("Net7 Server seems to have stopped\n");
            LogMessage("Net7SSL Terminating\n");
            break;
        }
    }

    delete MailMgr;

    LogMessage("Net7SSL shutting down\n");
    return 0;
}

