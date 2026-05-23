// server/compat/mailslot_shim.cpp
//
// See header. Stub-only.

#if !defined(_WIN32)

#include "mailslot_shim.h"

#include <atomic>
#include <iostream>

namespace {

std::atomic<unsigned long> g_create_calls{0};
std::atomic<unsigned long> g_write_calls{0};
std::atomic<unsigned long> g_read_calls{0};
std::atomic<unsigned long> g_getinfo_calls{0};

constexpr uintptr_t kTagMailslot = 0xDEADBEEF10;

inline HANDLE sentinel() {
    return reinterpret_cast<HANDLE>(kTagMailslot);
}

inline bool is_mailslot(HANDLE h) {
    return reinterpret_cast<uintptr_t>(h) == kTagMailslot;
}

}  // namespace

HANDLE CreateMailslotA(LPCSTR name,
                       DWORD  /*max_msg_size*/,
                       DWORD  /*read_timeout*/,
                       void*  /*security*/) {
    unsigned long n = ++g_create_calls;
    std::cerr << "TODO: mailslot_shim CreateMailslot('"
              << (name ? name : "<null>")
              << "') call #" << n << "\n";
    return sentinel();
}

BOOL WriteFile(HANDLE handle,
               const void* /*buffer*/,
               DWORD bytes_to_write,
               DWORD* bytes_written,
               void* /*overlapped*/) {
    if (!is_mailslot(handle)) {
        // Not our handle; we can't service it. Return failure so callers
        // notice during testing rather than silently corrupting state.
        return FALSE;
    }
    unsigned long n = ++g_write_calls;
    std::cerr << "TODO: mailslot_shim WriteFile " << bytes_to_write
              << " bytes, call #" << n << "\n";
    if (bytes_written) {
        *bytes_written = bytes_to_write;   // pretend full write
    }
    return TRUE;
}

BOOL ReadFile(HANDLE handle,
              void* /*buffer*/,
              DWORD /*bytes_to_read*/,
              DWORD* bytes_read,
              void* /*overlapped*/) {
    if (!is_mailslot(handle)) {
        return FALSE;
    }
    unsigned long n = ++g_read_calls;
    std::cerr << "TODO: mailslot_shim ReadFile call #" << n << "\n";
    if (bytes_read) {
        *bytes_read = 0;   // no data available
    }
    return TRUE;
}

BOOL GetMailslotInfo(HANDLE handle,
                     DWORD* max_msg_size,
                     DWORD* next_size,
                     DWORD* msg_count,
                     DWORD* read_timeout) {
    if (!is_mailslot(handle)) {
        return FALSE;
    }
    unsigned long n = ++g_getinfo_calls;
    std::cerr << "TODO: mailslot_shim GetMailslotInfo call #" << n << "\n";
    if (max_msg_size) *max_msg_size = 0;
    if (next_size)    *next_size    = 0;   // MAILSLOT_NO_MESSAGE-equivalent
    if (msg_count)    *msg_count    = 0;
    if (read_timeout) *read_timeout = 0;
    return TRUE;
}

#endif  // !_WIN32
