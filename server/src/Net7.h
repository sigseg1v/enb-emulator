// Net7.h
/* Net-7 Entertainment: Net-7 Earth and Beyond emulator project
**
** This code/content is licensed under the Creative Commons license, it is interactive content. You can view the terms of our:
** Creative Commons Attribution-Noncommercial-Share Alike 3.0 United States License
** http://creativecommons.org/licenses/by-nc-sa/3.0/us/
**
** Net-7 Emulator Project, an Earth & Beyond emulator by Net7 Entertainment is licensed under a Creative Commons Attribution-Noncommercial-Share Alike 3.0 United States License
**
** Based on a work at http://www.earthandbeyond.com
**
** Permissions beyond the scope of this license may be available at http://www.dreamersofdawn.org/docs/More_Information.htm
**
** The license can be modified at our discretion within the bounds of Creative Commons at any time.
**
** Copyright of our assets/code/software began in 2005-2009 ©, Net-7 Entertainment.
**
*/

#ifndef _NET_7_H_INCLUDED_
#define _NET_7_H_INCLUDED_

#define UPPER_VER	0
#define LOWER_VER	94
#define BUILD_VER   1302
#define VER_TYPE	"UDP Beta"

#define SQL_ENABLE

//#define DEV_SERVER

//#define SSL_IN_NET7PROXY

//#define DEV_QUICK_START  // This is for server devs to quick start a server locally for development. Most sectors will be missing

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

//#pragma warning(disable:4996 6255)

#include <memory.h>
#include <stdio.h>
#include <stdlib.h>
#include <stdarg.h>
#include <string.h>
#include <time.h>
#include <math.h>
#include <ctype.h>
#ifdef WIN32
    #include <io.h>
    #include <direct.h>
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

	#define WIN32_THREAD_STACK_SIZE 0x1024 //ensure threads don't use much stack, make sure large scratch areas are on the heap

	typedef unsigned __int64 u64;
	typedef signed __int64  s64;
	typedef unsigned __int32 u32;
	typedef signed __int32  s32;
    typedef unsigned short  u16;
    typedef signed short    s16;
    typedef unsigned char   u8;
    typedef signed char     s8;

