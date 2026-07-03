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

// ---- mouse-unlock hooks -----------------------------------------------------
// The client keeps the pointer inside its window TWO ways, and unlocking needs
// both neutered:
//   1. ClipCursor(rect) -- a confinement rectangle. Modern wine honours it
//      unconditionally (wine 11.x has no DXGrab/GrabPointer registry knob to
//      override it), so we swallow it and clear any pre-existing clip.
//   2. SetCursorPos(x,y) -- the client actively re-warps the pointer back to
//      window-centre a few times a second while focused. Killing only the clip
//      leaves this recenter, so the pointer still "teleports back in". We
//      swallow it too. This inherently disables cursor-recenter camera look,
//      which is the point: a free pointer (multibox: reach the other client)
//      cannot coexist with a pointer the game keeps yanking to centre.
// Both are gated on FREYA_LOCK_MOUSE=0 (launcher "Lock Mouse To Window" OFF).
typedef BOOL(WINAPI* ClipCursor_t)(const RECT*);
static ClipCursor_t real_ClipCursor = nullptr;
static BOOL WINAPI hk_ClipCursor(const RECT*) {
    return TRUE;
}

typedef BOOL(WINAPI* SetCursorPos_t)(int, int);
static SetCursorPos_t real_SetCursorPos = nullptr;
static BOOL WINAPI hk_SetCursorPos(int, int) {
    return TRUE;
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
                // We are about to rewrite this message to WM_NULL so the game's
                // wndproc never acts on it. For a swallowed WM_KEYDOWN that ALSO
                // kills the WM_CHAR TranslateMessage would have produced from it --
                // and the Freya chat input box needs that character. So synthesize
                // the WM_CHAR ourselves (honouring the live shift/caps state via
                // GetKeyboardState) and hand it straight to the input handler. A
                // handler not in text-capture mode just ignores WM_CHAR, so this is
                // harmless for the action-bar keys freya_ui also swallows.
                if (m->message == WM_KEYDOWN && (g_input_mask & hooks::WANT_CHAR)) {
                    BYTE ks[256];
                    if (GetKeyboardState(ks)) {
                        WORD ch = 0;
                        UINT sc = ((UINT)m->lParam >> 16) & 0xff;
                        if (ToAscii((UINT)m->wParam, sc, ks, &ch, 0) == 1) {
                            unsigned c = ch & 0xff;
                            if (c >= 0x20 && c != 0x7f)
                                g_on_input(WM_CHAR, c, 0);
                        }
                    }
                }
                m->message = WM_NULL;
                m->wParam = 0;
                m->lParam = 0;
            }
        }
    }
    return r;
}

