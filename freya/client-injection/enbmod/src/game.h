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
    // routes down to ChatLineAppend. (ECX=this confirmed at its call
    // sites -- the panel comes from a vtable getter, not a global.)
// Chat line RING-BUFFER append -- the single choke point every displayed chat-panel
// line passes through. Call graph: ChatLocalLine(0x0074d990) -> 0x00563760 (chat-log
// + add) -> this. System, computer, warning, and network chat (Broadcast/Tell/Guild)
// all funnel here, because the only path that fills the panel's ring is via
// ChatLocalLine. __thiscall, ECX = the RING-BUFFER object (== *(chatpanel + 0x13c),
// confirmed at 0x00563760's call site: `mov 0x13c(edi),ecx; call thunk`).
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
// are driven by the PDA panel controller (layout TSI14A).
// PdaSwitch is the unified "make PDA child screen N active" dispatcher,
// __thiscall(ECX = the PDA controller, int index): 0=Inventory, 1=Skills,
// 2=Character Info, 3=Vault, 4=Galaxy Map -- verified from 0x006956c0's
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
// "pending screen id" at +0x108. ShellApply (0x00565f80, __fastcall(ECX = the
// shell)) is the per-frame "apply the pending screen change" pump: when +0x108
// is non-zero it tears down the old screen and builds the requested one --
// id 1 = Options (verified: case 1 constructs the fa_opt01a screen class). It is
// called every frame by the in-game HUD updater, so hooking it captures the LIVE
// shell `this` continuously. ShellRequest (0x00565f30, __thiscall(ECX = the
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

// WorldMgrInit (0x00741180, __fastcall(ECX = M)): the WORLD/PLAYER manager M's
// initializer. It runs once at world entry (zone-in). Capturing ECX here is the
// deterministic, no-target / no-hover / no-packet way to obtain M: a world manager
// always exists once you are in space, so this fires every session. M is the object
// that carries the local player's game id at M + world::player_id and the live sector-
// server Connection at M + world::connection -- the same M the game's own group/verb
// command paths use to build and push a target-action command. We hook it READ-ONLY to
// capture M (ECX); M is a session-stable singleton, so one capture serves the whole
// session, exposed via hooks::world_mgr().
constexpr uintptr_t WorldMgrInit = 0x00741180;
// --- target-action (verb) dispatch: build a command, push it through M's Connection ---
// This is the exact sequence the client's own command paths run (e.g. the /group verb
// handler and the native target-action buttons): construct a fixed command object, then
// hand it to M->Connection. We replicate it from a Lua-callable helper (enb.target_action)
// so the Freya letter buttons perform the native action without needing the transient
// verb-UI object the on-click dispatcher uses internally.
//   CmdBuild (__thiscall, ECX = a caller-owned >=0x1c-byte command buffer; args: uint
//     player_id, char op, uint target_id, uint extra) writes the command in place (it
//     only sets buf[0..6], so a zeroed 0x20-byte buffer is sufficient and needs no
//     cleanup). op is the per-verb action byte -- the server's avatar-command case the
//     packet drives (see the verb table in target_frame.lua and the server's command
//     switch): 0x01 Tractor, 0x0a Group, 0x11 Prospect, 0x12 Gate, 0x14 Trade,
//     0x19 Register, 0x1a Jumpstart, 0x1c Dock, 0x1d Land, 0x1e Scan. (Tractor/Scan
//     pass the player's OWN game id as target_id; the rest pass the locked target's.)
//   AutoFollowBuild (__thiscall, ECX = buffer; args: uint player_id, char op=0x0c) is the
//     separate builder the Follow verb uses -- it carries no target field (the server
//     follows the player's current target), so it has its own constructor.
//   CmdSend (__thiscall, ECX = M, command buffer) routes the built command through
//     *(M + world::connection)->vtable[2]. Same address as ChatMsgSend (chat uses the
//     identical "build object, push through the manager's Connection" path).
//   CtaBuild (__thiscall, ECX = buffer; args: uint source_id, uint target_id, uint
//     action) is the builder for the group call-to-arms / formation request (wire
//     opcode 0x00BC, CTARequest{SourceID, TargetID, Action} -- see
//     common/include/net7/PacketStructures.h). It writes 6 dwords (0x18 bytes), so the
//     same zeroed 0x20-byte buffer works, and the built object goes through CmdSend
//     like every other outgoing command. This is the packet behind the native group
//     window's Formation / "Target my target" controls. action routes the server's
//     GroupAction switch: 4 Slot Back, 5 Block, 6 Pipe (set formation, leader-only),
//     7 Form Up, 8 Leave Formation, 9 Break Formation (leader), 12 target-my-target.
//     target_id -1 = the whole group (the builder's own no-arg default).
constexpr uintptr_t CmdBuild = 0x00876360;
constexpr uintptr_t AutoFollowBuild = 0x0089d070;
constexpr uintptr_t CmdSend = 0x00728150;
constexpr uintptr_t CtaBuild = 0x0086f070;

