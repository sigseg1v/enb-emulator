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

This is an AGENT-DRIVEN visual loop. The scripts do the mechanics (open the map,
screenshot+crop, click a window coordinate, press warp, click the gate icon,
update the ledger, append to the log). YOU look at the map screenshots, decide
which marker to click, and verify movement -- the scripts cannot see the map for
you.

## Hard rules (read first)

- **Prerequisite: already in space.** Run the `login-to-client` skill until it
  prints `INGAME inspace=true state=space`. This skill does not log in.
- **Warp cooldown >= 5s.** After a warp completes, wait at least 5 seconds before
  the next warp or the command is dropped.
- **There is NO top-down map in this build.** `M` and the toolbar `Map` button do
  nothing (the Galaxy Map renders empty). Navigation is done entirely in the **3D
  in-space view**: nav beacons render in the world as icons (a grey/yellow "?"
  when not yet identified, green when discovered/in-range), and the bottom-right
  HUD panel shows the currently-selected target's name + a `Dist` readout. This
  is what "the map" means here -- the beacons that appear on screen when you fly
  close. Everything is screen-read (crop + read) + mouse + the `Q` warp key.
- **SCREEN-READ ONLY -- no mods / no injection.** Do the whole survey with
  screenshots + XTEST mouse/keys. Do NOT use the enbmod cmd channel, memory
  reads, calibration, or `enb.on_tick` probes to drive or read navigation.
  (Calibration/autocalibrate froze a live client off the message pump -- see the
  no-debug-instrumentation memory.) The owner plays on this client; keep it to
  pixels and input.
- **Completion is OUR record, not the game's.** A sector is DONE only when our
  ledger shows every node visited and the action log records us visiting them. A
  player having explored it before (so the game shows it explored) does NOT count
  -- if we did not record it, it is not complete.
- **Don't move/resize the WINE window** (breaks click-coordinate translation).

## The scripts

| script | what it does |
|---|---|
| `scripts/map-shot.sh [out]` | screenshot the 3D view + crop. Prints `FULL <png>` and the crop origin `<X0> <Y0>` so `winx = X0 + cropx`. Use for every shot. (`open-map.sh` is a deprecated alias -- this build has no map to open.) |
| `scripts/click-map.sh <wx> <wy>` | left-click at window-relative coords (same frame as a full screenshot). Used to select a nav beacon, or click the gate icon. Hover-verify first (see precision note). |
| `scripts/warp.sh` | press **Q** to warp to the selected nav. |
| `scripts/gate.sh` | click the use-gate icon (needs `ENB_GATE_ICON="x y"`, calibrated once). |
| `scripts/navdata.py` | `sectors` / `list <S>` / `identify <label>...` -- the authoritative node lists from `docs/sectors/json/<S>.jsonl`. |
| `scripts/state.py` | the per-sector progress ledger (`init/status/remaining/suggest/reveal/visit/complete/summary`). |
| `scripts/logaction.sh <S> <action> [detail]` | append a timestamped line to the gitignored action log. |

Runtime output (ledgers + `actions.log`) lives in `state/` and is **gitignored**
-- it is per-playthrough progress, not source.

## Per-sector loop

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

### 2. Read the beacons in the 3D view (hover to identify)

Nav nodes render as beacons in the 3D world:
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
3. `warp.sh` (presses **Q**), then
   `logaction.sh <S> warp "<from> -> <node>, dist <d>"`.
4. Capture frames every ~3s through the warp tunnel until you arrive (the station/
   node fills the view and `Dist` drops to ~0); note any new green beacons that
   appeared in range during the flight.

Mark the node you arrived at as visited:

```bash
python3 $S/state.py visit Equatorial "GETCo Sector Headquarters"
bash $S/logaction.sh Equatorial warp "GDI Mandela -> GETCo Sector Headquarters"
```

### 4. Discover the hidden / missing nodes

`state.py remaining <S>` flags nodes still unvisited; `state.py status <S>` flags
hidden ones (`HIDDEN/unrevealed`). A hidden node only appears (as "?") once you
fly within scanner range of it. To get the ones not yet on screen:

1. From the ledger, find the unvisited node nearest your position / path.
2. Warp toward a known nearby node so you pass within range.
3. Screenshot repeatedly until the new "?" beacon appears; hover to identify it.
4. `state.py reveal <S> "<name>"`, then select it and warp as above.

Repeat until `state.py remaining <S>` is empty.

### 5. Close out the sector

```bash
python3 $S/state.py complete Equatorial     # succeeds only if all nodes visited
bash $S/logaction.sh Equatorial complete "all N nodes visited + logged"
```

`complete` refuses (exit 1) and lists what is left if any node is unvisited -- it
enforces the "every node, recorded by us" rule.

## Crossing to a new sector (gates)

To survey every sector, travel between them via **gates** (themselves nodes of
type `gate`/`system-gate`/etc. in the ledger):

1. Warp to the target gate like any node; `logaction.sh <S> select-gate "<gate>"`.
2. At the gate, the **use-gate icon appears near the bottom-right of the HUD**.
   Click it (`gate.sh` with `ENB_GATE_ICON` calibrated, see below);
   `logaction.sh <S> enter-gate "<gate>"`.
3. A load screen follows. Wait for it by screenshotting until the 3D view returns
   (the load screen clears and beacons/HUD reappear); read the new sector from the
   nav labels you hover. `logaction.sh <oldS> load-screen "entering <newS>"`.
4. In the new sector, restart at step 1 of the per-sector loop (identify, init,
   explore).

### Locked gates -> reroute

Some gates are **class- or reputation-locked** (`class-specific-gate`,
`factioned-gate`, `disabled-gate` in the data; in-game the use-gate icon is
absent or greyed). If you cannot use a gate:

- `logaction.sh <S> gate-locked "<gate> -- class/faction locked, rerouting"`.
- Pick a different gate out of this sector toward the goal sector. Use the gate
  graph: every sector's gates name their destination
  (`navdata.py list <S>` -> the `gate` rows like "Sector Gate to Earth"). Plan a
  detour through an intermediate sector whose gates you CAN use.
- Never assume a route; confirm each hop's gate is usable before committing.

## First-run calibration (do once per resolution)

The client renders a fixed 1280x960, so these are stable once set, but VERIFY
them on first use rather than trusting a guess:

- **Target panel crop**: the selected node's name reads from the bottom-right HUD
  panel around window `x 1040..1245, y 685..760`; the `Dist` readout sits just
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
