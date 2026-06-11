#pragma once
// hooks.h -- MinHook setup + the per-frame tick.
//
// The tick is driven by hooking PeekMessageA (a Win32 API with a KNOWN, stable signature -- no
// guessing required, unlike the game's own functions). The game's message pump calls it every
// loop iteration on the game thread, so our callback runs on the right thread at frame cadence.
// That gives Lua a safe place to run without us having to correctly reverse a game function's
// calling convention just to get a heartbeat.

#include <functional>

namespace enb { namespace hooks {

// Install MinHook + the PeekMessageA tick hook. Returns false on failure (logged).
bool init();
void shutdown();

// Called once per message-pump iteration, on the game thread. Set by dllmain to pump Lua.
void set_tick(std::function<void()> cb);

// --- game-function event hooks (opt-in; off by default until offsets are trusted) ---
// These wrap the game's __thiscall functions. We only read `this` (ECX) and log/dispatch;
// we never alter behaviour. Enabling them is gated behind enb.enable_event_hooks() from Lua
// so a bad assumption can't crash the client on load.
bool enable_event_hooks();   // hooks SkillLifecycle (ability use) + ChatChannel (chat lines)
void disable_event_hooks();

// Event sinks set by the Lua layer. Args are best-effort raw pointers/values.
void set_on_skill(std::function<void(unsigned /*this*/, unsigned /*arg*/)> cb);
void set_on_chat (std::function<void(unsigned /*this*/, unsigned /*arg*/)> cb);

}} // namespace enb::hooks
