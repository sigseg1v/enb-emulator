// Net7.h
//
// Phase M: server-native-Linux only. The Win32 build of net7proxy is
// not maintained — the proxy is the TCP entry point for the Westwood
// RSA+RC4 handshake on port 3801, not a client launcher. Win32 branches
// were stripped to remove cross-platform parity that nothing exercises.

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

#include <sys/types.h>
#include <sys/socket.h>
#include <netinet/in.h>
#include <netinet/tcp.h>
#include <arpa/inet.h>
#include <netdb.h>
#include <unistd.h>
#include <fcntl.h>
#include <errno.h>
#include <pthread.h>
#include <strings.h>
#include <cstdint>
#include <ctime>
#include <limits.h>

// MSVC names that legacy code uses; map to their POSIX equivalents.
#define _vsnprintf  vsnprintf
#define closesocket(s) ::close(s)
#define WSAGetLastError() errno

// MSVC-only directives gcc doesn't understand.
#define __cdecl
#ifndef WINAPI
#define WINAPI
#endif

#ifndef MAX_PATH
#define MAX_PATH 260
#endif

// Phase M Wave 3: the proxy's ~25-symbol Win32 typedef shim was retired.
// Only SOCKET / INVALID_SOCKET / SOCKET_ERROR are kept — 60+ Linux-active
// call sites across the listener/connection layer use them as canonical
// socket idioms, and rewriting them is cosmetic.
typedef int SOCKET;
#ifndef INVALID_SOCKET
#  define INVALID_SOCKET (-1)
#endif
#ifndef SOCKET_ERROR
#  define SOCKET_ERROR   (-1)
#endif

// The Net7Proxy logs/database paths are unused on the server-side Linux
// build (Net7Proxy was originally a client-side launcher). Keep the names
// defined so any legacy reference compiles.
#define SERVER_LOGS_PATH        "./logs/"
#define SERVER_HTML_PATH        "./html/"
#define SERVER_DATABASE_PATH	"./database/"
#define SERVER_USER_PATH        "./database/Users/"

#define CONFIG_FILE             "Net7Config.cfg"

// gcc equivalent of MSVC's #pragma pack(1) / __declspec(packed).
// Applied per-struct via the ATTRIB_PACKED suffix (see PacketStructures.h).
#define ATTRIB_PACKED __attribute__((packed))

#include <stdint.h>
typedef uint64_t u64;
typedef int64_t  s64;
typedef uint32_t u32;
typedef int32_t  s32;
typedef unsigned short  u16;
typedef signed short    s16;
typedef unsigned char   u8;
typedef signed char     s8;

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
