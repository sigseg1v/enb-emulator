# Phase AW -- Sector exploration skill (agent-driven nav survey)

- **2026-07-09 INCAP AUTO-RECOVERY WAS DEAD-ON-ARRIVAL (falsy-zero bug) -- FIXED
  + VALIDATED LIVE.** The distress->tow recovery added in the entry below NEVER
  FIRED for the exact case it exists for. `check_incapacitated`'s debounce re-read
  was `if not enb.inspace() or (hull_frac() or 1.0) > 0.0: return` -- but an
  incapacitated hull reads exactly `0.0`, and in Python `0.0 or 1.0 == 1.0` (0.0 is
  falsy), so a genuine incap was coerced to 1.0 and the guard bailed every time.
  Observed live: the LV122 ship sat incapacitated (hull 0/18000, "COMPUTER: you
  cannot do this while incapacitated") in Ishuan (sid 2005) while the driver tried
  every onward gate -- all refused by the server because incap refuses ALL actions
  -- then declared "reachable gate graph exhausted; done" and exited: a FALSE
  completion caused entirely by the unrecovered incap. Two fixes in `drive_lua.py`:
  (1) capture the hull into `h2` and test `h2 is None or h2 > 0.0` explicitly so a
  real `0.0` proceeds to recovery; (2) `tried_edges.clear()` in the `ShipRecovered`
  handler -- gates marked "tried" during incap are FALSE failures (the server
  refused everything, not that gate), and since the tow lands at the same sector's
  station and undocks back into it, without the clear it re-hits "graph exhausted"
  on its own already-tried gates. VALIDATED LIVE 2026-07-09: relaunched against the
  live incap ship; `incap-recover` -> tow (retry #1 missed a click, #2 landed) ->
  `incap-recovered` at Ishuan Station -> undock -> `enter-gate Gate to Capella
  System` (crossed OUT, hull restored, tried_edges cleared). Recovery is now
  genuinely automated end-to-end, not just when a human drives the clicks.

- **2026-07-09 FREYA NEVER-EXPLORE + AUTOMATED INCAP DISTRESS->TOW RECOVERY
  (owner-directed).** The frontier walker routed the survey ship THROUGH
  `Akerons Gate`'s "Gate to Aragoth System" into **Freya (sid 1750)** and the
  Freya pirates destroyed the LV122 ship (hull 0, stuck spinning warp-fails).
  Freya is a huge gravity well WITH pirate density so lethal the owner says even
  Brisings is unsafe within seconds -- "only pass thru, never explore". Two fixes:
  - **Never route INTO Freya, and defer the cluster behind it.** Freya has NO
    type-41 OT_GWELL object, so it does NOT belong in `gravity_wells.py` (whose
    provenance is strictly type-41 rows). Added a distinct `NEVER_EXPLORE_SECTORS
    = {"Freya"}` in `drive_lua.py`. The routing hole was that "Gate to Aragoth
    System" (Freya's SYSTEM name) resolved to no sid, so `_gate_graph` read it as
    unexplored frontier and routed straight in; `GATE_DEST_OVERRIDES = {"Aragoth
    System": 1750}` pins it. `gate_into_well` -> `gate_forbidden` (+ `_sector_
    forbidden` helper) now refuses gates into a well OR a NEVER_EXPLORE sector, and
    the `_gate_graph` frontier set excludes both. `choose_gate`/`settle_well_gates`
    take `leaving_defer` so that if the ship is ever INSIDE a deferred sector it
    exits only toward a KNOWN, non-forbidden sector (never deeper into the lethal
    Jotunheim/Ragnarok/Nifleheim/Centauri cluster, which is thereby deferred to a
    manual pass -- it is reachable only through Freya). VERIFIED OFFLINE: "Gate to
    Aragoth System" and "Gate to Freya" both -> dest 1750, forbidden=True.
  - **Auto-recover an incapacitated ship instead of halting.** `check_
    incapacitated` (called at the top of the survey loop and each hop) now, on a
    debounced in-space hull-0 read, runs the owner-taught distress->tow flow:
    `enb.freya_ui_on = false` to reveal the native distress orb our hide-ui mod
    hides (setting the flag directly is enough -- no Ctrl+U handler needed, the
    earlier failure was purely a wrong click coord on the "000" readout, not the
    orb), click the orb (win 633,858) to open the Station Mechanic comm dialog,
    click "I need a tow" MID button-bar (win 400,617 -- the left text-edge click
    did NOT register live), poll `enb.state()=="station"` for the tow, restore the
    Freya HUD, and `ensure_in_space()` to undock and resume. Raises `ShipRecovered`
    so `main()` re-detects the (now different) sector, no hop consumed. Retries the
    two clicks 3x; HALTS only if the tow never lands or the station did not repair
    the hull (avoids a tow loop). VALIDATED LIVE 2026-07-09: recovered the dead
    Starstrukk from Freya -> towed to Ishuan Station (Castor). Survey tooling only;
    no server/proxy/client change. Future option (owner hint): mine decomp for a
    distress action to add an enbmod primitive and drop the click path entirely.

- **2026-07-09 FRONTIER-DIRECTED GATE ROUTING -- stop dead-end wandering
  (commit `ec9216dc`, supersedes the `64786eb9` tier walk).** The graph traversal
  from `64786eb9` reached sectors behind completed ones but ranked gates with only
  a ONE-HOP `tier()` lookahead (fresh=0, done-not-visited-this-run=1, visited=2).
  With one hop of foresight it could not distinguish a done sector that transits
  TOWARD the frontier from a done dead-end. From the Sol cluster it walked
  `Mercury -> Venus -> Ceres` and thrashed the Venus/Ceres/Mercury triangle
  (`Ceres` only gates back to `Venus`; live actions.log showed
  Ceres->Mercury->Ceres->Venus bouncing) before backtracking to the real frontier.
  Owner flagged it: "if left mercury, went to venus, and is now flying to ceres,
  why?". Replaced `tier()` with frontier-directed BFS:
  - `_gate_graph()` builds an undirected sector graph over EVERY ledger's routable,
    non-well gates, plus the FRONTIER set (a dest that still needs surveying). A
    COMPLETED sector whose onward gate name resolves to no known sid is itself a
    dist-0 frontier source -- standing there you are one gate from undiscovered
    space (`Akerons Gate -> Aragoth`, `Saturn -> Asteroid Belt Alpha` resolve this
    way, so those onward gates were previously invisible to routing).
  - `_frontier_hops()` = multi-source BFS, min hops from each sector to any frontier.
  - `rank()`: a fresh/unresolved dest still sorts first; a transit through a DONE
    sector now sorts by how few hops the nearest frontier lies BEYOND it, then a
    revisit flag, then physical distance. A done dead-end (no reachable frontier)
    sorts last.
  VERIFIED OFFLINE against the live ledgers: from `Venus` it now picks `Mercury`
  (fhops=2) over `Ceres` (fhops=4); from `Mercury` it picks `Pluto` (fhops=1)
  toward `Akerons Gate`, escaping the Sol dead-end instead of bouncing in it.
  Relaunched live against the running client. Survey tooling only.

- **2026-07-09 REACTOR-DRAIN-ZONE UNWARPABLE SKIP (commit `5918f194`).** A nav
  named "reactor drain zone" cancels every warp ~1s after engage (the zone kills
  drive), so the picker retargeted it forever, cancelling each warp -- the ship
  never went anywhere. Fix: `UNWARPABLE_NAME_SUBSTRINGS` + `is_unwarpable()`, and
  `resolve_unwarpable_navs()` (run each loop, like `resolve_body_navs`) takes such
  navs OUT of `remaining` with a `skip` ("warp cancels ~1s after engage, cannot
  reach; flying elsewhere"), so the survey flies to a real nav instead. `pick_target`
  and the seen-tier / relocate lists all exclude it as a backstop. Survey tooling only.

- **2026-07-09 PLANET-BODY WARP-SPAM FIX (commit `6f3243af`).** The Ceres survey
  wedged ping-ponging warp between `Ceres` (d=292k) and `Thule` (d=15k): both are
  the planet BODIES, the client refuses to warp into a body (`target too close`)
  and holds the ship at the body radius, so the farthest-first picker re-selected
  them every loop and never marked either -> infinite warp spam. The existing
  body-class standoff (`arrive_slop`) keyed off the LEDGER type, but these nodes
  are typed generic `object` there, not `planet`; only the LIVE nav class from
  `enb.navs()` reports `Planet`. Fix keys off the live class: `BODY_CLASSES` +
  `is_body_class()`, and `resolve_body_navs()` (run each loop before `pick_target`)
  takes body navs OUT of `remaining` -- visits one already inside `BODY_SLOP`
  (as close as the game allows), skips a far one (cannot approach). `pick_target`
  also excludes body-class navs as a backstop. The real waypoints around a body
  (`Ceres 1..6`, ...) are separate nav-point nodes the survey still visits normally.
  VERIFIED LIVE: Ceres survey unstuck from 2/16 -- `Ceres` skipped, `Thule` visited,
  survey advanced onto the nav-points. Survey tooling only.

- **2026-07-09 NAV-DUMP PAGING + non-transit-gate exclusion + well-gate settle
  (commit `e07797eb`).** Three linked fixes surfaced by the ship getting stuck in
  the Asteroid Belt Beta (ABB, sid 1077) gravity-well sector, unable to route out.
  (1) **`get_navs` now PAGES the `enb.navs()` dump.** The enbmod cmd/log channel
  truncates each reply line at **2048 bytes**; a busy belt sector dumps ~3KB / 77
  records in one string, so everything past record ~53 was silently lost -- and the
  lost tail is the DISTANT navs, i.e. exactly the onward/exit gates `choose_gate`
  needs. Page 0 snapshots `enb.navs()` into a Lua global (globals persist across
  channel commands, verified) and each page reads a <2KB slice of that consistent
  view. ABB went from 53 navs seen (2 accelerator spheres only) to the full 77 (all
  4 real sector gates). This silently starved EVERY normal survey sector with >~53
  navs of its farthest nodes too. (2) **`routable_gate()`** restricts routing to
  names that actually cross a sector (`Gate to X` / `Sector Gate to X` / a
  `Wormhole`): `is_gate()` matches an Accelerator Sphere because its object class is
  `Stargate`, but warping to one never flips the sector -- it just wastes a warp
  and, in a well sector, sends the ship toward the well. `choose_gate` filters
  candidates through it. (3) **`settle_well_gates()`** lets a well sector's distant
  exit gates finish rendering (belt sectors render in bursts, past `settle_navs`'s
  2-stable-read cutoff) before `choose_gate` runs; clears the prior sector's
  `GATES_SEEN` first, early-exits once a routable non-well gate is in range.
  VERIFIED LIVE: ABB now well-skips and gates out via `Sector Gate to Venus`
  (nearest tier-0 fresh sector) -> hop 1 Venus [survey], instead of thrashing the
  two accelerator spheres then false-conceding "no gate out". Survey tooling only.

- **2026-07-09 GRAPH TRAVERSAL: survey now reaches sectors BEHIND completed ones
  (commit `64786eb9`).** `drive_lua.py` was a linear gate-walker: `choose_gate`
  picked only gates to a FRESH destination and `main()` hard-stopped the entire
  run the first time a crossing looped back into an already-surveyed/complete
  sector ("gated into already-surveyed sector NNNN -- stopping"). From a spawn
  boxed in by done sectors (Ishuan/Yokan) it took two internal gates, looped, and
  quit after 2 hops -- while the frontier gates (Gate to Sol, Gate to Sirius
  System) that lead to unexplored space sat untried. Replaced with a bounded graph
  traversal: `choose_gate(sector, navs, visited_sids, tried_edges, sid)` RANKS
  every reachable gate (tier 0 = fresh/unresolved dest, tier 1 = done-but-not-yet-
  entered-this-run = transit THROUGH it to reach the frontier beyond, tier 2 =
  loop-back last resort), excluding each `(sid, gid)` edge once crossed
  (`tried_edges`) so it is finite; `main()` separates SURVEY (only for a not-done,
  not-yet-surveyed sector) from TRANSIT (cross a done sector without re-surveying),
  drops the loop-back hard-stop, and retries a different gate when one fails to
  transit instead of killing the run. VERIFIED LIVE: relaunched against the live
  client, resumed Ganymede (1040) from its ledger at 11/34 with the new `[survey]`
  tag; earlier old-code run had already reached Ganymede via the frontier
  `Gate to Sol` from Ishuan, so the frontier-crossing path is exercised. Survey
  tooling only -- no wire/server behaviour touched.

- **2026-07-09 NEW `explore-live` skill: attach-aware live survey + crash recovery
  (commit `ed23dd8c`).** A second entry skill (`.claude/skills/explore-live/`,
  Freya/MIT) for surveying against a LIVE client the OWNER attached enbmod into
  (native `client.exe` + `Net7Proxy.exe`, NO Freya docker stack). It reuses the
  `drive_lua.py` survey engine unchanged and adds only the two attach-case pieces:
  (1) **path resolution for a client we did not launch** -- `client_dir()` now
  resolves the enbmod store from the RUNNING `client.exe`'s `/proc/<pid>/cwd` (via
  its X window), ahead of `settings.json` (which names OUR launcher's last prefix,
  wrong for an attached client / mbox slot); order is `ENB_CLIENT_DIR` -> /proc cwd
  -> settings.json. (2) **unattended crash recovery** -- `drive_lua` gained
  `ENB_RECOVER_CMD`: on a wedge it runs that command instead of manual-halt (exit 0
  == in-game). `explore-live` wires it to `recover.sh`, which relaunches the real
  Net-7 launcher (`LaunchNet7.exe`) -> clicks Play -> re-injects enbmod
  (`inject.exe --proc client.exe`) -> enbmod autologin (creds from the client
  PROCESS env, no OCR) returns to in-game -> survey resumes from the ledger.
  Credentials live in a gitignored `.env` (`ENB_ACC_NAME/ACC_PASS/CHARACTER/EULA`)
  and propagate via the `LaunchNet7` process environment; WINE prefix + launcher
  path auto-resolve with a screenshot-and-ask fallback for the one non-discoverable
  coordinate, the launcher Play button (`ENB_LAUNCHNET7_PLAY_XY`). VERIFIED LIVE:
  path resolution, prefix/launcher/`inject.exe` discovery, creds plumbing, the
  enbmod channel (in-game in `space`, `[run] 2`). UNVERIFIED end-to-end: the
  LaunchNet7 -> Play -> inject -> autologin recovery chain (no launcher GUI was up
  to pin Play coords; this box is a local-docker config) -- flagged in `recover.sh`
  + SKILL.md, needs a real crash + launcher GUI to validate.

- **2026-07-09 CONVERTED OFF OCR to the enbmod Lua command channel.** The whole
  survey now drives the client through the injected enbmod `enb.*` API instead of
  screenshots/OCR/XTEST -- the same channel `login-to-client` was converted to.
  `drive_lua.py` (already the multi-hop driver) gained the last pieces the OCR
  wrapper scripts owned: (1) hang-recovery/relogin ported from `run-sector.sh` --
  the Lua channel IS the liveness signal (enbmod's poll is pump-clocked, so a
  wedge stops answering `enbmod.cmd`); `lua()` raises `ClientHang` after the
  channel is silent past `ENB_LUA_HANG_SECS` (150s, above a gate-load screen) AND
  a `return 1+1` probe fails, and `main()` relogins (local stack) or HALTS (manual/
  live, exit 42) then resumes the SAME hop from the persisted ledger; (2)
  per-sector `pcap.sh ensure`/`stop`; (3) gravity-well refusal (`gravity_wells.py`,
  exit 3). `explore-live.sh` repointed at `drive_lua.py` with an in-game
  precondition (`08-wait-ingame.sh`) and now sources the login skill's `lib.sh`
  for `client_win`. **Deleted 24 OCR scripts** (drive.py, survey.sh, run-sector.sh,
  enum_fast.py, all read-*/map/warp/shield/gate/rescue/undock helpers, the old
  lib.sh) + `navdata.cmd_predict` (map-pixel projection) -- net -5.5k LOC. SKILL.md
  + .env.example rewritten for the Lua path. Verified: hang-detection offline
  harness (raise-on-wedge + recover), and a LIVE end-to-end run (client in ABG
  1079 -> sector detected via enb.sector(), 26 navs settled, correctly refused as
  a gravity well, pcap stopped, exit 3). Warp/nav/gate path already proven live
  (Luna 21/24 -> Earth hop). This closes the "convert explore-sector off OCR" task.

- **2026-06-24 map-reposition planet-filter fix.** Live on HighEarth the map-click
  SEVERE-stuck fallback kept warping toward the "Earth" PLANET marker (parked 37k out,
  warp never closes). The direct-marker branch already self-skips a planet target after
  2 warps (MAX_ATTEMPTS), but `map_reposition` kept re-picking the Earth marker as a
  reposition WAYPOINT (it sits nearest the remaining unvisited navs), wasting a warp per
  `map_click_reach` call on an unreachable planet that drags in no new slice. Fix:
  exclude `by[mk]["type"] == "planet"` from `map_reposition` candidates, mirroring
  `relocate_far`'s existing planet exclusion. Pure movement heuristic -> zero completion
  impact (the direct-branch self-skip still handles planet TARGETS). Takes effect on the
  next sector's drive.py spawn (the running process keeps its in-memory code but is
  already bounded by `used`+dry-streak). `drive.py` map_reposition ~L1465.

Owner ask (2026-06-20): augment the Claude skills so the agent can, beyond
login (Phase AU), drive the live client to **fly a sector visiting every nav
node**, discover the hidden/missing ones, plan efficient multi-node warp paths,
cross **gates** into adjacent sectors, **reroute around class/reputation-locked
gates**, and keep a **structured, persistent, gitignored ledger + timestamped
action log** so a sector counts done only when WE recorded visiting every node.

Skill lives at `.claude/skills/explore-sector/` (Freya/MIT). Built on the
`login-to-client` (Phase AU) `lib.sh` (window discovery, XTEST click, screenshot,
cmd channel). Authoritative node lists come from `docs/sectors/json/<S>.jsonl`.

## NO-PROGRESS WATCHDOG (owner ask, 2026-06-21 night) -- 10-min stall drop
`drive.py` now tracks `last_progress_ts` (wall-clock of the last visit/skip). If
`NOPROG_SECS` (default 600s = 10 min) pass with NO nav discovered or visited, the main
loop calls `investigate_stall`: print a WARNING, dump the visit order + the visible
unvisited navs + a screenshot (`state/stuck-<sector>.png`), and -- the owner's threshold
for "you can drop and skip" -- SKIP the single most-contested unvisited nav (max warp
attempts), then reset the timer and continue. Self-healing (unlike `escalate_stuck`,
which HALTS): a dead-zone tail costs at most one 10-min window per leftover nav instead
of grinding forever. This is what the 73-min farthest-first AdrielPrime baseline needed
and never had (it ground 20/23 for >50 min on 2-3 radiation dead-zone markers).
Env: `ENB_NOPROG_SECS`.

## HYBRID NAV ORDER (owner ask, 2026-06-21 night) -- farthest-first then nearest
The old `sweep_round` targeted the FARTHEST unvisited nav every round. Early on this
is great (one long crossing bags every nav within `ENROUTE_K` 5k of the warp line for
free), but LATE in the run it degenerates into an end-to-end zigzag between opposite
sector clusters. Measured on AdrielPrime farthest-first: warp-target path = 3465 units,
**11 cross-sector relocations/repositions, 23 slow (>10s) warp-gaps, ~69 min to 20/23**
with a dead-zone radiation tail. A nearest-neighbour cleanup of the same node set = 1179
units (~3x shorter).
**The rule (owner):** farthest-first until **>=50%** of the sector's navs are done (pick
up many for free along the long crossings), then SWITCH to nearest-unvisited for the
scattered remainder. Implemented in `sweep_round`: `nearest_phase = done/total >= 0.5`;
target order = `navs` (nearest) when in that phase, else `reversed(navs)` (farthest).
`relocate_far` stays farthest (its job is to jump to a fresh cluster). En-route pickup
widened to `ENROUTE_K=5.0` ("if actively warping on a segment and within 5k mark it
visited"). Re-run AdrielPrime from scratch to compare wall-clock against the 69-min
farthest-first baseline.

## SECTOR GRAPH (ground truth) + SURVEY STATUS (2026-06-22)
Gate adjacency is **only** knowable by crossing -- gate DISPLAY names do NOT match
destinations ("System Gate to Sol" in Margesi lands in Equatorial). A trap bit me:
the gate-graph filter `type=="gate"` SILENTLY DROPS every `class-specific-gate`,
which made the reachable cluster look CLOSED when it was not. Scan gates **by name**
(`"Gate"`/`"Accelerator"` in the nav name), never by `type`.

Verified crossings (from->gate-name->to), all class-specific gates included:
- AdrielPrime --"Sector Gate to Margesi"--> Margesi
- AdrielPrime --"System Gate to Aragoth"--> Freya
- Margesi     --"System Gate to Sol"--> Equatorial
- Equatorial  --"Accelerator to Earth"--> **Earth** (FRESH; safe non-Freya route!)
- (Equatorial --"System Gate to Proxima Centauri"--> Margesi; loops)

So the {AdrielPrime,Margesi,Equatorial} loop opens into the fresh map via
Equatorial's "Accelerator to Earth" -- Freya is NOT the only door. **Earth is a hub**:
onward gates to Asteroid Belt Alpha, High Earth, Luna (all fresh).

Survey status:
- DONE: AdrielPrime, Margesi, Equatorial, Freya.
- IN-PROGRESS: **Earth 22/38 visited, 3 skipped, 1 danger zone (Nav Group Shrine** at
  approx (-22,38) -- wrecked there twice). A wreck's tow dumps the ship at AdrielPrime
  (home), 3 gates from Earth, so each Earth wreck costs an AP->Margesi->Equatorial->Earth
  re-route. Auto-relogin recovery is broken (enbmod `enb.self()` channel never returns
  this session), so hostile-sector recovery is LLM-in-the-loop.

## FOCUS-STEAL BUG: keys land nowhere while clicks work (fixed 2026-06-22)
Symptom: the W/D/C nav cycle reads a STALE target forever (e.g. "ECS Falcon") and
never locks a nav, yet warp/gate/undock CLICKS still work. Root cause: `nemo-desktop`
(the Cinnamon file-manager desktop window) stole the X INPUT FOCUS. XTEST *clicks*
follow the pointer so they still land on the client; XTEST *keys* follow the X input
focus, so every keystroke silently went to the desktop. `ensure_focus` used only
`xdotool windowactivate --sync`, which raises/activates the client but does NOT move
the X input focus off nemo-desktop. Fix: `ensure_focus` now also runs
`xdotool windowfocus "$id"` (sets X input focus directly -- verified live to recover
it). Diagnostic: `xdotool getwindowfocus` != the client win id -> focus was stolen.
NOTE: a frozen client (HUNG) presents the SAME "keys do nothing" symptom -- ALWAYS run
`hangcheck.sh` (frozen-framebuffer test) FIRST; if HUNG it is not a focus problem, it
is a client wedge needing kill+relogin. The 2026-06-22 Earth recovery hit BOTH: the
client wedged during the tow/multi-gate reroute (0 changed pixels across 1s = HUNG).

## SECTOR-VERIFY GUARD (owner ask, 2026-06-21 night) -- `read-sector.sh` + drive.py

Root cause of this session's false-completes: a wreck's **Request Tow can cross
sectors** (a Freya wreck towed the ship to **Adriel Prime**). The Freya driver then
flew Adriel Prime while believing it was in Freya -- navdata's fuzzy match force-maps
the foreign nav labels onto the closest Freya names at garbage ratios (0.3-0.74), so
"visible" filled with bogus entries and the sweep skip-"completed". Nav-label OCR is
NOT a safe sector-identity signal.

Owner-taught ground truth (verbatim): "OCR the top center of the map. its always the
same spot, it says the sector name."

**What shipped:**
- **`read-sector.sh` (NEW).** Opens/normalises the map (`open-map.sh`), crops the
  white sector-name title at the map body's top-centre (window-rel box
  `333 246 327 32`, override `ENB_SECTOR_BOX`), isolates the bright text, upscales,
  OCRs it, and resolves it to a canonical navdata sector via `navdata.py identify`
  (title-match). Prints `TITLE <raw>` + `SECTOR <CamelCase>`. Validated live:
  TITLE "Adriel Prime" -> SECTOR AdrielPrime.
