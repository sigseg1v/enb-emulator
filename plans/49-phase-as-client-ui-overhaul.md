# Phase AS -- client UI overhaul (enbmod Lua)

Owner ask (2026-06-12, screenshot at 1280x992, Earth Sector): replace the
native bottom-bar HUD with our own Lua-drawn UI.

- Remove the six circular "1 2 3 4 5 6" buttons bottom-center.
- Remove the circular warp control (the `^` / `v` arrows + "<<Warp" ring).
- Keep the green / red / blue stat bars but stack them vertically, same
  length -- or replace them with same-design bars of our own.
- New action bar along the bottom: keys `1 2 3 4 5 6 7 8 9 0 - =` as twelve
  square, shiny, rounded-corner buttons.
- Lua must be able to disable classes of native UI elements so we can
  replace them, and handle keybinds.

Folds in two pre-existing tasks: the xp_overlay label repositioning (text to
the LEFT of each XP bar, padded, per-bar alignment) and the `enb.self()`
calibration (overlay still shows "self() not calibrated" / "L? ?%").

## Approach

Two tiers for "remove the native elements":

- **Tier A (deterministic, no client research -- DO FIRST): cover + swallow.**
  Draw opaque panels over the native elements with the existing overlay
  (which renders after the game's frame, so it wins), and SWALLOW mouse
  input landing in those regions before the game sees it. We hook
  `PeekMessageA` already; turning a retrieved message into `WM_NULL` before
  returning it to the game is a complete input kill. The native widget
  still exists, but it is invisible and unclickable -- functionally removed.
- **Tier B (research, later): true hide.** Find the client's UI widget
  objects (calib.lua scan / static anchors) and a visibility flag or
  per-widget draw skip. Strictly better (no overdraw cost, resize-proof),
  but needs in-game reversing time. Tier A ships the user-visible result
  without it. Tier B entries stay open until proven.

Everything new is Freya/MIT under `freya/client-injection/enbmod/`. No
binary assets (repo rule): the "shiny rounded" button look is procedural --
per-vertex gradient quads + rounded corners from triangle fans, not PNGs.

## Checklist

### AS-1 C++ API: screen + input interception  `[x]`

- `[x]` `enb.screen()` -> `{ w, h }` from the D3D8 backbuffer desc (the
  overlay already holds the device; cache per device change). Needed for
  bottom-anchoring at any resolution (user runs 1280x992 today).
- `[x]` `enb.on_input(fn)` in the PeekMessageA hook (PM_REMOVE path only):
  fn(msg, wparam_lo_vk_or_x, y) for `WM_KEYDOWN/WM_KEYUP/WM_CHAR/
  WM_LBUTTONDOWN/WM_LBUTTONUP/WM_RBUTTONDOWN/WM_MOUSEMOVE`; **return true
  to swallow** (message becomes WM_NULL). Lua errors fail-open (never
  swallow on error) so a script bug can't lock the user out of the game.
- `[x]` Keep the Lua call on the game thread (the hook already is); add a
  fast pre-filter so we only enter Lua for message types a handler
  registered.

### AS-2 C++ API: draw primitives for the new look  `[x]`

- `[x]` `enb.draw.rect_grad(x,y,w,h, rgbTop, rgbBottom [,alpha])` --
  per-vertex D3DCOLOR on the existing XYZRHW quad path (one-line shader-free
  change; gradient = "shiny" base).
- `[x]` `enb.draw.rrect(x,y,w,h, radius, rgb [,alpha [,filled]])` and
  `rrect_grad(...)` -- rounded rect via triangle fan corners.
- `[x]` Alpha support on rect/line (the image path already alpha-blends;
  expose it on the solid primitives for the cover panels and button shine).

### AS-3 self() calibration  `[~]`  (scaffolding done; live values need a game session)

- `[x]` Auto-calibration helper script `scripts/autocalib.lua`: scan for
  `player_ptr_addr` candidates via `calib.find_ptr_to_value` driven by
  values the user reads off the HUD (hull/shield/energy), verify with
  `calib.watch`, then `enb.calibrate{...}` and PERSIST the result to
  `scripts/calib_data.lua` (gitignored -- offsets are install-specific)
  loaded by init.lua when present.
- `[x]` Numeric vitals (cur/max) + discipline levels NO LONGER need flat-struct
  calibration: the client keeps them in a string-keyed property bag on the player
  ship entity, not flat fields, so there is no stable offset to pin. Added a new
  `enb.aux(key)` (float) / `enb.aux_i(key)` (int) that resolve a key on the live
  vitals-controller entity via the client's own property-bag getter (build_key
  `0x004ad380` __thiscall + get_value `0x00546710` __cdecl; value float/int at
  entry+0x84, validity at +0x70). `H.stats()` now reads HullPoints/MaxHullPoints
  (hull stored absolute), MaxShieldPower/MaxEnergyPower (shield/reactor current =
  max * live gadget fill fraction), and RPGInfo Combat/Trade/ExploreLevel. The
  card prints "cur / max" inside each bar whenever aux returns them -- no flat
  calibration, works the instant we are in space. Called from on_tick = the game
  message-pump thread = same thread the game's own vitals updater runs on, so the
  getter call is single-threaded against the game's aux access (no race).
  Real-client check: CV-AS-AUXNUMS (see plans/29).
- `[ ]` STILL un-pinned: overall level + per-level xp progress (owner confirmed
  overall level is 0 on a fresh char; no single aux key has surfaced it yet, so
  the header stays "LV --" until one does). player_ptr_addr / pos / target still
  use the autocalibrate flat path.
- Runbook: owner in-game + hot-reload loop; cannot be done offline.

### AS-4 Lua UI module: the new HUD  `[x]`

- `[x]` `scripts/freya_ui.lua` (required from init.lua), CFG-driven, all
  geometry derived from `enb.screen()` (bottom-anchored).
- `[x]` Cover panels (Tier A) over: the six circle buttons, the warp
  control ring + arrows, the native green/red/blue bars. Opaque fill
  matched to the HUD background tone; per-region mouse swallow.
- `[x]` Action bar: twelve square rounded shiny buttons labeled
  `1..9 0 - =` along the bottom. Click -> `enb.tap(vk)` for the matching
  key; physical keypress observed via on_input lights the same button
  (pressed state ~150ms). VKs: 0x31..0x39, 0x30, VK_OEM_MINUS 0xBD,
  VK_OEM_PLUS 0xBB.
- `[x]` Stat bars: hull (green), shield (blue), energy/reactor (red) -- we
  re-stack as three equal-length horizontal bars, vertically stacked,
  with value/max fill from `enb.self()`; gray skeleton until AS-3 lands.
- `[x]` No warp button, no up/down arrows -- removed, not relocated (owner
  said remove; throttle stays on the native keybinds).

### AS-5 xp_overlay reposition  `[x]`

- `[x]` Labels LEFT of each XP bar with padding, right-aligned against the
  bar start, one label per bar (1280x992 reference: bars start x~27,
  ~165px wide, y~935-975, ~20px apart). Anchor from `enb.screen()` bottom
  instead of hardcoded 1280x768-era constants.

### AS-6 Tier B: true native-widget hide  `[x]`  (SOLVED 2026-06-18 -- flag-gate the draw pass)

- `[x]` **Working solution (screenshot-verified live, 2026-06-18).** The whole
  in-space 2D HUD is ONE interface-scene object (vt 0x00aff1f4), reachable as
  `cockpit_ctrl()+0x44`. It owns an intrusive circular widget list at scene+0x40
  (sentinel = scene+0x3c; node next +0x04; node+0x0c points 0x10 INTO the child,
  so child = that ptr - 0x10). The per-frame draw pass walks that list and draws
  each leaf only when its visibility predicate `(flags & 0x700) == 0x700` passes,
  reading a flags dword at leaf+0x18. **Clearing bit 0x400 of leaf+0x18 makes the
  draw pass skip that widget** -- no vtable edit, no value-field write, no crash.
  The `hide-ui` mod (`native_hud_hide.lua`) re-applies this every tick (widgets
  rebuild on sector/dock change) on leaves of class 0x00b1327c only, which is the
  native chrome. The chat window (a different leaf class, 0x00afbf28) and the 3D
  cockpit border mesh (a separate PIP scene list) are untouched.
