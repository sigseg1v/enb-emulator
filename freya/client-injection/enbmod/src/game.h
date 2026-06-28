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
constexpr uintptr_t ChatGadget = 0x0065e3e0;  // ChatGadget (MSG:%d)
constexpr uintptr_t ChatRender = 0x00680700;  // chat rendering
constexpr uintptr_t ChatChannel = 0x0065bfd0; // channel routing
constexpr uintptr_t ChatSend = 0x00749ed0;    // slash-command parser (cdecl(char* line))
// --- chat SEND path (mirrors the native chat-gadget submit handler at 0x0065ccd0) ---
// A typed chat line is sent in one of two ways, exactly as the native input box does:
//   1. A '/'-prefixed line is handed to the chat-manager's command dispatcher
//      (its vtable+0x34 entry == this address): /tell <name> <msg> (whisper),
//      /gen /ooc /mkt /new /jen /ter /pro ... (subscribed channels). It returns
//      nonzero when it claimed the line; zero -> not a command, fall through.
//      NB this is an alternate ENTRY POINT inside ChatSend's function body
//      (0x749ed0..0x74b5e0), reached only via the manager vtable -- __thiscall,
//      ECX = chat manager, char* line.
constexpr uintptr_t ChatCmdDispatch = 0x0074abc0;
//   2. A plain line is packed into a Client_Chat message (opcode 0x33) and handed
//      to the sender. ChatBuildMsg is __thiscall(ECX = caller-owned message obj,
//      uint senderId, char* text, char channel, char* text2=0); it stores the text
//      POINTER (no copy) so the buffer must outlive the send. channel byte:
//      3 = Local/Sector, 4 = Broadcast (verified on the wire). senderId is read
//      from (manager + ChatMgrSenderId).
constexpr uintptr_t ChatBuildMsg = 0x008785d0;
//   ChatMsgSend is __thiscall(ECX = chat manager, message obj); it routes the built
//   message through manager->connection (manager + 0x1124) to the wire.
constexpr uintptr_t ChatMsgSend = 0x00728150;
constexpr uintptr_t ChatMgrSenderId = 0x112c;   // field offset on the chat manager
constexpr uintptr_t ChatLocalLine = 0x0074d990; // local chat-window line printer (no packet).
    // __thiscall(ECX = chat panel, int channel, const char* msg, char flag):
    // channel 0x11=error 0x13=system 0x15=warning 6=usage.
    // The client uses it for its own notices; prefixes COMPUTER/SYSTEM/WARNING then
    // routes down to ChatLineAppend. (ECX=this confirmed by disasm of its call
    // sites -- the panel comes from a vtable getter, not a global.)
// Chat line RING-BUFFER append -- the single choke point every displayed chat-panel
// line passes through. Call graph: ChatLocalLine(0x0074d990) -> FUN_00563760 (chat-log
// + add) -> this. System, computer, warning, and network chat (Broadcast/Tell/Guild)
// all funnel here, because the only path that fills the panel's ring is via
// ChatLocalLine. __thiscall, ECX = the RING-BUFFER object (== *(chatpanel + 0x13c),
// confirmed by disasm of FUN_00563760's call site: `mov 0x13c(edi),ecx; call thunk`).
// We hook it READ-ONLY to capture that ECX -- the exact capture pattern of the
// cockpit/xp/rpg hooks -- so the Freya chat window can poll the ring from Lua with no
// further game calls. Lines land in the ring in call order (== chronological), which
// is exactly the always-interleaved system+chat view we want.
constexpr uintptr_t ChatLineAppend = 0x0067d780;
// Two independent READ-ONLY captures, both firing on the first line printed this
// session: hooking ChatLineAppend (ECX = the RING) gives the scrollback read, and
// hooking ChatLocalLine (ECX = the PANEL, confirmed: both touch this+0x127c, the
// _chatpanel field) gives the `this` that ChatSend requires. The ring is NOT a fixed
// offset off the panel, so we capture each at its own choke point. Both objects are
// session-stable singletons once captured.

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
// In-space action-bar slot dispatcher ("Use Slot"): __thiscall(ECX = the action-bar
// controller, arg0 = the clicked slot's gadget id). Reached from both a slot click
// and a "1".."6" keypress. We hook it read-only to capture its `this` (ECX) -- the
// exact pattern of the rpg/xp/cockpit capture hooks -- so the Freya HUD can read the
// slots (primary + alternate) off it and re-dispatch a slot by calling this fn with
// the slot's real gadget id. Capture only; never alters the dispatch.
constexpr uintptr_t ActionBarUse = 0x006120e0;
// Action-bar bank CONSTRUCTOR (__thiscall, ECX = the bank controller). Runs once
// per 3-slot bank when the cockpit comes up (entering space), BEFORE any slot is
// ever clicked. We hook it read-only to capture the controller `this` the moment
// it exists -- so the Freya HUD shows real slot icons immediately on load instead
// of staying blank until the first slot interaction warms ActionBarUse. Same
// capture-only pattern as the cockpit ctors; never alters the constructor.
constexpr uintptr_t ActionBarCtor = 0x00610f80;
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

