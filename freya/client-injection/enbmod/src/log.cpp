#include "log.h"
#include <windows.h>
#include <cstdio>
#include <cstdarg>
#include <mutex>

namespace enb {
static std::mutex g_mx;
static char g_path[MAX_PATH] = {0};

void log_init() {
    // Put enbmod.log next to the loaded module.
    HMODULE self = nullptr;
    GetModuleHandleExA(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS |
                       GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
                       (LPCSTR)&log_init, &self);
    char dir[MAX_PATH];
    GetModuleFileNameA(self, dir, MAX_PATH);
    char* slash = strrchr(dir, '\\');
    if (slash) *slash = 0;
    snprintf(g_path, MAX_PATH, "%s\\enbmod.log", dir);
    FILE* f = fopen(g_path, "w");
    if (f) { fputs("=== enbmod log ===\n", f); fclose(f); }
}

void logs(const std::string& s) {
    std::lock_guard<std::mutex> lk(g_mx);
    OutputDebugStringA(("[enbmod] " + s + "\n").c_str());
    if (g_path[0]) {
        FILE* f = fopen(g_path, "a");
        if (f) { fputs(s.c_str(), f); fputc('\n', f); fclose(f); }
    }
}

void logf(const char* fmt, ...) {
    char buf[2048];
    va_list ap; va_start(ap, fmt);
    vsnprintf(buf, sizeof buf, fmt, ap);
    va_end(ap);
    logs(buf);
}
}