// RequestTarget (__cdecl(M, obj) -- BOTH args on the stack, NOT thiscall) is the
// client's own "make this object my target" call -- the same path a click on an
// object in space takes. It reads M's source GameID (M + 0x112c) and the object's
// GameID (obj + 0x90) into a REQUEST_TARGET (wire opcode 0x17) packet and pushes it
// through M's sector-server Connection. The server validates the target is real / in
// range and replies SET_TARGET (0x19), which the client applies natively (target
// frame, threat text, highlight). We replicate it from enb.request_target: resolve a
// GameID to its contact object with the same gid->object lookup enb.group uses, then
// call cdecl(M, obj). Pass ONLY an object the lookup returned (never a raw address).
constexpr uintptr_t RequestTarget = 0x00723230;

// StationExit (__thiscall, ECX = M the world manager; args: char action, int, int)
// is the client's own "leave station" method -- the same call the native "Exit
// Starbase" button runs. Called with action = 1 (and the current StarbaseID at
// M + world::starbase_id non-zero) it builds a STARBASE_REQUEST (wire opcode
// 0x004E, Action = 1 "exit station") and pushes it through M's sector-server
// Connection itself (no separate CmdSend needed); the server launches the player
// into space (a full sector handoff, like a gate jump). action = 0 is the
// server-driven teardown branch -- we never call that. We replicate the exit from
// enb.undock(): confirm docked (starbase_id != 0), then call thiscall(M, 1, 0, 0).
constexpr uintptr_t StationExit = 0x0074ba30;

// ---- warp (engage warp to the currently-targeted nav) ----
// Warp is NOT an avatar-command (CmdBuild verb) and NOT a raw byte buffer: it is a
// packet OBJECT the client serializes itself (wire opcode 0x009B), pushed through the
// same generic Connection send as every other packet (CmdSend -- *(M + connection)
// ->vtable[2]). The native warp-orb click reproduces as this sequence, all on M:
//   1. RequestTarget(nav) so the current target is the nav.
//   2. WarpPathBuild(radarCtrl) -- reads the current target off the radar/targeting
//      controller and runs NavListBuild into that controller's nav vector; returns 1
//      when the path is ready this frame, 0 while still building (retry next tick).
//   3. navList = *(M + world::navlist); navVec = WarpNavVec(radarCtrl) (== ctrl+0x40).
//   4. WarpPacketCtor(buf[5], navList, navVec) builds the WarpPacket object in buf.
//   5. CmdSend(M, buf) serializes+sends 0x009B (framed [u16 len][u16 0x9B][payload];
//      the client owns the byte order, so no htonl/ntohl on our side).
//   6. WarpPacketDtor(buf) frees the object's owned nav-vector copy.
// Warp is a server-side TOGGLE: re-running the same sequence while warping terminates
// it (enb.warp_stop), so there is no distinct client stop opcode.
//
// The radar/targeting controller `this` is a session singleton. The primary source is
// still the read-only ECX capture at WarpPathBuild's entry (hooks::warp_radar_ctrl),
// populated once the client has built a warp path at least once this session. But it
// IS also reachable by a fixed deref chain off M, with NO orb click needed to seed the
// hook: the orb handler loads SC = *(handlerThis + 0x40) (SC == M, the world/space
// client singleton), then MainView = *(SC + 0x1354) (SC's active view slot -- a HUD
// controller, NOT the radar), and the radar hangs off it at MainView + 0x80:
//
//     radar = *( *(M + warp::mainview_on_m) + warp::radar_in_mainview )
//
// The radar is a small (~0x50-byte) object built lazily during MainView init and
// stored ONLY there (no global handle), so *(MainView + 0x80) can be 0 before the 3D
// view is up. lua_api walks this chain and fully validates the radar's structure
// (warp::* offsets below) before ever calling into it, so enb.warp engages with zero
// screen interaction. See resolve_warp_radar.
constexpr uintptr_t WarpPathBuild = 0x007bd980;
constexpr uintptr_t WarpNavVec = 0x007bd960;
constexpr uintptr_t WarpPacketCtor = 0x008b1aa0;
constexpr uintptr_t WarpPacketDtor = 0x008b1c70;