// ---- PDA / micro-menu screen dispatcher (bottom-left button band) ----------
// The micro-menu buttons (Inventory / Character Info / Galaxy Map / Skills / Vault)
// are driven by the PDA panel controller (Ghidra srcfile "pdain", layout TSI14A).
// PdaSwitch is the unified "make PDA child screen N active" dispatcher,
// __thiscall(ECX = the PDA controller, int index): 0=Inventory, 1=Skills,
// 2=Character Info, 3=Vault, 4=Galaxy Map -- verified from FUN_006956c0's
// child-pointer switch over controller +0x9c..0xa8. PdaCtor is the controller
// CONSTRUCTOR, __fastcall(ECX = the controller); it builds the child screens and
// stores the TSI14A screen at controller+0x94. We hook the CTOR read-only to
// capture the controller `this` the moment it exists (entering space) so the Freya
// top-left buttons can dispatch on the first click, AND hook the dispatcher as a
// re-validating refresh -- the same capture-only pattern as ActionBarCtor/Use.
constexpr uintptr_t PdaCtor = 0x00693230;
constexpr uintptr_t PdaSwitch = 0x00695780;

// ---- in-game screen shell (Options micro-menu button) -----------------------
// The bottom-left Options button does NOT open a PDA child; it opens the in-game
// OPTIONS_MAIN screen (layout fa_opt01a.ini). That screen is owned by the screen
// SHELL controller, which holds the currently-shown screen at +0x104 and a
// "pending screen id" at +0x108. ShellApply (FUN_00565f80, __fastcall(ECX = the
// shell)) is the per-frame "apply the pending screen change" pump: when +0x108
// is non-zero it tears down the old screen and builds the requested one --
// id 1 = Options (verified: case 1 constructs the fa_opt01a screen class). It is
// called every frame by the in-game HUD updater, so hooking it captures the LIVE
// shell `this` continuously. ShellRequest (FUN_00565f30, __thiscall(ECX = the
// shell, int id)) just stores the pending id (+0x108 = id) -- exactly what every
// native "Options" button does; the next ShellApply frame opens it. We hook
// ShellApply read-only for the `this`, and drive ShellRequest to open a screen.
constexpr uintptr_t ShellApply = 0x00565f80;
constexpr uintptr_t ShellRequest = 0x00565f30;
constexpr int ShellScreenOptions = 1; // +0x108 id for the OPTIONS_MAIN screen

// ---- AuxData accessor candidates (universal read primitive once resolved) ----
constexpr uintptr_t AuxGet_Ability = 0x006a4b40;
constexpr uintptr_t AuxGet_Skill = 0x00417f21;

