#pragma once
struct lua_State;
namespace enb {
namespace lua {
// Register the global `enb` table + all bindings into L.
void open(lua_State* L);
// Drop all registered callbacks (on_tick/on_skill/on_chat). Call before re-running init.lua on a
// hot-reload, otherwise every reload re-appends its on_tick handlers and they accumulate forever.
void reset_callbacks(lua_State* L);
// Atomically (re)run init.lua from `path`. Compiles first (a syntax error / missing file leaves the
// CURRENT callbacks untouched), then runs the new chunk into a fresh callback set and either COMMITS
// on success or ROLLS BACK to the previous set on a runtime error -- so a failed reload never leaves
// the client with zero callbacks (a blanked custom UI). Returns true on a clean load. Use this
// instead of reset_callbacks()+dofile for both the initial load and hot-reloads.
bool run_init_atomic(lua_State* L, const char* path);
// Run every registered enb.on_tick callback. Called from the PeekMessageA tick.
void tick(lua_State* L);
// Resolve the local ship and publish its world position + orientation for the
// MVAS position feed (FreyaPosFeed.dll reads it via the FreyaEnbmodShipState
// export). Called from the PeekMessageA tick, under enbmod's fault guard.
void publish_ship_state();
// Dispatch event-hook callbacks (called from the game-function hooks, marshalled onto the tick).
void on_skill(unsigned thisptr, unsigned arg);
void on_chat(unsigned thisptr, unsigned arg);
} // namespace lua
} // namespace enb