// Offsets used to resolve + validate the radar controller off M without the ECX hook.
// All are read guarded (VEH-backed) BEFORE any native call, so a wrong candidate fails
// validation and is rejected rather than hanging the client. The radar's first member
// (radar + 0) is itself an SC-family object m0; subsys/subsys_flag/getdata_vtoff below
// are read off m0, while the nav-vector blocks live directly on the radar.
namespace warp {
constexpr int mainview_on_m = 0x1354; // M -> MainView (SC's active-view slot; a HUD controller)
constexpr int radar_in_mainview =
    0x80;                      // MainView + 0x80: the radar object (0 until the view is up)
constexpr int subsys = 0x12a4; // m0 (== *radar) + 0x12a4: targeting subsystem WarpPathBuild derefs
constexpr int subsys_flag = 0x10;   // (m0+subsys) + 0x10: "path already built" byte flag
constexpr int nav_vec = 0x40;       // radar + 0x40: primary nav vector control block
constexpr int nav_vec2 = 0x3c;      // radar + 0x3c: secondary nav vector control block
constexpr int vec_begin = 0x08;     // vector control block -> begin pointer
constexpr int vec_end = 0x0c;       // vector control block -> end pointer
constexpr int getdata_vtoff = 0x28; // m0->vtable[+0x28]: the getter WarpPathBuild invokes
} // namespace warp

// ---- front-end login flow (EULA / login / character select) -----------------
// The pre-game screens are driven by a single LoginTask object and its state
// machine. We capture the LoginTask `this` (ECX) read-only from the per-frame
// run loop and drive the env-fed auto-login (see namespace login + autologin.cpp).
//
// LoginRunLoop (__fastcall(ECX = LoginTask)): the main front-end
// loop. Runs EVERY frame while on the EULA/login/charselect screens and dispatches
// the state machine. We hook it READ-ONLY to capture the LoginTask `this` -- the
// exact capture pattern of WorldMgrInit, but for the front end. It is the one
// place the LoginTask pointer is reliably available before we are in the world.
constexpr uintptr_t LoginRunLoop = 0x00767f50;
// EulaAccepted (no args) -> nonzero when the EULA has been accepted (reads the
// "Trial" registry value; 0 == accepted/not-a-trial). We do not call it; we set
// the registry value it reads (autologin EULA accept).
constexpr uintptr_t EulaAccepted = 0x005fade0;
// EditSetText (__thiscall(this = edit widget, char* text, int append)): replaces
// (append == 0) the text of a login edit widget. The credential view's username
// and password widgets (login::view_user_widget / login::view_pass_widget) take
// this; setting the password widget shows the masked dots, exactly as typing does.
constexpr uintptr_t EditSetText = 0x006aa570;
// CredentialAccept (__thiscall(this = credential view), no args): the work the
// Login button does -- reads the two edit widgets and kicks off authentication.
// NON-BLOCKING: it returns immediately and the client's own per-frame run loop
// pumps the result. Safe to call from the tick (a blocking handler would wedge
// it). Driven with the credential-view `this`, NOT the LoginTask. NOTE: this path
// does NOT advance the front-end state off login::state_login (1) -- the
// char-select screen never renders; the avatar list instead arrives and populates
// the inline name slots, which is the signal auto-login waits on (see autologin.cpp).
constexpr uintptr_t CredentialAccept = 0x005ae450;
// CharEnter (__thiscall(this = LoginTask), no args): resolves the character at
// login::sel_index to its galaxy id and requests entry into the world. Also
// non-blocking.
constexpr uintptr_t CharEnter = 0x00769400;