// ---- current-target capture (two paired hooks in the target-frame refresh) --
// The target-frame refresh resolves the selected GameID through the object registry and,
// in the SAME pass, hands the resolved object to the radar-highlight routine and hands a
// lightweight display descriptor to the frame repaint. We capture both halves:
//
// TargetEntitySet (0x007bd6e0, __thiscall): the radar/target-highlight setter. ECX = the
// radar manager (ignored), arg0 (param_2) = the LIVE resolved target GAME OBJECT (0 on
// de-target). Every caller of this routine is a target-refresh path passing the resolved
// object or 0, so arg0 is exactly the current target object. Its hull/shield AuxData and
// class name do NOT live on this contact object directly: the contact holds a pointer to
// its properties container at +0x88, and the aux bag (HullPoints / MaxHullPoints /
// MaxShieldPower / ShieldPercent) plus the generic CLASS name ("Ship"/"Starbase", a
// char* at container+0x3c) read off THAT container. We capture arg0 (0 clears) and
// forward untouched.
//
// TargetFrameRefresh (0x00512400, __thiscall): the per-frame target-frame refresh.
// ECX (its `this`) is the in-space targeting/HUD controller. We capture it read-only
// (hooks::target_ctrl()) so lua_api can live-read the target's LEVEL each frame:
// controller[0x13] (ctrl + targeting::ctrl_subsys) is the targeting subsystem, whose
// vtable getter yields the targeting data object holding the "TargetThreatLevel" aux
// (see namespace targeting). The level is read live rather than captured here because
// it arrives from the server AFTER the target-change repaint -- a one-shot capture at
// target switch sees the not-yet-populated entry and reads -1. Both this refresh and
// TargetEntitySet still run while the native frame is draw-suppressed by the hide-ui
// mod (hide-ui only clears the widget's draw-gate bit; the update logic is untouched).
constexpr uintptr_t TargetFrameRefresh = 0x00512400;
constexpr uintptr_t TargetEntitySet = 0x007bd6e0;
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

// Target LEVEL read chain, off the in-space targeting controller captured at
// addr::TargetFrameRefresh (hooks::target_ctrl()). controller[0x13] (ctrl +
// ctrl_subsys) is the targeting subsystem `ts`; its vtable+getdata_vtoff getter
// (__thiscall, no args) returns the targeting DATA object; *(dataObj + aux_container)
// is the aux container holding the "TargetThreatLevel" entry, read as an int through
// rpg::get_entry (build_key + get_entry, val at +val_off when valid at +valid_off --
// the same shape as the discipline-level read, the threat keys being that aux-value
// type). Build-constant offsets observed in the native refresh; only the controller
// pointer varies per run.
namespace targeting {
constexpr int ctrl_subsys = 0x4c;   // controller[0x13] -> targeting subsystem
constexpr int getdata_vtoff = 0x28; // subsystem vtable+0x28 -> targeting data object
constexpr int aux_container = 0x88; // *(dataObj + 0x88) -> aux container (threat keys)
} // namespace targeting

// Target/player WORLD position, for the straight-line range shown on the frame. The
// client's camera controller computes target distance exactly this way: a space object
// (a GameID-resolved entity, like the target frame's resolved target) exposes a
// world-position getter at vtable+wpos_vtoff. It is a struct-returning __thiscall --
// ECX = the object, and the caller passes the address of a 3-float output buffer as the
// hidden return pointer; the getter writes world X/Y/Z to buf+wpos_x/_y/_z (three packed
// floats). Distance = |player_wpos - target_wpos|, both read this way. The flat object
// offsets are NOT world coords.
namespace locatable {
constexpr int wpos_vtoff = 0x24; // entity vtable+0x24(&buf) -> packed Vec3 in buf
constexpr int wpos_x = 0x00;     // buf + 0x00 -> world X
constexpr int wpos_y = 0x04;     // buf + 0x04 -> world Y
constexpr int wpos_z = 0x08;     // buf + 0x08 -> world Z
} // namespace locatable

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
constexpr uintptr_t get_entry = 0x00514b60; // __cdecl(container, keybuf) -> entry|0 (int-typed)
// String-typed property-bag lookup: same __cdecl(container, keybuf) shape as get_entry
// but resolves a string-typed entry. When valid (entry + aux::valid_off nonzero), the
// value at entry + aux::val_off is a char* (a NUL-terminated C string), not an int. This
// is how the HUD reads the target's "TargetThreat" line ("Level N" for a mob).
constexpr uintptr_t get_string = 0x00514bd0; // __cdecl(container, keybuf) -> entry|0 (string-typed)
} // namespace rpg

