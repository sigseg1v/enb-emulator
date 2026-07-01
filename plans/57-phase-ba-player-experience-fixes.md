# Phase BA -- player-experience fixes (multibox HUD, party frame, launcher profiles, chat)

Owner-reported batch (2026-06-30). Six independent issues; track each to done.
Investigation agents dispatched first to ground each fix in real code; sections
below get filled with file:line + mechanism as findings land.

Buckets: BA-1/BA-2 are enbmod client mods (Win32, WINE -- CC/Net7 dirs under
`freya/client-injection/enbmod`, MIT only where genuinely new Freya code).
BA-3/BA-4/BA-5 are the Avalonia launcher (`tools/LaunchFreya`). BA-6 is
server/proxy chat routing (governed by CLAUDE.md server-integrity rules:
primary-source proof + CLI parse/test first + plans/29 CV entry).

## Status

| Item | Summary | State |
|------|---------|-------|
| BA-1 | Multibox HUD reads the wrong process's stats (both clients show the last-launched char) | [x] REFUTED -- live in-space 2-box shot: each HUD shows its OWN char (GRIEVERTE 12/12 hull LV0 vs DEVUSERTT 3400/3400 hull LV75), zero cross-bleed. Symptom is identity duplication (same char on both slots), fix = per-slot identity (BA-3/BA-5) |
| BA-2 | No party/group member frame in the new HUD (old UI showed name/hull/shield bars for up to 5) | [x] roster frame + per-member hull AND shield (GameID->entity; shield via shared-auxdata ShieldPercent, BA-2b item 5). Verified live grouped 2-box, both directions: hull+shield bars fill correctly, no phantom slots, self-card keeps own identity |
| BA-3 | Launcher Profile section: per-profile config files, dropdown + add/delete, Default special-cased | [x] done (builds clean) |
| BA-4 | Launcher Resolution dropdown + Fullscreen checkbox under Client EXE, applied on Play | [x] done (builds clean) |
| BA-5 | Launcher optional Username / Password / Character fields -> auto-login env; autosave (not password) | [x] done (builds clean) |
| BA-6 | Broadcast chat does not reach other players (only echoes to sender) | [x] fixed |

## BA-1 -- multibox HUD reads the wrong process

Symptom: two clients (devusertt, grievertt); both HUDs show the most-recently
launched char's stats. A DLL in process A is sourcing process B's state -> some
cross-process shared state, not per-process memory.