#else // LINUX
	#define ATTRIB_PACKED __attribute__((packed))
	#define MAX_PATH        260
	#define SOCKET          int
	#define INVALID_SOCKET	-1
	#define closesocket	close
	#define WSAGetLastError() (errno)
	#define WSAECONNRESET	ECONNRESET

	#define u64				u_int64_t
	#define s64				int64_t
	// MSVC integer typedefs.
	typedef int64_t  __int64;
	typedef int32_t  __int32;
	typedef int16_t  __int16;
    	#define u32				u_int32_t
	#define s32				int32_t
	#define u16				u_int16_t
	#define s16				int16_t
	#define u8				u_int8_t
	#define s8				int8_t


	//#include <syslog.h>
	//#include <fcntl.h>
	//#include <netinet/in.h>
	//#include <sys/stat.h>
	//#include <sys/select.h>
	#include <arpa/inet.h>
	#include <sys/socket.h>
	#include <sys/types.h>
	#include <sys/times.h>
	#include <sys/time.h>
	#include <errno.h>
	#include <netdb.h>
	#include <pthread.h>
	#include <unistd.h>
	#include <cstdint>     // uintptr_t for _beginthreadex shim
	#include <sys/ioctl.h> // FIONBIO for ioctlsocket shim

	#define SERVER_LOGS_PATH        "../logs/"
	#define SERVER_HTML_PATH        "../html/"
	#define SERVER_DATABASE_PATH    "../database/"
	#define SERVER_USER_PATH        "../database/Users/"
	#define CONFIG_FILE				"Net7Config.cfg"

	// Phase M removed the MSVC-prefixed compat macros (_snprintf, _stricmp,
	// _strcmpi, _atoi64). Call sites use the POSIX names directly now.
	#define _isnan isnan
	#define _alloca alloca
	// MSVC's strtok_s and POSIX strtok_r have the same arg order.
	#define strtok_s strtok_r

	// Win32 calling-convention macros — no-ops on GCC
	#define __stdcall
	#define __cdecl
	#define __fastcall
	#define WINAPI
	#define CALLBACK

	// Phase M Wave 3: the Win32 OLE/COM string-pointer typedef block
	// (BSTR, LPSTR, LPCSTR, LPTSTR, LPCTSTR, LPVOID, LPCVOID) was
	// retired. Zero Linux-active call sites — every remaining hit was
	// either WIN32-walled or in vendored upstream code (xmlParser
	// _XMLWINDOWS branch, Net7Mysql MFC dialogs not built on Linux).
	// BSTR's definition now lives in common/include/net7/Packing.h
	// behind an `ifndef BSTR` guard so PacketStructures.h still compiles.

	// MSVC safe-string functions — map to their POSIX counterparts. Buffer size is
	// honored where possible; for *_s variants that take a size before the format
	// string we route to snprintf/strncpy/strncat which take size analogously.
	#include <stdio.h>
	#include <string.h>
	static inline int sprintf_s(char *dst, size_t size, const char *fmt, ...) {
		va_list ap; va_start(ap, fmt);
		int r = vsnprintf(dst, size, fmt, ap);
		va_end(ap);
		return r;
	}
	static inline int _snprintf_s(char *dst, size_t size, size_t /*count*/, const char *fmt, ...) {
		va_list ap; va_start(ap, fmt);
		int r = vsnprintf(dst, size, fmt, ap);
		va_end(ap);
		return r;
	}
	static inline int vsprintf_s(char *dst, size_t size, const char *fmt, va_list ap) {
		return vsnprintf(dst, size, fmt, ap);
	}
	static inline int strcpy_s(char *dst, size_t size, const char *src) {
		if (!dst || !src || size == 0) return 22; // EINVAL
		strncpy(dst, src, size - 1);
		dst[size - 1] = '\0';
		return 0;
	}
	// MSVC's strcpy_s also has a template overload that deduces destination size
	// from a fixed-size char array — match it so call sites using the 2-arg form
	// (strcpy_s(arr, src)) still compile.
	template <size_t N> static inline int strcpy_s(char (&dst)[N], const char *src) {
		return strcpy_s(dst, N, src);
	}
	static inline int strncpy_s(char *dst, size_t size, const char *src, size_t count) {
		if (!dst || !src || size == 0) return 22;
		size_t n = (count < size - 1) ? count : (size - 1);
		strncpy(dst, src, n);
		dst[n] = '\0';
		return 0;
	}
	// MSVC's gmtime_s/localtime_s take (struct tm*, time_t*); POSIX gmtime_r
	// takes them in the opposite order. Wrap to match the MSVC signature.
	#include <time.h>
	static inline int gmtime_s(struct tm *out, const time_t *in) {
		if (!out || !in) return 22;
		return gmtime_r(in, out) ? 0 : 22;
	}
	static inline int localtime_s(struct tm *out, const time_t *in) {
		if (!out || !in) return 22;
		return localtime_r(in, out) ? 0 : 22;
	}

	static inline int strcat_s(char *dst, size_t size, const char *src) {
		if (!dst || !src || size == 0) return 22;
		size_t cur = strnlen(dst, size);
		if (cur >= size) return 34; // ERANGE
		strncpy(dst + cur, src, size - cur - 1);
		dst[size - 1] = '\0';
		return 0;
	}

	// Phase M: GetTickCount() retired in favor of Net7TickMs()
	// (see <net7/Ticks.h>, included above). Sleep() retired in favor of
	// usleep() — call sites multiply ms × 1000.
	unsigned long GetCurrentDirectory(unsigned long size, char *path);
	int SetCurrentDirectory(const char *path);
	bool DeleteFile(const char *filename);

	// Win32 GetLastError: keep as an errno passthrough. CloseHandle, the
	// WAIT_*/ERROR_* macros for handle objects, and INFINITE were retired
	// in Phase M — see the typedef-block comment above.
	static inline unsigned long GetLastError() { return (unsigned long)errno; }

	// Phase O+ deleted the vendored OpenSSL 1.0 header tree, so the
	// WPARAM/LPARAM/WAIT_TIMEOUT compat that used to live here for the
	// RAND_event(UINT, WPARAM, LPARAM) declaration in the bundled
	// opensslconf.h-on-Linux mess is also gone. server/src has no Linux-
	// active call sites for those symbols any more.

	// fopen_s / memcpy_s / sscanf_s — MSVC bounds-checked variants
	#include <stdio.h>
	#include <string.h>
	static inline int fopen_s(FILE **pFile, const char *filename, const char *mode) {
		if (!pFile) return 22;
		*pFile = fopen(filename, mode);
		return *pFile ? 0 : errno;
	}
	static inline int memcpy_s(void *dst, size_t dstSize, const void *src, size_t srcSize) {
		if (!dst || !src) return 22;
		if (srcSize > dstSize) return 34;
		memcpy(dst, src, srcSize);
		return 0;
	}
	#define sscanf_s sscanf
	#define _strnicmp strncasecmp

	// Phase M Wave 3: TCHAR / TEXT / _TEXT / _T retired. Zero Linux-
	// active hits — the only references were inside the xmlParser
	// _XMLWINDOWS branch (dead on Linux) and an HTML doc file.

	// Threading: just include <pthread.h>. The Win32 _beginthreadex,
	// TerminateProcess, CreateMutex, CreateMailslot, CreateEvent,
	// SetEvent, ResetEvent, ResumeThread, CreateFile, ReadFile, WriteFile,
	// PROCESS_INFORMATION, STARTUPINFO, OVERLAPPED, LPSECURITY_ATTRIBUTES,
	// GENERIC_*/FILE_SHARE_*/OPEN_*, MAILSLOT_*, etc. stubs were all
	// retired in Phase M — every call site is now either rewritten over
	// POSIX (pthread_create, AF_UNIX SOCK_DGRAM, std::condition_variable)
	// or walled in `#ifdef WIN32` and never reached on Linux.
	#include <pthread.h>

	// SYSTEMTIME is the only Win32 time struct still referenced
	// unconditionally on Linux (ServerManager.cpp::OpenLogFile).
	typedef struct { unsigned short wYear, wMonth, wDayOfWeek, wDay, wHour, wMinute, wSecond, wMilliseconds; } SYSTEMTIME;

	// socket / select compat
	#define SD_BOTH SHUT_RDWR
	// FIONBIO is in <sys/ioctl.h>. ioctlsocket -> ioctl on a fd.
	static inline int ioctlsocket(int sock, long cmd, unsigned long *argp) {
		return ::ioctl(sock, cmd, argp);
	}

	// MAXINT/_mkdir/lstrlen/_access — old MSVC names.
	#include <sys/stat.h>
	#include <sys/types.h>
	#include <unistd.h> // access()
	#define MAXINT 0x7FFFFFFF
	#define _mkdir(p) mkdir((p), 0755)
	#define lstrlen strlen
	#define _access access

	static inline void GetSystemTime(SYSTEMTIME *st) {
		if (!st) return;
		struct timespec ts;
		clock_gettime(CLOCK_REALTIME, &ts);
		struct tm utc;
		gmtime_r(&ts.tv_sec, &utc);
		st->wYear = (unsigned short)(utc.tm_year + 1900);
		st->wMonth = (unsigned short)(utc.tm_mon + 1);
		st->wDayOfWeek = (unsigned short)utc.tm_wday;
		st->wDay = (unsigned short)utc.tm_mday;
		st->wHour = (unsigned short)utc.tm_hour;
		st->wMinute = (unsigned short)utc.tm_min;
		st->wSecond = (unsigned short)utc.tm_sec;
		st->wMilliseconds = (unsigned short)(ts.tv_nsec / 1000000);
	}

	// Map MSVC SEH (__try/__except) to standard C++ try/catch. The filter
	// expression and EXCEPTION_EXECUTE_HANDLER are accepted but ignored.
	#define __try try
	#define __except(filter) catch(...)
	#define EXCEPTION_EXECUTE_HANDLER 1

	// Windows in_addr has a S_un union wrapping s_addr. Call sites that
	// use `addr.S_un.S_addr` need to be rewritten to `addr.s_addr` (POSIX);
	// done on a per-file basis since macros can't rewrite member access.

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

