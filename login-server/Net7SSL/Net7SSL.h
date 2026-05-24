// Net7SSL.h

#ifndef _NET_7SSL_H_INCLUDED_
#define _NET_7SSL_H_INCLUDED_

#define SQL_ENABLE
#ifdef SQL_ENABLE
    #define USE_MYSQL_ACCOUNT_DATA
	#define USE_MYSQL_STATIONS
    #define USE_MYSQL_SECTOR 
    #define USE_MYSQL_ITEMS
#endif

#ifdef USE_MYSQL_ACCOUNT_DATA
    #define SQL_ACCOUNT_STRING " - SQL Accounts"
#else
    #define SQL_ACCOUNT_STRING ""
#endif

//#define BETA_TESTING

//If you use this setting, server will be local-cert login only
//#define SSL_SYSTEM_OFF

#define SSL_INSTANCE_MUTEX_NAME "Net7SSL Instance"

#define _CRT_SECURE_NO_WARNINGS 1		// Disable Warning messages about new Secure Functions in VS2008
#pragma warning(disable:4996)

#include <memory.h>
#include <stdio.h>
#include <stdlib.h>
#include <stdarg.h>
#include <string.h>
#include <time.h>
#include <math.h>
#include <ctype.h>

#ifdef WIN32
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
	#define CONFIG_FILE				"Net7Config.cfg"
	#pragma pack(1)
	#define ATTRIB_PACKED

	#define WIN32_THREAD_STACK_SIZE 0x10000

	typedef unsigned __int64 u64;
	typedef signed __int64  s64;
	typedef unsigned __int32 u32;
	typedef signed __int32  s32;
    typedef unsigned short  u16;
    typedef signed short    s16;
    typedef unsigned char   u8;
    typedef signed char     s8;

#else // LINUX
	// Phase M: the per-target compat/ shim that used to live in
	// login-server/Net7SSL/compat/ was deleted. The legacy code still
	// references Win32 typedefs (DWORD, HANDLE, SOCKET, ...) and helpers
	// (Sleep, GetTickCount) directly, so inline thin POSIX wrappers here.
	#include <sys/types.h>
	#include <sys/socket.h>
	#include <sys/times.h>
	#include <sys/time.h>
	#include <netinet/in.h>
	#include <netinet/tcp.h>
	#include <arpa/inet.h>
	#include <errno.h>
	#include <netdb.h>
	#include <pthread.h>
	#include <unistd.h>
	#include <fcntl.h>
	#include <strings.h>
	#include <cstdint>
	#include <ctime>

	#define ATTRIB_PACKED __attribute__((packed))
	#ifndef MAX_PATH
	#define MAX_PATH        260
	#endif
	// Win32 socket name closesocket -> POSIX close.
	#define closesocket(s) ::close(s)
	#ifndef WSAECONNRESET
	#define WSAECONNRESET	ECONNRESET
	#endif
	#ifndef UCHAR
	typedef unsigned char UCHAR;
	#endif
	#ifndef UINT
	typedef unsigned int UINT;
	#endif

	typedef uint64_t u64;
	typedef int64_t  s64;
	typedef uint32_t u32;
	typedef int32_t  s32;
	typedef uint16_t u16;
	typedef int16_t  s16;
	typedef uint8_t  u8;
	typedef int8_t   s8;

	#define SERVER_LOGS_PATH        "./logs/"
	#define SERVER_HTML_PATH        "./html/"
	#define SERVER_DATABASE_PATH    "./database/"
	#define SERVER_USER_PATH        "./database/Users/"
	#define CONFIG_FILE				"Net7Config.cfg"

	// Some MSVC <-> GCC redefines kept for legacy call sites.
	#define _vsnprintf vsnprintf
	#define _isnan isnan
	#define _alloca alloca
	// MSVC-only safe-string wrappers; map to their POSIX counterparts.
	#define strcpy_s(dst, sz, src)  strncpy((dst), (src), (sz)-1)
	#define sprintf_s(dst, sz, ...) snprintf((dst), (sz), __VA_ARGS__)
	// MSVC-only directives gcc doesn't understand.
	#define WINAPI
	#define __cdecl
	#define SOCKADDR_IN struct sockaddr_in
	// SOMAXCONN normally provided by <sys/socket.h> on Linux too — no-op.

	// Minimal Win32 typedef aliases used by both walled (#ifdef WIN32)
	// and Linux-active legacy code. Walled code never compiles on
	// Linux; these are here so the Linux paths that reference
	// SOCKET m_Socket (etc.) typecheck.
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

	// Win32 helpers used by Linux-active code.
	static inline void Sleep(DWORD ms) {
	    ::usleep(static_cast<useconds_t>(ms) * 1000u);
	}
	static inline DWORD GetTickCount() {
	    struct timespec ts;
	    ::clock_gettime(CLOCK_MONOTONIC, &ts);
	    return static_cast<DWORD>(
	        (static_cast<uint64_t>(ts.tv_sec) * 1000ull
	         + static_cast<uint64_t>(ts.tv_nsec) / 1000000ull) & 0xFFFFFFFFu);
	}

	unsigned long GetCurrentDirectory(unsigned long size, char *path);
	int SetCurrentDirectory(const char *path);
	bool DeleteFile(const char *filename);

