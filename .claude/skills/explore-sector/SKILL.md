---
name: explore-sector
description: Fly the live EnB client through a sector to visit (and discover) EVERY nav node, then cross gates to do the same for adjacent sectors -- with a structured, persistent ledger of what is visited per sector and a timestamped action log. Use when the owner asks to map/explore/survey a sector or "visit all the navs". Requires the game already in space (use login-to-client first).
---

# Explore a sector (visit + discover every nav)

Drive the running `client.exe` (under WINE, `DISPLAY=:0`) to fly through a sector
hitting every navigation node, reveal the hidden ones, record progress in a
per-sector ledger, log every action with a timestamp, and then move on through
gates to neighbouring sectors. The goal: **one run per sector, every node in that
sector visited AND logged by us.**

This is mostly an AUTONOMOUS loop: `drive.py` selects nav targets with the game's
own nav-target keybinds (W/D/C, below), warps to each, and records progress.
The scripts do the mechanics (drive the nav cycle, screenshot+crop+OCR, press
warp, click the gate icon, update the ledger, append to the log). YOU step in for
the things the scripts can't do alone: reading a sector name, deciding a gate
route, and -- only when the ship is SEVERELY stuck -- looking at a map screenshot
to pick a marker by hand (the demoted fallback, below).

## How navigation works: the W / D / C nav keys (PRIMARY method)

The game has built-in keybinds that target nav nodes directly -- no map click, no
HUD target-arrow button. This is how we select every nav:

- **`W`** = lock the target filter to **NAVS** and select the **NEAREST** nav.
  It is idempotent: pressing W again re-anchors to the current nearest, and it
  bootstraps an empty selection, so there is never a need to "seed" with a map
  click first.
- **`D`** = step to the **next-FARTHER** nav (the cycle is distance-ordered).
- **`C`** = step to the **next-CLOSER** nav.

So `W` = 1st-nearest, `WD` = 2nd, `WDD` = 3rd, `WC` = farthest. To walk the whole
nav set in increasing-distance order: press W, then D repeatedly. Because warping
toward a nav makes it the new nearest, the natural sweep is **W -> warp the
nearest unvisited -> repeat**. These keys DO land in the WINE client (confirmed
live; unlike `Q`/`M`, they are accepted) -- `enum_fast.py` / `drive.py` activate
the window with `xdotool windowactivate --sync` and send them with `xdotool key`.

## Hard rules (read first)

- **Prerequisite: already in space.** Run the `login-to-client` skill until it
  prints `INGAME inspace=true state=space`. This skill does not log in.
- **Warp is a CLICK on the warp orb, NEVER the Q key.** Engage warp by clicking the
  round warp orb at the bottom-centre of the HUD (window-rel `(633,868)`; `warp.sh`
  does this + verifies engagement). Do NOT press `Q`: keystrokes need keyboard focus,
  which the WINE window usually does not hold under XTEST, so `Q` is silently dropped
  and the ship just sits there looking like the warp "didn't take". The map opens by
  CLICKING the 3rd bottom-left round HUD button `(132,870)`, not the `M` key, for the
  same reason. (Mouse clicks are XTEST at absolute root coords and do NOT need focus.)
- **Nav selection is the W/D/C keys, not the map.** `drive.py` batch-enumerates the
  nav cycle with `enum_fast.py` (W-seed then a D-walk, one Xlib grab + one tesseract
  montage pass over the whole cycle), picks the nearest unvisited nav, re-selects it
  by NAME (W then D to it -- never by raw count), and warps. The map is NO LONGER the
  primary selector; it is the demoted SEVERELY-STUCK fallback only (see below).
- **ALWAYS BE WARPING.** The ship should be in warp essentially all the time -- the
  only acceptable dead time is the few seconds to pick the next target plus the ~5s
  warp engage. After every arrival, immediately confirm you are warping (green speed
  readout left of the orb is a 3-digit number > `000`); if it reads `000`, pick the
  next target and GO -- do not sit. `drive.py` enforces this: it warps to the FIRST
  fresh nav the cycle lands on, and it logs a `warp-gap <secs>` line for the time from
  speed-`000` to the next warp press. **Target gap is < 10s**; gaps over 10s print
  `<<SLOW >10s` so they can be tuned. Engage-confirm waits (~5s, `ENB_WARP_ENGAGE`)
  are part of warping, not dead time -- the gap is measured up to the orb click.
