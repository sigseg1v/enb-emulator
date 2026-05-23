// server/compat/threading_shim.cpp
//
// See header for design notes. tl;dr — Phase B placeholder. Not correct.

#if !defined(_WIN32)

#include "threading_shim.h"

#include <atomic>
#include <chrono>
#include <iostream>
#include <thread>

namespace {

std::atomic<unsigned long> g_create_mutex_calls{0};
std::atomic<unsigned long> g_create_event_calls{0};
std::atomic<unsigned long> g_set_event_calls{0};
std::atomic<unsigned long> g_reset_event_calls{0};
std::atomic<unsigned long> g_release_mutex_calls{0};

// Sentinel value distinct from INVALID_HANDLE_VALUE.
inline HANDLE make_sentinel(uintptr_t tag) {
    return reinterpret_cast<HANDLE>(tag);
}

constexpr uintptr_t kTagMutex = 0xDEADBEEF01;
constexpr uintptr_t kTagEvent = 0xDEADBEEF02;

struct PthreadStartCtx {
    NET7_THREAD_ENTRY entry;
    void*             arg;
};

extern "C" void* net7_pthread_trampoline(void* raw) {
    PthreadStartCtx* ctx = static_cast<PthreadStartCtx*>(raw);
    NET7_THREAD_ENTRY entry = ctx->entry;
    void* arg = ctx->arg;
    delete ctx;
    unsigned rc = entry ? entry(arg) : 0u;
    return reinterpret_cast<void*>(static_cast<uintptr_t>(rc));
}

}  // namespace

HANDLE _beginthreadex(void* /*security*/,
                      unsigned /*stack_size*/,
                      NET7_THREAD_ENTRY start,
                      void* arg,
                      unsigned /*init_flag*/,
                      unsigned* thrd_addr) {
    if (!start) {
        return nullptr;
    }
    Net7ThreadHandle* h = new Net7ThreadHandle{};
    h->joined = false;
    h->valid  = false;

    PthreadStartCtx* ctx = new PthreadStartCtx{start, arg};
    int rc = pthread_create(&h->thread, nullptr, &net7_pthread_trampoline, ctx);
    if (rc != 0) {
        delete ctx;
        delete h;
        std::cerr << "threading_shim: pthread_create failed errno=" << rc << "\n";
        return nullptr;
    }
    h->valid = true;
    if (thrd_addr) {
        // Win32 returns the thread ID here. We don't have a portable
        // mapping; emit something distinct.
        *thrd_addr = static_cast<unsigned>(reinterpret_cast<uintptr_t>(h) & 0xFFFFFFFFu);
    }
    return static_cast<HANDLE>(h);
}

DWORD WaitForSingleObject(HANDLE handle, DWORD timeout_ms) {
    if (!handle || handle == INVALID_HANDLE_VALUE) {
        return WAIT_FAILED;
    }
    // Sentinel mutex/event: pretend signalled.
    uintptr_t raw = reinterpret_cast<uintptr_t>(handle);
    if (raw == kTagMutex || raw == kTagEvent) {
        return WAIT_OBJECT_0;
    }

    Net7ThreadHandle* h = static_cast<Net7ThreadHandle*>(handle);
    if (!h->valid) {
        return WAIT_FAILED;
    }

    if (timeout_ms == INFINITE) {
        int rc = pthread_join(h->thread, nullptr);
        if (rc == 0) {
            h->joined = true;
            return WAIT_OBJECT_0;
        }
        return WAIT_FAILED;
    }

    // Crude timed wait — poll pthread_tryjoin_np. Sufficient for Phase B;
    // a future revision should use pthread_timedjoin_np where available.
    auto deadline = std::chrono::steady_clock::now()
                    + std::chrono::milliseconds(timeout_ms);
    while (std::chrono::steady_clock::now() < deadline) {
#if defined(__GLIBC__)
        int rc = pthread_tryjoin_np(h->thread, nullptr);
        if (rc == 0) {
            h->joined = true;
            return WAIT_OBJECT_0;
        }
        if (rc != EBUSY) {
            return WAIT_FAILED;
        }
#endif
        std::this_thread::sleep_for(std::chrono::milliseconds(5));
    }
    return WAIT_TIMEOUT;
}

BOOL CloseHandle(HANDLE handle) {
    if (!handle || handle == INVALID_HANDLE_VALUE) {
        return FALSE;
    }
    uintptr_t raw = reinterpret_cast<uintptr_t>(handle);
    if (raw == kTagMutex || raw == kTagEvent) {
        // sentinel; nothing to free
        return TRUE;
    }
    Net7ThreadHandle* h = static_cast<Net7ThreadHandle*>(handle);
    if (h->valid && !h->joined) {
        pthread_detach(h->thread);
    }
    delete h;
    return TRUE;
}

HANDLE CreateMutex(void* /*security*/, BOOL /*initial_owner*/, LPCSTR /*name*/) {
    unsigned long n = ++g_create_mutex_calls;
    std::cerr << "TODO: threading_shim CreateMutex call #" << n << "\n";
    return make_sentinel(kTagMutex);
}

HANDLE CreateEvent(void* /*security*/, BOOL /*manual_reset*/, BOOL /*initial_state*/, LPCSTR /*name*/) {
    unsigned long n = ++g_create_event_calls;
    std::cerr << "TODO: threading_shim CreateEvent call #" << n << "\n";
    return make_sentinel(kTagEvent);
}

BOOL SetEvent(HANDLE /*event*/) {
    unsigned long n = ++g_set_event_calls;
    std::cerr << "TODO: threading_shim SetEvent call #" << n << "\n";
    return TRUE;
}

BOOL ResetEvent(HANDLE /*event*/) {
    unsigned long n = ++g_reset_event_calls;
    std::cerr << "TODO: threading_shim ResetEvent call #" << n << "\n";
    return TRUE;
}

BOOL ReleaseMutex(HANDLE /*mutex*/) {
    unsigned long n = ++g_release_mutex_calls;
    std::cerr << "TODO: threading_shim ReleaseMutex call #" << n << "\n";
    return TRUE;
}

#endif  // !_WIN32