// ---- hulk / loot cargo enumeration ------------------------------------------
// When you prospect/loot a wreck or resource, the client shows a "hulk cargo"
// grid. Its per-slot data is read through three inventory-container accessors,
// all __thiscall with ECX = the cargo CONTAINER object (an embedded sub-object at
// InvData+0x54). We do NOT reach the container by offset-walking a UI object;
// instead we CAPTURE its pointer read-only from CargoTemplateID's ECX (it is
// called once per occupied slot every time the grid repaints), then replay these
// same accessors to enumerate. The container also carries its slot count
// precomputed at container + cargo::slot_count, and a char* inventory-name
// at container + cargo::inv_name_ptr (the discriminator between "Cargo"/hulk and
// ship inventory).
//   CargoTemplateID (ECX=container; arg: u32 slot) -> ItemTemplateID; 0xFFFFFFFF
//     = empty slot, 0xFFFFFFFE = error (skip both). We also hook this for capture.
//   CargoStackCount (ECX=container; arg: u32 slot) -> stack quantity (0 if empty).
//   CargoTemplateAt (ECX=container; args: u32 slot, then char mode = 0 pushed as a
//     dword) -> item-template object pointer, or 0 for an empty slot. The template
//     holds the item name (char* at cargo::tmpl_name_ptr, length at
//     cargo::tmpl_name_len) and a resolved icon asset at cargo::tmpl_icon_asset.
constexpr uintptr_t CargoTemplateID = 0x00689ca0;
constexpr uintptr_t CargoStackCount = 0x00689d20;
constexpr uintptr_t CargoTemplateAt = 0x0068aaa0;
// InvMoveBuild (__thiscall, ECX = a caller-allocated command object; args: u32
// GameID, u32 FromInv, u32 FromSlot, u32 ToInv, u32 ToSlot, u32 Num) -- the in-place
// constructor for the INVENTORY_MOVE command (wire opcode 0x0027). It stamps the
// command vtable (whose serializer emits opcode 0x27 with every field in network
// byte order) and the six fields, spanning 0x24 bytes. See namespace cargo for the
// full contract. The built object is pushed via CmdSend.
constexpr uintptr_t InvMoveBuild = 0x00894ca0;
// ClientOperatorNew (__cdecl, arg: u32 size) -> heap block. The client's own
// operator new. Command objects handed to the send path are freed by the client CRT
// heap, so they MUST be allocated here (a DLL/static buffer would be a cross-heap
// free -> crash).
constexpr uintptr_t ClientOperatorNew = 0x004e9cb0;
} // namespace addr

// Front-end LoginTask layout (build-constant field offsets, runtime-validated).
// Only the LoginTask pointer and the per-run credential-view pointer vary per
// launch; these offsets do not. The credential view is a child object located by
// signature each run (see autologin.cpp): its back-pointer (view_owner) equals the
// LoginTask, and its two edit-widget slots point at objects whose first dword is
// the edit-widget vtable. The username/password text lives in the widgets, not in
// any LoginTask copy, so auth only sees text written through EditSetText.
namespace login {
constexpr unsigned state = 0x24; // int front-end state
constexpr int state_login = 1;   //   login / pre-game screen up
// NOTE on the state field: when credentials are submitted programmatically, this
// client build authenticates and fills the character-name slots below WITHOUT
// flipping +0x24 off the login value (the character-select screen never renders).
// So the avatar list arriving is detected by the NAME SLOTS becoming populated,
// not by a state change -- the auto-enter gate watches the slots, not +0x24.
constexpr unsigned view_owner = 0x9c;                // credential view -> back-ptr to LoginTask
constexpr unsigned view_user_widget = 0xa4;          // credential view -> username edit widget
constexpr unsigned view_pass_widget = 0xa8;          // credential view -> password edit widget
constexpr uintptr_t edit_widget_vtable = 0x00afda10; // first dword of an edit widget
constexpr unsigned char_name_base = 0x424;           // char[0] name slot
constexpr unsigned char_name_stride = 0x10c;         // stride between char name slots
constexpr int char_max = 8;                          // character slots to scan
constexpr unsigned sel_index = 0x1080;               // selected character index (int)
} // namespace login

