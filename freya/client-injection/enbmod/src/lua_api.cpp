#include "lua_api.h"
#include "mem.h"
#include "game.h"
#include "hooks.h"
#include "log.h"
#include "overlay.h"
#include "actions.h"

#include <vector>
#include <deque>
#include <mutex>
#include <atomic>
#include <string>
#include <cstring>
#include <cstdio>
#include <windows.h>

extern "C" {
#include "lua.h"
#include "lauxlib.h"
#include "lualib.h"
}

namespace enb {
namespace lua {

using namespace enb::game;

// Diagnostic: monotonic count of tick() calls, clocked off the PeekMessageA pump
// (so this tracks INPUT rate -- a mouse-move flood spins it up). Compare to
// g_rebuild_n (display-list rebuilds, now gated to the draw rate) and
// overlay::present_count() (draw rate): after the once-per-present gate the
// rebuild count tracks Present and is independent of how fast the pump spins.
static std::atomic<unsigned long> g_tick_n{0};
static std::atomic<unsigned long> g_rebuild_n{0};

// Registered Lua callbacks, held as registry refs.
static std::vector<int> g_tick_refs;
static int g_skill_ref = LUA_NOREF;
static int g_chat_ref = LUA_NOREF;
// on_input is multi-handler (like on_tick): several mods register their own input
// handler. Each carries the want-mask it asked for; the C++ input hook is armed with
// the UNION of all masks. run_input calls them in registration order and STOPS at the
// first that returns truthy (that one swallowed the message), so an earlier handler can
// claim a message before a later one sees it. A handler that wants to defer to another
// (e.g. the action bar yielding while the chat input box is open) just returns false.
struct InputCb {
    int ref;
    unsigned mask;
};
static std::vector<InputCb> g_input_cbs;
// The Lua state, captured in open(). The input handler runs synchronously from
// the PeekMessageA hook (game thread, after the tick's pcall returns -- not
// re-entrant) and must return a swallow decision, so unlike skill/chat it can't
// be marshalled through the event queue.
static lua_State* g_L = nullptr;

// Events are produced inside game-function hooks; we queue and flush them on the tick so all
// Lua execution happens from one place (the tick), never re-entrantly mid-game-call.
struct Event {
    int kind;
    unsigned a, b;
}; // kind: 0 skill, 1 chat
static std::mutex g_evq_mx;
static std::deque<Event> g_evq;

// "/run <lua>" console: the chat-send hook (game thread) strips the prefix and
// pushes the code here; tick() (Lua thread) drains and executes it. Decoupled
// from g_evq because these carry a string payload, not the two-word Event.
static std::mutex g_runq_mx;
static std::vector<std::string> g_runq;

// =====================================================================================
// enb.mem.*  -- raw guarded memory access
// =====================================================================================
static int l_r_u8(lua_State* L) {
    lua_pushinteger(L, mem::u8((uintptr_t)luaL_checkinteger(L, 1)));
    return 1;
}
static int l_r_u16(lua_State* L) {
    lua_pushinteger(L, mem::u16((uintptr_t)luaL_checkinteger(L, 1)));
    return 1;
}
static int l_r_u32(lua_State* L) {
    lua_pushinteger(L, mem::u32((uintptr_t)luaL_checkinteger(L, 1)));
    return 1;
}
static int l_r_i32(lua_State* L) {
    lua_pushinteger(L, mem::i32((uintptr_t)luaL_checkinteger(L, 1)));
    return 1;
}
static int l_r_f32(lua_State* L) {
    lua_pushnumber(L, mem::f32((uintptr_t)luaL_checkinteger(L, 1)));
    return 1;
}
static int l_r_f64(lua_State* L) {
    lua_pushnumber(L, mem::f64((uintptr_t)luaL_checkinteger(L, 1)));
    return 1;
}
static int l_r_ptr(lua_State* L) {
    lua_pushinteger(L, mem::ptr((uintptr_t)luaL_checkinteger(L, 1)));
    return 1;
}

static int l_r_str(lua_State* L) {
    uintptr_t a = (uintptr_t)luaL_checkinteger(L, 1);
    size_t cap = (size_t)luaL_optinteger(L, 2, 512);
    std::string s = mem::cstr(a, cap);
    lua_pushlstring(L, s.data(), s.size());
    return 1;
}
static int l_r_wstr(lua_State* L) {
    uintptr_t a = (uintptr_t)luaL_checkinteger(L, 1);
    size_t cap = (size_t)luaL_optinteger(L, 2, 512);
    std::string s = mem::wstr(a, cap);
    lua_pushlstring(L, s.data(), s.size());
    return 1;
}
static int l_readable(lua_State* L) {
    uintptr_t a = (uintptr_t)luaL_checkinteger(L, 1);
    size_t n = (size_t)luaL_optinteger(L, 2, 1);
    lua_pushboolean(L, mem::readable((void*)a, n));
    return 1;
}
static int l_w_u32(lua_State* L) {
    uintptr_t a = (uintptr_t)luaL_checkinteger(L, 1);
    uint32_t v = (uint32_t)luaL_checkinteger(L, 2);
    lua_pushboolean(L, mem::write<uint32_t>(a, v));
    return 1;
}
static int l_w_f32(lua_State* L) {
    uintptr_t a = (uintptr_t)luaL_checkinteger(L, 1);
    float v = (float)luaL_checknumber(L, 2);
    lua_pushboolean(L, mem::write<float>(a, v));
    return 1;
}

// Pointer-chain helper: enb.mem.chain(base, off1, off2, ...) -> final address (0 on break)
static int l_chain(lua_State* L) {
    uintptr_t base = (uintptr_t)luaL_checkinteger(L, 1);
    int n = lua_gettop(L) - 1;
    std::vector<int> offs(n);
    for (int i = 0; i < n; i++)
        offs[i] = (int)luaL_checkinteger(L, i + 2);
    lua_pushinteger(L, (lua_Integer)mem::chain(base, offs.data(), n));
    return 1;
}

// =====================================================================================
// enb.calibrate{...} / enb.offsets()  -- set/get the runtime offsets table
// =====================================================================================
#define SET_INT(field)                                                                             \
    do {                                                                                           \
        lua_getfield(L, 1, #field);                                                                \
        if (!lua_isnil(L, -1))                                                                     \
            o.field = (int)luaL_checkinteger(L, -1);                                               \
        lua_pop(L, 1);                                                                             \
    } while (0)
#define SET_PTR(field)                                                                             \
    do {                                                                                           \
        lua_getfield(L, 1, #field);                                                                \
        if (!lua_isnil(L, -1))                                                                     \
            o.field = (uintptr_t)luaL_checkinteger(L, -1);                                         \
        lua_pop(L, 1);                                                                             \
    } while (0)

static int l_calibrate(lua_State* L) {
    luaL_checktype(L, 1, LUA_TTABLE);
    Offsets& o = offs();
    SET_PTR(player_ptr_addr);
    SET_INT(hull);
    SET_INT(hull_max);
    SET_INT(shield);
    SET_INT(shield_max);
    SET_INT(energy);
    SET_INT(energy_max);
    SET_INT(combat_lvl);
    SET_INT(trade_lvl);
    SET_INT(explore_lvl);
    SET_INT(combat_pct);
    SET_INT(trade_pct);
    SET_INT(explore_pct);
    SET_INT(skill_points);
    SET_INT(pos_x);
    SET_INT(pos_y);
    SET_INT(pos_z);
    SET_INT(name);
    SET_INT(name_is_ptr);
    SET_INT(name_wide);
    SET_PTR(game_state_addr);
    SET_INT(state_space);
    SET_INT(state_station);
    SET_INT(state_login);
    SET_INT(state_charsel);
    SET_INT(state_load);
    return 0;
}
#undef SET_INT
#undef SET_PTR

static int l_offsets(lua_State* L) {
    Offsets& o = offs();
    lua_newtable(L);
#define PUT(f)                                                                                     \
    lua_pushinteger(L, o.f);                                                                       \
    lua_setfield(L, -2, #f)
    PUT(player_ptr_addr);
    PUT(hull);
    PUT(hull_max);
    PUT(shield);
    PUT(shield_max);
    PUT(energy);
    PUT(energy_max);
    PUT(combat_lvl);
    PUT(trade_lvl);
    PUT(explore_lvl);
    PUT(combat_pct);
    PUT(trade_pct);
    PUT(explore_pct);
    PUT(skill_points);
    PUT(pos_x);
    PUT(pos_y);
    PUT(pos_z);
    PUT(name);
    PUT(name_is_ptr);
    PUT(name_wide);
    PUT(game_state_addr);
    PUT(state_space);
    PUT(state_station);
    PUT(state_login);
    PUT(state_charsel);
    PUT(state_load);
#undef PUT
    return 1;
}

// =====================================================================================
// High-level reads:  enb.self()  /  enb.target()
// =====================================================================================
// resolve the player object base from offsets.player_ptr_addr (an address that HOLDS the ptr)
static uintptr_t player_base() {
    Offsets& o = offs();
    if (!o.player_ptr_addr)
        return 0;
    return mem::ptr(o.player_ptr_addr);
}

// push field `field`-bytes into the object as an int; skip (leave nil) if off < 0 or base 0
static void push_int_field(lua_State* L, const char* key, uintptr_t base, int off) {
    if (!base || off < 0)
        return;
    lua_pushinteger(L, mem::i32(base + off));
    lua_setfield(L, -2, key);
}
static void push_flt_field(lua_State* L, const char* key, uintptr_t base, int off) {
    if (!base || off < 0)
        return;
    lua_pushnumber(L, mem::f32(base + off));
    lua_setfield(L, -2, key);
}

static int l_self(lua_State* L) {
    Offsets& o = offs();
    uintptr_t b = player_base();
    lua_newtable(L);
    lua_pushinteger(L, (lua_Integer)b);
    lua_setfield(L, -2, "base");
    if (!b)
        return 1; // empty-ish table with base=0 -> Lua side treats as "not calibrated"
    push_int_field(L, "hull", b, o.hull);
    push_int_field(L, "hull_max", b, o.hull_max);
    push_int_field(L, "shield", b, o.shield);
    push_int_field(L, "shield_max", b, o.shield_max);
    push_int_field(L, "energy", b, o.energy);
    push_int_field(L, "energy_max", b, o.energy_max);
    push_int_field(L, "combat_lvl", b, o.combat_lvl);
    push_int_field(L, "trade_lvl", b, o.trade_lvl);
    push_int_field(L, "explore_lvl", b, o.explore_lvl);
    push_int_field(L, "combat_pct", b, o.combat_pct);
    push_int_field(L, "trade_pct", b, o.trade_pct);
    push_int_field(L, "explore_pct", b, o.explore_pct);
    push_int_field(L, "skill_points", b, o.skill_points);
    push_flt_field(L, "x", b, o.pos_x);
    push_flt_field(L, "y", b, o.pos_y);
    push_flt_field(L, "z", b, o.pos_z);
    if (o.name >= 0) {
        uintptr_t namep = o.name_is_ptr ? mem::ptr(b + o.name) : (b + o.name);
        std::string nm = o.name_wide ? mem::wstr(namep) : mem::cstr(namep);
        lua_pushlstring(L, nm.data(), nm.size());
        lua_setfield(L, -2, "name");
    }
    return 1;
}

// enb.state() -> "space" | "station" | "login" | "charsel" | "load" | "unknown".
// Two sources, in priority order:
//   1. The calibrated game-state code (game_state_addr) -- the full state set,
//      but it is currently UNCALIBRATED (game_state_addr == 0), so it yields
//      nothing yet.
//   2. The in-space heartbeat (enable_inspace_hook) -- a zero-calibration signal
//      that positively reports "space" when the per-frame vitals updater is
//      firing. It cannot distinguish station/login/charsel from each other, so
//      it only ever upgrades "unknown" -> "space", never the reverse.
// "unknown" is the remaining pre-calibration default (HUD then shows the
// skeleton rather than hiding forever).
static int l_state(lua_State* L) {
    Offsets& o = offs();
    const char* name = "unknown";
    if (o.game_state_addr) {
        int s = mem::i32(o.game_state_addr);
        if (o.state_space >= 0 && s == o.state_space)
            name = "space";
        else if (o.state_station >= 0 && s == o.state_station)
            name = "station";
        else if (o.state_login >= 0 && s == o.state_login)
            name = "login";
        else if (o.state_charsel >= 0 && s == o.state_charsel)
            name = "charsel";
        else if (o.state_load >= 0 && s == o.state_load)
            name = "load";
    }
    // Heartbeat fallback: if the calibrated source could not name a state, but
    // the in-space vitals updater is firing, we ARE in space.
    if (!strcmp(name, "unknown")) {
        const unsigned long kFreshMs = 400;
        unsigned long last = hooks::last_inspace_tick();
        if (last != 0 && (GetTickCount() - last) <= kFreshMs)
            name = "space";
    }
    lua_pushstring(L, name);
    return 1;
}

// enb.cursor(on) -- draw our own pointer ON TOP of the overlay (the native
// cursor renders under the HUD). on=false stops drawing it.
static int l_cursor(lua_State* L) {
    bool on = lua_toboolean(L, 1);
    overlay::set_cursor(on, actions::game_hwnd());
    return 0;
}

// enb.patch_ret(addr [, pop_bytes]) -- overwrite a function's entry with a
// `ret` so it returns immediately. This is how the HUD suppresses a native
// draw routine whose pixels we replace (the in-space stat/xp/skill widgets).
//
// pop_bytes is the callee stack cleanup at that ret:
//   0 (default) -> 0xC3        (cdecl / caller-cleanup -- safe for any arg count)
//   N > 0       -> 0xC2 imm16  (stdcall/thiscall/fastcall callee-cleanup -- N is
//                               the EXACT stack-arg byte count; wrong N corrupts
//                               the stack on return)
// DANGEROUS and unverifiable from the headless harness: only call with an
// (addr, pop) pair confirmed against the real client (see the plans/29 CV
// entries). Nothing calls this unless a script explicitly opts in -- the HUD
// ships with native-widget suppression OFF. Returns true on success.
// Saved-byte registry so a patch_ret can be reverted live (enb.unpatch) without
// a relaunch -- essential when probing candidate paint functions.
static const int kPatchMax = 32;
struct PatchEnt {
    uintptr_t addr;
    unsigned char orig[3];
    int n;
};
static PatchEnt g_patch[kPatchMax];
static int g_patch_n = 0;

static bool write_code(uintptr_t addr, const unsigned char* bytes, int n) {
    DWORD old = 0;
    if (!VirtualProtect((void*)addr, (SIZE_T)n, PAGE_EXECUTE_READWRITE, &old))
        return false;
    memcpy((void*)addr, bytes, (size_t)n);
    VirtualProtect((void*)addr, (SIZE_T)n, old, &old);
    FlushInstructionCache(GetCurrentProcess(), (void*)addr, (SIZE_T)n);
    return true;
}

static int l_patch_ret(lua_State* L) {
    uintptr_t addr = (uintptr_t)luaL_checkinteger(L, 1);
    int pop = (int)luaL_optinteger(L, 2, 0);
    if (addr < kImageBase) {
        logf("patch_ret: refusing addr %p below image base", (void*)addr);
        lua_pushboolean(L, 0);
        return 1;
    }
    unsigned char bytes[3];
    int n;
    if (pop <= 0) {
        bytes[0] = 0xC3;
        n = 1;
    } else {
        bytes[0] = 0xC2;
        bytes[1] = (unsigned char)(pop & 0xFF);
        bytes[2] = (unsigned char)((pop >> 8) & 0xFF);
        n = 3;
    }
    // save originals first (so enb.unpatch can restore) unless already saved
    if (g_patch_n < kPatchMax && mem::readable((void*)addr, n)) {
        bool seen = false;
        for (int i = 0; i < g_patch_n; ++i)
            if (g_patch[i].addr == addr)
                seen = true;
        if (!seen) {
            g_patch[g_patch_n].addr = addr;
            g_patch[g_patch_n].n = n;
            memcpy(g_patch[g_patch_n].orig, (void*)addr, (size_t)n);
            ++g_patch_n;
        }
    }
    if (!write_code(addr, bytes, n)) {
        logf("patch_ret: VirtualProtect failed @ %p", (void*)addr);
        lua_pushboolean(L, 0);
        return 1;
    }
    logf("patch_ret: %p -> ret %d", (void*)addr, pop);
    lua_pushboolean(L, 1);
    return 1;
}

// enb.unpatch([addr]) -> int. Restore original bytes for one patched addr, or
// ALL of them if addr omitted. Returns how many were restored.
static int l_unpatch(lua_State* L) {
    uintptr_t want = (uintptr_t)luaL_optinteger(L, 1, 0);
    int n = 0;
    for (int i = 0; i < g_patch_n; ++i) {
        if (want && g_patch[i].addr != want)
            continue;
        if (g_patch[i].addr && write_code(g_patch[i].addr, g_patch[i].orig, g_patch[i].n)) {
            g_patch[i].addr = 0;
            ++n;
        }
    }
    // compact
    int w = 0;
    for (int i = 0; i < g_patch_n; ++i)
        if (g_patch[i].addr)
            g_patch[w++] = g_patch[i];
    g_patch_n = w;
    lua_pushinteger(L, n);
    return 1;
}

// aux helpers (defined below, after the enb.aux registrations) -- forward
// declared so l_target can read the target entity's vitals.
static uintptr_t player_entity();
static bool aux_read_f(uintptr_t entity, const char* key, double& out);
static int aux_entry_guarded(uintptr_t entity, const char* key);

// enb.target() -> { base, name, hull, hull_max, shield, shield_max, level } | nil.
// The current target is the live entity the native target frame repaints from,
// captured by the TargetFrameUpdate hook (hooks::target_obj()), refreshed on every
// target switch and 0 when nothing is selected. That entity is the same class
// enb.aux() accepts, so its hull/shield read straight off it; the game stores shield
// as a 0..1 percent (ShieldPercent) of MaxShieldPower, with no absolute current-
// shield key. The target's level is captured alongside it by the same update
// (hooks::target_level()) -- the HUD's own "Combat Level %d" source.
// enb.target_obj() -> live selected target entity pointer (0 until the hook fires
// / when nothing is targeted). Diagnostic: lets Lua walk the target entity with
// guarded raw reads (enb.read.*) without invoking the aux machinery, which faults
// on entities whose aux-container list shape differs from the player's.
static int l_target_obj(lua_State* L) {
    lua_pushinteger(L, (lua_Integer)hooks::target_obj());
    return 1;
}

static int l_target(lua_State* L) {
    uintptr_t entity = hooks::target_obj();
    if (!entity || !mem::readable((void*)entity, 4)) {
        lua_pushnil(L);
        return 1;
    }
    lua_newtable(L);
    lua_pushinteger(L, (lua_Integer)entity);
    lua_setfield(L, -2, "base");
    // name: char* at *(*(entity + 0x88) + 0x3c) (aux container -> display name)
    uintptr_t container = mem::ptr(entity + 0x88);
    if (container) {
        uintptr_t namep = mem::ptr(container + 0x3c);
        if (namep && mem::readable((void*)namep, 1)) {
            std::string nm = mem::cstr(namep);
            if (!nm.empty()) {
                lua_pushlstring(L, nm.data(), nm.size());
                lua_setfield(L, -2, "name");
            }
        }
    }
    double d;
    if (aux_read_f(entity, "HullPoints", d)) {
        lua_pushnumber(L, d);
        lua_setfield(L, -2, "hull");
    }
    if (aux_read_f(entity, "MaxHullPoints", d)) {
        lua_pushnumber(L, d);
        lua_setfield(L, -2, "hull_max");
    }
    double smax = -1.0, spct = -1.0;
    if (aux_read_f(entity, "MaxShieldPower", d)) {
        smax = d;
        lua_pushnumber(L, d);
        lua_setfield(L, -2, "shield_max");
    }
    if (aux_read_f(entity, "ShieldPercent", d))
        spct = d;
    if (smax >= 0.0 && spct >= 0.0) {
        lua_pushnumber(L, smax * spct);
        lua_setfield(L, -2, "shield");
    }
    // level: captured alongside the target by the same display update (-1 = none),
    // so it tracks the live selection exactly as the native frame's level line does.
    int lvl = hooks::target_level();
    if (lvl >= 0) {
        lua_pushinteger(L, lvl);
        lua_setfield(L, -2, "level");
    }
    return 1;
}

// enb.aux_entry(entity, key) -> raw aux entry pointer | nil. Diagnostic: runs the
// aux lookup under the fault guard and returns the entry base so Lua can inspect
// the candidate value slots (+0x70 valid, +0x84 scalar, +0x248 interpolated) and
// pin which slot a given key uses. Returns nil on miss/fault.
static int l_aux_entry(lua_State* L) {
    uintptr_t entity = (uintptr_t)luaL_checkinteger(L, 1);
    const char* key = luaL_checkstring(L, 2);
    int entry = aux_entry_guarded(entity, key);
    if (!entry)
        return 0;
    lua_pushinteger(L, (lua_Integer)(unsigned)entry);
    return 1;
}

// =====================================================================================
// callbacks: enb.on_tick / enb.on_skill / enb.on_chat / enb.enable_event_hooks
// =====================================================================================
// Release every registry ref we hold and clear the lists, so a hot-reload starts from a clean
// slate instead of stacking a second (third, …) copy of every on_tick handler.
void reset_callbacks(lua_State* L) {
    for (int ref : g_tick_refs)
        luaL_unref(L, LUA_REGISTRYINDEX, ref);
    g_tick_refs.clear();
    if (g_skill_ref != LUA_NOREF) {
        luaL_unref(L, LUA_REGISTRYINDEX, g_skill_ref);
        g_skill_ref = LUA_NOREF;
    }
    if (g_chat_ref != LUA_NOREF) {
        luaL_unref(L, LUA_REGISTRYINDEX, g_chat_ref);
        g_chat_ref = LUA_NOREF;
    }
    for (const InputCb& cb : g_input_cbs)
        luaL_unref(L, LUA_REGISTRYINDEX, cb.ref);
    g_input_cbs.clear();
    hooks::set_input_mask(0); // stop entering Lua for input until a reload re-registers
    {
        std::lock_guard<std::mutex> lk(g_evq_mx);
        g_evq.clear();
    }
    {
        std::lock_guard<std::mutex> lk(g_runq_mx);
        g_runq.clear();
    }
}

static int l_on_tick(lua_State* L) {
    luaL_checktype(L, 1, LUA_TFUNCTION);
    lua_pushvalue(L, 1);
    g_tick_refs.push_back(luaL_ref(L, LUA_REGISTRYINDEX));
    return 0;
}
static int l_on_skill(lua_State* L) {
    luaL_checktype(L, 1, LUA_TFUNCTION);
    if (g_skill_ref != LUA_NOREF)
        luaL_unref(L, LUA_REGISTRYINDEX, g_skill_ref);
    lua_pushvalue(L, 1);
    g_skill_ref = luaL_ref(L, LUA_REGISTRYINDEX);
    return 0;
}
static int l_on_chat(lua_State* L) {
    luaL_checktype(L, 1, LUA_TFUNCTION);
    if (g_chat_ref != LUA_NOREF)
        luaL_unref(L, LUA_REGISTRYINDEX, g_chat_ref);
    lua_pushvalue(L, 1);
    g_chat_ref = luaL_ref(L, LUA_REGISTRYINDEX);
    return 0;
}
static int l_enable_event_hooks(lua_State* L) {
    lua_pushboolean(L, hooks::enable_event_hooks());
    return 1;
}

// enb.enable_inspace() -- install the in-space heartbeat hook (opt-in, same
// safety gate as enable_event_hooks). Returns true on success.
static int l_enable_inspace(lua_State* L) {
    lua_pushboolean(L, hooks::enable_inspace_hook());
    return 1;
}

// enb.inspace() -> bool. True while the in-space vitals updater has fired
// recently (within the freshness window below). The updater runs every frame in
// space and not at all on the front-end / in station, so a fresh stamp means
// "in space" with zero offset calibration. Returns false if the heartbeat hook
// was never enabled (stamp stays 0).
static int l_inspace(lua_State* L) {
    // Freshness window: a few frames' grace so a single skipped paint (alt-tab,
    // a stall) doesn't flicker the HUD off. ~400ms ~= 24+ frames at 60fps.
    const unsigned long kFreshMs = 400;
    unsigned long last = hooks::last_inspace_tick();
    bool fresh = last != 0 && (GetTickCount() - last) <= kFreshMs;
    lua_pushboolean(L, fresh ? 1 : 0);
    return 1;
}

// enb.vitals_ctrl() -> int. Live pointer to the vitals-controller gadget,
// captured each frame by the in-space heartbeat hook (0 until seen in space).
// It is the root of the hull/shield/energy chain -- autocalib walks it instead
// of scanning memory.
static int l_vitals_ctrl(lua_State* L) {
    lua_pushinteger(L, (lua_Integer)hooks::vitals_ctrl());
    return 1;
}

// enb.vitals() -> { hull=frac, shield=frac, energy=frac }  (each 0..1, omitted
// if unreadable). Reads the live vitals controller (heartbeat-captured) -> each
// bar's gadget -> its fill fraction (game::vitals offsets). Independent of the
// flat-struct player_ptr_addr calibration: it works whenever the in-space vitals
// updater is firing, and the table is empty out of space (controller == 0).
static int l_vitals(lua_State* L) {
    lua_newtable(L);
    uintptr_t ctrl = hooks::vitals_ctrl();
    if (!ctrl)
        return 1;
    auto push_frac = [&](const char* key, int slot) {
        uintptr_t g = mem::ptr(ctrl + slot);
        if (!g)
            return;
        float f = mem::f32(g + game::vitals::fill_frac);
        if (f != f)
            return; // NaN guard
        if (f < 0.0f)
            f = 0.0f;
        else if (f > 1.0f)
            f = 1.0f;
        lua_pushnumber(L, f);
        lua_setfield(L, -2, key);
    };
    push_frac("hull", game::vitals::gadget_hull);
    push_frac("shield", game::vitals::gadget_shield);
    push_frac("energy", game::vitals::gadget_energy);
    // character name off the same controller's player-entity chain.
    uintptr_t data = mem::ptr(ctrl + game::player::ctrl_data);
    uintptr_t entity = data ? mem::ptr(data + game::player::data_entity) : 0;
    if (entity) {
        uintptr_t namep = mem::ptr(entity + game::player::entity_name);
        if (namep) {
            std::string nm = mem::cstr(namep);
            if (!nm.empty()) {
                lua_pushlstring(L, nm.data(), nm.size());
                lua_setfield(L, -2, "name");
            }
        }
    }
    return 1;
}

// enb.aux(key [, entity]) -> number | nil. Reads a string-keyed AuxData float
// value off the local player's ship entity (or an explicit entity address) via
// the client's own property-bag getter. This is how the NUMERIC current/max
// vitals live (HullPoints / MaxHullPoints / MaxShieldPower / MaxEnergyPower) and
// the discipline levels (RPGInfo CombatLevel / TradeLevel / ExploreLevel) -- they
// are NOT flat struct fields, so enb.mem at a fixed offset cannot find them.
// Returns nil if out of space, the entity/entry is unreadable, or the key unset.
//
// Convention is load-bearing: build_key is __thiscall (ECX = key buffer), get_value
// is __cdecl(entity, keybuf). Calling either with the wrong convention corrupts the
// stack and faults the client (the ChatLocalLine-class crash). The key buffer is a
// zeroed stack scratch object the builder fills in; it owns no heap, so no cleanup.
typedef void*(__thiscall* AuxBuildKey_t)(void* keybuf, const char* name);
typedef int(__cdecl* AuxGetValue_t)(int entity, void* keybuf);
// Resolve a string-keyed AuxData entry on `entity` to its value slot. Returns the
// address of the 4-byte value (entry+val_off) ready to read, or 0 on any miss.
// The value's type (float for vitals, int for levels) is the CALLER's choice -- the
// same slot holds both depending on the key, so the two wrappers below pick.
// The local player's ship entity (the aux getter's default subject): vitals
// controller -> data -> entity. 0 out of space.
static uintptr_t player_entity() {
    uintptr_t ctrl = hooks::vitals_ctrl();
    if (!ctrl)
        return 0;
    uintptr_t data = mem::ptr(ctrl + game::player::ctrl_data);
    return data ? mem::ptr(data + game::player::data_entity) : 0;
}

// Resolve a string-keyed AuxData entry on an explicit ENTITY to the address of its
// 4-byte value slot, or 0 on any miss. Pure C++ (no Lua stack) so callers other
// than the enb.aux wrappers (e.g. l_target) can reuse it.
// Run build_key + get_value under the VEH fault guard (mem::g_guard_*). get_value
// walks the entity's aux dictionary, and on entities whose dict is not ship-shaped
// (navs, gates, some decorations) that walk null-derefs -- which would take the
// client down. The guard __builtin_longjmp's out of any access violation raised
// while it is armed, so a bad entity yields entry=0 (no value) instead of a crash.
// Returns the raw entry pointer (0 on miss/fault); callers pick the value slot.
static int aux_entry_guarded(uintptr_t entity, const char* key) {
    if (!entity || !mem::readable((void*)entity, 4))
        return 0;
    unsigned char keybuf[game::aux::keybuf_sz];
    memset(keybuf, 0, sizeof(keybuf));
    if (__builtin_setjmp(mem::g_guard_jmp)) {
        mem::g_guard_on = 0;
        return 0; // aux lookup faulted on this entity -- treat as "no value"
    }
    mem::g_guard_on = 1;
    ((AuxBuildKey_t)game::aux::build_key)(keybuf, key);
    int entry = ((AuxGetValue_t)game::aux::get_value)((int)entity, keybuf);
    mem::g_guard_on = 0;
    return entry;
}

static uintptr_t aux_addr_core(uintptr_t entity, const char* key) {
    int entry = aux_entry_guarded(entity, key);
    if (!entry || !mem::readable((void*)(uintptr_t)entry, game::aux::val_off + 4))
        return 0;
    if (mem::i32((uintptr_t)entry + game::aux::valid_off) == 0)
        return 0; // value not set
    return (uintptr_t)entry + game::aux::val_off;
}
// Convenience typed readers off an entity (used by l_target).
static bool aux_read_f(uintptr_t entity, const char* key, double& out) {
    uintptr_t a = aux_addr_core(entity, key);
    if (!a)
        return false;
    float v = mem::f32(a);
    if (v != v)
        return false; // NaN guard
    out = v;
    return true;
}
static uintptr_t aux_value_addr(lua_State* L, int key_arg, int entity_arg) {
    const char* key = luaL_checkstring(L, key_arg);
    uintptr_t entity;
    if (lua_isnoneornil(L, entity_arg)) {
        entity = player_entity();
        if (!entity)
            return 0; // not in space
    } else {
        entity = (uintptr_t)luaL_checkinteger(L, entity_arg);
    }
    return aux_addr_core(entity, key);
}
// enb.aux(key [, entity]) -> number | nil. The value as a FLOAT. Use for the
// numeric vitals: HullPoints / MaxHullPoints / MaxShieldPower / MaxEnergyPower.
static int l_aux(lua_State* L) {
    uintptr_t a = aux_value_addr(L, 1, 2);
    if (!a)
        return 0;
    float v = mem::f32(a);
    if (v != v)
        return 0; // NaN guard
    lua_pushnumber(L, v);
    return 1;
}
// enb.aux_i(key [, entity]) -> integer | nil. The same slot as an INT. For aux
// keys whose value is an integer. NOTE: the discipline levels are NOT here -- they
// live on the RPG manager, not the ship entity, behind a different getter; use
// enb.rpg_level() for those.
static int l_aux_i(lua_State* L) {
    uintptr_t a = aux_value_addr(L, 1, 2);
    if (!a)
        return 0;
    lua_pushinteger(L, (lua_Integer)mem::i32(a));
    return 1;
}

// enb.rpg_level(key) -> integer | nil. Discipline levels off the RPG manager
// captured by the RpgLevels hook (hooks::rpg_mgr()): resolve the RPGInfo AuxData
// container at manager+container_off, then look the key up through the level
// getter (same __cdecl(obj, keybuf) shape as get_value, different value type).
// Keys: "RPGInfo CombatLevel" / "RPGInfo TradeLevel" / "RPGInfo ExploreLevel".
// nil until the RPG reader has run at least once (manager == 0) or on any miss.
// Returns 0 (not nil) on a fresh character -- a real, valid level of 0.
static int l_rpg_level(lua_State* L) {
    const char* key = luaL_checkstring(L, 1);
    uintptr_t mgr = hooks::rpg_mgr();
    if (!mgr || !mem::readable((void*)(mgr + game::rpg::container_off), 4))
        return 0;
    uintptr_t cont = mem::ptr(mgr + game::rpg::container_off);
    if (!cont || !mem::readable((void*)cont, 4))
        return 0;

    unsigned char keybuf[game::aux::keybuf_sz];
    memset(keybuf, 0, sizeof(keybuf));
    ((AuxBuildKey_t)game::aux::build_key)(keybuf, key);
    int entry = ((AuxGetValue_t)game::rpg::get_entry)((int)cont, keybuf);
    if (!entry || !mem::readable((void*)(uintptr_t)entry, game::aux::val_off + 4))
        return 0;
    if (mem::i32((uintptr_t)entry + game::aux::valid_off) == 0)
        return 0; // not set
    lua_pushinteger(L, (lua_Integer)mem::i32((uintptr_t)entry + game::aux::val_off));
    return 1;
}

// enb.rpg_mgr() -> int. Raw pointer to the RPG manager captured by the RpgLevels
// hook (hooks::rpg_mgr()). 0 = the client's level-reader has NOT run yet this
// session, so enb.rpg_level() will return nothing. Diagnostic: open the in-game
// status/avatar panel (which triggers the reader) and re-check -- a non-zero value
// means the hook fired and the levels are now readable.
static int l_rpg_mgr(lua_State* L) {
    lua_pushinteger(L, (lua_Integer)hooks::rpg_mgr());
    return 1;
}

// enb.xp_frac(which) -> number | nil. The live 0..1 XP-bar fill fraction for a
// discipline: which = "combat" | "trade" | "explore". Read off the XpBars
// controller captured by the updater hook (hooks::xp_ctrl()): the controller holds
// each bar gadget at a fixed slot, and the gadget caches its computed fill at
// gadget + xp::fill_frac (the same value the native bar paints). nil until the
// updater has run in space (controller == 0), the bar gadget is absent, or the
// bar is not live (exists flag clear). Returns 0 on a real 0% bar, not nil.
static int l_xp_frac(lua_State* L) {
    const char* which = luaL_checkstring(L, 1);
    int slot;
    if (std::strcmp(which, "combat") == 0)
        slot = game::xp::bar_combat;
    else if (std::strcmp(which, "trade") == 0)
        slot = game::xp::bar_trade;
    else if (std::strcmp(which, "explore") == 0)
        slot = game::xp::bar_explore;
    else
        return luaL_error(L, "xp_frac: which must be combat|trade|explore");

    uintptr_t ctrl = hooks::xp_ctrl();
    if (!ctrl || !mem::readable((void*)(ctrl + slot), 4))
        return 0;
    uintptr_t bar = mem::ptr(ctrl + slot);
    if (!bar || !mem::readable((void*)(bar + game::xp::fill_frac), 4))
        return 0;
    if (mem::u8(bar + game::xp::exists_flag) == 0)
        return 0; // bar not live this frame
    float v = mem::f32(bar + game::xp::fill_frac);
    if (v != v)
        return 0; // NaN guard
    if (v < 0.0f)
        v = 0.0f;
    if (v > 1.0f)
        v = 1.0f;
    lua_pushnumber(L, v);
    return 1;
}

// enb.xp_ctrl() -> int. Raw pointer to the XpBars controller captured by the
// updater hook (hooks::xp_ctrl()). 0 = the updater has not run in space yet, so
// enb.xp_frac() returns nothing. Diagnostic, mirrors enb.rpg_mgr().
static int l_xp_ctrl(lua_State* L) {
    lua_pushinteger(L, (lua_Integer)hooks::xp_ctrl());
    return 1;
}

// enb.actionbar() -> int. Raw pointer to the in-space action-bar controller captured
// by the "Use Slot" dispatch hook (hooks::actionbar()). 0 until a slot has been used
// in space (the hook only fires on a slot click / "1".."6" keypress). The Freya HUD
// reads the numbered slots off this and re-dispatches a slot through it.
static int l_actionbar(lua_State* L) {
    lua_pushinteger(L, (lua_Integer)hooks::actionbar());
    return 1;
}

// Call GadgetClass::SetVisible(visible) on one gadget via its vtable (slot at byte
// game::gadget::vt_set_visible, __thiscall(this, BOOL)). This is the engine's own
// hide/show: it flips the visible bit AND releases the rendered child (gadget+0x20)
// / focus refs. Poking the bit by hand leaves the rendered child live and corrupts
// layout, so we always go through the method. Fully pointer-guarded before the
// indirect call: a bad gadget/vtable/fn pointer is a no-op (returns false), never a
// fault. Returns true if the call was dispatched.
static bool gadget_set_visible(uintptr_t g, unsigned visible) {
    if (!g || !mem::readable((void*)g, 4))
        return false;
    uintptr_t vt = mem::ptr(g);
    if (!vt || !mem::readable((void*)(vt + game::gadget::vt_set_visible), 4))
        return false;
    uintptr_t fn = mem::ptr(vt + game::gadget::vt_set_visible);
    if (!fn || !mem::readable((void*)fn, 1))
        return false;
    uint32_t arg = visible ? 1u : 0u;
    actions::call_thiscall(fn, g, &arg, 1);
    return true;
}

// enb.gadget_set_visible(gadget_ptr, visible) -> bool. Generic primitive: call the
// engine SetVisible on any GadgetClass-derived widget pointer. Used for live probing
// and by the cockpit hide below. Returns false (no-op) if the pointer isn't a valid
// gadget (unreadable vtable / SetVisible slot).
static int l_gadget_set_visible(lua_State* L) {
    uintptr_t g = (uintptr_t)luaL_checkinteger(L, 1);
    unsigned vis = lua_toboolean(L, 2) ? 1u : 0u;
    lua_pushboolean(L, gadget_set_visible(g, vis));
    return 1;
}

// SetVisible(0) every child gadget of one captured cockpit controller over the
// inclusive int-slot range [first, last]. Each step is fully guarded, so a wrong
// controller or a non-gadget slot can only no-op, never fault. Returns how many
// gadgets the call was dispatched on.
static int hide_cockpit_ctrl(uintptr_t ctrl, int first, int last) {
    if (!ctrl)
        return 0;
    int hidden = 0;
    for (int slot = first; slot <= last; ++slot) {
        uintptr_t slot_addr = ctrl + (uintptr_t)slot * 4; // int-slot -> byte offset
        if (!mem::readable((void*)slot_addr, 4))
            continue;
        uintptr_t g = mem::ptr(slot_addr);
        if (gadget_set_visible(g, 0))
            ++hidden;
    }
    return hidden;
}

// enb.hide_cockpit() -> int. Hide the stock bottom-center cockpit widgets (the
// throttle/warp cluster and the "UI COMMANDS" action buttons) that the Freya
// overlay replaces, by calling the engine SetVisible(0) on each child gadget of
// the two captured cockpit controllers. Returns the number of gadgets the call was
// dispatched on THIS call (0 until the constructors have run -- i.e. until in
// space). The game re-shows a gadget when its state changes, so the hide-ui mod
// calls this every frame; once a gadget is hidden the call is an idempotent no-op.
static int l_hide_cockpit(lua_State* L) {
    int n = 0;
    n += hide_cockpit_ctrl(hooks::cockpit_throttle_ctrl(), game::cockpit::throttle_first,
                           game::cockpit::throttle_last);
    n += hide_cockpit_ctrl(hooks::cockpit_cmd_ctrl(), game::cockpit::command_first,
                           game::cockpit::command_last);
    lua_pushinteger(L, n);
    return 1;
}

// enb.cockpit_ctrl() -> throttle_ctrl, cmd_ctrl. Raw pointers to the two cockpit
// controllers captured by the constructor hooks. 0,0 until the cockpit has been
// built (entering space). Diagnostic, mirrors enb.xp_ctrl()/enb.rpg_mgr().
static int l_cockpit_ctrl(lua_State* L) {
    lua_pushinteger(L, (lua_Integer)hooks::cockpit_throttle_ctrl());
    lua_pushinteger(L, (lua_Integer)hooks::cockpit_cmd_ctrl());
    return 2;
}

// enb.chat_panel() -> int. Raw pointer to the chat PANEL object captured by the
// ChatLocalLine hook (hooks::chat_panel()). 0 until the first line is printed this
// session. The Freya chat window derives the line ring from it (panel +
// chat::panel_ring) and walks the ring (chat::ring_cap/ring_count/ring_base, slot
// stride chat::slot_stride) read-only to mirror the merged system+chat scrollback.
static int l_chat_panel(lua_State* L) {
    lua_pushinteger(L, (lua_Integer)hooks::chat_panel());
    return 1;
}

// enb.chat_buf() -> int. Raw pointer to the chat line RING object captured by the
// ChatLineAppend hook (hooks::chat_ring()). 0 until the first line is appended this
// session. The Freya chat window walks its ring (chat::ring_cap/ring_count/ring_base,
// slot stride chat::slot_stride) read-only to mirror the merged system+chat scrollback.
static int l_chat_buf(lua_State* L) {
    lua_pushinteger(L, (lua_Integer)hooks::chat_ring());
    return 1;
}

// enb.pda_ctrl() -> int. Raw pointer to the PDA panel controller captured by the
// PdaCtor/PdaSwitch hooks (hooks::pda_ctrl()). 0 until the controller is built
// (entering space). Diagnostic for the Freya top-left micro-menu.
static int l_pda_ctrl(lua_State* L) {
    lua_pushinteger(L, (lua_Integer)hooks::pda_ctrl());
    return 1;
}

// enb.pda_switch(index) -> uint. Open / switch the PDA to child screen `index`
// through the game's own dispatcher (game::addr::PdaSwitch == FUN_00695780),
// exactly as a native micro-menu button click would: 0=Inventory, 1=Skills,
// 2=Character Info, 3=Vault, 4=Galaxy Map. __thiscall(ECX = the PDA controller,
// int index) -- it dereferences `this` immediately, so we pass the captured
// controller. Returns 0 if the controller hasn't been captured yet. MUST run on
// the game thread (callers are on_tick/on_input).
static int l_pda_switch(lua_State* L) {
    int index = (int)luaL_checkinteger(L, 1);
    unsigned ctrl = hooks::pda_ctrl();
    if (!ctrl) {
        lua_pushinteger(L, 0);
        return 1;
    }
    uint32_t a[1] = {(uint32_t)index};
    lua_pushinteger(L, (lua_Integer)actions::call_thiscall(addr::PdaSwitch, ctrl, a, 1));
    return 1;
}

// enb.shell_ctrl() -> int. Raw pointer to the in-game screen shell captured by
// the ShellApply hook (hooks::shell_ctrl()). 0 until the per-frame apply pump has
// run this session. Diagnostic for the Freya Options button.
static int l_shell_ctrl(lua_State* L) {
    lua_pushinteger(L, (lua_Integer)hooks::shell_ctrl());
    return 1;
}

// enb.shell_screen(id) -> uint. Request the in-game screen shell show screen `id`,
// exactly as a native "Options" button does: ShellRequest (game::addr::ShellRequest
// == FUN_00565f30) just stores the pending id at shell+0x108, and the shell's own
// per-frame apply pump opens it next frame. Id 1 = the in-game OPTIONS_MAIN screen
// (the one micro-menu button that is not a PDA child). __thiscall(ECX = the shell,
// int id); it writes through `this`, so we pass the captured shell. Returns 0 if
// the shell hasn't been captured yet. MUST run on the game thread (callers are
// on_tick/on_input).
static int l_shell_screen(lua_State* L) {
    int id = (int)luaL_checkinteger(L, 1);
    unsigned ctrl = hooks::shell_ctrl();
    if (!ctrl) {
        lua_pushinteger(L, 0);
        return 1;
    }
    uint32_t a[1] = {(uint32_t)id};
    lua_pushinteger(L, (lua_Integer)actions::call_thiscall(addr::ShellRequest, ctrl, a, 1));
    return 1;
}

// enb.chat_send(line [, channel]) -> uint. Submit a typed chat line exactly the way
// the native chat-input box does (mirrors FUN_0065ccd0), so the Freya chat box owns
// text entry while the real send path stays the game's:
//   * A '/'-prefixed line goes to the game's command dispatcher (addr::ChatCmdDispatch,
//     the chat manager's vtable+0x34 entry): /tell <name> <msg> = whisper, /gen /ooc
//     /mkt /new /jen /ter /pro ... = subscribed channels. If it claims the line we are
//     done; if it returns 0 (not a command) we fall through to a plain send.
//   * A plain line is packed into a Client_Chat (opcode 0x33) via addr::ChatBuildMsg
//     and pushed with addr::ChatMsgSend. `channel` selects the target: 3 = Local/Sector
//     (default), 4 = Broadcast. (Earlier this called the slash-only parser directly,
//     which dropped every plain message -- the "box closes but the text vanishes" bug.)
// Returns 0 if the chat manager has not been captured yet (no line printed this
// session). The text is copied into a static buffer kept alive across the synchronous
// build+send (ChatBuildMsg stores the pointer, not a copy). MUST be called on the game
// thread (it is -- callers run from on_tick/on_input).
static std::string g_chat_send_buf;
static unsigned char g_chat_msg_obj[0x80];
static int l_chat_send(lua_State* L) {
    size_t len = 0;
    const char* s = luaL_checklstring(L, 1, &len);
    int channel = (int)luaL_optinteger(L, 2, 3); // 3 = Local/Sector, 4 = Broadcast
    g_chat_send_buf.assign(s, len);
    unsigned mgr = hooks::chat_panel();
    if (!mgr) {
        lua_pushinteger(L, 0);
        return 1;
    }
    const char* line = g_chat_send_buf.c_str();
    if (line[0] == '/') {
        uint32_t a[1] = {(uint32_t)(uintptr_t)line};
        unsigned r = actions::call_thiscall(addr::ChatCmdDispatch, mgr, a, 1);
        if (r & 0xff) { // command claimed the line
            lua_pushinteger(L, (lua_Integer)r);
            return 1;
        }
        // not a recognised command -> fall through and send it as a plain message
    }
    unsigned sender =
        *reinterpret_cast<unsigned*>(static_cast<uintptr_t>(mgr) + addr::ChatMgrSenderId);
    uint32_t build[4] = {sender, (uint32_t)(uintptr_t)line, (uint32_t)channel, 0};
    actions::call_thiscall(addr::ChatBuildMsg, (unsigned)(uintptr_t)g_chat_msg_obj, build, 4);
    uint32_t snd[1] = {(uint32_t)(uintptr_t)g_chat_msg_obj};
    actions::call_thiscall(addr::ChatMsgSend, mgr, snd, 1);
    lua_pushinteger(L, 1);
    return 1;
}

// ---- vtable call profiler (read-only) ------------------------------------
// Find a gadget's per-frame paint slot empirically. Static RE did not converge
// (the bar class's render slots are not in the symbol set), so we observe it
// live: copy the instance's vtable, replace every slot with a 12-byte stub that
// bumps a per-slot counter and then jumps to the ORIGINAL function with every
// register untouched (so it is __thiscall-transparent), and repoint the instance
// at the copy. The slot whose counter climbs ~once per frame is the paint. Fully
// reversible (enb.vt_restore) and non-destructive -- the stubs only count and
// forward, they never change behaviour. CV-AS-HIDE-COCKPIT.
static const int kVtSlots = 64;
static uint32_t g_vt_counts[kVtSlots];
static uintptr_t g_vt_orig[kVtSlots];
static uintptr_t g_vt_copy[kVtSlots]; // replacement vtable handed to the gadget
static uint8_t* g_vt_stubs = nullptr; // kVtSlots * 12 bytes, RWX
static uintptr_t g_vt_target = 0;     // instrumented gadget instance
static uintptr_t g_vt_saved = 0;      // its original vtable pointer

static bool vt_profile_install(uintptr_t g) {
    if (g_vt_target)
        return false; // already profiling one instance; restore first
    if (!g || !mem::readable((void*)g, 4))
        return false;
    uintptr_t vt = mem::ptr(g);
    if (!vt || !mem::readable((void*)vt, kVtSlots * 4))
        return false;
    if (!g_vt_stubs) {
        g_vt_stubs = (uint8_t*)VirtualAlloc(nullptr, kVtSlots * 12, MEM_COMMIT | MEM_RESERVE,
                                            PAGE_EXECUTE_READWRITE);
        if (!g_vt_stubs)
            return false;
    }
    for (int i = 0; i < kVtSlots; ++i) {
        g_vt_counts[i] = 0;
        g_vt_orig[i] = mem::ptr(vt + (uintptr_t)i * 4);
        uint8_t* s = g_vt_stubs + i * 12;
        s[0] = 0xFF; // inc dword [&g_vt_counts[i]]
        s[1] = 0x05;
        *(uint32_t*)(s + 2) = (uint32_t)(uintptr_t)&g_vt_counts[i];
        s[6] = 0xFF; // jmp dword [&g_vt_orig[i]]
        s[7] = 0x25;
        *(uint32_t*)(s + 8) = (uint32_t)(uintptr_t)&g_vt_orig[i];
        g_vt_copy[i] = (uintptr_t)s;
    }
    g_vt_saved = vt;
    g_vt_target = g;
    *(uintptr_t*)g = (uintptr_t)g_vt_copy; // repoint the instance at the copy
    return true;
}

// enb.vt_profile(gadget_ptr) -> bool. Start profiling one gadget instance.
static int l_vt_profile(lua_State* L) {
    uintptr_t g = (uintptr_t)luaL_checkinteger(L, 1);
    lua_pushboolean(L, vt_profile_install(g));
    return 1;
}

// enb.vt_restore() -> bool. Put the instrumented instance's original vtable back.
static int l_vt_restore(lua_State* L) {
    bool ok = false;
    if (g_vt_target && mem::readable((void*)g_vt_target, 4)) {
        *(uintptr_t*)g_vt_target = g_vt_saved;
        ok = true;
    }
    g_vt_target = 0;
    g_vt_saved = 0;
    lua_pushboolean(L, ok);
    return 1;
}

// enb.vt_dump() -> table of "slot=N count=M fn=0xADDR" strings for every slot
// that fired since enb.vt_profile (sorted by count, highest first). The top
// entry that tracks frame count is the paint.
static int l_vt_dump(lua_State* L) {
    // simple selection ordering by count, descending
    int order[kVtSlots];
    int n = 0;
    for (int i = 0; i < kVtSlots; ++i)
        if (g_vt_counts[i])
            order[n++] = i;
    for (int a = 0; a < n; ++a)
        for (int b = a + 1; b < n; ++b)
            if (g_vt_counts[order[b]] > g_vt_counts[order[a]]) {
                int t = order[a];
                order[a] = order[b];
                order[b] = t;
            }
    lua_newtable(L);
    for (int k = 0; k < n; ++k) {
        int i = order[k];
        char buf[96];
        snprintf(buf, sizeof(buf), "slot=%d count=%u fn=0x%08X", i, g_vt_counts[i],
                 (unsigned)g_vt_orig[i]);
        lua_pushstring(L, buf);
        lua_rawseti(L, -2, k + 1);
    }
    return 1;
}

// ---- per-instance paint suppression --------------------------------------
// Non-destructive hide: give ONE gadget instance its own copy of its vtable in
// which a single slot (its per-frame paint, found via the profiler above) is a
// bare `ret` that cleans up the right number of stack-arg bytes, then repoint the
// instance at the copy. Only that instance stops painting -- no global function
// patch, no SetVisible side-effects, so gadget state and the engine
// mouse-focus/target globals are untouched and input keeps working. Reversible
// via enb.vt_unhide(). CV-AS-HIDE-COCKPIT.
static const int kHideMax = 32;
struct HideEnt {
    uintptr_t gadget;
    uintptr_t orig_vptr;
};
static HideEnt g_hide[kHideMax];
static uintptr_t g_hide_vt[kHideMax][kVtSlots]; // per-instance vtable copies (data)
static int g_hide_n = 0;
static uint8_t* g_ret_pool = nullptr; // ret-stub pool, 4 bytes/slot, RWX
static int g_ret_n = 0;

static uintptr_t make_ret_stub(unsigned pop) {
    if (!g_ret_pool) {
        g_ret_pool = (uint8_t*)VirtualAlloc(nullptr, kHideMax * 4, MEM_COMMIT | MEM_RESERVE,
                                            PAGE_EXECUTE_READWRITE);
        if (!g_ret_pool)
            return 0;
    }
    if (g_ret_n >= kHideMax)
        return 0;
    uint8_t* s = g_ret_pool + g_ret_n * 4;
    ++g_ret_n;
    if (pop == 0) {
        s[0] = 0xC3; // ret
    } else {
        s[0] = 0xC2; // ret imm16
        *(uint16_t*)(s + 1) = (uint16_t)pop;
    }
    return (uintptr_t)s;
}

static bool vt_hide_paint(uintptr_t g, int slot, unsigned pop) {
    if (g_hide_n >= kHideMax)
        return false;
    if (!g || !mem::readable((void*)g, 4))
        return false;
    if (slot < 0 || slot >= kVtSlots)
        return false;
    uintptr_t vt = mem::ptr(g);
    if (!vt || !mem::readable((void*)vt, kVtSlots * 4))
        return false;
    uintptr_t stub = make_ret_stub(pop);
    if (!stub)
        return false;
    uintptr_t* copy = g_hide_vt[g_hide_n];
    for (int i = 0; i < kVtSlots; ++i)
        copy[i] = mem::ptr(vt + (uintptr_t)i * 4);
    copy[slot] = stub;
    g_hide[g_hide_n].gadget = g;
    g_hide[g_hide_n].orig_vptr = vt;
    ++g_hide_n;
    *(uintptr_t*)g = (uintptr_t)copy; // repoint instance at the suppressed copy
    return true;
}

// enb.vt_hide_paint(gadget_ptr, slot, pop) -> bool. Suppress one slot's draw on
// one instance. pop = stack-arg bytes the slot's calling convention cleans up
// (0 for __thiscall(this)/__fastcall(this); 8 for __thiscall(this,a,b); etc.).
static int l_vt_hide_paint(lua_State* L) {
    uintptr_t g = (uintptr_t)luaL_checkinteger(L, 1);
    int slot = (int)luaL_checkinteger(L, 2);
    unsigned pop = (unsigned)luaL_optinteger(L, 3, 0);
    lua_pushboolean(L, vt_hide_paint(g, slot, pop));
    return 1;
}

// enb.vt_unhide() -> int. Restore every instance hidden via vt_hide_paint to its
// original vtable. Returns how many were restored.
static int l_vt_unhide(lua_State* L) {
    int n = 0;
    for (int i = 0; i < g_hide_n; ++i) {
        if (g_hide[i].gadget && mem::readable((void*)g_hide[i].gadget, 4)) {
            *(uintptr_t*)g_hide[i].gadget = g_hide[i].orig_vptr;
            ++n;
        }
    }
    g_hide_n = 0;
    g_ret_n = 0;
    lua_pushinteger(L, n);
    return 1;
}

// enb.reset_callbacks() -- drop every registered on_tick/on_skill/on_chat/on_input
// handler so a hot-reload re-runs init.lua from a clean slate instead of stacking a
// second copy of every handler. The Lua-side reload() (init.lua) calls this before
// it re-dofiles the bootstrap; the C++ mtime-poll hot-reload uses it too.
static int l_reset_callbacks(lua_State* L) {
    reset_callbacks(L);
    return 0;
}

// enb.on_input(fn [, mask])  -- fn(msg, wparam, lparam) -> truthy to SWALLOW.
// Optional mask = bitwise-or of enb.WANT_KEY/WANT_CHAR/WANT_MOUSE; default all.
// MULTI-handler: each call ADDS a handler (like on_tick). Handlers run in
// registration order and the FIRST that returns truthy swallows the message --
// later handlers don't see it. The arming mask is the union of every handler's
// mask. enb.on_input(nil) is a no-op for registration but enb.reset_callbacks()
// drops them all (called on reload).
static bool run_input(unsigned msg, unsigned wparam, long lparam) {
    if (g_input_cbs.empty() || !g_L)
        return false;
    lua_State* L = g_L;
    // Snapshot count: a handler must not be able to mutate the list mid-iteration
    // in a way that invalidates our index (Lua can't re-enter here anyway).
    for (size_t i = 0; i < g_input_cbs.size(); ++i) {
        lua_rawgeti(L, LUA_REGISTRYINDEX, g_input_cbs[i].ref);
        lua_pushinteger(L, (lua_Integer)msg);
        lua_pushinteger(L, (lua_Integer)wparam);
        lua_pushinteger(L, (lua_Integer)lparam);
        if (lua_pcall(L, 3, 1, 0) != LUA_OK) {
            logf("lua on_input error: %s", lua_tostring(L, -1));
            lua_pop(L, 1);
            continue; // FAIL OPEN: a script error never swallows input
        }
        bool swallow = lua_toboolean(L, -1);
        lua_pop(L, 1);
        if (swallow)
            return true; // first claimant wins; later handlers don't see it
    }
    return false;
}
static int l_on_input(lua_State* L) {
    if (lua_isnoneornil(L, 1))
        return 0; // nil no longer clears; use reset_callbacks()
    luaL_checktype(L, 1, LUA_TFUNCTION);
    unsigned mask =
        (unsigned)luaL_optinteger(L, 2, hooks::WANT_KEY | hooks::WANT_CHAR | hooks::WANT_MOUSE);
    lua_pushvalue(L, 1);
    g_input_cbs.push_back({luaL_ref(L, LUA_REGISTRYINDEX), mask});
    unsigned uni = 0;
    for (const InputCb& cb : g_input_cbs)
        uni |= cb.mask;
    hooks::set_input_mask(uni);
    return 0;
}

// enb.screen() -> w, h  (real backbuffer size; 0,0 until the first present)
static int l_screen(lua_State* L) {
    int w = 0, h = 0;
    overlay::screen_size(&w, &h);
    lua_pushinteger(L, w);
    lua_pushinteger(L, h);
    return 2;
}
// enb.diag() -> tick_count (pump/input rate), rebuild_count (gated to draw rate),
// present_count (draw rate)
static int l_diag(lua_State* L) {
    lua_pushinteger(L, (lua_Integer)g_tick_n.load(std::memory_order_relaxed));
    lua_pushinteger(L, (lua_Integer)g_rebuild_n.load(std::memory_order_relaxed));
    lua_pushinteger(L, (lua_Integer)overlay::present_count());
    return 3;
}
// enb.measure(s) -> w, h  (font pixel size; 0,0 until the atlas is built)
static int l_measure(lua_State* L) {
    int w = 0, h = 0;
    overlay::measure_text(luaL_checkstring(L, 1), &w, &h);
    lua_pushinteger(L, w);
    lua_pushinteger(L, h);
    return 2;
}

static int l_log(lua_State* L) {
    logs(luaL_checkstring(L, 1));
    return 0;
}

// =====================================================================================
// enb.draw.*  -- overlay display list (immediate-mode, rebuilt each tick)
// =====================================================================================
static int l_draw_text(lua_State* L) {
    int x = (int)luaL_checkinteger(L, 1), y = (int)luaL_checkinteger(L, 2);
    const char* s = luaL_checkstring(L, 3);
    uint32_t rgb = (uint32_t)luaL_optinteger(L, 4, 0xFFFFFF);
    float scale = (float)luaL_optnumber(L, 5, 1.0);
    overlay::text(x, y, s, rgb, scale);
    return 0;
}
static int l_draw_rect(lua_State* L) {
    int x = (int)luaL_checkinteger(L, 1), y = (int)luaL_checkinteger(L, 2);
    int w = (int)luaL_checkinteger(L, 3), h = (int)luaL_checkinteger(L, 4);
    uint32_t rgb = (uint32_t)luaL_optinteger(L, 5, 0xFFFFFF);
    bool filled = lua_toboolean(L, 6);
    int a = (int)luaL_optinteger(L, 7, 255);
    overlay::rect(x, y, w, h, rgb, filled, a);
    return 0;
}
static int l_draw_line(lua_State* L) {
    overlay::line((int)luaL_checkinteger(L, 1), (int)luaL_checkinteger(L, 2),
                  (int)luaL_checkinteger(L, 3), (int)luaL_checkinteger(L, 4),
                  (uint32_t)luaL_optinteger(L, 5, 0xFFFFFF), (int)luaL_optinteger(L, 6, 255));
    return 0;
}
// enb.draw.rect_grad(x,y,w,h, rgb_top, rgb_bottom [, alpha])
static int l_draw_rect_grad(lua_State* L) {
    int x = (int)luaL_checkinteger(L, 1), y = (int)luaL_checkinteger(L, 2);
    int w = (int)luaL_checkinteger(L, 3), h = (int)luaL_checkinteger(L, 4);
    uint32_t top = (uint32_t)luaL_checkinteger(L, 5), bot = (uint32_t)luaL_checkinteger(L, 6);
    int a = (int)luaL_optinteger(L, 7, 255);
    overlay::rect_grad(x, y, w, h, top, bot, a);
    return 0;
}
// enb.draw.rrect(x,y,w,h, radius [, rgb [, alpha [, filled]]])
static int l_draw_rrect(lua_State* L) {
    int x = (int)luaL_checkinteger(L, 1), y = (int)luaL_checkinteger(L, 2);
    int w = (int)luaL_checkinteger(L, 3), h = (int)luaL_checkinteger(L, 4);
    int r = (int)luaL_checkinteger(L, 5);
    uint32_t rgb = (uint32_t)luaL_optinteger(L, 6, 0xFFFFFF);
    int a = (int)luaL_optinteger(L, 7, 255);
    bool filled = lua_isnoneornil(L, 8) ? true : lua_toboolean(L, 8);
    overlay::rrect(x, y, w, h, r, rgb, a, filled);
    return 0;
}
// enb.draw.rrect_grad(x,y,w,h, radius, rgb_top, rgb_bottom [, alpha])
static int l_draw_rrect_grad(lua_State* L) {
    int x = (int)luaL_checkinteger(L, 1), y = (int)luaL_checkinteger(L, 2);
    int w = (int)luaL_checkinteger(L, 3), h = (int)luaL_checkinteger(L, 4);
    int r = (int)luaL_checkinteger(L, 5);
    uint32_t top = (uint32_t)luaL_checkinteger(L, 6), bot = (uint32_t)luaL_checkinteger(L, 7);
    int a = (int)luaL_optinteger(L, 8, 255);
    overlay::rrect_grad(x, y, w, h, r, top, bot, a);
    return 0;
}
static int l_draw_image(lua_State* L) {
    const char* p = luaL_checkstring(L, 1);
    int x = (int)luaL_checkinteger(L, 2), y = (int)luaL_checkinteger(L, 3);
    int w = (int)luaL_optinteger(L, 4, 0), h = (int)luaL_optinteger(L, 5, 0);
    int a = (int)luaL_optinteger(L, 6, 255);
    overlay::image(p, x, y, w, h, a);
    return 0;
}
// enb.draw.texture_quad(texptr, x, y, w, h [, alpha [, tint [, additive]]]) -- blit
// a live game-owned IDirect3DTexture8* (e.g. an action-bar icon resolved by walking
// the slot gadget tree) into a quad, UV 0..1, inside the game's Present hook.
//   alpha    0..255 brightness scale (default 255).
//   tint     0xRRGGBB multiplied into the icon (default 0xFFFFFF = untinted); use it
//            to give the HUD a hue (e.g. a light blue).
//   additive when true, blend ONE/ONE (pure glow); when false (default), ONE/
//            INVSRCCOLOR -- a "black key" screen blend that drops the icon's black
//            background to transparent while keeping bright pixels solid. Game icon
//            textures are opaque RGB on black with no usable alpha, so this is what
//            makes the background transparent.
static int l_draw_texquad(lua_State* L) {
    void* tex = (void*)(uintptr_t)luaL_checkinteger(L, 1);
    int x = (int)luaL_checkinteger(L, 2), y = (int)luaL_checkinteger(L, 3);
    int w = (int)luaL_checkinteger(L, 4), h = (int)luaL_checkinteger(L, 5);
    int a = (int)luaL_optinteger(L, 6, 255);
    uint32_t tint = (uint32_t)luaL_optinteger(L, 7, 0xFFFFFF);
    bool additive = lua_toboolean(L, 8) != 0;
    overlay::texture_quad(tex, x, y, w, h, a, tint, additive);
    return 0;
}

// =====================================================================================
// enb.tap / enb.key / enb.char / enb.call(_cdecl)  -- actions
// =====================================================================================
static int l_tap(lua_State* L) {
    lua_pushboolean(L, actions::tap_key((int)luaL_checkinteger(L, 1)));
    return 1;
}
static int l_key(lua_State* L) {
    int vk = (int)luaL_checkinteger(L, 1);
    if (lua_isnoneornil(L, 2)) {
        lua_pushboolean(L, actions::tap_key(vk));
    } else {
        lua_pushboolean(L, actions::post_key(vk, lua_toboolean(L, 2)));
    }
    return 1;
}
static int l_char(lua_State* L) {
    lua_pushboolean(L, actions::post_char((unsigned)luaL_checkinteger(L, 1)));
    return 1;
}

static int collect_args(lua_State* L, int from, uint32_t* out) {
    int n = 0;
    for (int i = from; i <= lua_gettop(L) && n < 8; ++i)
        out[n++] = (uint32_t)luaL_checkinteger(L, i);
    return n;
}
// enb.call(addr, this, a, b, ...) -> eax  (__thiscall)
static int l_call(lua_State* L) {
    uintptr_t fn = (uintptr_t)luaL_checkinteger(L, 1);
    uintptr_t self = (uintptr_t)luaL_checkinteger(L, 2);
    uint32_t a[8];
    int n = collect_args(L, 3, a);
    lua_pushinteger(L, (lua_Integer)actions::call_thiscall(fn, self, a, n));
    return 1;
}
// enb.call_cdecl(addr, a, b, ...) -> eax
static int l_call_cdecl(lua_State* L) {
    uintptr_t fn = (uintptr_t)luaL_checkinteger(L, 1);
    uint32_t a[8];
    int n = collect_args(L, 2, a);
    lua_pushinteger(L, (lua_Integer)actions::call_cdecl(fn, a, n));
    return 1;
}
static int l_hwnd(lua_State* L) {
    lua_pushinteger(L, (lua_Integer)(uintptr_t)actions::game_hwnd());
    return 1;
}

// enb.list_dir(path) -> { name, name, ... }
// Directory entry names (files + subdirs, excluding "." and ".."). Used by the
// Lua mod loader to discover the mod folders staged under scripts/mods/. Returns
// an empty table if the path does not exist or cannot be opened.
static int l_list_dir(lua_State* L) {
    const char* path = luaL_checkstring(L, 1);
    std::string pat = std::string(path) + "\\*";
    lua_newtable(L);
    WIN32_FIND_DATAA fd;
    HANDLE h = FindFirstFileA(pat.c_str(), &fd);
    if (h == INVALID_HANDLE_VALUE)
        return 1;
    int i = 0;
    do {
        const char* n = fd.cFileName;
        if (std::strcmp(n, ".") == 0 || std::strcmp(n, "..") == 0)
            continue;
        lua_pushstring(L, n);
        lua_rawseti(L, -2, ++i);
    } while (FindNextFileA(h, &fd));
    FindClose(h);
    return 1;
}

// =====================================================================================
// registration
// =====================================================================================
static void push_addr_table(lua_State* L) {
    lua_newtable(L);
#define A(name)                                                                                    \
    lua_pushinteger(L, (lua_Integer)addr::name);                                                   \
    lua_setfield(L, -2, #name)
    A(StatBlock);
    A(EnergyBar);
    A(HullPoints);
    A(VitalsBars);
    A(VitalsPaint);
    A(LevelText);
    A(XpBars);
    A(XpPaint);
    A(RpgLevels);
    A(TargetInfo);
    A(TargetPanel);
    A(ChatGadget);
    A(ChatRender);
    A(ChatChannel);
    A(ChatSend);
    A(ChatCmdDispatch);
    A(ChatBuildMsg);
    A(ChatMsgSend);
    A(NavListBuild);
    A(NavListRender);
    A(WarpPath);
    A(KeyDefinitions);
    A(BindCategories);
    A(AbilitySlots);
    A(SkillLifecycle);
    A(SkillButton);
    A(MsgPump_Get);
    A(MsgPump_Peek);
#undef A
}

void open(lua_State* L) {
    g_L = L; // captured for the synchronous input handler (see run_input)
    luaL_openlibs(L);

    lua_newtable(L); // enb

    lua_pushinteger(L, (lua_Integer)kImageBase);
    lua_setfield(L, -2, "base");

// input-mask flags (enb.on_input second arg) + raw window-message ids so a
// Lua handler can classify msg without magic numbers.
#define ENBI(name, val)                                                                            \
    lua_pushinteger(L, (lua_Integer)(val));                                                        \
    lua_setfield(L, -2, #name)
    ENBI(WANT_KEY, hooks::WANT_KEY);
    ENBI(WANT_CHAR, hooks::WANT_CHAR);
    ENBI(WANT_MOUSE, hooks::WANT_MOUSE);
#undef ENBI

    static const luaL_Reg fns[] = {{"log", l_log},
                                   {"calibrate", l_calibrate},
                                   {"offsets", l_offsets},
                                   {"self", l_self},
                                   {"target", l_target},
                                   {"target_obj", l_target_obj},
                                   {"aux_entry", l_aux_entry},
                                   {"state", l_state},
                                   {"cursor", l_cursor},
                                   {"patch_ret", l_patch_ret},
                                   {"unpatch", l_unpatch},
                                   {"on_tick", l_on_tick},
                                   {"on_skill", l_on_skill},
                                   {"on_chat", l_on_chat},
                                   {"on_input", l_on_input},
                                   {"screen", l_screen},
                                   {"diag", l_diag},
                                   {"measure", l_measure},
                                   {"enable_event_hooks", l_enable_event_hooks},
                                   {"enable_inspace", l_enable_inspace},
                                   {"inspace", l_inspace},
                                   {"vitals_ctrl", l_vitals_ctrl},
                                   {"vitals", l_vitals},
                                   {"aux", l_aux},
                                   {"aux_i", l_aux_i},
                                   {"rpg_level", l_rpg_level},
                                   {"rpg_mgr", l_rpg_mgr},
                                   {"xp_frac", l_xp_frac},
                                   {"xp_ctrl", l_xp_ctrl},
                                   {"actionbar", l_actionbar},
                                   {"hide_cockpit", l_hide_cockpit},
                                   {"cockpit_ctrl", l_cockpit_ctrl},
                                   {"chat_panel", l_chat_panel},
                                   {"chat_buf", l_chat_buf},
                                   {"pda_ctrl", l_pda_ctrl},
                                   {"pda_switch", l_pda_switch},
                                   {"shell_ctrl", l_shell_ctrl},
                                   {"shell_screen", l_shell_screen},
                                   {"chat_send", l_chat_send},
                                   {"gadget_set_visible", l_gadget_set_visible},
                                   {"vt_profile", l_vt_profile},
                                   {"vt_dump", l_vt_dump},
                                   {"vt_restore", l_vt_restore},
                                   {"vt_hide_paint", l_vt_hide_paint},
                                   {"vt_unhide", l_vt_unhide},
                                   {"reset_callbacks", l_reset_callbacks},
                                   {"tap", l_tap},
                                   {"key", l_key},
                                   {"char", l_char},
                                   {"call", l_call},
                                   {"call_cdecl", l_call_cdecl},
                                   {"hwnd", l_hwnd},
                                   {"list_dir", l_list_dir},
                                   {nullptr, nullptr}};
    luaL_setfuncs(L, fns, 0);

    // enb.draw subtable
    lua_newtable(L);
    static const luaL_Reg drawfns[] = {{"text", l_draw_text},
                                       {"rect", l_draw_rect},
                                       {"line", l_draw_line},
                                       {"image", l_draw_image},
                                       {"texture_quad", l_draw_texquad},
                                       {"rect_grad", l_draw_rect_grad},
                                       {"rrect", l_draw_rrect},
                                       {"rrect_grad", l_draw_rrect_grad},
                                       {nullptr, nullptr}};
    luaL_setfuncs(L, drawfns, 0);
    lua_setfield(L, -2, "draw");

    // enb.mem subtable
    lua_newtable(L);
    static const luaL_Reg memfns[] = {
        {"u8", l_r_u8},           {"u16", l_r_u16},   {"u32", l_r_u32},
        {"i32", l_r_i32},         {"f32", l_r_f32},   {"f64", l_r_f64},
        {"ptr", l_r_ptr},         {"str", l_r_str},   {"wstr", l_r_wstr},
        {"readable", l_readable}, {"chain", l_chain}, {"write_u32", l_w_u32},
        {"write_f32", l_w_f32},   {nullptr, nullptr}};
    luaL_setfuncs(L, memfns, 0);
    lua_setfield(L, -2, "mem");

    // enb.addr subtable
    push_addr_table(L);
    lua_setfield(L, -2, "addr");

    // enb.msg subtable -- raw Win32 message ids for on_input classification.
    lua_newtable(L);
#define ENBM(name, val)                                                                            \
    lua_pushinteger(L, (lua_Integer)(val));                                                        \
    lua_setfield(L, -2, #name)
    ENBM(KEYDOWN, 0x0100);
    ENBM(KEYUP, 0x0101);
    ENBM(SYSKEYDOWN, 0x0104);
    ENBM(SYSKEYUP, 0x0105);
    ENBM(CHAR, 0x0102);
    ENBM(MOUSEMOVE, 0x0200);
    ENBM(MOUSEWHEEL, 0x020A);
    ENBM(LBUTTONDOWN, 0x0201);
    ENBM(LBUTTONUP, 0x0202);
    ENBM(LBUTTONDBLCLK, 0x0203);
    ENBM(RBUTTONDOWN, 0x0204);
    ENBM(RBUTTONUP, 0x0205);
    ENBM(RBUTTONDBLCLK, 0x0206);
    ENBM(MBUTTONDOWN, 0x0207);
    ENBM(MBUTTONUP, 0x0208);
    ENBM(MBUTTONDBLCLK, 0x0209);
#undef ENBM
    lua_setfield(L, -2, "msg");

    lua_setglobal(L, "enb");

    // wire the game-function hooks to enqueue events for the tick
    hooks::set_on_skill([](unsigned a, unsigned b) {
        std::lock_guard<std::mutex> lk(g_evq_mx);
        g_evq.push_back({0, a, b});
    });
    hooks::set_on_chat([](unsigned a, unsigned b) {
        std::lock_guard<std::mutex> lk(g_evq_mx);
        g_evq.push_back({1, a, b});
    });
    // input handler runs synchronously on the game thread (returns swallow bool).
    hooks::set_on_input(run_input);
    // "/run <lua>" chat console: swallow any typed line starting with "/run " and
    // queue the remainder for execution on the tick thread. The decision (swallow
    // or pass through) is pure string work -- no Lua here, so it is safe on the
    // game thread. Returning true tells the hook to skip the real chat send.
    hooks::set_on_chat_send([](const char* line) -> bool {
        if (!line || std::strncmp(line, "/run ", 5) != 0)
            return false;
        const char* code = line + 5;
        std::lock_guard<std::mutex> lk(g_runq_mx);
        g_runq.emplace_back(code);
        return true;
    });
}

static void call_ref(lua_State* L, int ref, int nargs) {
    // expects nargs already on stack above the function slot; we insert the function below them
    lua_rawgeti(L, LUA_REGISTRYINDEX, ref);
    if (nargs)
        lua_insert(L, -1 - nargs);
    if (lua_pcall(L, nargs, 0, 0) != LUA_OK) {
        logf("lua callback error: %s", lua_tostring(L, -1));
        lua_pop(L, 1);
    }
}

// Echo "/run" output. This used to call the client's local chat-line printer
// (game::addr::ChatLocalLine) so results appeared in the game's chat window, but
// that routine's address + calling convention were never exercised until the
// console first ran, and the first real call faulted the client (stack/ABI
// mismatch -- the signature is unverified). Until ChatLocalLine is verified
// against the real client, route console output to enbmod.log ONLY: reliable,
// crash-free, and still fully visible. (Re-enable an in-game echo only after the
// printer's signature is confirmed -- track it as a CV item.)
static void chat_echo(const std::string& text) {
    const int kMaxLines = 60;
    size_t start = 0;
    int lines = 0;
    while (true) {
        size_t nl = text.find('\n', start);
        std::string seg =
            text.substr(start, nl == std::string::npos ? std::string::npos : nl - start);
        if (!seg.empty() && seg.back() == '\r')
            seg.pop_back();
        if (++lines > kMaxLines) {
            logf("[run] ... (output truncated)");
            break;
        }
        logf("[run] %s", seg.c_str());
        if (nl == std::string::npos)
            break;
        start = nl + 1;
    }
}

// Convert a result on the Lua stack at `idx` to display text. Tables are run
// through the global dump() (set in init.lua) so structures are inspectable;
// strings/numbers print directly; everything else shows its type tag.
static std::string result_text(lua_State* L, int idx) {
    if (lua_type(L, idx) == LUA_TTABLE) {
        lua_getglobal(L, "dump");
        if (lua_isfunction(L, -1)) {
            lua_pushvalue(L, idx);
            if (lua_pcall(L, 1, 1, 0) == LUA_OK && lua_isstring(L, -1)) {
                std::string s = lua_tostring(L, -1);
                lua_pop(L, 1);
                return s;
            }
            lua_pop(L, 1); // dump error object or non-string result
            return "<table> (dump failed)";
        }
        lua_pop(L, 1); // dump not available
        return "<table>";
    }
    if (lua_isstring(L, idx))
        return lua_tostring(L, idx);
    return std::string("<") + luaL_typename(L, idx) + ">";
}

// Execute one "/run" snippet on the Lua thread. Tries it as an expression first
// (`return <code>`) so a bare value or function call echoes its result, then
// falls back to running it as a statement. The expression's output is echoed to
// the chat window as "[Lua] ..."; errors go there too so failures are visible.
static void run_console(lua_State* L, const std::string& code) {
    std::string expr = "return " + code;
    if (luaL_loadstring(L, expr.c_str()) != LUA_OK) {
        lua_pop(L, 1); // discard the expr compile error; try as a statement
        if (luaL_loadstring(L, code.c_str()) != LUA_OK) {
            std::string e = lua_tostring(L, -1) ? lua_tostring(L, -1) : "?";
            logf("[run] compile error: %s", e.c_str());
            lua_pop(L, 1);
            return;
        }
    }
    int base = lua_gettop(L) - 1; // index below the loaded chunk
    if (lua_pcall(L, 0, LUA_MULTRET, 0) != LUA_OK) {
        std::string e = lua_tostring(L, -1) ? lua_tostring(L, -1) : "?";
        logf("[run] error: %s", e.c_str());
        lua_pop(L, 1);
        return;
    }
    int nres = lua_gettop(L) - base;
    for (int i = 1; i <= nres; i++) {
        std::string out = result_text(L, base + i);
        chat_echo(out); // chat_echo logs each line to enbmod.log
    }
    lua_pop(L, nres);
}

// ---- external console channel (enbmod.cmd) ---------------------------------
// A dev/debug channel so Lua can be driven into the running client from OUTSIDE
// the game (a shell, the launcher, an agent): write line(s) of Lua to a file
// "enbmod.cmd" sited next to enbmod.log, and the next tick runs each line through
// the exact same path as the in-game "/run" console -- output (results, errors)
// lands in enbmod.log. Read-then-truncate: the external writer appends, we
// consume and clear so each command runs exactly once. Single-writer is assumed
// (this is a debug aid, not an RPC); a line written in the tiny window between
// our read and truncate is the writer's to retry. Blank lines and lines starting
// with '#' are ignored so the file can carry comments.
static void poll_cmd_file(lua_State* L) {
    const char* dir = log_dir();
    if (!dir || !dir[0])
        return;
    char path[MAX_PATH];
    snprintf(path, sizeof path, "%s\\enbmod.cmd", dir);
    FILE* f = fopen(path, "rb");
    if (!f)
        return; // no pending commands (the common case)
    std::string body;
    char buf[1024];
    size_t n;
    while ((n = fread(buf, 1, sizeof buf, f)) > 0)
        body.append(buf, n);
    fclose(f);
    if (body.empty())
        return;
    // consume: truncate so the same command never re-runs next tick
    if (FILE* t = fopen(path, "wb"))
        fclose(t);

    size_t start = 0;
    while (start < body.size()) {
        size_t nl = body.find('\n', start);
        std::string line =
            body.substr(start, nl == std::string::npos ? std::string::npos : nl - start);
        start = (nl == std::string::npos) ? body.size() : nl + 1;
        while (!line.empty() && (line.back() == '\r' || line.back() == ' ' || line.back() == '\t'))
            line.pop_back();
        size_t b = line.find_first_not_of(" \t");
        if (b == std::string::npos || line[b] == '#')
            continue; // blank line or '#' comment
        run_console(L, line.substr(b));
    }
}

void tick(lua_State* L) {
    g_tick_n.fetch_add(1, std::memory_order_relaxed);
    // poll the external command channel (throttled; cheap no-op when absent)
    static unsigned cmd_poll = 0;
    if (++cmd_poll >= 15) {
        cmd_poll = 0;
        poll_cmd_file(L);
    }

    // run any queued "/run" console snippets (game thread enqueued; we run here)
    std::vector<std::string> runs;
    {
        std::lock_guard<std::mutex> lk(g_runq_mx);
        runs.swap(g_runq);
    }
    for (auto& code : runs)
        run_console(L, code);

    // drain queued events first
    std::deque<Event> local;
    {
        std::lock_guard<std::mutex> lk(g_evq_mx);
        local.swap(g_evq);
    }
    for (auto& e : local) {
        int ref = e.kind == 0 ? g_skill_ref : g_chat_ref;
        if (ref == LUA_NOREF)
            continue;
        lua_pushinteger(L, e.a);
        lua_pushinteger(L, e.b);
        call_ref(L, ref, 2);
    }
    // overlay: rebuild the display list (enb.draw.* calls land in staging), then
    // commit it for the Present hook to render.
    //
    // Clock the rebuild at the DRAW rate, NOT the pump rate. tick() rides
    // PeekMessageA, which the game's pump calls once per loop iteration -- and a
    // mouse-move flood makes the pump spin several times per rendered frame
    // (measured ~4x). Rebuilding on every pump call therefore (a) re-runs every
    // on_tick draw handler ~4x as often as the frame is actually drawn (pure
    // wasted CPU that scales with mouse input -> the lag), and (b) widens the
    // window in which a transient empty/partial rebuild gets committed between
    // two Presents, which draws as a blank HUD frame -> the flicker. Gating to
    // once per Present makes the rebuild rate track the draw rate and become
    // independent of input: one rebuild per drawn frame, no matter how fast the
    // pump spins. The cmd-channel poll + event drain above stay at pump rate, so
    // they remain responsive even when the game is not presenting; if the game
    // stops presenting entirely (alt-tab/minimize) the rebuild stalls, which is
    // harmless -- nothing is being drawn anyway.
    static unsigned long last_rebuild_present = (unsigned long)-1;
    unsigned long pn = overlay::present_count();
    if (pn != last_rebuild_present) {
        last_rebuild_present = pn;
        g_rebuild_n.fetch_add(1, std::memory_order_relaxed);
        overlay::begin_frame();
        for (int ref : g_tick_refs)
            call_ref(L, ref, 0);
        overlay::commit_frame();
    }
}

void on_skill(unsigned a, unsigned b) {
    std::lock_guard<std::mutex> lk(g_evq_mx);
    g_evq.push_back({0, a, b});
}
void on_chat(unsigned a, unsigned b) {
    std::lock_guard<std::mutex> lk(g_evq_mx);
    g_evq.push_back({1, a, b});
}

} // namespace lua
} // namespace enb
