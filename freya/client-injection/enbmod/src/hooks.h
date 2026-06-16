#pragma once
// hooks.h -- MinHook setup + the per-frame tick.
//
// The tick is driven by hooking PeekMessageA (a Win32 API with a KNOWN, stable signature -- no
// guessing required, unlike the game's own functions). The game's message pump calls it every
// loop iteration on the game thread, so our callback runs on the right thread at frame cadence.
// That gives Lua a safe place to run without us having to correctly reverse a game function's
// calling convention just to get a heartbeat.

#include <functional>

namespace enb {
namespace hooks {

// Initialize the MinHook library only (idempotent). Call this BEFORE running any
// Lua that may install a game hook at load time, so MH_CreateHook does not fail
// with MH_ERROR_NOT_INITIALIZED. init() calls it too.
bool mh_init();

// Install MinHook + the PeekMessageA tick hook. Returns false on failure (logged).
bool init();
void shutdown();

// Called once per message-pump iteration, on the game thread. Set by dllmain to pump Lua.
void set_tick(std::function<void()> cb);

// --- input interception (Tier A "swallow") ---
// Set a handler called from the GetMessageA hook (game thread) for each message
// the game RETRIEVES (the client pump removes + dispatches via GetMessageA, not
// PeekMessageA -- see hooks.cpp). It receives (msg, wparam, lparam); returning
// true SWALLOWS the message -- the hook rewrites it to WM_NULL so the game's
// window proc never sees it. This is how a Lua HUD claims clicks landing on a
// cover panel without touching the client's widget code.
//
// The handler runs Lua via lua_pcall; on ANY error it must report "do not
// swallow" (fail-open), so a script bug can never lock the user out of input.
// A null handler (the default) swallows nothing.
void set_on_input(std::function<bool(unsigned /*msg*/, unsigned /*wparam*/, long /*lparam*/)> cb);

// Bitmask of message types a handler cares about, so the hook only pays the Lua
// round-trip for those. 0 (the default) = call for none. Mirror of the WANT_*
// flags below; set by the Lua layer when a handler registers.
void set_input_mask(unsigned mask);
enum {
    WANT_KEY = 1u << 0,   // WM_KEYDOWN / WM_KEYUP / WM_SYSKEYDOWN / WM_SYSKEYUP
    WANT_CHAR = 1u << 1,  // WM_CHAR
    WANT_MOUSE = 1u << 2, // WM_*BUTTON* / WM_MOUSEMOVE / WM_MOUSEWHEEL
};

// --- game-function event hooks (opt-in; off by default until offsets are trusted) ---
// These wrap the game's __thiscall functions. We only read `this` (ECX) and log/dispatch;
// we never alter behaviour. Enabling them is gated behind enb.enable_event_hooks() from Lua
// so a bad assumption can't crash the client on load.
bool enable_event_hooks(); // hooks SkillLifecycle (ability use) + ChatChannel (chat lines)
void disable_event_hooks();

// In-space heartbeat (opt-in, same safety gate as the event hooks). Hooks
// game::addr::EnergyBar -- the per-frame in-space vitals VALUE updater -- read
// only, and records GetTickCount() of its most recent call. last_inspace_tick()
// returns that stamp (0 = never seen). The Lua layer turns this into
// enb.inspace(): "in space" while the stamp is fresh. Zero-calibration state
// signal -- needs no game_state_addr offset.
bool enable_inspace_hook();
unsigned long last_inspace_tick();

// ECX (this) captured from the most recent vitals-updater call: the live root of
// the hull/shield/energy gadget chain. 0 until the hook has fired in space.
// Calibration tooling (autocalib) uses this instead of scanning memory.
unsigned vitals_ctrl();

// ECX (this) captured from the most recent RPG level-reader call: the RPG manager
// that holds the RPGInfo AuxData container (discipline levels). 0 until the hook
// (installed by enable_event_hooks) has fired. lua_api reads levels off this.
unsigned rpg_mgr();

// Event sinks set by the Lua layer. Args are best-effort raw pointers/values.
void set_on_skill(std::function<void(unsigned /*this*/, unsigned /*arg*/)> cb);
void set_on_chat(std::function<void(unsigned /*this*/, unsigned /*arg*/)> cb);

// Chat send-line sink. Fires on the game thread with the raw typed line BEFORE
// it becomes a chat packet. Return true to SWALLOW the line (the real send is
// skipped entirely, so nothing goes on the wire); false to let it send normally.
// Must NOT touch Lua directly (Lua lives on the tick thread) -- inspect + enqueue.
void set_on_chat_send(std::function<bool(const char* /*line*/)> cb);

} // namespace hooks
} // namespace enb
