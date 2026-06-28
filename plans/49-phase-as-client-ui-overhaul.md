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
| AS-14b target action buttons (hide native + Freya letter-buttons) | in progress (owner ask 2026-06-28) -- FIRST attempt (force-enable every transient leaf at the 0x500 gate to keep the native buttons always-on) was REVERTED: it also caught the action-bar tab panel (yellow button stuck on tab switch) and did not reliably show the real buttons -- `0x500` is not a unique fingerprint. New direction: HIDE the native target-action buttons entirely (no hover), then REDRAW them in-overlay as Freya square buttons just above the target hull/shield bars, each a single letter = first letter of the action (Follow=F, Loot=L, Dock=D, Register=R, ...), click-wired to the native action. native_hud_hide.lua reverted to `086c68c9^`. **RE done 2026-06-28 (no code shipped yet -- the remaining piece is a real RE task, not a quick edit):** (1) **action dispatch FULLY MAPPED** -- the verb dispatcher is `FUN_00658430(VerbManager this, verb_id<<16)`, called only via the button gadget's click callback, so it is invocable as `enb.call(0x00658430, verbmgr, {verb_id})`. Real verb_id->action map read from the dispatcher body (NOT the subagent's guesses): `0x10000`->op 0x1e on self (scan/inspect), `0x20000`->op 0x1d **Dock** (+Landing seq), `0x30000`->local `FUN_00581e60`, `0x40000`->op 0x0a **Attack**, `0x50000`->local `FUN_00744e70(0)` **Follow** (message_1.wav), `0x60000`->op 0x14 **Trade** (guards self-trade), `0x70000`->op 0x01 on self, `0x80000`->op 0x1c (closes frame), `0x90000`->op 0x11, `0xa0000`->op 0x12 **Board** (SR_scifielectronic40.wav), `0xb0000`->op 0x19 **Loot**, `0xc0000`->op 0x1a. The dispatcher reads the target id from `this+0x88 -> +0x90` and the player id from `worldmgr[1099]`. (2) **the per-target active-button source is the problem.** The button panel the game pops on hover is a CHROME_VT leaf CREATED on mouseover and DESTROYED off-hover (AS-13 (4)), so it cannot be read persistently for the overlay -- the letters must come from the persistent **VerbManager** whose verb catalog is the pointer array `[VerbManager+0xd4 .. +0xd8)` (each entry a verb obj with its id at +0x64). **BLOCKER: the live VerbManager pointer is not yet robustly located.** A depth-3 fingerprint scan (object with a 1..16 ptr array at +0xd4/+0xd8 whose every entry has a valid 0x10000..0xd0000 id at +0x64) seeded from `cockpit_ctrl`/`target_ctrl`/`target_obj` found only ONE weak hit (a 1-entry list, id 0xd0000 -- a false positive; the real catalog has ~13 entries). So the VerbManager is a different UI/shell singleton, reachable from neither the cockpit nor the target controller within depth 3; the decomp global that holds it still needs to be found (`FUN_00595530`/`FUN_00658430` are its methods but are called via internal thunks that hide the `this`). (3) **hide** = clear ENABLE on the hover-created transient leaf (mechanism known); deferred until (2) lands so hide + redraw ship together. **NEXT SESSION:** find the VerbManager singleton (decomp: the accessor/global feeding `FUN_00595530`), read its catalog + the current target's active subset, draw Freya letter-buttons in `target_frame.lua` above the bars, wire clicks via `enb.call(0x00658430, ...)`, test Follow/Attack (reversible) live first. CV entry needed in plans/29 for the native-dispatch wire path before it counts as done |
