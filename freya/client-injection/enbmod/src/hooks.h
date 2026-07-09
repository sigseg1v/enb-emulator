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

// Front-end LoginTask capture (env-driven auto-login). enable_login_hook()
// installs a READ-ONLY capture hook on the front-end run loop
// (game::addr::LoginRunLoop) -- called by autologin::init() only when an
// auto-login env var is set, so an ordinary launch never gains it. login_task()
// returns the captured LoginTask `this` (0 until the run loop has fired, i.e.
// until the pre-game screens are up); autologin.cpp drives login off it.
bool enable_login_hook();
unsigned login_task();

// ECX (this) captured from the most recent vitals-updater call: the live root of
// the hull/shield/energy gadget chain. 0 until the hook has fired in space.
// Calibration tooling (autocalib) uses this instead of scanning memory.
unsigned vitals_ctrl();

// ECX (this) captured from the most recent RPG level-reader call: the RPG manager
// that holds the RPGInfo AuxData container (discipline levels). 0 until the hook
// (installed by enable_event_hooks) has fired. lua_api reads levels off this.
unsigned rpg_mgr();

// ECX (this) captured from the most recent XpBars-updater call: the discipline
// XP-bar controller, whose combat/trade/explore bar gadgets cache the live 0..1 xp
// fill fraction. 0 until the updater has run in space. lua_api reads the fractions
// off this (enb.xp_frac).
unsigned xp_ctrl();

// The live selected target object, captured from the native target-refresh's radar
// highlight setter (game::addr::TargetEntitySet, arg0): the real game object the refresh
// resolves from the selected GameID, refreshed on every target switch, 0 when nothing is
// targeted. 0 until the hook (installed by enable_event_hooks) has fired.
// lua_api reads the current target's hull/shield aux AND its instance name off the
// object's properties container (object+0x88): the aux bag for hull/shield, the
// instance name as a char* at container+0x124, the class name at container+0x3c.
// target_ctrl() is the in-space targeting/HUD controller (ECX of the target-frame
// refresh, game::addr::TargetFrameRefresh), captured so lua_api can live-read the
// target LEVEL off the targeting subsystem each frame; 0 until the refresh has run.
unsigned target_obj();
unsigned target_ctrl();

// Nav registry: gid-keyed mirror of the client's nav map, fed by read-only hooks
// on the 0x0099 NAVIGATION / 0x009A NavDelete packet handlers (installed by
// enable_event_hooks; see game.h::addr::NavPacketProcess). nav_lookup() answers
// "is this GameID a nav?" plus its signature/visited/navtype/huge -- the ONLY
// reliable nav discriminator (an entity's class string is just "Decoration").
// Cleared at world entry (GameIDs are per-sector). Game thread only.
struct NavInfo {
    float sig;
    int navtype;
    bool visited;
    bool huge;
};
bool nav_lookup(unsigned gid, NavInfo* out);
int nav_registry_count();
void nav_registry_clear();

// world_mgr() is the world/player manager M, captured (ECX) from the world-manager
// initializer (game::addr::WorldMgrInit), which runs once at world entry. M is a
// session-stable singleton; lua_api uses it for target-action dispatch -- the local
// player game id at M + game::world::player_id and the sector-server Connection at
// M + game::world::connection. 0 until the world manager has been initialized this
// session (i.e. once you are in space).
unsigned world_mgr();

// loot_container() is the hulk/loot cargo CONTAINER pointer, captured (ECX)
// read-only from the per-slot ItemTemplateID accessor (game::addr::CargoTemplateID),
// which the client calls once per occupied slot every time a loot/hulk cargo grid
// repaints. 0 until a cargo grid has been drawn this session. Because the accessor
// fires for ANY inventory grid, lua_api (enb.loot) re-reads the container's
// inventory-name to confirm it is the hulk cargo before trusting the rows.
// loot_container_tick() is GetTickCount() of the most recent latch (0 = never), a
// freshness signal for whether the grid is actively being drawn.
unsigned loot_container();
unsigned long loot_container_tick();

// ECX (this) of the in-space action-bar controller that owns the numbered
// ability/weapon slots (primary + alternate). Captured from the bank constructor
// the moment a bank exists (entering space) and refreshed on every slot dispatch,
// so it is valid before any interaction. lua_api reads the slots off this and
// re-dispatches a slot through it (enb.actionbar).
unsigned actionbar();

// ECX (this) captured from the cockpit constructors: the throttle/warp cluster
// controller and the "UI COMMANDS" action-button controller. 0 until each ctor
// has run (entering space). lua_api walks each controller's child gadgets and
// clears their visible flag (enb.hide_cockpit) so the Freya overlay replaces them.
unsigned cockpit_throttle_ctrl();
unsigned cockpit_cmd_ctrl();

// ECX (this) captured from WarpPathBuild (game::addr::WarpPathBuild): the
// radar/targeting controller that builds the warp nav path from the current target.
// It is a session singleton with no fixed M/player offset, so we capture it here. 0
// until the client has built a warp path this session (targeted a nav + engaged
// warp at least once). lua_api (enb.warp) needs it to drive the native path build.
unsigned warp_radar_ctrl();

// Seed the warp radar controller WITHOUT a real orb engage: lua_api resolves it by
// offset off M and validates its full structure (see resolve_warp_radar) before
// calling this, so enb.warp works with zero screen interaction. Only takes effect
// while unset (a genuine WarpPathBuild capture always wins if it has already fired),
// so a resolved value can never clobber the ground-truth ECX the hook latched.
void seed_warp_radar_ctrl(unsigned thisp);

// ECX (this) captured from ChatLocalLine (game::addr::ChatLocalLine): the session-stable
// chat PANEL object. The line RING hangs off it at +chat::panel_ring, and it is the
// `this` ChatSend requires. 0 until the first line is printed this session. lua_api
// exposes it (enb.chat_panel()); the Freya chat window derives the ring read-only and
// passes it as ChatSend's `this`.
unsigned chat_panel();

// ECX (this) captured from ChatLineAppend (game::addr::ChatLineAppend): the chat line
// RING object (chat::ring_* layout). 0 until the first line is appended this session.
// lua_api exposes it (enb.chat_buf()); the Freya chat window walks the ring read-only.
unsigned chat_ring();

// ECX (this) captured from the PDA panel controller (game::addr::PdaCtor ctor +
// PdaSwitch dispatcher): the bottom-left micro-menu controller whose child screens
// are Inventory/Skills/Character Info/Vault/Galaxy Map. 0 until the controller is
// constructed (entering space). lua_api exposes it (enb.pda_ctrl()) and dispatches
// a panel through PdaSwitch (enb.pda_switch).
unsigned pda_ctrl();

// ECX (this) captured from the screen shell's per-frame apply pump
// (game::addr::ShellApply): the in-game controller that owns the active screen
// (+0x104) and the pending-screen id (+0x108). The in-game HUD updater calls the
// pump every frame, so this is captured continuously and stays fresh. lua_api
// exposes it (enb.shell_ctrl()) and requests a screen through ShellRequest
// (enb.shell_screen) -- id 1 opens the in-game Options screen, the one micro-menu
// button that is NOT a PDA child.
unsigned shell_ctrl();

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
