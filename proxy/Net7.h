// Net7.h
//
// Net7Proxy is a TCP/UDP terminator for the Westwood RSA+RC4 handshake
// (port 3801) and the master/global UDP planes. It builds in two flavours:
//
//   * Linux (g++) — the historical server-side deployment target. The
//     proxy runs as a docker service alongside the game server.
//   * Win32 PE (x86_64-w64-mingw32-g++) — the same proxy, packaged for
//     side-by-side execution under WINE next to the Win32 client. This
//     mode is what end users actually ship and run; the launcher spawns
//     the proxy as a sibling process inside the same WINE prefix.
//
// Header guards below isolate platform-specific includes/shims so the
// rest of the codebase can write straight BSD-style socket idioms
// (SOCKET / INVALID_SOCKET / closesocket / WSAGetLastError).

#ifndef _NET_7_H_INCLUDED_
#define _NET_7_H_INCLUDED_

#define VERSION "1.74"
#define VERSION_N 174

#define SSL_IN_NET7PROXY

#include <memory.h>
#include <stdio.h>
#include <stdlib.h>
#include <stdarg.h>
#include <string.h>
#include <time.h>
#include <math.h>
#include <ctype.h>
#include <cstdint>
#include <ctime>
#include <limits.h>

#ifdef _WIN32
// Win32 (MinGW) sockets. winsock2.h must precede windows.h.
#  ifndef WIN32_LEAN_AND_MEAN
#    define WIN32_LEAN_AND_MEAN
#  endif
#  include <winsock2.h>
#  include <ws2tcpip.h>
#  include <windows.h>
#  include <io.h>
#  include <process.h>
#  include <unistd.h>   // MinGW-posix: usleep, sleep, close
#  include <pthread.h>  // MinGW-posix: winpthreads
// winsock2.h provides SOCKET / INVALID_SOCKET / SOCKET_ERROR /
// closesocket / WSAGetLastError natively. MinGW also ships _vsnprintf.
// strcasecmp / strncasecmp aren't in <string.h> on Win32 — alias them
// to MSVC's _stricmp / _strnicmp so legacy call sites compile.
#  ifndef strcasecmp
#    define strcasecmp  _stricmp
#  endif
#  ifndef strncasecmp
#    define strncasecmp _strnicmp
#  endif
// MSG_NOSIGNAL doesn't exist on Win32 (no SIGPIPE on send-to-closed).
#  ifndef MSG_NOSIGNAL
#    define MSG_NOSIGNAL 0
#  endif
// POSIX setenv → Win32 _putenv_s. Ignore `overwrite` like setenv does
// (current callers all pass overwrite=0, and we only setenv after gating
// on getenv being empty, so the semantics line up).
#  include <stdlib.h>
static inline int setenv(const char *name, const char *value, int /*overwrite*/)
{
    return _putenv_s(name, value);
}
// POSIX `in_addr_t` is the inet_addr() return type / IPv4 in network byte
// order. Winsock spells it ULONG and exposes no `in_addr_t` typedef.
typedef ULONG in_addr_t;
// Socket shutdown(2) "how" values. POSIX: SHUT_RD/SHUT_WR/SHUT_RDWR.
// Winsock: SD_RECEIVE/SD_SEND/SD_BOTH. Map the POSIX names.
#  ifndef SHUT_RD
#    define SHUT_RD   SD_RECEIVE
#  endif
#  ifndef SHUT_WR
#    define SHUT_WR   SD_SEND
#  endif
#  ifndef SHUT_RDWR
#    define SHUT_RDWR SD_BOTH
#  endif
#else
// POSIX (Linux) sockets + threading.
#  include <sys/types.h>
#  include <sys/socket.h>
#  include <netinet/in.h>
#  include <netinet/tcp.h>
#  include <arpa/inet.h>
#  include <netdb.h>
#  include <unistd.h>
#  include <fcntl.h>
#  include <errno.h>
#  include <pthread.h>
#  include <strings.h>

// MSVC names that legacy code uses; map to their POSIX equivalents.
#  define _vsnprintf  vsnprintf
#  define closesocket(s) ::close(s)
#  define WSAGetLastError() errno

// MSVC-only directives gcc doesn't understand.
#  define __cdecl
#  ifndef WINAPI
#    define WINAPI
#  endif

#  ifndef MAX_PATH
#    define MAX_PATH 260
#  endif

// Phase M Wave 3: the proxy's ~25-symbol Win32 typedef shim was retired.
// Only SOCKET / INVALID_SOCKET / SOCKET_ERROR are kept — 60+ Linux-active
// call sites across the listener/connection layer use them as canonical
// socket idioms, and rewriting them is cosmetic.
typedef int SOCKET;
#  ifndef INVALID_SOCKET
#    define INVALID_SOCKET (-1)
#  endif
#  ifndef SOCKET_ERROR
#    define SOCKET_ERROR   (-1)
#  endif
#endif // !_WIN32