- **drive.py `actual_sector()` / `verify_sector(expected, where)`.** Called at
  **drive-start** (after the first `open_map()`) and **post-tow** (end of
  `handle_wreck`, after undock). On a confirmed mismatch it aborts `EXIT_STUCK` with
  a precise message ("driving Freya but the ship is in AdrielPrime ... operator must
  re-route") instead of corrupting the wrong ledger. A None/unreadable title is
  tolerated (one bad OCR does not halt a good run). Validated live: `drive.py Freya`
  while the ship sat in Adriel Prime aborted immediately with SECTOR MISMATCH.

This closes the entire wrong-sector class of bug: the driver can no longer fly a
sector it is not physically in.

## METHOD v3 (live, 2026-06-21): the W / D / C nav keys replace the `>>`/`<<` button cycle

Owner found the game's built-in nav-target keybinds and directed a full conversion
away from the target-panel `>>`/`<<` BUTTON cycle (METHOD v2) to keys:
- **`W`** = lock the target filter to NAVS + select the NEAREST nav. Idempotent
  re-anchor; also bootstraps an empty selection (so NO map-click seed is needed).
- **`D`** = next-FARTHER nav (the cycle is distance-ordered). **`C`** = next-closer.
- So `W`=1st-nearest, `WD`=2nd, `WDD`=3rd, `WC`=farthest.

Owner ask (verbatim): "write the scripts and skill to use the W D and C method but
you can indicate that if SEVERELY stuck the map click can be a fallback. completely
forget about the arrow left and right arrows to cycle. update all scripts and skills.
use the fast rapid fire capture and batch ocr."

**CONFIRMED LIVE in Freya** that W/D/C land in the WINE client (unlike `Q`/`M`, which
are dropped under XTEST focus theft) -- `xdotool windowactivate --sync` + `xdotool
key` is the path. W->Nav Freya 4 (nearest), D->Nav Freya 5, D->Freya welc 3, etc.

**What shipped:**
- **`enum_fast.py` (NEW, the primary nav reader).** ONE process holding ONE X
  connection: press W (seed=nearest), then walk D `--count` times, grabbing ONLY the
  220x30 target-NAME box off the root window via Xlib `get_image` (~0.6ms/grab vs
  ~355ms for an `import` spawn -- ~590x faster), with a `grab_fresh()` byte-identity
  re-grab to dodge the stale-frame trap (a D whose new name has not repainted). After
  the walk, OCR EVERY frame in a SINGLE tesseract `--psm 6 tsv` pass over a stacked
  montage (one ~0.4s cold start, not N), bucketing word boxes by y-row back to slot
  index (robust to blank slots). In-process difflib matcher snaps each raw read to the
  sector's canon nav names. Prints `<rank>\t<raw>\t<canon>\t<ratio>` per slot, nearest
  first. Does NOT cut at a "wrap" -- distinct navs OCR-collide and stale frames
  duplicate, so the consumer dedups.
- **`drive.py` converted.** `click_next`/`click_prev`/`NEXT_BTN`/`PREV_BTN` replaced by
  `nav_seed()`(W)/`nav_next()`(D)/`nav_prev()`(C) via `ensure_focus()`+`tap()`. New
  `enum_navs()` wraps `enum_fast.py` and dedups by canonical name keeping the nearest
  occurrence. `sweep_round()` rewritten: enum -> pick NEAREST unvisited non-death-zone
  nav -> `select_named()` (W then a verified D-walk onto it by NAME, never by D-count)
  -> `warp_to()`. `relocate_far()` rewritten to walk `reversed(enum_navs)` for the
  farthest non-planet nav (no distance OCR needed). `flee()` is now W + D-hops. Dead
  `warp_candidate`/`OLD_STOP`/`ABW_FIRST`/`SANE_MAX_K` removed. Nav-key env:
  `ENB_NAV_SEED=w`, `ENB_NAV_NEXT=d`, `ENB_NAV_PREV=c`, `ENB_ENUM_SETTLE=0.13`.
- **Dead scripts deleted:** `enum.sh` + `goto.sh` (old `>>`-button cycle, zero external
  refs, superseded by `enum_fast.py`/`goto.py`). `goto.py` reuses
  `drive.enumerate_cycle`/`drive.select_named` (now W/D/C-based) and still works.
- **Map demoted to SEVERELY-STUCK FALLBACK only.** W self-bootstraps an empty pocket,
  so a map click is no longer in the normal loop -- `map_click_reach`
  (`ENB_MAP_FALLBACK=1`) still fires only when the cycle is dry AND shown navs remain.
- **SKILL.md updated:** new "How navigation works: the W/D/C nav keys (PRIMARY method)"
  section; map rules reworded to fallback; scripts table adds `enum_fast.py` and
  rewrites `drive.py`; "normal path: just run drive.py" prepended to the per-sector loop
  (the manual map walkthrough is now the fallback breakdown).

**LIVE TEST (Freya, 25/47 -> 26/47, bounded 2-round run):** enum_fast read the cycle
cleanly (most canon ratios 0.85-1.00); drive.py selected, warped, recovered from a
mid-transit stall (2 re-warps), VISIT'd Nav Freya 1 en-route, re-enumerated on a
target mismatch, and skipped Nav Gateway 3 after the 2x attempt counter. Full path
works end-to-end.

**HONEST CAVEAT (worth watching):** in round 0 `select_named` reported landing on
"Nav Gateway 3" but the transit polls show it actually warped toward Nav Freya 5 -- a
fuzzy-match collision (MATCH_MIN=0.55 is low, so a garbled OCR slot can mis-key onto
the wrong canon). The drive self-corrects (re-enumerate + the parked-not-target branch
+ the attempt counter), so it still makes progress, but targeting is not pixel-precise.
This is the same OCR-collision class METHOD v2 had; not introduced by the conversion.
A tighter MATCH_MIN or a confirming second read in `select_named` is the follow-up if
mis-lands prove costly.

## METHOD v2 (live, 2026-06-20 night): physical 2k-proximity via the target panel + target-cycle

Owner reframed the task: "visited" means **physically bringing the ship within 2
units** of each node (NOT map-click identification). The map-click-identify ledger
(the 12/25 below) was DISCARDED and the Equatorial ledger reset. The reliable
primitives that make this work, all screen-read + mouse + Q (no mods/injection):

- **Selected-target panel, bottom-RIGHT of the SCREEN** (not the map body). Shows a
  node NAME (window-rel box `1035 686 220 30`, can **marquee**-scroll for long
  names) over a thumbnail, and **"Dist: <n>k"** (box `992 906 160 30`). `read-target.sh`
  OCRs both (name = clean HUD font, reads well; dist = digit-whitelisted because the
  round "0" mis-OCRs as "O"). The dist is in **k** and **k == JSON-coordinate
  distance** (proven: GETCo Path 6 read 100.63k from origin == its JSON radius). So
  "within 2 units" == **Dist <= 2.0k**; `0.00k` == on it (big objects like Earth
  can't be reached closer than their hull -- that's fine, owner confirmed).
- **Target-cycle = selection primitive.** The panel's `>>` next-target arrow
  (window-rel `1246 925`; `<<` prev at `1216 925`) cycles through every in-range
  targetable nav, one per click, showing NAME+Dist. Clicking it sets BOTH the target
  AND the warp Dest (verified: cycling to a node then Q warps there). `enum.sh`
  cycles until a name repeats (wrap) to enumerate all in-range navs (16 in range near
  Earth in Equatorial); `goto.sh` cycles `>>` until the panel matches a wanted node,
  warps, and polls Dist to <=2k. No map-pixel math needed -- this REPLACES the
  click-the-marker approach (pixel-imprecise, missed clicks echoed stale Dest).
- **Warp = focus + Q.** `warp.sh` does `xdotool windowactivate` (raise alone left Q
  going nowhere) then `q`; NEVER sends Enter (a stray chat box eats Q). Map may stay
  open while warping. Warps are straight-line, ~2k/s, Dist decreases ~linearly.
- **Screenshot wedge hardened.** `import -window` can grab X and hang forever,
  silently killing a warp-poll loop (lost a 150s run to it). `lib.sh` now overrides
  `shot()` with `timeout -k 2 10` + `pkill -9 import` retry.

Validated end-to-end this session: warped Earth -> GETCo Path 6 (0.38k) -> Accelerator
to Earth (0.00k); `goto.sh` SELECT-by-name + warp + record confirmed. Ledger
(physical method) at 3/25.

**Scripts added/changed:** `read-target.sh` (panel name+dist), `enum.sh` (cycle
enumerate), `goto.sh` (select-by-name + warp + record-within-2k + en-route hits),
`warp.sh` (windowactivate+Q), `lib.sh` (robust `shot()`), `drive.py` (autonomous
per-sector driver: enumerate -> pick farthest unvisited -> warp -> record arrival +
en-route -> when no unvisited nav is in scanner range, skip the leftover
hidden/unreachable navs and complete).

**drive.py validated (2026-06-20 night):** round 0 on Equatorial enumerated 16
in-range navs, targeted the farthest unvisited (GETCo Sector HQ @165.83k), warped,
recorded the arrival @0.27k AND GETCo Path 3 @1.71k en-route; 4/25 -> 7/25. The
en-route catch only fires for nodes the straight warp line passes within 2k of
(GETCo Path 5 @4.22k, Huygens @2.99k closest-approach were correctly NOT recorded --
they get dedicated warps in later rounds). Loop is correct; it is just slow.

### Shield safety rule (owner ask, 2026-06-20 night) -- `shield.sh` + drive.py guard
While the ship is STOPPED, poll the shield every 5s; if it drops sharply or hits ~0
the ship is under fire -> FLEE (select a node a few navs away, warp to safety, wait
for the shield to recover). Do NOT shield-check while warping (moving == safe).
- The shield is the long horizontal **blue** bar at the bottom of the HUD, right of
  centre (~88% down, ~20% inward from centre; to the RIGHT of the short red hull
  bar). Blue-bar pixel bbox (full shield): x 819..954, y ~853. `shield.sh` band
  `812 962 847 860`.
- **The HUD tooltip does NOT render under synthetic (XTEST) hover in WINE** -- 7
  hover variants (raise/activate, incremental approach, micro-jiggle, 4s dwell, map
  open/closed) all failed to pop it. So we do NOT hover/OCR a tooltip. Instead
  `shield.sh` measures the blue bar's FILL directly (rightmost blue pixel within the
  fixed frame); the bar shrinks from the right as shield drops, empty at 0 -- which
  is exactly the danger signal, and needs no mouse (so it never knocks the cursor
  off the target-cycle arrows). Full shield reads a stable 99.3%.