// Phase M vocabulary sweep: monotonic ms tick counter replaces GetTickCount().
#include <net7/Ticks.h>

#define	MAX_BUFFER					25000
//#define SSL_PORT					8891	// handles authentication - HTTPS protocol (0x22BB)

#define	RACE_TERRAN					0
#define RACE_JENQUAI				1
#define RACE_PROGEN					2

#define PROFESSION_WARRIOR			0
#define PROFESSION_TRADER		    1
#define PROFESSION_EXPLORER			2

#define TERRAN_ENFORCER				0
#define TERRAN_TRADER				1
#define TERRAN_SCOUT				2
#define JENQUAI_DEFENDER			3
#define JENQUAI_SEEKER				4
#define JENQUAI_EXPLORER			5
#define PROGEN_WARRIOR				6
#define PROGEN_PRIVATEER			7
#define PROGEN_SENTINEL				8

#define MAX_SECTOR_ID		9999

// Permissions
#define ADMIN				100
#define SDEV				90
#define DEV					80
#define HGM					70
#define DGM					60
#define GM					50
#define BETA_PLUS			40
#define BETA				30
#define HELPER				20
#define USER				0

extern char g_LogFilename[MAX_PATH];
extern char g_InternalIP[MAX_PATH];
extern char g_DomainName[MAX_PATH];
extern char g_Ticket_User[MAX_PATH];
extern char g_Ticket_Pass[MAX_PATH];
extern char g_Ticket_Host[MAX_PATH];
extern char g_Ticket_DB[MAX_PATH];
extern char g_MySQL_User[MAX_PATH];
extern char g_MySQL_Pass[MAX_PATH];
extern char g_MySQL_Host[MAX_PATH];
extern int g_DASE;
extern unsigned long g_SSL_receive_time;
extern unsigned long g_cumulative_mem;