// The Net7Proxy logs/database paths are unused on the server-side Linux
// build (Net7Proxy was originally a client-side launcher). Keep the names
// defined so any legacy reference compiles.
#define SERVER_LOGS_PATH        "./logs/"
#define SERVER_HTML_PATH        "./html/"
#define SERVER_DATABASE_PATH	"./database/"
#define SERVER_USER_PATH        "./database/Users/"

#define CONFIG_FILE             "Net7Config.cfg"

// ATTRIB_PACKED + sized integer typedefs are defined in net7/Packing.h
// (included by net7/PacketStructures.h). Don't redefine here — the
// per-process redef triggers -Wmacro-redefined on cross-builds.
#include <stdint.h>

#define PERIODIC_CACHE_SEND_SIZE 512



// This should be incremented as needed to prevent obsolete Sector Servers
// from connecting to the Master Server.
#define SECTOR_SERVER_MAJOR_VERSION			0
#define SECTOR_SERVER_MINOR_VERSION			2

// Three server types are supported by the TcpListener and Connection classes
#define CONNECTION_TYPE_CLIENT_TO_GLOBAL_SERVER			1
#define CONNECTION_TYPE_CLIENT_TO_MASTER_SERVER			2
#define	CONNECTION_TYPE_CLIENT_TO_SECTOR_SERVER			3
#define CONNECTION_TYPE_MASTER_SERVER_TO_SECTOR_SERVER	4
#define CONNECTION_TYPE_SECTOR_SERVER_TO_SECTOR_SERVER	5
#define CONNECTION_TYPE_PROXY_TO_SECTOR_SERVER          6
#define CONNECTION_TYPE_SECTOR_SERVER_TO_PROXY          9
#define CONNECTION_TYPE_GLOBAL_PROXY_TO_SERVER		    10

// Port macros + CLIENT_TYPE_* tags live in common/include/net7/Ports.h
// (Phase R Wave 2 — wire-load-bearing, kept in exactly one place).
// Proxy's old SECTOR_SERVER_PORT (3500) was renamed to PROXY_LOCAL_TCP_PORT
// to distinguish it from the canonical sector server port (3501).
#include <net7/Ports.h>

// Phase M vocabulary sweep: monotonic ms tick counter replaces GetTickCount().
#include <net7/Ticks.h>

#define	MAX_BUFFER					25000
extern unsigned short ssl_port;

#define	RACE_TERRAN					0
#define RACE_JENQUAI				1
#define RACE_PROGEN					2

#define PROFESSION_WARRIOR			0
#define PROFESSION_TRADER		    1
#define PROFESSION_EXPLORER			2

extern char g_LogFilename[MAX_PATH];
extern char g_InternalIP[MAX_PATH];
extern char g_DomainName[MAX_PATH];
extern char g_UpstreamHost[MAX_PATH];
extern char g_MySQL_User[MAX_PATH];
extern char g_MySQL_Pass[MAX_PATH];
extern char g_MySQL_Host[MAX_PATH];
extern int g_DASE;

extern char g_Galaxy_Name[MAX_PATH];
extern long g_AddrStore;


void LockMessageQueue();
void UnlockMessageQueue();
void LogMessage(char *format, ...);
void LogVMessage(char *format, ...);
void LogDebug(char *format, ...);
void LogChatMsg(char *format, ...);
void DumpBuffer(unsigned char *buffer, int length);
void DumpBufferToFile(unsigned char *buffer, int length, char *filename, bool rawData);
// engine_* + ClientStillRunning + PatchClient + ShutdownClient are no-op
// stubs on Linux (server-native build). They remain declared because
// WIN32-walled translation units name them in their declarations.
bool engine_open_process(char * processwindowtitle);
bool engine_read_process(void* lpBaseAddress, void* lpBuffer, uint32_t nSize);
bool GetProcessHandle();
bool StartENBClient();
void PatchClient();
bool ClientStillRunning();
void WaitForEngineReady();
bool ShutdownClient();
void WaitForLogin();
unsigned long GetNet7TickCount();

class GMemoryHandler;
class ServerManager;
class PlayerManager;
class StringManager;
class ItemBaseManager;
class AccountManager;

extern ServerManager * g_ServerMgr;
extern GMemoryHandler * g_GlobMemMgr;
extern PlayerManager * g_PlayerMgr;
extern ItemBaseManager * g_ItemBaseMgr;
extern AccountManager * g_AccountMgr;
extern bool g_LoggedIn;

extern bool g_Debug;
extern bool g_ServerShutdown;
extern bool g_LocalCert;
extern bool g_OpcodeDebugging;
extern bool g_Packet_Opt_requested;
extern bool g_Debug_Launch;

// _CrtCheckMemory was MSVC-only; on Linux this is a no-op so legacy
// call sites in the proxy compile cleanly.
static inline void check_memory ()
{
}

#endif // _NET_7_H_INCLUDED_
