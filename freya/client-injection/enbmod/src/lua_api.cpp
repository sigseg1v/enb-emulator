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
#include <cmath>
#include <cctype>
#include <algorithm>
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
// Sources, in priority order:
//   1. The optional calibrated game-state code (game_state_addr) -- kept as an
//      override hook, but the client has no single global state enum, so it stays
//      uncalibrated (game_state_addr == 0) and yields nothing.
//   2. The in-world docked/in-space discriminator on the captured world manager M:
//      M + world::station_view is non-zero exactly while docked (the station
//      interior view) and 0 while in space, so a live M positively names
//      "station" vs "space" with zero calibration. This is the reliable source.
//   3. The in-space heartbeat (enable_inspace_hook) -- a per-frame vitals-updater
//      signal that confirms "space"; it only ever upgrades "unknown" -> "space".
// "unknown" is the pre-M default (front-end screens; HUD then shows the skeleton).
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
    // In-world source: a captured M means we are in-game; station_view splits
    // docked (station) from undocked (space). Only consulted while state is still
    // "unknown" so an explicit calibrated code (source 1) still wins if present.
    if (!strcmp(name, "unknown")) {
        uintptr_t m = hooks::world_mgr();
        if (m && mem::readable((void*)(m + game::world::station_view), 4))
            name = mem::i32(m + game::world::station_view) != 0 ? "station" : "space";
    }
    // Heartbeat fallback: if neither source above could name a state, but the
    // in-space vitals updater is firing, we ARE in space.
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
static bool aux_read_shared_f(uintptr_t container, const char* key, double& out);
static int aux_entry_guarded(uintptr_t entity, const char* key);
static int target_level_live();
static uintptr_t targeting_data_obj();
static bool world_pos(uintptr_t obj, double out[3]);

