// server/compat/threading_shim.h
//
// Thin Win32 threading-API wrappers over pthreads. STUB-LEVEL during
// Phase B: just enough to compile, NOT to run correctly.
//
// Real implementation: deferred to Phase B continuation. The current
// CreateMutex/CreateEvent/SetEvent/ResetEvent return sentinel handles and
// log to stderr so we know they're being called at runtime. Anything that
// actually depends on event-driven coordination will deadlock or busy-spin;
// that's expected. We accept this trade-off because the goal of Phase B is
// "get a link", not "get a runnable server".
//
// _beginthreadex and WaitForSingleObject ARE real — they spawn / join a
// pthread, which is the most common use in Net-7 (a manager thread loop).

#ifndef NET7_THREADING_SHIM_H
#define NET7_THREADING_SHIM_H

#if !defined(_WIN32)

#include "win32_shim.h"

#include <pthread.h>
#include <unistd.h>
#include <cerrno>
#include <chrono>
#include <cstddef>

// --------------------------------------------------------------------------
// Thread-handle entry point signature — Win32 uses unsigned __stdcall, we
// take a more permissive `unsigned (*)(void*)`. Callers that have a
// `DWORD WINAPI` signature need a tiny adapter, or just cast.
// --------------------------------------------------------------------------
typedef unsigned (*NET7_THREAD_ENTRY)(void*);

// Opaque handle implementation. Returned through `HANDLE` (void*).
struct Net7ThreadHandle {
    pthread_t       thread;
    bool            joined;     // joined or detached
    bool            valid;
};

// _beginthreadex(security, stack_size, start, arg, init_flag, thrd_addr)
//
// Real Win32 prototype is uglier; the legacy code overwhelmingly calls it
// with NULL for security and 0 for stack/init. We accept extra args and
// ignore them.
HANDLE _beginthreadex(void* security,
                      unsigned stack_size,
                      NET7_THREAD_ENTRY start,
                      void* arg,
                      unsigned init_flag,
                      unsigned* thrd_addr);

// WaitForSingleObject(handle, timeout_ms)
//
// For a thread handle: pthread_join when timeout == INFINITE; otherwise
// timed wait via pthread_tryjoin_np polling (yes, polling — Phase B-grade).
// For a mutex/event sentinel: log + return WAIT_OBJECT_0 immediately.
DWORD WaitForSingleObject(HANDLE handle, DWORD timeout_ms);

// CloseHandle(handle)
//
// Thread handle: detach if not already joined, then free.
// Sentinel handle: free.
BOOL CloseHandle(HANDLE handle);

// --------------------------------------------------------------------------
// Synchronization primitive stubs.
//
// These return SENTINEL handles. They do NOT provide actual locking or
// signalling. Any code that relies on correctness here will be broken
// until Phase B continuation replaces them with pthread_mutex_t /
// condition-variable backed implementations.
// --------------------------------------------------------------------------
HANDLE CreateMutex(void* security, BOOL initial_owner, LPCSTR name);
HANDLE CreateEvent(void* security, BOOL manual_reset, BOOL initial_state, LPCSTR name);
BOOL   SetEvent(HANDLE event);
BOOL   ResetEvent(HANDLE event);
BOOL   ReleaseMutex(HANDLE mutex);

#endif  // !_WIN32
#endif  // NET7_THREADING_SHIM_H
