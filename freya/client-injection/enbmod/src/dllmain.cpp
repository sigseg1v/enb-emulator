#include <windows.h>
#include <string>
#include <atomic>

#include "log.h"
#include "hooks.h"
#include "lua_api.h"
#include "mem.h"
#include "overlay.h"

extern "C" {
#include "lua.h"
#include "lauxlib.h"
#include "lualib.h"
}

namespace {

lua_State* g_L = nullptr;
std::atomic<bool> g_ready{false};
std::string g_mod_dir;     // folder containing enbmod.dll
std::string g_init_path;   // <mod_dir>\scripts\init.lua
FILETIME    g_init_mtime{};
int         g_tick_count = 0;

// True only when the current process image is client.exe. Targeted injection
// (FreyaInject.exe) only ever loads us into client.exe, but guard anyway so an
// accidental prefix-global load (e.g. AppInit_DLLs) never spins a Lua VM and a
// Flip hook inside an unrelated process.
bool host_is_client_exe() {
    char path[MAX_PATH];
    DWORD n = GetModuleFileNameA(nullptr, path, (DWORD)sizeof(path));
    if (n == 0 || n >= sizeof(path)) return false;
    const char* base = path;
    for (const char* p = path; *p; ++p)
        if (*p == '\\' || *p == '/') base = p + 1;
    return _stricmp(base, "client.exe") == 0;
}

std::string module_dir() {
    HMODULE self = nullptr;
    GetModuleHandleExA(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS |
                       GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
                       (LPCSTR)&module_dir, &self);
    char p[MAX_PATH]; GetModuleFileNameA(self, p, MAX_PATH);
    std::string s(p); auto pos = s.find_last_of('\\');
    return pos==std::string::npos ? s : s.substr(0,pos);
}

bool file_mtime(const std::string& path, FILETIME& out) {
    WIN32_FILE_ATTRIBUTE_DATA d;
    if (!GetFileAttributesExA(path.c_str(), GetFileExInfoStandard, &d)) return false;
    out = d.ftLastWriteTime; return true;
}

void run_init_script() {
    if (!g_L) return;
    // Clear callbacks registered by the previous run first, so re-running init.lua on a hot-reload
    // replaces handlers instead of accumulating duplicates of every on_tick.
    enb::lua::reset_callbacks(g_L);
    if (luaL_dofile(g_L, g_init_path.c_str()) != LUA_OK) {
        enb::logf("init.lua error: %s", lua_tostring(g_L,-1));
        lua_pop(g_L,1);
    } else {
        enb::logf("loaded %s", g_init_path.c_str());
    }
    file_mtime(g_init_path, g_init_mtime);
}

// Runs on the GAME THREAD (from the PeekMessageA hook). Owns all g_L access post-setup.
void on_tick() {
    if (!g_ready.load()) return;
    enb::lua::tick(g_L);
    // hot-reload: poll init.lua mtime every ~120 ticks
    if (++g_tick_count >= 120) {
        g_tick_count = 0;
        FILETIME ft;
        if (file_mtime(g_init_path, ft) &&
            CompareFileTime(&ft, &g_init_mtime) != 0) {
            enb::logf("init.lua changed -> reloading");
            run_init_script();
        }
    }
}

DWORD WINAPI worker(LPVOID) {
    enb::log_init();
    enb::logf("enbmod worker start (pid build, 32-bit)");
    enb::mem::install_guard();   // VEH fault guard, before any game-memory reads

    g_mod_dir   = module_dir();
    g_init_path = g_mod_dir + "\\scripts\\init.lua";

    g_L = luaL_newstate();
    if (!g_L) { enb::logf("luaL_newstate failed"); return 1; }
    enb::lua::open(g_L);

    // Expose the staged scripts dir and put it + its lib/ on package.path so
    // require() resolves both top-level scripts and shared libs (lib/freya_hud,
    // lib/modloader, lib/json, ...). The mod loader appends each mod's own dir.
    {
        std::string scripts = g_mod_dir + "\\scripts";
        std::string lp =
            "enb.script_dir = [[" + scripts + "]]\n"
            "package.path = [[" + scripts + "\\?.lua]] .. ';' .. "
                          "[[" + scripts + "\\lib\\?.lua]] .. ';' .. package.path";
        if (luaL_dostring(g_L, lp.c_str()) != LUA_OK) lua_pop(g_L,1);
    }

    run_init_script();

    // wire the tick BEFORE installing hooks, then install. g_ready gates execution.
    enb::hooks::set_tick(on_tick);
    g_ready.store(true);
    if (!enb::hooks::init()) enb::logf("hooks::init failed -- no tick");

    // overlay needs MinHook initialized (hooks::init did MH_Initialize) before it hooks Flip.
    if (!enb::overlay::init()) enb::logf("overlay::init failed -- no drawing");

    enb::logf("enbmod ready");
    return 0;
}

} // anon

BOOL APIENTRY DllMain(HMODULE h, DWORD reason, LPVOID reserved) {
    if (reason == DLL_PROCESS_ATTACH) {
        DisableThreadLibraryCalls(h);
        // Only run inside client.exe; in any other host we load and do nothing.
        if (!host_is_client_exe()) return TRUE;
        // Do NOT do real work in loader lock -- hand off to a worker thread.
        CloseHandle(CreateThread(nullptr, 0, worker, nullptr, 0, nullptr));
    } else if (reason == DLL_PROCESS_DETACH) {
        // reserved != NULL  => the whole process is terminating. The loader is tearing everything
        // down and other threads are already dead; touching MinHook (which rewrites code pages and
        // may take locks) under the loader lock here can deadlock or crash. Only unhook on a real
        // FreeLibrary unload (reserved == NULL), where the process keeps running and cleanup matters.
        if (reserved == nullptr) {
            enb::hooks::shutdown();
            enb::overlay::shutdown();
        }
    }
    return TRUE;
}
