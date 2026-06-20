# Phase AW -- Sector exploration skill (agent-driven nav survey)

Owner ask (2026-06-20): augment the Claude skills so the agent can, beyond
login (Phase AU), drive the live client to **fly a sector visiting every nav
node**, discover the hidden/missing ones, plan efficient multi-node warp paths,
cross **gates** into adjacent sectors, **reroute around class/reputation-locked
gates**, and keep a **structured, persistent, gitignored ledger + timestamped
action log** so a sector counts done only when WE recorded visiting every node.

Skill lives at `.claude/skills/explore-sector/` (Freya/MIT). Built on the
`login-to-client` (Phase AU) `lib.sh` (window discovery, XTEST click, screenshot,
cmd channel). Authoritative node lists come from `docs/sectors/json/<S>.jsonl`.

## What shipped (AW-1, scaffolding + validated mechanics)

- **Scripts** (`scripts/`): `open-map.sh` (press M, shot, crop), `map-shot.sh`
  (shot+crop, no toggle), `click-map.sh <wx> <wy>` (XTEST click at window coords),
  `warp.sh` (Q), `gate.sh` (use-gate icon, `ENB_GATE_ICON` calibrated),
  `navdata.py` (sectors/list/identify), `state.py` (per-sector ledger:
  init/status/remaining/suggest/reveal/visit/complete/summary), `logaction.sh`
  (timestamped gitignored action log), `lib.sh` (reuses login lib + map helpers).
- **Persistent state** under `state/` is **gitignored** (`state/.gitignore` keeps
  the dir, ignores all runtime ledgers + `actions.log`).
- **SKILL.md** documents the full loop: identify sector (from map title and/or
  nav labels) -> init ledger -> read markers (green=in range, grey "?"=seen-not-
  visited, absent=not yet near per PB-22) -> plot the LONGEST visible path (click a
  far marker, the nav system draws the multi-node line) -> warp -> periodic shots
  -> mark visited -> fly near to reveal hidden nodes -> complete; then gate-cross
  to the next sector, rerouting around locked gates via the gate graph.

### Validated this session (live, Equatorial Earth)
- Map opens on M; markers render as green/grey "?" exactly matching PB-22's
  proximity-discovery model; map window TITLE is the sector display name.
- `navdata.py identify` resolves the map title to the data file across the naming
  variants (CamelCase `HighEarth`/`AragothPrime`, planet-suffixed
  `Equatorial Earth`->`Equatorial`) and by nav-label intersection.
- `state.py` init/visit/complete and `logaction.sh` work; all shell scripts pass
  `bash -n`; service logs (server/proxy/net7go/freya-online/postgres) clean.

## VALIDATED METHOD (live run, 2026-06-20 PM): screen-read + click + Q, no injection

Owner directive this session (overrides the memory-read path below): **screen-read
ONLY -- no mods, no injection, no calibration.** "the navs when you get close
ALWAYS show on the map ... just crop and read, and click." Done, and it works:

**The working loop (proven live, drove Equatorial 1/25 -> 3/25 this session):**
1. In the 3D in-space view, nav beacons render as **"?"** (grey/yellow, not yet
   identified) or **green** (discovered/in-range) icons. The bottom-right HUD
   panel shows the **selected target's name**; a `Dist` readout sits below it.
2. **Hover** a beacon (mousemove, NO click) -> a **tooltip prints the node name +
   distance** (e.g. `GETCo Sector Headquarters  161.30k`). This is the reliable
   identify primitive and confirms the beacon's pixel location.
3. **Left-click** the beacon -> the bottom-right target panel updates to that
   node (the confirmation the select landed). A non-nav object (cargo/asteroid)
   gives NO tooltip and does NOT change the panel -- skip it.
4. **Q** warps to the selected node; capture frames every ~3s through the warp
   tunnel until the node fills the view and `Dist` -> ~0. New green beacons appear
   in range during the flight (free discovery).
5. `state.py visit` + `logaction.sh`. Cooldown >=5s before the next warp.

Confirmed warps this session: GDI Mandela (hidden, was pre-targeted), then
GETCo Sector Headquarters (selected via hover+click of its "?" at 161k).

**Click-aim calibration (important):** clicks register at the **commanded
window-relative coordinate** (verified: command center (640,480) -> game cursor
tip renders at ~(640,487), dead-on; `xdotool getmouselocation` confirms the OS
pointer is exact). The game-drawn cursor *sprite* sits slightly up-left of its
hotspot, so do NOT judge aim by the sprite. The ONLY real difficulty is reading a
tiny beacon's pixel coords off a dark crop accurately -- misreads of 30-60px are
what cause missed selects. Mitigation: brighten + fine-grid the crop, and/or
hover-verify (tooltip present) before clicking.

