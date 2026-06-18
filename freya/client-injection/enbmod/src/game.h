#pragma once
// game.h -- the address book for client.exe (Earth & Beyond, base 0x00400000, no ASLR).
//
// Two kinds of numbers live here:
//   (1) CODE ADDRESSES (addr::*) -- function entry points from INJECTION-MAP.md. These are
//       STABLE every run (no ASLR) and as confident as static string-anchoring gets.
//   (2) STRUCT OFFSETS (Offsets struct) -- field positions inside game objects. These are
//       UNCONFIRMED HYPOTHESES. They are deliberately runtime-editable (settable from Lua via
//       enb.calibrate{...}) because you WILL discover the real values by poking at runtime,
//       and recompiling for every guess would be miserable. All default to -1 = "unknown".
//
// Status: nothing in Offsets is verified. The plumbing that reads them is real; the
// numbers are placeholders. See README "Calibration".

#include <cstdint>

namespace enb {
namespace game {

constexpr uintptr_t kImageBase = 0x00400000;

namespace addr {
// ---- engine anchors (injection / per-frame) ----
constexpr uintptr_t MsgPump_Get = 0x00443710;  // GetMessageA loop (main thread)
constexpr uintptr_t MsgPump_Peek = 0x007428f0; // PeekMessageA + RedrawWindow variant

// ---- vitals: hull / shield / reactor(energy) ----
constexpr uintptr_t StatBlock = 0x006f66e0; // Energy/MaxEnergy/Shield/MaxShield/Owner/Title
constexpr uintptr_t EnergyBar =
    0x005dc4a0; // vitals VALUE updater (pushes %s into the bars) -- NOT a hide target
constexpr uintptr_t HullPoints = 0x006fe1e0; // HullPoints / ship damage
constexpr uintptr_t VitalsBars =
    0x005dbfc0; // vitals bar CONSTRUCTOR (builds reactor/shield/hull) -- NOT a hide target
constexpr uintptr_t VitalsPaint =
    0x005dcae0; // vitals per-frame PAINT (hull/shield/reactor). Pure paint, gated on each
// gadget's visible flag; entry is a clean ret-patch target (player-card replaces it)

// ---- levels / xp (combat / trade / explore) ----
constexpr uintptr_t LevelText = 0x00548d60; // "Combat/Trade/Explore ... Level:%d"
constexpr uintptr_t XpBars =
    0x0058c450; // xp bar CONSTRUCTOR (combat/trade/explore) -- NOT a hide target
constexpr uintptr_t XpPaint =
    0x0058cf60; // xp per-frame PAINT (combat/trade/explore). Pure paint, clean ret-patch
                // target (discipline card replaces it)
constexpr uintptr_t XpBarsUpdate =
    0x0058cb50; // xp bar VALUE updater (recomputes each bar's fill fraction from the RPGInfo
                // AuxData container and writes it to gadget+0x68). __fastcall(ECX = the XpBars
                // controller). We hook it READ-ONLY to capture the controller `this` so the
                // discipline card can read the live fill (hooks::xp_ctrl()); never patched
                // (XpPaint above is the hide target -- a different entry on the same object).
constexpr uintptr_t RpgLevels = 0x0074bfb0; // reads RPGInfo Combat/TradeLevel from AuxData

// ---- target ----
constexpr uintptr_t TargetInfo = 0x0065e5e0;  // "Target: %s at" + %.2f distance/pos
constexpr uintptr_t TargetPanel = 0x00581e60; // current-target panel / HulkUI

// ---- chat ----
constexpr uintptr_t ChatGadget = 0x0065e3e0;    // ChatGadget (MSG:%d)
constexpr uintptr_t ChatRender = 0x00680700;    // chat rendering
constexpr uintptr_t ChatChannel = 0x0065bfd0;   // channel routing
constexpr uintptr_t ChatSend = 0x00749ed0;      // channel state + send-message
constexpr uintptr_t ChatLocalLine = 0x0074d990; // local chat-window line printer (no packet).
    // __stdcall(int channel, const char* msg, int flag):
    // channel 0x11=error 0x13=system 0x15=warning 6=usage.
    // The client uses it for its own notices; observed at
    // runtime to take 3 stack args + ret 0xc (stdcall, no this).

// ---- navs ----
constexpr uintptr_t NavListBuild = 0x007b7ef0;
constexpr uintptr_t NavListRender = 0x007b8510;
constexpr uintptr_t WarpPath = 0x007c0ee0;

// ---- keybinds / activation slots ----
constexpr uintptr_t KeyDefinitions = 0x0071a960; // KeyDefinitions list
constexpr uintptr_t BindCategories = 0x004081fc; // Targeting/Navigation/Camera/Activation

// ---- skills / abilities ----
constexpr uintptr_t AbilitySlots = 0x006a4b40;   // RPGInfo SkillPowerupAbilityNumber
constexpr uintptr_t SkillLifecycle = 0x0060f1a0; // Skill Activated/Deactivated/Interrupted
constexpr uintptr_t SkillButton =
    0x00662dc0; // skill button CONSTRUCTOR -- NOT a hide target. The skill gadget has no
                // standalone pure-paint entry (render is fused with state mutation), so unlike
                // vitals/xp there is no clean ret-patch; hiding it needs a runtime per-gadget
                // visible-flag write instead. Left to CV-AS-HIDE-SKILL.

// ---- cockpit (bottom-center throttle/warp cluster + UI command buttons) ----
// Two controllers built once when the cockpit comes up (entering space). We hook
// each CONSTRUCTOR read-only to capture its `this` (ECX), exactly like the vitals/
// xp/rpg capture hooks, then clear each child gadget's engine visible flag
// (gadget + 0x60 -- the engine-wide "is this gadget painted" byte, read by 43
// paint gates across the client) so the Freya overlay's own throttle/action UI is
// the only one drawn. Read-only on the ctor; the only write is the guarded byte
// clear, which runs NO game code and is reversible (stop clearing -> the engine
// repaints the gadget next frame). NOT patched -- these are capture targets.
constexpr uintptr_t CockpitThrottle =
    0x0057dd20; // throttle/warp cluster CTOR. __fastcall(ECX = controller). Children
                // (WARP BAR/THROTTLE/REVERSE/LEFT/RIGHT) at controller int-slots 0x2d..0x31.
constexpr uintptr_t CockpitCommands =
    0x0057be50; // "UI COMMANDS" action-button CTOR. __fastcall(ECX = controller). Button
                // children from int-slot 0x2b upward (two groups: 0x2b.. and 0x2d..0x33).

// ---- AuxData accessor candidates (universal read primitive once resolved) ----
constexpr uintptr_t AuxGet_Ability = 0x006a4b40;
constexpr uintptr_t AuxGet_Skill = 0x00417f21;
} // namespace addr

// Vitals-bar fill chain. Unlike the Offsets struct below (runtime hypotheses),
// these are CONFIRMED build-constant field offsets, observed at runtime: the
// vitals controller `this` is captured live every frame by the in-space
// heartbeat hook (hooks::vitals_ctrl()); from it each bar's gadget sits at a
// fixed slot, and the gadget's true fill fraction (cur/max, 0..1) is at
// +fill_frac. That fraction SNAPS to the new ratio the instant a bar changes (a
// second copy at +0x70 lags/animates the on-screen tween -- we read the snapping
// one). These hold for every install of this client build; only the controller
// pointer varies per run, so it is read live rather than stored here.
namespace vitals {
constexpr int gadget_energy = 0x1c; // [ctrl + 0x1c] -> reactor bar gadget
constexpr int gadget_shield = 0x20; // [ctrl + 0x20] -> shield bar gadget
constexpr int gadget_hull = 0x24;   // [ctrl + 0x24] -> hull bar gadget
constexpr int fill_frac = 0x68;     // gadget + 0x68 -> 0..1 fill fraction (cur/max)
} // namespace vitals

// Player-entity chain hung off the same live vitals controller. The controller's
// data object ([ctrl+0x04]) points at the local player's ship entity
// ([data+0x88]); the entity carries the character name as a char* at +0x124
// (observed: the field held "<your char>" at runtime). Build-constant struct
// offsets, same caveat as vitals -- only the controller pointer varies per run.
namespace player {
constexpr int ctrl_data = 0x04;    // [ctrl + 0x04]   -> data object
constexpr int data_entity = 0x88;  // [data + 0x88]   -> player ship entity
constexpr int entity_name = 0x124; // [entity + 0x124]-> char* character name
} // namespace player

// AuxData property-bag reader. The client keeps per-object gameplay vitals as
// string-keyed float entries (a property bag), NOT flat struct fields -- so the
// numeric current/max for hull/shield/reactor (and the discipline levels) have
// no fixed struct offset to read. They are resolved through the client's own
// getter: intern a key object from a key string, then look it up on the object's
// aux container. Observed at runtime in the vitals updater (the same
// hooks::vitals_ctrl chain), which reads exactly these keys every frame:
//   HullPoints / MaxHullPoints      -- hull stored absolute
//   MaxShieldPower / MaxEnergyPower -- shield/reactor max (current = fill_frac*max)
// The discipline levels (RPGInfo CombatLevel/TradeLevel/ExploreLevel) use this
// same property-bag mechanism but live on a DIFFERENT object -- see namespace rpg.
namespace aux {
// build_key(keybuf, "KeyName"): __thiscall, ECX = keybuf (a zeroed scratch
// buffer >= keybuf_sz). Interns the key; no owned heap, so no cleanup needed.
constexpr uintptr_t build_key = 0x004ad380;
// get_value(entity, keybuf) -> entry|0: __cdecl. Resolves an absolute
// (non-percent) value entry on `entity`; type-checked, returns 0 on miss.
constexpr uintptr_t get_value = 0x00546710;
constexpr int val_off = 0x84;   // entry + 0x84 -> float value
constexpr int valid_off = 0x70; // entry + 0x70 -> nonzero when value is set
constexpr int keybuf_sz = 0x40; // zeroed scratch key-object buffer
} // namespace aux

// Discipline levels (RPGInfo Combat/Trade/Explore) -- a property bag like aux,
// but on the RPG manager, not the ship entity, and behind a different-typed
// getter. The manager is the `this` (ECX) of the level-reader at addr::RpgLevels,
// captured live by its hook (hooks::rpg_mgr()); it holds the RPGInfo AuxData
// container at manager + container_off. Entries are resolved with get_entry
// (__cdecl(container, keybuf), same shape as aux::get_value but a different value
// type tag), with the int level at entry + aux::val_off and validity at
// entry + aux::valid_off. Keys are built with aux::build_key. Levels read 0 on a
// fresh character (correct), so a successful read shows "0", not the "--" skeleton.
namespace rpg {
constexpr int container_off = 0x12c0;       // manager + this -> RPGInfo AuxData container
constexpr uintptr_t get_entry = 0x00514b60; // __cdecl(container, keybuf) -> entry|0
} // namespace rpg

// Discipline XP-bar fill fractions (combat/trade/explore). The per-discipline XP
// progress is NOT a plain AuxData key we can read directly: the explore/trade
// fractions are computed through a multi-step getter chain inside the XpBars
// updater (addr::XpBarsUpdate) that we cannot replicate from Lua. But that updater
// caches the final 0..1 fraction on each bar gadget at gadget + fill_frac, exactly
// like the vitals bars. So we capture the XpBars controller `this` live from the
// updater hook (hooks::xp_ctrl()) and read the cached fraction off each bar gadget.
// Build-constant offsets observed in addr::XpBarsUpdate; only the controller
// pointer varies per run, so it is read live rather than stored.
namespace xp {
constexpr int bar_combat = 0x1c;  // [ctrl + 0x1c] -> combat xp-bar gadget
constexpr int bar_trade = 0x20;   // [ctrl + 0x20] -> trade xp-bar gadget
constexpr int bar_explore = 0x24; // [ctrl + 0x24] -> explore xp-bar gadget
constexpr int exists_flag = 0x60; // gadget + 0x60 -> nonzero when the bar is live
constexpr int fill_frac = 0x68;   // gadget + 0x68 -> 0..1 xp fill fraction
} // namespace xp

// Cockpit child-gadget layout. Each controller (captured live, see addr::Cockpit*)
// stores its child gadget pointers at fixed int-slots; clearing each child's
// engine visible flag (child + visible_flag) hides it. Build-constant offsets read
// out of the two constructors; only the controller pointer varies per run.
namespace cockpit {
constexpr int visible_flag = 0x60; // gadget + 0x60 -> engine "is painted" byte (clear to hide)
// throttle/warp cluster controller: WARP/THROTTLE/REVERSE/LEFT/RIGHT.
constexpr int throttle_first = 0x2d; // first child int-slot (inclusive)
constexpr int throttle_last = 0x31;  // last child int-slot (inclusive)
// "UI COMMANDS" action buttons controller: two adjacent groups.
constexpr int command_first = 0x2b; // first child int-slot (inclusive)
constexpr int command_last = 0x33;  // last child int-slot (inclusive)
} // namespace cockpit

// Runtime-editable field offsets. -1 means "not calibrated -- reads return nil/0".
// Layout intentionally flat & simple so the Lua calibrate() can poke any field by name.
struct Offsets {
    // global pointer to the local player / ship object (in .data, 0x00B6C000+). Once you find
    // it (pointer scan, or hook StatBlock and log ECX), set player_ptr_addr to the
    // ADDRESS that holds the pointer; the runtime derefs it each frame.
    uintptr_t player_ptr_addr = 0;

