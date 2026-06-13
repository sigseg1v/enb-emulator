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

### AS-6 Tier B research: true native-widget hide  `[ ]`

- `[ ]` Locate the client UI widget tree / per-widget visibility from the
  static anchors (XpBars 0x0058c450, LevelText 0x00548d60) + calib scans.
- `[ ]` `enb.ui.hide(kind)` once a safe flag is proven. Replaces the cover
  panels; until then Tier A stands.

### AS-7 Build + docs  `[x]`

- `[x]` `make` (i686-w64-mingw32) clean; README API table updated; note the
  fail-open swallow rule and the no-binary-assets decision.

## Status

| Item | State |
|---|---|
| AS-1 input/screen API | done -- `enb.screen()`/`enb.measure()`, `enb.on_input(fn,mask)` PeekMessageA swallow (fail-open), `enb.WANT_*`/`enb.msg.*`. hooks.cpp/.h, lua_api.cpp, overlay.cpp/.h |
| AS-2 draw primitives | done -- `rect_grad`/`rrect`/`rrect_grad` + alpha on `rect`/`line` (procedural gradient + triangle-fan corners, no assets). overlay.cpp/.h, lua_api.cpp |
| AS-3 calibration | partial -- scaffolding done (`scripts/autocalib.lua` find/probe/save, `scripts/.gitignore` for `calib_data.lua`, init.lua loads it via pcall). Live offset VALUES still need owner in-game session |
| AS-4 freya_ui.lua | done -- `scripts/freya_ui.lua`: cover panel + swallow, 12-button action bar (1..9 0 - =) with click->`enb.tap`/keypress-lit, 3 stacked stat bars (gray until AS-3) |
| AS-5 xp_overlay fix | done -- `scripts/xp_overlay.lua` rewritten: labels right-aligned LEFT of each bar, screen-bottom-anchored via `enb.screen()` |
| AS-6 Tier B hide | research, later |
| AS-7 build+docs | done -- `make clean && make` clean (zero -Wall -Wextra warnings); README API table + AS section updated |