- **Warp needs a target > ~2k away.** Warp will not engage toward anything closer
  than ~2k (there is nothing to warp to). If a warp won't engage, the selected target
  is probably too close (a common trap near a planet, where the map markers are mostly
  Gas Cloud junk sitting <2k away) -- select a real far nav instead.
- **Warp cooldown >= 5s.** After a warp completes, wait at least 5 seconds before
  the next warp or the command is dropped.
- **Map navigation is the DEMOTED SEVERELY-STUCK FALLBACK only.** The W/D/C nav-key
  cycle is the primary selector and self-bootstraps an empty pocket, so a map click is
  no longer part of the normal loop. Reach for the map ONLY when the cycle is genuinely
  dry AND shown navs remain unreached (`drive.py`'s `map_click_reach`, env
  `ENB_MAP_FALLBACK=1`) -- or by hand when a run is wedged. When you do: you pick a node
  on the map (mouse click), click the warp orb, the ship flies there. The only real
  gotcha is the > ~2k warp floor (see above): a map click on a Gas Cloud sitting <2k
  away just won't warp, which looks like a bug but is not.
- **There IS a top-down sector map (fallback use) -- open it, MAXIMIZE it, then zoom
  it OUT.** (`M` is not bound; the scroll wheel is a red herring -- over the map it
  only moves the 3D camera.) Exact owner-given coords (already converted from the
  title-bar-inclusive values: content-relative Y = owner_Y - 32; `open-map.sh`
  runs this whole sequence):
  1. **Open**: click the **3rd bottom-left HUD button** (interlinked-nodes icon)
     at window ~`(132, 870)`.
  2. **Maximize FIRST (mandatory)**: left-click the **"+" button at `(455, 431)`**.
     This makes the map large and EVERY coord below assumes it. If already
     maximized the "+" is absent and the click is a harmless no-op, so always do it.
  3. **Center**: left-click the "center" toolbar button at **`(84, 238)`** (tooltip
     "center camera on your ship").
  4. **Zoom out**: right-click that SAME button `(84, 238)` ~8-12 times (tooltip
     "zoom out").
  The maximized map fills window-rel **`x22..760, y222..786`**; crop THERE. Node
  icons: **gray pentagon** = nav station, **gray "?"** = unidentified nav,
  **green "?"** = nearby/discovered nav, **green square** = your current location;
  some nodes carry a coloured name label and the current target shows as
  "Dest: <node>" at the bottom. After clicking a node, **OCR the 580x20 box at
  window-rel `(105, 758)`** (owner `(105, 790)` minus the 32px title bar) to read
  the selected nav's name consistently. Everything is screen-read + mouse only --
  see the warp rule below (warp is a CLICK on the orb, never the Q key). Verify
  these coords on first use (vanilla HUD differs from the modded HUD).
- **SCREEN-READ ONLY -- no mods / no injection.** Do the whole survey with
  screenshots + XTEST mouse/keys. Do NOT use the enbmod cmd channel, memory
  reads, calibration, or `enb.on_tick` probes to drive or read navigation.
  (Calibration/autocalibrate froze a live client off the message pump -- see the
  no-debug-instrumentation memory.) The owner plays on this client; keep it to
  pixels and input.
- **Completion is OUR record, not the game's.** A sector is DONE only when our
  ledger shows every node visited (or skipped as unreachable) and the action log
  records it. A player having explored it before (so the game shows it explored)
  does NOT count -- if we did not record it, it is not complete.
- **Shield watch while stopped (danger rule).** Whenever the ship is NOT warping,
  check the shield every 5s with `shield.sh` (the long horizontal **blue** bar at
  the bottom of the HUD, right of centre, to the right of the red hull bar). If the
  shield drops sharply or is ~0, you are under fire -> immediately select a node a
  few navs away and warp to safety. Do NOT shield-check while warping (moving is
  safe). NOTE: the HUD shield *tooltip* does NOT render under synthetic XTEST hover
  in WINE, so we do not hover/OCR it -- `shield.sh` measures the blue bar's fill
  width directly (shrinks from the right as shield drops, empty at 0).
  **Read the shield BEST-OF-N (take the MAX of ~5 reads), never a single read.** The
  translucent bar washes out against bright gas-cloud / nebula backgrounds and reads
  artificially LOW -- a healthy 90% shield returns `0.0` on individual frames
  (observed `0,0,87,94,89` across 5 reads). The washout is ONE-DIRECTIONAL (it can
  only drop a reading, never invent a high one), so the MAX over a burst is the
  reliable estimate; a genuinely downed shield cannot fabricate a 90% sample. A
  single re-read is NOT enough (it can return `0.0` twice in a row) -- that false
  flee-looped a whole run to a standstill in a gas field. `drive.py` does all this
  automatically (`read_shield_robust` -> `shield_guard`/`flee`).
