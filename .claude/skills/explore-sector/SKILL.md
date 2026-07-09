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

This is a fully AUTONOMOUS loop driven through the injected **enbmod Lua command
channel** -- no screenshots, no OCR, no synthetic mouse/keyboard. `drive_lua.py`
reads the sector id, the live nav list, and distances straight from the game's
`enb.*` API, warps with `enb.warp()`, crosses gates with `enb.gate()`, and records
every visit in the same ledger the survey has always used. You (the agent)
normally do not step in at all: launch it and read the log.

## Quick start: "explore live" (the one command)

When the owner says **"explore live"**, run ONE command:

```bash
bash .claude/skills/explore-sector/scripts/explore-live.sh
```

This is the unattended-safe path against a client the OWNER launched. It does NOT
start our docker stack and never launches/kills the client. Preconditions: the
live client + `Net7Proxy.exe` are running under WINE **and the character is
already in space** (or at least logged into the world) -- the Lua channel only
answers once `enb.self()` is a table, so the survey confirms in-game before it
drives anything. Get there first with the `login-to-client` skill.

`explore-live.sh` is a thin, reliable wrapper around `drive_lua.py`:

1. **Loads config from a gitignored `.env`** in the skill folder (next to this
   file), so there are NO long inline env strings to remember. Copy `.env.example`
   to `.env` once and fill in the character name + a persistent workdir outside
   the repo. Every threshold knob keeps its baked-in default.
