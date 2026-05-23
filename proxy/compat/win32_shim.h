// server/compat/win32_shim.h
//
// Win32 -> POSIX typedef and macro shim. Header-only.
//
// PURPOSE
// -------
// The Net-7 server is 2010-vintage Windows code. It scatters DWORD, HANDLE,
// LPTSTR, Sleep(), _snprintf, stricmp, INVALID_HANDLE_VALUE, etc. through
// roughly every translation unit. Rewriting each callsite is a Phase F-or-
// later concern; for Phase B we just need the legacy code to compile.
//
// This header defines the absolute-minimum set of Windows symbols that lets
// existing code pass through gcc/clang on Linux. There is NO runtime
// equivalence beyond the trivial cases (Sleep, stricmp, etc.). Anything
// that needs actual Windows semantics (handle objects, mutex objects,
// mailslots, events) is shimmed in:
//   - threading_shim.h      (mutex/event/thread handles)
//   - mailslot_shim.h       (CreateMailslot, ReadFile/WriteFile on slots)
// and those are STUBS during Phase B — they exist to satisfy the linker,
// not to produce correct behaviour at runtime.
//
// USAGE
// -----
// Include this header BEFORE any file that uses Win32 typedefs. On a
// modern Windows toolchain we are a no-op: the entire body is guarded by
// `!defined(_WIN32)`.
//
// NEW CODE
// --------
// **Do not** include this from new code. Use POSIX directly, or
// std::thread / std::mutex / std::condition_variable, or the helpers in
// docs/02-architecture.md. This file exists as a one-way ratchet: shim
// counts only go down.

#ifndef NET7_WIN32_SHIM_H
#define NET7_WIN32_SHIM_H

#if !defined(_WIN32)

#include <cstdint>
#include <cstdio>
#include <cstring>
#include <cstdlib>
#include <unistd.h>
#include <strings.h>
#include <time.h>

// --------------------------------------------------------------------------
// Integer / pointer typedefs
// --------------------------------------------------------------------------
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
typedef void* HINSTANCE;
typedef void* HMODULE;
typedef void* HWND;

typedef uintptr_t WPARAM;
typedef intptr_t  LPARAM;

// Sockets
typedef int SOCKET;
#ifndef INVALID_SOCKET
#  define INVALID_SOCKET (-1)
#endif
#ifndef SOCKET_ERROR
#  define SOCKET_ERROR   (-1)
#endif

// --------------------------------------------------------------------------
// Bool-ish macros
// --------------------------------------------------------------------------
#ifndef TRUE
#  define TRUE  1
#endif
#ifndef FALSE
#  define FALSE 0
#endif

// --------------------------------------------------------------------------
// String / text macros (Win32 TCHAR family — ANSI only on our side)
// --------------------------------------------------------------------------
#ifndef TEXT
#  define TEXT(x) x
#endif
#ifndef _T
#  define _T(x) x
#endif

// --------------------------------------------------------------------------
// Handle sentinels
// --------------------------------------------------------------------------
#ifndef INVALID_HANDLE_VALUE
#  define INVALID_HANDLE_VALUE ((HANDLE)(intptr_t)(-1))
#endif

// --------------------------------------------------------------------------
// msvcrt-isms that POSIX names differently
// --------------------------------------------------------------------------
#define _snprintf  snprintf
#define _stricmp   strcasecmp
#define _strnicmp  strncasecmp
#define stricmp    strcasecmp
#define strnicmp   strncasecmp
#define _strdup    strdup
#define _unlink    unlink

// --------------------------------------------------------------------------
// Sleep / timing
// --------------------------------------------------------------------------
static inline void Sleep(DWORD ms) {
    usleep(static_cast<useconds_t>(ms) * 1000u);
}

// GetTickCount: milliseconds since some unspecified epoch. We use
// CLOCK_MONOTONIC; the "since boot" semantic on Linux is close enough for
// the relative-time uses in Net-7. 32-bit wraparound matches Win32.
static inline DWORD GetTickCount() {
    struct timespec ts;
    clock_gettime(CLOCK_MONOTONIC, &ts);
    uint64_t ms = static_cast<uint64_t>(ts.tv_sec) * 1000ull
                + static_cast<uint64_t>(ts.tv_nsec) / 1000000ull;
    return static_cast<DWORD>(ms & 0xFFFFFFFFu);
}

// GetTickCount64 is occasionally used.
static inline uint64_t GetTickCount64() {
    struct timespec ts;
    clock_gettime(CLOCK_MONOTONIC, &ts);
    return static_cast<uint64_t>(ts.tv_sec) * 1000ull
         + static_cast<uint64_t>(ts.tv_nsec) / 1000000ull;
}

// --------------------------------------------------------------------------
// WAIT_* result codes (used by WaitForSingleObject in threading_shim.h)
// --------------------------------------------------------------------------
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

#endif  // !_WIN32
#endif  // NET7_WIN32_SHIM_H
