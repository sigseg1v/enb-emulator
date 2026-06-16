#pragma once
struct lua_State;
namespace enb {
namespace lua {
// Register the global `enb` table + all bindings into L.
void open(lua_State* L);
// Drop all registered callbacks (on_tick/on_skill/on_chat). Call before re-running init.lua on a
// hot-reload, otherwise every reload re-appends its on_tick handlers and they accumulate forever.
void reset_callbacks(lua_State* L);
// Run every registered enb.on_tick callback. Called from the PeekMessageA tick.
void tick(lua_State* L);
// Dispatch event-hook callbacks (called from the game-function hooks, marshalled onto the tick).
void on_skill(unsigned thisptr, unsigned arg);
void on_chat(unsigned thisptr, unsigned arg);
} // namespace lua
} // namespace enb