extern char g_Galaxy_Name[MAX_PATH];

void LockMessageQueue();
void UnlockMessageQueue();
void LogMessage(const char *format, ...);
void LogDebug(char *format, ...);
void LogChatMsg(char *format, ...);
void LogMySQLMsg(char *format, ...);
void DumpBuffer(unsigned char *buffer, int length);
void DumpBufferToFile(unsigned char *buffer, int length, char *filename, bool rawData);
unsigned long GetNet7TickCount();
void RelaunchNet7SSL(); // from servermanager
void LaunchNet7SSL();
void TerminateNet7SSL();

class GMemoryHandler;
class ServerManager;
class PlayerManager;
class StringManager;
class ItemBaseManager;
class AccountManager;
class SaveManager;
class MailManager;

extern ServerManager * g_ServerMgr;
extern GMemoryHandler * g_GlobMemMgr;
extern PlayerManager * g_PlayerMgr;
extern StringManager * g_StringMgr;
extern ItemBaseManager * g_ItemBaseMgr;
extern AccountManager * g_AccountMgr;
extern SaveManager	  * g_SaveMgr;
extern MailManager	  * g_MailMgr;

extern bool g_Debug;
extern bool g_ServerShutdown;
extern bool m_ShuttingDown;
extern bool g_ResetContent;

#endif // _NET_7_H_INCLUDED_
