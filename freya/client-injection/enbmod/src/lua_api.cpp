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
#include <string>

extern "C" {
#include "lua.h"
#include "lauxlib.h"
#include "lualib.h"
}

namespace enb { namespace lua {

using namespace enb::game;

// Registered Lua callbacks, held as registry refs.
static std::vector<int> g_tick_refs;
static int g_skill_ref = LUA_NOREF;
static int g_chat_ref  = LUA_NOREF;

// Events are produced inside game-function hooks; we queue and flush them on the tick so all
// Lua execution happens from one place (the tick), never re-entrantly mid-game-call.
struct Event { int kind; unsigned a, b; }; // kind: 0 skill, 1 chat
static std::mutex g_evq_mx;
static std::deque<Event> g_evq;

// =====================================================================================
// enb.mem.*  -- raw guarded memory access
// =====================================================================================
static int l_r_u8 (lua_State* L){ lua_pushinteger(L, mem::u8 ((uintptr_t)luaL_checkinteger(L,1))); return 1; }
static int l_r_u16(lua_State* L){ lua_pushinteger(L, mem::u16((uintptr_t)luaL_checkinteger(L,1))); return 1; }
static int l_r_u32(lua_State* L){ lua_pushinteger(L, mem::u32((uintptr_t)luaL_checkinteger(L,1))); return 1; }
static int l_r_i32(lua_State* L){ lua_pushinteger(L, mem::i32((uintptr_t)luaL_checkinteger(L,1))); return 1; }
static int l_r_f32(lua_State* L){ lua_pushnumber (L, mem::f32((uintptr_t)luaL_checkinteger(L,1))); return 1; }
static int l_r_f64(lua_State* L){ lua_pushnumber (L, mem::f64((uintptr_t)luaL_checkinteger(L,1))); return 1; }
static int l_r_ptr(lua_State* L){ lua_pushinteger(L, mem::ptr((uintptr_t)luaL_checkinteger(L,1))); return 1; }

static int l_r_str(lua_State* L){
    uintptr_t a = (uintptr_t)luaL_checkinteger(L,1);
    size_t cap = (size_t)luaL_optinteger(L,2,512);
    std::string s = mem::cstr(a, cap);
    lua_pushlstring(L, s.data(), s.size()); return 1;
}
static int l_r_wstr(lua_State* L){
    uintptr_t a = (uintptr_t)luaL_checkinteger(L,1);
    size_t cap = (size_t)luaL_optinteger(L,2,512);
    std::string s = mem::wstr(a, cap);
    lua_pushlstring(L, s.data(), s.size()); return 1;
}
static int l_readable(lua_State* L){
    uintptr_t a = (uintptr_t)luaL_checkinteger(L,1);
    size_t n = (size_t)luaL_optinteger(L,2,1);
    lua_pushboolean(L, mem::readable((void*)a, n)); return 1;
}
static int l_w_u32(lua_State* L){
    uintptr_t a = (uintptr_t)luaL_checkinteger(L,1);
    uint32_t v = (uint32_t)luaL_checkinteger(L,2);
    lua_pushboolean(L, mem::write<uint32_t>(a, v)); return 1;
}
static int l_w_f32(lua_State* L){
    uintptr_t a = (uintptr_t)luaL_checkinteger(L,1);
    float v = (float)luaL_checknumber(L,2);
    lua_pushboolean(L, mem::write<float>(a, v)); return 1;
}

// Pointer-chain helper: enb.mem.chain(base, off1, off2, ...) -> final address (0 on break)
static int l_chain(lua_State* L){
    uintptr_t base = (uintptr_t)luaL_checkinteger(L,1);
    int n = lua_gettop(L) - 1;
    std::vector<int> offs(n);
    for (int i=0;i<n;i++) offs[i] = (int)luaL_checkinteger(L, i+2);
    lua_pushinteger(L, (lua_Integer)mem::chain(base, offs.data(), n)); return 1;
}

// =====================================================================================
// enb.calibrate{...} / enb.offsets()  -- set/get the runtime offsets table
// =====================================================================================
#define SET_INT(field) do{ lua_getfield(L,1,#field); if(!lua_isnil(L,-1)) o.field=(int)luaL_checkinteger(L,-1); lua_pop(L,1);}while(0)
#define SET_PTR(field) do{ lua_getfield(L,1,#field); if(!lua_isnil(L,-1)) o.field=(uintptr_t)luaL_checkinteger(L,-1); lua_pop(L,1);}while(0)

static int l_calibrate(lua_State* L){
    luaL_checktype(L,1,LUA_TTABLE);
    Offsets& o = offs();
    SET_PTR(player_ptr_addr);
    SET_INT(hull); SET_INT(hull_max);
    SET_INT(shield); SET_INT(shield_max);
    SET_INT(energy); SET_INT(energy_max);
    SET_INT(combat_lvl); SET_INT(trade_lvl); SET_INT(explore_lvl);
    SET_INT(combat_pct); SET_INT(trade_pct); SET_INT(explore_pct);
    SET_INT(skill_points);
    SET_INT(pos_x); SET_INT(pos_y); SET_INT(pos_z);
    SET_PTR(target_ptr_addr);
    SET_INT(tgt_name); SET_INT(tgt_name_is_ptr); SET_INT(tgt_name_wide);
    SET_INT(tgt_hull); SET_INT(tgt_pos_x); SET_INT(tgt_pos_y); SET_INT(tgt_pos_z);
    return 0;
}
#undef SET_INT
#undef SET_PTR

static int l_offsets(lua_State* L){
    Offsets& o = offs();
    lua_newtable(L);
    #define PUT(f) lua_pushinteger(L,o.f); lua_setfield(L,-2,#f)
    PUT(player_ptr_addr);
    PUT(hull);PUT(hull_max);PUT(shield);PUT(shield_max);PUT(energy);PUT(energy_max);
    PUT(combat_lvl);PUT(trade_lvl);PUT(explore_lvl);
    PUT(combat_pct);PUT(trade_pct);PUT(explore_pct);PUT(skill_points);
    PUT(pos_x);PUT(pos_y);PUT(pos_z);
    PUT(target_ptr_addr);PUT(tgt_name);PUT(tgt_name_is_ptr);PUT(tgt_name_wide);
    PUT(tgt_hull);PUT(tgt_pos_x);PUT(tgt_pos_y);PUT(tgt_pos_z);
    #undef PUT
    return 1;
}

// =====================================================================================
// High-level reads:  enb.self()  /  enb.target()
// =====================================================================================
// resolve the player object base from offsets.player_ptr_addr (an address that HOLDS the ptr)
static uintptr_t player_base() {
    Offsets& o = offs();
    if (!o.player_ptr_addr) return 0;
    return mem::ptr(o.player_ptr_addr);
}

// push field `field`-bytes into the object as an int; skip (leave nil) if off < 0 or base 0
static void push_int_field(lua_State* L, const char* key, uintptr_t base, int off) {
    if (!base || off < 0) return;
    lua_pushinteger(L, mem::i32(base + off));
    lua_setfield(L, -2, key);
}
static void push_flt_field(lua_State* L, const char* key, uintptr_t base, int off) {
    if (!base || off < 0) return;
    lua_pushnumber(L, mem::f32(base + off));
    lua_setfield(L, -2, key);
}

static int l_self(lua_State* L){
    Offsets& o = offs();
    uintptr_t b = player_base();
    lua_newtable(L);
    lua_pushinteger(L, (lua_Integer)b); lua_setfield(L,-2,"base");
    if (!b) return 1; // empty-ish table with base=0 -> Lua side treats as "not calibrated"
    push_int_field(L,"hull",       b,o.hull);
    push_int_field(L,"hull_max",   b,o.hull_max);
    push_int_field(L,"shield",     b,o.shield);
    push_int_field(L,"shield_max", b,o.shield_max);
    push_int_field(L,"energy",     b,o.energy);
    push_int_field(L,"energy_max", b,o.energy_max);
    push_int_field(L,"combat_lvl", b,o.combat_lvl);
    push_int_field(L,"trade_lvl",  b,o.trade_lvl);
    push_int_field(L,"explore_lvl",b,o.explore_lvl);
    push_int_field(L,"combat_pct", b,o.combat_pct);
    push_int_field(L,"trade_pct",  b,o.trade_pct);
    push_int_field(L,"explore_pct",b,o.explore_pct);
    push_int_field(L,"skill_points",b,o.skill_points);
    push_flt_field(L,"x",          b,o.pos_x);
    push_flt_field(L,"y",          b,o.pos_y);
    push_flt_field(L,"z",          b,o.pos_z);
    return 1;
}

static int l_target(lua_State* L){
    Offsets& o = offs();
    if (!o.target_ptr_addr) { lua_pushnil(L); return 1; }
    uintptr_t t = mem::ptr(o.target_ptr_addr);
    if (!t) { lua_pushnil(L); return 1; }
    lua_newtable(L);
    lua_pushinteger(L,(lua_Integer)t); lua_setfield(L,-2,"base");
    if (o.tgt_name >= 0) {
        uintptr_t namep = o.tgt_name_is_ptr ? mem::ptr(t + o.tgt_name) : (t + o.tgt_name);
        std::string nm = o.tgt_name_wide ? mem::wstr(namep) : mem::cstr(namep);
        lua_pushlstring(L, nm.data(), nm.size()); lua_setfield(L,-2,"name");
    }
    push_int_field(L,"hull", t,o.tgt_hull);
    push_flt_field(L,"x",    t,o.tgt_pos_x);
    push_flt_field(L,"y",    t,o.tgt_pos_y);
    push_flt_field(L,"z",    t,o.tgt_pos_z);
    // convenience: distance from self, if both positions known
    uintptr_t b = player_base();
    if (b && o.pos_x>=0 && o.tgt_pos_x>=0) {
        double dx=mem::f32(b+o.pos_x)-mem::f32(t+o.tgt_pos_x);
        double dy=mem::f32(b+o.pos_y)-mem::f32(t+o.tgt_pos_y);
        double dz=mem::f32(b+o.pos_z)-mem::f32(t+o.tgt_pos_z);
        lua_pushnumber(L, (dx*dx+dy*dy+dz*dz)>0?  __builtin_sqrt(dx*dx+dy*dy+dz*dz):0.0);
        lua_setfield(L,-2,"distance");
    }
    return 1;
}

// =====================================================================================
// callbacks: enb.on_tick / enb.on_skill / enb.on_chat / enb.enable_event_hooks
// =====================================================================================
// Release every registry ref we hold and clear the lists, so a hot-reload starts from a clean
// slate instead of stacking a second (third, …) copy of every on_tick handler.
void reset_callbacks(lua_State* L){
    for (int ref : g_tick_refs) luaL_unref(L, LUA_REGISTRYINDEX, ref);
    g_tick_refs.clear();
    if (g_skill_ref != LUA_NOREF) { luaL_unref(L, LUA_REGISTRYINDEX, g_skill_ref); g_skill_ref = LUA_NOREF; }
    if (g_chat_ref  != LUA_NOREF) { luaL_unref(L, LUA_REGISTRYINDEX, g_chat_ref);  g_chat_ref  = LUA_NOREF; }
    { std::lock_guard<std::mutex> lk(g_evq_mx); g_evq.clear(); }
}

static int l_on_tick(lua_State* L){
    luaL_checktype(L,1,LUA_TFUNCTION);
    lua_pushvalue(L,1);
    g_tick_refs.push_back(luaL_ref(L, LUA_REGISTRYINDEX));
    return 0;
}
static int l_on_skill(lua_State* L){
    luaL_checktype(L,1,LUA_TFUNCTION);
    if (g_skill_ref!=LUA_NOREF) luaL_unref(L,LUA_REGISTRYINDEX,g_skill_ref);
    lua_pushvalue(L,1); g_skill_ref = luaL_ref(L,LUA_REGISTRYINDEX);
    return 0;
}
static int l_on_chat(lua_State* L){
    luaL_checktype(L,1,LUA_TFUNCTION);
    if (g_chat_ref!=LUA_NOREF) luaL_unref(L,LUA_REGISTRYINDEX,g_chat_ref);
    lua_pushvalue(L,1); g_chat_ref = luaL_ref(L,LUA_REGISTRYINDEX);
    return 0;
}
static int l_enable_event_hooks(lua_State* L){
    lua_pushboolean(L, hooks::enable_event_hooks()); return 1;
}

static int l_log(lua_State* L){
    logs(luaL_checkstring(L,1)); return 0;
}

// =====================================================================================
// enb.draw.*  -- overlay display list (immediate-mode, rebuilt each tick)
// =====================================================================================
static int l_draw_text(lua_State* L){
    int x=(int)luaL_checkinteger(L,1), y=(int)luaL_checkinteger(L,2);
    const char* s=luaL_checkstring(L,3);
    uint32_t rgb=(uint32_t)luaL_optinteger(L,4,0xFFFFFF);
    overlay::text(x,y,s,rgb); return 0;
}
static int l_draw_rect(lua_State* L){
    int x=(int)luaL_checkinteger(L,1), y=(int)luaL_checkinteger(L,2);
    int w=(int)luaL_checkinteger(L,3), h=(int)luaL_checkinteger(L,4);
    uint32_t rgb=(uint32_t)luaL_optinteger(L,5,0xFFFFFF);
    bool filled=lua_toboolean(L,6);
    overlay::rect(x,y,w,h,rgb,filled); return 0;
}
static int l_draw_line(lua_State* L){
    overlay::line((int)luaL_checkinteger(L,1),(int)luaL_checkinteger(L,2),
                  (int)luaL_checkinteger(L,3),(int)luaL_checkinteger(L,4),
                  (uint32_t)luaL_optinteger(L,5,0xFFFFFF)); return 0;
}
static int l_draw_image(lua_State* L){
    const char* p=luaL_checkstring(L,1);
    int x=(int)luaL_checkinteger(L,2), y=(int)luaL_checkinteger(L,3);
    int w=(int)luaL_optinteger(L,4,0), h=(int)luaL_optinteger(L,5,0);
    int a=(int)luaL_optinteger(L,6,255);
    overlay::image(p,x,y,w,h,a); return 0;
}

// =====================================================================================
// enb.tap / enb.key / enb.char / enb.call(_cdecl)  -- actions
// =====================================================================================
static int l_tap(lua_State* L){ lua_pushboolean(L, actions::tap_key((int)luaL_checkinteger(L,1))); return 1; }
static int l_key(lua_State* L){
    int vk=(int)luaL_checkinteger(L,1);
    if (lua_isnoneornil(L,2)) { lua_pushboolean(L, actions::tap_key(vk)); }
    else { lua_pushboolean(L, actions::post_key(vk, lua_toboolean(L,2))); }
    return 1;
}
static int l_char(lua_State* L){ lua_pushboolean(L, actions::post_char((unsigned)luaL_checkinteger(L,1))); return 1; }

static int collect_args(lua_State* L, int from, uint32_t* out){
    int n=0;
    for (int i=from; i<=lua_gettop(L) && n<8; ++i) out[n++]=(uint32_t)luaL_checkinteger(L,i);
    return n;
}
// enb.call(addr, this, a, b, ...) -> eax  (__thiscall)
static int l_call(lua_State* L){
    uintptr_t fn=(uintptr_t)luaL_checkinteger(L,1);
    uintptr_t self=(uintptr_t)luaL_checkinteger(L,2);
    uint32_t a[8]; int n=collect_args(L,3,a);
    lua_pushinteger(L,(lua_Integer)actions::call_thiscall(fn,self,a,n)); return 1;
}
// enb.call_cdecl(addr, a, b, ...) -> eax
static int l_call_cdecl(lua_State* L){
    uintptr_t fn=(uintptr_t)luaL_checkinteger(L,1);
    uint32_t a[8]; int n=collect_args(L,2,a);
    lua_pushinteger(L,(lua_Integer)actions::call_cdecl(fn,a,n)); return 1;
}
static int l_hwnd(lua_State* L){ lua_pushinteger(L,(lua_Integer)(uintptr_t)actions::game_hwnd()); return 1; }

// =====================================================================================
// registration
// =====================================================================================
static void push_addr_table(lua_State* L){
    lua_newtable(L);
    #define A(name) lua_pushinteger(L, (lua_Integer)addr::name); lua_setfield(L,-2,#name)
    A(StatBlock);A(EnergyBar);A(HullPoints);A(VitalsBars);
    A(LevelText);A(XpBars);A(RpgLevels);
    A(TargetInfo);A(TargetPanel);
    A(ChatGadget);A(ChatRender);A(ChatChannel);A(ChatSend);
    A(NavListBuild);A(NavListRender);A(WarpPath);
    A(KeyDefinitions);A(BindCategories);
    A(AbilitySlots);A(SkillLifecycle);A(SkillButton);
    A(MsgPump_Get);A(MsgPump_Peek);
    #undef A
}

void open(lua_State* L){
    luaL_openlibs(L);

    lua_newtable(L); // enb

    lua_pushinteger(L,(lua_Integer)kImageBase); lua_setfield(L,-2,"base");

    static const luaL_Reg fns[] = {
        {"log", l_log},
        {"calibrate", l_calibrate},
        {"offsets", l_offsets},
        {"self", l_self},
        {"target", l_target},
        {"on_tick", l_on_tick},
        {"on_skill", l_on_skill},
        {"on_chat", l_on_chat},
        {"enable_event_hooks", l_enable_event_hooks},
        {"tap", l_tap},
        {"key", l_key},
        {"char", l_char},
        {"call", l_call},
        {"call_cdecl", l_call_cdecl},
        {"hwnd", l_hwnd},
        {nullptr,nullptr}
    };
    luaL_setfuncs(L, fns, 0);

    // enb.draw subtable
    lua_newtable(L);
    static const luaL_Reg drawfns[] = {
        {"text",l_draw_text},{"rect",l_draw_rect},{"line",l_draw_line},{"image",l_draw_image},
        {nullptr,nullptr}
    };
    luaL_setfuncs(L, drawfns, 0);
    lua_setfield(L,-2,"draw");

    // enb.mem subtable
    lua_newtable(L);
    static const luaL_Reg memfns[] = {
        {"u8",l_r_u8},{"u16",l_r_u16},{"u32",l_r_u32},{"i32",l_r_i32},
        {"f32",l_r_f32},{"f64",l_r_f64},{"ptr",l_r_ptr},
        {"str",l_r_str},{"wstr",l_r_wstr},{"readable",l_readable},
        {"chain",l_chain},{"write_u32",l_w_u32},{"write_f32",l_w_f32},
        {nullptr,nullptr}
    };
    luaL_setfuncs(L, memfns, 0);
    lua_setfield(L,-2,"mem");

    // enb.addr subtable
    push_addr_table(L);
    lua_setfield(L,-2,"addr");

    lua_setglobal(L,"enb");

    // wire the game-function hooks to enqueue events for the tick
    hooks::set_on_skill([](unsigned a, unsigned b){
        std::lock_guard<std::mutex> lk(g_evq_mx); g_evq.push_back({0,a,b}); });
    hooks::set_on_chat([](unsigned a, unsigned b){
        std::lock_guard<std::mutex> lk(g_evq_mx); g_evq.push_back({1,a,b}); });
}

static void call_ref(lua_State* L, int ref, int nargs){
    // expects nargs already on stack above the function slot; we insert the function below them
    lua_rawgeti(L, LUA_REGISTRYINDEX, ref);
    if (nargs) lua_insert(L, -1-nargs);
    if (lua_pcall(L, nargs, 0, 0) != LUA_OK){
        logf("lua callback error: %s", lua_tostring(L,-1));
        lua_pop(L,1);
    }
}

void tick(lua_State* L){
    // drain queued events first
    std::deque<Event> local;
    { std::lock_guard<std::mutex> lk(g_evq_mx); local.swap(g_evq); }
    for (auto& e : local){
        int ref = e.kind==0 ? g_skill_ref : g_chat_ref;
        if (ref==LUA_NOREF) continue;
        lua_pushinteger(L,e.a); lua_pushinteger(L,e.b);
        call_ref(L, ref, 2);
    }
    // overlay: rebuild the display list this frame (enb.draw.* calls land in staging),
    // then commit it for the Flip hook to render.
    overlay::begin_frame();
    for (int ref : g_tick_refs) call_ref(L, ref, 0);
    overlay::commit_frame();
}

void on_skill(unsigned a, unsigned b){ std::lock_guard<std::mutex> lk(g_evq_mx); g_evq.push_back({0,a,b}); }
void on_chat (unsigned a, unsigned b){ std::lock_guard<std::mutex> lk(g_evq_mx); g_evq.push_back({1,a,b}); }

}} // namespace enb::lua
