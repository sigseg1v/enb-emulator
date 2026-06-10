#pragma once
// mem.h -- raw, bounds-checked memory primitives for the injected runtime.
//
// We are in-process (inside client.exe), so reading is just a pointer deref -- BUT a wrong
// offset/pointer will fault and crash the game. Every read/write goes through a two-layer guard:
// a cheap VirtualQuery pre-check (readable()/writable()) plus a Vectored Exception Handler
// backstop (see mem.cpp) that __builtin_longjmp's out of an access violation while a guarded read
// is in flight. A bad read therefore returns a default instead of taking the client down. That
// makes offset calibration from Lua *safe to get wrong*, which is the whole point: you will be
// guessing struct offsets at runtime, and a guess must not be fatal. NOTE: this is NOT MSVC SEH --
// mingw 32-bit has none and clang's __try doesn't catch at runtime under Wine; the VEH is why.

#include <windows.h>
#include <cstdint>
#include <string>
#include <cstring>

namespace enb { namespace mem {

// ---- fault guard (vectored exception handler) -------------------------------
// mingw-w64 GCC (32-bit) has no MSVC SEH, and clang's 32-bit __try compiles but does NOT catch
// at runtime on the mingw target (verified under Wine -- it drops to WineDbg). So we install a
// Vectored Exception Handler that, ONLY while a guarded read is in flight on this thread,
// __builtin_longjmp's back to the read with a failure. This is true access-violation isolation
// (verified working under Wine) and is scoped to our own reads -- it won't swallow the game's AVs.
extern thread_local void* g_guard_jmp[16];
extern thread_local volatile int g_guard_on;
void install_guard();   // call once at startup (idempotent)

// Is [p, p+len) safe to touch? Cheap VirtualQuery-based check (committed + readable).
inline bool readable(const void* p, size_t len) {
    if (!p) return false;
    MEMORY_BASIC_INFORMATION mbi;
    const uint8_t* a = (const uint8_t*)p;
    const uint8_t* end = a + len;
    while (a < end) {
        if (!VirtualQuery(a, &mbi, sizeof mbi)) return false;
        if (mbi.State != MEM_COMMIT) return false;
        DWORD prot = mbi.Protect & 0xFF;
        bool ok = prot == PAGE_READONLY || prot == PAGE_READWRITE ||
                  prot == PAGE_EXECUTE_READ || prot == PAGE_EXECUTE_READWRITE ||
                  prot == PAGE_WRITECOPY || prot == PAGE_EXECUTE_WRITECOPY;
        if (!ok || (mbi.Protect & PAGE_GUARD)) return false;
        a = (const uint8_t*)mbi.BaseAddress + mbi.RegionSize;
    }
    return true;
}

inline bool writable(const void* p, size_t len) {
    if (!p) return false;
    MEMORY_BASIC_INFORMATION mbi;
    if (!VirtualQuery(p, &mbi, sizeof mbi)) return false;
    if (mbi.State != MEM_COMMIT) return false;
    DWORD prot = mbi.Protect & 0xFF;
    return prot == PAGE_READWRITE || prot == PAGE_EXECUTE_READWRITE ||
           prot == PAGE_WRITECOPY || prot == PAGE_EXECUTE_WRITECOPY;
}

// Guarded typed read. Returns `def` if the address isn't committed+readable (cheap pre-check)
// OR if the deref still faults (VEH backstop -- covers the TOCTOU race where the page is unmapped
// between the VirtualQuery and the read).
template <typename T>
inline T read(uintptr_t addr, T def = T{}) {
    if (!readable((const void*)addr, sizeof(T))) return def;
    if (__builtin_setjmp(g_guard_jmp)) { g_guard_on = 0; return def; }
    g_guard_on = 1;
    T v = *(volatile T*)addr;
    g_guard_on = 0;
    return v;
}

inline uint8_t  u8 (uintptr_t a) { return read<uint8_t >(a); }
inline uint16_t u16(uintptr_t a) { return read<uint16_t>(a); }
inline uint32_t u32(uintptr_t a) { return read<uint32_t>(a); }
inline int32_t  i32(uintptr_t a) { return read<int32_t >(a); }
inline float    f32(uintptr_t a) { return read<float   >(a); }
inline double   f64(uintptr_t a) { return read<double  >(a); }
inline uintptr_t ptr(uintptr_t a){ return read<uintptr_t>(a); }

// Follow a pointer chain: base, then add each offset and deref (except the last, which is added
// but not dereferenced) -- classic "pointer + offsets" chain walk. Returns final address.
inline uintptr_t chain(uintptr_t base, const int* offsets, int n) {
    uintptr_t p = base;
    for (int i = 0; i < n; ++i) {
        if (i == n - 1) return p + offsets[i];
        p = ptr(p + offsets[i]);
        if (!p) return 0;
    }
    return p;
}

// How many contiguous bytes from `addr` are committed+readable, capped at `cap`. One VirtualQuery
// per memory region crossed (not per byte), so a string scan costs ~1-2 syscalls, not `cap` of them.
inline size_t readable_run(uintptr_t addr, size_t cap) {
    MEMORY_BASIC_INFORMATION mbi;
    uintptr_t a = addr;
    size_t total = 0;
    while (total < cap) {
        if (!VirtualQuery((const void*)a, &mbi, sizeof mbi)) break;
        if (mbi.State != MEM_COMMIT) break;
        DWORD prot = mbi.Protect & 0xFF;
        bool ok = prot == PAGE_READONLY || prot == PAGE_READWRITE ||
                  prot == PAGE_EXECUTE_READ || prot == PAGE_EXECUTE_READWRITE ||
                  prot == PAGE_WRITECOPY || prot == PAGE_EXECUTE_WRITECOPY;
        if (!ok || (mbi.Protect & PAGE_GUARD)) break;
        uintptr_t region_end = (uintptr_t)mbi.BaseAddress + mbi.RegionSize;
        total += (size_t)(region_end - a);
        a = region_end;
    }
    return total < cap ? total : cap;
}

// Read a NUL-terminated narrow string with a hard cap. Validates the readable span once, then
// reads the bytes UNDER the VEH guard (so a page unmapped mid-scan returns what we had, not a crash).
inline std::string cstr(uintptr_t addr, size_t cap = 512) {
    std::string out;
    size_t run = readable_run(addr, cap);
    if (!run) return out;
    if (__builtin_setjmp(g_guard_jmp)) { g_guard_on = 0; return out; }  // fault backstop
    g_guard_on = 1;
    for (size_t i = 0; i < run; ++i) {
        char c = *(volatile char*)(addr + i);
        if (!c) break;
        out.push_back(c);
    }
    g_guard_on = 0;
    return out;
}

// Read a UTF-16 string (the game uses wide strings in many UI paths) -> UTF-8.
inline std::string wstr(uintptr_t addr, size_t cap = 512) {
    std::wstring w;
    size_t run = readable_run(addr, cap * 2) / 2;   // run is in wchars
    if (run) {
        if (__builtin_setjmp(g_guard_jmp)) { g_guard_on = 0; }
        else {
            g_guard_on = 1;
            for (size_t i = 0; i < run; ++i) {
                wchar_t c = *(volatile wchar_t*)(addr + i * 2);
                if (!c) break;
                w.push_back(c);
            }
            g_guard_on = 0;
        }
    }
    if (w.empty()) return {};
    int n = WideCharToMultiByte(CP_UTF8, 0, w.c_str(), (int)w.size(), nullptr, 0, nullptr, nullptr);
    std::string out(n, 0);
    WideCharToMultiByte(CP_UTF8, 0, w.c_str(), (int)w.size(), &out[0], n, nullptr, nullptr);
    return out;
}

template <typename T>
inline bool write(uintptr_t addr, T val) {
    if (!writable((const void*)addr, sizeof(T))) return false;
    if (__builtin_setjmp(g_guard_jmp)) { g_guard_on = 0; return false; }
    g_guard_on = 1;
    *(volatile T*)addr = val;
    g_guard_on = 0;
    return true;
}

}} // namespace enb::mem
