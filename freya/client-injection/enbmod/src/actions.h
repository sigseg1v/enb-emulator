#pragma once
// actions.h -- triggering game actions. Two layers:
//
//  (1) Input synthesis (works today): find the game window, post key/char messages. The
//      keybind system maps keys -> ability slots, so posting the bound key fires the ability the
//      same way a player would. Fragile (focus/remaps) but real today.
//
//  (2) Generic __thiscall caller (mechanism; supply args per target): call any game member
//      function by address with a `this` pointer and up to 8 dword args. This is how you'd invoke
//      the skill-activation path (0x0060f1a0) or a gadget click handler directly -- once YOU have
//      recovered the correct this/args at runtime. We give you the call primitive; getting the
//      arguments right is on you (wrong args can corrupt state -- test on a throwaway character).

#include <cstdint>

namespace enb { namespace actions {

// ---- input synthesis ----
void* game_hwnd();                       // best-effort top-level window of this process (HWND)
bool  post_key(int vk, bool down);       // PostMessage WM_KEYDOWN/UP to the game window
bool  tap_key(int vk);                   // down + up
bool  post_char(unsigned codepoint);     // WM_CHAR (for chat input, etc.)

// ---- generic member-function call ----
// Calls fn as __thiscall: ECX = thisptr, args pushed right-to-left. Robust to cleanup-convention
// mismatch (saves/restores ESP). Returns EAX. argc must be 0..8.
uint32_t call_thiscall(uintptr_t fn, uintptr_t thisptr, const uint32_t* args, int argc);
// Same but no `this` (plain cdecl/stdcall free function).
uint32_t call_cdecl(uintptr_t fn, const uint32_t* args, int argc);

}} // namespace enb::actions