- drive.py: `shield_guard()` gated to every 5s, flees on `pct <= 8` OR a `>=10`-point
  drop since the last poll (regen/rising is safe). Tunable via `ENB_SHIELD_*`.

### Skip rule (owner ask, 2026-06-20 night; RESOLVED 2026-06-21) -- visited-all-in-scanner == done
**The rule (owner, 2026-06-21):** "if you've visited all in your scanner you are
done this sector." When every nav the target panel can cycle to (everything in
scanner range) is visited, STOP. Whatever is still unvisited -- hidden navs that
never revealed, or shown navs that never came into range -- is `skip`ped, and the
sector completes. `state.py status`/`summary` and the COMPLETE line print the
per-sector skipped list. Skipped counts as resolved. (Per-warp `ENB_MAX_ATTEMPTS`,
default 2, still skips a node whose dedicated warps never get within 2k.)

**Why the relocation loop was DELETED (2026-06-21):** the old driver, after
visiting every in-range nav, re-targeted already-visited navs and reflew them
(farthest-first, 90 polls each) hoping proximity would reveal a hidden "?". Owner:
"reflying the same nodes will never reveal more" -- and the live Equatorial run
proved it: the drive visited Research Station Earth at 0.00k while **Kampala Annex
(-9.98,9.92) sat 0.06k away and stayed HIDDEN/unrevealed**, and it relocated off
every hidden nav's nearest visible neighbour with zero reveals. That futile loop is
exactly what made a run spin to the ~50-min timeout instead of finishing in 5-8 min
(diagnosis: one screenshot+OCR is only 1.67s, so OCR was never the cost -- the cost
was the relocation reflying). drive.py now stops as soon as no unvisited nav is in
range; `state.py` lost its dead `relocated`/`reloc-add`/`reloc-list` plumbing.

**Equatorial CLOSED (2026-06-21):** 16/25 visited (all shown navs), 9 hidden
`skip`ped (GDI Antwerp/Gutenberg/Mandela/Naverre, GETCo Somnus 16-19, Kampala
Annex), status=COMPLETE. Hidden-nav discovery is NOT achievable in the screen-only
cycle method (revealed hidden navs do not enter the target-panel cycle); that is an
accepted limitation, not a TODO -- the 84-sector sweep counts a sector done when
every in-scanner nav is visited.

### CLICK-WARP + false-complete fixes (2026-06-21 PM/evening, owner-directed)

A live AdrielPrime run sat parked and falsely "completed" at 2/23; fixing it
exposed several real bugs and one amendment to the skip rule above.

1. **Warp is now a CLICK, not the Q key** (`warp.sh` rewritten). The Q keybind only
   fires with window keyboard focus, which this desktop silently steals (VM
   pointer-warp / screenshot grabs), so Q was being DROPPED -- the ship sat still
   while the driver "warped" nav after nav and mis-recorded the stalls as visits.
   `warp.sh` now LEFT-CLICKS the round warp orb at window-rel **(633,868)**, waits
   ~5s for engage, and re-clicks (ENB_WARP_RETRY) if it has not engaged. (Owner:
   "to warp instead of Q just click the warp at bottom middle, it's a round button";
   "it takes 5 sec after you click warp to engage".)
2. **Warp-speed readout** (`read-speed.sh`). Green VARIABLE-WIDTH number left of the
   orb, box `578 618 852 874`. 0 == stopped. **REWRITTEN 2026-06-21 night (warp-cancel
   root cause):** the readout is NOT a 7-segment font -- it has a curved "2" -- and
   warp speed is **2000 by default** (4 digits), higher on many ships. The old custom
   7-segment classifier mis-classified every curved non-zero digit as 0 (and `[:3]`
   truncated to 3 cells), so it read a real 2000 as **0**; `warp.sh` then saw "0",
   judged the ship stopped, and CLICKED the warp orb again -- and the orb is a TOGGLE,
   so the click CANCELLED the live warp ("you keep cancelling warp"). Now: isolate the
   green pixels -> invert -> LANCZOS upscale -> **add a white border** (without it
   tesseract drops the leading/trailing digit) -> tesseract `--psm 8` digit-whitelist.
   Validated live against real frames: "000" parked -> 0, "2000" mid-warp -> 2000. The
   7-segment classifier and `[:3]` truncation were deleted (no dead code).
   - **`warp.sh` click-gating (2026-06-21 night, owner: "only click warp if at 0").**
     EVERY orb click (first included) is now gated on a confirmed solid 000: `warp.sh`
     samples the speed N times (`ENB_WARP_SPEED_SAMPLES`, default 3) and classifies
     moving / stopped / unknown with a bias toward "moving" (a SINGLE positive read ->
     do not click). It only clicks when every read is 000; a "?" / mixed read waits
     rather than risk a cancelling click. Verbose `elog` at every sample + engage poll.
3. **min-approach false-visit KILLED** (`warp_to`). The old fallback declared a nav
   "visited" once its distance stopped dropping for a few polls -- at ANY distance --
   so with warp silently dropped it marked navs 77k-207k away as visited. Now a stop
   far from the target triggers a bounded RE-WARP (REWARP_MAX), and a stop is a visit
   only within VISIT_K (2k) or MINAPPROACH_K (6k) of the wanted nav.
4. **Shield reader debounced by best-of-N** (`shield_guard` + `read_shield_robust`).
   The translucent shield bar washes out over gas-cloud/nebula backgrounds and reads
   0.0% on a genuinely ~90% shield (observed burst 0,0,87,94,89). The washout is
   ONE-DIRECTIONAL (only drops a reading, never invents a high one), so we take the
   MAX of a 5-read burst as the true shield. A single re-read was NOT enough -- it
   caught a second false 0.0% and flee-looped the sweep to a standstill.