// enb.target() -> { base, name, hull, hull_max, shield, shield_max, level } | nil.
// The current target is the live game object the native target-frame refresh resolves
// from the selected GameID, captured by the TargetEntitySet hook (hooks::target_obj()),
// refreshed on every target switch and 0 when nothing is selected. Its hull/shield
// AuxData and class name live on its properties container (object+0x88), not the contact
// object; the game stores shield as a 0..1 percent (ShieldPercent) of MaxShieldPower,
// with no absolute current-shield key -- and ShieldPercent is a DELTA-INTERPOLATED
// shared value, so aux_read_shared_f reads its settled snapshot float (game::aux::
// shared_val_off) rather than the per-frame animation scratch, which extrapolates to
// 0 while a shield is draining (see game.h). Its INSTANCE name is a char* on that same
// container at +0x124 (class name at +0x3c). The target's level is live-read off the
// targeting subsystem (target_level_live()) -- the server-fed "TargetThreat" string.
// enb.target_obj() -> live selected target object pointer (0 until the hook fires
// / when nothing is targeted). Diagnostic: lets Lua walk the target object with
// guarded raw reads (enb.read.*) without invoking the aux machinery, which faults
// on objects whose aux-container list shape differs from the player's.
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
    // The target's hull/shield AuxData and its class name both live on the object's
    // PROPERTIES CONTAINER, not the contact object itself: the contact at entity holds
    // a pointer to it at +0x88. The aux property bag (HullPoints / MaxHullPoints /
    // MaxShieldPower) is read off that container; the class name ("Ship"/"Starbase") is
    // a char* at container+0x3c.
    uintptr_t container = mem::ptr(entity + 0x88);
    // name: prefer the object's INSTANCE name ("Loki Station", "Scuttlebug", "Needlenose"),
    // a char* at container+0x124. Fall back to the generic CLASS name (char* at
    // container+0x3c, "Ship"/"Starbase") only when the instance name is missing/empty, so
    // the frame always shows something.
    {
        std::string nm;
        if (container) {
            uintptr_t namep = mem::ptr(container + 0x124);
            if (namep && mem::readable((void*)namep, 1))
                nm = mem::cstr(namep);
        }
        if (nm.empty() && container) {
            uintptr_t clsp = mem::ptr(container + 0x3c);
            if (clsp && mem::readable((void*)clsp, 1))
                nm = mem::cstr(clsp);
        }
        if (!nm.empty()) {
            lua_pushlstring(L, nm.data(), nm.size());
            lua_setfield(L, -2, "name");
        }
    }
    // class: the generic CLASS name (char* at container+0x3c, e.g. "Ship"/"Starbase"/
    // "Stargate"/"Asteroid"). The mod gates which verb letter-buttons to show on this.
    if (container) {
        uintptr_t clsp = mem::ptr(container + 0x3c);
        if (clsp && mem::readable((void*)clsp, 1)) {
            std::string cls = mem::cstr(clsp);
            if (!cls.empty()) {
                lua_pushlstring(L, cls.data(), cls.size());
                lua_setfield(L, -2, "class");
            }
        }
    }
    double d;
    if (container && aux_read_f(container, "HullPoints", d)) {
        lua_pushnumber(L, d);
        lua_setfield(L, -2, "hull");
    }
    if (container && aux_read_f(container, "MaxHullPoints", d)) {
        lua_pushnumber(L, d);
        lua_setfield(L, -2, "hull_max");
    }
    double smax = -1.0, spct = -1.0;
    if (container && aux_read_f(container, "MaxShieldPower", d)) {
        smax = d;
        lua_pushnumber(L, d);
        lua_setfield(L, -2, "shield_max");
    }
    if (container && aux_read_shared_f(container, "ShieldPercent", d))
        spct = d;
    if (smax >= 0.0 && spct >= 0.0) {
        lua_pushnumber(L, smax * spct);
        lua_setfield(L, -2, "shield");
    }
    // level: live-read off the targeting subsystem each call. The server sends a mob's
    // level as the "TargetThreat" string ("Level N"), which target_level_live() parses.
    // Non-combat targets (stations, navs, decorations) get an empty/relative threat with
    // no parseable level, so target_level_live() returns -1 and we show no level.
    // Cache the level per target: target_level_live() does a vtable getter call, so we
    // call it only until a valid level lands for the CURRENT target object, then serve
    // the cached value on later frames (the level is async-populated a few frames after
    // the target switch). Re-arms when the target object changes. This keeps the
    // per-frame path a pure read once the level is known.
    static uintptr_t s_lvl_obj = 0;
    static int s_lvl_val = -1;
    if (entity != s_lvl_obj) {
        s_lvl_obj = entity;
        s_lvl_val = -1;
    }
    int lvl = s_lvl_val;
    if (lvl < 0) {
        lvl = target_level_live();
        if (lvl >= 0)
            s_lvl_val = lvl;
    }
    if (lvl >= 0 && lvl <= 255) {
        lua_pushinteger(L, lvl);
        lua_setfield(L, -2, "level");
    }
    // distance: straight-line range = |player_wpos - target_wpos|, both read through the
    // locatable getter (world_pos). The player position comes from the SAME ship object the
    // native target frame resolves (targeting_data_obj()) -- the local ship, a positional
    // locatable -- NOT the vitals-chain entity (which is not a positional space object). The
    // flat pos offsets are uncalibrated, so position must come through the getter.
    {
        double tp[3], pp[3];
        uintptr_t ship = targeting_data_obj();
        if (ship && world_pos(entity, tp) && world_pos(ship, pp)) {
            double dx = tp[0] - pp[0], dy = tp[1] - pp[1], dz = tp[2] - pp[2];
            double dist = std::sqrt(dx * dx + dy * dy + dz * dz);
            // a plausible in-sector range; reject NaN / absurd values from a bad read.
            if (dist == dist && dist >= 0.0 && dist < 1.0e9) {
                lua_pushnumber(L, dist);
                lua_setfield(L, -2, "dist");
            }
        }
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

// Read a SHARED / network-replicated auxdata float (ShieldPercent lives here, not
// in the plain get_value list). Builds the key, resolves the entry through
// aux::get_shared, checks the valid flag, and returns the live 0..1 value at
// entry + shared_val_off. Fully fault-guarded like aux_entry_guarded: a fault in
// the key build or resolve longjmps back and yields "no value".
static bool aux_read_shared_f(uintptr_t container, const char* key, double& out) {
    if (!container || !mem::readable((void*)container, 4))
        return false;
    unsigned char keybuf[game::aux::keybuf_sz];
    memset(keybuf, 0, sizeof(keybuf));
    int entry = 0;
    if (__builtin_setjmp(mem::g_guard_jmp)) {
        mem::g_guard_on = 0;
        return false;
    }
    mem::g_guard_on = 1;
    ((AuxBuildKey_t)game::aux::build_key)(keybuf, key);
    entry = ((AuxGetValue_t)game::aux::get_shared)((int)container, keybuf);
    mem::g_guard_on = 0;
    if (!entry || !mem::readable((void*)(uintptr_t)entry, game::aux::shared_val_off + 4))
        return false;
    if (mem::i32((uintptr_t)entry + game::aux::valid_off) == 0)
        return false; // value not set
    float v = mem::f32((uintptr_t)entry + game::aux::shared_val_off);
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

// enb.group() -> { count = N, members = { {name=, gameid=, [hull=, hull_max=,
// shield=, shield_max=]}, ... } } | empty table.
// Reads the party roster from the "GroupInfo" object-typed aux entry, which lives in
// the same property-bag container as the RPGInfo levels (avatar object + container_off).
// The container-holding avatar object is captured two ways in this DLL; we try the RPG
// manager first (the proven holder of that container) and fall back to the vitals-chain
// ship entity, using whichever actually yields a group object -- both are guarded reads,
// so a wrong base just misses. Returns an empty table when not in a group, out of space,
// or before either object has been captured.
// The member struct stores only name + GameID (+ formation/position); per-member
// hull/shield are NOT on it. We get them the way the native group window did: resolve
// each member's GameID to its live contact object via the world manager's entity table
// (entity_by_gid) and read HullPoints / MaxHullPoints / MaxShieldPower / ShieldPercent
// off its aux bag, identical to enb.target(). That only works for members in the current
// sector / scanner range; members elsewhere have no live entity and come back name+id
// only (their vital keys are simply absent).
//
// Convention is load-bearing (same trap as enb.aux): build_key is __thiscall(ECX=keybuf),
// group::get_object is __cdecl(container, keybuf). The get_object call and the member-array
// walk run under the VEH fault guard so a stale/half-built roster yields an empty table
// instead of faulting the client.
struct GroupMember {
    uintptr_t namep; // member name char* (resolved to a string outside the guard)
    unsigned gameid;
};

// Resolve a GameID to its live in-scene contact object via the world manager's GameID
// hash table (game::world::ent_*). The member struct stores no vitals, so a party
// member's hull/shield must be read off the SAME render/contact object the rest of the
// HUD uses, found by GameID. This is a pure guarded walk -- no native call, so no
// calling-convention hazard on the live client; a GameID with no live entity (member in
// another sector / not yet in scene) returns 0, and the bounded hop count guards against
// a corrupt or cyclic bucket chain. The bucket for gid % ent_modulus holds the node whose
// ent_node_gid keys the entity; match the key and take ent_node_obj. Self-guarded (own
// setjmp); call it with no other fault guard active.
static uintptr_t entity_by_gid(unsigned gameid) {
    uintptr_t M = hooks::world_mgr();
    if (!M || !gameid)
        return 0;
    uintptr_t result = 0;
    if (__builtin_setjmp(mem::g_guard_jmp)) {
        mem::g_guard_on = 0;
        return 0; // a read faulted while walking the table -- treat as "no entity"
    }
    mem::g_guard_on = 1;
    uintptr_t head = M + game::world::ent_buckets + (gameid % game::world::ent_modulus) * 4;
    if (mem::readable((void*)head, 4)) {
        uintptr_t node = mem::ptr(head);
        for (int hops = 0; node && hops < 256; ++hops) {
            if (!mem::readable((void*)(node + game::world::ent_node_gid), 4))
                break;
            // Match the key FIRST. (An earlier version broke the walk on a
            // "deleted/skip" bit at node+0x18 before comparing the key -- but that
            // byte reads back non-zero on perfectly LIVE nodes, so it aborted the
            // walk on the first, matching node and every avatar resolved to 0.
            // That was the "party-frame hull/shield stay empty" bug: the member's
            // real contact object was right there under a matching key.)
            if ((unsigned)mem::i32(node + game::world::ent_node_gid) == gameid) {
                result = mem::ptr(node + game::world::ent_node_obj);
                break;
            }
            if (!mem::readable((void*)(node + game::world::ent_node_next), 4))
                break;
            node = mem::ptr(node + game::world::ent_node_next);
        }
    }
    mem::g_guard_on = 0;
    return result;
}

static int l_group(lua_State* L) {
    uintptr_t bases[2] = {hooks::rpg_mgr(), player_entity()};
    unsigned char keybuf[game::aux::keybuf_sz];
    memset(keybuf, 0, sizeof(keybuf));

    // Collect raw member pointers/ids UNDER the fault guard with NO Lua stack ops, so a
    // mid-walk longjmp can never leave a half-built table on the Lua stack (it just
    // truncates the C++ vector). The Lua table is built afterwards from these locals.
    GroupMember raw[game::group::max_members];
    int n = 0;
    if (__builtin_setjmp(mem::g_guard_jmp)) {
        mem::g_guard_on = 0; // a read faulted mid-walk: keep what we gathered so far
    } else {
        mem::g_guard_on = 1;
        ((AuxBuildKey_t)game::aux::build_key)(keybuf, "GroupInfo");
        uintptr_t group = 0;
        for (uintptr_t base : bases) {
            if (!base || !mem::readable((void*)(base + game::rpg::container_off), 4))
                continue;
            uintptr_t cont = mem::ptr(base + game::rpg::container_off);
            if (!cont || !mem::readable((void*)cont, 4))
                continue;
            uintptr_t g = (uintptr_t)((AuxGetValue_t)game::group::get_object)((int)cont, keybuf);
            if (g && mem::readable((void*)(g + game::group::members_end), 4) &&
                mem::i32(g + game::group::active_off) != 0) {
                group = g;
                break;
            }
        }
        if (group) {
            uintptr_t begin = mem::ptr(group + game::group::members_begin);
            uintptr_t end = mem::ptr(group + game::group::members_end);
            for (uintptr_t p = begin; p && p < end && n < game::group::max_members; p += 4) {
                uintptr_t m = mem::ptr(p);
                if (!m || !mem::readable((void*)m, 4))
                    continue;
                // The member array is a FIXED max_members-slot block; unused slots read
                // back with a zero GameID (and no name). Skip those so `count` is the
                // number of REAL party members -- otherwise a solo player (or a small
                // party) reports the empty trailing slots and the party frame paints
                // blank "Member" rows for them.
                unsigned gid = (unsigned)mem::i32(m + game::group::member_gameid);
                if (gid == 0)
                    continue;
                raw[n].namep = mem::ptr(m + game::group::member_name);
                raw[n].gameid = gid;
                ++n;
            }
        }
        mem::g_guard_on = 0;
    }

    lua_newtable(L); // result
    lua_newtable(L); // members
    for (int i = 0; i < n; ++i) {
        lua_newtable(L); // member row
        if (raw[i].namep && mem::readable((void*)raw[i].namep, 1)) {
            std::string nm = mem::cstr(raw[i].namep); // self-guarding
            if (!nm.empty()) {
                lua_pushlstring(L, nm.data(), nm.size());
                lua_setfield(L, -2, "name");
            }
        }
        lua_pushinteger(L, (lua_Integer)raw[i].gameid);
        lua_setfield(L, -2, "gameid");
        // Per-member hull/shield: the member struct holds none, so resolve the member's
        // GameID to its live contact object and read vitals off its +tgt_container aux bag
        // EXACTLY as enb.target() does (HullPoints / MaxHullPoints absolute, shield =
        // MaxShieldPower * ShieldPercent). Only members in the current sector / scanner
        // range have a live entity; the rest stay name+id and party_frame draws their bars
        // as empty tracks. entity_by_gid + each aux_read_f are independently fault-guarded.
        uintptr_t ent = entity_by_gid(raw[i].gameid);
        if (ent && mem::readable((void*)(ent + game::world::tgt_container), 4)) {
            uintptr_t cont = mem::ptr(ent + game::world::tgt_container);
            double d;
            if (cont && aux_read_f(cont, "HullPoints", d)) {
                lua_pushnumber(L, d);
                lua_setfield(L, -2, "hull");
            }
            if (cont && aux_read_f(cont, "MaxHullPoints", d)) {
                lua_pushnumber(L, d);
                lua_setfield(L, -2, "hull_max");
            }
            double smax = -1.0, spct = -1.0;
            if (cont && aux_read_f(cont, "MaxShieldPower", d)) {
                smax = d;
                lua_pushnumber(L, d);
                lua_setfield(L, -2, "shield_max");
            }
            if (cont && aux_read_shared_f(cont, "ShieldPercent", d))
                spct = d;
            if (smax >= 0.0 && spct >= 0.0) {
                lua_pushnumber(L, smax * spct);
                lua_setfield(L, -2, "shield");
            }
        }
        lua_rawseti(L, -2, i + 1); // members[i+1] = row
    }
    lua_setfield(L, -2, "members");
    lua_pushinteger(L, n);
    lua_setfield(L, -2, "count");
    return 1;
}

// Live-read the current target's combat level off the targeting subsystem, replicating
// the native target-frame refresh (game.h::addr::TargetFrameRefresh): controller[0x13]
// -> targeting subsystem `ts` -> ts vtable+0x28 getter (__thiscall, no args) -> data
// object -> *(data + 0x88) aux container -> "TargetThreatLevel" int. Returns the level,
// or -1 when none / not-yet-populated. The whole chain (including the vtable getter
// call) runs under the VEH fault guard, so a stale controller / mid-switch object
// yields -1 instead of taking the client down.
// The local player ship object, fetched exactly as the native target-frame refresh does:
// target_ctrl[ctrl_subsys] is the targeting subsystem, whose vtable+getdata_vtoff getter
// (__thiscall, no args) returns the ship/targeting data object. This is the very object the
// camera controller calls vtable+wpos_vtoff on to compute the on-frame target distance, so it
// is a positional locatable -- its world position is the player's, and its aux (+aux_container)
// holds the live TargetThreatLevel. Both the level and the player-distance reads go through it.
// Returns 0 on miss/fault. Runs the vtable getter under the VEH guard.
static uintptr_t targeting_data_obj() {
    uintptr_t ctrl = hooks::target_ctrl();
    if (!ctrl || !mem::readable((void*)(ctrl + game::targeting::ctrl_subsys), 4))
        return 0;
    uintptr_t ts = mem::ptr(ctrl + game::targeting::ctrl_subsys);
    if (!ts || !mem::readable((void*)ts, 4))
        return 0;
    uintptr_t vt = mem::ptr(ts);
    if (!vt || !mem::readable((void*)(vt + game::targeting::getdata_vtoff), 4))
        return 0;
    uintptr_t getfn = mem::ptr(vt + game::targeting::getdata_vtoff);
    if (!getfn)
        return 0;
    uintptr_t data = 0;
    if (__builtin_setjmp(mem::g_guard_jmp)) {
        mem::g_guard_on = 0;
        return 0;
    }
    mem::g_guard_on = 1;
    data = (uintptr_t)actions::call_thiscall(getfn, ts, nullptr, 0);
    mem::g_guard_on = 0;
    return data;
}

static int target_level_live() {
    uintptr_t data = targeting_data_obj();
    if (!data)
        return -1;
    unsigned char keybuf[game::aux::keybuf_sz];
    memset(keybuf, 0, sizeof(keybuf));
    uintptr_t strptr = 0;
    if (__builtin_setjmp(mem::g_guard_jmp)) {
        mem::g_guard_on = 0;
        return -1; // faulted somewhere in the aux read
    }
    mem::g_guard_on = 1;
    uintptr_t cont = mem::ptr(data + game::targeting::aux_container);
    if (cont) {
        // The server populates the target's level as the "TargetThreat" string ("Level N"
        // for a mob; a relative word like "Even"/"Hard" for a player). It is a string-typed
        // aux entry, so resolve it with the string lookup and read the char* value.
        ((AuxBuildKey_t)game::aux::build_key)(keybuf, "TargetThreat");
        int entry = ((AuxGetValue_t)game::rpg::get_string)((int)cont, keybuf);
        if (entry && mem::i32((uintptr_t)entry + game::aux::valid_off) != 0)
            strptr = mem::ptr((uintptr_t)entry + game::aux::val_off);
    }
    mem::g_guard_on = 0;
    if (!strptr)
        return -1;
    // Parse a leading "Level N" out of the threat string; anything else (a relative
    // assessment, or empty) has no absolute level to show. mem::cstr is self-guarding.
    std::string threat = mem::cstr(strptr, 64);
    int level = -1;
    if (sscanf(threat.c_str(), "Level %d", &level) == 1 && level >= 0)
        return level;
    return -1;
}

// Read an in-space object's world position the way the client's camera controller does
// (see game.h::namespace locatable), under the VEH fault guard. The objects we pass here
// (the GameID-resolved target, and the player's ship entity) expose a struct-returning
// world-position getter at vtable+wpos_vtoff: ECX = the object, and the address of a
// 3-float output buffer is passed as the hidden return pointer. The getter writes world
// X/Y/Z to buf+wpos_x/_y/_z. Returns false on any miss/fault; on success out = {x, y, z}.
static bool world_pos(uintptr_t obj, double out[3]) {
    if (!obj || !mem::readable((void*)obj, 4))
        return false;
    uintptr_t vt = mem::ptr(obj);
    if (!vt || !mem::readable((void*)(vt + game::locatable::wpos_vtoff), 4))
        return false;
    uintptr_t wfn = mem::ptr(vt + game::locatable::wpos_vtoff);
    if (!wfn)
        return false;
    alignas(4) unsigned char buf[0x40];
    bool ok = false;
    if (__builtin_setjmp(mem::g_guard_jmp)) {
        mem::g_guard_on = 0;
        return false;
    }
    mem::g_guard_on = 1;
    memset(buf, 0, sizeof(buf));
    uint32_t args[1] = {(uint32_t)(uintptr_t)buf};
    actions::call_thiscall(wfn, obj, args, 1);
    mem::g_guard_on = 0;
    float x = *(float*)(buf + game::locatable::wpos_x);
    float y = *(float*)(buf + game::locatable::wpos_y);
    float z = *(float*)(buf + game::locatable::wpos_z);
    if (x == x && y == y && z == z) {
        out[0] = x;
        out[1] = y;
        out[2] = z;
        ok = true;
    }
    return ok;
}

// ---------------------------------------------------------------------------
// MVAS position-feed publisher (BA-2c).
//
// FreyaPosFeed.dll relays the LOCAL ship's world position + orientation to the
// server as MVAS opcode 0x1004 so other clients can render us. Its old source
// was a render-loop transform hijack that captured ANY player hull matching a
// shared engine signature -- so with a grouped teammate rendered in range it
// periodically shipped the OTHER ship's transform stamped as ours, and the
// remote ship "teleported" between two positions every few seconds.
//
// enbmod already resolves the LOCAL ship unambiguously (targeting_data_obj(),
// which the native target frame uses as "our ship") and reads it under the VEH
// fault guard, so we compute the sample here on the tick and publish it for
// FreyaPosFeed to pick up over a process-local export -- no code patching, no
// signature guessing, and never another ship's transform. See plans/57 BA-2c
// and plans/29 CV-MVAS-POS.
//
// Position comes from world_pos() (the same struct-returning locatable getter
// the target frame trusts for our position). Orientation comes from the ship's
// Ship orientation comes from the affine world transform reachable off the ship
// object: *(obj + PTR_OFF) points at a row-major 3x4 matrix at + MAT_OFF whose
// translation column (bytes 12/28/44) equals the ship world_pos and whose X
// (nose) column is bytes 0/16/32. We validate the translation column against the
// world_pos we already trust BEFORE reading the nose, so a candidate offset that
// does not resolve to this build's transform is rejected and we publish
// position-only (zero heading) rather than a wrong facing. The primary candidate
// (*(obj+0xac) + 0x1c) is the one observed live to carry the moving ship's nose;
// a second candidate covers a differing object/build layout. Both are validated,
// so listing more than one can never yield a wrong heading -- only the matching
// one is ever trusted.
struct FreyaShipSample {
    volatile unsigned int seq; // seqlock: odd = write in progress
    float pos[3];
    float heading[3];
    unsigned int sector;
    unsigned int valid; // 1 = fresh in-space sample
};
static FreyaShipSample g_ship_sample = {0, {0, 0, 0}, {0, 0, 0}, 0, 0};

// One (pointer-offset, matrix-offset) candidate for the world transform.
struct FreyaTransformCandidate {
    uintptr_t ptr_off; // *(obj + ptr_off) -> matrix base
    uintptr_t mat_off; // matrix starts at base + mat_off
};
static const FreyaTransformCandidate kFreyaTransformCandidates[] = {
    {0xac, 0x1c}, // observed live to carry the moving ship's nose (X column)
    {0x14, 0x48}, // alternate object/build layout
};

// Try to read the ship orientation from one candidate object, walking each
// (ptr_off, mat_off) transform candidate and validating the matrix translation
// column (bytes 12/28/44) against the world_pos we already trust. Returns true
// and fills heading_out with the X (nose) column (bytes 0/16/32) on the first
// validated matrix; leaves heading_out untouched otherwise. All reads are
// self-guarding (mem::ptr / mem::f32 pre-check + VEH), so a bad pointer during a
// sector change just fails the read, never faults.
static bool try_read_transform_heading(uintptr_t obj, const float pos[3], float heading_out[3]) {
    if (!obj)
        return false;
    for (const auto& cand : kFreyaTransformCandidates) {
        uintptr_t t = mem::ptr(obj + cand.ptr_off);
        if (!t)
            continue;
        uintptr_t m = t + cand.mat_off;
        if (!mem::readable((const void*)m, 48))
            continue;
        float tx = mem::f32(m + 12), ty = mem::f32(m + 28), tz = mem::f32(m + 44);
        float dx = tx - pos[0], dy = ty - pos[1], dz = tz - pos[2];
        // >2 units apart -> this matrix is not the ship world_pos resolved: don't
        // trust its orientation. Also rejects NaN (a compare with NaN is false).
        if (!(dx * dx + dy * dy + dz * dz <= 4.0f))
            continue;
        heading_out[0] = mem::f32(m + 0);
        heading_out[1] = mem::f32(m + 16);
        heading_out[2] = mem::f32(m + 32);
        return true;
    }
    return false;
}

// Best-effort ship orientation, validated against the position world_pos gave
// us. The affine transform lives on the ship ENTITY (the render-loop object), so
// try player_entity() first; fall back to the targeting data object the position
// came from. Leaves heading_out at {0,0,0} if neither candidate's matrix
// validates -- so a wrong offset yields a neutral heading, never a wrong facing.
static void read_ship_heading(uintptr_t ship, const float pos[3], float heading_out[3]) {
    heading_out[0] = heading_out[1] = heading_out[2] = 0.0f;
    if (try_read_transform_heading(player_entity(), pos, heading_out))
        return;
    try_read_transform_heading(ship, pos, heading_out);
}

// Called every tick from on_tick (the pump thread), under enbmod's fault guard.
// Resolves the local ship and publishes {pos, heading, valid} via a seqlock so
// FreyaPosFeed's feed thread can read a consistent sample without touching any
// client memory itself.
void publish_ship_state() {
    uintptr_t ship = targeting_data_obj();
    double wp[3];
    if (!ship || !world_pos(ship, wp)) {
        // No live in-space ship (loading / docked / char-select / pre-capture).
        g_ship_sample.seq++; // begin write (odd)
        __atomic_signal_fence(__ATOMIC_ACQ_REL);
        g_ship_sample.valid = 0;
        __atomic_signal_fence(__ATOMIC_ACQ_REL);
        g_ship_sample.seq++; // end write (even)
        return;
    }
    float pos[3] = {(float)wp[0], (float)wp[1], (float)wp[2]};
    float heading[3];
    read_ship_heading(ship, pos, heading);

    // seqlock write. The compiler fences keep the field stores between the two
    // seq bumps -- x86 preserves store order in hardware, so a pure compiler
    // barrier is all the reader (FreyaEnbmodShipState, other thread) needs.
    g_ship_sample.seq++; // begin write (odd)
    __atomic_signal_fence(__ATOMIC_ACQ_REL);
    g_ship_sample.pos[0] = pos[0];
    g_ship_sample.pos[1] = pos[1];
    g_ship_sample.pos[2] = pos[2];
    g_ship_sample.heading[0] = heading[0];
    g_ship_sample.heading[1] = heading[1];
    g_ship_sample.heading[2] = heading[2];
    // The proxy attributes the sample to its own session; the historical feed
    // never carried a sector id, so leave it 0.
    g_ship_sample.sector = 0;
    g_ship_sample.valid = 1;
    __atomic_signal_fence(__ATOMIC_ACQ_REL);
    g_ship_sample.seq++; // end write (even)
}

// Process-local export FreyaPosFeed.dll binds to (GetModuleHandleA("enbmod.dll")
// -> GetProcAddress). extern "C" gives it the unqualified symbol name regardless
// of this namespace nesting. Returns 1 and fills pos/heading/sector from the
// latest valid sample, or 0 when there is no live sample yet. Reads via the
// seqlock so it never returns a torn (half-updated) sample.
extern "C" __declspec(dllexport) int FreyaEnbmodShipState(float pos[3], float heading[3],
                                                          unsigned int* sector) {
    for (int tries = 0; tries < 8; ++tries) {
        unsigned int s1 = g_ship_sample.seq;
        __atomic_signal_fence(__ATOMIC_ACQ_REL);
        if (s1 & 1u)
            continue; // write in progress
        float p[3] = {g_ship_sample.pos[0], g_ship_sample.pos[1], g_ship_sample.pos[2]};
        float h[3] = {g_ship_sample.heading[0], g_ship_sample.heading[1], g_ship_sample.heading[2]};
        unsigned int sec = g_ship_sample.sector;
        unsigned int val = g_ship_sample.valid;
        __atomic_signal_fence(__ATOMIC_ACQ_REL);
        unsigned int s2 = g_ship_sample.seq;
        if (s1 != s2)
            continue; // sample changed under us -> retry
        if (!val)
            return 0;
        pos[0] = p[0];
        pos[1] = p[1];
        pos[2] = p[2];
        heading[0] = h[0];
        heading[1] = h[1];
        heading[2] = h[2];
        if (sector)
            *sector = sec;
        return 1;
    }
    return 0;
}

// enb.target_ctrl() -> int. Diagnostic: the captured targeting/HUD controller pointer
// (hooks::target_ctrl()), 0 until the target-frame refresh has run.
static int l_target_ctrl(lua_State* L) {
    lua_pushinteger(L, (lua_Integer)hooks::target_ctrl());
    return 1;
}

// enb.worldmgr() -> int. The world/player manager M, captured (ECX) by the world-
// manager initializer hook (hooks::world_mgr()). 0 until you have zoned into space this
// session. M carries the local player game id at M + world::player_id and the live
// sector-server Connection at M + world::connection -- the two things enb.target_action
// needs to build and push a target-action command. Diagnostic + the dispatch root.
static int l_worldmgr(lua_State* L) {
    lua_pushinteger(L, (lua_Integer)hooks::world_mgr());
    return 1;
}

// enb.login_task() -> int. The front-end LoginTask `this` captured read-only from
// the login run-loop hook (hooks::login_task()); 0 until the pre-game screens are
// up. The auto-login driver and live probing read the login-field/char-list state
// off it. Only non-zero when an auto-login env var armed the capture hook.
static int l_login_task(lua_State* L) {
    lua_pushinteger(L, (lua_Integer)hooks::login_task());
    return 1;
}

// enb.target_action(op[, mode]) -> bool. Perform a target-action verb on the current
// target by replaying the client's own command path: build a command object with the
// local player as actor and a target game id, then push it through M's sector-server
// Connection (game::addr::CmdSend). This is byte-for-byte the sequence the native
// target-action button / group-verb handler runs. `mode` selects the builder + target:
//   "target" (default) -- CmdBuild(player, op, <current target's game id>): the verbs
//                         that act ON the locked target (Dock 0x1c, Register 0x19,
//                         Prospect 0x11, Trade 0x14, Group 0x0a, ...). Needs a target.
//   "self"             -- CmdBuild(player, op, <player's own game id>): the verbs the
//                         client issues against the player as the command target
//                         (Tractor 0x01, Scan 0x1e -- the server reads the actor's own
//                         current target server-side). Does not require target_obj().
//   "follow"           -- AutoFollowBuild(player, op): the Follow verb (op 0x0c), which
//                         has its own builder and carries no target field (the server
//                         follows the player's current target).
//   "land"             -- planet landing. The native Land verb is a TWO-step server
//                         exchange, NOT one: op 0x1d (server case 29) only ARMS the
//                         transit -- it sets m_Gating + SetStargateDestination(planet
//                         Destination()) and waits; the actual sector handoff happens in
//                         server case 8 ("land"), which the native client sends when its
//                         client finishes the landing animation. case 8 does NOT check
//                         the player's position (only that StargateDestination is armed),
//                         so we send 0x1d then 0x08 back-to-back -- an instant transit
//                         exactly like a gate (the same two-step arm/handoff a stargate
//                         uses: 0x12 then 0x13). No fly-in; just a different animation +
//                         label from a gate. Acts on the target.
// Returns false (no send) when M is not captured yet, or -- for "target"/"land" mode --
// when nothing is targeted / the target's game id is unreadable. MUST be called on the
// game thread (it is -- callers run from on_input). The command buffer is a static zeroed
// 0x20 bytes (the builders only write buf[0..6]); single writer (game thread).
static unsigned char g_cmd_obj[0x20];
// Build CmdBuild(player, op, target, 0) into g_cmd_obj and push it via M's Connection.
static void send_target_cmd(uintptr_t m, uint32_t player, uint32_t op, uint32_t target) {
    std::memset(g_cmd_obj, 0, sizeof(g_cmd_obj));
    uint32_t b[4] = {player, op, target, 0};
    actions::call_thiscall(game::addr::CmdBuild, (unsigned)(uintptr_t)g_cmd_obj, b, 4);
    uint32_t snd[1] = {(uint32_t)(uintptr_t)g_cmd_obj};
    actions::call_thiscall(game::addr::CmdSend, m, snd, 1);
}
static int l_target_action(lua_State* L) {
    int op = (int)luaL_checkinteger(L, 1);
    const char* mode = luaL_optstring(L, 2, "target");
    uintptr_t m = hooks::world_mgr();
    if (!m || !mem::readable((void*)(m + game::world::player_id), 4)) {
        lua_pushboolean(L, 0);
        return 1;
    }
    uint32_t player = mem::u32(m + game::world::player_id);
    if (std::strcmp(mode, "follow") == 0) {
        // Follow/auto-follow: build(player, op) -- no target field on the wire.
        std::memset(g_cmd_obj, 0, sizeof(g_cmd_obj));
        uint32_t b[2] = {player, (uint32_t)op};
        actions::call_thiscall(game::addr::AutoFollowBuild, (unsigned)(uintptr_t)g_cmd_obj, b, 2);
        uint32_t snd[1] = {(uint32_t)(uintptr_t)g_cmd_obj};
        actions::call_thiscall(game::addr::CmdSend, m, snd, 1);
        lua_pushboolean(L, 1);
        return 1;
    }
    uint32_t target;
    if (std::strcmp(mode, "self") == 0) {
        target = player; // the command target is the player's own game id
    } else {
        // The target's GameID sits directly on the captured contact object at
        // +tgt_gid (the object stores its own GameID there -- the same field the
        // client's gid->object lookup validates). This is exactly what the native
        // verb dispatcher sends; it is NOT a field of the +0x88 aux/properties bag.
        uintptr_t tgt = hooks::target_obj();
        if (!tgt || !mem::readable((void*)(tgt + game::world::tgt_gid), 4)) {
            lua_pushboolean(L, 0);
            return 1;
        }
        target = mem::u32(tgt + game::world::tgt_gid);
    }
    send_target_cmd(m, player, (uint32_t)op, target);
    if (std::strcmp(mode, "land") == 0) {
        // Complete the landing: op 0x08 (server case 8) does TerminateWarp +
        // SectorServerHandoff to the destination the 0x1d we just sent armed.
        send_target_cmd(m, player, 0x08, target);
    }
    lua_pushboolean(L, 1);
    return 1;
}

// ---------------------------------------------------------------------------
// Bot API -- named high-level actions a Lua bot can call by name instead of by
// raw opcode. Every one is built on the SAME validated primitives the rest of
// this file uses: send_target_cmd (CmdBuild + CmdSend, the avatar-command path),
// the gid->object entity hash (entity_by_gid's walk), and the locatable getter
// (world_pos). No new server behaviour; these only invoke command paths the
// client already drives natively. Game-thread only (native __thiscall calls).
// ---------------------------------------------------------------------------

// Resolve (M, local player GameID, current target GameID). false if the world
// manager is not captured yet or nothing is targeted.
static bool bot_actor_and_target(uintptr_t& m, uint32_t& player, uint32_t& target) {
    m = hooks::world_mgr();
    if (!m || !mem::readable((void*)(m + game::world::player_id), 4))
        return false;
    player = mem::u32(m + game::world::player_id);
    uintptr_t tgt = hooks::target_obj();
    if (!tgt || !mem::readable((void*)(tgt + game::world::tgt_gid), 4))
        return false;
    target = mem::u32(tgt + game::world::tgt_gid);
    return true;
}

// enb.dock() -> bool. Dock at the currently-targeted station (avatar-command
// 0x1c). Target a starbase first (enb.request_target / enb.navs()); returns false
// (sends nothing) if nothing is targeted. Same path as enb.target_action(0x1c),
// live-validated end to end.
static int l_dock(lua_State* L) {
    uintptr_t m;
    uint32_t player, target;
    if (!bot_actor_and_target(m, player, target)) {
        lua_pushboolean(L, 0);
        return 1;
    }
    send_target_cmd(m, player, 0x1c, target);
    lua_pushboolean(L, 1);
    return 1;
}

// enb.register() -> bool. Register at the currently-targeted station
// (avatar-command 0x19) -- sets it as the recall/home point. Target a station
// first. Same path as enb.target_action(0x19).
static int l_register(lua_State* L) {
    uintptr_t m;
    uint32_t player, target;
    if (!bot_actor_and_target(m, player, target)) {
        lua_pushboolean(L, 0);
        return 1;
    }
    send_target_cmd(m, player, 0x19, target);
    lua_pushboolean(L, 1);
    return 1;
}

// enb.gate() -> bool. Jump through the currently-targeted stargate. The native
// gate button is a two-step avatar-command: op 0x12 arms the jump (server
// TerminateWarp + GateActivate, which stores the gate's destination) and op 0x13
// finishes it (the sector handoff to that destination). The real client sends
// 0x13 once its fly-in animation completes; back-to-back arm+finish transits
// immediately, since the finish step does not re-check position -- the same
// shortcut the "land" verb (0x1d -> 0x08) already uses. Target a stargate first.
// The fly-in-skip behaviour is tracked for real-client confirmation in plans/29.
static int l_gate(lua_State* L) {
    uintptr_t m;
    uint32_t player, target;
    if (!bot_actor_and_target(m, player, target)) {
        lua_pushboolean(L, 0);
        return 1;
    }
    send_target_cmd(m, player, 0x12, target);
    send_target_cmd(m, player, 0x13, target);
    lua_pushboolean(L, 1);
    return 1;
}

// enb.undock() -> bool. Leave the current station into space. Calls the client's
// own station-exit method (addr::StationExit, thiscall on the captured world
// manager M) with action = 1, which builds a STARBASE_REQUEST (wire opcode
// 0x004E, Action = 1) and sends it through M's Connection itself; the server then
// launches the player into space. No-op (returns false, sends nothing) when M is
// not captured or the player is not docked (M + starbase_id == 0), so calling it
// in space is harmless. Real-client station->space transition tracked in plans/29.
static int l_undock(lua_State* L) {
    uintptr_t m = hooks::world_mgr();
    if (!m || !mem::readable((void*)(m + game::world::starbase_id), 4)) {
        lua_pushboolean(L, 0);
        return 1;
    }
    if (mem::i32(m + game::world::starbase_id) == 0) {
        lua_pushboolean(L, 0); // not docked -- nothing to exit
        return 1;
    }
    uint32_t b[3] = {1u, 0u, 0u}; // action = 1 (exit station), then two ignored args
    actions::call_thiscall(game::addr::StationExit, m, b, 3);
    lua_pushboolean(L, 1);
    return 1;
}

// Structural predicate: does `radar` have the exact shape WarpPathBuild + the orb
// handler require? All reads are guarded (VEH-backed), so this never faults. The checks
// mirror the native code's own dereferences, so passing them means the native build path
// is safe to run on this object -- and a wrong candidate (garbage / not the controller)
// fails one of them and is rejected rather than hanging the client.
static bool looks_like_radar(uintptr_t radar) {
    using namespace game::addr::warp;
    // radar readable through both nav-vector ptr slots (+0x3c/+0x40). The radar is a small
    // (~0x50-byte) object, so we only touch the low fields we actually validate here.
    if (!radar || !mem::readable((void*)radar, 0x44))
        return false;
    // First member m0 (== *radar) is an SC-family object: *m0 is its vtable pointer (into
    // image .rdata, NOT necessarily executable), and the getter slot WarpPathBuild invokes
    // (m0->vtable[+0x28]) must point at real executable code.
    uintptr_t m0 = mem::ptr(radar);
    if (!m0)
        return false;
    uintptr_t vt = mem::ptr(m0);
    if (!mem::is_image_data(vt) || !mem::is_code(mem::ptr(vt + getdata_vtoff)))
        return false;
    // The targeting subsystem WarpPathBuild dereferences (*(m0 + 0x12a4)) must be present
    // (non-null, its flag byte readable) -- else the native build takes its assert branch.
    uintptr_t ts = mem::ptr(m0 + subsys);
    if (!ts || !mem::readable((void*)(ts + subsys_flag), 1))
        return false;
    // The nav-vector control blocks bound WarpPathBuild's copy loop: a garbage block yields
    // a huge (end-begin)>>2 count -> the exact hang we must never repeat. Require each block
    // readable and, when non-empty, a sane forward, 4-aligned span.
    const int vecs[2] = {nav_vec, nav_vec2};
    for (int i = 0; i < 2; ++i) {
        uintptr_t vp = mem::ptr(radar + vecs[i]);
        if (!vp || !mem::readable((void*)vp, 0x10))
            return false;
        uintptr_t b = mem::ptr(vp + vec_begin);
        uintptr_t e = mem::ptr(vp + vec_end);
        if (b && (e < b || (e - b) > 0x40000 || ((e - b) & 3)))
            return false;
    }
    return true;
}

// Resolve the warp radar controller off M by deref chain and validate its shape, with NO
// native call -- the safe alternative to a real warp-orb engage:
//     radar = *( *(M + mainview_on_m) + radar_in_mainview )
// (M -> MainView -> radar). Returns the validated radar ptr, or 0 if it cannot be trusted
// (e.g. the 3D view is not up yet, so MainView + 0x80 is still 0).
static uintptr_t resolve_warp_radar(uintptr_t m) {
    using namespace game::addr::warp;
    if (!m)
        return 0;
    uintptr_t mainview = mem::ptr(m + mainview_on_m);
    if (!mainview)
        return 0;
    uintptr_t radar = mem::ptr(mainview + radar_in_mainview);
    return looks_like_radar(radar) ? radar : 0;
}

// Engage (or, run again, terminate) warp to the CURRENT target -- the shared body
// of enb.warp / enb.warp_stop. Reproduces the native warp-orb sequence on M:
// build the nav path from the current target off the captured radar controller,
// wrap the persistent NavigationList (M + navlist) and the built nav vector into a
// WarpPacket, and send it through M's Connection (CmdSend). Warp is a server-side
// TOGGLE, so the SAME call terminates an in-progress warp -- warp and warp_stop are
// one code path. Returns false (sends nothing) when M or the radar controller is not
// yet captured, or when the path is not ready this frame (no target / still
// building) -- the caller should target a nav first (enb.request_target/enb.navs)
// and may retry next tick. reason is filled for the false cases (nil on success).
static bool do_warp(uintptr_t& out_m, const char*& reason) {
    reason = nullptr;
    uintptr_t m = hooks::world_mgr();
    if (!m || !mem::readable((void*)(m + game::world::navlist), 4)) {
        reason = "no world manager (not in space)";
        return false;
    }
    uintptr_t radar = hooks::warp_radar_ctrl();
    if (!radar) {
        // Not seeded by a real orb engage yet -- resolve + validate it off M so warp
        // works with zero screen interaction. seed only latches while unset, so a later
        // genuine WarpPathBuild capture still wins.
        radar = resolve_warp_radar(m);
        if (radar)
            hooks::seed_warp_radar_ctrl((unsigned)radar);
    }
    if (!radar || !mem::readable((void*)radar, 4)) {
        reason = "warp controller not resolvable off M (not in space, or ship half-built) "
                 "-- retry after fully in space, or engage warp once via the orb to seed it";
        return false;
    }
    out_m = m;
    // Build the warp path from the current target. The native builder returns a bool in
    // its LOW byte (the game tests `== '\0'`); a false return means "path not finished
    // building" (no nav targeted, or still building) -- do NOT send in that case. Mask to
    // the low byte so a dirty high EAX can't be misread as success.
    if ((actions::call_thiscall(game::addr::WarpPathBuild, radar, nullptr, 0) & 0xFF) == 0) {
        reason = "warp path not ready (no nav targeted, or still building)";
        return false;
    }
    // Mirror the orb handler's packet build exactly: navId is an inline VALUE (stored
    // verbatim, never dereferenced by the ctor -- so any value, incl. small ones, is
    // safe); navVec is a POINTER to the just-built nav-node vector, which the ctor CLONES
    // (so it must be non-null or the clone faults dereferencing it).
    uint32_t nav_id = mem::u32(m + game::world::navlist); // *(SC + 0x1138), inline value
    uint32_t nav_vec = actions::call_thiscall(game::addr::WarpNavVec, radar, nullptr, 0);
    if (!nav_vec) {
        reason = "warp nav vector not built";
        return false;
    }
    // Caller-owned WarpPacket storage. The ctor writes through +0x10 (needs >=0x14 bytes)
    // and installs the packet vtable; CmdSend serializes it through that vtable. 0x20 for
    // margin, zero-initialized.
    uint32_t pkt[8] = {0};
    uint32_t ctor_args[2] = {nav_id, nav_vec};
    actions::call_thiscall(game::addr::WarpPacketCtor, (uintptr_t)pkt, ctor_args, 2);
    uint32_t snd[1] = {(uint32_t)(uintptr_t)pkt};
    actions::call_thiscall(game::addr::CmdSend, m, snd, 1);
    actions::call_thiscall(game::addr::WarpPacketDtor, (uintptr_t)pkt, nullptr, 0);
    return true;
}

// enb.warp() -> bool[, string]. Engage warp to the currently-targeted nav (target
// one first with enb.request_target / enb.navs). Returns true on send, or false +
// a reason string. Because warp is a server toggle, calling this while already
// warping terminates the warp -- see enb.warp_stop, which is the same path.
static int l_warp(lua_State* L) {
    uintptr_t m = 0;
    const char* reason = nullptr;
    bool ok = do_warp(m, reason);
    lua_pushboolean(L, ok);
    if (ok) {
        return 1;
    }
    lua_pushstring(L, reason ? reason : "warp failed");
    return 2;
}

// enb.warp_stop() -> bool[, string]. Terminate an in-progress warp. Warp is a
// server-side toggle with no distinct client stop opcode, so this re-runs the exact
// warp send: the server flips warping -> TerminateWarp. Same guards/reasons as
// enb.warp (needs a resolvable path, which still holds while warping).
static int l_warp_stop(lua_State* L) {
    return l_warp(L);
}

// enb.warp_radar() -> { hooked=<ptr>, resolved=<ptr> }. Read-only diagnostic: the
// radar controller as captured by the WarpPathBuild hook (0 until a real orb engage)
// and as resolved+validated off M by resolve_warp_radar (0 if the structure does not
// check out). Makes NO native call -- purely for confirming the offset resolution is
// sound before trusting enb.warp. A non-zero resolved value means enb.warp can engage
// with zero screen interaction; equal hooked+resolved values once both are set proves
// the offset resolves to the same object the game passes.
static int l_warp_radar(lua_State* L) {
    using namespace game::addr::warp;
    uintptr_t m = hooks::world_mgr();
    lua_newtable(L);
#define WR_SET(name, val)                                                                          \
    do {                                                                                           \
        lua_pushinteger(L, (lua_Integer)(val));                                                    \
        lua_setfield(L, -2, name);                                                                 \
    } while (0)
    WR_SET("hooked", hooks::warp_radar_ctrl());
    WR_SET("m", m);
    WR_SET("resolved", m ? resolve_warp_radar(m) : 0);
    // Intermediate chain, for diagnosing a 0 resolve (all guarded reads, no native call):
    // M -> MainView -> radar.
    uintptr_t mainview = m ? mem::ptr(m + mainview_on_m) : 0;
    WR_SET("mainview", mainview);
    uintptr_t radar = mainview ? mem::ptr(mainview + radar_in_mainview) : 0;
    WR_SET("radar", radar);
    uintptr_t m0 = radar ? mem::ptr(radar) : 0;
    WR_SET("m0", m0);
    uintptr_t vt = m0 ? mem::ptr(m0) : 0;
    WR_SET("vt", vt);
    WR_SET("vt_code", vt ? (mem::is_code(vt) ? 1 : 0) : 0);
    WR_SET("getter", vt ? mem::ptr(vt + getdata_vtoff) : 0);
    WR_SET("getter_code", vt ? (mem::is_code(mem::ptr(vt + getdata_vtoff)) ? 1 : 0) : 0);
    WR_SET("ts", m0 ? mem::ptr(m0 + subsys) : 0);
    WR_SET("navvec", radar ? mem::ptr(radar + nav_vec) : 0);
    WR_SET("navvec2", radar ? mem::ptr(radar + nav_vec2) : 0);
#undef WR_SET
    return 1;
}

// enb.dist(...) -> number | nil. Straight-line distance, three call shapes:
//   enb.dist()                    -- local ship -> current target
//   enb.dist(x, y, z)             -- local ship -> a world point
//   enb.dist(x,y,z, x2,y2,z2)     -- point -> point
// The local-ship position comes through the locatable getter (world_pos on the
// same local ship object enb.target()/enb.self() distance uses). nil if a needed
// position can't be read (no target, ship not resolved yet).
static int l_dist(lua_State* L) {
    int n = lua_gettop(L);
    double a[3], b[3];
    if (n >= 6) {
        for (int i = 0; i < 3; ++i)
            a[i] = luaL_checknumber(L, 1 + i);
        for (int i = 0; i < 3; ++i)
            b[i] = luaL_checknumber(L, 4 + i);
    } else if (n >= 3) {
        for (int i = 0; i < 3; ++i)
            b[i] = luaL_checknumber(L, 1 + i);
        uintptr_t ship = targeting_data_obj();
        if (!ship || !world_pos(ship, a)) {
            lua_pushnil(L);
            return 1;
        }
    } else {
        uintptr_t ship = targeting_data_obj();
        uintptr_t tgt = hooks::target_obj();
        if (!ship || !tgt || !world_pos(ship, a) || !world_pos(tgt, b)) {
            lua_pushnil(L);
            return 1;
        }
    }
    double dx = a[0] - b[0], dy = a[1] - b[1], dz = a[2] - b[2];
    lua_pushnumber(L, std::sqrt(dx * dx + dy * dy + dz * dz));
    return 1;
}

// Case-insensitive substring test (empty needle matches everything).
static bool ci_contains(const std::string& hay, const char* needle) {
    if (!needle || !*needle)
        return true;
    std::string h = hay, n = needle;
    for (char& c : h)
        c = (char)std::tolower((unsigned char)c);
    for (char& c : n)
        c = (char)std::tolower((unsigned char)c);
    return h.find(n) != std::string::npos;
}

struct BotSnap {
    uint32_t gid;
    uintptr_t obj;
};
struct BotRow {
    uint32_t gid;
    uintptr_t base;
    std::string cls, nm;
    double x, y, z, dist;
    bool haspos, hasdist;
};

// Walk M's GameID entity hash (the same 0x101-bucket chained table entity_by_gid
// resolves single ids through) and collect every live object, enriched with its
// class name, instance name, world position and distance from the local ship.
// class_filter (nullptr = all) keeps only objects whose class name CONTAINS it
// (case-insensitive). The bucket walk runs under one fault guard (a corrupt chain
// aborts the walk, keeping what was gathered); the per-object enrichment reads all
// self-check / self-guard, so it runs outside the guard.
static void bot_gather(std::vector<BotRow>& out, const char* class_filter) {
    uintptr_t M = hooks::world_mgr();
    if (!M)
        return;
    static const int kMax = 4096;
    static BotSnap snap[kMax]; // static: no large stack frame / no alloc under guard
    int count = 0;
    if (__builtin_setjmp(mem::g_guard_jmp)) {
        mem::g_guard_on = 0; // a read faulted mid-walk -- keep what we have
    } else {
        mem::g_guard_on = 1;
        for (unsigned bucket = 0; bucket < game::world::ent_modulus && count < kMax; ++bucket) {
            uintptr_t head = M + game::world::ent_buckets + bucket * 4;
            if (!mem::readable((void*)head, 4))
                continue;
            uintptr_t node = mem::ptr(head);
            for (int hops = 0; node && hops < 512 && count < kMax; ++hops) {
                if (!mem::readable((void*)(node + game::world::ent_node_gid), 4))
                    break;
                uint32_t gid = (uint32_t)mem::i32(node + game::world::ent_node_gid);
                uintptr_t obj = mem::ptr(node + game::world::ent_node_obj);
                if (gid && obj) {
                    snap[count].gid = gid;
                    snap[count].obj = obj;
                    ++count;
                }
                if (!mem::readable((void*)(node + game::world::ent_node_next), 4))
                    break;
                node = mem::ptr(node + game::world::ent_node_next);
            }
        }
        mem::g_guard_on = 0;
    }
    double pp[3];
    bool have_pp = false;
    {
        uintptr_t ship = targeting_data_obj();
        if (ship)
            have_pp = world_pos(ship, pp);
    }
    for (int i = 0; i < count; ++i) {
        uintptr_t obj = snap[i].obj;
        if (!mem::readable((void*)obj, 4))
            continue;
        std::string cls, nm;
        uintptr_t container = mem::readable((void*)(obj + game::world::tgt_container), 4)
                                  ? mem::ptr(obj + game::world::tgt_container)
                                  : 0;
        if (container) {
            uintptr_t clsp = mem::ptr(container + 0x3c);
            if (clsp && mem::readable((void*)clsp, 1))
                cls = mem::cstr(clsp);
            uintptr_t namep = mem::ptr(container + 0x124);
            if (namep && mem::readable((void*)namep, 1))
                nm = mem::cstr(namep);
        }
        if (class_filter && (cls.empty() || !ci_contains(cls, class_filter)))
            continue;
        BotRow r{};
        r.gid = snap[i].gid;
        r.base = obj;
        r.cls = cls;
        r.nm = nm;
        double wp[3];
        r.haspos = world_pos(obj, wp);
        if (r.haspos) {
            r.x = wp[0];
            r.y = wp[1];
            r.z = wp[2];
            if (have_pp) {
                double dx = wp[0] - pp[0], dy = wp[1] - pp[1], dz = wp[2] - pp[2];
                r.dist = std::sqrt(dx * dx + dy * dy + dz * dz);
                r.hasdist = true;
            }
        }
        out.push_back(r);
    }
}

// Push a vector<BotRow> as a Lua array of {gid, base, class?, name?, x?,y?,z?, dist?}.
static void bot_push_rows(lua_State* L, const std::vector<BotRow>& rows) {
    lua_newtable(L);
    int idx = 0;
    for (const BotRow& r : rows) {
        lua_newtable(L);
        lua_pushinteger(L, (lua_Integer)(uint32_t)r.gid);
        lua_setfield(L, -2, "gid");
        lua_pushinteger(L, (lua_Integer)r.base);
        lua_setfield(L, -2, "base");
        if (!r.cls.empty()) {
            lua_pushlstring(L, r.cls.data(), r.cls.size());
            lua_setfield(L, -2, "class");
        }
        if (!r.nm.empty()) {
            lua_pushlstring(L, r.nm.data(), r.nm.size());
            lua_setfield(L, -2, "name");
        }
        if (r.haspos) {
            lua_pushnumber(L, r.x);
            lua_setfield(L, -2, "x");
            lua_pushnumber(L, r.y);
            lua_setfield(L, -2, "y");
            lua_pushnumber(L, r.z);
            lua_setfield(L, -2, "z");
        }
        if (r.hasdist) {
            lua_pushnumber(L, r.dist);
            lua_setfield(L, -2, "dist");
        }
        lua_rawseti(L, -2, ++idx);
    }
}

// enb.objects([class_filter]) -> array of every live in-scene object, each a table
// {gid, base, class, name, x, y, z, dist}. Optional class_filter keeps only
// objects whose class name contains that substring (case-insensitive), e.g.
// enb.objects("Ship") / enb.objects("Asteroid"). This is the raw scene snapshot a
// bot introspects; enb.navs() is a filtered view of it.
static int l_objects(lua_State* L) {
    const char* filter = luaL_optstring(L, 1, nullptr);
    std::vector<BotRow> rows;
    bot_gather(rows, filter);
    bot_push_rows(L, rows);
    return 1;
}

// enb.navs() -> array of navigation objects + stargates in scene, nearest-first,
// each {gid, base, class, name, x, y, z, dist}. A distance-sorted view of
// enb.objects() filtered to nav-ish class names. Feed a row's gid to
// enb.request_target(gid) to select it, then enb.warp()/enb.gate().
//
// COVERAGE CAVEAT: this returns navs that are present in the client's live entity
// hash (in scan range / already revealed). The game's full nav catalogue for a
// sector may live in a separate nav list, and the exact class strings navs carry
// are not yet live-confirmed -- run enb.objects() in a real sector to verify the
// class tokens below cover them. Do not assume enb.navs() is the complete nav set
// for a sector until that live check is done.
static const char* const kNavClassTokens[] = {"nav", "gate", "wormhole"};
static int l_navs(lua_State* L) {
    std::vector<BotRow> all;
    bot_gather(all, nullptr);
    std::vector<BotRow> rows;
    for (const BotRow& r : all) {
        for (const char* tok : kNavClassTokens) {
            if (ci_contains(r.cls, tok)) {
                rows.push_back(r);
                break;
            }
        }
    }
    std::stable_sort(rows.begin(), rows.end(), [](const BotRow& a, const BotRow& b) {
        if (a.hasdist != b.hasdist)
            return a.hasdist; // known-distance rows first
        return a.dist < b.dist;
    });
    bot_push_rows(L, rows);
    return 1;
}

// enb.loot() -> array of occupied hulk/loot cargo rows (empty table if none). Each
// row is { slot=<0-based int>, name=<string>, stack=<qty int>, tid=<ItemTemplateID>,
// tmpl=<template ptr>, asset=<template+icon-asset ptr> }; the array also carries a
// top-level `src` = the container's inventory-name ("Cargo" for a hulk), the caller's
// discriminator against a ship-inventory grid.
//
// Reads the cargo CONTAINER captured read-only from addr::CargoTemplateID
// (hooks::loot_container) and replays the three inventory accessors off it. The
// captured pointer can be stale (a closed loot window) or belong to a different
// inventory grid, so before ANY accessor call we validate structurally: the container
// header + its item-DB pointer (container+0, which the accessors dereference) must be
// readable and the precomputed slot count sane. The residual risk is a freed container
// that still passes those reads; the caller mitigates it by only asking while a
// hulk/harvestable is targeted (and enb.loot_age() reports how fresh the latch is).
// Game-thread only (the accessors are native __thiscall calls).
static int l_loot(lua_State* L) {
    lua_newtable(L); // result array -- returned empty on any bail
    uintptr_t c = hooks::loot_container();
    if (!c || !mem::readable((void*)c, 0x48))
        return 1;
    uintptr_t db = mem::ptr(c + 0); // item-DB the accessors deref via *container
    if (!db || !mem::readable((void*)db, 4))
        return 1;
    uint32_t n = mem::u32(c + game::cargo::slot_count);
    if (n == 0 || n > 256) // sane slot-count guard (a wrong object rarely lands here)
        return 1;
    uintptr_t namep = mem::ptr(c + game::cargo::inv_name_ptr);
    if (namep) {
        std::string src = mem::cstr(namep, 64);
        lua_pushstring(L, src.c_str());
        lua_setfield(L, -2, "src");
    }
    int row = 0;
    for (uint32_t slot = 0; slot < n; ++slot) {
        uint32_t a1[1] = {slot};
        uint32_t tid = actions::call_thiscall(game::addr::CargoTemplateID, c, a1, 1);
        if (tid == game::cargo::tid_empty || tid == game::cargo::tid_error)
            continue; // empty / invalid slot -- skip, exactly as the client does
        uint32_t qty = actions::call_thiscall(game::addr::CargoStackCount, c, a1, 1);
        uint32_t a2[2] = {slot, 0}; // slot, then char mode = 0 (pushed as a dword)
        uint32_t tmpl = actions::call_thiscall(game::addr::CargoTemplateAt, c, a2, 2);
        if (!tmpl || !mem::readable((void*)(uintptr_t)tmpl, 0x20))
            continue;
        std::string name;
        uintptr_t np = mem::ptr((uintptr_t)tmpl + game::cargo::tmpl_name_ptr);
        if (np)
            name = mem::cstr(np, 96);
        if (name.empty())
            name = "Unknown Item";
        uint32_t asset = mem::u32((uintptr_t)tmpl + game::cargo::tmpl_icon_asset);

        lua_newtable(L);
        lua_pushinteger(L, (lua_Integer)slot);
        lua_setfield(L, -2, "slot");
        lua_pushstring(L, name.c_str());
        lua_setfield(L, -2, "name");
        lua_pushinteger(L, (lua_Integer)qty);
        lua_setfield(L, -2, "stack");
        lua_pushinteger(L, (lua_Integer)(int32_t)tid);
        lua_setfield(L, -2, "tid");
        lua_pushinteger(L, (lua_Integer)tmpl);
        lua_setfield(L, -2, "tmpl");
        lua_pushinteger(L, (lua_Integer)asset);
        lua_setfield(L, -2, "asset");
        lua_rawseti(L, -2, ++row);
    }
    return 1;
}

// enb.loot_age() -> milliseconds since the cargo container was last latched by the
// per-slot accessor (i.e. since the loot grid last repainted), or -1 if never latched.
// The Freya loot panel uses this to decide when the captured container has gone stale
// (window closed) and should no longer be read.
static int l_loot_age(lua_State* L) {
    unsigned long t = hooks::loot_container_tick();
    if (t == 0) {
        lua_pushinteger(L, -1);
        return 1;
    }
    lua_pushinteger(L, (lua_Integer)(GetTickCount() - t));
    return 1;
}

// enb.loot_take(slot) -> bool. Loot one occupied slot out of the currently-open
// hulk/harvestable cargo grid into the player's own hold. This is the exact command
// the native loot/mining window's take emits: an INVENTORY_MOVE command (wire opcode
// 0x0027) built by game::addr::InvMoveBuild and pushed through the container-owner
// SClient's Connection (game::addr::CmdSend) -- the same "construct object, hand to
// Connection" path enb.target_action uses for the verbs. No server change and no new
// wire behaviour: the client already sends this exact packet when you click a native
// loot/mining item; we only invoke the same builder + sender from Lua.
//
// The command object is allocated with the CLIENT's operator new and deliberately
// NOT freed: the Connection send path owns it and frees it through the client CRT
// heap (a DLL-heap or static buffer would be a cross-heap free -> crash). The send
// holder is the SClient that OWNS the open container (C = *(container +
// cargo::view_client)), NOT the world-manager M -- routing an InvMove through M's
// connection does not transmit. GameID is read off that SClient (C +
// world::sub_client_gid), exactly as the native commit does.
// FromInv is derived from the captured container's inventory-name (a "Harvest..."
// container is the mining window = 0x12; a hulk "Cargo"/husk container = 0x06); the
// server's InvMove handler routes FromInv=18 to the mining/resource take and
// FromInv=6 to the husk loot take. Game-thread only (native __thiscall calls).
static int l_loot_take(lua_State* L) {
    int slot = (int)luaL_checkinteger(L, 1);
    uintptr_t m = hooks::world_mgr();
    if (!m || !mem::readable((void*)(m + game::world::player_id), 4)) {
        lua_pushboolean(L, 0);
        return 1;
    }
    uintptr_t c = hooks::loot_container();
    if (!c || !mem::readable((void*)c, 0x48)) {
        lua_pushboolean(L, 0);
        return 1;
    }
    // Bound the slot against the container's own precomputed slot count so a bad
    // index can never reach the builder.
    uint32_t n = mem::u32(c + game::cargo::slot_count);
    if (n == 0 || n > 256 || slot < 0 || (uint32_t)slot >= n) {
        lua_pushboolean(L, 0);
        return 1;
    }
    // FromInv: mining window (harvestable resource) vs loot/husk window, off the name.
    uint32_t from_inv = game::cargo::inv_type_hulk;
    std::string src;
    uintptr_t namep = mem::ptr(c + game::cargo::inv_name_ptr);
    if (namep) {
        src = mem::cstr(namep, 64);
        if (src.rfind("Harvest", 0) == 0)
            from_inv = game::cargo::inv_type_harvest;
    }
    uint32_t m_gid = mem::readable((void*)(m + game::world::sub_client_gid), 4)
                         ? mem::u32(m + game::world::sub_client_gid)
                         : 0xDEADBEEF;
    // Holder: the verb path (enb.target_action, live-validated) sends through M's
    // Connection (M + 0x1124), so M is a proven CmdSend holder. GameID for the
    // resource (FromInv=18) path is IGNORED by the server (it keys off the target),
    // so use M's own GameID -- valid and non-zero.
    uint32_t gid = (m_gid != 0xDEADBEEF) ? m_gid : 0;
    uint32_t sz[1] = {game::cargo::inv_move_size};
    uintptr_t obj = actions::call_cdecl(game::addr::ClientOperatorNew, sz, 1);
    if (!obj) {
        lua_pushboolean(L, 0);
        return 1;
    }
    uint32_t b[6] = {gid, from_inv, (uint32_t)slot, 1u, 0xFFFFFFFFu, 1u};
    actions::call_thiscall(game::addr::InvMoveBuild, obj, b, 6);
    uint32_t snd[1] = {(uint32_t)obj};
    actions::call_thiscall(game::addr::CmdSend, m, snd, 1);
    lua_pushboolean(L, 1);
    return 1;
}

// enb.request_target(gid) -> bool. Make the given GameID the local player's target,
// exactly as clicking that object in space does. We resolve the GameID to its live
// contact object (entity_by_gid -- the same gid->object hash walk enb.group uses for
// member hull/shield) and hand it to the client's own target-request call
// (addr::RequestTarget). That builds a REQUEST_TARGET (wire opcode 0x17) packet from
// the object's GameID and pushes it through M's sector-server Connection; the server
// validates the target is real / in range and replies SET_TARGET (0x19), which the
// client applies natively. Returns false -- and sends NOTHING -- when M is not
// captured yet or the GameID has no live entity in this sector (a group member in
// another sector / not yet in scene), which is precisely the "skip if not targetable"
// case the party frame wants. Game-thread only (callers run from on_input);
// entity_by_gid is fault-guarded, and the native call only touches M and the object
// that walk just validated (same posture as enb.target_action / enb.group_action).
static int l_request_target(lua_State* L) {
    unsigned gid = (unsigned)(uint32_t)luaL_checkinteger(L, 1);
    uintptr_t m = hooks::world_mgr();
    if (!m) {
        lua_pushboolean(L, 0);
        return 1;
    }
    uintptr_t obj = entity_by_gid(gid);
    if (!obj) {
        lua_pushboolean(L, 0); // no live entity for this GameID -- not targetable, skip
        return 1;
    }
    // RequestTarget is __cdecl(M, obj): it reads M+0x112c (our source GameID) and
    // obj+0x90 (the target's GameID), builds REQUEST_TARGET, and sends. BOTH operands
    // come off the stack -- it does NOT take M in ECX. Calling it thiscall-with-one-arg
    // left param_2 (obj) pointing at an uninitialized stack slot, so obj+0x90 faulted
    // the moment a party-member row was clicked. Pass both on the stack, in order.
    uint32_t args[2] = {(uint32_t)m, (uint32_t)obj};
    actions::call_cdecl(game::addr::RequestTarget, args, 2);
    lua_pushboolean(L, 1);
    return 1;
}

// enb.group_action(action [, target_gid]) -> bool. Send a group call-to-arms /
// formation request (wire opcode 0x00BC, CTARequest{SourceID, TargetID, Action}) --
// the packet behind the native group window's Formation and "Target my target"
// controls. Built with the client's own request constructor (addr::CtaBuild) into
// the shared command buffer and pushed through M's Connection exactly like
// enb.target_action. action codes (the server's GroupAction switch): 4 Slot Back,
// 5 Block, 6 Pipe (set formation, leader-only), 7 Form Up, 8 Leave Formation,
// 9 Break Formation (leader), 12 ask the group to target my target. target_gid
// defaults to -1 = the whole group (the constructor's own default). Returns false
// (no send) when M is not captured yet. Game-thread only (callers run from
// on_input), same single-writer g_cmd_obj contract as send_target_cmd.
static int l_group_action(lua_State* L) {
    int action = (int)luaL_checkinteger(L, 1);
    uint32_t target = (uint32_t)(int32_t)luaL_optinteger(L, 2, -1);
    uintptr_t m = hooks::world_mgr();
    if (!m || !mem::readable((void*)(m + game::world::player_id), 4)) {
        lua_pushboolean(L, 0);
        return 1;
    }
    uint32_t player = mem::u32(m + game::world::player_id);
    std::memset(g_cmd_obj, 0, sizeof(g_cmd_obj));
    uint32_t b[3] = {player, target, (uint32_t)action};
    actions::call_thiscall(game::addr::CtaBuild, (unsigned)(uintptr_t)g_cmd_obj, b, 3);
    uint32_t snd[1] = {(uint32_t)(uintptr_t)g_cmd_obj};
    actions::call_thiscall(game::addr::CmdSend, m, snd, 1);
    lua_pushboolean(L, 1);
    return 1;
}

// enb.is_leader() -> bool. Whether the local player LEADS their current group --
// the client's own leader check (group::is_leader), the same one the native group
// window runs to choose GRP DISBAND (leader) vs GRP LEAVE (member). Called with
// the same candidate avatar bases the roster read (enb.group) walks; false when
// solo, member, out of space, or before the bases are captured. VEH-guarded like
// every other native call.
static int l_is_leader(lua_State* L) {
    uintptr_t bases[2] = {hooks::rpg_mgr(), player_entity()};
    unsigned char keybuf[game::aux::keybuf_sz];
    memset(keybuf, 0, sizeof(keybuf));
    int lead = 0;
    if (__builtin_setjmp(mem::g_guard_jmp)) {
        mem::g_guard_on = 0; // the native check faulted: report not-leader
    } else {
        mem::g_guard_on = 1;
        ((AuxBuildKey_t)game::aux::build_key)(keybuf, "GroupInfo");
        for (uintptr_t base : bases) {
            if (!base || !mem::readable((void*)(base + game::rpg::container_off), 4))
                continue;
            uintptr_t cont = mem::ptr(base + game::rpg::container_off);
            if (!cont || !mem::readable((void*)cont, 4))
                continue;
            // Only run the native leader check once a VALID, ACTIVE group object
            // actually exists for this base. In the brief window right after a group
            // INVITE the client already holds a GroupInfo entry (the invitee's GroupID
            // is set server-side before they accept), but its internal pointers are not
            // yet constructed. Calling native is_leader then walks that half-built
            // object and dispatches through an uninitialized vtable -- control flow
            // jumps into unmapped/zeroed memory, which the VEH read-guard below CANNOT
            // recover (it only catches read/write faults, not a wild jump), so the
            // client crashed the instant the invite arrived. Gate on the same signals
            // the roster read (l_group) uses -- group active flag set and member-array
            // end readable -- so we never touch a group the client is still building.
            uintptr_t g = (uintptr_t)((AuxGetValue_t)game::group::get_object)((int)cont, keybuf);
            if (!g || !mem::readable((void*)(g + game::group::members_end), 4) ||
                mem::i32(g + game::group::active_off) == 0)
                continue;
            // The check returns a char in AL -- the rest of EAX is callee scratch,
            // so mask to the low byte before testing.
            if ((actions::call_thiscall(game::group::is_leader, base, nullptr, 0) & 0xff) != 0) {
                lead = 1;
                break;
            }
        }
        mem::g_guard_on = 0;
    }
    lua_pushboolean(L, lead);
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
// through the game's own dispatcher (game::addr::PdaSwitch == 0x00695780),
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
// == 0x00565f30) just stores the pending id at shell+0x108, and the shell's own
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
// the native chat-input box does (mirrors 0x0065ccd0), so the Freya chat box owns
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
    int alpha = (int)luaL_optinteger(L, 6, 255);
    overlay::text(x, y, s, rgb, scale, alpha);
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
// enb.key_down(vk) -> true if the physical key is currently held. Backed by
// GetAsyncKeyState so it reflects the real hardware state at call time -- unlike
// a KEYDOWN/KEYUP latch, it cannot get stuck when a KEYUP is dropped (e.g. a
// sector transition steals window focus mid-chord). Used by the Ctrl+U toggle.
static int l_key_down(lua_State* L) {
    int vk = (int)luaL_checkinteger(L, 1);
    lua_pushboolean(L, (GetAsyncKeyState(vk) & 0x8000) != 0);
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
// enb.strbuf(s) -> address of a persistent client-memory copy of the string.
// collect_args marshals only integers, so a game function that takes a char*
// (e.g. a UI edit-control text setter) cannot be fed a Lua string directly. This
// copies the bytes into one of a small rotating pool of static buffers (so a few
// pending arguments do not clobber each other) and returns the buffer address,
// which IS a valid pointer inside client.exe. Truncates to the buffer size.
static int l_strbuf(lua_State* L) {
    size_t len = 0;
    const char* s = luaL_checklstring(L, 1, &len);
    static char pool[8][256];
    static int slot = 0;
    char* b = pool[slot];
    slot = (slot + 1) & 7;
    if (len > sizeof(pool[0]) - 1)
        len = sizeof(pool[0]) - 1;
    memcpy(b, s, len);
    b[len] = '\0';
    lua_pushinteger(L, (lua_Integer)(uintptr_t)b);
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
    A(CmdBuild);
    A(AutoFollowBuild);
    A(CmdSend);
    A(CtaBuild);
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
    A(WarpPathBuild);
    A(WarpNavVec);
    A(WarpPacketCtor);
    A(WarpPacketDtor);
    A(KeyDefinitions);
    A(BindCategories);
    A(AbilitySlots);
    A(SkillLifecycle);
    A(SkillButton);
    A(MsgPump_Get);
    A(MsgPump_Peek);
    A(CargoTemplateID);
    A(CargoStackCount);
    A(CargoTemplateAt);
    A(InvMoveBuild);
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
                                   {"target_ctrl", l_target_ctrl},
                                   {"worldmgr", l_worldmgr},
                                   {"target_action", l_target_action},
                                   {"request_target", l_request_target},
                                   {"dock", l_dock},
                                   {"register", l_register},
                                   {"gate", l_gate},
                                   {"undock", l_undock},
                                   {"warp", l_warp},
                                   {"warp_stop", l_warp_stop},
                                   {"warp_radar", l_warp_radar},
                                   {"dist", l_dist},
                                   {"objects", l_objects},
                                   {"navs", l_navs},
                                   {"loot", l_loot},
                                   {"loot_age", l_loot_age},
                                   {"loot_take", l_loot_take},
                                   {"group_action", l_group_action},
                                   {"is_leader", l_is_leader},
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
                                   {"login_task", l_login_task},
                                   {"aux", l_aux},
                                   {"aux_i", l_aux_i},
                                   {"rpg_level", l_rpg_level},
                                   {"rpg_mgr", l_rpg_mgr},
                                   {"group", l_group},
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
                                   {"key_down", l_key_down},
                                   {"char", l_char},
                                   {"call", l_call},
                                   {"call_cdecl", l_call_cdecl},
                                   {"strbuf", l_strbuf},
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