- **The NAV CYCLE (W/D/C) is clean; the MAP MARKERS near a planet are not.** The W/D
  nav cycle lists the real navs in distance order from any normal position -- it filters
  to NAVS, so the gas-cloud junk never enters it. The pollution problem is specifically
  the MAP MARKERS (`detect-nodes.py`), which is exactly why the map is only the last-
  resort fallback: ~56 "Gas Cloud" junk objects flood the map near a planet. So when you
  are buried in a gas-cloud pocket, stay on the nav cycle; do not drop to the map unless
  the cycle is truly dry.
- **Never relocate / fly to a planet to "extend scanner range."** Planets sit way out
  at the sector edge (Proxima reads 109k-228k in the AdrielPrime cycle) with minimal
  navs around them -- relocating to one is a long flight to a barren spot. When you
  need to cross the sector to bring a different cluster into range, pick the farthest
  NON-planet nav. `drive.py relocate_far` excludes `type == "planet"`.
- **THE MAP IS THE COMPLETION ORACLE -- once every nav DRAWN on the map is visited,
  the sector is DONE; move on (owner, 2026-06-21: "look at the map; once you've hit all
  nodes on the map, fuck off to the next sector -- bail faster for legit unfindable
  navs").** When the cycle goes dry, `drive.py` opens the map and identifies every
  marker: if an unvisited nav is drawn, it chases it via map-click; if NO unvisited nav
  is drawn, it changes pocket once (bounded by `RELOC_MAX`, since a far nav may draw
  after moving); when the map is still empty and the relocation budget is spent, the
  remainder is a SCANNER DEAD-ZONE nav (too far from any other nav to ever enter ~22k
  of a reachable pocket -- proven on AdrielPrime: 5 navs 39-88k from their nearest
  visited neighbour). Those are legit unfindable in screen-only mode; skip them and
  move on. Do NOT grind relocations for navs that are not on the map.
- **Visited everything in scanner range -> the sector is DONE -- but confirm it is
  truly out of range, not just out of THIS pocket.** When every nav the target panel
  can cycle to is visited, the remainder is a hidden nav that never revealed or a
  shown nav that never came into range -- mark each `state.py skip <S> "<name>"` and
  complete. TWO caveats learned the hard way:
  - Do **not** refly already-visited navs hoping a hidden one pops: reflying never
    reveals more, it just burns time (a sector legitimately takes ~20-30 min with
    real warp/flight times; the old reflying loop spun runs far past that to ~50 min+).
  - But if a sweep finds nothing fresh in range while SHOWN navs are still unvisited,
    you may be parked in a pocket (the gas-cloud field near a planet) where those navs
    are simply out of range -- that is NOT proof the sector is done. Fly to the
    farthest non-planet nav to change what is in range, then retry (bounded). The old
    code skipped every shown nav on the first such sweep and **false-completed
    AdrielPrime at 2/23 while 21 reachable navs sat unvisited.** `drive.py` handles
    both: it relocates up to `RELOC_MAX` times (the budget resets whenever a
    relocation brings in a new nav, so it can never loop forever) before conceding the
    remainder is unreachable, then skips it. Always output the per-sector skipped list
    (`state.py skipped <S>` / it prints in `status`).
  - **Map-click fallback before skipping shown navs.** When relocate is exhausted but
    SHOWN navs are still unvisited, `drive.py` tries `map_click_reach` (env
    `ENB_MAP_FALLBACK=1`, default on) BEFORE skipping them. Each round it opens the
    map, detects every marker, and clicks each to OCR its `Dest:` name (PB-22: a nav
    is only drawn once it is in scanner range, so this is exactly the set the target
    cycle could not reach). If an unvisited nav is drawn now, it warps straight to it;
    otherwise it REPOSITIONS -- warps to the visible marker nearest (in world coords)
    a remaining nav, dragging a fresh slice of the sector into range -- then re-surveys
    (chain-walk). Bounded by `ENB_MAP_ROUNDS` and an `ENB_MAP_REPO_DRY` dry-streak.
    The dry-streak counts MARKER DISCOVERY, not visited-count: a reposition's payoff
    is delayed (warping to a marker drags a fresh slice into range; the VISIT lands a
    round or two later via the direct-marker branch), so it concedes only when a
    reposition reveals NO previously-unseen marker -- gating on visited-count made it
    concede a round too early and quit mid-walk.
    Verified live on AdrielPrime: the direct-marker path reached Radiation Marker 2,
    which the cycle + relocate method had skipped. Markers sitting OVER the planet's
    red glow render warm-white (not cyan) and MERGE into each other along the bright
    rim, so `detect-nodes` has a dedicated `glow` mask and `identify_markers` cracks
    each glow blob with an offset probe comb (clicking a spot selects the nearest nav)
    -- this recovers planet-hugging navs like Gas Collector Ring 36 that a single blob
    centroid hides. KNOWN LIMIT -- SCANNER DEAD-ZONE navs: a nav too far from every
    other nav to ever enter scanner range from a reachable park point cannot be reached
    by EITHER method. A nav is only selectable (cycle) or drawn (map marker) within
    ~22k; a map-click can then warp to a drawn marker across long range (~85k seen). But
    if a nav sits, say, 39-88k from its nearest visited neighbour (measured live on
    AdrielPrime: Radiation Marker 1/3, Proxima Approach 3, Gas Collector Ring 5, Adriel
    Collector 36), you can never park within ~22k of it, so it never draws a marker to
    click and never enters the cycle -- it only flickers selectable mid-flight as you
    pass within ~22k. Reaching those would need a NEW capability (opportunistic in-flight
    retargeting, or free-flight to empty coordinates), which the screen-only nav stack
    does not have. Such navs are skipped with reason `scanner dead-zone: <d>k from
    nearest reachable nav`. The fallback is strictly better than skipping shown navs,
    not a guarantee of 100% in a sparse sector.
- **Refuse Gravity Well sectors.** A gravity well terminates warp mid-flight
  (server `PlayerClass.cpp:1806`) and slows the ship, so a screen-only
  warp-to-the-next-nav run drifts off course and gets wrecked. `drive.py` refuses
  any sector in `gravity_wells.GRAVITY_WELL_SECTORS` and exits 3. The list is
  derived from game logic -- `sector_objects.type == 41` (OT_GWELL,
  `SectorContentSQL.cpp:378`). Refused explorable sectors: **ABB, ABG, Achenar,
  AragothPrime, BlackbeardsWake, Lagarto, MarsGamma, Menorb, Paramis**. Check any
  sector with `python3 scripts/gravity_wells.py <S>`. (Override only for a
  deliberate test with `ENB_ALLOW_GRAVITY_WELL=1`.)
- **Don't move/resize the WINE window** (breaks click-coordinate translation).

## The scripts

| script | what it does |
|---|---|
| `scripts/enum_fast.py [--count N] [--sector S]` | **PRIMARY nav reader.** Drives the W/D nav-key cycle in ONE process holding ONE X connection: W-seed then a D-walk, grabbing only the name box via Xlib XGetImage (~0.6ms each), then OCRs every frame in a SINGLE tesseract montage pass. Prints `<rank>\t<raw>\t<canon>\t<ratio>` per slot in increasing-distance order. `drive.py` wraps it (`enum_navs`) and dedups by canonical name. |
| `scripts/open-map.sh [out]` | (FALLBACK) ensure the sector map is open (toggle only if closed), click "+" to maximize, center on ship, zoom all the way out, screenshot, and crop to the large map body. Prints `FULL <png>` and `MAP <crop> <X0> <Y0>` so `winx = X0 + cropx`. Used only by the severely-stuck map fallback. |
| `scripts/detect-nodes.py <full.png> [l t w h]` | find the nav-node markers on the map body and print `NODE <wx> <wy> <colour> <pixels>` centroids to click. An AID -- validate each by clicking + `read-navname.sh` and cross-ref `navdata.py list`. |
| `scripts/read-navname.sh [out]` | after clicking a node, OCR the bottom "Dest: <nav>" strip (tesseract) and print `NAVNAME <name>` (prefix stripped). The reliable selected-nav read. |
| `scripts/map-shot.sh [out]` | screenshot + crop WITHOUT toggling/zooming the map. Prints `FULL <png>` and crop origin `<X0> <Y0>`. Use for a quick re-shot when the map is already set up. |
| `scripts/click-map.sh <wx> <wy>` | left-click at window-relative coords (same frame as a full screenshot). Used to select a node on the map, or click the gate icon. |
| `scripts/warp.sh` | **CLICK the round warp orb** at the bottom-centre of the HUD (window-rel `(633,868)`, `ENB_WARP_BTN`) to warp to the selected nav, then verify it engaged by reading the green speed readout. Prints `WARP <speed>` (>0 == warping, 0 == never engaged). **NOT the Q key** -- Q is silently dropped whenever the WINE window lacks keyboard focus (it usually does, under XTEST). |
| `scripts/read-speed.sh` | read the green warp-speed readout (3 digits left of the warp orb, box `578 618 852 874`): `SPEED <n>` (`000` == stopped) or `SPEED ?`. A coarse engaged/stopped check only -- the readout misreads `000` mid-warp, so for ground-truth "am I moving" trust the TARGET distance CLOSING (`read-target.sh`), not this. |
| `scripts/drive.py <S> [--max-rounds N]` | autonomous per-sector driver (Always-Be-Warping): batch-enumerate the nav cycle via `enum_fast.py` (`enum_navs`), pick the nearest unvisited nav, re-select it by NAME with W/D (`select_named`, never by raw count), warp (CLICK the orb), record arrival + en-route hits; logs a `warp-gap` per warp (000 -> next warp, target <10s). When the cycle is dry but shown navs remain, relocates to a far NON-planet nav (bounded), then tries the map fallback, before skipping the unreachable/hidden leftovers and completing. Best-of-N shield guard while stopped. Resumable via the ledger. Nav-key env: `ENB_NAV_SEED=w`, `ENB_NAV_NEXT=d`, `ENB_NAV_PREV=c`, `ENB_ENUM_SETTLE=0.13`. |
| `scripts/read-target.sh [shot]` | OCR the bottom-RIGHT selected-target panel: `TARGET <name>` + `DIST <k>` (k == JSON-coord distance; `<=2.0k` == visited). Used to verify the W/D selection landed + read its distance. |
| `scripts/shield.sh [shot]` | read shield level: `SHIELD <pct>` from the blue HUD bar fill (no hover). |
| `scripts/gate.sh` | click the use-gate icon (needs `ENB_GATE_ICON="x y"`, calibrated once). |
| `scripts/navdata.py` | `sectors` / `list <S>` / `identify <label>...` / `match <S> <ocr>` -- authoritative node lists from `docs/sectors/json/<S>.jsonl`. |
| `scripts/state.py` | the per-sector progress ledger (`init/status/remaining/suggest/reveal/visit/skip/skipped/danger-add/danger-list/complete/summary`). |
| `scripts/gravity_wells.py [S]` | authoritative gravity-well sector blocklist (game logic: `sector_objects.type==41`). No arg lists all; `<S>` prints whether that sector is refused. `drive.py` imports it to refuse. |
| `scripts/wreck.sh [shot]` | detect a death panel (`WRECKED <winx> <winy>` for the Request Tow button, else `ALIVE`) via OCR; `ENB_TOW_BTN="x y"` pins the button once a real death calibrates it. |
| `scripts/undock.sh` | launch the ship back into space when DOCKED at a station (prints `UNDOCKED`/`IN-SPACE`/`FAILED`). See "Undocking from a station" below. `handle_wreck` calls it after a Request-Tow drops us in a bay. |
| `scripts/logaction.sh <S> <action> [detail]` | append a timestamped line to the gitignored action log. |

Runtime output (ledgers + `actions.log`) lives in `state/` and is **gitignored**
-- it is per-playthrough progress, not source.

## The normal path: just run `drive.py`

For a sector that is already identified, the whole sweep is autonomous:

```bash
S=.claude/skills/explore-sector/scripts
python3 $S/state.py init <Sector>
python3 $S/drive.py <Sector>
```

`drive.py` batch-enumerates the nav cycle (W-seed + D-walk via `enum_fast.py`),
picks the nearest unvisited nav, re-selects it by name with W/D, warps, records
arrivals + en-route hits, runs the shield guard while stopped, relocates to extend
range when the cycle goes dry, falls back to the map only when severely stuck, and
completes when every in-range nav is visited (skipping proven dead-zone navs). The
sections below are the MANUAL/fallback breakdown of what it does -- use them when
you need to step in by hand (a wedged run, a gate route, an unidentified sector).

## Per-sector loop (manual / fallback breakdown)

### 1. Identify the sector and open the ledger

Take a screenshot of the 3D view (`map-shot.sh` just shots+crops; it does NOT
press any key). Read the sector name from the in-space HUD / the nav labels you
hover (step 2), then seed the ledger:

```bash
S=.claude/skills/explore-sector/scripts
python3 $S/navdata.py identify "GETCo Sector Headquarters" "GDI Mandela"  # nav labels -> sector
python3 $S/state.py init Equatorial                 # create/refresh ledger
bash $S/logaction.sh Equatorial sector-enter "ledger seeded"
```

`navdata.py identify` takes nav labels you read off the screen (hover tooltips,
the target panel) and returns the sector whose node set matches best.

### 2a. Open the sector map and read ALL in-range nodes (the completeness view)

Run `open-map.sh` -- it opens (3rd bottom-left button ~`(132,870)`, only if
closed), **maximizes** (left-click "+" at `(455,431)`, always-done, no-op if
already large), **centers** (left-click `(84,232)`), **zooms out** (right-click
`(84,232)` ~12x), and prints the cropped map png (window-rel `x22..760,
y222..786`). The legend: gray pentagons (stations), gray/green "?" (unidentified /
nearby navs), the green-square current-location marker, coloured name labels; the
3D world/planet shows THROUGH the semi-transparent overlay.

Then locate + identify each node with the validated pipeline:

```bash
S=.claude/skills/explore-sector/scripts
read -r _ FULL <<< "$(bash $S/open-map.sh | grep '^FULL')"   # FULL=<shot>
python3 $S/detect-nodes.py "$FULL"          # -> NODE <wx> <wy> <colour> <px> lines
# for a candidate (wx,wy): select it, SETTLE, read the Dest line, snap to a real name
bash $S/click-map.sh <wx> <wy>; sleep 0.6   # <-- 0.6s settle is MANDATORY (see below)
NAME=$(bash $S/read-navname.sh | sed -n 's/^NAVNAME //p')    # OCR of "Dest: <nav>"
python3 $S/navdata.py match Equatorial "$NAME"               # OCR -> authoritative name (+ratio)
```

**Settle + miss detection (learned the hard way).** After `click-map.sh` you MUST
`sleep 0.6` before `read-navname.sh`: the Dest label redraw lags the click, and a
0.3s settle reads the PREVIOUS frame, so every node looks like the last one you
selected. Worse, **a click that misses the tiny icon does NOT clear Dest** -- it
stays on the previously-selected node. So the only way to tell a hit from a miss is
to **diff the Dest name before vs after the click**: unchanged Dest == you missed
(clicked empty transparent map), so DISCARD that candidate, do not record it.
`detect-nodes.py` over-produces (it flags label-text fragments and the 3D-hull
bleed as candidates); the before/after-Dest diff is what filters them. In practice
~3/4 of detected candidates resolve to distinct real nodes and the rest are misses.

`detect-nodes.py` is an AID (it catches the clear coloured markers; dim gray
pentagons can slip past), so the AUTHORITATIVE completeness check is: click every
candidate, diff+OCR its Dest name, `navdata.py match` it to a known node, and
reconcile against `navdata.py list <S>` (Equatorial = 25). A low match ratio or a
name not in the list means you clicked empty map / a non-nav -- skip it.

**One map view only shows the in-range CLUSTER.** Even fully zoomed out, the map is
centred on the ship and only the nearby nodes render; sector-edge `shown` nodes and
all `hidden-` nodes will NOT be on the first view. To reach the rest: fly toward the
unexplored edges (warp to a known far node) to bring new nodes into range, then
re-run `open-map.sh` and re-sweep. The sector is done when every nav in scanner
range has been `visit`ed; any `navdata.py list` entry that never came into range
(hidden navs that never revealed, far shown navs that never popped) is `skip`ped --
do NOT refly visited navs to coax a hidden one out, it never reveals more.

Record + log each confirmed node (note: `logaction.sh` takes the SECTOR first):

```bash
python3 $S/state.py visit Equatorial "<authoritative name>"
bash $S/logaction.sh Equatorial map-identify "<name> (map click+OCR)"
```

### 2b. Read the beacons in the 3D view (hover to identify)

Nav nodes also render as beacons in the 3D world:
- **green** = within range / discovered,
- **grey/yellow "?"** = seen but not yet identified (in scanner range, name not
  resolved until you hover or select it),
- nodes you have NOT been near yet are **absent** until you fly close (PB-22:
  the server only sends a node's create once it is within scanner range).

The reliable, screen-only way to identify a beacon is to **hover** it: move the
mouse onto the beacon (NO click) and screenshot -- a tooltip prints the node's
**name + distance** (e.g. `GETCo Sector Headquarters  161.30k`). Use this to know
which node a "?" is before committing. Then **left-click** the beacon to select it
-- the **bottom-right target panel** updates to that node's name (this is the
confirmation the select landed; the `Dist` readout under the panel is its range).

> PRECISION + AIM. Beacons are tiny; a few px off misses. Read the beacon's pixel
> coords from a tight, BRIGHTENED, gridded crop (overlay labelled gridlines so you
> read true window coords, not eyeballed math off an upscale). Then HOVER-VERIFY
> before clicking: `mousemove` to the coord, screenshot, confirm (a) the drawn
> cursor tip sits on the beacon and (b) the hover tooltip names the node you want.
> Only then click. If the target panel does not change after a click, you missed
> -- re-read coords and retry; do not assume it worked. (`xdotool getmouselocation`
> confirms the OS cursor is where you commanded; any residual gap is your
> coordinate read, so close the loop visually.)

### 3. Select a far node and warp

Warping node-by-node is slow. Prefer a **far** visible node so the warp carries
you across the sector, revealing other beacons in range along the way:

1. Pick the farthest visible unvisited beacon (helper:
   `state.py suggest <S> <curx> <cury>` ranks unvisited nodes farthest-first;
   the hover tooltip's distance also tells you which is far).
2. Hover-verify, then left-click it; confirm the bottom-right panel shows it.
3. `warp.sh` (CLICKS the warp orb at `(633,868)`, NOT Q), then
   `logaction.sh <S> warp "<from> -> <node>, dist <d>"`.
4. Capture frames every ~3s through the warp tunnel until you arrive (the station/
   node fills the view and `Dist` drops to ~0); note any new green beacons that
   appeared in range during the flight. Movement ground-truth is the target `Dist`
   CLOSING, not the green speed readout (which can misread `000` mid-warp).

Mark the node you arrived at as visited:

```bash
python3 $S/state.py visit Equatorial "GETCo Sector Headquarters"
bash $S/logaction.sh Equatorial warp "GDI Mandela -> GETCo Sector Headquarters"
```

### 4. Hidden / missing nodes -- reveal what you can, skip the rest

`state.py remaining <S>` flags nodes still unvisited; `state.py status <S>` flags
hidden ones (`HIDDEN/unrevealed`). A hidden node only appears (as "?") once you fly
within scanner range of it -- so en route to the shown navs you naturally reveal
the hidden ones whose neighbourhood you pass through:

1. From the ledger, find the unvisited node nearest your position / path.
2. Warp toward a known nearby node so you pass within range.
3. Screenshot repeatedly until the new "?" beacon appears; identify it.
4. `state.py reveal <S> "<name>"`, then select it and warp as above.

But once every nav in scanner range is visited, **stop**. Hidden navs that did not
reveal -- and shown navs that never came into range -- are `skip`ped, not chased.
Reflying already-visited navs hoping a hidden one pops does NOT work and is what
made runs overrun (a normal sector is ~20-30 min of real warp/flight time; the
reflying loop blew that out to ~50 min+). `drive.py` enforces this automatically.

### 5. Close out the sector

```bash
python3 $S/state.py complete Equatorial     # succeeds only if all nodes visited
bash $S/logaction.sh Equatorial complete "all N nodes visited + logged"
```

`complete` refuses (exit 1) and lists what is left if any node is unvisited -- it
enforces the "every node, recorded by us" rule.

## Undocking from a station (after a tow, or any docked start)

A Request Tow drops the ship DOCKED in a station bay, and from there the whole
sweep wedges (no warp cluster, every warp/cycle no-ops). `handle_wreck` now runs
`undock.sh` automatically after a tow; run it by hand any time you find yourself
docked. The owner-taught, live-validated method (2026-06-21):

- **Tell you are docked:** the bottom-centre warp cluster (warp dial + speed
  digits + the green/red/blue bars + the 1..6 action circles) is HIDDEN while
  docked. Cheapest check: OCR the warp-speed region -- blank (not even "000") ==
  docked; any chars == in space.
- **Station layout:** you zone into a T-shaped hangar at one of 3 random catwalk
  spots. The EXIT DOOR is top-middle of the T (DO NOT go there). Your SHIP is
  always parked at the bottom-right. Turn with the Left/Right ARROW KEYS (sent to
  the activated window -- the one sanctioned keyboard use; warp etc. stay clicks).
- **Find + launch:** rotate RIGHT in batches of ~10 until the ship shows on the
  RIGHT HALF of the screen, clear of the chat overlay -- it is unmistakable: hull
  flanked by a GREEN and a RED nav light with a bright cyan/white engine plume.
  Overshoot (it slides off left) -> nudge LEFT; fine steps of 5. Click the HULL
  CENTRE (between the nav lights, above the plume) -- NOT the engine glow, NOT the
  chat box. Re-check warp-speed OCR; digits back == in space == undocked.
- A tow can also cross sectors (it tows to the home station). After undocking,
  re-identify the sector from its navs and gate back to the target sector if the
  tow moved you.

## Crossing to a new sector (gates)

To survey every sector, travel between them via **gates** (themselves nodes of
type `gate`/`system-gate`/etc. in the ledger):

1. Warp to the target gate like any node; `logaction.sh <S> select-gate "<gate>"`.
2. At the gate, the **use-gate icon appears near the bottom-right of the HUD**.
   Click it (`gate.sh` with `ENB_GATE_ICON` calibrated, see below);
   `logaction.sh <S> enter-gate "<gate>"`.
   **If NO use-gate icon appears when you are on the gate, you CANNOT use that
   gate** (it is class-/faction-locked for this character) -- do not keep trying
   it; pick a different gate out of the sector (see "Locked gates -> reroute").
3. A load screen follows. Wait for it by screenshotting until the 3D view returns
   (the load screen clears and beacons/HUD reappear); read the new sector from the
   nav labels you hover. `logaction.sh <oldS> load-screen "entering <newS>"`.
4. In the new sector, restart at step 1 of the per-sector loop (identify, init,
   explore).

### Locked gates -> reroute

Some gates are **class- or reputation-locked** (`class-specific-gate`,
`factioned-gate`, `disabled-gate` in the data; in-game the use-gate icon is
absent or greyed). **The test is operational: fly onto the gate and look for the
use-gate icon. No icon == you cannot use this gate -- reroute, do not retry it.**
If you cannot use a gate:

- `logaction.sh <S> gate-locked "<gate> -- class/faction locked, rerouting"`.
- Pick a different gate out of this sector toward the goal sector. Use the gate
  graph: every sector's gates name their destination
  (`navdata.py list <S>` -> the `gate` rows like "Sector Gate to Earth"). Plan a
  detour through an intermediate sector whose gates you CAN use.
- Never assume a route; confirm each hop's gate is usable before committing.

## First-run calibration (do once per resolution)

The client renders a fixed 1280x960, so these are stable once set, but VERIFY
them on first use rather than trusting a guess:

- **Map button + controls** (owner-given exact coords, content-relative):
  open-map button (3rd bottom-left HUD button) at ~`(132, 870)`; **maximize "+"
  button at `(455, 431)`** (left-click FIRST, always); the map "center" toolbar
  button at `(84, 238)` (left-click = center on ship, right-click = zoom out a
  step). The maximized map fills window-rel `x22..760, y222..786`, so crop THERE.
- **Selected-nav name (OCR)**: after clicking a node, OCR the **580x20 box at
  window-rel `(105, 758)`** (owner `(105, 790)` minus the 32px title bar) for the
  nav's name -- this is the consistent read, more reliable than the 3D tooltip.
- **Target panel crop**: the selected node's name also shows in the bottom-right
  HUD panel around window `x 1040..1245, y 685..760`; the `Dist` readout sits just
  below it (`x 1180..1280, y 790..815`). Confirm yours and read both off every
  shot after a select.
- **Gate icon** (`ENB_GATE_ICON="x y"`). At a gate, screenshot the bottom-right
  action cluster, read the use-gate icon's window coords. `gate.sh` refuses to
  guess.
- **Click aim.** `click-map.sh` uses the login skill's XTEST click (window
  absolute via `xwininfo`), proven correct for screenshot-relative coords. If a
  click lands wrong, screenshot, move the mouse to a known coord, and confirm
  where the game draws the cursor before proceeding (a WM frame offset can creep
  in).

## Gotchas

- "no client window" / "no client dir" -> the game is not running; run
  `login-to-client` first. This skill cannot launch the GUI.
- The beacon `?`/green colour is the per-player discovery state (PB-22); it is the
  signal for which nodes still need a visit, so trust it over assuming.
- Do NOT reach for the enbmod cmd channel / memory reads for position or nav state
  -- this skill is screen-read + input only (see Hard rules). The 3D view + hover
  tooltips + target panel ARE the source of truth for where you are and what is
  selected.
- Keep `actions.log` append-only; it is the audit trail the owner asked for.

## Related skills

- `login-to-client` -- get in-game first (this skill reuses its `lib.sh`).
- `take-client-screenshot` / `run-lua-client-command` -- the lower-level shot +
  cmd-channel helpers.