// Renderer globals. The engine creates one IDirect3DDevice8 at startup and
// stores it in a static global (build-constant address, no ASLR). It is null
// until the renderer initializes -- a few frames after process start -- so
// readers must treat 0 as "not yet" and retry. The overlay takes the Present
// vtable slot straight off this device (see overlay.cpp poll()).
namespace render {
constexpr uintptr_t device = 0x00c00660; // IDirect3DDevice8* (null until renderer up)
} // namespace render

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

// World/player manager M field offsets, off the M captured at addr::WorldMgrInit
// (hooks::world_mgr()). These are the two fields the target-action dispatch needs:
// the local player's game id (the command's actor) and the live sector-server
// Connection the built command is pushed through. Build-constant offsets, confirmed
// against the client's own command paths (the same M[player_id] / M[connection] the
// group-verb and target-button handlers use). Only the M pointer varies per run.
//
// The dispatch target's game id is NOT on M -- it is read off the captured current-
// target object (hooks::target_obj()): the contact object stores its own GameID at
// +tgt_gid (the same field the client's gid->object lookup validates), and its
// properties/aux bag (hull/shield/name) hangs off +tgt_container. The native verb
// dispatcher sends the +tgt_gid GameID directly; it is NOT a field of the aux bag.
namespace world {
constexpr int player_id = 0x112c;  // M -> local player game id (command actor)
constexpr int connection = 0x1124; // M -> sector-server Connection (command sink)
constexpr int navlist = 0x1138;    // M -> persistent NavigationList (warp packet input)
constexpr int sector_id = 0x12f0;  // M -> current sector id (matches the sectors table id)
// The INVENTORY/LOOT command channel is NOT M's connection. The native loot/mining
// take commits the InvMove through the SClient that OWNS the open cargo container:
// C = *(container + cargo::view_client), and the command is pushed on C's Connection
// (C + connection == 0x1124, the same offset as on M). The InvMove GameID field is
// the value the SClient stashes at C + sub_client_gid (0x1130) for the take. Reaching
// the sink via M's own connection does NOT transmit the inventory command -- it must
// go through the container-owner SClient, so we fetch C off the container exactly as
// the native commit does rather than off M.
constexpr int sub_client_gid = 0x1130; // SClient -> InvMove GameID field (the take actor id)
constexpr int tgt_container =
    0x88;                     // target contact object -> properties/aux bag (hull/shield/name)
constexpr int tgt_gid = 0x90; // target contact object + 0x90 -> its own GameID (command target)
// M also holds the session ENTITY TABLE keyed by GameID -- the same map the native
// group HUD resolves each member through to reach its render object and read its
// vitals. It is a fixed 0x101-bucket chained hash on M + ent_buckets: bucket =
// gid % ent_modulus; each node links to the next via ent_node_next, keys on
// ent_node_gid, and points to the contact object at ent_node_obj. Walk it with
// pure guarded reads (no native call -- avoids any calling-convention hazard on
// the live client), matching ent_node_gid and taking ent_node_obj; a GameID with
// no live entity (member in another sector / not yet in scene) simply misses. The
// resolved contact object's +tgt_container aux bag then yields hull/shield exactly
// as enb.target() reads them.
constexpr int ent_buckets = 0x38;       // M -> base of the GameID hash bucket array
constexpr unsigned ent_modulus = 0x101; // bucket index = gid % ent_modulus
constexpr int ent_node_next = 0x04;     // node -> next node in its bucket chain
constexpr int ent_node_obj = 0x10;      // node -> resolved contact object
constexpr int ent_node_gid = 0x14;      // node -> key (GameID)

// In-world docked/in-space discriminator, both fields on the SClient world manager
// M. When the player is docked the station interior view controller lives at
// M + station_view (non-zero) and the current StarbaseID at M + starbase_id
// (non-zero); both are cleared to 0 the moment the player launches into space. So
// docked == (*(int*)(M + station_view) != 0), and starbase_id is a redundant
// confirm that also yields the station's GameID. Set/cleared by the client's
// STARBASE_SET (0x4F) handler. This is how enb.state() names "station" vs "space"
// without a global state enum (the client has none -- it uses task polymorphism:
// a LoginTask means front-end, an M means in-world, then station_view splits it).
constexpr int station_view = 0x135c; // M -> station interior view ptr (non-zero == docked)
constexpr int starbase_id = 0x1324;  // M -> current StarbaseID (0 while in space)
} // namespace world

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
// get_shared(container, keybuf) -> entry|0: __cdecl. Resolves a SHARED /
// network-replicated, delta-interpolated auxdata entry (a different list than
// get_value's) -- this is where ShieldPercent lives. get_value returns 0 for
// these keys (their +val_off slot is empty); the live value is a 0..1 float at
// entry + shared_val_off, guarded by the same valid_off flag. Present for both
// the local player and any in-range remote (so a group member's current shield
// is readable client-side).
constexpr uintptr_t get_shared = 0x005bf2b0;
// A shared entry is DELTA-INTERPOLATED, so it carries two floats: the settled
// snapshot value at +0x23c (the last value the server replicated -- what the
// interpolator eases TOWARD and clamps against) and a volatile per-frame
// animation scratch at +0x248. +0x248 is written by the client's interpolator
// each frame and, once an interpolation window elapses, is EXTRAPOLATED to the
// draining/filling extreme (0.0 while a shield is dropping, 1.0 while rising) --
// so a raw read of +0x248 catches a shield mid-drain reading 0 the instant the
// target is fired on, even though its true shield is nonzero. We want the
// authoritative snapshot, not the animation, so we read +0x23c. Reading it is a
// pure side-effect-free read; running the interpolator ourselves would MUTATE
// the entry (+0x240/+0x244/+0x248/+0x24c) and fight the client's own per-frame
// pass. The settled read yields +0x23c directly, confirming it is the value.
constexpr int shared_val_off = 0x23c; // entry + 0x23c -> float (0..1) settled snapshot value
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