#endif

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
#define CONNECTION_TYPE_MVAS_TO_PROXY                   6
#define CONNECTION_TYPE_GLOBAL_SERVER_TO_PROXY          7
#define CONNECTION_TYPE_MASTER_SERVER_TO_PROXY          8
#define CONNECTION_TYPE_SECTOR_SERVER_TO_PROXY          9
#define CONNECTION_TYPE_GLOBAL_PROXY_TO_SERVER		    10

// Port macros + CLIENT_TYPE_* tags live in common/include/net7/Ports.h
// (Phase R Wave 2 — wire-load-bearing, kept in exactly one place).
#include <net7/Ports.h>

#define	MAX_BUFFER					4096
//#define SSL_PORT					8891	// handles authentication - HTTPS protocol (0x22BB)


#define	RACE_TERRAN					0
#define RACE_JENQUAI				1
#define RACE_PROGEN					2

#define PROFESSION_WARRIOR			0
#define PROFESSION_TRADER		    1
#define PROFESSION_EXPLORER			2

//these should probably go in a class - ho hum
extern char g_LogFilename[MAX_PATH];
extern char g_InternalIP[MAX_PATH];
extern char g_DomainName[MAX_PATH];
extern char g_MySQL_User[MAX_PATH];
extern char g_MySQL_Pass[MAX_PATH];
extern char g_MySQL_Host[MAX_PATH];
extern int g_DASE;
extern unsigned long g_receive_time;
extern long g_PlayerCount;
extern unsigned long g_cumulative_mem;
extern long g_MaxPlayerCount;

extern char g_Galaxy_Name[MAX_PATH];

void LockMessageQueue();
void UnlockMessageQueue();
void LogMessage(const char *format, ...);
void LogDebug(char *format, ...);
void LogChatMsg(char *format, ...);
void LogMySQLMsg(char *format, ...);
void DumpBuffer(unsigned char *buffer, int length);
void DumpBufferToFile(unsigned char *buffer, int length, char *filename, bool rawData);
char* GetSectorName(long sector_id);
unsigned long GetNet7TickCount();
void LoadSectorData();


class GMemoryHandler;
class ServerManager;
class PlayerManager;
class StringManager;
class ItemBaseManager;
class AccountManager;
class SaveManager;
class SSL_DenyList;
class CircularBuffer;
class ConnectionManager;

extern ServerManager * g_ServerMgr;
extern GMemoryHandler * g_GlobMemMgr;
extern PlayerManager * g_PlayerMgr;
extern StringManager * g_StringMgr;
extern ItemBaseManager * g_ItemBaseMgr;
extern AccountManager * g_AccountMgr;
extern SaveManager	  * g_SaveMgr;
extern SSL_DenyList * g_SSL_Deny_List;
extern ServerManager *g_ServerMgr;
extern ConnectionManager *g_ConnectionMgr;

extern bool g_Debug;
extern bool g_ServerShutdown;
extern bool g_LoggedIn;
extern bool g_ShuttingDown;

#endif // _NET_7SSL_H_INCLUDED_