// ---- chat send-line interception (__thiscall) -------------------------------
// game::addr::ChatSend is a C++ member function, NOT cdecl: `this` in ECX, the
// raw typed chat line as the one stack arg, callee-cleaned (`ret 4`, verified at
// the call boundary -- the prologue does `mov %ecx,%ebx` and the function ends in
// `c2 04 00`). A plain `__cdecl` detour is WRONG twice over: it ends in `ret 0`,
// leaking 4 stack bytes on every chat send until ESP climbs into a bad return
// slot and the client jumps onto its own stack (the "/run" crash); and on the
// pass-through it re-enters the trampoline without ECX = the caller's `this`, so
// the original runs against a garbage object. So this is a NAKED trampoline,
// exactly like hk_Skill/hk_Chat: observe via a cdecl notify, then either swallow
// (`ret 4`) or tail-`jmp` into MinHook's trampoline with ECX + stack untouched.
//
// We give the Lua layer first refusal on the line via g_on_chat_send (called
// from notify_chat_send); if it returns true the line is consumed -- we return
// 1 and never call the real function, so no chat packet is ever built.
extern "C" {
void* real_ChatSend_tramp = nullptr;
// g_on_chat_send only inspects text + enqueues under a mutex (no Lua, no throw).
// Returns 1 to swallow the line, 0 to forward it to the real send.
int notify_chat_send(const char* line) {
    if (g_on_chat_send && line && g_on_chat_send(line))
        return 1;
    return 0;
}
}
// At naked entry: [esp]=return addr, [esp+4]=line, ECX=this. After `pushal`
// (0x20 bytes) the line arg sits at 0x24(%esp). notify_chat_send is cdecl(line).
// On swallow we restore regs, set eax=1 (a success flag the caller ignores for
// typed input) and `ret 4` to clean the one stack arg the client pushed; on
// pass-through we restore regs and tail-jmp the trampoline with ECX + stack
// exactly as the game left them, so the original does its own `ret 4`.
extern "C" __attribute__((naked)) void hk_ChatSend() {
    __asm__ __volatile__("pushal\n\t"
                         "pushl 0x24(%esp)\n\t" // line (original [esp+4])
                         "call _notify_chat_send\n\t"
                         "addl $4, %esp\n\t"
                         "testl %eax, %eax\n\t"
                         "jnz 1f\n\t"
                         "popal\n\t"
                         "jmp *_real_ChatSend_tramp\n\t"
                         "1:\n\t"
                         "popal\n\t"
                         "movl $1, %eax\n\t"
                         "ret $4\n\t");
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
    if (!thisp)
        return;
    // thisp is a live object (ECX of the call). Its +ctrl_data is the avatar
    // object the bar is painting; 0 means this is not a real, backed vitals bar.
    unsigned player = *(volatile unsigned*)(thisp + game::player::ctrl_data);
    if (player == 0)
        return;
    // The SAME EnergyBar updater repaints the self HUD bar AND every party/group
    // member bar, so a naive latch grabs whichever drew LAST -- in a group that is
    // a teammate, and the self player-card then shows the teammate's
    // hull/shield/name (the "reactor values get mixed up" multibox bug). A member
    // bar's controller is NOT distinguishable by "+ctrl_data != 0": a grouped
    // teammate's controller has a perfectly valid avatar object there. Gate on
    // IDENTITY instead: the world manager holds the local avatar's GameID at
    // M + world::player_id, and every bar controller's avatar carries its own
    // GameID at player + world::tgt_gid. Latch ONLY the controller whose avatar is
    // the local player. Before M is captured (pre-zone) or before the id is
    // populated, fall back to latching any backed controller so a solo player's
    // card still fills.
    unsigned M = world_mgr();
    if (M) {
        unsigned localgid = *(volatile unsigned*)(M + game::world::player_id);
        unsigned ctrlgid = *(volatile unsigned*)(player + game::world::tgt_gid);
        if (localgid != 0 && ctrlgid != localgid)
            return; // a teammate's member bar -- never clobber the local latch
    }
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

// ---- action-bar controller capture -----------------------------------------
// game.h::addr::ActionBarCtor (0x00610f80) is the action-bar bank CONSTRUCTOR and
// addr::ActionBarUse (0x006120e0) is the slot dispatcher; both are
// __thiscall(ECX = the bank controller). We capture the controller `this` (ECX)
// read-only from BOTH -- the constructor so the Freya HUD has the controller the
// moment a bank exists (entering space), before any interaction, so slot icons
// show immediately; the dispatcher as a re-validating refresh on every click /
// "1".."6" keypress. Either is fine: a bank's sibling is +/-0x60 and ab_banks()
// normalizes via the +0x3c bank flag, so capturing whichever bank fires last is
// enough. Capture only -- the exact pattern of hk_RpgLevels; never alters either
// call. Both forward every argument untouched via the trampoline.
static volatile unsigned g_actionbar = 0; // ECX (this) of the action-bar controller
extern "C" {
void* real_ActionBar_tramp = nullptr;
void* real_ActionBarCtor_tramp = nullptr;
void notify_actionbar(unsigned thisp) {
    g_actionbar = thisp;
}
}
extern "C" __attribute__((naked)) void hk_ActionBar() {
    __asm__ __volatile__("pushal\n\t"
                         "pushl %ecx\n\t" // this (action-bar controller)
                         "call _notify_actionbar\n\t"
                         "addl $4, %esp\n\t"
                         "popal\n\t"
                         "jmp *_real_ActionBar_tramp\n\t");
}
extern "C" __attribute__((naked)) void hk_ActionBarCtor() {
    __asm__ __volatile__("pushal\n\t"
                         "pushl %ecx\n\t" // this (bank controller, captured at ctor entry)
                         "call _notify_actionbar\n\t"
                         "addl $4, %esp\n\t"
                         "popal\n\t"
                         "jmp *_real_ActionBarCtor_tramp\n\t");
}

// ---- XpBars controller capture ---------------------------------------------
// game.h::addr::XpBarsUpdate (0x0058cb50) is the discipline XP-bar value updater:
// __fastcall(ECX = the XpBars controller). It recomputes each bar's 0..1 fill
// fraction and caches it on the bar gadget (gadget + xp::fill_frac). The
// explore/trade fractions go through a getter chain we cannot replicate from Lua,
// so we capture the controller `this` (ECX) here -- the read-only pattern of
// hk_RpgLevels -- and lua_api reads the cached fractions off it (hooks::xp_ctrl()).
// Forwards every argument untouched via the trampoline; never alters game behaviour.
static volatile unsigned g_xp_ctrl = 0; // ECX (this) of the XpBars updater
extern "C" {
void* real_XpBars_tramp = nullptr;
void notify_xp(unsigned thisp) {
    g_xp_ctrl = thisp;
}
}
extern "C" __attribute__((naked)) void hk_XpBars() {
    __asm__ __volatile__("pushal\n\t"
                         "pushl %ecx\n\t" // this (XpBars controller)
                         "call _notify_xp\n\t"
                         "addl $4, %esp\n\t"
                         "popal\n\t"
                         "jmp *_real_XpBars_tramp\n\t");
}

// ---- current-target capture (two read-only hooks in the target-frame refresh) -
// See game.h::addr::TargetEntitySet / TargetFrameRefresh for the full rationale. The
// refresh resolves the selected GameID and hands the resolved object to the radar-
// highlight setter (TargetEntitySet); we capture that object there (its hull/shield aux
// read off the properties container at object+0x88, its instance name straight off the
// container in lua_api). We also capture the targeting/HUD controller (ECX) at the
// refresh entry so lua_api can live-read the target LEVEL off the targeting subsystem
// every frame (the level arrives after the target-change repaint, so a one-shot capture
// would read -1). Both hooks are read-only and forward every argument via the trampoline.
static volatile unsigned g_target_obj = 0;  // live resolved target object (0 = none)
static volatile unsigned g_target_ctrl = 0; // live targeting/HUD controller (ECX of refresh)
extern "C" {
void* real_TargetEntitySet_tramp = nullptr;
void notify_target_entity(unsigned obj) {
    g_target_obj = obj; // 0 = de-target (clear)
}
void* real_TargetFrameRefresh_tramp = nullptr;
void notify_target_ctrl(unsigned ctrl) {
    g_target_ctrl = ctrl; // the in-space targeting controller (never 0 while in space)
}
}
extern "C" __attribute__((naked)) void hk_TargetEntitySet() {
    // __thiscall: ECX = radar manager (ignored), param_2 = resolved target object (0 to
    // clear). After pushal (esp -= 0x20): ret at 0x20(esp), param_2 at 0x24(esp).
    __asm__ __volatile__("pushal\n\t"
                         "movl 0x24(%esp), %ecx\n\t" // param_2 (resolved object) -> obj
                         "pushl %ecx\n\t"
                         "call _notify_target_entity\n\t"
                         "addl $4, %esp\n\t"
                         "popal\n\t"
                         "jmp *_real_TargetEntitySet_tramp\n\t");
}
extern "C" __attribute__((naked)) void hk_TargetFrameRefresh() {
    // __thiscall: ECX = the targeting/HUD controller. Capture it (ECX) read-only, the
    // exact pattern of hk_XpBars, and forward untouched.
    __asm__ __volatile__("pushal\n\t"
                         "pushl %ecx\n\t"
                         "call _notify_target_ctrl\n\t"
                         "addl $4, %esp\n\t"
                         "popal\n\t"
                         "jmp *_real_TargetFrameRefresh_tramp\n\t");
}

// ---- world/player manager capture (world-manager initializer) ---------------
// game.h::addr::WorldMgrInit (0x00741180) is the WORLD/PLAYER manager M's init
// routine, __fastcall(ECX = M). It runs once at world entry (zone-in). M carries the
// local player's game id (M + game::world::player_id) and the live sector-server
// Connection (M + game::world::connection) -- the two fields the Freya target-action
// letter buttons need to build and push a verb command (enb.target_action). We capture
// M (ECX) read-only -- the same shape as hk_XpBars -- and forward untouched. It does not
// depend on the server emitting anything: a world manager always exists once in space,
// so it fires every session. M is a session-stable singleton, so one capture serves the
// whole session; lua_api exposes it (enb.worldmgr()).
static volatile unsigned g_world_mgr = 0; // ECX (M) of the world-manager initializer
extern "C" {
void* real_WorldMgrInit_tramp = nullptr;
void notify_world_mgr(unsigned m) {
    g_world_mgr = m;
}
}
extern "C" __attribute__((naked)) void hk_WorldMgrInit() {
    // __fastcall: ECX = M. Preserve ECX across the cdecl notify call, then forward.
    __asm__ __volatile__("pushal\n\t"
                         "pushl %ecx\n\t" // M (this) -> notify_world_mgr(M)
                         "call _notify_world_mgr\n\t"
                         "addl $4, %esp\n\t"
                         "popal\n\t"
                         "jmp *_real_WorldMgrInit_tramp\n\t");
}

// ---- hulk/loot cargo container capture ---------------------------------------
// game.h::addr::CargoTemplateID (0x00689ca0) is the per-slot ItemTemplateID
// accessor, __thiscall(ECX = the cargo CONTAINER, arg = slot). The client calls it
// once per occupied slot every time a hulk/loot cargo grid repaints, so hooking it
// read-only and latching ECX hands us the live container pointer the moment a loot
// window is populated -- no UI-object offset walk, no vtable call (the low-risk
// capture per the cargo analysis). We only READ ECX and forward untouched; lua_api
// (enb.loot) replays this and the sibling accessors off the captured container to
// enumerate the slots. NOTE the accessor fires for ANY inventory grid (ship cargo,
// vault, ...), so lua_api re-reads the container's inventory-name (container +
// cargo::inv_name_ptr) to confirm it is the hulk cargo before trusting the rows.
static volatile unsigned g_loot_container = 0;           // ECX of the last cargo accessor call
static volatile unsigned long g_loot_container_tick = 0; // GetTickCount of that latch (0 = never)
extern "C" {
void* real_CargoTemplateID_tramp = nullptr;
void notify_loot_container(unsigned c) {
    g_loot_container = c;
    g_loot_container_tick = GetTickCount();
}
}
extern "C" __attribute__((naked)) void hk_CargoTemplateID() {
    // __thiscall: ECX = cargo container. Capture it read-only, then forward
    // (identical shape to hk_WorldMgrInit). We do NOT touch the slot arg or EAX.
    __asm__ __volatile__("pushal\n\t"
                         "pushl %ecx\n\t" // container (this) -> notify_loot_container
                         "call _notify_loot_container\n\t"
                         "addl $4, %esp\n\t"
                         "popal\n\t"
                         "jmp *_real_CargoTemplateID_tramp\n\t");
}

// ---- cockpit controller capture ---------------------------------------------
// game.h::addr::CockpitThrottle (0x0057dd20) and CockpitCommands (0x0057be50) are
// the two cockpit-widget CONSTRUCTORS (__fastcall, ECX = the controller). They run
// once when the cockpit comes up (entering space). We capture each `this` (ECX)
// read-only -- the exact pattern of hk_XpBars -- so lua_api (enb.hide_cockpit) can
// walk each controller's child gadgets and clear their visible flag, letting the
// Freya throttle/action overlay replace the stock widgets. Forwards every argument
// untouched via the trampoline; never alters the constructor's behaviour.
static volatile unsigned g_cockpit_throttle = 0; // ECX of the throttle/warp ctor
static volatile unsigned g_cockpit_cmd = 0;      // ECX of the UI-commands ctor
extern "C" {
void* real_CockpitThrottle_tramp = nullptr;
void* real_CockpitCommands_tramp = nullptr;
void notify_cockpit_throttle(unsigned thisp) {
    g_cockpit_throttle = thisp;
}
void notify_cockpit_cmd(unsigned thisp) {
    g_cockpit_cmd = thisp;
}
}
extern "C" __attribute__((naked)) void hk_CockpitThrottle() {
    __asm__ __volatile__("pushal\n\t"
                         "pushl %ecx\n\t" // this (throttle controller)
                         "call _notify_cockpit_throttle\n\t"
                         "addl $4, %esp\n\t"
                         "popal\n\t"
                         "jmp *_real_CockpitThrottle_tramp\n\t");
}
extern "C" __attribute__((naked)) void hk_CockpitCommands() {
    __asm__ __volatile__("pushal\n\t"
                         "pushl %ecx\n\t" // this (UI-commands controller)
                         "call _notify_cockpit_cmd\n\t"
                         "addl $4, %esp\n\t"
                         "popal\n\t"
                         "jmp *_real_CockpitCommands_tramp\n\t");
}

// ---- chat panel capture ----------------------------------------------------
// game.h::addr::ChatLocalLine (0x0074d990) is __thiscall(ECX = the chat PANEL).
// Every displayed line -- system, local notice, computer, warning, and network
// chat alike -- funnels through it, so capturing its `this` (ECX) read-only -- the
// exact pattern of hk_CockpitCommands -- hands us the session-stable panel object.
// From the panel the Freya chat window derives the line RING at +chat::panel_ring
// (mirrored merged scrollback) AND chat_send uses it as the `this` ChatSend needs
// (ChatSend is __thiscall and faults on a bogus `this`). One capture, both uses.
// Forwards every argument untouched via the trampoline; never alters behaviour.
static volatile unsigned g_chat_panel = 0; // ECX (this) of the chat panel
extern "C" {
void* real_ChatPanel_tramp = nullptr;
void notify_chat_panel(unsigned thisp) {
    g_chat_panel = thisp;
}
}
extern "C" __attribute__((naked)) void hk_ChatPanel() {
    __asm__ __volatile__("pushal\n\t"
                         "pushl %ecx\n\t" // this (chat panel object)
                         "call _notify_chat_panel\n\t"
                         "addl $4, %esp\n\t"
                         "popal\n\t"
                         "jmp *_real_ChatPanel_tramp\n\t");
}

// ---- chat line RING capture ------------------------------------------------
// Separate from the panel: the ring is NOT a fixed offset off the panel (the old
// +0x13c guess was wrong). game.h::addr::ChatLineAppend (0x0067d780) is the ring
// append, __thiscall(ECX = the RING object). We capture its `this` read-only -- the
// same pattern -- so the Freya chat window walks the ring (chat::ring_* layout) with
// no game calls. Panel (for send) and ring (for read) are two independent captures;
// both fire on the first line printed this session.
static volatile unsigned g_chat_ring = 0; // ECX (this) of the ring append
extern "C" {
void* real_ChatRing_tramp = nullptr;
void notify_chat_ring(unsigned thisp) {
    g_chat_ring = thisp;
}
}
extern "C" __attribute__((naked)) void hk_ChatRing() {
    __asm__ __volatile__("pushal\n\t"
                         "pushl %ecx\n\t" // this (ring object)
                         "call _notify_chat_ring\n\t"
                         "addl $4, %esp\n\t"
                         "popal\n\t"
                         "jmp *_real_ChatRing_tramp\n\t");
}

// ---- PDA / micro-menu controller capture -----------------------------------
// game.h::addr::PdaCtor (0x00693230) is the PDA panel-controller CONSTRUCTOR
// (__fastcall, ECX = the controller) and addr::PdaSwitch (0x00695780) is the
// "switch active PDA child screen" dispatcher (__thiscall, ECX = the SAME
// controller, int index). We capture the controller `this` (ECX) read-only from
// BOTH -- the ctor so the Freya top-left micro-menu has the controller the moment
// it exists (entering space), before any interaction, so the buttons work on the
// first click; the dispatcher as a re-validating refresh on every panel switch.
// Exactly the action-bar ctor+use capture pattern. Both forward every argument
// untouched via the trampoline; never alters either call. lua_api reads the
// controller off this (hooks::pda_ctrl()) and re-dispatches a panel through
// PdaSwitch (enb.pda_switch).
static volatile unsigned g_pda_ctrl = 0; // ECX (this) of the PDA controller
extern "C" {
void* real_PdaCtor_tramp = nullptr;
void* real_PdaSwitch_tramp = nullptr;
void notify_pda(unsigned thisp) {
    g_pda_ctrl = thisp;
}
}
extern "C" __attribute__((naked)) void hk_PdaCtor() {
    __asm__ __volatile__("pushal\n\t"
                         "pushl %ecx\n\t" // this (PDA controller, captured at ctor entry)
                         "call _notify_pda\n\t"
                         "addl $4, %esp\n\t"
                         "popal\n\t"
                         "jmp *_real_PdaCtor_tramp\n\t");
}
extern "C" __attribute__((naked)) void hk_PdaSwitch() {
    __asm__ __volatile__("pushal\n\t"
                         "pushl %ecx\n\t" // this (PDA controller)
                         "call _notify_pda\n\t"
                         "addl $4, %esp\n\t"
                         "popal\n\t"
                         "jmp *_real_PdaSwitch_tramp\n\t");
}

// ---- in-game screen shell capture (Options micro-menu button) ---------------
// game.h::addr::ShellApply (0x00565f80) is the screen shell's per-frame "apply
// pending screen change" pump, __fastcall(ECX = the shell controller). The
// in-game HUD updater calls it every frame, so hooking it read-only captures the
// LIVE shell `this` continuously (always fresh, never stale). lua_api reads the
// shell off this (hooks::shell_ctrl()) and requests a screen through ShellRequest
// (enb.shell_screen) -- id 1 = the in-game OPTIONS_MAIN screen, the one micro-menu
// button that is NOT a PDA child. We never alter the call; we only read ECX.
static volatile unsigned g_shell_ctrl = 0; // ECX (this) of the in-game screen shell
extern "C" {
void* real_ShellApply_tramp = nullptr;
void notify_shell(unsigned thisp) {
    g_shell_ctrl = thisp;
}
}
extern "C" __attribute__((naked)) void hk_ShellApply() {
    __asm__ __volatile__("pushal\n\t"
                         "pushl %ecx\n\t" // this (screen shell controller)
                         "call _notify_shell\n\t"
                         "addl $4, %esp\n\t"
                         "popal\n\t"
                         "jmp *_real_ShellApply_tramp\n\t");
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

    // Mouse unlock: only when the launcher explicitly asked for it. Non-fatal
    // if it fails -- the game just keeps its native pointer confinement.
    char lock[8] = {0};
    if (GetEnvironmentVariableA("FREYA_LOCK_MOUSE", lock, sizeof(lock)) && lock[0] == '0' &&
        lock[1] == '\0') {
        void* clip = (void*)GetProcAddress(u32, "ClipCursor");
        if (clip && MH_CreateHook(clip, (void*)&hk_ClipCursor, (void**)&real_ClipCursor) == MH_OK &&
            MH_EnableHook(clip) == MH_OK) {
            // Clear any confinement set before the hook landed (FreyaInject
            // starts the client suspended, so normally there is none).
            real_ClipCursor(nullptr);
            logf("mouse unlock: ClipCursor neutered (FREYA_LOCK_MOUSE=0)");
        } else {
            logf("WARNING: ClipCursor hook failed -- mouse stays locked to the window");
        }
        void* scp = (void*)GetProcAddress(u32, "SetCursorPos");
        if (scp &&
            MH_CreateHook(scp, (void*)&hk_SetCursorPos, (void**)&real_SetCursorPos) == MH_OK &&
            MH_EnableHook(scp) == MH_OK) {
            logf("mouse unlock: SetCursorPos recenter neutered (FREYA_LOCK_MOUSE=0)");
        } else {
            logf("WARNING: SetCursorPos hook failed -- pointer still teleports back to centre");
        }
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
    if (MH_CreateHook((void*)game::addr::ChatSend, (void*)&hk_ChatSend, &real_ChatSend_tramp) !=
        MH_OK) {
        logf("hook ChatSend failed");
        ok = false;
    }
    if (MH_CreateHook((void*)game::addr::RpgLevels, (void*)&hk_RpgLevels, &real_RpgLevels_tramp) !=
        MH_OK) {
        logf("hook RpgLevels failed");
        ok = false;
    }
    if (MH_CreateHook((void*)game::addr::XpBarsUpdate, (void*)&hk_XpBars, &real_XpBars_tramp) !=
        MH_OK) {
        logf("hook XpBarsUpdate failed");
        ok = false;
    }
    if (MH_CreateHook((void*)game::addr::TargetFrameRefresh, (void*)&hk_TargetFrameRefresh,
                      &real_TargetFrameRefresh_tramp) != MH_OK) {
        logf("hook TargetFrameRefresh failed");
        ok = false;
    }
    if (MH_CreateHook((void*)game::addr::TargetEntitySet, (void*)&hk_TargetEntitySet,
                      &real_TargetEntitySet_tramp) != MH_OK) {
        logf("hook TargetEntitySet failed");
        ok = false;
    }
    if (MH_CreateHook((void*)game::addr::WorldMgrInit, (void*)&hk_WorldMgrInit,
                      &real_WorldMgrInit_tramp) != MH_OK) {
        logf("hook WorldMgrInit failed");
        ok = false;
    }
    if (MH_CreateHook((void*)game::addr::CargoTemplateID, (void*)&hk_CargoTemplateID,
                      &real_CargoTemplateID_tramp) != MH_OK) {
        logf("hook CargoTemplateID failed");
        ok = false;
    }
    if (MH_CreateHook((void*)game::addr::ActionBarUse, (void*)&hk_ActionBar,
                      &real_ActionBar_tramp) != MH_OK) {
        logf("hook ActionBarUse failed");
        ok = false;
    }
    if (MH_CreateHook((void*)game::addr::ActionBarCtor, (void*)&hk_ActionBarCtor,
                      &real_ActionBarCtor_tramp) != MH_OK) {
        logf("hook ActionBarCtor failed");
        ok = false;
    }
    if (MH_CreateHook((void*)game::addr::CockpitThrottle, (void*)&hk_CockpitThrottle,
                      &real_CockpitThrottle_tramp) != MH_OK) {
        logf("hook CockpitThrottle failed");
        ok = false;
    }
    if (MH_CreateHook((void*)game::addr::CockpitCommands, (void*)&hk_CockpitCommands,
                      &real_CockpitCommands_tramp) != MH_OK) {
        logf("hook CockpitCommands failed");
        ok = false;
    }
    if (MH_CreateHook((void*)game::addr::ChatLocalLine, (void*)&hk_ChatPanel,
                      &real_ChatPanel_tramp) != MH_OK) {
        logf("hook ChatLocalLine failed");
        ok = false;
    }
    if (MH_CreateHook((void*)game::addr::ChatLineAppend, (void*)&hk_ChatRing,
                      &real_ChatRing_tramp) != MH_OK) {
        logf("hook ChatLineAppend failed");
        ok = false;
    }
    if (MH_CreateHook((void*)game::addr::PdaCtor, (void*)&hk_PdaCtor, &real_PdaCtor_tramp) !=
        MH_OK) {
        logf("hook PdaCtor failed");
        ok = false;
    }
    if (MH_CreateHook((void*)game::addr::PdaSwitch, (void*)&hk_PdaSwitch, &real_PdaSwitch_tramp) !=
        MH_OK) {
        logf("hook PdaSwitch failed");
        ok = false;
    }
    if (MH_CreateHook((void*)game::addr::ShellApply, (void*)&hk_ShellApply,
                      &real_ShellApply_tramp) != MH_OK) {
        logf("hook ShellApply failed");
        ok = false;
    }
    MH_EnableHook((void*)game::addr::SkillLifecycle);
    MH_EnableHook((void*)game::addr::ChatChannel);
    MH_EnableHook((void*)game::addr::ChatSend);
    MH_EnableHook((void*)game::addr::RpgLevels);
    MH_EnableHook((void*)game::addr::XpBarsUpdate);
    MH_EnableHook((void*)game::addr::TargetFrameRefresh);
    MH_EnableHook((void*)game::addr::TargetEntitySet);
    MH_EnableHook((void*)game::addr::WorldMgrInit);
    MH_EnableHook((void*)game::addr::CargoTemplateID);
    MH_EnableHook((void*)game::addr::ActionBarUse);
    MH_EnableHook((void*)game::addr::ActionBarCtor);
    MH_EnableHook((void*)game::addr::CockpitThrottle);
    MH_EnableHook((void*)game::addr::CockpitCommands);
    MH_EnableHook((void*)game::addr::ChatLocalLine);
    MH_EnableHook((void*)game::addr::ChatLineAppend);
    MH_EnableHook((void*)game::addr::PdaCtor);
    MH_EnableHook((void*)game::addr::PdaSwitch);
    MH_EnableHook((void*)game::addr::ShellApply);
    g_event_hooks_on = ok;
    logf("event hooks %s", ok ? "enabled" : "partially enabled");
    return ok;
}

void disable_event_hooks() {
    MH_DisableHook((void*)game::addr::SkillLifecycle);
    MH_DisableHook((void*)game::addr::ChatChannel);
    MH_DisableHook((void*)game::addr::ChatSend);
    MH_DisableHook((void*)game::addr::RpgLevels);
    MH_DisableHook((void*)game::addr::XpBarsUpdate);
    MH_DisableHook((void*)game::addr::TargetFrameRefresh);
    MH_DisableHook((void*)game::addr::TargetEntitySet);
    MH_DisableHook((void*)game::addr::WorldMgrInit);
    MH_DisableHook((void*)game::addr::CargoTemplateID);
    MH_DisableHook((void*)game::addr::ActionBarUse);
    MH_DisableHook((void*)game::addr::ActionBarCtor);
    MH_DisableHook((void*)game::addr::CockpitThrottle);
    MH_DisableHook((void*)game::addr::CockpitCommands);
    MH_DisableHook((void*)game::addr::ChatLocalLine);
    MH_DisableHook((void*)game::addr::ChatLineAppend);
    MH_DisableHook((void*)game::addr::PdaCtor);
    MH_DisableHook((void*)game::addr::PdaSwitch);
    MH_DisableHook((void*)game::addr::ShellApply);
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

// ---- front-end LoginTask capture (env-driven auto-login) --------------------
// game.h::addr::LoginRunLoop (0x00767f50) is the front-end run loop,
// __fastcall(ECX = LoginTask). It dispatches the EULA/login/charselect state
// machine every frame while we are on the pre-game screens. We capture its `this`
// (ECX) read-only -- the exact pattern of hk_WorldMgrInit, but for the front end --
// so autologin.cpp has the LoginTask pointer to drive credential submit + character
// select. Capture only; forwards every argument untouched via the trampoline.
// Installed ONLY when an auto-login env var is set (enable_login_hook), so an
// ordinary launch never gains this hook.
static volatile unsigned g_login_task = 0; // ECX (LoginTask) of the front-end run loop
extern "C" {
void* real_LoginRunLoop_tramp = nullptr;
void notify_login_task(unsigned thisp) {
    g_login_task = thisp;
}
}
extern "C" __attribute__((naked)) void hk_LoginRunLoop() {
    // __fastcall: ECX = LoginTask. Preserve ECX across the cdecl notify call, forward.
    __asm__ __volatile__("pushal\n\t"
                         "pushl %ecx\n\t" // LoginTask (this) -> notify_login_task
                         "call _notify_login_task\n\t"
                         "addl $4, %esp\n\t"
                         "popal\n\t"
                         "jmp *_real_LoginRunLoop_tramp\n\t");
}

static bool g_login_hook_on = false;
bool enable_login_hook() {
    if (g_login_hook_on)
        return true;
    MH_STATUS s = MH_CreateHook((void*)game::addr::LoginRunLoop, (void*)&hk_LoginRunLoop,
                                &real_LoginRunLoop_tramp);
    if (s != MH_OK) {
        logf("hook LoginRunLoop MH_CreateHook failed: %s", MH_StatusToString(s));
        return false;
    }
    s = MH_EnableHook((void*)game::addr::LoginRunLoop);
    if (s != MH_OK) {
        logf("enable LoginRunLoop MH_EnableHook failed: %s", MH_StatusToString(s));
        return false;
    }
    g_login_hook_on = true;
    logf("login capture hook installed on LoginRunLoop @ %p", (void*)game::addr::LoginRunLoop);
    return true;
}

unsigned login_task() {
    return g_login_task;
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
unsigned xp_ctrl() {
    return g_xp_ctrl;
}
unsigned target_obj() {
    return g_target_obj;
}
unsigned target_ctrl() {
    return g_target_ctrl;
}
unsigned world_mgr() {
    return g_world_mgr;
}
unsigned loot_container() {
    return g_loot_container;
}
unsigned long loot_container_tick() {
    return g_loot_container_tick;
}
unsigned actionbar() {
    return g_actionbar;
}
unsigned cockpit_throttle_ctrl() {
    return g_cockpit_throttle;
}
unsigned cockpit_cmd_ctrl() {
    return g_cockpit_cmd;
}
unsigned chat_panel() {
    return g_chat_panel;
}
unsigned chat_ring() {
    return g_chat_ring;
}
unsigned pda_ctrl() {
    return g_pda_ctrl;
}
unsigned shell_ctrl() {
    return g_shell_ctrl;
}

} // namespace hooks
} // namespace enb