// Chat line RING-BUFFER layout, off the ring object captured at addr::ChatLineAppend
// (hooks::chat_buf() == FUN_0067d780's ECX). NOT a sorted vector -- it is a fixed-size
// ring of 0x14-byte slots, indexed (write_count % capacity). Confirmed by reading
// FUN_0067d780's body:
//   slot = ring[+0xc] + (ring[+8]++ % ring[+4]) * 0x14
//   slot+0x00 = uint type/channel   slot+0x08 = char* text   slot+0x0c = uint len
// To read chronologically: i from max(0, count-capacity) to count-1, slot[(i%cap)].
// Build-constant offsets; only the ring pointer varies per run (captured live).
namespace chat {
constexpr int ring_cap = 0x04;    // ring obj -> uint slot capacity
constexpr int ring_count = 0x08;  // ring obj -> uint monotonic write count
constexpr int ring_base = 0x0c;   // ring obj -> ptr to slot array
constexpr int slot_stride = 0x14; // bytes per slot
constexpr int slot_type = 0x00;   // slot -> uint channel/type id
constexpr int slot_text = 0x08;   // slot -> char* (NUL-terminated ASCII)
constexpr int slot_len = 0x0c;    // slot -> uint text length
} // namespace chat

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

// GadgetClass: the engine's base UI-widget class. Every HUD widget (vitals bars,
// xp bars, cockpit throttle/warp/speed, action buttons) derives from it. The slot
// at vtable byte +0x40 is a candidate visibility setter (__thiscall(this, BOOL)).
// EMPIRICAL RESULT (live, 2026-06-18): dispatching it on the captured cockpit /
// vitals / xp HUD controllers had NO visible effect -- those gadgets keep
// painting regardless. The mechanism that DOES hide them is per-instance paint-
// slot suppression: give one instance its own vtable copy whose per-frame paint
// slot is a bare `ret` (lua_api enb.vt_profile to find the slot, enb.vt_hide_paint
// to suppress it, enb.vt_unhide to restore). The per-frame paint slot is class-
// specific, not universal: the cockpit panel controller paints via vtable slot 24,
// while the vitals and xp controllers paint via slot 4 (all pop 0). Keep this
// constant for gadget classes that may yet honour a visibility setter.
namespace gadget {
constexpr int vt_set_visible = 0x40; // vtable byte offset of the candidate visibility setter
} // namespace gadget

// Cockpit child-gadget layout. The controller captured at addr::CockpitThrottle
// stores its child gadget pointers at fixed int-slots; the children span
// 0x2b..0x38 (0x2c is a sub-object, 0x36 unused -- both skipped safely because the
// per-gadget guard rejects a non-gadget vtable). NOTE: the controller captured
// under the "throttle" label actually parents the in-space VITALS bars (its child
// at slot 0x34 is the vitals controller); the round throttle/warp dial + action
// buttons live under a separate controller that is NOT yet captured
// (addr::CockpitCommands's ctor hook does not fire -- enb.cockpit_ctrl() returns 0
// for it). Revisit the address/label mapping before relying on these ranges.
namespace cockpit {
constexpr int throttle_first = 0x2b; // first child int-slot (inclusive)
constexpr int throttle_last = 0x38;  // last child int-slot (inclusive)
// "UI COMMANDS" action buttons controller (addr::CockpitCommands), same scheme.
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
};

// The single live offsets instance (defined in game.cpp). Mutated by Lua enb.calibrate{}.
Offsets& offs();

} // namespace game
} // namespace enb
