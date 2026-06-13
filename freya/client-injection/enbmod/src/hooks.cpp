#include "hooks.h"
#include "game.h"
#include "log.h"
#include <windows.h>
#include "MinHook.h"

namespace enb { namespace hooks {

static std::function<void()> g_tick;
static std::function<void(unsigned, unsigned)> g_on_skill, g_on_chat;
static std::function<bool(unsigned, unsigned, long)> g_on_input;
static unsigned g_input_mask = 0;
static bool g_event_hooks_on = false;

// ---- PeekMessageA tick hook -------------------------------------------------
typedef BOOL (WINAPI *PeekMessageA_t)(LPMSG, HWND, UINT, UINT, UINT);
static PeekMessageA_t real_PeekMessageA = nullptr;

// Which WANT_* class (if any) does this window message belong to? 0 = none.
static unsigned msg_class(UINT msg) {
    switch (msg) {
    case WM_KEYDOWN: case WM_KEYUP: case WM_SYSKEYDOWN: case WM_SYSKEYUP:
        return hooks::WANT_KEY;
    case WM_CHAR:
        return hooks::WANT_CHAR;
    case WM_MOUSEMOVE: case WM_MOUSEWHEEL:
    case WM_LBUTTONDOWN: case WM_LBUTTONUP: case WM_LBUTTONDBLCLK:
    case WM_RBUTTONDOWN: case WM_RBUTTONUP: case WM_RBUTTONDBLCLK:
    case WM_MBUTTONDOWN: case WM_MBUTTONUP: case WM_MBUTTONDBLCLK:
        return hooks::WANT_MOUSE;
    default:
        return 0;
    }
}

static BOOL WINAPI hk_PeekMessageA(LPMSG m, HWND h, UINT min, UINT max, UINT rm) {
    // g_tick runs Lua via lua_pcall (errors are caught Lua-side); the C++ path only enqueues,
    // so nothing here throws. (mingw 32-bit has no SEH, so we can't __try around it anyway.)
    if (g_tick) g_tick();

    BOOL got = real_PeekMessageA(m, h, min, max, rm);

    // Input interception: only on messages the game actually REMOVES from its
    // queue (PM_REMOVE). A peek that leaves the message queued will be retrieved
    // again later; swallowing it here would not stick and we'd double-dispatch.
    if (got && m && (rm & PM_REMOVE) && g_on_input && g_input_mask) {
        unsigned cls = msg_class(m->message);
        if (cls & g_input_mask) {
            // g_on_input runs Lua via lua_pcall and is documented to FAIL OPEN:
            // any error returns false (do not swallow), so a script bug cannot
            // wedge the user's input. We only act on an explicit true.
            if (g_on_input(m->message, (unsigned)m->wParam, (long)m->lParam)) {
                m->message = WM_NULL;
                m->wParam = 0;
                m->lParam = 0;
            }
        }
    }
    return got;
}

// ---- game __thiscall event hooks --------------------------------------------
// The targets are C++ member functions of UNKNOWN arity called as __thiscall: `this` in ECX,
// the remaining arguments on the stack, callee-cleaned. We must NOT need to know their signature
// just to observe them, and we must forward EVERY original argument (this + all stack dwords) to the
// real function untouched -- otherwise the game reads garbage and corrupts/crashes.
//
// A C wrapper cannot do that generically: a `__fastcall(ecx,edx)` detour that re-calls
// real(ecx,edx) drops every stack argument (verified -- the original function then reads junk).
// So each detour is a NAKED trampoline that (1) preserves all registers, (2) calls a cdecl notify
// helper with (this, first-stack-dword) for observation only, (3) restores registers, and
// (4) tail-`jmp`s into MinHook's trampoline. Because we jump (not call) with the stack and ECX
// exactly as the game left them, the real function sees its full, correct argument list and
// returns straight to the game's original caller. We read `this`+arg0 but change nothing.
//
// Symbols referenced from asm are extern "C" so their names are stable (mingw prefixes `_`):
//   real_Skill_tramp / real_Chat_tramp -- MinHook fills these with the trampoline entry pointer.
//   notify_skill / notify_chat         -- observation callbacks (cdecl).
extern "C" {
    void* real_Skill_tramp = nullptr;
    void* real_Chat_tramp  = nullptr;
    void notify_skill(unsigned thisp, unsigned arg0) { if (g_on_skill) g_on_skill(thisp, arg0); }
    void notify_chat (unsigned thisp, unsigned arg0) { if (g_on_chat)  g_on_chat (thisp, arg0); }
}

// At naked entry: [esp]=return addr, [esp+4]=arg0, ECX=this. After `pushal` (32 bytes) the return
// addr sits at 0x20(%esp) and arg0 at 0x24(%esp). We push arg0 then this for cdecl notify(this,arg0).
extern "C" __attribute__((naked)) void hk_Skill() {
    __asm__ __volatile__(
        "pushal\n\t"
        "pushl 0x24(%esp)\n\t"      // arg0 (original [esp+4])
        "pushl %ecx\n\t"            // this
        "call _notify_skill\n\t"
        "addl $8, %esp\n\t"
        "popal\n\t"
        "jmp *_real_Skill_tramp\n\t"
    );
}
extern "C" __attribute__((naked)) void hk_Chat() {
    __asm__ __volatile__(
        "pushal\n\t"
        "pushl 0x24(%esp)\n\t"
        "pushl %ecx\n\t"
        "call _notify_chat\n\t"
        "addl $8, %esp\n\t"
        "popal\n\t"
        "jmp *_real_Chat_tramp\n\t"
    );
}

bool init() {
    if (MH_Initialize() != MH_OK) { logf("MH_Initialize failed"); return false; }

    HMODULE u32 = GetModuleHandleA("user32.dll");
    void* target = (void*)GetProcAddress(u32, "PeekMessageA");
    if (!target) { logf("PeekMessageA not found"); return false; }
    if (MH_CreateHook(target, (void*)&hk_PeekMessageA, (void**)&real_PeekMessageA) != MH_OK) {
        logf("CreateHook(PeekMessageA) failed"); return false;
    }
    if (MH_EnableHook(target) != MH_OK) { logf("EnableHook(PeekMessageA) failed"); return false; }
    logf("tick hook installed on PeekMessageA");
    return true;
}

void shutdown() {
    MH_DisableHook(MH_ALL_HOOKS);
    MH_Uninitialize();
}

void set_tick(std::function<void()> cb) { g_tick = std::move(cb); }
void set_on_skill(std::function<void(unsigned, unsigned)> cb) { g_on_skill = std::move(cb); }
void set_on_chat (std::function<void(unsigned, unsigned)> cb) { g_on_chat  = std::move(cb); }
void set_on_input(std::function<bool(unsigned, unsigned, long)> cb) { g_on_input = std::move(cb); }
void set_input_mask(unsigned mask) { g_input_mask = mask; }

bool enable_event_hooks() {
    if (g_event_hooks_on) return true;
    bool ok = true;
    if (MH_CreateHook((void*)game::addr::SkillLifecycle, (void*)&hk_Skill,
                      &real_Skill_tramp) != MH_OK) { logf("hook SkillLifecycle failed"); ok = false; }
    if (MH_CreateHook((void*)game::addr::ChatChannel, (void*)&hk_Chat,
                      &real_Chat_tramp) != MH_OK) { logf("hook ChatChannel failed"); ok = false; }
    MH_EnableHook((void*)game::addr::SkillLifecycle);
    MH_EnableHook((void*)game::addr::ChatChannel);
    g_event_hooks_on = ok;
    logf("event hooks %s", ok ? "enabled" : "partially enabled");
    return ok;
}

void disable_event_hooks() {
    MH_DisableHook((void*)game::addr::SkillLifecycle);
    MH_DisableHook((void*)game::addr::ChatChannel);
    g_event_hooks_on = false;
}

}} // namespace enb::hooks