// Party/group roster -- the "GroupInfo" entry in the SAME property-bag container
// the RPGInfo levels live in (rpg::container_off, +0x12c0, off the avatar object).
// Unlike the value/level entries (get_value / get_entry, which return an ENTRY whose
// data is at entry+val_off), GroupInfo is an OBJECT-typed entry: get_object returns
// the group object DIRECTLY (no val_off indirection). get_object is __cdecl(container,
// keybuf) like the others -- internally it routes the container to a __fastcall index
// builder (container+0x94) and the keybuf to a __thiscall red-black-tree search, but the
// public entry point takes both on the stack, so the same typedef as get_value applies.
// The group object holds the roster:
//   +0x70  active flag (nonzero when the local player is in a group)
//   +0x674 member-array begin ptr   +0x678 member-array end ptr
//          member count = (end - begin) / 4, capped at max_members
// Each member is a heap object pointer:
//   +0x120 name (char*)   +0x1a8 GameID (uint32)
// Keys are built with aux::build_key. Read under the VEH fault guard.
namespace group {
constexpr uintptr_t get_object = 0x005927e0; // __cdecl(container, keybuf) -> group object|0
constexpr int active_off = 0x70;             // group + this -> nonzero when in a group
constexpr int members_begin = 0x674;         // group + this -> member-array begin ptr
constexpr int members_end = 0x678;           // group + this -> member-array end ptr
constexpr int member_name = 0x120;           // member + this -> name char*
constexpr int member_gameid = 0x1a8;         // member + this -> GameID (uint32)
constexpr int max_members = 5;               // roster cap (UI shows at most 5 rows)
// The client's own "am I the group leader?" check -- the exact function the native
// group window runs at init to decide whether its top button reads GRP DISBAND
// (leader) or GRP LEAVE (member). __fastcall(avatar object) -- the same
// container-holding avatar base the roster read uses (it looks up the GroupInfo
// entry off base+rpg::container_off itself, so a single-ECX call_thiscall with no
// stack args is the right calling shape). Returns nonzero when the local player
// leads their current group; zero when member / solo. Call under the VEH guard.
constexpr uintptr_t is_leader = 0x0074d690; // __fastcall(avatar base) -> leader?
} // namespace group

