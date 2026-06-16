#include "hooks.h"
#include "game.h"
#include "log.h"
#include <windows.h>
#include "MinHook.h"

namespace enb {
namespace hooks {

static std::function<void()> g_tick;
static std::function<void(unsigned, unsigned)> g_on_skill, g_on_chat;
static std::function<bool(unsigned, unsigned, long)> g_on_input;
static std::function<bool(const char*)> g_on_chat_send;
static unsigned g_input_mask = 0;
static bool g_event_hooks_on = false;

// ---- message-pump hooks -----------------------------------------------------
// The client's pump (verified across every loop in the client) is:
//     while (PeekMessageA(&m, NULL, 0, 0, PM_NOREMOVE)) {
//         GetMessageA(&m, NULL, 0, 0);   // <-- the ACTUAL retrieval + removal
//         TranslateMessage(&m); DispatchMessageA(&m);
//     }
// So PeekMessageA is only a NON-removing poll -- it never carries PM_REMOVE, and
// the message is really pulled off the queue (and dispatched to the wndproc) by
// GetMessageA. Therefore:
//   * the per-frame TICK rides PeekMessageA (the pump calls it every frame), and
//   * INPUT interception must ride GetMessageA -- that is the only place a message
//     is removed, so it is the only place we can both observe and SWALLOW it
//     (rewriting the retrieved message to WM_NULL before Translate/Dispatch).
typedef BOOL(WINAPI* PeekMessageA_t)(LPMSG, HWND, UINT, UINT, UINT);
typedef BOOL(WINAPI* GetMessageA_t)(LPMSG, HWND, UINT, UINT);
static PeekMessageA_t real_PeekMessageA = nullptr;
static GetMessageA_t real_GetMessageA = nullptr;

// Which WANT_* class (if any) does this window message belong to? 0 = none.
static unsigned msg_class(UINT msg) {
    switch (msg) {
    case WM_KEYDOWN:
    case WM_KEYUP:
    case WM_SYSKEYDOWN:
    case WM_SYSKEYUP:
        return hooks::WANT_KEY;
    case WM_CHAR:
        return hooks::WANT_CHAR;
    case WM_MOUSEMOVE:
    case WM_MOUSEWHEEL:
    case WM_LBUTTONDOWN:
    case WM_LBUTTONUP:
    case WM_LBUTTONDBLCLK:
    case WM_RBUTTONDOWN:
    case WM_RBUTTONUP:
    case WM_RBUTTONDBLCLK:
    case WM_MBUTTONDOWN:
    case WM_MBUTTONUP:
    case WM_MBUTTONDBLCLK:
        return hooks::WANT_MOUSE;
    default:
        return 0;
    }
}

static BOOL WINAPI hk_PeekMessageA(LPMSG m, HWND h, UINT min, UINT max, UINT rm) {
    // TICK ONLY. The pump calls PeekMessageA every frame (PM_NOREMOVE poll), so
    // this is our frame heartbeat for Lua. Input is NOT handled here -- this peek
    // does not remove the message, so swallowing it would not stick (GetMessageA
    // re-reads it). g_tick runs Lua via lua_pcall (errors caught Lua-side); the
    // C++ path only enqueues, so nothing here throws. (mingw 32-bit has no SEH.)
    if (g_tick)
        g_tick();
    return real_PeekMessageA(m, h, min, max, rm);
}

static BOOL WINAPI hk_GetMessageA(LPMSG m, HWND h, UINT min, UINT max) {
    BOOL r = real_GetMessageA(m, h, min, max);
    // r == -1 is an error, 0 is WM_QUIT; only a positive return removed a real
    // message. This is THE point input is taken off the queue and about to be
    // dispatched to the wndproc, so it is where we observe and (optionally) drop
    // it: rewriting to WM_NULL makes Translate/DispatchMessage harmless no-ops.
    if (r > 0 && m && g_on_input && g_input_mask) {
        unsigned cls = msg_class(m->message);
        if (cls & g_input_mask) {
            // g_on_input runs Lua via lua_pcall and FAILS OPEN: any error returns
            // false (do not swallow), so a script bug cannot wedge the user's
            // input. We only swallow on an explicit true.
            if (g_on_input(m->message, (unsigned)m->wParam, (long)m->lParam)) {
                m->message = WM_NULL;
                m->wParam = 0;
                m->lParam = 0;
            }
        }
    }
    return r;
}

// ---- chat send-line interception (cdecl) ------------------------------------
// game::addr::ChatSend takes the raw typed chat line as its only argument, by
// cdecl (caller-cleaned `ret`, no `ret 4`), so a plain typed C detour forwards
// it correctly -- no naked trampoline needed. We give the Lua layer first refusal
// on the line via g_on_chat_send; if it returns true the line is consumed (we
// return without calling the real function, so no chat packet is ever built).
typedef int(__cdecl* ChatSend_t)(const char*);
static ChatSend_t real_ChatSend = nullptr;

static int __cdecl hk_ChatSend(const char* line) {
    // g_on_chat_send only inspects text + enqueues (no Lua, no throw). On true we
    // swallow: skip the real send and report "handled" (1). The original return
    // value is a success flag the caller does not act on for typed input.
    if (g_on_chat_send && line && g_on_chat_send(line))
        return 1;
    return real_ChatSend(line);
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
void* real_Chat_tramp = nullptr;
void notify_skill(unsigned thisp, unsigned arg0) {
    if (g_on_skill)
        g_on_skill(thisp, arg0);
}
void notify_chat(unsigned thisp, unsigned arg0) {
    if (g_on_chat)
        g_on_chat(thisp, arg0);
}
}

// ---- in-space heartbeat -----------------------------------------------------
// game.h::addr::EnergyBar (0x005dc4a0) is the vitals VALUE updater the client
// repaints EVERY FRAME while the player is in space (it pushes the live
// energy/shield/hull %s into the bars). It is NOT painted in station / on the
// front-end screens, so "was it called in the last frame?" is a zero-calibration
// "am I in space?" signal -- no game_state_addr offset required. We record the
// timestamp of the most recent call; enb.inspace() (lua_api) reports true while
// that timestamp is fresh. This hook is READ-ONLY: it observes the call and
// forwards every original argument untouched via the naked trampoline, exactly
// like the skill/chat hooks above -- it never alters the game's behaviour.
//
// EnergyBar is a __thiscall taking no stack args we care about; notify takes no
// args, so the naked thunk just preserves registers, calls notify, and tail-jmps
// into MinHook's trampoline with ECX + the stack exactly as the game left them.
static volatile unsigned long g_last_inspace_tick = 0;
static volatile unsigned g_vitals_ctrl = 0; // ECX (this) of the vitals updater
extern "C" {
void* real_InSpace_tramp = nullptr;
// thisp = ECX = the vitals-controller gadget. From the known layout this is
// the root of the live hull/shield/energy chain (see autocalib), so capturing
// it each frame gives calibration tooling a live, valid root with no scanning.
void notify_inspace(unsigned thisp) {
    g_last_inspace_tick = GetTickCount();
    g_vitals_ctrl = thisp;
}
}
extern "C" __attribute__((naked)) void hk_InSpace() {
    __asm__ __volatile__("pushal\n\t"
                         "pushl %ecx\n\t" // this (vitals controller)
                         "call _notify_inspace\n\t"
                         "addl $4, %esp\n\t"
                         "popal\n\t"
                         "jmp *_real_InSpace_tramp\n\t");
}

// ---- RPG manager capture ----------------------------------------------------
// game.h::addr::RpgLevels (0x0074bfb0) is the client's own discipline-level
// reader: __fastcall(ECX = the RPG manager), which resolves RPGInfo Combat/Trade/
// Explore off the AuxData container at manager + rpg::container_off. The
// discipline levels live on THIS manager, not the ship vitals controller, so we
// capture its `this` (ECX) here -- exactly the read-only pattern of hk_InSpace --
// and lua_api reads the levels off it (hooks::rpg_mgr()). Forwards every argument
// untouched via the trampoline; never alters game behaviour.
static volatile unsigned g_rpg_mgr = 0; // ECX (this) of the RPG level reader
extern "C" {
void* real_RpgLevels_tramp = nullptr;
void notify_rpg(unsigned thisp) {
    g_rpg_mgr = thisp;
}
}
extern "C" __attribute__((naked)) void hk_RpgLevels() {
    __asm__ __volatile__("pushal\n\t"
                         "pushl %ecx\n\t" // this (RPG manager)
                         "call _notify_rpg\n\t"
                         "addl $4, %esp\n\t"
                         "popal\n\t"
                         "jmp *_real_RpgLevels_tramp\n\t");
}

// At naked entry: [esp]=return addr, [esp+4]=arg0, ECX=this. After `pushal` (32 bytes) the return
// addr sits at 0x20(%esp) and arg0 at 0x24(%esp). We push arg0 then this for cdecl notify(this,arg0).
extern "C" __attribute__((naked)) void hk_Skill() {
    __asm__ __volatile__("pushal\n\t"
                         "pushl 0x24(%esp)\n\t" // arg0 (original [esp+4])
                         "pushl %ecx\n\t"       // this
                         "call _notify_skill\n\t"
                         "addl $8, %esp\n\t"
                         "popal\n\t"
                         "jmp *_real_Skill_tramp\n\t");
}
extern "C" __attribute__((naked)) void hk_Chat() {
    __asm__ __volatile__("pushal\n\t"
                         "pushl 0x24(%esp)\n\t"
                         "pushl %ecx\n\t"
                         "call _notify_chat\n\t"
                         "addl $8, %esp\n\t"
                         "popal\n\t"
                         "jmp *_real_Chat_tramp\n\t");
}

bool mh_init() {
    MH_STATUS s = MH_Initialize();
    // Idempotent: dllmain calls this BEFORE running init.lua so a script that
    // enables a game hook at load time (enable_event_hooks / enable_inspace_hook)
    // finds MinHook ready. init() below may call it again -- ALREADY_INITIALIZED
    // is success, not an error.
    if (s != MH_OK && s != MH_ERROR_ALREADY_INITIALIZED) {
        logf("MH_Initialize failed: %s", MH_StatusToString(s));
        return false;
    }
    return true;
}

bool init() {
    if (!mh_init())
        return false;

    HMODULE u32 = GetModuleHandleA("user32.dll");
    void* peek = (void*)GetProcAddress(u32, "PeekMessageA");
    if (!peek) {
        logf("PeekMessageA not found");
        return false;
    }
    if (MH_CreateHook(peek, (void*)&hk_PeekMessageA, (void**)&real_PeekMessageA) != MH_OK) {
        logf("CreateHook(PeekMessageA) failed");
        return false;
    }
    if (MH_EnableHook(peek) != MH_OK) {
        logf("EnableHook(PeekMessageA) failed");
        return false;
    }
    logf("tick hook installed on PeekMessageA");

    // Input rides GetMessageA (see the pump note above). Non-fatal if it fails:
    // the tick/HUD still work, only input interception is lost.
    void* getm = (void*)GetProcAddress(u32, "GetMessageA");
    if (getm && MH_CreateHook(getm, (void*)&hk_GetMessageA, (void**)&real_GetMessageA) == MH_OK &&
        MH_EnableHook(getm) == MH_OK) {
        logf("input hook installed on GetMessageA");
    } else {
        logf("WARNING: GetMessageA hook failed -- HUD input (clicks/key highlight) disabled");
    }
    return true;
}

void shutdown() {
    MH_DisableHook(MH_ALL_HOOKS);
    MH_Uninitialize();
}

void set_tick(std::function<void()> cb) {
    g_tick = std::move(cb);
}
void set_on_skill(std::function<void(unsigned, unsigned)> cb) {
    g_on_skill = std::move(cb);
}
void set_on_chat(std::function<void(unsigned, unsigned)> cb) {
    g_on_chat = std::move(cb);
}
void set_on_input(std::function<bool(unsigned, unsigned, long)> cb) {
    g_on_input = std::move(cb);
}
void set_on_chat_send(std::function<bool(const char*)> cb) {
    g_on_chat_send = std::move(cb);
}
void set_input_mask(unsigned mask) {
    g_input_mask = mask;
}

bool enable_event_hooks() {
    if (g_event_hooks_on)
        return true;
    bool ok = true;
    if (MH_CreateHook((void*)game::addr::SkillLifecycle, (void*)&hk_Skill, &real_Skill_tramp) !=
        MH_OK) {
        logf("hook SkillLifecycle failed");
        ok = false;
    }
    if (MH_CreateHook((void*)game::addr::ChatChannel, (void*)&hk_Chat, &real_Chat_tramp) != MH_OK) {
        logf("hook ChatChannel failed");
        ok = false;
    }
    if (MH_CreateHook((void*)game::addr::ChatSend, (void*)&hk_ChatSend, (void**)&real_ChatSend) !=
        MH_OK) {
        logf("hook ChatSend failed");
        ok = false;
    }
    if (MH_CreateHook((void*)game::addr::RpgLevels, (void*)&hk_RpgLevels, &real_RpgLevels_tramp) !=
        MH_OK) {
        logf("hook RpgLevels failed");
        ok = false;
    }
    MH_EnableHook((void*)game::addr::SkillLifecycle);
    MH_EnableHook((void*)game::addr::ChatChannel);
    MH_EnableHook((void*)game::addr::ChatSend);
    MH_EnableHook((void*)game::addr::RpgLevels);
    g_event_hooks_on = ok;
    logf("event hooks %s", ok ? "enabled" : "partially enabled");
    return ok;
}

void disable_event_hooks() {
    MH_DisableHook((void*)game::addr::SkillLifecycle);
    MH_DisableHook((void*)game::addr::ChatChannel);
    MH_DisableHook((void*)game::addr::ChatSend);
    MH_DisableHook((void*)game::addr::RpgLevels);
    g_event_hooks_on = false;
}

static bool g_inspace_hook_on = false;
bool enable_inspace_hook() {
    if (g_inspace_hook_on)
        return true;
    MH_STATUS s =
        MH_CreateHook((void*)game::addr::EnergyBar, (void*)&hk_InSpace, &real_InSpace_tramp);
    if (s != MH_OK) {
        logf("hook EnergyBar (inspace heartbeat) MH_CreateHook failed: %s", MH_StatusToString(s));
        return false;
    }
    s = MH_EnableHook((void*)game::addr::EnergyBar);
    if (s != MH_OK) {
        logf("enable EnergyBar (inspace heartbeat) MH_EnableHook failed: %s", MH_StatusToString(s));
        return false;
    }
    g_inspace_hook_on = true;
    logf("inspace heartbeat hook installed on EnergyBar @ %p", (void*)game::addr::EnergyBar);
    return true;
}

unsigned long last_inspace_tick() {
    return g_last_inspace_tick;
}
unsigned vitals_ctrl() {
    return g_vitals_ctrl;
}
unsigned rpg_mgr() {
    return g_rpg_mgr;
}

} // namespace hooks
} // namespace enb