Owner suggestion (2026-06-30): have the launcher give each launched game a unique
window name so a mod can identify + bind to just its own one ("or if you have a
better way then go ahead").

Candidate fixes (pick after the agent confirms the actual root cause):
- **(a) per-process window bind in the DLL (preferred if it's a window-enum bug).**
  If the mod finds "the game window" by enumerating windows of class `client.exe`
  and grabs the global most-recent, two clients collide. Fix WITHOUT launcher help:
  resolve the window owned by THIS process (EnumWindows + GetWindowThreadProcessId
  == GetCurrentProcessId). Self-contained, no unique-name coordination.
- **(b) per-instance keying of any shared named resource.** If the leak is a named
  shared-memory / mutex / file mapping (collides only inside ONE WINE prefix's
  Win32 namespace), suffix the name with the PID or the slot's FREYA_GAME_PORT_BASE
  (the DLL already has it). More robust than window names.
- **(c) unique window title from the launcher (owner's idea).** Launcher sets a
  per-instance title/env; the DLL keys off it. Works, but needs launcher<->DLL
  coordination; only better than (a) if the DLL genuinely needs an external key.
- Note: my multibox redesign already gives slot>=1 a SEPARATE WINE prefix
  (~/.wine-enb-mboxN) -> separate wineserver -> isolated Win32 object namespace,
  so named-object collisions only bite when both clients share ONE prefix; an
  absolute HOST-path state file would leak regardless of prefix. The agent's
  finding decides which of (a)/(b)/(c) applies.

FINDINGS (2026-06-30, read the actual enbmod source, not an agent):

The reported mechanism -- "a DLL in process A is sourcing process B's state"
through memory -- is NOT possible in the enbmod code as written. Every HUD data
source is per-process:
- `enb.self()` / `enb.vitals()` / `enb.target()` / `enb.aux()` read the CURRENT
  process via hook-captured `this` pointers: `g_vitals_ctrl`, `g_rpg_mgr`,
  `g_target_ctrl`, `hooks::target_obj()` (hooks.cpp). Each is a per-DLL-instance
  static captured from ECX by a hook running INSIDE that client. Two clients each
  capture their own.
- There is NO `ReadProcessMemory` / `OpenProcess` / `CreateFileMapping` /
  `MapViewOfFile` anywhere in enbmod (grep-confirmed). `mem::*` reads are all
  same-process. `player_ptr_addr` defaults to 0 and freya-hud does not use the
  calibrate path at all; it is purely hook-driven.
- The only window enumerations (actions.cpp, autologin.cpp) already filter by
  `GetWindowThreadProcessId == GetCurrentProcessId()` -- correctly per-process.
- The only shared HOST files between two same-dir instances are enbmod.log /
  enbmod.cmd / (autocalibrate's) calib_data.lua. None feeds the HUD. Even if
  shared, an address written by B is meaningless in A's address space -- a number
  cannot carry B's object into A's process.

A number in A's address space cannot resolve to B's character. So "both HUDs show
grievertt (the last-launched)" is consistent with exactly ONE thing: both clients
are actually ON grievertt -- an IDENTITY/login duplication, not a memory read.
That is a launcher/env issue, not an enbmod bug. The `play-local` multibox path
launches each slot as a SEPARATE launcher process (separate env), and the GUI
launcher sets the credential env + CreateProcess synchronously per Play click, so
there is no obvious env race in either path GIVEN distinct credentials per slot.

Per-slot distinct identity is exactly what BA-3 profiles + BA-5 login fields now
enable: one profile per character (each carries its own AccountName/CharacterName
-> its own FREYA_ACC_NAME/FREYA_CHARACTER at its own launch). So the supported fix
for "each box on its own character" is: distinct profile per slot.

OPEN (needs a live repro to pin, do NOT fake a fix): confirm whether, in the
owner's repro, the two clients were genuinely logged into DIFFERENT characters
server-side while both HUDs showed grievertt (which would contradict the code and
mean there is a shared-state path I have not found -- re-investigate then), OR
both ended up logged into grievertt (identity duplication -> the per-profile path
above is the fix, and the "wrong process memory" framing was a misdiagnosis).
Options (a)/(c) below (per-process window bind / unique window title) are NOT
implemented: the per-process bind is ALREADY correct, and a unique title that no
mod consumes would be dead code (CLAUDE.md no-dead-code).

LIVE 2-BOX SESSION (2026-06-30, this box, distinct creds per slot):
- slot0 `just play-local devuser devpass devusertt` (SHARED freya-dev proxy),
  slot1 `just play-local griever <pw> grieverte` (private mbox2 proxy, cloned
  ~/.wine-enb-mbox2 prefix). Both windowed 1280x960, both reached in-game.
- Server confirmed TWO DISTINCT avatars logged in simultaneously:
  `player 0x40000022 'devusertt' fully logged in` and
  `player 0x40000029 'grieverte' fully logged in`. NOT the same char on both.
  => the "both HUDs show the last-launched char" symptom cannot arise here: each
  HUD reads ITS OWN process's per-char state, and the two processes are on
  different chars. BA-1 does NOT reproduce with distinct per-slot credentials.
  This is the expected outcome given the code findings above; it makes the
  supported fix explicit -- give each box its own identity (BA-3 profile / BA-5
  login fields per slot). The "wrong process memory" framing was a misdiagnosis;
  the real failure mode is identity duplication (both slots launched onto the
  same char), which distinct creds prevent.
- A duplicate-login collision IS easy to trigger by accident and is worth noting
  as the likely origin of the owner's report: relaunching slot0 while a prior
  devuser session was still resident produced
  `Account user devuser trying to log in twice, removed` + `avaLogout` of the
  first session, bouncing that client to the login screen with "That account is
  already logged in." If a user launches two boxes on the SAME account/char (or
  reuses one profile for both), one box wins the char and the other is a
  duplicate -> looks like "both on the same char" == the BA-1 symptom.

DOCKED-VISUAL CONFIRMATION (2026-06-30 relaunch, both boxes windowed 1280x960):
captured each window individually (window->char mapped via PID->WINEPREFIX, then
`import -window <id>`). The two HUDs are NOT cross-bleeding -- each renders its
OWN char in its OWN station: griever's chat reads "Welcome to Guiana Spaceport,
home of the Good Earth Trading Company's Sol Space Division"; devusertt's reads
"DOCK OFFICER: Landing clearance granted. Welcome to InfinitiCorp's Loki
Station!". Different chars, different stations, different per-char chat/dock
messages in each window. This is the visual half of the refutation at the DOCKED
state (the docked HUD is otherwise minimal -- chat + bottom buttons -- since
`enb.self().base==0` while docked, so vitals render nothing either way).

IN-SPACE VISUAL -- DELIVERED (2026-06-30, owner undocked both boxes manually):
side-by-side, both HUDs populated in space, each showing its OWN char's distinct
vitals with NO cross-bleed whatsoever:
- griever/slot0 self-frame: `GRIEVERTE`  LV 0,  hull 12/12,   shield 50/50,
  energy 134/134, C/E/T all LV 0, in Equatorial Earth Sector.
- devusertt/slot1 self-frame: `DEVUSERTT` LV 75, hull 3400/3400, shield
  126822/126822, energy 4389/4389, C/E/T LV 25, in High Earth Sector.
devusertt was launched SECOND (the "most-recent" char BA-1 says both HUDs would
show). griever's HUD does NOT show devusertt's 3400 hull -- it shows griever's
own 12. That is the direct, populated-HUD refutation of BA-1. Shots:
/tmp/enbshot/mbox/{griever,devusertt}-space.png. BA-1 CLOSED as refuted; the
supported multibox fix remains per-slot identity (BA-3 profiles / BA-5 login
fields), NOT any change to how the HUD reads state.

(Undock automation is still an open TOOLING gap -- undock.sh can't drive a fresh
concourse to space under XTEST, see below -- but it is no longer blocking BA-1,
since the owner flew both boxes out by hand for the shot.)

UNDOCK-AUTOMATION GAP (found while trying to get both boxes to space):
`.claude/skills/explore-sector/scripts/undock.sh` cannot drive a FRESH station
dock to space on this WINE client. Two reasons: (1) its `turn()` (avatar
rotation) uses arrow **keys** (`xdotool key Left/Right`), and keyboard input does
NOT land on the WINE client under XTEST (no keyboard focus) -- so undock.sh can
only walk straight forward (walk_forward uses RMB-held, which is mouse and DOES
land), never turn to face the launch bay. (2) undock.sh was built for the
POST-TOW hangar layout (you spawn already in the launch bay); a fresh dock drops
you in the multi-room CONCOURSE (Loki: Manufacturing terminal cluster ->
glowing pad room -> yellow "To Main Deck" wayfinding beacons -> ... -> launch),
which undock.sh has no navigation for. Verified live that RMB-drag mouse-look
(hold button 3, sweep the pointer horizontally) DOES rotate the avatar under
XTEST -- that is the fix for (1). Concourse beacon-following ("To Main Deck" +
blue arrow = walk this way, not a click target) would need to be added for (2).
Until undock.sh is reworked, the reliable path to the in-space visual is the
OWNER flying both boxes out (matches this phase's owner-client-verify pattern).

## BA-2 -- party/group member frame

Old UI (right side): up to 5 party members as stacked bars (name, hull, shield).
New enbmod HUD (Phase AS overhaul) lacks it. Need a party-frame panel reading the
client's group roster.

DONE -- roster + per-member vitals (client-only mod, no wire change, no CV gate;
every game-memory read is VEH-guarded, so a wrong offset shows garbage / nothing,
never crashes). Builds clean (`just build-enbmod`):
- `enb.group()` binding (`src/lua_api.cpp`, `l_group`) + offsets in `game.h`
  (`namespace group`, plus the entity-table offsets in `namespace world`).
  Returns `{count=N, members={{name=, gameid=, [hull=, hull_max=, shield=,
  shield_max=]}, ...}}`; empty when solo / out of space / before capture.
- Roster retrieval (VERIFIED in decomp): GroupInfo is an OBJECT-typed aux entry in
  the SAME property-bag container the RPGInfo levels use (avatar object +
  `rpg::container_off` = +0x12c0). Fetched with `group::get_object` = `0x005927e0`,
  __cdecl(container, keybuf) -> group object|0. Group obj: `+0x70` active flag,
  `+0x674`/`+0x678` member-array begin/end (count = (end-begin)/4, max 5). Member
  obj: `+0x120` name char*, `+0x1a8` GameID. The member struct is 700 bytes
  (0x2BC) and stores ONLY name/GameID/formation/position -- hull/shield are NOT on
  it (confirmed at the member constructor write-site; the old "member +0x12c0
  vitals" note was wrong).
- Per-member hull/shield: the native group window resolved each member's GameID to
  its live contact object and read vitals off that object's aux bag (the ordinary
  0x001B AUX_DATA path, not group-specific). `l_group` does the same:
  `entity_by_gid(gameid)` walks the world manager's GameID hash table (M +
  `world::ent_buckets` 0x38, `gid % 0x101`, node next +0x04 / obj +0x10 / key +0x14
  / flags +0x18) with PURE guarded reads -- no native call, so no
  calling-convention hazard on the live client -- then reads HullPoints /
  MaxHullPoints / MaxShieldPower / ShieldPercent off contact+`world::tgt_container`
  (+0x88) via the existing `aux_read_f`, identical to `enb.target()`
  (shield = MaxShieldPower * ShieldPercent).
- LIMIT (by design, not a bug): per-member vitals exist only for members in the
  current sector / scanner range; a member elsewhere has no live entity, so
  `entity_by_gid` misses and the roster omits that member's vitals. party_frame
  then draws empty bar tracks for that member (target_frame's vitals-less
  convention) while still showing the name. Same-sector groupmates -- the common
  case -- get full hull/shield bars.
- Panel `scripts/mods/freya-hud/party_frame.lua` (modelled on `target_frame.lua`,
  `H` module). Right-edge glass panel below the radar, stacked down, one row per
  member: name + hull bar + shield bar (draw_bar = target_frame's, empty track when
  a vital key is absent). Registered in `init.lua`. Draws only while `count > 0`.

CLIENT-VERIFY (owner, async -- not a blocker): confirm against a live grouped
client that (a) the roster shows the right members, (b) a same-sector groupmate's
hull/shield bars track that member's actual vitals (not the local player's), and
(c) a remote/other-sector member shows name with empty bars rather than stale or
wrong values. The entity-table offsets (M+0x38 bucket base, node layout) are from
decomp analysis and have not yet been exercised against a live group.

### BA-2b -- three grouped-HUD defects (owner report, 2026-06-30)

Reported on a live 2-box grouped session (griever + devusertt, both in space):

1. **Party frame shows phantom empty "Member" slots.** `enb.group()` returned
   `count=5` with only slot 1 real and slots 2-5 `gameid==0` (confirmed live:
   `count=5 [1]devusertt/40000022 [2]nil/0 [3]nil/0 [4]nil/0 [5]nil/0`). The
   member array is a FIXED `max_members` (5) block; `l_group` walked all 5 and
   emitted the empty ones. FIX (`src/lua_api.cpp`, `l_group`): skip any slot with
   `gameid==0` so `count` is the real member count. Post-fix this session would
   report `count=1` (devusertt), so `party_frame.lua` (already loops only real
   members) draws exactly one bar-set, no placeholders. [x] built, logic-validated
   live; needs relaunch to load the DLL for the visual.

2. **Party frame too close to the right edge.** `party_frame.lua` `MARGIN_R`
   12 -> 28 (inset 16px more). [x] done.

3. **Self player-card falls back to "PILOT" and loses hull/shield when grouped.**
   ROOT CAUSE (confirmed live): the native party/group member bars are repainted
   through the SAME EnergyBar updater (`0x005dc4a0`) that `hk_InSpace` hooks to
   capture the vitals controller, so a member-bar controller clobbered
   `g_vitals_ctrl`. That controller reads back `data@+0x04 == 0` (no player-data
   backref) and zeroed fill fractions (`Efrac=-1 Sfrac=0 Hfrac=0`), so
   `enb.vitals()` returned `name=nil, hull=0, shield=0` -> the card showed
   `PILOT` / empty bars. The player's OWN controller always carries a non-null
   player-data object at +0x04 (the chain `vitals()` walks to the character name).
   FIX (`src/hooks.cpp`, `notify_inspace`): only latch `g_vitals_ctrl` when
   `*(thisp + player::ctrl_data) != 0`, so member bars never overwrite the real
   controller; the heartbeat (`g_last_inspace_tick`) stays unconditional. [x]
   built, root-caused live; needs relaunch to load the DLL for the visual.

All three are client-only enbmod mods (freya/ MIT, no wire change, no CV gate;
every game-memory read is VEH-guarded). DLL rebuilt clean (`just build-enbmod`).
The two DLL fixes (1, 3) require relaunching the client to load; #2 is Lua.

RELAUNCH (2026-06-30): both boxes torn down and relaunched on the fresh DLL
(griever slot0 shared proxy / devusertt slot1 mbox2), both back in space with the
new `enbmod.dll` staged. Verified SOLO: the self-card keeps name +
hull/shield/energy (griever GRIEVERTE 12/12 50/50 134/134; devusertt DEVUSERTT
3400/3400 126822/126822 4389/4389) and the low-res shift (#4) clears the chat
overlap at 960p.

VERIFIED GROUPED (2026-06-30): re-grouped the two boxes (`/invite devusertt` from
griever via chat, Accept on devusertt) and confirmed live:
- **Defect 1 (phantom slots): GONE.** `enb.group()` reports exactly one member each
  way; `party_frame.lua` draws a single bar-set, no empty "Member" placeholders.
- **Defect 3 (self-card PILOT/vitals): GONE.** Each box's self-card keeps its OWN
  name + vitals while grouped (griever's card = grieverte's, devusertt's =
  devusertt's -- not mixed). The `g_vitals_ctrl` identity latch holds.
- **Party-frame HULL: filled** both directions (griever sees devusertt 3400/3400;
  devusertt sees grieverte 12/12) -- the `entity_by_gid` gid-match-before-flag-break
  fix.
- **Party-frame SHIELD: filled** both directions (griever sees devusertt
  126822/126822; devusertt sees grieverte 50/50) -- the shared-auxdata fix in item 5
  below. Screenshot-confirmed: each party frame draws BOTH the red hull bar and the
  blue shield bar full, previously the shield track was empty.

4. **Player card overlaps the bottom-left chat box at low res** (owner,
   2026-06-30). At 1280x960 the card's left edge (sw/2 - PC_W = 416) sits ~66px
   inside the chat box (X=22, W=460 -> right edge 482). FIX (`freya_ui.lua`
   `layout()`): nudge the card RIGHT by `PC_LOWRES_DX` (72), full at the 1280x960
   reference and decaying LINEARLY to 0 by 2560x1440 via `PC_LOWRES_DX -
   H.sx(PC_LOWRES_DX)` (clamped >= 0) -- the inverse of the HUD's usual H.sx
   growth. Larger screens keep the current centered position EXACTLY (the
   centerline moves right on its own there, so the card never touches the chat).
   Verified across 1280x960 / 1440x1080 / 1920x1440 / 2560x1440: card clears the
   chat at every step; dx = 72/63/36/0. Pure Lua. [x] done.

5. **Party-frame SHIELD bar stayed empty for group members** (owner: "hull and
   shield on party frame both missing still", 2026-06-30). Once the hull fix landed,
   the member hull bar filled but the shield bar remained an empty track. ROOT
   CAUSE: current shield is a 0..1 percent, `ShieldPercent`, and it does NOT live in
   the plain aux value list that `enb.aux()` / `aux_read_f` read (the generic getter
   `aux::get_value` @0x546710 returns 0 for it -- its `+val_off` (0x84) slot is
   empty, which is why the read came back nil). `ShieldPercent` lives in a SEPARATE
   SHARED / network-replicated, delta-interpolated auxdata list, resolved through a
   different getter (`aux::get_shared` @0x5bf2b0, same `__cdecl(container, keybuf)`
   shape). The live value is a float at `entry + 0x248` (guarded by the same
   `+0x70` valid flag as the plain list). HullPoints/MaxHullPoints DO sit in the
   plain list at `+0x84`, which is why hull worked and shield did not.
   FIX: added `aux_read_shared_f(container, key, out)` (`src/lua_api.cpp`) --
   builds the key, resolves via `aux::get_shared`, checks the valid flag, returns
   the float at `+shared_val_off` (0x248), fully VEH-guarded like
   `aux_entry_guarded`. Added `aux::get_shared` + `shared_val_off` to
   `src/game.h`. `enb.group()` and `enb.target()` now read `ShieldPercent` through
   it; `shield = MaxShieldPower * ShieldPercent`. The shared entry is present for
   BOTH the local player and any in-range remote, so a group member's CURRENT shield
   is genuinely readable client-side (proven live: a remote member's
   `ShieldPercent+0x248` resolves from the other box) -- no faked full-bar. [x]
   built, verified live grouped (see the VERIFIED GROUPED note above).

### BA-2c -- same-sector position/MVAS overlap (owner report, 2026-06-30) [ ] OPEN

Part of the same "multibox HUD derps hard" report: with both boxes in ONE sector,
the owner saw them "keep teleporting to each other's positions" and the MVAS
overlays overlapping. This is NOT the enbmod HUD (fixed above) -- it is
position/visibility REPLICATION between two boxes sharing a sector, i.e.
server avatar-position broadcast and/or the per-box MVAS position feed, which is
gated wire territory (server/proxy change needs primary-source + CLI parse/tests +
plans/29 CV). Deferred as a separate investigation; the client-side HUD defects
(1, 3, hull, 5-shield) are the ones the owner's latest message was about and are
done. Not yet root-caused.

## BA-3 -- launcher Profile section

New section BETWEEN the Emulator/Server section and the Client EXE section:
- Dropdown to select profile; `+` (add: prompt name, copy current profile) and
  `X` (delete; **cannot delete Default**).
- Each profile = its own config file in the launcher settings dir; the launcher
  populates the dropdown from those files. Deleting removes the file.
- **Default** is special: it uses the existing baseline config file (whatever it
  is named now -- NOT renamed to "Default"), and cannot be deleted.
- A profile persists: Emulator, Server, Client EXE, Upstream Auth Port, Lua Mods
  (including which mods are selected).

Launcher architecture (confirmed):
- Settings: `tools/LaunchFreya/Config/UserSettings.cs` (System.Text.Json ->
  `FreyaLauncher.settings.json` in `AppContext.BaseDirectory`; `Load()`/`Save()`).
  Per-profile fields: ClientPath, LastEmulatorName, LastServerName,
  AuthenticationPort, UseClientMods, ModStates (Dictionary<string,bool>),
  UsePositionFeed. Launcher-global (NOT per-profile): FormMainPositionX/Y,
  SettingsVersion, ActiveProfile (new).
- UI: pure Avalonia XAML `MainWindow.axaml` + code-behind `MainWindow.axaml.cs`
  (no ViewModel; direct manipulation of `_user`). 7-row Grid; insert Profile
  section as a new row before the Emulator section.
- Mods selection: `OnConfigureModsClick` writes `_user.ModStates[id]`; catalog
  from `ModCatalog.Scan()` / `ModStore.Dir()`.
- Play: `OnPlayClick` builds `LaunchSetting` -> `launcher.Launch()`.
- DESIGN: Default profile = the baseline `FreyaLauncher.settings.json` (special,
  undeletable). Named profile N = `FreyaLauncher.profile.<sanitized>.settings.json`
  in the same dir; dropdown populated by scanning the dir. ActiveProfile name
  persisted in the baseline file (single pointer, read at startup) so the right
  profile loads on launch. Add/`+` = prompt name + copy current; `X`/delete =
  remove file (blocked for Default).

DONE (2026-06-30). `UserSettings` rewritten for profiles: per-profile fields
(ClientPath, LastEmulator/ServerName, AuthenticationPort, UseClientMods,
ModStates, UsePositionFeed, + BA-4/5 Resolution/Fullscreen/AccountName/
CharacterName, Name); launcher-global keys (FormMainPositionX/Y, SettingsVersion,
ActiveProfile) authoritative in the baseline file. `PathFor`/`ListProfiles`/
`LoadProfile`/`Save`/`CreateProfile`/`DeleteProfile`/`SetActive`. UI: new Profile
border (Row 1) with `c_ComboBox_Profiles` + `+`/`X`; code-behind FillProfiles/
OnProfileChanged/OnAddProfileClick (PromptForText input dialog)/OnDeleteProfileClick,
ApplyUserToUi/CaptureUiToUser marshalling, `_loadingProfile` re-entry guard.
Default delete-button disabled. Builds clean, fmt-check passes.

## BA-4 -- Resolution + Fullscreen (under Client EXE, same section)

- `Resolution:` dropdown populated from the system's available resolutions.
- `Fullscreen:` checkbox, defaults unchecked.
- On Play: apply both to the client (registry/config knob -- reuse existing
  resolution-apply mechanism; `config-game`/net7config is a lead) BEFORE launch.
  Overwrite is fine (only needs to hold for the launch that immediately follows).
- Autosave resolution + fullscreen to the selected profile's config.

Resolution mechanism (confirmed):
- Reg key `HKLM\Software\Westwood Studios\Earth and Beyond\Render` (lands in
  Wow6432Node via the 32-bit reg view), values `RenderDeviceWidth` /
  `RenderDeviceHeight` (REG_DWORD, decimal pixels) and `RenderDeviceWindowed`
  (REG_DWORD, 1=windowed, 0=fullscreen). Baseline 1280x960 today is baked by the
  installer (`client/linux-installer/install-enb-linux.sh:1046-1048`).
- Apply in `Launcher.cs PatchRegistry()` (already runs before LaunchClient) via
  the existing `WineRegAdd`/`WineRegAddView` helpers (`wine reg add ... /reg:32`).
- Enumerate system resolutions with `xrandr` on Linux (WMI Win32_VideoController
  on native Windows). Dropdown options = distinct WxH from xrandr.

DONE (2026-06-30). `LaunchSetting.Resolution`/`Fullscreen` carry it to
`Launcher.PatchRegistry`: WINE branch appends RenderDeviceWidth/Height/Windowed
to the reg-write set (both 32/64 views via WineRegAdd); Windows branch calls the
new `WindowsRegistryHelpers.SetDisplay`. `TryParseResolution` skips the write when
unset. UI: `c_ComboBox_Resolution` (xrandr enum + EnB-baseline fallback set, deduped,
sorted largest-first) + `c_CheckBox_Fullscreen` (default unchecked). Autosaved to the
active profile on Play and on close.

BA-4b -- "Lock Mouse to Window" toggle (owner, 2026-06-30). New checkbox beside
Fullscreen: confines the cursor to the focused client window (multibox QoL).
Maps to the WINE X11 driver's `DXGrab` (`HKCU\Software\Wine\X11 Driver` "DXGrab"
= "Y"/"N", written EVERY launch so toggling off clears a prior grab).
`UserSettings.LockMouseToWindow` (persisted per-profile) + `LaunchSetting`
mirror; `Launcher.cs` registry base-`entries` adds the DXGrab write; UI
`c_CheckBox_LockMouse` marshalled in ApplyUserToUi/CaptureUiToUser + set on the
Play + Save paths. Builds clean. Also centered the Profile `+`/`X` button glyphs
(HorizontalContentAlignment/VerticalContentAlignment=Center).

## BA-5 -- optional login fields

- `Username (optional)`, `Password (optional)` (asterisks), `Character (optional)`
  text inputs.
- If provided, pass to the auto-login env vars (ENB_ACC_NAME / ENB_ACC_PASS /
  ENB_CHARACTER) so the client auto-logs-in.
- Autosave username + character to the profile config; **never** save password.

DONE (2026-06-30). `OnPlayClick` sets FREYA_ACC_NAME / FREYA_ACC_PASS /
FREYA_CHARACTER from the boxes (cleared to null when blank so a prior launch
never leaks), and FREYA_EULA=ACCEPT only when a username is present (hands-free
login must clear the EULA gate; a plain launch still shows it). enbmod's
autologin.cpp already reads FREYA_* (ENB_* fallback), and `ConfigureInjection`'s
autoLogin gate now recognises FREYA_ACC_NAME/FREYA_EULA so enbmod injects even
without the Lua-mods toggle. Username + character autosaved to the active profile;
password held only in the session text box, never written to disk.

## BA-6 -- broadcast chat not delivered

Broadcast messages echo to the sender's own chat but never reach other players.
Likely a server-side recipient-set bug (broadcast not iterating the right player
set) or a proxy drop. Server change requires CLAUDE.md proof: capture/decomp
primary source + CLI parse/test first + a plans/29 CV entry.

FIXED. Broadcast (client chat Type 4) goes to the ENTIRE SERVER (every online
player); "Local Area" (Type 3) is the sector-scoped channel. The starting code
had an old/inaccurate `BroadcastChat` that walked only the sender's sector list
(`s->GetSectorPlayerList()`), so a recipient in another sector never saw it.
Fixed `PlayerManager::BroadcastChat` to walk `m_GlobalPlayerList`, identical
reach to `ChatSendEveryone`. Owner-confirmed correct retail behaviour
(broadcast == global, sector == local); not a divergence.
