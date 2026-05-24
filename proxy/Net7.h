// Net7.h

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

#ifdef WIN32
#include <crtdbg.h>
#pragma warning(disable:4996)


#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN		// Exclude rarely-used stuff from Windows headers
#endif

#include <windows.h>
#include <winsock2.h>
#include <objbase.h>
#include <process.h>
#include <malloc.h>
#define SERVER_LOGS_PATH        "..\\logs\\"
#define SERVER_HTML_PATH        "..\\html\\"
#define SERVER_DATABASE_PATH	"..\\database\\"
#define SERVER_USER_PATH		"..\\database\\Users\\"
#else // Linux
// Phase M: the legacy code references Win32 typedefs (DWORD, HANDLE,
// SOCKET, ...) and helpers (Sleep, GetTickCount) directly. Inline them
// here as thin POSIX wrappers; the per-target `compat/` shim directory
// that used to provide them was deleted in Phase M.
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
// _vsnprintf is the msvcrt name; POSIX has vsnprintf.
#define _vsnprintf  vsnprintf
// Win32 sockets API names that legacy code uses:
#define closesocket(s) ::close(s)
#define WSAGetLastError() errno
// MSVC-only directives the compiler doesn't understand on gcc:
#define __cdecl
#ifndef WINAPI
#define WINAPI
#endif
#ifndef UINT
typedef unsigned int UINT;
#endif
#ifndef SOCKADDR_IN
#define SOCKADDR_IN struct sockaddr_in
#endif
// MAX_PATH is a Win32 idiom; default to PATH_MAX semantics.
#include <limits.h>
#ifndef MAX_PATH
#define MAX_PATH 260
#endif

// Minimal Win32 typedef aliases used by both walled (#ifdef WIN32) and
// Linux-active legacy code. Walled code never compiles these on Linux;
// these are here so the Linux paths that reference SOCKET m_Socket
// (etc.) typecheck. Treat as a stable shim, not a place to grow new
// Windows-isms.
typedef uint32_t  DWORD;
typedef uint16_t  WORD;
typedef uint8_t   BYTE;
typedef int       BOOL;
typedef int32_t   LONG;
typedef uint32_t  ULONG;
typedef int64_t   LONGLONG;
typedef uint64_t  ULONGLONG;
typedef void*       LPVOID;
typedef const char* LPCSTR;
typedef char*       LPSTR;
typedef char*       LPTSTR;
typedef const char* LPCTSTR;
typedef void* HANDLE;
typedef int   SOCKET;
#ifndef INVALID_SOCKET
#  define INVALID_SOCKET (-1)
#endif
#ifndef SOCKET_ERROR
#  define SOCKET_ERROR   (-1)
#endif
#ifndef TRUE
#  define TRUE  1
#endif
#ifndef FALSE
#  define FALSE 0
#endif
#ifndef TEXT
#  define TEXT(x) x
#endif
#ifndef _T
#  define _T(x) x
#endif
#ifndef INVALID_HANDLE_VALUE
#  define INVALID_HANDLE_VALUE ((HANDLE)(intptr_t)(-1))
#endif
#ifndef INFINITE
#  define INFINITE       0xFFFFFFFFu
#endif
#ifndef WAIT_OBJECT_0
#  define WAIT_OBJECT_0  0x00000000u
#endif
#ifndef WAIT_TIMEOUT
#  define WAIT_TIMEOUT   0x00000102u
#endif
#ifndef WAIT_FAILED
#  define WAIT_FAILED    0xFFFFFFFFu
#endif

// Phase M: Sleep() and GetTickCount() were inlined Win32-name shims here;
// they've been retired. Live call sites use ::usleep(ms * 1000) and
// Net7TickMs() (declared in <net7/Ticks.h>, included below) directly.

// The Net7Proxy logs/database paths are unused on the server-side Linux
// build (Net7Proxy was originally a client-side launcher). Keep the names
// defined so any legacy reference compiles.
#define SERVER_LOGS_PATH        "./logs/"
#define SERVER_HTML_PATH        "./html/"
#define SERVER_DATABASE_PATH	"./database/"
#define SERVER_USER_PATH		"./database/Users/"
#endif

#define CONFIG_FILE				"Net7Config.cfg"
#ifdef WIN32
#pragma pack(1)
#define ATTRIB_PACKED
#else
// gcc equivalent of MSVC's #pragma pack(1) / __declspec(packed).
// Applied per-struct via the ATTRIB_PACKED suffix (see PacketStructures.h).
#define ATTRIB_PACKED __attribute__((packed))
#endif

#ifdef WIN32
typedef unsigned __int64 u64;
typedef signed __int64  s64;
typedef unsigned __int32 u32;
typedef signed __int32  s32;
#else
#include <stdint.h>
typedef uint64_t u64;
typedef int64_t  s64;
typedef uint32_t u32;
typedef int32_t  s32;
#endif
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
bool engine_open_process(char * processwindowtitle);
bool engine_read_process(LPVOID lpBaseAddress, LPVOID lpBuffer, DWORD nSize);
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

static inline void check_memory ()
{
#ifdef WIN32
    _ASSERTE (_CrtCheckMemory ());
#endif
}

#endif // _NET_7_H_INCLUDED_