// Chat line RING-BUFFER layout, off the ring object captured at addr::ChatLineAppend
// (hooks::chat_buf() == 0x0067d780's ECX). NOT a sorted vector -- it is a fixed-size
// ring of 0x14-byte slots, indexed (write_count % capacity). Confirmed from
// 0x0067d780's body:
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

// Hulk / loot cargo container + item-template field offsets. The container pointer
// is the ECX captured at addr::CargoTemplateID; the template pointer is what
// addr::CargoTemplateAt returns for an occupied slot. All CONFIDENT from analysis.
namespace cargo {
constexpr int inv_name_ptr = 0x04;    // container -> char* inventory-name ("Cargo" for a hulk)
constexpr int view_client = 0x4c;     // container -> owning SClient (the InvMove send holder)
constexpr int slot_count = 0x44;      // container -> uint precomputed slot count
constexpr int tmpl_name_ptr = 0x18;   // item template -> char* narrow item name
constexpr int tmpl_name_len = 0x1c;   // item template -> int name length
constexpr int tmpl_type = 0x0c;       // item template -> ItemType/category
constexpr int tmpl_icon_asset = 0x10; // item template -> resolved icon asset object
constexpr unsigned tid_empty = 0xFFFFFFFFu; // CargoTemplateID sentinel: empty slot
constexpr unsigned tid_error = 0xFFFFFFFEu; // CargoTemplateID sentinel: error

// ---- loot TAKE (send a single slot into your own cargo) ---------------------
// Looting one slot is the exact command the native loot/mining window's take emits:
// an INVENTORY_MOVE command (wire opcode 0x0027), NOT the compact 0x5D command (that
// 0x5D "use-inventory-item" builder is never emitted on a harvestable take -- proven
// by a read-only hook on it staying silent while a native resource take succeeded).
// The InvMove payload is six u32s (all network byte order on the wire):
//   GameID  -- the actor's game id (sub-client + world::sub_client_gid == 0x40000020)
//   FromInv -- the source container's kind: LOOT/HUSK window = 0x06, MINING window
//              (harvestable resource) = 0x12 (derived from the container's inventory-
//              name -- "Harvest..." => mining window)
//   FromSlot-- the slot to take
//   ToInv   -- destination inventory: 1 (the player's cargo hold)
//   ToSlot  -- 0xFFFFFFFF (server auto-selects a free cargo slot)
//   Num     -- 1 (the harvest take moves the whole slot; the server's FromInv=18
//              case reads only FromInv + FromSlot, so ToInv/ToSlot/Num are matched to
//              the native emitter but not consumed for a mining-window take).
//   InvMoveBuild (game::addr::InvMoveBuild) is an IN-PLACE constructor over a
//     caller-allocated object; it installs the command vtable + the six fields,
//     spanning inv_move_size bytes. The object MUST be allocated with the client's
//     own operator new (game::addr::ClientOperatorNew) -- the send path takes
//     ownership and frees it through the client CRT heap, so a DLL/static buffer
//     would be a cross-heap free. The built object is pushed on the sub-client's
//     Connection via CmdSend (see world::sub_client_vtbl).
constexpr unsigned char inv_type_hulk = 0x06; // FromInv: loot/husk window (server case 6)
constexpr unsigned char inv_type_harvest =
    0x12; // FromInv: mining window / harvestable (server case 18)
constexpr uint32_t inv_move_size =
    0x2c; // InvMove command object span (ctor writes to +0x20; >=0x24)
} // namespace cargo

} // namespace game
} // namespace enb