- `[x]` **Per-element control API (2026-06-18).** The 16 chrome panels are each
  named (by observing which on-screen widget vanished when each leaf was hidden)
  and exposed via `enb.hud.{hide,show,toggle,set,hide_all,show_all,list,names,dump}`
  -- a modder/`/run` addresses a panel by name (e.g. `enb.hud.show("TargetFrame")`).
  The panel is the ATOMIC unit: a panel paints its own children, ignoring per-child
  draw gates (tested live -- clearing a child gadget's 0x700 bits did nothing), so
  sub-widget hiding would need a DLL draw-hook or mesh-scale, deliberately out of
  scope. Panels carry no name/id at a fixed offset, so the key is the chrome-ordinal
  (position among 0x00b1327c leaves); `enb.hud.dump()` prints the live mapping.
  Bits 0x100/0x200/0x400 are Visible/Enabled/in-Layout and ALSO gate hit-testing,
  so hiding a panel can break 3D click-to-select -- show it back if so.
  Default hidden set: ActionBarIcons, LevelBarsAndMicroMenu, BottomHudChrome,
  ActionBarBackground, ChatFrameBackgroundAndScroll (owner-chosen 2026-06-18).
- `[x]` **Ordinal-drift fix via tail anchoring (2026-06-18).** The scene list is NOT
  static: opening a window inserts chrome leaves (inventory +5, target +1,
  comm/portrait +2), which shifts later ordinals -- so keying a hide by raw ordinal
  moved it onto the wrong panel and, because apply() runs every tick, permanently
  cleared the draw bit on whatever inventory/loot/equipment leaf landed on a "hidden"
  ordinal. That produced the blank inventory **equipment frame** ("shows my ship but
  not engine/reactors/weapons") and the missing **loot button** with a target.
  - **Fix: tail anchoring.** Verified live (`enb.hud.dump()` + raw scene walks): the
    16 persistent cockpit panels are a contiguous block at the END of the list (last
    leaf is always ActionBarBackground), and EVERY transient window inserts its
    leaves at the FRONT, ahead of that block (inventory adds 5 before it, a target 1,
    comm 2). So the persistent panels are simply the LAST `#ELEMENTS` chrome leaves;
    keying each name off the end (`leaves[count - #ELEMENTS + k]`) is correct no
    matter how many transient windows are open, including a target selected when the
    mod first runs. No baseline-capture or ordinal-timing step to get wrong.
  - **Baseline is 16 chrome leaves, not 17.** (A mid-session "17 / off-by-one"
    theory was WRONG and briefly broke the mod -- with a bogus 17th name `map_panels`
    required `count>=17`, which only happened once a transient opened, so the mod did
    nothing for ~15s after load. Reverted to 16; it now applies from the first
    in-space tick. This is the "wrong at load, correct ~15s later" report.)
  - **The equipment paperdoll is a SEPARATE leaf, not ActionBarIcons.** Proven by
    diffing the chrome list across an inventory open: ActionBarIcons (the native
    ability glyph by hotbar slot 1) keeps its exact address and flags when inventory
    opens; the equipment paperdoll is one of the 5 NEW transient front leaves. They
    were never the same object, so there is nothing to "separate" -- and the
    "restore ActionBarIcons while a modal is open" hack from the 17-theory was
    unnecessary AND was the cause of the **glyph flashing back on inventory open**.
    Removed it: ActionBarIcons is hidden unconditionally, the equipment frame
    (untouched transient leaf) draws on its own, no flash.
  - Visually confirmed on the live client: clean action bar (Freya hotbar 1-9,0, no
    native glyph by slot 1), inventory open/close keeps the glyph hidden with no
    flash. -> CV-AS-HUD-INVENTORY for owner equip-view confirm.
- `[x]` **Why every earlier approach was a visual no-op** (all disproven live
  2026-06-18, kept here so we never retry them):
  - `patch_ret(VitalsPaint 0x005dcae0)` / `patch_ret(XpPaint 0x0058cf60)`: changed
    nothing on screen. These are not the draw path for these widgets.
  - `SetVisible` / the gadget +0x2c "visible" bit / the +0x60 flag: the draw pass
    reads leaf+0x18 `& 0x700`, NOT those, so writing them does nothing visible.
  - `vt_hide_paint` repointing a controller's or a container's slot-0x24: the
    controllers/containers are NOT on the draw path; only the scene's leaf list is.
    (The earlier "vitals hide works" claim was a MISREAD -- the bars that vanished
    were the Freya overlay PlayerCard going blank after slot-4, the value-UPDATER
    0x5DC4A0, corrupted the vitals data it reads. vt_hide_paint the mechanism is
    fine and still in lua_api.cpp as an RE tool; it just cannot reach these leaves.)
- `[x]` Mesh scale-zero (the old `hide-ui` vitals-fill suppression): confirmed live
  to be running (scales read 0.0) yet zero visual effect -- those quads are not the
  visible bars. Removed in the rewrite (dead code).
- `[~]` Real-client confirmation: I verified this on the actual Win32 client under
  WINE via screenshots, so the visual result is real. CV-AS-HIDE-COCKPIT in
  plans/29 updated to record the working mechanism; owner should confirm the menu
  bar + scanner removal is desired scope (it removes the top menu bar too; scope
  can be tightened to bottom-only by screen position if wanted).
- Note: the earlier "Tier A cover panel" approach is GONE -- the design is
  translucent glass, so an opaque cover would defeat the look.

### AS-7 Build + docs  `[x]`

- `[x]` `make` (i686-w64-mingw32) clean; README API table updated; note the
  fail-open swallow rule and the no-binary-assets decision.

### AS-8 Headless Lua mod test suite  `[x]`  (added 2026-06-12, owner ask)

Purpose: debug + test mods programmatically (Claude included) WITHOUT
launching the EnB client -- including visual verification via rendered
screenshots.

- `[x]` `tests/mock_enb.lua` -- pure-Lua mock of the C++ `enb` host API;
  scripts under `scripts/` run unmodified against it on a NATIVE Linux
  build of the vendored Lua. Mirrors the C++ contracts (hooks.cpp
  msg_class/mask, lua_api.cpp run_input fail-open) with the mirrors
  pinned by `tests/spec/mock_contract_spec.lua`.
- `[x]` Specs: freya_ui (layout/buttons/swallow/flash/key-lighting, 17
  tests), xp_overlay (5), autocalib (4), init startup path (4),
  mock contract (7), screenshot scenarios (4) -- 41 tests, all green.
- `[x]` Screenshot pipeline: `screenshots_spec.lua` dumps full-HUD frames
  to JSON; `tests/render_frame.py` (Pillow) rasterizes them to
  `build/tests/shots/*.png` approximating overlay.cpp draw semantics.
  Verified visually: 4 scenarios (uncal/cal/interaction/1024x768).
- `[x]` `make test` target + README "Testing the mods headless" section.
- First real catch: xp_overlay labels at x=-16 (off-screen left -- a
  label cannot physically fit LEFT of a bar that starts at x=27).
  Fixed: clamp to x=2, label overlaps the bar's left edge
  (value-on-the-bar style). Owner may veto placement at the CV pass.

### AS-9 Port the "Earth & Beyond HUD" design  `[x]`  (added 2026-06-12, owner ask)

Owner: "implement this as the ui overlay" (the Claude `Earth & Beyond HUD.html`
design) -- "only show it when in space. not login screen, not character select,
not load screen. show in station but hide skill buttons at bottom. Draw mouse
pointer on top not under" + "implement this fully ... hide the ui elements we
replaced ... once your ui matches claude design and you confirm accuracy vs
screenshots commit and push".

- `[x]` `scripts/freya_hud.lua` -- shared toolkit: the design palette (hull
  #e8453c / shield #3d8bff / reactor #37d27a / combat teal / explore blue /
  trade purple), the `H.glass()` panel (vertical-gradient body + gloss sheen +
  hairline border + cyan corner ticks), outlined text (`H.otext`: 4 dark offset
  copies + 1 fill, since the overlay font is single flat RGB), and `H.vis()`
  visibility gating.
- `[x]` `freya_ui.lua` rewritten to the design PlayerCard (name + LV header,
  three vitals as track+vertical-gradient fill with cur/max inside + pct) and a
  glass hotbar (12 rounded slots, gloss, cyan armed glow) anchored above the
  card.
- `[x]` `xp_overlay.lua` rewritten to the design DiscCard (titleless bottom-left
  glass, three rows: colored C/E/T letter + xp bar + "LV n" badge).
- `[x]` Visibility gating (AS-6's `enb.state()`): space = cards+hotbar, station
  = cards only, login/charsel/load = nothing. CV-AS-STATE.
- `[x]` Cursor on top: `enb.cursor(on)` draws a procedural arrow after the
  display list in the Present hook; freya_ui toggles it with HUD visibility.
  CV-AS-CURSOR.
- `[x]` Specs rewritten to the new geometry/colors/gating (freya_ui 20,
  xp_overlay 8, init 4, screenshots 6 incl. station + login). Headless
  previewer (`preview_server.py`) gained a space/station/login state selector.
- `[x]` Visual check vs design: `build/tests/shots/0{1,2,5}_*.png` rendered and
  compared -- player card, discipline card, glass hotbar, station-drops-hotbar
  all match the design intent. Fidelity gaps (documented in freya_hud.lua):
  ONE ~14px overlay font (no per-element sizing / Michroma), no real backdrop
  blur, single-stop shape alpha.
- Font + draw-order + the native-widget hide still need the real client
  (CV-AS-STATE / -CURSOR / -HIDE-*).
- `[x]` **AS-12 action-bar binding (dispatch half).** The Freya hotbar now fires
  the native in-space action bar. The native bar is two adjacent 3-slot "bank"
  controllers (one vtable `0x00af7484`, `0x60` bytes apart): 6 slots, each with a
  PRIMARY and an ALTERNATE fire mode -- the 6 primaries are the native "1".."6",
  the 6 alternates are what the native alt-toggle button swaps to. Mapped onto our
  twelve keys: Freya 1-6 -> the six primaries, Freya 7-12 -> the six alternates.
  - Read-only ECX-capture hook on the Use-Slot dispatcher `FUN_006120e0`
    (`game::addr::ActionBarUse`, staged in game.h/hooks.cpp/.h/lua_api.cpp) exposes
    the live controller as `enb.actionbar()`; the sibling bank is `+/-0x60`, so one
    capture yields both banks. Each bank gives its primary START gadget id at
    `+0x50` and alt START id at `+0x54`; slot k is primary `(v50+k)` / alt
    `(v54+k)`, k in 0..2.
  - `freya_ui.lua` `dispatch_slot(i)` calls `enb.call(0x006120e0, bank, gid)` --
    byte-identical to a native click/keypress, incl. the per-slot `+0x6c` on/off
    gating. Click slots 1-6 tap their native key (works cold + captures the bank);
    click 7-12 and physical keys 7-12 (no native bind) dispatch through the
    controller (edge-triggered to suppress auto-repeat). Verified live: mapping
    resolves to b0 gid 2/3/4 + b1 gid 9/10/11 (primary) and b0 gid 5/6/7 + b1 gid
    12/13/14 (alt); a toggle-id dispatch flipped bar state 0->1->0 (no crash).
  - `[~]` **icon half: pipeline PROVEN + wired; awaits populated-bar verification
    (2026-06-18).** Built + staged (not committed):
    - `enb.draw.texture_quad(texptr,x,y,w,h[,alpha])` -- new `K_TEXQUAD` overlay
      display-list cmd that binds an arbitrary game-owned `IDirect3DTexture8*` and
      blits it (UV 0..1) inside the existing Present hook. Pointer-guarded
      (null/garbage -> no-op). overlay.cpp/.h + lua_api.cpp.
    - PROVEN live: captured the game's font-glyph atlas texture by walking the
      gadget tree and blitted it with `texture_quad` -- the glyphs rendered on
      screen. Capturing a game-owned `IDirect3DTexture8*` from memory and drawing it
      in our Present hook works end-to-end.
    - The per-slot icon texture is reachable by a FIXED pointer chain off the native
      action-bar bank (no draw hook needed): slot gadget (bank `+0x10 + k*4`,
      k=0..2) `-> +0x64` ability-data `-> +0x64` icon gadget (vt `0x00afcd04`)
      `-> +0x24` image node `-> +0x34` = the live `IDirect3DTexture8*`. Verified the
      leaf is a real WINE d3d8 resource: device ptr `0x015a99d8` at `+0x18`, refcount
      at `+0x04`, vtable `0x7fc8d5a0` (same `0x7f______` band as every live texture).
    - `freya_ui.lua` now walks that chain per Freya slot (`slot_icon_tex(i)`) and
      blits the icon onto the glass button; empty slots resolve to a transparent
      placeholder and draw nothing. Reloaded live, no crash, hotbar intact.
    - DEAD CODE REMOVED: the earlier `0x006a8760` "IconDraw" hook fired **0 times**
      in space -- wrong function. Deleted it + `enb.icon_src`/`enb.icon_stats`
      (game.h `addr::IconDraw`, hooks.cpp/.h, lua_api.cpp) and the now-unused
      `<mutex>`/`<unordered_map>`/`mem.h` includes. DLL rebuilt clean (-Wall -Wextra).
    - REMAINING (CV-AS-ACTIONBAR-ICONS): the dev character's bar is EMPTY (every slot
      is the "Unused Slot" placeholder -> transparent texture), so a real
      ability/weapon icon has never been on screen to confirm the chain yields the
      right ART for a POPULATED slot, and the primary-vs-alternate split (Freya 1-6
      vs 7-12 share one slot gadget) is unverified. Needs a character with an
      equipped weapon / trained ability to validate visually with the real client.

### AS-14 Resolution-scaled layout offsets + always-visible target buttons  `[x]`  (owner ask 2026-06-28)

- `[x]` **Resolution-scaled positioning.** The owner tuned a set of HUD
  position/size nudges at 2560x1440 and asked that they apply ONLY when the
  screen is strictly larger than the 1280x960 design baseline, scaling linearly
  with the diff (0 at 1280x960, full strength at 2560x1440, interpolated in
  between -- so a 1280x960 player sees exactly the current layout). Implemented
  as shared infra in `lib/freya_hud.lua`: `H.REF_W/H = 1280,960`,
  `H.TUNE_W/H = 2560,1440`, a private `dpi_frac()` (returns 0,0 unless BOTH dims
  exceed the reference), and `H.sx(px)`/`H.sy(px)` (scaled pixel offset on each
  axis) + `H.smul(mult)` (scaled multiplier, 1.0 at baseline). Every element move
  is `base + H.sx/H.sy(tuned_offset)` so nothing compounds and the baseline is
  untouched. The offsets, all owner-tuned at 1440p:
    - target frame: moved IN 32px (`target_frame.lua` area_rect `-H.sx(32)`);
      both hull+shield bars +6px height (`H.sy(6)`); hull/shield value font 1.5x
      (`H.smul(1.5)`). draw_bar() now takes `bar_h`/`val_scale` params.
    - action bar + player-card: UP 20px off a shared `base_bar_y` so they don't
      compound (`freya_ui.lua` layout()).
    - play card: additionally RIGHT 32px (`+H.sx(32)`).
    - discipline card ("player frame" / C-E-T card, `xp_overlay.lua`): UP 20px
      (`-H.sy(20)`) and RIGHT 32px (`+H.sx(32)`, the same shift chat.lua applies)
      so the card left-aligns with the chat box below it.
    - chat box: UP 20px + RIGHT 32px, applied locally in `chat.lua` (NOT in
      `H.chat_rect`) so xp_overlay's `H.chat_top()` anchor keeps its own offset.
    - top micro-menu strip: RIGHT 32px + DOWN 16px on the strip origin
      (`micromenu.lua` layout()) so draw + hit-test stay in lockstep.
  Verified live at 2560x1440: `H.sx(32)=32`, `H.sy(20)=20/12=12/16=16/6=6`,
  `H.smul(1.5)=1.500` (all full strength); zero at the 1280x960 baseline.
- `[!]` **Target action buttons always visible -- REVERTED (owner-reported wrong
  2026-06-28), STILL OPEN.** First attempt force-ORed `ENABLE_BIT` each tick onto
  every TRANSIENT front leaf resting at gate `0x500` (Visible|inLayout, Enabled
  clear). That was too broad and ALSO wrong: (a) the action-bar TAB panel is a
  transient leaf that rests at `0x500`, so switching bar tabs left "a big yellow
  button stuck at the bottom"; (b) the owner reported it did not even reliably
  show the actual target buttons. So `0x500` is NOT a unique fingerprint for the
  target-action cluster, and blanket-enabling that resting state is unsafe.
  Reverted `hide-ui/native_hud_hide.lua` to its pre-AS-14 state (restored from
  `086c68c9^`); target buttons are back to native hover-only. A correct fix needs
  to IDENTIFY the target-action button leaf specifically (not by the shared `0x500`
  gate) -- e.g. by its child-gadget UI names / geometry, or by hooking the hover
  create path -- or redraw + click-wire the buttons in-overlay (the AS-13 (4)
  approach). Tracked there; deferred.

### AS-15 Group frame action buttons + roster shift  `[x]`  (owner ask 2026-06-30, owner-confirmed 2026-07-01)

Owner: "in group at the top above the group frame should be buttons though for
Disband, Formation, Target. (for leader), and for group member its Leave,
Formation, Target. We are hiding them right now. Can you unhide them and move the
party frames down so they appear just below it please."

Honest framing: we are NOT hiding native group buttons -- like the AS-14b target
buttons they are game-managed transients we cannot reliably force-show, so the fix
is the same as the target frame's: Freya-DRAWN letter buttons above the party
frame, click-wired to the native packet paths.

- `[x]` **DLL: enb.group_action(action[, target_gid])** -- builds the group
  call-to-arms / formation request (wire opcode 0x00BC,
  `CTARequest{SourceID, TargetID, Action}`, `common/include/net7/PacketStructures.h`)
  with the client's own request constructor (`game::addr::CtaBuild`) into the
  shared command buffer and pushes it through M's Connection like
  `enb.target_action`. `target_gid` defaults to -1 = whole group. Action codes =
  the server `GroupAction` switch: 4 Slot Back / 5 Block / 6 Pipe (set formation,
  leader-only server-side), 7 Form Up, 8 Leave Formation, 9 Break Formation
  (leader), 12 target-my-target.
- `[x]` **DLL: enb.is_leader()** -- the client's own leader check
  (`game::group::is_leader`, the function the native group window runs to choose
  GRP DISBAND vs GRP LEAVE), called with the same avatar bases the roster read
  walks, VEH-guarded, char return masked to AL.
- `[x]` **Lua: party_frame.lua button rows** -- leader `[D]isband [F]ormation
  [T]arget`, member `[L]eave [F] [T]`; F toggles a formation sub-row (leader
  `S/B/P/X` = actions 4/5/6/9; member `U/X` = 7/8 -- asymmetric because the server
  rejects a non-leader SetFormation). D/L fire the native avatar-command path
  (`enb.target_action` 0x0d/0x0e self -- the exact `/group disband`//`/group remove`
  packets); T and the sub-row fire `enb.group_action`. Roster panel shifts down
  below the rows. Gated on `enb.is_leader ~= nil` so an older DLL degrades to the
  plain frame at its original position. Same rect/flash/on_input pattern as
  target_frame's verb row; syntax-checked with a native luac build.
- `[x]` **Live verify (needs client relaunch -- DLL changed)**: two grouped
  multibox clients; see plans/29 CV-AS-GRPBTN. 2026-07-01 agent-driven pass:
  relaunched both boxes on the new DLL, grouped them via the native command path
  (0x0a invite w/ explicit GameID + 0x0b accept -- the `/group invite` slash verb
  via chat_send never reached the server), rosters + is_leader split verified,
  group_action(12) sent clean, no faults. Owner confirmed the on-screen rows
  work same day ("works great").
- Known server-side caveat (recorded, NOT changed): `GroupAction` 12
  (RequestTargetMyTarget) is a stub upstream -- it chat-messages the group
  ("Target my target.") and the actual retarget push is commented out. The button
  fires the correct native wire packet; making the server actually retarget is a
  separate server change under the full CLAUDE.md gate (primary source + CLI
  parse/tests + CV entry).

## Status

| Item | State |
|---|---|
| AS-1 input/screen API | done -- `enb.screen()`/`enb.measure()`, `enb.on_input(fn,mask)` PeekMessageA swallow (fail-open), `enb.WANT_*`/`enb.msg.*`. hooks.cpp/.h, lua_api.cpp, overlay.cpp/.h |
| AS-2 draw primitives | done -- `rect_grad`/`rrect`/`rrect_grad` + alpha on `rect`/`line` (procedural gradient + triangle-fan corners, no assets). overlay.cpp/.h, lua_api.cpp |
| AS-3 calibration | partial -- scaffolding done (`scripts/autocalib.lua` find/probe/save, `scripts/.gitignore` for `calib_data.lua`, init.lua loads it via pcall). Live offset VALUES still need owner in-game session |
| AS-4 freya_ui.lua | done -- `scripts/freya_ui.lua`: cover panel + swallow, 12-button action bar (1..9 0 - =) with click->`enb.tap`/keypress-lit, 3 stacked stat bars (gray until AS-3) |
| AS-5 xp_overlay fix | done -- `scripts/xp_overlay.lua` rewritten: labels right-aligned LEFT of each bar, screen-bottom-anchored via `enb.screen()` |
| AS-6 Tier B hide | **SOLVED 2026-06-18.** `hide-ui` mod clears bit 0x400 of a chrome leaf's flags dword (leaf+0x18) every tick, so the in-space HUD draw pass skips it. Exposes a per-element `enb.hud.*` API over the 16 named chrome panels (panel is the atomic unit; sub-widget control needs a DLL draw-hook, out of scope). Default hides ActionBarIcons/LevelBarsAndMicroMenu/BottomHudChrome/ActionBarBackground/ChatFrameBackgroundAndScroll; keeps chat + 3D border. Screenshot-verified on the real WINE client. Replaces the disproven patch_ret/vt_hide_paint/mesh-scale approaches (all visual no-ops). See CV-AS-HIDE-COCKPIT |
| AS-7 build+docs | done -- `make clean && make` clean (zero -Wall -Wextra warnings); README API table + AS section updated |
| AS-8 headless test suite | done -- 43 tests green + 5 rendered screenshot scenarios (incl. station); `make test` |
| AS-9 design port | done -- freya_hud/freya_ui/xp_overlay ported to the `Earth & Beyond HUD.html` glass design; state-gated (space/station/login); cursor-on-top; specs + screenshots rewritten. Real-client checks: CV-AS-STATE/-CURSOR/-HIDE-* |
| AS-10 mod folders + launcher Configure Mods | done -- scripts restructured into `scripts/lib/` (shared: modloader, json, freya_hud, calib) + `scripts/mods/<id>/` (player-hud, discipline-card, native-hud-hide, autocalibrate), each with a `mod.json` (id/name/author/description/entrypoint). New C++ `enb.list_dir` + `enb.script_dir`; `init.lua` is now a loader (`lib/modloader.lua` discovers + dofiles enabled mods). Launcher gained a "Configure Mods..." button (enabled only when "Enable Lua Mods" is checked) opening a scrollable popup of enable/disable + name + author rows with description tooltips; `UserSettings.ModStates` persists choices; `StageClientMods` stages only enabled mods and prunes disabled ones (disabled = not loaded/injected). `ModCatalog.cs` reads the manifests. Suite green (20/20 enbmod incl. loader test); launcher builds 0 warnings |
| AS-11 consolidate HUD into one mod + dep system | done -- merged `player-hud` + `discipline-card` into a single **`freya-hud`** mod (`init.lua` requires xp_overlay then freya_ui); pulled the bar-hiding out as the separate **`hide-ui`** mod and made `freya-hud` declare it in a new `mod.json` `"dependencies"` array. `ModCatalog.ModInfo.Dependencies` parses it; the Configure Mods window now paints a row red ("requires &lt;id&gt; (disabled/missing)") when an enabled mod has an unmet dep, re-evaluated live on every checkbox toggle. The loader does NOT enforce deps (freya-hud still draws if hide-ui is off, just over the natives). README/MOD-STRUCTURE.md document the field. Suite + dotnet tests green |
| AS-12 action-bar binding | dispatch half done -- Freya hotbar fires the native bar via the Use-Slot dispatcher (`enb.actionbar` capture hook + `enb.call`), Freya 1-6 -> 6 primaries, 7-12 -> 6 alternates; live-verified, staged (no rebuild needed). Icon half: pipeline PROVEN (captured the game font atlas + blitted it via the new `texture_quad`) and wired -- `freya_ui` walks the fixed slot chain (slot `+0x64` ability `+0x64` icon `+0x24` image `+0x34` = live `IDirect3DTexture8*`) and blits each icon. Dead `0x006a8760` IconDraw hook removed. Awaits a POPULATED bar (equipped weapon / trained ability) to confirm real-icon art + primary/alt split. CV-AS-ACTIONBAR-ICONS |
| AS-13 target frame redesign | in progress -- the bottom-right target frame is rebuilt. hide-ui now default-hides the native `TargetFrame` chrome panel (bg + native hull/shield bars + name + "Dist:" + cycle arrows + the round scan/cycle buttons, all one atomic panel); the 3D target model is a separate PIP scene and stays visible. New `freya-hud/target_frame.lua` repaints the 2D layer per the owner ask: no background, two stacked thin bars at the top of the target area (hull red over shield blue, ~1/3 the player-card 16px bar height = 6px, no gap, full width, current/total value printed on the bar at 0.6 scale), then the target name + " (L<level>)" below, wrapping to a 2nd line when too wide. Data from the new VEH-guarded `enb.target()`. Layout proven live vs the home-sector station; bar fill + cur/total + level + name-wrap proven via a fake-vitals stub (no mob target in the home sector). CV-AS-TARGETFRAME. **TRACKING FIX (owner-reported: frame did not update on target switch)**: `enb.target()` sources the live target object from the radar-highlight setter (`TargetEntitySet` 0x007bd6e0, arg0 = the resolved object, 0 on de-target -- fires on every switch) and the level from the frame display update (`TargetFrameUpdate` 0x0060e540, param_4). **NAME + ENEMY-HULL FIX (owner-reported: showed class name "Ship"; enemy targets showed no hull) -- SOLVED, verified live 2026-06-28**: the hull/shield AuxData and the names do NOT live on the contact object directly but on its PROPERTIES CONTAINER at `object+0x88`. `enb.target()` now reads hull/shield aux off that container (enemy hull now shows -- live: a mob read HullPoints 32.55/32.55), the proper INSTANCE name as a char* at `container+0x124` (live: "Needlenose", not "Ship"), and falls back to the class name at `container+0x3c` only when the instance name is empty. The earlier reads (aux + name off the contact object, name at object+0x84) were the bug: the contact carries no aux and +0x84 is garbage. DLL hook change -> needs a client relaunch to load (Lua hot-reloads; the C++ DLL does not). Built + staged (`bin/enbmod.dll`); relaunched + verified on an enemy ship (name "Needlenose" + full hull bar render confirmed on-screen). **REMAINING**: (a) confirm the instance name on a STATION target ("Loki Station") and a named NPC mob ("Scuttlebug") -- container+0x124 is the same object layout, but only a ship target was verified live; (b) level is currently nil for the tested mob (frame param_4 did not yield a level; not a complaint, frame degrades gracefully); (c) keeping the gate/loot/scan/cycle icons that sat above the native frame -- they are children of the now-hidden panel and cannot be separated natively, so they must be redrawn in-overlay and click-wired to the native actions (scan/cycle are testable here; contextual gate/loot need an appropriate target); (d) real-client validation against a live vital target (bars filling + tracking damage). **OWNER FOLLOW-UP ASKS (2026-06-28): (1+2) cur/total value clipped at the screen edge + bars too wide -> bars trimmed 12px on the RIGHT (`CFG.BAR_TRIM_R`), which both narrows the bars and pulls the right-aligned value inward clear of the edge. (3) distance -- **the earlier +0xE8/+0xEC/+0xF0 "viewer-relative magnitude" claim was WRONG** (owner: "dist isn't even calculating properly, totally wrong, pulling from wrong spot"); those flat object offsets are not world coords. CORRECT approach (how the native client itself computes it): both the target and the local ship are GameID-resolved positional locatables exposing a struct-returning world-position getter at `vtable+0x24` (ECX = object, hidden return ptr = &Vec3 buffer -> 3 packed floats at +0/+4/+8); distance = `|player_wpos - target_wpos|`. Target object = `enb.target_obj()`; the PLAYER ship object is fetched exactly as the native target-frame refresh (`FUN_00512400`) does -- `target_ctrl[0x4c]` is the targeting subsystem, its `vtable+0x28()` (no args) returns the local ship (the SAME object the camera controller `FUN_00793ce0` calls `vtable+0x24` on to compute target distance, and whose aux at +0x88 holds TargetThreatLevel -- so it doubles as the level source). `targeting_data_obj()` returns it; `enb.target().dist` reads both ends through `world_pos()` and the freya-hud frame renders it abbreviated `Dist %.2fk`. The player position was the hard part: the vitals-chain entity (`player_entity()`) is NOT a positional space object (its `vtable+0x24` returns 0,0,0); only the targeting-subsystem ship object is. **DANGER LEARNED -- froze the live client 3x this session:** calling `vtable+0x24` (or any vtable getter) on an object that is not the exact locatable class invokes the wrong virtual method and WEDGES the message pump; the VEH fault-guard does NOT catch a non-faulting wrong call. So the ship object must come from the deterministic native chain above, NEVER from scanning memory and probing `vtable+0x24` on candidate pointers. (Reverted the abandoned radar-manager capture from `hk_TargetEntitySet` + the `enb.radar`/`enb.arm_dist` scan diagnostics.) Format `Dist %.2fk` + the value path (`targeting_data_obj()` + `world_pos()`) implemented and built; **live end-to-end VERIFIED 2026-06-28**: targeting the station just undocked-from (Luna), `enb.target()` returned distinct ship/target world positions with no freeze and `dist=28268`, and the freya-hud frame rendered `Dist 28.27k` on-screen directly under the name -- the value tracks correctly now (the old +0xE8 magnitude was garbage). The throwaway scan diagnostics (`enb.wpos`/`l_wpos`, `enb.self_obj`/`l_self_obj`) were deleted per the no-dead-code rule once the render was confirmed; `targeting_data_obj()` + `world_pos()` (the load-bearing path) stay. Level was nil on the Luna station target (a station has no TargetThreatLevel -- correct graceful degrade); the `(L<level>)` append was verified earlier on a mob. (4) the contextual action buttons above the frame (e.g. a station's Board) are still hover-only -- still OPEN; the game CREATES that button panel as a transient CHROME_VT leaf on mouse-over and DESTROYS it off-hover (confirmed via enb.hud.dump: a new leaf at full 0x6701 draw-gate appears on hover, vanishes after), so the existing hide/show force-flag (which targets persistent tail leaves) cannot pin it -- forcing it always-on needs hooking the hover create/destroy or redrawing+click-wiring the buttons in-overlay. **(5) LEVEL fix (owner-reported: "doesn't show the level still") -- ROOT-CAUSED + FIXED 2026-06-28.** The earlier read pulled the `TargetThreatLevel` INT aux off the targeting container, but that key is a global singleton that stays at its -1 registration default on our server: `Player::HandleRequestTarget` (server `PlayerConnection.cpp:3603-3610`) leaves `SetTargetThreatLevel(Lvl)` COMMENTED OUT and instead sends the mob's level as the `TargetThreat` STRING ("Level N"). The native HUD reads that string via a string-typed aux lookup (`0x00514bd0`, value is a char* at entry+0x84). `target_level_live()` now resolves `TargetThreat` with the string lookup and `sscanf "Level %d"` to recover the level; non-mob targets carry an empty/relative threat ("Even"/"Hard"/...) with no parseable level -> returns -1 -> no suffix, graceful. No server change needed (reads what the server already puts on the wire). Built + relaunched 2026-06-28. The user's instinct that the field was "threat/aggro, not level" was half right: `TargetThreat`/`TargetThreatSound` ARE the threat assessment, and on our server `TargetThreat` is where the level number actually lives.** |
| AS-14 res-scaled layout | done (owner ask 2026-06-28) -- new `lib/freya_hud.lua` `H.sx/H.sy/H.smul` scale helpers (0 at the 1280x960 baseline, full at 2560x1440, linear in between); owner-tuned 1440p nudges applied per element (target frame in 32 + bars +6h + value 1.5x; action bar/player card up 20; play card right 32; disc card up 20 + right 32 to align with chat; chat up 20/right 32; micro-menu right 32/down 16), none compounding. Pure Lua, hot-reloaded + verified live at 2560x1440 |
| AS-14b target action buttons (hide native + Freya letter-buttons) | in progress (owner ask 2026-06-28) -- FIRST attempt (force-enable every transient leaf at the 0x500 gate to keep the native buttons always-on) was REVERTED: it also caught the action-bar tab panel (yellow button stuck on tab switch) and did not reliably show the real buttons -- `0x500` is not a unique fingerprint. New direction: HIDE the native target-action buttons entirely (no hover), then REDRAW them in-overlay as Freya square buttons just above the target hull/shield bars, each a single letter = first letter of the action (Follow=F, Loot=L, Dock=D, Register=R, ...), click-wired to the native action. native_hud_hide.lua reverted to `086c68c9^`. **RE done 2026-06-28 (no code shipped yet -- the remaining piece is a real RE task, not a quick edit):** (1) **action dispatch FULLY MAPPED** -- the verb dispatcher is `FUN_00658430(VerbManager this, verb_id<<16)`, called only via the button gadget's click callback, so it is invocable as `enb.call(0x00658430, verbmgr, {verb_id})`. Real verb_id->action map read from the dispatcher body (NOT the subagent's guesses): `0x10000`->op 0x1e on self (scan/inspect), `0x20000`->op 0x1d **Dock** (+Landing seq), `0x30000`->local `FUN_00581e60`, `0x40000`->op 0x0a **Attack**, `0x50000`->local `FUN_00744e70(0)` **Follow** (message_1.wav), `0x60000`->op 0x14 **Trade** (guards self-trade), `0x70000`->op 0x01 on self, `0x80000`->op 0x1c (closes frame), `0x90000`->op 0x11, `0xa0000`->op 0x12 **Board** (SR_scifielectronic40.wav), `0xb0000`->op 0x19 **Loot**, `0xc0000`->op 0x1a. The dispatcher reads the target id from `this+0x88 -> +0x90` and the player id from `worldmgr[1099]`. (2) **the per-target active-button source is the problem.** The button panel the game pops on hover is a CHROME_VT leaf CREATED on mouseover and DESTROYED off-hover (AS-13 (4)), so it cannot be read persistently for the overlay -- the letters must come from the persistent **VerbManager** whose verb catalog is the pointer array `[VerbManager+0xd4 .. +0xd8)` (each entry a verb obj with its id at +0x64). **BLOCKER: the live VerbManager pointer is not yet robustly located.** A depth-3 fingerprint scan (object with a 1..16 ptr array at +0xd4/+0xd8 whose every entry has a valid 0x10000..0xd0000 id at +0x64) seeded from `cockpit_ctrl`/`target_ctrl`/`target_obj` found only ONE weak hit (a 1-entry list, id 0xd0000 -- a false positive; the real catalog has ~13 entries). So the VerbManager is a different UI/shell singleton, reachable from neither the cockpit nor the target controller within depth 3; the decomp global that holds it still needs to be found (`FUN_00595530`/`FUN_00658430` are its methods but are called via internal thunks that hide the `this`). (3) **hide** = clear ENABLE on the hover-created transient leaf (mechanism known); deferred until (2) lands so hide + redraw ship together. **2026-06-28 (this session) -- VerbManager capture: FIRST attempt DISPROVEN live, then RE-ANCHORED.** The first capture hook targeted `FUN_007387f0`, which the decomp confirms is `VerbListPacketClass::Process` -- a NETWORK PACKET handler (`M = param_2`). Our local Freya server never emits that verb-list packet, so the hook NEVER FIRES: live, `enb.verbmgr()` stayed 0 even with Luna Station AND a mob targeted. (The earlier "n=1 mob catalog" came from the old fingerprint scan, never from this hook -- it was never validated live until now.) **REPLACEMENT (deterministic, no packet, no target, no hover):** hook `FUN_00741180` = the world/player manager M's INITIALIZER (`__fastcall(ECX = M)`), which runs once at world entry and itself constructs the VerbManager into `M[0x4a5]` (= `M + 0x1294`). It does not depend on any server packet -- a world manager always exists once in space -- so it fires every session. `hk_VerbList` rewritten to capture `ECX` (M); `game::addr::WorldMgrInit = 0x00741180` replaces `VerbListProcess`; `hooks::world_mgr()`/`enb.verbmgr()`/`enb.addr.VerbDispatch` unchanged. Builds clean -Wall -Wextra; staged to `bin/enbmod.dll`. Lua side (`target_frame.lua`) unchanged: `read_verbs()` walks the catalog `[vm+0xd4 .. +0xd8)`, reads each verb's LABEL (char* at +0x14) for the button letter + id at +0x64; `draw_buttons()` lays one square single-letter glass button per verb across the TOP of the target area (wraps rows, pushes the bars down); `on_input` hit-tests + fires `enb.call(enb.addr.VerbDispatch, vm, id)` on LBUTTONDOWN. **STILL OPEN:** (a) live validation -- the new init hook only fires on a FRESH zone-in, so it needs a client relaunch + login-to-space, then `enb.verbmgr()` should be non-zero immediately (no target needed); retarget a STATION/GATE (rich verb set) per owner instruction; if init still yields 0 live, fall back to the hover-frame constructor (`FUN_006580b0`, proven to fire on hover, stores VerbManager at frame+0xc8). (b) hide the native hover-transient button cluster (mechanism known, not yet coded); (c) plans/29 CV entry for the native-dispatch wire path. **LIVE 2026-06-28 (relaunched, char at Luna): M-capture CONFIRMED, catalog mechanism ROOT-CAUSED.** `enb.verbmgr()` returned a valid non-zero VerbManager (0x71B6238) immediately on entering space -- the `FUN_00741180` world-init hook fires deterministically, no target/hover/packet needed. Catalog offsets re-confirmed against the dispatcher decomp (`FUN_00658430` searches `[this+0xd4 .. +0xd8)`, id at entry+0x64). BUT the catalog read `n=0` for EVERY target tried -- mob (Addled Hound Bot), Luna Station, and gates (Sector Gate to Earth, Gate to Alpha Centauri) -- because **verbs are RANGE-GATED**: server `Player::UpdateVerbs` (PlayerClass.cpp:3501) sends ONLY `0x5C VERB_UPDATE` (there is NO `0x5B VerbList` emit anywhere) and only when a verb's active-state flips, which needs `RangeFrom(target) <= activate_range`. `StaticMap::OnTargeted` (NavTypeClass.cpp:432) adds DOCK/REGISTER/GATE at `DOCKING_RANGE = 5000`. We spawned ~52k from Luna Station, so no 0x5C is sent and `vm+0xd4` stays empty -- correct behaviour, not a bug. So the catalog only populates within 5000u of a station/gate. **BLOCKED on flight:** the freya-hud `hide_cockpit` hides the native warp orb and there is no Freya warp replacement yet, and calling `enb.addr.WarpPath` blind is the same freeze risk that hit 3x this session -- so the ship cannot be safely driven within docking range from the agent side. Needs the owner to fly within 5000u of Luna Station (or a gate); `target_frame.lua` loaded error-free and will render the letter buttons + allow dispatch the instant `read_verbs()` sees a non-empty catalog. **2026-06-28 (later, same session) -- BOTH above conclusions DISPROVEN; range was a red herring.** Flew the live client POINT-BLANK onto Luna Station (target locked, class=Starbase, DOCK clearly available in-game) and `enb.verbmgr()+0xd4/+0xd8` STILL read `[0,0]`. Root cause: **`enb.verbmgr()` = `*(M+0x1294)` is the WRONG object.** Dumping it (`+0x20` onward, stride 0x20, uniform vtable `0xb1474c`) and reading the node payload labels shows `EffectClass / BaseAssetID / Duration / ScaleToRadius / Distance / ChangeTime / StartLinkID / Description / TargetCenterToPoint / ...` -- it is an **effect/property-descriptor `std::map`, not the verb manager at all.** Reading `+0xd4/+0xd8` off it lands MID-NODE (record-start+0x14/+0x18, which are the zero fields of a 0x20-byte node), so "empty catalog" was an artifact of reading garbage offsets into an unrelated object -- never a real empty vector, never range. The prior "M[0x4a5]=VerbManager validated live (0x71B6238)" was a FALSE POSITIVE: only the pointer's non-zero stability was ever confirmed, never that it was the verb store. `target_ctrl+0xd4/0xd8` is also wrong (they're float HUD-bar fractions). The dispatcher `FUN_00658430`'s `this` (verb vector at +0xd4, target id at +0x88->+0x90) is a DIFFERENT, still-unlocated object -- almost certainly a transient context-menu/verb-UI object built when the radial menu opens, not a persistent singleton off M. **STILL VALID from the RE:** the verb_id->action map + the dispatch function itself. **CORRECTED PATH:** stop trying to read a persistent VerbManager catalog off an M offset; instead capture the active-target verbs by HOOKING the verb-update packet handler `FUN_007388b0` (`VerbUpdatePacketClass::Process`, `__thiscall(packet, M)`) -- it receives the `0x5C VERB_UPDATE` payload (verb ids + enable/disable attributes) the moment the server pushes it, which is exactly the native button set. Dispatch then replays the dispatcher's per-verb op calls (op 0x1d Dock, 0x0a Attack, etc., fully mapped) directly with the target id, or via the located `this`. `enb.verbmgr()`/`game::addr::WorldMgrInit` (0x1294) to be removed/re-derived; do NOT ship the `vm+0xd4` read. **SHIPPED 2026-06-28 (pragmatic direct-dispatch + class-gated buttons):** abandoned the persistent-catalog hunt (the dispatcher `this` has no static xref and the server rarely emits 0x5C, so hooking `FUN_007388b0` would usually capture nothing). Instead the buttons now drive the **server command path directly**, mirroring the already-working chat-send. New `game.h` addrs: `CmdBuild = 0x00876360` (`__thiscall(cmdbuf; player, op<char>, target, extra)`, writes buf[0..6]), `AutoFollowBuild = 0x0089d070` (`__thiscall(cmdbuf; player, op)`, no target field), `CmdSend = 0x00728150` (`__thiscall(M; cmd)`, does `*(*(M+0x1124)+8)(cmd)`). M offsets re-derived: `world::player_id = 0x112c`, `world::connection = 0x1124`, `world::tgt_container = 0x88`, `world::tgt_gid = 0x90`. Removed the disproven `verb` namespace + `addr::VerbDispatch`; `enb.verbmgr()` -> `enb.worldmgr()` (raw M). New Lua `enb.target_action(op[, no_target])` (lua_api.cpp `l_target_action`, static 0x20 zeroed cmd buf): reads player from M, target gid from `target_obj()->+0x88->+0x90`, builds via CmdBuild/AutoFollowBuild, pushes via CmdSend. `enb.target()` table gains a `class` field (container+0x3c char*). Op bytes (from dispatcher body): Attack 0x0a, Follow/autofollow 0x0c, Loot 0x19, Board 0x12, Trade 0x14, Dock 0x1d. `target_frame.lua` `read_verbs()` is now a **class-gated fixed table** (cls_has/is_station/is_gate/is_ship): A/F/L/B for ships, T for ship+station, D for station+gate; `on_input` fires `enb.target_action(r.op, r.no_target)`. Builds clean -Wall -Wextra, staged bin/enbmod.dll. **STILL OPEN:** (a) ONE controlled `enb.target_action` validation via the cmd channel against a real station/gate target BEFORE trusting buttons (freeze-safety -- mirrors chat-send so low risk, but unverified live); (b) Dock op 0x1d sends the server dock command but omits the client-side Landing_Init/camera sequence the native verb runs, so the station transition may not fire client-side (CV-AS-DOCK); (c) HIDE the native hover-transient target-action button cluster (mechanism known: clear ENABLE on the transient leaf, not yet coded); (d) refine the fixed verb set against the real per-target verb list if the server ever reliably emits 0x5C. plans/29: CV-AS-VERBDISPATCH (native command-path wire check) + CV-AS-DOCK (client landing seq). **2026-06-28 (later, same session) -- CORRECTED OP MAP + REWRITE (the previously-shipped ops were WRONG).** The "op bytes from the dispatcher body" used above were mislabeled, which is why the owner saw the station buttons do the wrong thing ("T" ran a hack, not register). Ground truth, cross-checked three ways: (1) the client dispatcher `FUN_00658430`'s `param_2 >> 16` IS the server **`VERBID` enum** (server/src/ObjectClass.h:464): SCAN 1, LAND 2, LOOT 3, GROUP 4, MESSAGE 5, TRADE 6, TRACTOR 7, DOCK 8, PROSPECT 9, GATE 0xa, REGISTER 0xb, JUMPSTART 0xc, FOLLOW 0xd. (2) Each verb's CmdBuild **op byte = the server avatar-command switch case** it drives (server/src/PlayerConnection.cpp): Tractor **0x01**, Group **0x0a**, Prospect **0x11**, Gate **0x12**, Trade **0x14**, Register **0x19**, Jumpstart **0x1a**, Dock **0x1c**, Land(planet) **0x1d**, Scan **0x1e**. (3) Per-class verb SETS = the server `AddVerb` calls: NavTypeClass -> Station: DOCK+REGISTER, Gate: GATE; ResourceClass -> PROSPECT+TRACTOR; MOBClass/PlayerClass -> FOLLOW (+Trade/Group on players). **Key corrections vs what shipped:** station Dock is op **0x1c** (the old `0x1d` is the PLANET Land op), and Register is op **0x19** (the old code labeled `0x19` "Loot" -- it is REGISTER). The owner-confirmed native station buttons are exactly **R (Register) + D (Dock)**, which now match. **Loot is NOT shippable via CmdBuild:** the server command switch has NO loot case (looting rides the proxy-handled loot band) and the client dispatches LOOT (verb id 3) through a local HulkUI panel object (`FUN_00581e60`) reachable only via the native button -- so the husk Loot button is **deliberately omitted** rather than shipped as a no-op/freeze. **REWRITE landed:** `l_target_action(op, mode)` -- `mode` = "target" (CmdBuild on the locked target) | "self" (CmdBuild with the player's own gid, for Tractor) | "follow" (AutoFollowBuild). `target_frame.lua` VERBS table is now the server-authoritative class-gated set: Station -> **D**ock(0x1c)+**R**egister(0x19), Asteroid/Resource -> **P**rospect(0x11)+**T**ractor(0x01,self), Ship/mob/player -> **F**ollow(0x0c,follow). **Layout per owner ask:** the frame is now anchored BOTTOM-RIGHT with scaled edge gaps and stacks UPWARD -- bars at the bottom, distance above them, target name above that, the letter buttons on top (button DESIGN unchanged). DLL rebuilt clean -Wall -Wextra, staged to the client; Lua synced to the launcher store. **STILL OPEN:** (a) live-validate D/R against Luna Station (relaunched this turn) -- the CmdBuild path is the proven chat-send convention so freeze-risk is low, but unverified; (b) CV-AS-DOCK still applies (Dock 0x1c sends the server command but not the client Landing_Init/camera seq); (c) hide the native hover button cluster; (d) wire Loot via the HulkUI panel capture (future). **LIVE-VERIFIED 2026-06-28 (in-game at Luna, owner + agent): layout CONFIRMED correct (bottom-right, stacked up: D R buttons on top, name "Luna Station", "Dist 2.82k", bars at bottom) and the verb SET is correct (Starbase -> D+R). F (Follow) on ship targets WORKS (owner: works on enemy + friendly).** BUT **D (Dock 0x1c) and R (Register 0x19) do NOT fire server-side** -- `enb.target_action(0x19,"target")` returns true yet the server logs nothing and nothing happens in-game. The Follow path (AutoFollowBuild 0x0089d070) works; the CmdBuild "target" path (0x00876360) builds a no-op. So the bug is the **CmdBuild invocation** for the target-mode ops (wrong builder / wrong arg signature / wrong target read), NOT the op-byte map. Committed the good state (layout + Follow); **D/R dispatch is the open fix** -- re-derive the real native Dock/Register command-build path from the dispatcher (FUN_00658430 -> the actual builder it calls), since CmdBuild alone is insufficient. **ROOT-CAUSED + FIXED 2026-06-28 (this turn): the bug was the TARGET GID, not the builder.** CmdBuild/CmdSend/call_thiscall are all byte-identical to native (re-verified); the prior "CmdBuild builds a no-op" diagnosis was wrong. The target-mode code read the gid as `*(target_obj()+0x88)+0x90` -- it dereferenced through the `+0x88` aux/properties bag and read an AuxData field (live: `0x06F9CDE4`) instead of the GameID, so the server's `GetObjectFromID(Target)` returned null and dock/register fell through silently (case 28/25 are silent no-ops on a null/non-station obj -- consistent with "no message, nothing happens"). Three decomp facts prove the gid lives DIRECTLY on the contact object at `+0x90`: (1) the dispatcher reads the target id for EVERY verb as `*(*(this+0x88)+0x90)` (FUN_00658430: op 0x1c dock L878118, 0x19 register L878110, 0x0a, 0x1d, 0x14); (2) in TargetFrameRefresh (FUN_00512400 L688749/688775) the resolved object `iVar7` -- exactly what is handed to `TargetEntitySet(iVar7)`, i.e. our captured `g_target_obj` -- has its id read as `*(iVar7+0x90)`, so the dispatcher's `*(this+0x88)` == our `g_target_obj`; (3) the gid->object hash lookup FUN_00727070 (L1005610) validates a bucket with `*(object+0x90) == gid`, proving an object stores its own GameID at `+0x90`. Live probe confirmed `g_target_obj+0x90 = 0x00018705` (the real station GameID; the low value is fine -- the hash is keyed mod 257) vs the wrong `container+0x90 = 0x06F9CDE4`. **Fix:** `l_target_action` target mode now reads `u32(target_obj() + tgt_gid)` directly (no `+0x88` indirection); `game.h` tgt_gid comment corrected (`tgt_container 0x88` still used by target_info for the aux/hull/shield/name bag). DLL rebuilt clean -Wall -Wextra, staged to the release dir + bin/. **LIVE-VALIDATED 2026-06-28 (agent, "you do it") -- D (Dock 0x1c) FIRES END-TO-END.** Relaunched the client (DLL re-injected), logged in (Luna), flew point-blank onto Luna Station, locked it as target (`enb.target_obj()=0x08A7C088`, `+0x90 gid=0x00018705` -- the real station GameID the fix now reads), and dispatched `enb.target_action(0x1c,"target")`. The client DOCKED FULLY: state flipped `space/inspace=true` -> `inspace=false`, the view swapped to the Luna Station interior (third-person avatar on the deck), and the green dock-officer line printed "Landing clearance granted. Welcome to Luna Station." So the gid-offset fix is correct AND **CV-AS-DOCK is RESOLVED POSITIVELY: op 0x1c ALONE drives the full client-side station transition** (scene swap + dock dialogue) -- no separate client Landing_Init/camera call needed. Register (0x19) rides the identical path (same target gid, same CmdBuild/CmdSend) differing only in the server switch case, so it is validated by extension (can't re-test standalone while already docked/registered at Luna). **DONE: D/R dispatch + CV-AS-DOCK.** **R-feedback diagnosis 2026-06-29 (owner: "click R, no button-press anim and nothing happens"): R is NOT broken -- it is an idempotent register no-op + a too-subtle click flash.** D and R take byte-identical client code (same `btn_rects`/`point_in` hit-test loop, `l_target_action` does not branch on op, only the x-offset + op byte differ; D's hit-test is proven by the live dock). At Luna the char is already registered ("Registration Confirmed" already in chat), so server register case 25 correctly does nothing on repeat clicks -- "nothing happens" is correct. The missing "button-press anim" was a real UX gap: the click glow was an 8-tick near-flat color shift, easy to miss. **FIXED (26a62fa2): hot state now paints a vivid cyan fill (0x7ad4ff/0x2a8fd6) at full alpha + bright border + dark letter, held 16 ticks; action path unchanged.** Hot-reloaded live. Remaining smaller follow-ups: (c) hide the native hover button cluster; (d) wire Loot via the HulkUI panel (`FUN_00581e60`) capture. **2026-06-29 -- FULL VERB SET BOUND + native chat-control widget hidden (owner: "make sure ALL verbs are bound dont miss any").** The `VERBS` table in `target_frame.lua` now binds every CmdBuild-dispatchable verb, class-gated so first-letter reuse never collides on one target: Station -> **D**ock(0x1c)+**R**egister(0x19); Gate -> **G**ate(0x12); Planet -> **L**and(0x1d); Resource -> **P**rospect(0x11)+**T**ractor(0x01,self); any ship/mob/player -> **F**ollow(0x0c,follow); player only (gid >= 0x40000000) -> **T**rade(0x14)+**G**roup(0x0a)+**J**umpStart(0x1a). New class predicates `is_planet` (cls "planet"), `is_player` (is_ship && gid >= 0x40000000); `is_ship` now also excludes planet/resource; `draw_target` enriches `t.gid` from `target_obj()+0x90`. **Three verbs DELIBERATELY left unbound (would ship dead buttons), reasons recorded:** (1) **Loot** -- no server avatar-command switch case (loot rides the proxy loot band); client loots via local HulkUI panel `FUN_00581e60`, future work. (2) **Message/Hail** -- no server case 5 (MESSAGE); client-side hail with no command-path dispatch. (3) **Scan** -- server case 30 exists but the server attaches the SCAN verb only to mission objects (`PlayerMissions` AddVerb), and there is no client-side class signal to gate the button, so binding it would mis-show Scan on the wrong targets. Commit a363c960. **Native chat-control widget hidden (owner: "hoverable up arrow for the old chat and split pane options still, remove those"):** the top-right "change text window split / hide-show chat window" button + its up-arrow (one CHROME_VT panel, formerly "Unknown3" in `native_hud_hide.lua`) was the ONLY native chrome leaf still at the full 0x700 draw gate after the rest of the stock HUD was hidden -- so it stayed visible AND hoverable. Identified live via `enb.hud.dump()` (lone fl=0x6701 leaf), confirmed by hiding it (cluster vanished), renamed to `ChatSplitControl`, added to the default-hidden set so it clears its ENABLE bit on load. Commit 433843cc. **STILL OPEN (low priority):** wire Loot via the HulkUI panel capture. **2026-06-29 -- Land (L) REMOVED (owner: "click L on a planet plays the hack animation, doesnt land").** Live-verified the planet class string IS "Planet" (Inverness), so `is_planet` fires correctly -- but the ACTION is wrong: E&B has no planet-surface gameplay. Server case 29 (op 0x1d, "planet landing button (or our satellite deployment button)") only routes the player when `obj->Destination() > 0` (a rare transit/gate planet); for an ordinary planet it falls through to the `else` DEPLOY branch -- `SendEffectRL(gid,0,10007,...)` + `MissionObjectVerb(obj, DEPLOY_ITEM)` -- which is the deploy/scan effect the owner saw as a "hack animation". The native client shows Land only on landable planets via the server verb list (which we don't read); the class string is "Planet" for ALL of them, so we cannot distinguish landable from not. Showing L on every planet misfires the deploy effect, so Land is OMITTED like Loot/Message/Scan until a real landability signal exists. `is_planet` kept (still excludes planets from the ship-like Follow gate). So the bound set is now D/R/G/P/T/F/Trade/Group/JumpStart; deliberately unbound: Loot, Message, Scan, **Land**. **2026-06-29 -- Land (L) RESTORED; the "no planet-surface gameplay" conclusion was WRONG (owner: "inverness IS landable", "it goes to Inverness Planet once you land", "it played hack effect").** The DB confirms it: Inverness planet (`sector_objects` id 1083) has `gate_to = 4093` (the "Inverness Down" planet zone, sector 4093), so its `Object::Destination()` is non-zero and server case 29 SHOULD route the ship there exactly like a stargate -- the deploy/hack branch (effect 10007) is only the `Destination()==0` fallthrough. The L button is back (`is_planet`, op 0x1d, mode "target", sends the planet's GameID at entity+0x90 -- the same id Dock uses). NOTE `sector_objects_planets.is_landable` exists in the DB but is UNUSED by the server (zero refs), so it is not the discriminator. **Open: live diagnosis of why op 0x1d still deployed instead of landing on a confirmed-landable planet.** Evidence chain says obj resolves in the land branch too (the deploy effect played ON the planet, so `GetObjectFromID(GetTargetGameID())` found it, and that gid == the locked target gid == entity+0x90), so the skip must be a runtime `Destination()==0` (despite DB gate_to=4093) or `m_Gating==true`. A TEMPORARY `LogMessage` diagnostic is in `PlayerConnection.cpp` case 29 (logs Target/obj/type/Dest/m_Gating) -- UNCOMMITTED, MUST be reverted after the next landing test (server was rebuilt with it, which dropped the owner's live session -- a process mistake to flag before bouncing next time). **2026-06-29 -- Ctrl+U master UI toggle ADDED (owner ask: "shortcut to toggle the freya ui / native hidden ui on and off, in case something is bugged").** One shared flag `enb.freya_ui_on` (default on) on the global table flips the WHOLE Freya overlay: `lib/freya_hud.lua` gains `H.ui_on/set_ui/toggle_ui` and `H.vis()` returns hidden when off (blanks every Freya overlay -- they all gate on `H.vis()`); `mods/hide-ui/native_hud_hide.lua` `apply()` re-shows ALL native chrome each tick while the flag is off (stock HUD returns intact); new `mods/freya-hud/ui_toggle.lua` is an always-listening `on_input` (NOT gated on in-space, so it can turn the UI back ON) that tracks Ctrl down-state and flips the flag on Ctrl+U keydown, swallowing only that chord. Pure Lua (hot-reloads, no DLL rebuild); parse-checked + staged to the launcher store. Live-verify pending (client was killed at owner request). **Ctrl+U LIVE-CONFIRMED 2026-06-29:** `enbmod.log` shows the owner toggling "freya UI ON / OFF (native HUD restored)" repeatedly on the relaunched client -- the chord works. **Land "did nothing" ROOT-CAUSED 2026-06-29 = OUT OF RANGE, not a server bug.** A temporary `LANDDBG LogMessage` in case 29 + a live distance probe showed the owner was **150,243 units** from Inverness while server `DOCKING_RANGE` (the verb-activation range, `PlanetClass.cpp` `AddVerb(VERBID_LAND, 5000.0f)`) is **5000, measured EDGE-to-edge** (`Object::RangeFrom` subtracts `m_ObjectRadius`). Planets are huge (stock radii ~35k-120k), so the native Land verb only activates near the surface. Our Freya L button showed at ANY range and fired case 29 from 150k out, which set `m_Gating=true` with no client approach -> the handoff never completes and the verb-set wedges (a second click then falls through to the deploy/effect-10007 branch -- the earlier "hack effect"). The server is CORRECT; the bug was the client button firing a docking verb the native client would have greyed out. **RANGE-GATE FIX SHIPPED 2026-06-29 (client-side, no server change):** `target_frame.lua` gates only the wedge-prone docking verbs (D/R/G/L) by a per-verb CENTRE-distance ceiling = (largest body radius for that class + DOCKING_RANGE): stations/gates `DOCK_GATE=20000`, planets `LAND_GATE=130000`. The ceilings exceed the real activation distance so they NEVER false-disable an in-range button; they only catch the wildly-out-of-range case. Out-of-range docking buttons render DIMMED and, when clicked, paint an amber "denied" flash + `enb.log("<verb>: out of range -- fly closer")` and do NOT fire (mirrors the native client greying an inactive verb). P/T/F/Trade/Group/JumpStart are deliberately UNGATED -- they don't set `m_Gating`, so out-of-range just no-ops server-side. Pure Lua, parse-checked (built `luac` from the bundled Lua 5.4 src), staged + hot-reloaded live via `reload()` over the cmd channel ("target_frame.lua loaded" confirmed). NOTE: the C++ mtime auto-reload (`dllmain.cpp` poll of top-level `scripts/init.lua`) re-`dofile`s init but does NOT clear `package.loaded`, so it serves CACHED mod modules -- only the global `reload()` (cmd channel / `/run`) re-reads changed mod `.lua` from disk. The LANDDBG diagnostic was REVERTED in source (`PlayerConnection.cpp` case 29); it is harmless log spam in the currently-running 17-min-old server binary and will compile out at the owner's next relaunch (NOT rebuilt now -- the owner is actively in-game and a server bounce would drop their session). **STILL OPEN:** owner must fly within ~5000u of Inverness's surface and click L to actually land (the in-range landing has never been tested end-to-end -- only out-of-range misfires were observed); if `m_Gating` is stuck from the earlier out-of-range clicks, a relog clears it. Radius-aware tightening of the gate (read the target body radius client-side -> gate on true edge-distance at a flat 5000) is a future refinement needing a live offset probe. **2026-06-29 -- "Land = out of range" headline DISPROVEN by the owner; real cause = our op 0x1d skips the client-side landing sequence.** Owner, IN RANGE this time: "clicked L, said 'Confirmed' audio but did nothing; second click did the hack anim; then Ctrl+U -> old UI -> native Land -> works. So ours is still fucked." Native Land working from the same spot kills the range theory. Decompiled the native verb dispatcher: the LAND branch (`FUN_00658430`, param_2 == `0x20000`) does FAR more than send op 0x1d -- after `CmdSend(0x1d)` it runs the client-side landing seq: `FUN_0079b2b0(8)`, stores the target gid into the ship/player field `piVar4[0x4ee]`, plays "Landing Init", and calls the ship-controller vtable slot `0x30` WITH arg 1 + `FUN_006fc950(1)` (DOCK/op 0x1c calls slot 0x30 with NO arg and skips all of this -- which is why Dock completes server-side alone but Land cannot). Server case 29 only sets `m_Gating=true` + `SetStargateDestination(obj->Destination())` and waits for the player to ARRIVE; the client landing autopilot is what flies the approach that triggers the arrival handoff. Our bare op 0x1d sets up the destination but never starts the approach -> never arrives -> nothing lands; the second click falls through to the deploy/effect-10007 branch (the "hack anim"). **FIX SHIPPED 2026-06-29 (owner re-corrected the model: "THERE IS NO MANUAL FLY IN / it works exactly like a gate / but the button is just a different label / and it has a different animation / if you can do gate you can do land").** The native client autopilot seq above (vtable-slot-0x30 + FUN_006fc950/FUN_0079b2b0) is ONLY the cosmetic fly-in animation; it is NOT what changes the zone. Landing is a TWO-STEP server exchange exactly like a stargate, confirmed in `PlayerConnection.cpp`: op `0x1d` (**case 29**) only ARMS -- requires `obj && m_Gating==false && obj->Destination()>0`, sets `m_Gating=true` + `SetStargateDestination(obj->Destination())` and plays the "Confirmed" sound; op `0x08` (**case 8**) COMPLETES -- `TerminateWarp()` + `SectorServerHandoff(this, StargateDestination())`, and case 8 does NOT check the player's position (only that the destination is armed). (Gate analog: 0x12 arms via GateActivate, 0x13 finishes via the same handoff; Dock 0x1c is a one-shot because it has no two-step.) So the new `"land"` mode in `l_target_action` (lua_api.cpp) sends 0x1d then 0x08 back-to-back -> instant transit to the planet zone, no client autopilot, no fly-in -- "a different label + animation from a gate", as the owner described. `target_frame.lua` L entry is `op=0x1d, mode="land"`. DLL rebuilt clean -Wall -Wextra, staged to bin/enbmod.dll. **LIVE-VALIDATED 2026-06-29 (owner relaunched + clicked L on Inverness: "works!!!").** The 0x1d+0x08 two-step lands instantly like a gate -- the native client landing autopilot was never required; SectorServerHandoff drives the zone change. DONE. **Ctrl+U sector-transition bug FIXED 2026-06-29 (owner: "Ctrl+U gets fucked up between sectors; leaves old UI showing even when you toggle back; chat box bg + target frame stuck").** Root cause of the Ctrl+U half: `ui_toggle.lua` LATCHED `ctrl_down` from KEYDOWN/KEYUP, but a sector transition steals window focus mid-chord so the Ctrl KEYUP is DROPPED -> latch stuck `true` -> any later `U` (even typed in chat) silently toggled the UI. Fix: new DLL primitive `enb.key_down(vk)` (GetAsyncKeyState, stateless -- cannot get stuck) and `ui_toggle.lua` rewritten to read Ctrl LIVE at the instant U is pressed (no latch), with leading-edge-only handling so held auto-repeat doesn't flap. DLL rebuilt clean -Wall -Wextra, staged. **Native-chrome bleed-through half (old UI + chat bg + native target frame stuck after a gate) -- DIAGNOSED, fix needs live data.** `hide-ui` re-hides by TAIL-ANCHORING (persistent panels assumed to be the last 16 CHROME_VT leaves); when the HUD scene is torn down + rebuilt on a gate the leaf list is transiently mis-shaped (wrong count -> `map_panels` no-ops -> native flashes; or a lingering transient shifts the mapping -> wrong panels hidden -> chat bg + target frame stay shown persistently). Will NOT rewrite the hard-won tail-anchor on a guess: needs a one-shot `enb.hud.dump()` captured immediately before AND after a gate on the live client to see the real reshuffle, then fix the anchoring against data. Pending a relaunch (DLL changed -> needs re-inject anyway). |
| AS-15 group frame action buttons | DONE + owner-confirmed 2026-07-01 (agent live-verify: grouped multibox pair on the new DLL, rosters/is_leader/0xBC send all clean; owner: "works great") (CV-AS-GRPBTN resolved) -- Freya-drawn rows above the party frame (leader D/F/T, member L/F/T; F expands the formation sub-row, leader S/B/P/X = GroupAction 4/5/6/9, member U/X = 7/8), roster shifted below. New DLL API `enb.group_action` (native CTA builder -> 0x00BC CTARequest via CmdSend) + `enb.is_leader` (the client's own leader check, VEH-guarded). D/L = the native /group disband//remove avatar-commands (0x0d/0x0e self). Degrades to the plain frame on an older DLL. Server GroupAction 12 is a chat-notify stub upstream; recorded, not changed. |
| AS-16 Prospect verb on prospectable targets | DONE + live-verified 2026-07-02 -- the target-frame `is_resource()` gate missed asteroids because it matched the class token "asteroid"/"resource" while the live class string is exactly **"Harvestable"** (name "Asteroid"), confirmed by a one-shot `enb.target()` read. `is_resource()` now matches harvestable/asteroid/resource/field/ore/cloud, so P (Prospect 0x11) + T (Tractor 0x01,self) show on a locked rock. Pure Lua, hot-reloaded via `reload()`. Commit 34a529e7. |
| AS-17 Freya loot/salvage frame (LEFT-anchored readout) | in progress 2026-07-02 -- the native hulk loot window pops on the RIGHT, on top of the Freya party frame. New LEFT-anchored `mods/freya-hud/loot_frame.lua` repaints the contents Freya-style: per-slot **Slot / Name / Stack** (+ an icon tile, texture blit pending a live offset RE). Data via new DLL binding `enb.loot()` -> replays the three hulk cargo accessors (`0x00689ca0` ItemTemplateID / `0x00689d20` StackCount / `0x0068aaa0` TemplateAt) off the CONTAINER captured READ-ONLY from a naked ECX hook on `0x00689ca0` (`hooks::loot_container()`); each row `{slot,name,stack,tid,tmpl,asset}` + top-level `src` (container inventory-name, the hulk-vs-ship-inventory discriminator). `enb.loot_age()` gates freshness so a closed (possibly freed) container is never read. game.h (`addr::Cargo*` + `namespace cargo` offsets), hooks.cpp/.h (hook + accessors), lua_api.cpp (`l_loot`/`l_loot_age`). **READ path LIVE-VALIDATED 2026-07-02**: prospected asteroid reads "Copper Ore x5" byte-correct, `src=HarvestableResources`. Refinements shipped: header is just "Loot" (was "SALVAGE (src)"); a `src`-prefix filter drops the player's own holds (`Inventory*`), showing only lootable containers (harvest/hulk cargo). **TAKE path WIRED 2026-07-02 (premise corrected: native loot take is NOT opcode 0x27 -- that is the generic MOVE)**: the native take emits the compact **loot command, wire opcode 0x005D** `[ctx:u32][inv_type:u8][slot:u8]`, in-place ctor `LootBuild 0x008ae810`, pushed via `CmdSend 0x00728150` -- the identical "build object, hand to Connection" path the verb buttons already use. New `enb.loot_take(slot)`: ctx = local player game id (`M + world::player_id`; empirically both `+0x112c` and `+0x1130` hold it -- probed live); inv_type derived from the container name (`Harvest...`=>0x12 harvest, else 0x06 hulk cargo); the command object is allocated with the CLIENT operator new (`0x004e9cb0`) and NOT freed so the Connection send path frees it through the matching heap (a DLL-heap/static buffer would be a cross-heap free -> crash). This is NOT a server/proxy change and not a new packet -- the client already sends this exact command on a native loot click; we only drive the same builder+sender. Clicking a Freya row's icon tile calls it (Lua wire already live, guarded). game.h (`addr::LootBuild`/`ClientOperatorNew` + `cargo::inv_type_*`/`loot_cmd_size`), lua_api.cpp (`l_loot_take`). DLL rebuilt clean -Wall -Wextra + staged to `bin/enbmod.dll`. **NEEDS: one relaunch to load the new DLL, then live click-to-loot validation against a real hulk; native loot window stays functional underneath until confirmed. Then RE the icon texture offset off `row.asset` (read-only, no rebuild).** CV-AS-LOOT (plans/29). |
