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
- `[ ]` Calibrate: player_ptr_addr, hull/shield/energy (+max), combat/
  explore/trade lvl + pct, pos. Until done the new stat bars render in
  "uncalibrated" gray, same convention as xp_overlay today.
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

### AS-6 Tier B: true native-widget hide  `[~]`  (vitals+xp ON by default 2026-06-13; skill open)

- `[x]` Suppression primitive: `enb.patch_ret(addr [,pop])` (lua_api.cpp
  `l_patch_ret`) overwrites a function entry with `ret` (0xC3, or 0xC2 imm16
  for callee-cleanup) under VirtualProtect + FlushInstructionCache. Refuses
  addrs below the image base; logs each patch. Mock records calls headless.
- `[x]` Paint targets pinned by behavioural analysis (2026-06-13): the hide
  target is each widget's per-frame PAINT routine, not the constructor/updater.
  `enb.addr.VitalsPaint` 0x005dcae0 and `enb.addr.XpPaint` 0x0058cf60 are both
  `void __fastcall(ECX)`, pop 0, pure paint (each just checks the per-gadget
  visible flag at +0x60 and calls the gadget paint primitive), so an early ret
  hides them clean. The earlier candidates were WRONG: VitalsBars 0x005dbfc0 /
  XpBars 0x0058c450 are constructors, EnergyBar 0x005dc4a0 is the value-updater,
  SkillButton 0x00662dc0 is the skill constructor -- ret-patching any of those
  leaves uninitialised pointers / frozen state and crashes. game.h + the init.lua
  HIDE block now name the paint entries; the ctor/updater addrs are kept and
  labelled "NOT a hide target".
- `[!]` Skill buttons have NO standalone pure-paint entry (render is fused with
  state mutation); hiding them needs a runtime per-gadget visible-flag write
  (clear gadget+0x60 once the live pointer is known), not a static patch. Open.
- `[x]` Vitals + xp hides turned **ON by default** (owner-directed 2026-06-13):
  init.lua's HIDE block now runs `enb.patch_ret(VitalsPaint)` +
  `enb.patch_ret(XpPaint)` at startup. init_spec asserts both paint entries are
  ret-patched (pop 0) and that the ctor/updater addrs are NOT.
- `[!]` ON ahead of real-client confirmation: "an early ret hides the widget
  without breaking gameplay" is still real-client-only. **CV-AS-HIDE-VITALS /
  -XP** in plans/29 remain open; if the client crashes on load, comment the two
  patch_ret lines. **CV-AS-HIDE-SKILL** stays open (no clean paint entry; needs
  the runtime visible-flag path).
- Note: the earlier "Tier A cover panel" approach is GONE -- the design is
  translucent glass, so an opaque cover would defeat the look. Suppression
  (patch_ret) is the only path to a clean result, and it is gated on CV.

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

## Status

| Item | State |
|---|---|
| AS-1 input/screen API | done -- `enb.screen()`/`enb.measure()`, `enb.on_input(fn,mask)` PeekMessageA swallow (fail-open), `enb.WANT_*`/`enb.msg.*`. hooks.cpp/.h, lua_api.cpp, overlay.cpp/.h |
| AS-2 draw primitives | done -- `rect_grad`/`rrect`/`rrect_grad` + alpha on `rect`/`line` (procedural gradient + triangle-fan corners, no assets). overlay.cpp/.h, lua_api.cpp |
| AS-3 calibration | partial -- scaffolding done (`scripts/autocalib.lua` find/probe/save, `scripts/.gitignore` for `calib_data.lua`, init.lua loads it via pcall). Live offset VALUES still need owner in-game session |
| AS-4 freya_ui.lua | done -- `scripts/freya_ui.lua`: cover panel + swallow, 12-button action bar (1..9 0 - =) with click->`enb.tap`/keypress-lit, 3 stacked stat bars (gray until AS-3) |
| AS-5 xp_overlay fix | done -- `scripts/xp_overlay.lua` rewritten: labels right-aligned LEFT of each bar, screen-bottom-anchored via `enb.screen()` |
| AS-6 Tier B hide | mechanism shipped, OFF by default -- `enb.patch_ret(addr,pop)` + commented init.lua HIDE block; correctness gated on CV-AS-HIDE-VITALS/-XP/-SKILL (real-client-only) |
| AS-7 build+docs | done -- `make clean && make` clean (zero -Wall -Wextra warnings); README API table + AS section updated |
| AS-8 headless test suite | done -- 43 tests green + 5 rendered screenshot scenarios (incl. station); `make test` |
| AS-9 design port | done -- freya_hud/freya_ui/xp_overlay ported to the `Earth & Beyond HUD.html` glass design; state-gated (space/station/login); cursor-on-top; specs + screenshots rewritten. Real-client checks: CV-AS-STATE/-CURSOR/-HIDE-* |