2. **Forces the live-mode invariants**: `ENB_EXPLORE_MANUAL_CLIENT=1` (never touch
   our stack, halt-don't-relaunch on a hang), `ENB_EXPLORE_PROXY_NAME=Net7Proxy.exe`
   and `ENB_PCAP=1` (capture the WINE proxy, one file per sector). If it detects the
   dockerized `net7proxy` instead of the WINE proxy, it flips to a LOCAL run and
   forces `ENB_PCAP=0` (owner rule: never pcap a local freya run).
3. **Preflights and FAILS LOUD (exit 2) before driving anything** if a precondition
   that has silently broken runs before is missing: no `.env`, no character name /
   workdir, workdir not writable, X unreachable, no `client.exe` window, the proxy
   not running, the capture worker not installed (with `ENB_PCAP=1`), or **the
   character not in-game** (`08-wait-ingame.sh` from the login skill). Each failure
   prints the exact fix.

Then it `exec`s `drive_lua.py`, which detects the sector, drives it, crosses a
gate, and repeats. **Resume** a survey by re-running the same command: completed
sectors are marked done in the workdir ledgers and skipped, and a half-finished
sector picks up from its persisted visited rows.

## How it drives: the enbmod Lua channel (no OCR)

Everything the old screen driver read off pixels now comes from the game runtime
through `enbmod.cmd`/`enbmod.log` (the same channel the `run-lua-client-command`
skill documents). `drive_lua.py`'s `lua()` writes one Lua line and reads the
`[run]` reply. The mapping from the retired OCR path:

| what we need | Lua call | (old OCR way) |
|---|---|---|
| current sector id | `enb.sector()` -> int | map-title OCR |
| gate crossed? | `enb.sector()` id changed | map-title OCR |
| nav list | `enb.navs()` -> `{gid,name,dist,class}` | W/D/C nav-cycle OCR |
| arrived at a nav | `nav.dist <= VISIT_K` | speed-readout OCR |
| target + distance | `enb.request_target(gid)` + `enb.dist()` | target-panel OCR |
| select + warp | `enb.request_target(gid)` + `enb.warp()` | click the warp orb |
| stop warp | `enb.warp_stop()` | (n/a) |
| transit a gate | `enb.request_target(gid)` + `enb.gate()` | click the gate icon |
| docked? / undock | `enb.state()` / `enb.undock()` | warp-cluster-blank OCR |
| reactor energy | `enb.vitals().energy` | (n/a) |

Ship x/y/z from `enb.self()` read 0 in space (uncalibrated flat-offset reads), so
position and speed are derived from **nav-range deltas**: `ship_speed()` samples
`enb.navs()` distances ~1s apart and takes the max closure rate (warp is 2000+
u/s, so it clears the moving threshold well off-axis). This is the OCR-free
equivalent of the old speed readout.

## Hard rules (read first)

- **Prerequisite: already in space.** Run the `login-to-client` skill until it
  prints `INGAME inspace=true state=space`. This skill does not log in;
  `explore-live.sh` only CONFIRMS in-game (and, at startup, undocks if the
  character loaded docked, via `enb.undock()`).
- **The Lua channel IS the liveness signal.** enbmod's command poll is clocked off
  the client's message pump, so a HARD-HUNG client stops answering `enbmod.cmd`
  entirely. A scene LOAD also silences the channel, but only briefly (a gate
  transit's load screen clears well under 90s); a wedge never clears. So the driver
  declares the client hung only after the channel has been continuously silent for
  `ENB_LUA_HANG_SECS` (default 150s, above the worst-case load) AND a direct
  `return 1+1` liveness probe confirms it. TIME is the distinguisher -- never treat
  a brief mid-load silence as a wedge. (This is also why heavy per-tick
  instrumentation freezes a live client: a wedged pump answers nothing. Keep the
  channel to discrete request/reply calls; do not install `enb.on_tick` probes.)
- **Hang recovery / relogin.** On a confirmed hang `drive_lua.py` recovers the same
  way `run-sector.sh` used to around the screen driver:
  - **Manual/live mode (`ENB_EXPLORE_MANUAL_CLIENT=1`, the default under
    `explore-live.sh`):** we do NOT own the client, so we HALT (exit `42`) and ask
    the operator to relaunch the client + proxy and re-run. The ledger persists, so
    the re-run resumes.
  - **Local stack:** kill + relogin through the `login-to-client` skill
    (`login.sh` then poll `08-wait-ingame.sh`), reset the hang clock, and re-enter
    the SAME hop -- `get_sector_id()` + `survey_sector()` resume from the persisted
    ledger, so a hang costs no hop. Bounded by `ENB_MAX_RELOGIN` (default 4).
- **Refuse Gravity Well sectors.** A gravity well terminates warp mid-flight
  (server `PlayerClass.cpp:1806`, `sector_objects.type==41` OT_GWELL) and we cannot
  auto-route out, so the ship drifts off course. `drive_lua.py` calls
  `gravity_wells.py` on every sector entry and exits `3` if the sector is a well.
  Check any sector with `python3 scripts/gravity_wells.py <S>`. Refused explorable
  sectors: **ABB, ABG, Achenar, AragothPrime, BlackbeardsWake, Lagarto, MarsGamma,
  Menorb, Paramis**.
- **Target order is HYBRID: farthest-first, then nearest after 20%.** While fewer
  than `NEAR_SWITCH` (20%) of the sector's ledger nodes are done, target the
  FARTHEST unvisited nav -- one long crossing bags every nav that comes within
  `VISIT_K` of the warp line for FREE (en-route pickup). Once `>=20%` are done,
  switch to NEAREST-unvisited (late farthest-first degenerates into an end-to-end
  zigzag). A nav counts as visited within `VISIT_K` (8k), and every poll during a
  warp marks any nav that comes within `VISIT_K` of the ship, so a fly-by is never
  missed.
- **`enb.warp()` is a TOGGLE -- never fire it while already at warp speed.** Firing
  it mid-warp CANCELS the leg (the Lua equivalent of the old only-click-the-orb-at-
  speed-0 rule). `ensure_not_warping()` calls `warp_stop()` and waits for the ship
  to slow before any engage, and a stall is only treated as a warp drop-out when
  `ship_speed()` confirms the ship is genuinely slow -- a multi-hop path can fly
  AWAY from the final target for a while without having dropped warp.
- **Motion is the only proof of engage.** `enb.warp()` returns true on both engage
  AND terminate, and an engage can silently no-op on a drained reactor (engage eats
  ~half the energy pool up front). `warp_engage()` therefore tops up energy
  (`wait_energy`), fires, and VERIFIES closure via `ship_speed()` before believing
  the warp took; retries demand a near-full recharge.
- **Flyby memory (`SEEN`) + observed gates (`GATES_SEEN`) are load-bearing.**
  Beltway-style sectors only list their mid-sector navs DURING a crossing; by the
  time a leg ends at a gate those navs are out of scanner range again. A gid stays
  targetable after it leaves range, so every nav the client ever lists this sector
  is remembered (`SEEN`) and can be warped back to, and every gate seen mid-sweep
  is remembered (`GATES_SEEN`) so `choose_gate` can still pick a 200k-out onward
  gate that is not in range at the parked spot. Without these the run false-concedes
  a big sector at a few nodes. Both are cleared per sector.
- **Relocate before conceding; skip only what is truly unreachable.** When nothing
  is in range but unvisited ledger nodes remain, `survey_sector` relocates to a far
  present nav (bounded by `RELOC_MAX`, budget resets whenever a relocation drags a
  fresh nav into range) to change what is in scanner range. Anchor choice is
  CORRIDOR-SCORED: prefer the nav whose flight line from the estimated position
  passes the most unvisited nodes. Only when the budget is spent is the remainder
  skipped (`state.py skip`, with a reason), never chased forever.
- **Duplicate-name navs are keyed by gid, not name.** Sectors carry duplicate nav
  names (ABA has three distinct 'Mining Station' objects). A visit consumes exactly
  one ledger row per physical object: `record_visit` dedups by gid (`MARKED_GIDS`,
  reseeded from the ledger's `visited_gid` stamps on restart), and `state.py visit`
  stamps the gid on the first unvisited row of that name. Keying by name alone
  re-marks one row forever while its siblings starve.
- **Completion is OUR record, not the game's.** A sector is DONE only when our
  ledger shows every node visited (or skipped as unreachable) and the action log
  records it. A player having explored it before does NOT count -- if we did not
  record it, it is not complete. `state.py complete` refuses (exit 1) if any node
  is unresolved.
- **Don't move/resize the WINE window.** (No screen input is used, but the login
  skill's `client_win` reads its geometry; moving it desyncs WINE's rect.)

## The scripts

| script | what it does |
|---|---|
| `scripts/explore-live.sh [drive args]` | **The entry point.** Loads the gitignored `.env`, forces live-mode invariants (manual client, WINE proxy, pcap), detects LIVE-vs-LOCAL by which proxy is up, preflights every precondition (`.env`, char name, workdir, X, client window, proxy, capture worker, **in-game**) and exits 2 with the fix if one fails, then `exec`s `drive_lua.py`. Extra args pass through. |
| `scripts/drive_lua.py [drive args]` | **The driver.** The whole survey over the enbmod Lua channel: detect sector (`enb.sector()`), refuse gravity wells, undock if docked, drive every reachable nav (hybrid farthest/nearest, flyby memory, corridor-scored relocation), cross a gate to a fresh sector, repeat up to `ENB_MAX_SECTORS`. Hang-detects the channel and relogins (local) or halts (manual). Resumable via the ledger. |
| `scripts/navdata.py` | `sectors` / `list <S>` / `identify <label>...` / `match <S> <ocr>` -- authoritative node lists from `docs/sectors/json/<S>.jsonl`. Its `norm()` name-normaliser is imported by the driver, which joins live nav names to ledger nodes through it (+ a per-sector difflib alias for spelling variants). |
| `scripts/state.py` | the per-sector progress ledger (`init/status/remaining/visit/skip/skipped/complete/summary`). Runtime output is per-playthrough progress, not source. |
| `scripts/gravity_wells.py [S]` | authoritative gravity-well blocklist (game logic: `sector_objects.type==41`). No arg lists all; `<S>` exits non-zero if that sector is refused. `drive_lua.py` shells out to it per sector. |
| `scripts/logaction.sh <S> <action> [detail]` | append a timestamped line to the gitignored action log. |
| `scripts/pcap.sh {start\|rotate\|ensure\|stop\|status} [S]` | per-sector packet capture control (best-effort; self-gates on `ENB_PCAP=0` and worker availability). `drive_lua.py` calls `ensure` at each sector entry and `stop` at the end. |
| `scripts/pcap-install.sh` | ONE-TIME `sudo` setup: installs the root-owned capture worker + a scoped NOPASSWD rule so capture runs unattended. |

Runtime output (ledgers + `actions.log` + `captures/`) lives under
`ENB_EXPLORE_WORKDIR` (default the skill's `state/` dir) and is **gitignored** --
it is per-playthrough progress, not source.

## Work directory (`ENB_EXPLORE_WORKDIR`)

Everything persistent for a survey lives under ONE root: the per-sector ledgers
(`<Sector>.json`, the finished-vs-remaining truth), the timestamped `actions.log`,
and the pcaps (`captures/`). Default is the skill's own `state/` dir; set
`ENB_EXPLORE_WORKDIR=/path` (outside the repo) to park a whole survey elsewhere.
It is inherited by `drive_lua.py` -> `state.py`/`logaction.sh`/`pcap.sh`, so set it
once. **Resume** by pointing at the same folder again -- completed sectors are
detected from the ledgers there and skipped. (`ENB_PCAP_DIR` overrides just the
pcap location.)

## Per-sector packet capture (one .pcap per sector)

The survey records the proxy traffic into **one capture file per sector**, each
started BEFORE we enter the sector so it includes the entry handshake. Wired
automatically, BEST-EFFORT -- it no-ops if not set up, never blocking the survey.

- **Boundaries:** `drive_lua.py` calls `pcap.sh ensure <sector>` at each sector
  entry (relabels a placeholder or starts a fresh file) and `pcap.sh stop` at the
  end. For the very first sector, run `login-to-client` with `ENB_PCAP=1` so the
  capture starts at char-select.
- **Files:** `<workdir>/captures/<Sector>__<UTC>.pcap` (gitignored). tcpdump runs
  in the proxy's netns, so each file has the cleartext proxy<->server UDP leg plus
  the encrypted client<->proxy leg.
- **Correlation:** pcap frame timestamps and `actions.log` are both UTC, and a
  `pcap-*` marker is written to `actions.log` at every boundary.
- **One-time setup (sudo once):** `sudo bash scripts/pcap-install.sh` installs the
  root-owned worker + a SCOPED NOPASSWD rule for that one binary. After that the
  survey captures unattended.
- **Which proxy (`ENB_EXPLORE_PROXY_NAME`):** unset / `freya` captures the
  dockerized FreyaProxy container; a host process name (e.g. `Net7Proxy.exe`)
  captures the real proxy under WINE. `explore-live.sh` sets this for you and, on a
  detected LOCAL run, disables capture entirely.

## Crossing to a new sector (gates)

Gate transit is handled by `cross_gate()`:

1. `choose_gate` picks the nearest gate (from the union of in-range gates and every
   gate seen mid-sweep) whose DESTINATION is neither surveyed this run nor a
   completed ledger -- so the survey does not immediately gate back where it came
   from. Gate destinations resolve via the committed sid catalog (`enb.sector()`
   ids <-> `docs/sectors/sector-ids.md`).
2. Warp to the gate and let the warp DROP OUT (a `enb.gate()` fired mid-warp at 8k
   is outside activation range and silently ignored).
3. Fire `enb.request_target(gid)` + `enb.gate()` and poll `enb.sector()` for the id
   to flip, retrying a few times (the first fire can land while still decelerating).
4. If the new id was already surveyed this run, stop (v1 has no multi-gate route
   planner). Otherwise continue in the new sector.

A gate the character CANNOT use (class-/faction-locked) simply never flips the
sector id; `cross_gate` returns None and the run stops rather than spinning.

## Manual client (`ENB_EXPLORE_MANUAL_CLIENT=1`)

Set when YOU launch and own the client + proxy (a real client paired with
`Net7Proxy.exe` under WINE). `explore-live.sh` forces it on. The survey then never
launches or relaunches the client: on a hard hang `drive_lua.py` HALTS (exit 42)
and asks you to relaunch + re-run (the ledger persists) instead of bouncing your
session. On a LOCAL stack (`=0`) the same hang triggers an auto kill + relogin +
resume.

## Gotchas

- **"enbmod channel not answering"** at startup -> the game is not running / mods
  are off / the character is not in-game. Run `login-to-client` first;
  `explore-live.sh` preflights this, but `drive_lua.py` run bare will exit on it.
- **No `[run]` output for a call** usually means the called function returned zero
  values, or (if it persists past `ENB_LUA_HANG_SECS`) the client is wedged. The
  DLL writes CRLF, so replies are `\r`-stripped before any compare.
- **Live vs ledger name spelling.** The live DB and the ledger spell a few navs
  differently ("Infiniti Campus" vs "Infinity Campus"). `build_alias` joins them
  per sector by difflib ratio (>=0.84) so those navs stay pickable; visits/skips
  always carry the ledger spelling (`ledger_name`) or `state.py` lands nowhere.
- Keep `actions.log` append-only; it is the audit trail the owner asked for.

## Related skills

- `login-to-client` -- get in-game first (this skill reuses its `lib.sh` for
  `client_win` and its `08-wait-ingame.sh` in-game check + relogin path).
- `run-lua-client-command` / `read-lua-client-logs` -- the lower-level enbmod
  command-channel helpers this driver is built on.
