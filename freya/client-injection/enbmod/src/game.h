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

namespace enb { namespace game {

constexpr uintptr_t kImageBase = 0x00400000;

namespace addr {
    // ---- engine anchors (injection / per-frame) ----
    constexpr uintptr_t MsgPump_Get   = 0x00443710; // GetMessageA loop (main thread)
    constexpr uintptr_t MsgPump_Peek  = 0x007428f0; // PeekMessageA + RedrawWindow variant

    // ---- vitals: hull / shield / reactor(energy) ----
    constexpr uintptr_t StatBlock     = 0x006f66e0; // Energy/MaxEnergy/Shield/MaxShield/Owner/Title
    constexpr uintptr_t EnergyBar     = 0x005dc4a0; // EnergyPercent/MaxEnergyPower UI
    constexpr uintptr_t HullPoints    = 0x006fe1e0; // HullPoints / ship damage
    constexpr uintptr_t VitalsBars    = 0x005dbfc0; // draws reactor/shield/hull bars

    // ---- levels / xp (combat / trade / explore) ----
    constexpr uintptr_t LevelText     = 0x00548d60; // "Combat/Trade/Explore ... Level:%d"
    constexpr uintptr_t XpBars        = 0x0058c450; // combat/trade/explore bar %  (UI EXPERIENCE)
    constexpr uintptr_t RpgLevels     = 0x0074bfb0; // reads RPGInfo Combat/TradeLevel from AuxData

    // ---- target ----
    constexpr uintptr_t TargetInfo    = 0x0065e5e0; // "Target: %s at" + %.2f distance/pos
    constexpr uintptr_t TargetPanel   = 0x00581e60; // current-target panel / HulkUI

    // ---- chat ----
    constexpr uintptr_t ChatGadget    = 0x0065e3e0; // ChatGadget (MSG:%d)
    constexpr uintptr_t ChatRender    = 0x00680700; // chat rendering
    constexpr uintptr_t ChatChannel   = 0x0065bfd0; // channel routing
    constexpr uintptr_t ChatSend      = 0x00749ed0; // channel state + send-message

    // ---- navs ----
    constexpr uintptr_t NavListBuild  = 0x007b7ef0;
    constexpr uintptr_t NavListRender = 0x007b8510;
    constexpr uintptr_t WarpPath      = 0x007c0ee0;

    // ---- keybinds / activation slots ----
    constexpr uintptr_t KeyDefinitions= 0x0071a960; // KeyDefinitions list
    constexpr uintptr_t BindCategories= 0x004081fc; // Targeting/Navigation/Camera/Activation

    // ---- skills / abilities ----
    constexpr uintptr_t AbilitySlots  = 0x006a4b40; // RPGInfo SkillPowerupAbilityNumber
    constexpr uintptr_t SkillLifecycle= 0x0060f1a0; // Skill Activated/Deactivated/Interrupted
    constexpr uintptr_t SkillButton   = 0x00662dc0; // hotbar skill gadget button

    // ---- AuxData accessor candidates (universal read primitive once resolved) ----
    constexpr uintptr_t AuxGet_Ability= 0x006a4b40;
    constexpr uintptr_t AuxGet_Skill  = 0x00417f21;
}

// Runtime-editable field offsets. -1 means "not calibrated -- reads return nil/0".
// Layout intentionally flat & simple so the Lua calibrate() can poke any field by name.
struct Offsets {
    // global pointer to the local player / ship object (in .data, 0x00B6C000+). Once you find
    // it (pointer scan, or hook StatBlock and log ECX), set player_ptr_addr to the
    // ADDRESS that holds the pointer; the runtime derefs it each frame.
    uintptr_t player_ptr_addr = 0;

    // offsets within the player/ship object:
    int hull        = -1;
    int hull_max    = -1;
    int shield      = -1;   // ShieldPercent (0..100) or raw -- you decide during calibration
    int shield_max  = -1;
    int energy      = -1;   // reactor
    int energy_max  = -1;

    int combat_lvl  = -1;
    int trade_lvl   = -1;
    int explore_lvl = -1;
    int combat_pct  = -1;
    int trade_pct   = -1;
    int explore_pct = -1;
    int skill_points= -1;

    int pos_x       = -1;   // float
    int pos_y       = -1;
    int pos_z       = -1;

    // player display name (player-card header): offset to a char* or inline cstr.
    int name        = -1;
    int name_is_ptr = 1;    // 1: field holds char*; 0: field is inline string
    int name_wide   = 0;    // 1: UTF-16

    // game-state machine: an address holding the int state/screen code, plus the
    // code value for each screen. enb.state() reads the int and maps it to a name
    // so the HUD can gate itself (in-space-only; station hides the hotbar). With
    // game_state_addr == 0 (or no code matches) enb.state() returns "unknown",
    // which the HUD treats as "show everything" -- the pre-calibration default.
    uintptr_t game_state_addr = 0;
    int state_space   = -1;
    int state_station = -1;
    int state_login   = -1;
    int state_charsel = -1;
    int state_load    = -1;

    // target object: address of the pointer to the current target, + fields within it.
    uintptr_t target_ptr_addr = 0;
    int tgt_name    = -1;   // offset to a char* or inline cstr
    int tgt_name_is_ptr = 1;// 1: field holds char*; 0: field is inline string
    int tgt_name_wide = 0;  // 1: UTF-16
    int tgt_hull    = -1;
    int tgt_pos_x   = -1;
    int tgt_pos_y   = -1;
    int tgt_pos_z   = -1;
};

// The single live offsets instance (defined in game.cpp). Mutated by Lua enb.calibrate{}.
Offsets& offs();

}} // namespace enb::game