5. **Map markers are NOT the nav cycle (owner: "fall back to map parsing").** Near a
   planet the ship is surrounded by ~56 "Gas Cloud" objects. They DO flood the MAP
   markers (every detect-nodes hit reads "Dest: Gas Cloud") and clicking a map marker
   sets the DEST, not the target -- warp then chases the near gas cloud and drops to
   000. BUT the NAV TARGET CYCLE (the >> arrow / read-target) is CLEAN: from Proxima
   Approach 1 it cycled 13 real navs (62k-160k), zero junk. So the working method
   stays the target cycle, not map-marker clicking. (Map-click identify via
   hover->read-xyz is unreliable right now: the X/Y/Z readout OCRs "?".)
6. **false-complete fix + skip-rule AMENDMENT (note the tension with the rule above).**
   The 2/23 "complete" happened because, parked in the gas-cloud pocket, the cycle was
   flooded past CYCLE_MAX=40 so sweep_round reached no real nav, returned "done", and
   the done-branch SKIPPED all 21 reachable navs. The ship had never TRAVERSED the
   sector. Fix: when a done-sweep leaves SHOWN (non-hidden) navs unvisited, drive.py
   now `relocate_far()` -- flies to the farthest selectable nav to change what is in
   range -- and retries, bounded by RELOC_MAX (5). **This re-introduces a relocation
   loop the owner deleted on 2026-06-21 -- but for a DIFFERENT purpose.** The deleted
   loop reflew already-VISITED navs hoping to reveal HIDDEN ones (futile; 50-min
   spins). This one only fires when reachable SHOWN navs remain out of range from a
   junk pocket, and `relocs` RESETS on any progress so RELOC_MAX bounds only
   CONSECUTIVE no-progress relocations -- a relocation that reveals nothing burns the
   budget and the sector completes; it can never spin for 50 minutes. Hidden-nav
   reveal is still NOT attempted (accepted limitation, unchanged). **If the owner
   considers this a violation of the visited-all-in-scanner rule, flag for review** --
   the alternative is accepting false-completes whenever a warp lands the ship in a
   gas-cloud pocket before it has crossed the sector.

### ALWAYS-BE-WARPING speed pass + AdrielPrime coverage failure (2026-06-21 late, owner-directed)

Owner: "you are still going pretty slow. I seriously meant Always Be Warping ...
log how long it took ... from when you saw 000 speed vs when you pressed the next
warp and optimize this to get it below 10 seconds."

**What changed in `drive.py` (the 000->next-warp dead-time path):**
1. **Warp-gap instrumentation.** `_motion["stopped_at"]` is stamped on every
   `warp_to` exit (the ship is stationary); the next `warp()` logs `warp-gap <s>`
   (printed + `logaction`), flagging `<<SLOW >10s`. This is the metric the owner
   asked for: time from speed-000 to the next orb click (the engage-confirm ~5s is
   part of warping, not counted).
2. **ABW first-fresh (ENB_ABW_FIRST=1, default).** `sweep_round` no longer scans the
   whole cluster to OLD_STOP for a "longest path" node -- it warps to the FIRST fresh
   in-range nav it lands on. The cluster is still bagged by the en-route pickup. The
   OLD_STOP longest-path sweep is kept behind ENB_ABW_FIRST=0.
3. **Name-only OCR while skipping done navs.** `read-target.sh ENB_TGT_NAMEONLY=1`
   skips the distance OCR (a second crop+upscale+tesseract). The sweep reads name-only
   to skip already-done navs and pays for distance only when it lands on a not-done
   node. Roughly halves per-slot OCR.
4. **Cheap-first shield.** `shield_guard` does ONE quick read; since the washout is
   one-directional, a single read >= SHIELD_SAFE proves "safe" and skips the 3-read
   burst. The burst (now ENB_SHIELD_SAMPLES=3, was 5) fires only when a quick read
   looks low. The redundant forced pre-enumerate shield burst was removed.
5. **Distance sanity cap (SANE_MAX_K=900).** The dist OCR occasionally fuses marquee
   digits into junk (`2441.2k`, `1382.4k`); relocate_far trusted these when picking
   the "farthest" nav. Capped; relocate's re-acquire pass is now name-only.

**Measured:** productive-path gap fell **37.9s -> 12.9s**; cheap-first shield +
name-only should take a sector with consecutive fresh navs under 10s. NOT yet proven
<10s in steady state -- see the coverage failure below, which kept AdrielPrime out of
the productive path the whole run.

**HONEST RESULT -- AdrielPrime did NOT get genuinely surveyed.** It "completed" at
**14/23 visited, 10 SKIPPED**. The ship was parked in the gas-cloud pocket near
Proxima; from there the target cycle only ever listed an in-range sub-cluster, and
`relocate_far` kept relocating to that SAME cluster (Gas Collector Ring 3 / Sector
Gate to Margesi) without dragging the other navs into scanner range. After RELOC_MAX
it skipped 10 navs -- including Radiation Marker 1/2/3 and Proxima Approach 3, which
have ordinary mid-sector coords and are almost certainly REACHABLE. So:
- The relocate endgame is slow (58-78s gaps: full-cycle name-only sweep finds nothing
  -> relocate scan) AND futile here (same cluster every time).