**Honest throughput note:** the loop is correct but SLOW (~visible-beacon hunt +
hover-verify + warp per node), and beacons near a large station are occluded /
hard to read; there is no overhead map to select far navs from, so you can only
select what is currently visible and precisely hittable. Completing all 25
Equatorial nodes (and then all 84 sectors) is a long grind at this cadence, but
it is resumable via the ledger + action log. SKILL.md updated to teach THIS
method (the "press M for a top-down map" instructions were removed).

## CORRECTION (live run, 2026-06-20): there is NO top-down map in this build

The AW-1 premise -- "press M to open a top-down sector map, read markers off it" --
is WRONG for this client/build. Established live this session, with evidence:

- **`M` is not bound to a map.** Keys reach the game (Escape opens/closes the Quit
  Menu; the Freya HUD toolbar `Character` button opens the character panel), but
  `M` opens nothing. The prior session's "map" screenshots (`g1.png`, `clk2.png`)
  are the **3D in-space view**, not an overhead map -- the planet is the big sphere,
  the "?" markers are **nav beacons rendered in the 3D world**, and the "ship
  marker" was the cockpit. There was never a top-down map.
- **The Galaxy Map renders nothing.** It is a PDA child screen, opened via the
  game's own dispatcher `enb.pda_switch(4)` (0=Inventory 1=Skills 2=Character
  3=Vault 4=Galaxy Map; verified `pda_switch(2)` opens the Character panel, return
  1). `pda_switch(4)` returns success but draws no map -- the inter-sector galaxy
  map is non-functional/empty in this build. EnB has no separate top-down *sector*
  map; in-sector navigation is the 3D nav beacons + the HUD radar.
- **The only proven nav primitive:** in the 3D view, click a nav beacon -> it
  becomes the target, its name label appears, and the auto-path draws a line; then
  `Q` warps along it. This DID work (reached Guiana Space Port). But beacons are
  tiny, only visible in the current camera facing, and reading them off the 3D
  render is unreliable -- and with no overhead map you cannot see ALL beacons at
  once, so "visit EVERY node" cannot be guaranteed visually.

### The clean fix is blocked on calibration (which is unsafe to run live)

The robust approach is to read the sector nav list straight from client memory and
drive warps by data, not pixels. Decomp recon (external decomp, read-only) found:
- nav-list head candidate `0x00be8738`, count `0x00be8740`; entries are a 36-byte
  linked list: `next@0x00`, `nav_obj@0x04`, `distance@0x08 (f32)`, `flags@0x0C`
  (discovered/in-range). Builder `FUN_007b7ef0`, renderer `FUN_007b8510`.
- warp path lives on the sector/star-map object: node-ptr array between `+0x8C`
  (start) and `+0x90` (end), count `=(end-start)/4`. `FUN_007c0ee0`.
- nav_obj name/world-pos offsets are still TBD (need runtime probing).

BLOCKER: enbmod's offsets are **entirely uncalibrated** (`enb.offsets()` is all
`-1`/`0`), so `enb.self()`/`enb.target()` and any nav_obj field read return
nothing until calibration runs. Calibration/autocalibrate is the exact thing that
**froze a live client off the message pump** (commit `af27d03e`, and the
no-debug-instrumentation memory) -- so it MUST NOT be run against a client the
owner is using. Until offsets are obtained safely (calibrate on a throwaway
client, or hardcode the probed offsets into enbmod), the memory-read survey is not
available.

## Open (AW-2) -- decision needed from owner before more live grinding

- **Path A (data-driven, robust):** add a safe one-shot offset probe to enbmod
  (NOT a per-tick hook) + a `enb.nav_list()` reader that walks `0x00be8738`, plus
  fill the nav_obj name/pos offsets. Then the survey is precise and completeable.
  This is the right investment but is real enbmod + RE work, and needs a safe way
  to calibrate.
- **Path B (visual, best-effort):** keep the 3D-beacon click+warp loop, accept it
  cannot prove "every node" without an overhead map; rotate the camera to hunt
  beacons. Lower confidence, no completeness guarantee.
- **Full multi-sector survey of all 84 sectors** depends on whichever path; the
  ledger + log make it resumable either way.
- Equatorial progress so far (ledger): 1/25 visited -- Guiana Space Port (clicked
  beacon in 3D, warped, arrived; verified via the 3D label + "Warp target
  reached").

## Notes
- Client-only, no server/proxy/wire change -> no CLAUDE.md server-integrity gate
  and no `plans/29` CV entry. The only real-client proof is the survey running.
- MUST NOT register heavy `enb.on_tick` probes (pump-clocked tick = hang vector;
  see the no-debug-instrumentation memory and Phase AV). This skill uses
  screenshots + XTEST + the cmd channel only.