    // offsets within the player/ship object:
    int hull = -1;
    int hull_max = -1;
    int shield = -1; // ShieldPercent (0..100) or raw -- you decide during calibration
    int shield_max = -1;
    int energy = -1; // reactor
    int energy_max = -1;

    int combat_lvl = -1;
    int trade_lvl = -1;
    int explore_lvl = -1;
    int combat_pct = -1;
    int trade_pct = -1;
    int explore_pct = -1;
    int skill_points = -1;

    int pos_x = -1; // float
    int pos_y = -1;
    int pos_z = -1;

    // player display name (player-card header): offset to a char* or inline cstr.
    int name = -1;
    int name_is_ptr = 1; // 1: field holds char*; 0: field is inline string
    int name_wide = 0;   // 1: UTF-16

    // game-state machine: an address holding the int state/screen code, plus the
    // code value for each screen. enb.state() reads the int and maps it to a name
    // so the HUD can gate itself (in-space-only; station hides the hotbar). With
    // game_state_addr == 0 (or no code matches) enb.state() returns "unknown",
    // which the HUD treats as "show everything" -- the pre-calibration default.
    uintptr_t game_state_addr = 0;
    int state_space = -1;
    int state_station = -1;
    int state_login = -1;
    int state_charsel = -1;
    int state_load = -1;

    // target object: address of the pointer to the current target, + fields within it.
    uintptr_t target_ptr_addr = 0;
    int tgt_name = -1;       // offset to a char* or inline cstr
    int tgt_name_is_ptr = 1; // 1: field holds char*; 0: field is inline string
    int tgt_name_wide = 0;   // 1: UTF-16
    int tgt_hull = -1;
    int tgt_pos_x = -1;
    int tgt_pos_y = -1;
    int tgt_pos_z = -1;
};

// The single live offsets instance (defined in game.cpp). Mutated by Lua enb.calibrate{}.
Offsets& offs();

} // namespace game
} // namespace enb