- This is precisely the case the owner flagged for the MAP-CLICK fallback ("find the
  nodes on the map and issue a mouse click and warp"; "if it's taking too long fall
  back to map parsing"). The AUTONOMOUS driver does NOT do map-click navigation, so it
  cannot reach spread-out navs that never enter scanner range from a pocket.
- **AW-3 RESOLVED (2026-06-21) -- map-click fallback built into `drive.py`.** Owner
  picked map-click autonomy ("build map click nav as fallback"). `map_click_reach`
  (env `ENB_MAP_FALLBACK=1`) runs after relocate is exhausted, BEFORE skipping shown
  navs: open map -> `detect_nodes` every marker -> click each + OCR `Dest:` name -> if
  an unvisited nav is drawn, warp straight to it; else `map_reposition` warps to the
  visible marker nearest (world coords) a remaining nav to drag a fresh slice into
  range, then re-survey (chain-walk). Bounded by `ENB_MAP_ROUNDS` + `ENB_MAP_REPO_DRY`;
  `map_reposition` iterates candidates so one re-click drift does not abort it.
  VERIFIED LIVE on AdrielPrime: direct-marker path reached Radiation Marker 2 (a
  cycle+relocate-skipped nav), 15/23 -> 16/23.
  - Affine prediction (`navdata.py cmd_predict`) was tried first and DROPPED: clustered
    anchors over-compress, every predicted click fell through to a visited sibling.
  - GLOW-MARKER detection (fixed 2026-06-21): the cyan mask missed pentagons over the
    planet's red glow (they render warm-white, not cyan, and merge along the rim).
    `detect-nodes` now has a `glow` mask and `identify_markers` cracks each blob with an
    offset probe comb -- recovered Gas Collector Ring 36 (0.76k), a planet-hugging nav a
    single blob centroid hid.
  - DRY-STREAK metric (fixed 2026-06-21): the chain-walk's concede counter gated on
    visited-count and quit after 2 repositions, mid-walk. A reposition's payoff is
    DELAYED (it drags a fresh slice into range; the visit lands a round or two later via
    the direct-marker branch), so the counter now gates on MARKER DISCOVERY -- it
    concedes only when a reposition reveals no previously-unseen marker. After the fix
    the walk crossed the whole Radiation Marker cluster (RM2 -> RM4 -> RM36) instead of
    conceding early.
  - KNOWN LIMIT -- SCANNER DEAD-ZONE navs (root-caused 2026-06-21, NOT a code bug):
    AdrielPrime finished 17/23, 6 skipped. One is the planet Adriel Prime itself (can't
    get within 2k of a planet centre). The other 5 (Radiation Marker 1/3, Proxima
    Approach 3, Gas Collector Ring 5, Adriel Collector 36) sit 39-88k from their nearest
    VISITED nav. A nav is only selectable (cycle) or drawn (map marker) within ~22k; a
    map-click can then warp to a *drawn* marker across long range (~85k observed). But a
    nav 39-88k from any reachable park point never enters ~22k of a place we can stand,
    so it never draws a marker to click and never enters the cycle -- it only flickers
    selectable mid-flight as the ship passes within ~22k (seen: RM1 at 21.89k while
    warping to RM4). Both the cycle-walk (relocate 5x) and the map fallback (reposition
    3x) walked the sector and neither could select them. Reaching dead-zone navs needs a
    NEW capability the screen-only stack lacks: opportunistic in-flight retargeting (grab
    a nav when it transiently enters ~22k) or free-flight to empty coordinates. Skipped
    with reason `scanner dead-zone: <d>k from nearest reachable nav`.
  - OWNER DECISION (2026-06-21): ACCEPT dead-zone navs as legit unfindable and bail
    faster -- do NOT build in-flight grab/free-flight. "Look at the map; once you've hit
    all nodes on the map, move on." AdrielPrime marked COMPLETE at 17/23.
  - MAP-ORACLE ENDGAME (built 2026-06-21, `drive.py main()` done-branch rewritten): when
    the cycle goes dry, open the map and `identify_markers`. If an unvisited nav is DRAWN
    -> chase it via `map_click_reach`. If NO unvisited nav is drawn -> relocate ONE pocket
    (bounded by RELOC_MAX, a far nav may draw after moving). Map still empty + relocation
    spent -> remainder is off-map / dead-zone -> skip and move on. Removes the old "grind
    RELOC_MAX blind relocations THEN one map fallback" ordering; the map now decides when
    the sector is done. Per-nav skip reason distinguishes "not on the map from any
    reachable pocket" (dead-zone) from "drawn on map but warp never closed within 2k".

### Gate-usability rule (owner ask, 2026-06-21) -- no icon == unusable gate
Crossing to the next sector is via gates. **Operational test:** fly onto the gate;
if NO use-gate icon appears (bottom-right HUD), this character CANNOT use that gate
(class-/faction-locked) -- do NOT retry it, reroute through a different gate.
(SKILL.md "Crossing to a new sector" / "Locked gates -> reroute".)

### GATE-CROSS VALIDATED end-to-end (live, 2026-06-21): Equatorial -> Margesi
First real gate-cross succeeded, proving the whole screen-only crossing path:
1. `goto.py Equatorial "System Gate to Proxima Centauri"` flew the Scout onto the
   gate (0.0k). (Cassini Point turned out to be a regular nav ~200k from the gates,
   not a gate -- don't trust name guesses, fly to the gate by its real nav name.)
2. Target the gate via the panel cycle (name-confirmed "System Gate to Proxima").
3. The **use-gate icon DID appear** -> the Scout CAN use this gate (it is
   `class-specific-gate` but not locked to this class). The icon is a blue disc
   with converging arrows centred at **(1236,641)** at 1280x960 -- now the
   validated default in `gate.sh` (`ENB_GATE_ICON` still overrides).
4. `ENB_GATE_ICON="1236 641" gate.sh` clicked it; the scene swapped to the new
   sector near-instantly (NO long dark load screen -- detect arrival by the scene
   change, brown-planet Equatorial -> red-nebula destination; mean-luma was flat
   ~27 throughout, so luma is useless as an arrival signal).
5. Identified the destination from its navs via `navdata.py identify` (Margesi,
   Loch Kaele, Loch Mar, Sol Approach 2, System Gate to Sol) -> **Margesi**
   (title=2 labels=5, strong match). **The gate's display name lies about the
   destination:** "...to Proxima Centauri" lands in Margesi. ALWAYS identify the
   new sector from its navs after the jump, never from the gate label.
6. Margesi is not a gravity-well sector; ledger init'd (32 nodes); `drive.py
   Margesi` launched. Equatorial -> Margesi is the second sector entered.

### SPEED on large sectors (diagnosed live, 2026-06-21) -- inherent warp time, NOT a bug
Margesi ran ~24 min for only 4/32 navs. Root cause diagnosed (not a software
stall): `warp_to` presses Q ONCE and the ship warps continuously to the target,
so each warp's wall-time is purely physical -- warp speed is **~5k/s** and
Margesi's navs are **200-400k apart** (Loch Mar 406k took ~80 one-second polls;
Loch Kaele 253k took ~47 and min-approach-stalled at 31.8k). No single inter-nav
gap exceeds the 3-min ceiling (longest warp ~80-90s), but a 32-node sweep over
such spacing is inherently ~1-2h, far past the ~20-30 min a normal sector takes (and
the few minutes a small/dense sector like Equatorial hits). There is no faster-warp
knob in screen-only mode -- the Scout's warp speed is fixed.
- **The one real optimization is visit ORDER.** drive.py currently visits in the
  game's target-cycle order, which zig-zags (Loch Kaele 253k -> Loch Mar 406k,
  i.e. out then further out). A nearest-neighbour ordering (greedy: from current
  pos always warp to the closest unvisited in-range nav) would cut total warp
  distance materially. This is a core-driver change that alters how the LIVE
  client is driven, so it is left for the owner to apply + watch -- NOT hot-swapped
  onto a running unattended drive. **Owner decision needed:** apply nearest-
  neighbour visit ordering to drive.py? (resumable: ledger persists visited, so a
  restart picks up where the current run left off).
- Margesi run progressed to 9/32 then WEDGED (stopped by autonomous tick 2026-06-21,
  ledger persisted at 9/32 -- resumable). See flee-wedge below.

### SPEED FIXES (2026-06-21 PM) -- the ~10s/nav crawl was an xdotool bug, not warp time
Owner watched the drive sit cycling ~1 nav/10s and warping nowhere. Three fixes,
in order of impact; Margesi finished (25/32 + 7 skipped) right after they landed:
- **`xdotool mousemove --sync` was blocking ~15.7s PER CLICK.** `--sync` waits for
  a `MotionNotify` the WINE client never confirms on this box, so every map/panel
  click (cycle `>>`, warp focus, gate) stalled ~15s; plain `mousemove` lands in
  ~3ms. `--sync` was added to beat a pointer-warping QEMU VM, but that VM is PAUSED
  (`-S`), so it was pure dead weight. Removed from `click_win`
  (`login-to-client/scripts/lib.sh`); it now moves, cheap-verifies the pointer
  landed (`getmouselocation`), retries once, clicks. ~15x faster clicks. THE fix.
- **read-target.sh full-window grab -> root region crop.** OCR only needs the two
  small panel boxes; grabbing the whole 1280x960 window cost ~1.3s. Now one
  `import -window root -crop` of just the bounding region of the name+dist boxes
  (~0.4s) then local crops. read-target dropped from ~2s to ~0.95s. (`xwd|convert`
  was faster still but fails `BadColor` on this root window.)
- **In-process sleeps trimmed:** `click_next` 0.45s -> 0.1s.

### STREAMING ENUMERATION (owner rule, 2026-06-21 PM) -- stop re-reading seen navs
Owner: "don't spam re-read the stuff we already saw -- cycle, and once you've found
a new nav and then hit known ones, back up to the last new one and warp." drive.py
`main` no longer calls the full `enumerate_cycle` every round (that re-OCR'd all
~35 in-range navs before every warp). New `sweep_round` streams the `>>` cycle:
- Tracks nodes seen THIS pass; a fresh (unvisited, in-range beyond 2k, not in a
  death zone) node is the live candidate and is the currently-SELECTED node.
- Counts already-KNOWN nodes (visited/skipped or seen-this-pass) but ONLY after the
  first fresh node appears (leading knowns ignored); the count accumulates across
  later fresh nodes and does NOT reset. At `OLD_STOP=3` (`ENB_OLD_STOP`) it stops
  and warps to the LAST fresh node, stepping `<<` back the slots walked since it.
  Worked examples (owner's): `NNNNOOO` -> 3rd old trips -> warp 4th(last) new;
  `OOOONOONO` -> leading O's ignored, 2+1 olds across 2 news = 3 -> warp 2nd new;
  `NNNNNNNNN` that loops -> first wrap (a node seen again this pass) -> warp the
  farthest(last) new immediately, no need to reach 3.
- A full wrap with NO fresh candidate => every in-range nav done => sector done.
- `<<` prev-target arrow pinned at `1217 925` (`ENB_TGT_PREV`); read live 2026-06-21.
- `enumerate_cycle` is KEPT (not dead) -- `goto.py` (gate-cross navigation to a
  specific named nav) still uses the full-list read to hop toward an out-of-range
  target. Streaming `goto` is a follow-up optimization.

### goto.py SELECT + WARP fixes (2026-06-21 PM) -- crossing Margesi -> AdrielPrime
Validated the gate cross with three fixes after `goto.py` warped to a 0k nav and then
sat polling a parked ship for 90s:
- **`select_index` deleted, replaced by `select_named`.** Index-by-click-count
  selection drifts onto the wrong nav (warped to "Shipyard Beltway 10" we were
  already 0.00k from -> warp cancels instantly). `select_named` streams `>>`
  reading each slot by NAME and stops when the panel is actually on the wanted key.
- **`select_named` wrap detection uses a single `first_key`, not a cumulative
  `seen` set.** Fuzzy matching (MATCH_MIN 0.55) collides two slots onto one key and
  a `seen`-set wrap trips a FALSE "wrapped" break before reaching the target gate
  (symptom: "select failed; retry" while `enumerate_cycle` -- which uses first_key
  -- found the gate fine). Now mirrors `enumerate_cycle` exactly.
- **goto hop candidates filtered to `dist > VISIT_K`** so a hop never targets a nav
  we are already sitting on (the 0k instant-cancel).
- **`warp_to` parked detector.** A warp can auto-chain down a nav lane and park the
  ship ON A DIFFERENT nav than `want_key` (the client re-targets nearest on
  arrival); the `want_key`-only stall logic then never fires and it burns all 90
  `WARP_POLLS` sitting still. New check: same non-target nav, distance steady
  (<0.5k delta), dist <=5k, for >=4 polls -> bail so the round re-enumerates from
  where we actually ended up. This is what let goto walk the SB9->...->Adriel
  Approach lane and then warp the final 98k hop straight onto the gate.
- Cross confirmed: clicked use-gate icon (1236,641), landed in **AdrielPrime**
  (gate label "Sector Gate to Adriel Prime" != navdata sector name, as warned);
  identified by its "System Gate to Aragoth" + "Sector Gate to Margesi" navs.

### HARD-HANG detection + auto-recovery (2026-06-21 PM) -- owner caught a frozen client
While `warp_to` polled in AdrielPrime the **client hard-hung**: `client.exe` stayed
alive but wedged in an internal busy-loop. Detection signature (the durable part):
- **The discriminator is a FROZEN FRAMEBUFFER, not CPU.** A healthy in-space client
  animates the starfield every frame, so two full-window screenshots ~1s apart
  differ. A hung client renders byte-identical frames. CPU is NOT the signal: the
  normal in-space render also pegs ~980-1100% under WINE (measured both hung AND
  healthy this session) -- high CPU alone false-triggers. `hangcheck.sh` takes 3
  shots ~1s apart and prints HUNG only if all identical.
- `drive.py`: `client_hung()` wraps hangcheck.sh; `warp_to` calls it on a run of
  blank panels (>=5) BEFORE `handle_wreck`, raising `HangDetected`; `main` exits
  `EXIT_HANG=42`. New `run-sector.sh` wrapper runs drive.py and, on exit 42,
  kills + re-logs-in (`login.sh` + forced 07/08 retry for the charselect Enter
  flake) and RE-RUNS the same sector -- the ledger persists visits so it resumes,
  not restarts. `run-sector.sh` is now the preferred entry point for a long sweep.

### BLIND-CYCLE false-complete bug (found + fixed 2026-06-21 PM)
After the first relogin, `sweep_round` read an empty target panel for a whole cycle
(HUD not settled post-load) and returned `done`, so `main` SKIPPED all 22 remaining
AdrielPrime navs as "unreachable" and printed COMPLETE 1/23 -- a FALSE complete that
skipped "System Gate to Aragoth" sitting ~2.5k away. Fixes:
- `sweep_round` now counts `readable` slots; a pass that read ZERO navs returns
  `{"status":"blind"}` (NOT `done`). "Nothing readable" != "every nav visited".
- `main` on `blind`: check `client_hung()` (raise if wedged) else re-open map +
  settle 2s + re-round; after 5 consecutive blind rounds it stops WITHOUT marking
  complete (`blind-stop`), never false-completing.
- The 22 false skips were cleared from AdrielPrime state (logged `reset-falseskip`).

### FLEE-WEDGE bug (found live, 2026-06-21) -- a weak mob halts the whole sweep
At round 6 the ship reached the **Shipyard Beltway** nav cluster, where a mob chips
shield ~10% per pass (99.3% -> 89.3%, landing EXACTLY on `SHIELD_DROP=10`). Sequence
that wedges: round-top `shield_guard(force=True)` (drive.py:395) sees the 10-pt drop
-> `flee()` -> shield regens to ~99 at the safe spot -> next round re-enumerates back
INTO the same cluster (the unvisited Shipyard Beltway navs are still the target) ->
chipped to 89 again -> flee again. 4 rounds at 9/32, zero progress, no death risk
(shield held 88-94). It will loop indefinitely: there is no "give up on a nav after
K flees and skip it" escape, so one weak mob near a nav stops everything.
- **Root cause is two-fold:** (a) `SHIELD_DROP=10` is twitchy -- a trivial 10% dip is
  not real danger but trips the guard; (b) after a flee the driver retargets the SAME
  contested nav with no retry budget, so flee+return loops forever.
- **FIX IMPLEMENTED (owner-directed 2026-06-21: "flee retry three times otherwise
  pick a different route"):** drive.py now has a wedge detector. `FLEE_RETRY=3`
  (env `ENB_FLEE_RETRY`). The round loop counts consecutive flees with no new visit
  (resets on any progress); after 3, `reroute_from_wedge()` skips the unvisited nav
  NEAREST the wedge spot (`_dest` coords, reason "contested -- fled 3x ... rerouting")
  and does a long relocate (`flee(hops=10)`) to leave the contested cluster, so the
  next round targets a different route. The `<=8%` absolute-danger flee and the
  10-pt-drop flee are UNCHANGED -- the safety guard is not weakened, we just stop
  fleeing forever to adjacent contested navs. Did NOT widen `ENB_SHIELD_DROP`
  (owner asked only for the retry cap).
  Resume Margesi from 9/32 with `drive.py Margesi` (reads the persisted ledger, skips
  the 9 already visited; the contested Shipyard Beltway cluster will exercise the
  new reroute on the first wedge).

#### flee() REWRITTEN for radiation sectors (2026-06-21 night, AdrielPrime)
AdrielPrime has **environmental radiation** that pins shield to 0% but does NOT kill
the ship -- it cannot be out-run, so the old `flee()` (fly to safe spot, then WAIT up
to 30s for shield to regen) wedged forever: every round read 0%, fled, waited for a
regen that never comes in radiation, made no progress (stuck 3/23). Owner: "shields
at 0 because radiation ... make flee react fast but not break ... just go to the next
nearest one that's farther than 2k." `flee(sector)` now: enumerate navs nearest-first,
warp to the FIRST unvisited (non-done, non-danger) nav, mark it, and RETURN -- no
recovery wait. `shield_guard()`'s danger branch honours flee's return (True =
redirected, keep going; False = nothing unvisited in range). The `hops` param is gone;
the wedge-reroute caller now uses `relocate_far(sector)`. Validated `ast.parse` + the
single remaining `flee()` caller is `shield_guard`.

### SHIELD READER IS BROKEN -- the real cause of the "wedge" (found live, 2026-06-21)
Owner observed shields regen to 100% in ~5s, so a persistent <100% reading is a
sensor bug. Confirmed with data (state/shielddbg/, served at http://127.0.0.1:8899):
- `shield.sh` measures fill by the RIGHTMOST single pixel passing a strict
  `b>110 && b>r+40 && b>g+20` test in box `812 962 847 860`.
- The shield bar is a DIM desaturated-blue body (b~=60-90) with sparse BRIGHT-blue
  "sparkle" pixels. The dim body FAILS `b>110`; only ~6-29 sparkle pixels qualify per
  frame. So the rightmost-qualifying-pixel jitters frame to frame -> readings bounce
  82/89/91/96.7% on a bar that is actually FULL. blue-fill-fraction was ~0.5% (15px).
- Row profile (frame_2): configured box rows 847-860 catch a sparse shimmer line at
  y=855-857; the DENSE bar body is ~y=864-865 (91-144 px), dim, and OUTSIDE the box.
  See state/shielddbg/shield_rows.png (annotated).
- **Consequence:** the Margesi flee-loop was firing on PHANTOM shield drops, not a mob.
  The flee-retry-3 reroute (above) is a valid safety net for genuinely contested
  clusters but was treating a symptom here.
- **FIX (owner wants to WATCH the re-test -- NOT done unattended):** rewrite the
  reader to detect the bar BODY, not sparkles: background nebula is red-dominant, so
  `blue-dominant` (`b>r && b>g`, loose b floor ~40-50) cleanly separates bar from
  background. Measure fill as rightmost blue-dominant column (or blue-dominant
  fraction) over the bar-body rows (~y 853-866) between the gold endcaps (~x 838-963).
  Recalibrate `ENB_SHIELD_BOX` to the body rows. Validate across several live frames
  (should read ~100% steady when parked) before re-enabling the drive.

### Wreck / Request-Tow rule (owner ask, 2026-06-20 night) -- `wreck.sh` + drive.py
If the ship gets stuck / can't move, screenshot the whole screen and check for a death
panel. The "Request Tow" button returns you to your last registered station. On a
wreck: (1) record WHERE you died (sector + coords) as a permanent **danger zone**
(`state.py danger-add`), (2) skip the node you died at, (3) click Request Tow. Never
CONCLUDE a warp within `DANGER_RADIUS` (=10, k-units) of a danger zone -- warping PAST
is fine, stopping there is not. drive.py: `check_wreck()`/`handle_wreck()` +
`in_danger()` filters targets; `wreck.sh` finds the Tow button by OCR
(`ENB_TOW_BTN="x y"` pins it once a real death calibrates the coord). The WRECKED
path is coded but UNCALIBRATED (no real death yet); ALIVE is confirmed (no false
positive on a healthy screen).

### UNDOCK-AFTER-TOW (owner-taught + validated live, 2026-06-21) -- `undock.sh`
A real wreck finally happened: a Freya combat death near "Gaius Wreck" + Request Tow
towed the ship CROSS-SECTOR back to Adriel Mining Station in AdrielPrime, where it sat
DOCKED. The old `handle_wreck` clicked Tow then reopened the map -> the drive spun
forever at the dock (no warp cluster). Two fixes:
- **`undock.sh` (new).** Detect docked = warp-speed OCR region blank (the bottom-centre
  warp dial / speed / bars / 1..6 circles are hidden while docked). Station is a T-hangar;
  ship parked bottom-right; you zone in at one of 3 random catwalk orientations. Rotate
  the avatar RIGHT with ARROW KEYS (the one sanctioned keyboard use) in batches until the
  ship shows on the RIGHT HALF clear of the chat overlay -- detected by its GREEN+RED
  nav-light PAIR (saturated colour the station's white light-strips never make; the glow/
  plume signal was dropped as it false-matched light-strips). Click the hull centre,
  verify warp-speed digits return. Overshoot -> nudge left, fine steps of 5.
- **`handle_wreck` calls `undock.sh` after the tow respawn**; if it can't get back to
  space it exits `EXIT_HANG` with a loud message rather than spinning (a tow can cross
  sectors, which the drive can't auto-route from -- operator handles the handoff).
- VALIDATED LIVE 2026-06-21: manual undock by this exact method launched the ship from
  Adriel Mining Station into AdrielPrime space; then gated AdrielPrime "System Gate to
  Aragoth" -> Freya and resumed the Freya sweep. `ENB_TOW_BTN` for `wreck.sh` still
  uncalibrated (the death panel auto-recovered before a coord was pinned).
- SKILL.md "Undocking from a station" documents the procedure.

### Gravity Well rule (owner ask, 2026-06-20 night) -- `gravity_wells.py` + drive.py
Refuse to explore any sector that contains a Gravity Well. Source of truth is GAME
LOGIC, not a guess: `sector_objects.type == 41` maps to `AddNewObject(OT_GWELL)` in
`server/src/SectorContentSQL.cpp:378`. A gravity well terminates warp mid-flight
(`PlayerClass.cpp:1806` "detect gravity well in warp" -> `if (m_GWell != -1)
TerminateWarp();`) and degrades speed/handling (`m_GravityField`,
`AdjustAndSetSpeeds`) -- fatal for a screen-only warp-to-the-next-nav run. The
explorable (has-jsonl) refused sectors: **ABB, ABG, Achenar, AragothPrime,
BlackbeardsWake, Lagarto, MarsGamma, Menorb, Paramis**. (type-41 sectors with no nav
file, never explored anyway: Moto, Nebiros, Chandilar, Sho, Menorb Planet, Test
Sector.) drive.py refuses these on launch (exit 3, logged "refused"); override only
with `ENB_ALLOW_GRAVITY_WELL=1` for a deliberate test. Per-well coords+radii captured
in `gravity_wells.py` for a possible future finer (radius-only) avoidance.

NOTE (brutally honest): a blanket sector refusal is COARSE. Several of these (Aragoth
Prime, Lagarto, Menorb) are major sectors whose gravity wells are localized objects
with finite radius, not sector-wide. The finer alternative -- treat each well like a
danger zone (never conclude a warp inside its `radius_k`) and still explore the rest
of the sector -- would cost ~0 coverage. We implement the literal owner ask (refuse
the whole sector) and keep the per-well data so the finer mode is a small follow-up.

**Known slow spots / next:** the `goto.sh` SELECT loop re-OCRs the panel each cycle
step (~3s/step); a full sector at warp-to-each-node cadence is minutes/sector x84 =
too slow. Efficiency redesign (the analytic sweep): since k==JSON-distance and warps
are straight lines, while warping to a far DEST compute ship pos = current +
(dest-current)*(1 - Dist/seg_len) from the single Dist read, then mark EVERY node
within 2k of that point visited -- many nodes per warp, one selection per warp. Build
this after Equatorial is finished correctly node-by-node to prove the method.

## BREAKTHROUGH (live, 2026-06-20 PM): the top-down map DOES exist and works (mods OFF)

The earlier "there is NO top-down map in this build" conclusion (kept below for
history) was WRONG. It was reached with the modded client (the `hide-ui` mod was
breaking/hiding the HUD) and by looking for the wrong control. Re-tested on a
VANILLA client (UseClientMods=false, UsePositionFeed=false, all ModStates false)
the in-game sector map renders correctly and shows every in-range nav node. Owner
confirmed the exact controls.

**Opening + zooming the map (screen-read + mouse only, NO mods/injection).**
Owner gave EXACT coords (2026-06-20), measured INCLUDING the 32px WM title bar.
`import -window` captures the content area only (no title bar), so the
window-relative coords we click are **owner_Y - 32** (X unchanged). All values
below are already converted to content-relative:
1. **Open the map**: click the 3rd of the four round bottom-left HUD buttons
   (the interlinked-nodes / galaxy icon) at window ~**(132, 870)**. A map panel
   titled with the sector name (e.g. "Equatorial Earth") opens.
2. **Maximize FIRST (mandatory)**: LEFT-CLICK the "+" at window **(455, 431)**
   (owner 455,463). It is the SMALL map's TOP-RIGHT corner button; the map grows
   from bottom-left toward top-right, so clicking it expands to the large extent.
   When the map is ALREADY large, (455,431) is interior transparent map (the 3D
   world shows through), so the click is a harmless no-op -- ALWAYS click it first.
   EVERY other map coord below assumes the large map.
3. **Center on ship**: LEFT-CLICK the "center" toolbar button at window
   **(84, 238)** (owner 84,270). Tooltip "Left-click to center camera on your ship".
4. **Zoom out**: RIGHT-CLICK that SAME button (84, 238) repeatedly (~8-12 times).
   Tooltip "Right-click to zoom out". Each right-click steps the zoom out. (The
   mouse SCROLL WHEEL is a RED HERRING -- over the map/scanner it only drives the
   3D chase camera, never the map zoom. Do NOT use the wheel for the map.)
5. The maximized map content area is window-rel corners (22,222)-(760,222)-
   (760,786)-(22,786) (owner gave 22,254 / 760,254 / 760,818 / 22,818). The
   sector's nav nodes render across this rectangle, so crop to **x22..760,
   y222..786** (l=22 t=222 w=738 h=564). Earlier crops of the panel's top-left
   corner showed only starfield because the map was never maximized.

**Node icon legend on the map (confirmed against owner's capture):**
- **gray pentagon** (house-shaped w/ a cross) = a nav station / nav node,
- **gray "?"** = an unidentified nav (in range, name not resolved),
- **green "?"** = nav (nearby / discovered state),
- **green square** (boxed station glyph) = current location marker,
- **coloured name labels** sit next to some nodes: e.g. red "GETCo Path 6"
  (current Dest), blue "Scout devuserie". "Dest: <node>" prints at the bottom.

**Read flow:** screenshot the full frame, crop the centre overlay (NOT the corner),
brighten, and read the pentagons/"?" positions + any labels. To resolve a "?" name,
hover the node (tooltip) or click it to set Dest (label turns coloured). This gives
an at-a-glance view of EVERY in-range node at once -- the completeness tool the
3D-beacon hunt lacked. Fly toward unexplored regions to bring more nodes into range
(PB-22), re-open/recentre/zoom the map, repeat until the ledger is full.

Next: re-validate the warp loop driven off this map (pick a node on the map, set
Dest, warp), recalibrate the vanilla-HUD coords (toolbar/target panel differ from
the modded HUD), then grind Equatorial -> all 84 sectors. Update SKILL.md to teach
THIS map method (supersedes both the AW-1 "press M" model and the "no map" note).

### LIVE VERIFICATION (2026-06-20 later PM) -- what reproduces and what does NOT

Drove the map live on the vanilla client (window 130023428). Findings, with
evidence in `/tmp/enbexplore/`:

- **CONFIRMED: the "center" toolbar button is at window-rel `(84, 232)`** (matches
  owner `(84,238)`). It is the 2nd of 5 round blue buttons in a notch at the
  BOTTOM of the map panel `[? | center | xyz | dot | labels]` (5th tooltip:
  "Click to toggle between various label settings on the starmap"). Left-click =
  center on ship; right-click = zoom out one step. Zooming out DID spread the
  nodes. (`zoomed.png`, `mapbody.png`, blue-button x-centers ~50/79/110/140/168
  at y~232.)
- **CONFIRMED two real bugs, both fixed in `open-map.sh`:**
  1. `xdotool mousemove --sync` HANGS on this box -- a QEMU VM (`gastown`) runs a
     usb-tablet ABSOLUTE pointer device that fights the warp; `--sync` blocks
     forever. Switched to plain `mousemove` + settle sleep. (Hung the first run
     for 2+ min at the center mousemove.)
  2. The open-map HUD button `(132,870)` is a TOGGLE -- the map was already open,
     so clicking it CLOSED the map and the rest of the sequence clicked into 3D
     space. `open-map.sh` now DETECTS whether the map is open (blue center button
     present at 84,232) and only toggles when closed.
- **RESOLVED (owner corrected my misread): the map I was viewing WAS already the
  large/maximized version.** My error was cropping the WRONG region -- I moved the
  crop to the area ABOVE the toolbar, when the map BODY is the lower-center region
  the owner gave: content-rel **(22,222)-(760,786)**. The toolbar/center button at
  (84,232) sits at the TOP-LEFT of that body; the −/X window buttons are top-right;
  "Equatorial Earth" is the SECTOR-name title at top-center; nodes render across
  the body; "Dest: <nav>" prints at the bottom. Cropping run1.png to
  (22,222,738,564) shows the full readable map (`body.png`). The owner's coords
  were right all along.
- **The "+" maximize at `(455,431)`: it is the SMALL map's TOP-RIGHT corner
  button.** The map grows from bottom-left toward top-right; when small, the
  top-right "+" is at ~(455,431); clicking it expands to the full (760,222)
  top-right extent. When ALREADY large, (455,431) is interior transparent map, so
  the click is a harmless no-op -- hence "ALWAYS click it first" (my earlier
  hover there showing the 3D station was just the world showing THROUGH the
  transparent overlay, not "no button"). `open-map.sh` now always left-clicks it
  first.
- **Node reading now works** off the correctly-cropped large body:
  `detect-nodes.py` (box (22,270,738,486), size-capped <=120px to drop the title /
  Dest label / 3D hull bleed) returns ~12 clean green/cyan/gray marker centroids;
  some dim gray pentagons still slip past, so the authoritative check is
  click-each + OCR-Dest + cross-ref the 25 known Equatorial nodes from navdata.py.
- **Name OCR works**: `read-navname.sh` crops the (105,758,580,20) Dest strip,
  upscales+thresholds, runs **tesseract 5.3.4** (owner installed it), strips the
  "Dest: " prefix -> nav name. Verified on run1: "Dest. GETCo Sector Headquarters"
  -> "GETCo Sector Headquarters".

New/edited scripts this session: `open-map.sh` (open-if-closed detect via blue
center-button probe, ALWAYS click "+" (455,431) first, no `--sync`, crop the large
body (22,222,738,564)), `detect-nodes.py` (numpy node-marker detector over the
large body), `read-navname.sh` (tesseract OCR of the Dest strip).

### LIVE SURVEY RUN (2026-06-20 night): Equatorial 3/25 -> 12/25, two more findings

Ran the full loop live. It works. Drove the ledger 3 -> 12 from a SINGLE map view.
Two corrections baked into SKILL.md step 2a:

1. **0.6s settle is mandatory after click-map.sh.** The Dest label redraw lags the
   click; a 0.3s settle reads the PREVIOUS frame, so every node falsely reads as the
   last-selected one (my first sweep returned "GETCo Path 2" for all 12 candidates --
   it was stale frames, not a selection failure). At 0.6s, distinct nodes resolve
   cleanly (Rosetta Point 0.92, Research Station Earth, GETCo Path 6/5/2, Cassini
   Point, Ulysses Point, Earth, Accelerator to Earth, GETCo Sector HQ -- all matched).
2. **A missed click does NOT clear Dest.** Clicking empty transparent map leaves the
   previously-selected node in Dest, so a miss is indistinguishable from a hit unless
   you DIFF the Dest name before vs after. `detect-nodes.py` over-produces (label-text
   fragments, 3D-hull bleed); the before/after diff is the real filter. ~3/4 of
   candidates were real nodes; the rest (e.g. (387,517), (470,630), (470,722)) were
   misses that echoed the prior Dest.
3. **One map view only shows the in-range CLUSTER**, even fully zoomed out. The 9
   nodes recorded were the ship's-current-cluster shown nodes. The remaining 13 are
   NOT on this view: 5 sector-EDGE shown nodes (Huygens Point, Guiana Path 1, GETCo
   Path 1, GETCo Path 3, System Gate to Proxima Centauri) and 8 HIDDEN nodes (GDI
   Antwerp/Gutenberg/Naverre, GETCo Somnus 16-19, Kampala Annex). Reaching them needs
   actually WARPING the ship across the sector to bring them into range / reveal the
   hidden ones, then re-sweeping. That is the next, more disruptive step on the live
   client (multiple warp.sh hops + re-open-map per region).

QEMU note: the `gastown` VM is currently paused (`-S`), so the usb-tablet pointer
trap is NOT active right now -- `mousemove`+click lands exactly where commanded.
click_win in login-to-client's lib.sh still uses `--sync`; it did not hang this run
because the VM is paused, but if gastown is ever resumed the explore clicks will
need the no-`--sync` path (already used in open-map.sh).

Next: warp-and-resweep loop for the 5 edge shown nodes + 8 hidden nodes to finish
Equatorial 12/25 -> 25/25, then gate-cross. (Currently 12/25 in the ledger.)

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

## AW-3 (2026-06-23) -- live Starstrukk survey: two egress bugs fixed

Live scan (char Starstrukk, Net7Proxy, ENB_PCAP=1) completed Ishuan 24/29 but
HALTED unable to leave. Two distinct, now-fixed bugs in the egress path:

1. **undock-after-tow camera pitch** (`undock.sh`). A Request-Tow wreck recovery
   drops the avatar in a hangar with the camera pitched DOWN at the deck, so the
   ship parked ELEVATED in the bay sits ABOVE the top of the frame -- outside
   `find_ship`/`find_ship_hint`'s `y in [0,0.72h]` scan region -- and the yaw
   sweep could never see it (acquire FAILED outright). Fix: `aim_camera_up()`
   presses the **Up arrow** (`ENB_UNDOCK_CAM_UP`, default 6) before the acquire
   loop, plus a progressive `ENB_UNDOCK_CAM_UP_STEP` (default 2) per close-in
   round. Validated live: ship boarded + launched back to space (Ishuan).

2. **approach_gate never warped to the gate** (`drive.py`). The old code believed
   "you CANNOT warp to a gate" and ONLY hopped between gate-adjacent navs (with a
   unit-buggy `_d/1000` world-coord print), never once selecting+warping the gate
   itself -- so it looped forever (Yokan -> Barn's -> Traders Run ...) at huge
   warp-gaps and then HALTED. **That assumption is false**: a gate IS a selectable
   target in the W/D/C cycle (live: "Gate to Capella System" enumerates at ratio
   0.97) and warp DOES engage toward it. goto.py already crossed gates this exact
   way. Rewrote `approach_gate` to mirror goto.py: enumerate -> select gate by
   name -> `warp_to` it; hop to the nearest in-range nav only when the gate is out
   of scanner range. Validated live: goto.py warped onto the Capella gate (3.91k),
   `gate.sh` saw the use-gate icon (blue=0.31) and crossed **Ishuan -> Yokan**.

Survey resumed from Yokan; the corrected approach_gate now drives subsequent
gate crossings. Client-only changes -> no server/proxy wire change, no plans/29 CV.

3. **gate routing bounced between completed sectors** (`gate_route.py` NEW,
   `drive.py`, `survey.sh`). With approach_gate fixed, the autonomous survey
   surfaced a third bug: it kept taking the SAME first gate out of every completed
   sector and ping-ponged Yokan <-> Ishuan forever, while 4 other gates (to new
   sectors) sat unused -- then STALE_MAX=3 killed the whole survey. Root cause: a
   gate's DISPLAY label does NOT name the sector on the other side ("Gate to Castor
   System" in Yokan lands in **Ishuan**; "Gate to Capella System" in Ishuan lands in
   **Yokan**), so `pick_gates`' old label-vs-completed-name heuristic could not route
   out of a surveyed region. Fix: new `gate_route.py` records the ACTUAL destination
   of each (sector,gate) crossing (`pending_gate.json` -> `gate_edges.json` in the
   workdir) and ranks gates unknown-dest-first, known-incomplete next, proven-path-
   to-completed (and locked/no-icon gates) last. `survey.sh` now only counts a
   completed-sector arrival as "stale" when `gate_route.py has-unexplored` reports
   NO frontier gate remains -- so traversing a finished region toward fresh ground
   no longer burns the stale budget; the survey stops only when every reachable gate
   is a proven dead-end. Validated live: seeded the two observed edges, relaunched,
   and the survey routed Yokan -> **Sector Gate to Kailaasa** -> **Kailaasa**
   (new sector, 0/22) instead of bouncing back to Ishuan.

4. **dud / arrival-only gates + flaky single crossings** (`gate_route.py`,
   `drive.py`, `survey.sh`). Beyond labels lying, two more failure modes killed
   unattended runs: (a) a **wormhole-exit you arrive through but cannot leave by**
   (`Maeldun´s Weft` in Kailaasa) shows a use-gate icon, accepts the click, but the
   ship never crosses -- and because a non-crossing records no edge it stayed a
   rank-0 frontier and got re-tried on every visit, forever; (b) a single flaky
   crossing (slow wormhole load outlasting the detect poll, or a missed use-gate
   click) made `survey.sh` HALT immediately, so one bad click killed a whole
   multi-hour run. Fixes:
   - `survey.sh` now RE-DRIVES a same-sector arrival up to `ENB_CROSS_RETRY`
     (default 3) times before HALTing, instead of halting on the first non-crossing.
   - `gate_route.resolve()` returns the gate that was clicked-but-did-not-cross so
     `drive.py` sets `SOFT_STUCK_GKEY` and `pick_gates` deprioritises it THIS run --
     the retry picks a different gate (routed Kailaasa around Maeldun to
     **Sector Gate to Dahin -> Dahin**, then on the return visit to **Gate to Sol
     System**, both fresh ground).
   - `resolve()` also keeps a PERSISTENT per-gate fail tally (`gate_fails.json`);
     at `FAIL_LIMIT` (2) the gate is recorded `LOCKED` (rank 3, tried last and not
     counted as frontier) so a true dud stops being re-tried across visits. A real
     crossing clears the tally. `record_unusable` (gate.sh exit 2, no icon) locks
     immediately. `GATE_TYPES` is now single-sourced in `gate_route.py` and imported
     by `drive.py` so `has_unexplored`/`pick_gates` can never disagree on what a
     gate is.
   - Validated live (Starstrukk, 2026-06-23): Maeldun´s Weft clicked, did not cross,
     survey logged "did not move the ship -- re-attempting (1/3)", `has-unexplored`
     kept it from burning the stale budget, the gate was deprioritised, and
     `pick_gates` routed onward to **Gate to Sol System**; `gate_fails.json` recorded
     `Kailaasa|maeldunsweft: 1` (one more failure locks it).

5. **galaxy-sized walk cap + label tiebreak** (`survey.sh`, `drive.py`,
   `gate_route.py`). `ENB_SURVEY_MAX` raised 90 -> 300: the galaxy is ~100+ sectors
   and a greedy destination-ranked walk revisits some completed sectors in transit,
   so 90 stopped mid-galaxy; `STALE_MAX` + `has-unexplored` remain the real
   exhaustion stop, the cap is just a runaway backstop. `pick_gates` gained a
   `gate_route.label_names_completed` tiebreak: among equal-rank (mostly unknown-
   frontier) gates, one whose LABEL does not name an already-completed sector sorts
   first -- a soft hint (labels lie, so it is only a tiebreak) biasing toward fresh
   ground over an obvious backtrack when no real edge is recorded for either yet.

Client-only changes throughout -> no server/proxy wire change, no plans/29 CV entry.

## AW-4 -- survey outcome + capture parse/import (2026-06-24)

### Reachability boundary reached (NOT a bug)
The Starstrukk run fully surveyed the region reachable by this ship's gate access:
**8 sectors -- AdrielPrime, Dahin, Equatorial, Freya, Ishuan, Kailaasa, Margesi,
Yokan.** Every *crossable* gate among them leads back into an already-complete
sector; the only unexplored frontier is behind gates this ship cannot traverse:
- `Kailaasa|maeldunsweft` -> LOCKED (wormhole-exit, arrival-only).
- `Kailaasa|gatetosolsystem` -> LOCKED (class-specific gate).
So `survey.sh` correctly converges to "no crossable gate -> HALT" rather than
spinning. "Every sector in the game" is unreachable from this character: most of
the galaxy is gated behind jump gates needing a different ship class / wormhole
device. To extend coverage, move the character through one of those gates (or use
a ship with the access) and re-run `explore-live.sh` -- it resumes from the new
region via the ledgers.

### Capture parse + import: nothing to import (DB already complete)
Parsed all four full-sector captures with `tools/pcap-inventory` and diffed every
captured nav vs the `net7` content DB by name AND position (3k eps, Phase Y rule):
- Kailaasa 12/12, Dahin 12/12, Ishuan 18/18 -- **zero** real gap; the DB has every
  captured nav.
- All apparent "misses" ran to ground as false positives: gate-approach beacons
  named after the destination sector (same name, neighbouring sector, ~50k off);
  and the Ishuan "Nishido/Sundari factory" pair, whose capture coords are
  byte-exact matches to existing DB objects -- the DB merely has the
  Weapons<->Manufacturing / Nishido<->Sundari labels scrambled (a possible DB name
  error, out of nav-marker import scope; correcting from one OCR is unsafe).
- The 177M "Yokan" capture is multi-sector contaminated (3986 flows, first-day file
  pre rotation); its "missing Yokan navs" are verified Ishuan navs. NOT imported --
  attributing them to Yokan would corrupt the DB.
**Conclusion:** these live captures CONFIRM the content DB, they do not extend it.
No `seed_phase_aw_navs.sql` written (would be a no-op on clean sectors, wrong on
contaminated). Differs from Phase Y reconstruct (which had genuine fillable gaps).

## AW-5 -- wreck-detect + undock fixes (2026-06-24)

Hit a hard wreck in **Swooping Eagle** (Sirius), recovered manually, root-caused
two script defects the run exposed and fixed both:

- **Wreck false-negative (rescue.sh `detect`):** `is_wrecked` keys on the amber
  `<<Distress` HUD label, but a fresh wreck auto-opens the Station-Mechanic comm
  dialog ON TOP of that slot, hiding the label -- so `check_wreck()` read ALIVE
  every poll while the hull sat destroyed, spinning the survey on STALL/DEFER.
  Fixed: `detect` is now composite -- WRECKED if `is_wrecked` OR the tow dialog
  (`tow_xy`, "Toggle distress beacon"/"I need a tow") is on screen.
- **Undock could not find the launch control (undock.sh `exit_starbase_xy`):**
  the post-tow hangar shows a YELLOW "Exit Starbase" world-object label over the
  parked ship; clicking it launches. The old detector used grayscale tesseract,
  which read ZERO "starbase" words (yellow text on a bright-blue hull does not
  segment) and also false-matched the docking-panel "STARBASE" header. Replaced
  with a COLOUR detector (yellow mask + 60px neighbour-merged clustering, lower
  55% of frame). Verified live: detector returned (1018,803), the click launched
  us from Sirius Station into Ishuan/Castor (SPEED 0 confirmed). The green+red
  hangar-walk path was never the right mechanism for the tow layout.
- **state.py summary crash:** crashed on the non-ledger JSON in the workdir
  (gate_edges/gate_fails); now skips dicts without a `nodes` key.

**Swooping Eagle marked complete/skipped** (6 visited before wreck, 50 nodes
skipped, reason "sector hostile -- ship wrecks on entry") so the survey will not
route back into the death sector. NOTE: the wreck was towed manually, bypassing
drive.py's `handle_wreck` danger-add, so no in-sector danger zone was recorded --
the sector-complete state is what keeps the survey out.

## AW-6 -- targeted point-to-point nav + goto.py far-gate fix (2026-06-24)

Owner ask: "go from swooping eagle to beta hydri, dont go anywhere unnecessary".
Ship was actually parked in Ishuan; real route was
Ishuan -> Yokan -> Swooping Eagle -> Beta Hydri (entry sector **Glenn**). All
three gates crossed via `gate.sh`, Swooping Eagle transited with NO detour and NO
wreck, arrived Glenn (shield 97%, capture live). Reaching each gate exposed a
`goto.py` defect that is now fixed:

- **goto.py far-gate traversal:** the old loop only HOPPED when the target was
  OUT of scanner range; an in-range-but-far gate got a blind direct warp, which
  the **HUD auto-flip** (nearest-nav re-lock the instant warp engages in a dense
  nav field, documented in `drive._warp_to_impl`) stole every time, so the gate
  distance never shrank (seen on the Capella gate: 78k -> 101k -> 106k, diverging).
  Rewrite: progress is judged by the REAL gate distance `d` (scanner range here is
  effectively unlimited -- a 422k gate still enumerates), the ship walks toward the
  gate via the in-range nav CLOSEST to it (`gdist`), and only commits a direct warp
  once within `HOP_THRESHOLD` (`ENB_GOTO_HOP_K`, default 30k; ran this trip at 35).
  A `STALL`-counter (`ENB_GOTO_STALL`, default 3) detects a hop 2-cycle between the
  gate's two nearest navs and falls back to a direct warp. An earlier per-node
  monotonic-best guard was tried and REVERTED -- it broke the across-sector walk in
  huge sectors (after landing on the closest nav it is excluded, next-closest is
  farther, walk wrongly conceded). Proven on the Capella, Sirius, and Beta Hydri
  gates. Also guarded coordless target/hop navs (`node_xyz` None -> no route /
  never prefer as hop).

- **CAPTURE MISLABELLED (my error, disclosed):** the crossings were driven with
  `gate.sh "<gate-name>"` (single arg) instead of the real signature
  `gate.sh <from-sector> <gate-name> <to-sector>`. gate.sh's rotate guard only
  fires when `$to` or `$gatename` (positional 2/3) is set, so with the name landing
  in `$from` the pcap NEVER rotated. The entire Ishuan->Glenn traversal was captured
  into ONE mislabelled file (`Ishuan__20260624T142306Z.pcap`, ~1.05 MB, finalized),
  correlatable to the route via `actions.log` UTC timestamps. A clean ongoing
  capture was started on arrival (`Glenn__...163815Z.pcap`, confirmed growing).
  LESSON: always call `gate.sh <from> <gate-name> <to>` so each crossing rotates.

## AW-7 -- Glenn hard-hang, relogin position-revert, undock map-close fix (2026-06-24)

Continuing the Ishuan->Glenn survey. Three things happened:

- **Glenn scan-popup then hard-hang.** Glenn (Beta Hydri) threw a "YOU ARE BEING
  SCANNED" modal that blocked the screen-only driver; OCR'd it + clicked Done
  (Glenn went 2->8 navs after). Protocol saved to memory `survey-scan-popup`
  (ALWAYS check between loops / when stuck). Later Glenn genuinely WINE-froze at
  19/57 (frozen frame 8s+, client.exe pegged ~1029% redrawing one frame, no modal)
  -> survey exit 42. Owner chose "kill and ill relaunch"; killed client.exe +
  Net7Proxy.exe + wineserver by PID.

- **Relogin reverts position to home station.** After relaunch the character did
  NOT resume at Glenn 19/57 -- login dropped it **DOCKED at Somerset Station, New
  Edinburgh (Tau Ceti)** (home StartSector; consistent with the fresh-char sector
  routing trap memory: login routes through home). So Glenn's partial run is
  stranded server-side, but **New Edinburgh is a fresh unsurveyed sector** -- doing
  it instead, no loss.

- **undock.sh now closes the map first (owner: "close the fuckin map so you can
  see" -> "update the script").** The survey's read-sector leaves the
  SYSTEM/SECTOR/STARBASE map open; while docked that panel overlays the hangar and
  BLINDS undock.sh's ship/label detectors. Added `close_map()` + `map_open()` to
  `undock.sh`: OCRs for the docked map's "STARBASE"/"GALAXY MAP" label and clicks
  the (132,870) toggle to close it (idempotent-guarded -- only clicks when an open
  map is actually detected), verified before the ship sweep. Runs right after the
  window-activate, before exit_starbase_xy + find_ship.

- **Screen-only undock from a station concourse is unreliable; owner launched
  manually.** Even with the map closed, undock.sh could not board: the relogin
  dropped the avatar in the STATION CONCOURSE (not the hangar catwalk), where there
  is **no launch button** -- the 4 bottom-left concourse icons are Inventory, the
  sector map (SYSTEM/SECTOR/STARBASE), the galaxy starmap, and a window toggle, none
  of which launch. EnB undock from the concourse is walk-the-avatar-to-the-ship, and
  the green+red nav-light detector never fired (the parked ship shows a cyan engine
  glow / a single green light from most angles, not a saturated green+red pair).
  `exit_starbase_xy` also mis-fired a yellow false-positive at (660,523). Owner
  launched the ship into space manually ("ok i got you out"). KNOWN GAP: screen-only
  undock works from the hangar-catwalk layout but NOT the concourse layout; not
  solved this session.

- **Survey resumed in space on New Edinburgh.** Closed a stray avatar-status panel
  (Escape opened the Quit Menu; second Escape dismissed it), relaunched
  `explore-live.sh`: preflight OK (window/proxy LIVE/capture), `=== driving
  NewEdinburgh (crossing #1) ===`. Map-close fix + manual launch both confirmed
  working.

- **SOLVED: hangar/concourse undock = DOUBLE-CLICK the ship hull, not single.**
  Later in the run the survey wrecked at the Gate to Beta Hydri (a recorded death
  zone), towed + respawned DOCKED, and undock.sh's single-clicks failed again
  (300s timeout aborted the whole survey). Got back into space manually and found
  the mechanic: a SINGLE click on the ship hull only SELECTS/targets it (stays
  "SPEED ?" docked); a DOUBLE-click boards and launches it ("SPEED 0" in space).
  Proven live: `xdotool click --repeat 2 --delay 120 1` on the hull at abs
  (3343,640) -> read-speed.sh returned SPEED 0. Encoded in `undock.sh`: new
  `board_click()` helper (win-rel -> abs via win_abs, fast native double-click);
  replaced BOTH single-click board sites (Exit Starbase label ~L155 and the
  APPROACH hull-board ~L350) with it. This is the fix for the recurring
  concourse/hangar undock failure. The green+red nav-light find_ship detector is
  still the weak link (parked ship reads cyan/single-green from most angles), but
  once it does center the ship, the double-click now actually launches.

- **Gate to Beta Hydri System = death zone (skip, warp-past-only).** Recorded:
  warping INTO it wrecks the ship. New Edinburgh reached 18/22 resolved (13
  visited + 5 skipped) before the wreck. Survey process tree died with the abort;
  restarting to finish the remaining ~4 navs and continue crossing gates.

- **NewEdinburgh COMPLETE 17/22 (5 skipped); gate-leave was mis-routing to the
  death zone -- fixed pick_gates to deprioritise SKIPPED gates.** On completion
  the gate-leave picked "Gate to Beta Hydri System" first because Glenn/Beta Hydri
  is in-progress 19/57 (a frontier), but that gate is `skipped:True` (death zone)
  and approaching it would wreck the ship repeatedly until the 2-wreck egress
  budget aborts -- never trying the safe gate. `pick_gates` had NO death-zone
  awareness (its "deprioritise" only covered soft-stuck gates). Added a DOMINANT
  sort key in `drive.py pick_gates`: a gate node with `skipped:True` sorts AFTER
  every non-skipped gate regardless of frontier rank (a proven-reachable backtrack
  beats a death-zone gate). NewEdinburgh has a safe `Sector Gate to Inverness`
  (`visited:True`, leads to a FRESH unsurveyed sector -- no Inverness ledger) and a
  `Gate to High Earth` (class-specific). Killed the in-flight survey (was already
  warping at the old code toward Beta Hydri; sector already complete so no loss),
  restarted explore-live.sh -> gate-leave now heads to "Sector Gate to Inverness".
  General fix, no hardcoded sector names; benefits every future death-zone gate.

- **Crossed to Inverness (fresh, 24 navs); surveyed to 9/24 then WRECKED + halted at
  the concourse-undock gap.** The Inverness gate crossing worked; drove to 9/24
  (auto-found onward gates to New Edinburgh + Arduinne). Shield dropped 97->54% near
  Nav Sunside 1 (hostiles), ship destroyed -> recorded Nav Sunside 1 as a death zone,
  requested tow. **The tow dropped the avatar in the Somerled Station CONCOURSE**
  (chat: "Welcome to InfinitiCorp's Somerled Station" -- Somerled is a NewEdinburgh
  station, so the tow crossed sectors back). undock.sh "could not undock after tow";
  run-sector mis-classified the failed undock as a hang (exit 42) and HALTED
  (manual-client, no auto-relaunch). **Client is ALIVE, not frozen** (frozen-frame
  check: frames differ; SPEED ? = docked).
  - **The double-click undock fix is NOT the blocker here -- the CONCOURSE layout is.**
    The double-click board works from the HANGAR-catwalk layout (ship hull visible).
    A wreck-tow drops you in the CONCOURSE (avatar standing in an empty hall, NO ship
    hull on screen), so find_ship has nothing to detect and board_click has nothing to
    double-click. This is the same KNOWN GAP from AW-7 (concourse undock unsolved in
    screen-only mode), now hit via the wreck-tow path. Not solved.
  - **State preserved / resume path:** Inverness ledger persists at 9/24 (Nav Sunside 1
    skipped as death zone). To resume: operator undocks the ship into space, then
    re-run `explore-live.sh` -- it auto-detects the current sector from the map title
    and routes (if towed back to NewEdinburgh-complete, gate-leave -> Inverness via the
    pick_gates fix; the Inverness ledger continues where it left off). Left the client
    docked + alive for the operator; did NOT kill it (not a hang) and did NOT blind-click
    the concourse (no reliable screen-only board from this layout).
  - **RESOLVED via operator-assisted undock; the walk+double-click DOES work once the
    ship is visible.** Owner manually navigated the avatar out of the concourse hallway
    into the HANGAR (where the ship + yellow "Exit Starbase" label become visible) and
    said "just walk there, right-click hold, at screen center." Did exactly that:
    `walk_forward` (RMB-hold 7s at window centre) closed the distance, then detected the
    yellow Exit-Starbase label by colour (median ~(818,480), x-span 590-1046) and
    DOUBLE-clicked it -> SPEED 0, in space. So the board mechanic (walk + double-click
    the label/hull) is sound; the ONLY unsolved piece is auto-navigating from the
    post-tow CONCOURSE HALLWAY into the hangar so the ship comes on screen. undock.sh
    already has the label detector + walk + double-click; a future enhancement is the
    concourse->hangar walk-out (the owner did that step by hand this time).
  - **DONE -- concourse->hangar walk-out is now autonomous (owner: "update scripts as
    needed to make it work on your own next time", 2026-06-24).** Replaced undock.sh's
    static 3-screenshot Exit-Starbase check with a bounded WALK-AND-SCAN loop
    (`CONCOURSE_MAX=8`, `CONCOURSE_SECS=3`): each round it (1) closes a stray map,
    (2) scans for the yellow Exit-Starbase label via the colour detector and, if found,
    double-clicks (`board_click`) to launch; (3) if instead the green+red ship PAIR is
    visible it breaks to the existing hangar rotate/approach sweep (catwalk layout);
    (4) otherwise -- empty hallway -- it `walk_forward`s one short RMB-hold burst into
    the hangar (turning Right 6 every other round to re-aim) and rescans. This is
    exactly the manual sequence the owner walked me through, now looped + bounded so a
    genuinely empty layout still falls through instead of walking forever. Takes effect
    on the next wreck/tow with no restart (undock.sh is spawned fresh per wreck).
    Still screen-read-only. Real-client confirmation that the full autonomous path lands
    in space is pending the next live wreck-tow (the manual halves are each proven).
  - **Resumed:** restarted explore-live.sh. Tow had returned us to NewEdinburgh
    (complete), so gate-leave fired; with the death-zone gate sorted last it now heads
    to "Gate to High Earth" (unknown frontier) -- Inverness is now a known-incomplete
    destination (9/24) so it ranks below the fresh High Earth frontier. If High Earth's
    class-specific gate is locked it falls through to Inverness. Survey driving again.
  - **CORRECTION -- the "DONE autonomous" claim above was PREMATURE; the WALK-AND-SCAN
    loop was a NO-OP (bash function-ordering bug). Carpenter wreck-tow, 2026-06-24.** The
    concourse loop was inserted ABOVE the definitions of `find_ship` / `walk_forward` /
    `turn` in undock.sh, and bash needs a function defined before it is called -- so every
    one of those calls errored `command not found` and the loop never walked or detected
    anything. The post-tow undock fell straight through to the hangar sweep and stalled,
    and the survey mis-reported it as a frozen-frame hang (exit 42) when the client was
    alive + docked. FIX: moved the whole concourse loop to BELOW the locomotion-helper
    definitions (after `aim_camera_up "$CAM_UP"`, before the ACQUIRE sweep) and added a
    guard comment; `bash -n` clean; the loop now actually executes (`walk_forward` walks,
    `find_ship` runs, confirmed live on the recovery).
  - **Second fix: `find_ship` rejected a boardable ship because of a DECOY green light.**
    Diagnosed on the same Carpenter tow: the clean initial view had a strong red nav light
    (94px @ x=733) AND green (320px @ x=272), but the green centroid was a far-LEFT station
    decoy light, so the global red-vs-green span was 461px > the 450px wingtip cap and the
    pair was REJECTED -- the undock walked right past a ship that filled the frame. FIX:
    `find_ship` now buckets each colour into compact 40px BLOBS and pairs the closest
    red-blob/green-blob that fits the wingtip geometry (preferring the pair nearest frame
    centre), so a far decoy blob is never chosen. Re-validated on the recovery captures:
    the clean view the old code rejected now detects, and the decoy-green corridor/crystal
    frames still return no false positive.
  - **Carpenter client recovered without a kill.** The still-docked Carpenter client (alive,
    NOT hung) was hand-driven back into the hangar (rotate-scan to find the red nav light,
    centre + walk_forward, then DOUBLE-click the hull at ~640,600) -> SPEED 0, in space.
    No client/proxy process was killed. The two undock.sh fixes above take effect on the
    next wreck/tow with no restart (undock.sh is spawned fresh per wreck). Real-client
    confirmation that the FULL autonomous concourse->board path lands in space is still
    pending the next live wreck-tow -- the function-ordering + detection halves are now
    each proven, but the end-to-end loop has not yet auto-boarded unaided.
  - **LIVE TEST (same session, ~40 min later): the fixed undock STILL could not board
    the concourse-hallway layout -- the autonomous concourse->ship navigation is the
    real unsolved limitation, NOT the function-ordering/detection bugs (those are fixed).
    Carpenter wreck #2, 2026-06-24.** The survey drove Carpenter to 6/23, took real
    combat damage (shield 97%->0 was genuine fire, not a washout: best-of-5 maxed at 16%),
    and WRECKED at Yamuna`s Weft -> tow -> docked. handle_wreck spawned the fixed
    undock.sh; this time the loop DID execute (walk_forward walked, find_ship ran), but it
    spent its whole budget wandering the concourse without the ship ever coming into view,
    and aborted "could not undock after tow" -> run-sector exit 42 -> survey HALT. A
    post-wreck NPC dialog ("that was one heck of an explosion! are you alright over there?")
    was also overlaying the centre for part of the run -- undock.sh has NO handling for a
    blocking conversation/comm dialog, so its centre walk/board clicks may have been eaten.
    Manual screen-only recovery ALSO struggled: the concourse is maze-like and blind
    walk_forward bursts wall-stick; without 3D depth perception from a screenshot I cannot
    reliably navigate a corridor to the hangar either. NET: for a docked-but-alive client
    in the CONCOURSE-HALLWAY layout, neither the autonomous loop nor my manual screen-only
    driving is reliable -- this is the case to hand to the owner (walk the avatar into the
    hangar, or relaunch). TODO for undock.sh: (a) detect + dismiss a blocking NPC/comm
    dialog (find its close X / a safe dismiss) before the walk-scan each round; (b) the
    concourse->hangar pathfinding needs a better signal than blind forward bursts.
  - **Carpenter is a DEATH-ZONE sector for this ship** -- two skipped death zones already
    (Sector Gate to Slayton, Yamuna`s Weft) and it wrecked the ship at 6/23. Re-grinding
    it just re-wrecks; once recovered, the better route is to gate OUT to a safer sector
    rather than keep surveying Carpenter with this hull.
