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
	// Phase J Linux port — the original ad-hoc #defines below predate the
	// shared compat shim that proxy/ and server/ use. Pull in those shims
	// first; the remaining typedefs are kept because much of the
	// login-server tree references them with subtly-different signatures
	// (e.g. `unsigned long` DWORD vs. shim's `uint32_t`). The compat shim
	// is `#ifndef`-guarded throughout so duplicate definitions are not
	// emitted.
	#include "compat/win32_shim.h"
	#include "compat/threading_shim.h"
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

	#define ATTRIB_PACKED __attribute__((packed))
	#ifndef MAX_PATH
	#define MAX_PATH        260
	#endif
	// Win32 socket name closesocket -> POSIX close.
	#define closesocket(s) ::close(s)
	#define strcasecmp strcasecmp
	#ifndef WSAECONNRESET
	#define WSAECONNRESET	ECONNRESET
	#endif
	#ifndef UCHAR
	typedef unsigned char UCHAR;
	#endif
	#ifndef UINT
	typedef unsigned int UINT;
	#endif

	#include <stdint.h>
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

	// Some MSVC <-> GCC redefines
	#define snprintf snprintf
	#define _vsnprintf vsnprintf
	#define strcasecmp strcasecmp
	#define _isnan isnan
	#define _alloca alloca
	#define _sleep Sleep
	#define strcasecmp strcasecmp
	// atoll is specific to MSVC; atoll is the POSIX equivalent.
	#define atoll atoll
	// MSVC-only safe-string wrappers; map to their POSIX counterparts.
	#define strcpy_s(dst, sz, src)  strncpy((dst), (src), (sz)-1)
	#define sprintf_s(dst, sz, ...) snprintf((dst), (sz), __VA_ARGS__)
	// MSVC-only directives gcc doesn't understand.
	#define WINAPI
	#define __cdecl
	#define SOCKADDR_IN struct sockaddr_in
	// SOMAXCONN normally provided by <sys/socket.h> on Linux too — no-op.

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

#define CLIENT_TYPE_FIXED_PORT                          1
#define CLIENT_TYPE_MULTI_PORT                          2


#define	MAX_BUFFER					4096
#define SSL_PORT					443		// handles authentication (0x01BB)
//#define SSL_PORT					8891	// handles authentication - HTTPS protocol (0x22BB)

#define GLOBAL_SERVER_PORT			3805	// handles multiple galaxies
#define MASTER_SERVER_PORT			3801	// handles a single galaxy
#define SECTOR_SERVER_PORT			3501	// handles a single sector - note we start from 3501 now because 3500 is used as the local TCP port in Net7Proxy
#define MVAS_LOGIN_PORT				3806

#define SSL_LOCALCERT_LOGIN_PORT    3807
#define UDP_MASTER_SERVER_PORT      3808
#define PROXY_SERVER_PORT           3809


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
