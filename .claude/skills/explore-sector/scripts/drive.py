#!/usr/bin/env python3
# SPDX-License-Identifier: MIT
# Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
# License: LICENSES/Freya
#
# drive.py <Sector> [--max-rounds N] -- autonomously fly a sector to completion
# using the screen-read + nav-target + warp method (METHOD v3, plan 53). No mods,
# no injection: every action is a screenshot OCR, an in-game nav keypress, or a mouse
# CLICK on the warp orb.
#
# NAV TARGETING IS BY KEY (owner-directed 2026-06-21). The game's own target-filter
# keys drive the nav cycle -- the old ">>"/"<<" target-arrow BUTTONS are gone:
#   * W -- lock the target filter to NAVS and select the NEAREST nav. Idempotent: each
#          press re-anchors to the nearest, so it also bootstraps an empty selection
#          (no map-click needed to seed the cycle any more).
#   * D -- advance to the next-FARTHER nav (the cycle is distance-ordered).
#   * C -- step back to the next-closer nav.
# So W, WD, WDD, ... select the 1st, 2nd, 3rd ... nearest nav; WC selects the farthest.
# Warp is still engaged by CLICKING the warp orb (warp.sh), NOT the Q key -- Q is
# dropped when the WINE window lacks keyboard focus. The nav keys are sent after a
# windowactivate, which is the focus path that proved reliable here.
#
# READING THE CYCLE IS BATCHED (fast rapid-fire capture + one OCR). enum_fast.py walks
# W then D..D grabbing ONLY the target-name box off the root window via Xlib XGetImage
# (~0.6ms/grab vs ~355ms for an `import` spawn), then OCRs all the frames in a SINGLE
# tesseract pass over a stacked montage. One enumerate yields the whole in-range nav
# list in distance order; we dedup it with sector knowledge and pick the target.
#
# ALWAYS BE WARPING (owner's hard rule). The ship should be in warp essentially all
# the time; the only acceptable dead time is the few seconds to pick the next target
# and the ~5s warp engage. The nearest UNVISITED nav (first non-done in the W/D order)
# is the target, so we warp toward it immediately and let the en-route pickup bag the
# rest. The gap from "speed 000" to "next warp pressed" is timed/logged (warp-gap).
#
# Per round:
#   1. Batch-enumerate the in-range nav cycle (W + D-walk + montage OCR) in distance
#      order, with each nav's panel DISTANCE read in the same walk; dedup to the
#      authoritative node names.
#   2. Any enumerated nav already within ENROUTE_K (12k) is recorded visited right away
#      -- no warp trip for a node we are already sitting next to. This is what keeps the
#      ship moving instead of sitting minutes re-targeting near/unreachable navs.
#   3. Pick the target non-death-zone unvisited nav: FARTHEST while <50% of the sector
#      is done (long crossings bag many navs en-route for free), then NEAREST once >=50%
#      is done (scattered remainder, avoids the late-run zigzag). Select it (W then D to
#      it, verifying by NAME -- never by click-count, which drifts). En-route nodes
#      within ENROUTE_K are also bagged during the warp poll loop.
#   4. Warp to the target (CLICK the warp orb), poll the panel distance until <=2k;
#      re-warp if speed drops to 000 while still far.
#   5. If nothing unvisited is in range, every nav within scanner range is visited ->
#      the sector is DONE. Skip whatever is left (hidden navs that never revealed, or
#      shown navs that never came into range) and stop. The MAP is consulted only as a
#      completion oracle / SEVERE-stuck fallback, not for ordinary navigation.
#
# All visits are timestamped to the wall-clock second via logaction.sh. The ledger
# (state.py) is the resumable source of truth; re-running continues where it left off.
import math
import os
import subprocess
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
# Survey work root: per-sector ledgers, the action log, pcaps and stuck shots all live
# here so a whole survey is one folder (resume by pointing WORKDIR at it again). Mirror
# of state.py's STATE_DIR -- keep the two in sync. Override with ENB_EXPLORE_WORKDIR.
WORKDIR = os.environ.get("ENB_EXPLORE_WORKDIR") or os.path.join(HERE, "..", "state")
# Every transient image we OCR (or dump for the operator to eyeball) goes under a
# nested scratch/ so the work root holds only durable artifacts (ledgers, pcaps,
# logs) and the throwaway PNGs are one `rm -rf scratch` away. Owner, 2026-06-22.
SCRATCH = os.path.join(WORKDIR, "scratch")
sys.path.insert(0, HERE)
import navdata  # noqa: E402
import state    # noqa: E402
import gravity_wells  # noqa: E402
import gate_route  # noqa: E402

# In-game nav-target keybinds (owner-directed): W = lock filter to Navs + nearest,
# D = next-farther nav, C = next-closer nav. The old ">>"/"<<" target-arrow buttons
# are gone -- the cycle is walked entirely by these keys now.
SEED_KEY = os.environ.get("ENB_NAV_SEED", "w")
NEXT_KEY = os.environ.get("ENB_NAV_NEXT", "d")
PREV_KEY = os.environ.get("ENB_NAV_PREV", "c")
# Batch-enumeration capture tuning. enum_fast.py BURST-grabs the name+dist boxes
# ENUM_BURST times spaced ENUM_GAP apart per slot (no blocking per-key settle), then
# advances. The burst WINDOW (burst x gap) must span the HUD repaint (~0.13s) so the
# next nav has drawn before the next D fires; oversampling + consecutive-dedup then
# folds any stale frame away. burst*gap ~= 0.16s/slot, faster than the old
# settle-then-grab-once which stalled re-grabbing on slow repaints.
ENUM_GAP = os.environ.get("ENB_ENUM_GAP", "0.02")        # seconds between burst grabs
ENUM_BURST = os.environ.get("ENB_ENUM_BURST", "8")       # name+dist grabs per slot
VISIT_K = float(os.environ.get("ENB_VISIT_K", "8.0"))
# En-route pickup threshold (owner, 2026-06-22: "consider visited already because it's
# within 8k"). While in warp transit the ship blows PAST other navs at high speed, so a
# poll may only ever read a passing nav at several k -- waiting for the 2k target
# threshold misses navs we clearly flew through. So a NON-TARGET nav seen within
# ENROUTE_K during the warp poll loop -- OR read within ENROUTE_K straight off the batch
# enumeration -- is credited as visited (no warp trip needed for one already this close).
# The TARGET still has to be reached at VISIT_K / MINAPPROACH_K; this band is only for
# opportunistic already-near nodes. Owner 2026-06-23: widened to 1.5x the old 8k -- the
# range-based discovery missed too many "?" navs the ship clearly flew past, so the
# en-route discover/bag radius is now 12k (independent of VISIT_K, which stays 8k).
ENROUTE_K = float(os.environ.get("ENB_ENROUTE_K", "12.0"))
# A nav we approach but cannot physically get within VISIT_K of (its centre sits
# inside a planet/station, or warp parks just short) counts as visited only once we
# are THIS close and have clearly stopped closing. Anything farther that stops
# closing is a STALL (warp not engaging), never a visit -- the old code declared a
# "min-approach" visit at ANY distance, so a dropped warp at 200k got recorded as
# done. Hard cap so that can never happen again.
MINAPPROACH_K = float(os.environ.get("ENB_MINAPPROACH_K", "6.0"))
# Distance within which the use-gate icon is reachable, i.e. close enough that
# leave_via_gate's gate.sh should attempt the crossing. You CANNOT warp to a gate
# (the orb never engages on a gate target -- measured live), so approach_gate hops
# the ship via navs to the one nearest the gate; if that leaves the gate inside
# GATE_USE_K we hand off to gate.sh, otherwise the gate needs a sublight run we do
# not do in warp-only mode. Generous on purpose: gate.sh's icon check is the final
# arbiter of whether the crossing actually fires.
GATE_USE_K = float(os.environ.get("ENB_GATE_USE_K", "9.0"))
# Re-engage tries on a mid-transit stall. A re-warp recovers a TOGGLE-cancelled warp
# (one re-click re-engages), but when the ship is far and a re-warp moves it zero (the
# orb does nothing at that range -- the distance reads identical across re-warps), more
# clicks only burn ~12s each sitting still. So 1: confirm-and-give-up fast, then the
# caller's attempt counter routes the nav to the map-click path. Owner, 2026-06-22:
# "sitting minutes without warp is extremely dangerous" -- this bounds the worst flail.
REWARP_MAX = int(os.environ.get("ENB_REWARP_MAX", "1"))
WARP_POLLS = int(os.environ.get("ENB_WARP_POLLS", "90"))
# In-flight warp-poll cadence. Each poll already costs ~0.5-1s (screenshot crop +
# two tesseract reads), so the extra fixed sleep on top is what sets the period.
# 2s polls the distance trace while warping without busy-spinning the OCR (owner,
# 2026-06-22: poll distance every ~2s while warping). Speed-confirm reads (the
# toggle-cancel guard) are a SEPARATE hardcoded 1s gap below -- they need real
# time-separation to tell "spinning up" from "stopped" and are not on this knob.
POLL_SLEEP = float(os.environ.get("ENB_POLL_SLEEP", "2.0"))
# Minimum distance drop between polls to count as "clearly warping". The distance OCR
# jitters by a few tenths of a k; a tiny apparent decrease must NOT be read as motion
# (that jitter was resetting the stall watch forever, parking the ship at 000). When
# really in warp the distance falls by many k per poll, so a 2k floor clears easily and
# filters noise. Below it we do not trust distance -- we read the authoritative speed.
CLOSE_MARGIN = float(os.environ.get("ENB_CLOSE_MARGIN", "2.0"))
# Polls of NO distance progress (no CLOSE_MARGIN drop) before we treat the warp as not
# engaging and act (re-warp, then skip). The speed OCR is unreliable (octagonal-zero
# font misreads "000" as "9"), so distance-not-closing -- NOT the speed readout -- is
# the authoritative stall signal. At POLL_SLEEP=2s this is ~8s before the first re-warp.
STALL_POLLS = int(os.environ.get("ENB_STALL_POLLS", "4"))
# A WRECK freezes the ship in place (speed 000, distance frozen) while the W/D/C nav
# cycle STILL flips target names -- so the per-target `best`/`flat` stall window keeps
# resetting (best_k changes every poll) and NEVER fires. That is exactly how a dead
# hull spun the survey for 89 polls at a frozen 35.05k. So we check the wreck on a
# signal the target-flip cannot mask: the ship "isn't moving". "Not moving" = the
# ABSOLUTE distance (tracked independent of which node the HUD is targeting) has not
# improved for FROZEN_POLLS polls, OR the speed readout is a confirmed 0. rescue.sh's
# detect is fast + ALIVE-cheap, so firing it liberally on a frozen ship is safe; the
# real defect was never firing it at all.
FROZEN_POLLS = int(os.environ.get("ENB_FROZEN_POLLS", "4"))
MATCH_MIN = 0.55           # below this, treat the OCR as garbage / non-nav
CYCLE_MAX = 40             # safety cap on a single cycle traversal
MAX_ATTEMPTS = int(os.environ.get("ENB_MAX_ATTEMPTS", "2"))   # warps before skip
# When a sweep finds nothing fresh in range but SHOWN (non-hidden) navs are still
# unvisited, the ship is parked in a pocket (e.g. the gas-cloud field near a planet)
# where those navs are out of scanner range -- NOT proof the sector is done. We fly
# to the farthest selectable nav to change what is in range and retry, up to this
# many times before conceding the remainder is unreachable. (Earlier code skipped
# all of them on the first such sweep, falsely "completing" a sector at 2/23.)
RELOC_MAX = int(os.environ.get("ENB_RELOC_MAX", "5"))

# --- shield safety (danger detection) ---------------------------------------
# While the ship is STOPPED we poll the shield every SHIELD_INTERVAL seconds. The
# shield is the blue HUD bar; shield.sh measures its fill 0..100. If the fill is
# near zero, or has dropped sharply since the last poll (== under fire), we FLEE:
# select a node a few navs away and warp to safety. We do NOT shield-check while
# warping -- moving is the safe state.
SHIELD_DANGER = float(os.environ.get("ENB_SHIELD_DANGER", "8"))   # <= this == ~0
SHIELD_DROP = float(os.environ.get("ENB_SHIELD_DROP", "10"))      # drop == under fire
SHIELD_SAFE = float(os.environ.get("ENB_SHIELD_SAFE", "60"))      # recovered
SHIELD_INTERVAL = float(os.environ.get("ENB_SHIELD_INTERVAL", "5"))
# A dense contested nav cluster (e.g. a mob parked among a "Beltway" of navs) can
# wedge the sweep: we sit to enumerate, get chipped, flee to an ADJACENT nav still
# in the cluster, sit again, flee again -- forever, with no progress. So we cap the
# flee retries: after FLEE_RETRY consecutive flees with no new visit, we REROUTE
# (skip the contested unvisited nav nearest where we are wedged + relocate far) so
# the next round targets a different route out of the area.
FLEE_RETRY = int(os.environ.get("ENB_FLEE_RETRY", "3"))
_shield = {"pct": None, "t": 0.0}


EXIT_HANG = 42             # drive.py exits with this when it detects a hard client hang
EXIT_STUCK = 43            # no visit/skip progress for STUCK_ROUNDS rounds: a bad state
                           # the fast scripts cannot resolve -> hand off to the operator/
                           # LLM (run-sector.sh HALTS instead of relogin-resuming).
EXIT_KILL = 44             # ENB_EXPLORE_KILL_NO_PROGRESS deadman switch fired: drive.py
                           # killed the client and the whole run must STOP (no relogin,
                           # no next sector). Terminal -- run-sector.sh / survey.sh bail.
# How many consecutive rounds with zero visit+skip progress before we stop spinning
# and escalate. The per-round logic already terminates the ordinary "nothing left in
# range" cases; this catches an UNFORESEEN spin (warp toggle-cancel, OCR going bad,
# a target the cycle keeps landing on but never reaches). Deliberately generous so a
# genuinely slow long-range map-click chase is never mistaken for stuck.
STUCK_ROUNDS = int(os.environ.get("ENB_STUCK_ROUNDS", "8"))
# Wall-clock no-progress watchdog (owner ask, 2026-06-21): if more than this many
# seconds pass with NO nav discovered or visited, print a WARNING, investigate (dump
# the visit order, the visible/unvisited navs and a screenshot), and -- because that is
# the threshold the owner set for "you can drop and skip" -- skip the single most-
# contested unvisited nav so the run advances instead of grinding a dead-zone forever.
# The timer resets on any real progress (a visit/skip) AND each time the watchdog skips
# one, so each stalled nav costs at most one window, never the whole run.
NOPROG_SECS = int(os.environ.get("ENB_NOPROG_SECS", "600"))

# Hard deadman switch (owner ask). OFF by default (0 / unset). Set it to a number of
# SECONDS via ENB_EXPLORE_KILL_NO_PROGRESS and, if that long passes with NO progress
# (no nav visited or skipped) -- i.e. after the softer NOPROG_SECS investigate+drop has
# ALREADY failed to advance the run -- drive.py KILLS the client and STOPS the whole
# survey (exit EXIT_KILL). Distinct from the hang path (relogin) and the stuck path
# (halt for re-route): this is terminal teardown. The timer is the SAME signal the soft
# watchdog uses (last_progress_ts, reset on any real progress), so it measures genuine
# no-progress wall-clock, not time-since-launch.
KILL_NO_PROGRESS = int(os.environ.get("ENB_EXPLORE_KILL_NO_PROGRESS", "0") or "0")


def kill_client():
    """Terminate the WINE client.exe (the deadman switch). Uses login-to-client's
    canonical kill so it tears down the same way a fresh login would. Best-effort:
    a pkill fallback covers a missing/renamed kill script."""
    killer = os.path.join(HERE, "..", "..", "login-to-client", "scripts", "00-kill.sh")
    try:
        if os.path.exists(killer):
            subprocess.run(["bash", killer], timeout=60)
            return
    except Exception:
        pass
    try:
        subprocess.run(["pkill", "-9", "-f", "client.exe"], timeout=20)
    except Exception:
        pass


class HangDetected(Exception):
    """The live client wedged in an internal busy-loop (frozen frame). Unrecoverable
    from inside the drive -- main() exits EXIT_HANG so the wrapper re-logs-in."""


def sh(*args, timeout=120, env=None):
    runenv = None
    if env:
        runenv = dict(os.environ)
        runenv.update(env)
    return subprocess.run(args, cwd=HERE, capture_output=True, text=True,
                          timeout=timeout, env=runenv)


def client_hung():
    """True if the client is hard-hung (alive but frozen frame). hangcheck.sh is the
    detector: a healthy in-space client animates the starfield every frame, so a
    byte-identical framebuffer across ~3 shots == wedged. CPU is NOT the signal (the
    normal in-space render pegs ~1000% too)."""
    try:
        r = sh("bash", os.path.join(HERE, "hangcheck.sh"))
    except subprocess.TimeoutExpired:
        return False   # the check itself stalling is not proof of a client hang
    return r.stdout.strip().splitlines()[-1:] == ["HUNG"]


def log(sector, action, detail):
    sh("bash", os.path.join(HERE, "logaction.sh"), sector, action, detail)


def ensure_focus():
    """Give the client window keyboard X INPUT FOCUS so the nav keys land in it.
    `windowactivate` alone is NOT enough on this Cinnamon desktop: when nemo-desktop
    (the file-manager desktop window) steals X input focus, `windowactivate --sync`
    raises/activates the client but does NOT move the X input focus off nemo-desktop,
    so every W/D/C keystroke silently goes to the desktop and the nav cycle never
    targets anything (XTEST *clicks* still land because they follow the pointer, which
    is why warp/gate clicks worked while keys did nothing). `windowfocus` sets the X
    input focus directly and DOES recover it -- verified live 2026-06-22. Do both:
    activate (raise) then focus (input)."""
    sh("bash", "-c",
       f'. "{os.path.join(HERE, "lib.sh")}"; id="$(client_win)" || exit 1; '
       f'xdotool windowactivate "$id" 2>/dev/null; xdotool windowfocus "$id"')


def tap(ch):
    sh("xdotool", "key", "--clearmodifiers", ch)


def nav_seed():
    """W: lock the target filter to NAVS and select the NEAREST nav. Re-anchors the
    cycle, so it also bootstraps from an empty selection (no map-click seed needed)."""
    ensure_focus()
    tap(SEED_KEY)
    time.sleep(0.25)   # the filter switch + first selection needs a touch longer to draw


def nav_next():
    tap(NEXT_KEY)
    time.sleep(0.1)   # just long enough for the panel to advance one nav


def nav_prev():
    tap(PREV_KEY)
    time.sleep(0.1)


# --- Always-Be-Warping gap timing ------------------------------------------
# The ship should be warping almost all the time. _motion["stopped_at"] is stamped
# the instant warp_to concludes the ship is stationary (arrival / speed 000), and
# the NEXT warp() reports how long the ship sat still before re-engaging. The owner's
# hard target is <10s; anything over is flagged so the enumeration can be tuned.
_motion = {"stopped_at": None}


def warp(sector=None):
    """Engage warp toward the selected target by clicking the warp orb, and confirm
    it actually engaged (speed left 000). Returns the warp speed (0 == never
    engaged: target under the ~2k floor, or every click was dropped). warp.sh does
    the click + speed-verify + bounded re-click; we just surface the result."""
    if _motion["stopped_at"] is not None:
        gap = time.time() - _motion["stopped_at"]
        flag = "  <<SLOW >10s" if gap > 10 else ""
        print(f"  warp-gap {gap:.1f}s (000 -> next warp){flag}", flush=True)
        if sector:
            log(sector, "warp-gap", f"{gap:.1f}s from speed-000 to next warp{flag}")
        _motion["stopped_at"] = None
    r = sh("bash", os.path.join(HERE, "warp.sh"))
    for line in r.stdout.splitlines():
        if line.startswith("WARP "):
            try:
                return int(line[5:].strip())
            except ValueError:
                return 0
    return 0


def read_speed():
    """Warp speed from the green HUD readout, or None if unreadable. 0 == stopped."""
    r = sh("bash", os.path.join(HERE, "read-speed.sh"))
    for line in r.stdout.splitlines():
        if line.startswith("SPEED "):
            v = line[6:].strip()
            return int(v) if v.isdigit() else None
    return None


def read_shield():
    r = sh("bash", os.path.join(HERE, "shield.sh"))
    for line in r.stdout.splitlines():
        if line.startswith("SHIELD "):
            try:
                return float(line[7:].strip())
            except ValueError:
                return None
    return None


SHIELD_SAMPLES = int(os.environ.get("ENB_SHIELD_SAMPLES", "3"))


def read_shield_robust(n=None):
    """Best-of-N shield read. The translucent shield bar washes out against bright
    gas-cloud / nebula backgrounds and reads artificially LOW -- a healthy 90% shield
    returns 0.0 on some frames (observed 0,0,87,94,89 across 5 reads). The washout is
    one-directional: it can only DROP the reading, never invent a high one. So the MAX
    over a burst is the reliable estimate of the true shield -- a genuinely downed
    shield cannot fabricate a 90% sample. Returns (best, samples)."""
    if n is None:
        n = SHIELD_SAMPLES
    samples = []
    for _ in range(n):
        s = read_shield()
        if s is not None:
            samples.append(s)
    if not samples:
        return None, samples
    return max(samples), samples


def flee(sector):
    """Shield dropped. The common cause in these sectors is RADIATION, which drains the
    shield to 0 but does NOT kill the ship -- so there is nothing to wait out. The old
    flee hopped a few navs and then polled up to 30s for the shield to climb back to
    SHIELD_SAFE; in a radiation field it never does, so every round re-fled and the whole
    sweep looped to a standstill (observed live in Adriel Prime, stuck 3/23).

    New behaviour (owner, 2026-06-21): react FAST and keep making progress -- warp
    straight to the NEAREST unvisited nav (an unvisited nav is one we have never been
    within VISIT_K of, so it is always >2k away) and return immediately. No recovery
    wait. Warping == moving, and warp_to credits the target plus every nav within 2k en
    route, so a 'flee' is just a nearest-first exploration step. Returns True if it
    warped to a nav, False if nothing unvisited is in range (caller then lets the normal
    sweep handle done/relocate -- safe, since radiation will not kill us)."""
    st, by = ledger(sector)
    zones = danger_zones(sector)
    navs = enum_navs(sector)
    if not navs:
        return False
    mark_visible(sector, navs)
    st, by = ledger(sector)
    for nv in navs:                        # nearest-first
        k = nv["key"]
        if is_done_node(by, k):
            continue
        if in_danger(node_xyz(by, k), zones):
            continue                       # death zone: only warp past, never target
        print(f"  shield low -> nearest unvisited {nv['name']} (rank {nv['rank']})",
              flush=True)
        log(sector, "shield-flee-nearest", f"to {nv['name']} (nearest unvisited)")
        sel_name, _ = select_named(sector, k)
        if sel_name is None:
            continue
        _dest["name"] = nv["name"]
        _dest["xyz"] = node_xyz(by, k)
        reached = warp_to(sector, k, nv["name"])
        if reached:
            maybe_register(sector, by, k, nv["name"])
        return True
    return False                           # nothing unvisited in range here


def shield_guard(sector, force=False):
    """Poll the shield at most every SHIELD_INTERVAL seconds while stationary. If it
    is near zero, or dropped sharply since the last poll, flee. Returns True if we
    fled (caller should re-enumerate, since the ship has moved)."""
    now = time.time()
    if not force and now - _shield["t"] < SHIELD_INTERVAL:
        return False
    _shield["t"] = now
    # The shield bar is translucent and OCRs noisily over a bright/shifting
    # background (gas clouds, nebula): a healthy 90% shield reads 0.0 on some frames
    # (observed 0,0,87,94,89 across 5 reads), and an un-debounced reader flee-looped
    # the whole sweep to a standstill in a gas field. The washout is ONE-DIRECTIONAL
    # -- it can only drop a reading, never invent a high one -- so we take a best-of-N
    # burst and trust the MAX as the true shield. A single re-read was not enough:
    # the washout can return 0.0 twice in a row.
    # Cheap-first: one quick read. The washout is ONE-DIRECTIONAL (it only reads LOW),
    # so a single read >= SHIELD_SAFE guarantees the true shield is at least that --
    # clearly not under fire, no flee, and we skip the 3-read burst. Bursting every
    # round was ~4.5s of dead time (== 000->warp gap) when the shield is almost always
    # healthy. Only a quick read BELOW safe is ambiguous (real damage vs washout), and
    # only then do we pay for the best-of-N burst to disambiguate.
    quick = read_shield()
    if quick is not None and quick >= SHIELD_SAFE:
        _shield["pct"] = quick
        return False
    pct, samples = read_shield_robust()
    if pct is None:
        # no readable shield bar can mean we are dead -- check for the death panel
        if handle_wreck(sector):
            return True
        return False
    prev = _shield["pct"]
    _shield["pct"] = pct
    low = pct <= SHIELD_DANGER          # best-of-N still near zero == genuinely down
    drop = prev is not None and pct <= prev - SHIELD_DROP
    danger = False
    if low:
        log(sector, "shield-zero", f"shield {pct}% (burst {samples}) {_loc_str()}")
        if handle_wreck(sector):        # near-zero confirmed -- might be dead
            return True
        danger = True
    elif drop:
        danger = True
    if danger:
        print(f"  !! shield {pct}% (prev {prev}) -- redirecting to nearest unvisited nav",
              flush=True)
        log(sector, "shield-low", f"shield {pct}% (prev {prev}); redirect to nearest unvisited")
        if flee(sector):
            _shield["pct"] = None   # moved -- reset baseline at the new location
            return True
        # Nothing unvisited in range to redirect to. Radiation will not kill us, so do
        # NOT spin re-fleeing -- fall through and let the normal farthest-first sweep run
        # its done/relocate logic from here.
        return False
    return False


def skip_node(sector, name, reason):
    sh("python3", os.path.join(HERE, "state.py"), "skip", sector, name, reason)
    log(sector, "skip", f"{name} -- {reason}")
    print(f"  SKIP {name} -- {reason}", flush=True)


def reroute_from_wedge(sector):
    """Called after FLEE_RETRY consecutive flees with no progress: we are wedged in a
    contested cluster near the last target (_dest). Skip the unvisited nav nearest the
    wedge spot -- we keep getting forced to flee there, so stop trying to sit at it --
    then relocate FAR (a long flee) so the next round picks a different route out of the
    area. Returns the skipped nav name (or None)."""
    st, _ = ledger(sector)
    anchor = _dest["xyz"]
    skipped_name = None
    if anchor is not None:
        cand = [n for n in st["nodes"]
                if not n["visited"] and not n.get("skipped")
                and n.get("x") is not None]
        if cand:
            nearest = min(cand, key=lambda n: math.dist(
                (n["x"], n["y"], n["z"]), anchor))
            skip_node(sector, nearest["name"],
                      f"contested -- fled {FLEE_RETRY}x without progress, rerouting")
            skipped_name = nearest["name"]
    print(f"  REROUTE: fled {FLEE_RETRY}x with no progress -- relocating to a "
          f"different route", flush=True)
    log(sector, "reroute", f"fled {FLEE_RETRY}x; skipped {skipped_name}; relocating")
    relocate_far(sector)    # reposition FAR to leave the contested area
    return skipped_name


# --- wreck / death-zone safety ----------------------------------------------
# A wreck marks a 5-unit-radius circle (in JSON-coord "k" units, same as VISIT_K)
# around where we died as permanent danger: never conclude a warp inside it.
DANGER_RADIUS = float(os.environ.get("ENB_DANGER_RADIUS", "5"))
# where we are trying to stop right now -- the place we would die if wrecked.
_dest = {"name": None, "xyz": None}


def node_xyz(by, key):
    n = by.get(key)
    return (n["x"], n["y"], n["z"]) if n else None


def _loc_str():
    """Best screen-only location string for event logging: the current target nav
    and its coords (we cannot read exact ship coords without injection)."""
    name = _dest["name"] or "unknown"
    xyz = _dest["xyz"]
    return f"near {name} {xyz}" if xyz is not None else f"near {name}"


def danger_zones(sector):
    st = state.read(sector)
    return [(d["x"], d["y"], d["z"]) for d in st.get("dangers", [])]


def in_danger(xyz, zones, radius=DANGER_RADIUS):
    if xyz is None:
        return False
    return any(math.dist(xyz, z) <= radius for z in zones)


def check_wreck():
    """True if the ship is WRECKED. Authoritative, deterministic detector: rescue.sh
    fuzzy-matches the amber "<<Distress" HUD label, the one wreck signal that is
    independent of every other UI element (map/comm open or not). The OLD wreck.sh
    OCR'd the whole screen for the word "Tow"/"Request Tow" -- which the real death
    UI NEVER shows (it shows "<<Distress" + a comm option "I need a tow") -- so it
    returned ALIVE on a wrecked ship and the survey spun 89 polls on a dead hull."""
    r = sh("bash", os.path.join(HERE, "rescue.sh"), "detect")
    return r.stdout.strip().splitlines()[-1:] == ["WRECKED"]


def handle_wreck(sector):
    """We were destroyed. Record the death location + a permanent danger zone (never
    conclude a warp within DANGER_RADIUS of it), run the deterministic tow-recovery
    state machine (rescue.sh: close map if open -> click "I need a tow" -> wait for
    <<Distress to clear), then undock. Returns True if it handled a wreck."""
    if not check_wreck():
        return False
    loc = _dest["xyz"]
    where = _dest["name"] or "unknown location"
    print(f"  ## WRECKED near {where} {loc} -- recording danger zone + tow recovery",
          flush=True)
    # wreck DETECTED: record time (logaction stamps it) + location.
    log(sector, "wreck-detected", f"destroyed near {where} {loc}")
    if loc is not None:
        sh("python3", os.path.join(HERE, "state.py"), "danger-add", sector,
           str(loc[0]), str(loc[1]), str(loc[2]),
           f"wrecked near {where}; never stop within {DANGER_RADIUS}")
        # the node we died trying to reach cannot be stop-visited: skip it.
        if _dest["name"]:
            skip_node(sector, _dest["name"],
                      f"death zone -- wrecked here; warp past only")
    # Run the deterministic recovery state machine. It closes an occluding map ONCE
    # (only if open), fuzzy-locates and clicks "I need a tow", and waits (bounded) for
    # the <<Distress label to clear -- towing the ship back to its station (docked).
    log(sector, "tow-clicked", f"requesting tow {_loc_str()}")
    try:
        rr = sh("bash", os.path.join(HERE, "rescue.sh"), "rescue",
                timeout=300, env={"ENB_RESCUE_SECTOR": sector})
    except subprocess.TimeoutExpired:
        print("  ## rescue.sh timed out -- aborting drive for operator handoff",
              flush=True)
        log(sector, "tow-timeout", "rescue.sh exceeded 300s; aborting")
        sys.exit(EXIT_HANG)
    # HONOR the recovery result. rescue.sh exits 3 / prints STILL-WRECKED when it
    # opened the dialog but could not confirm the tow cleared the wreck. Proceeding
    # to undock here would fly a DESTROYED ship (undock's in_space() reads the 000
    # speed of a wrecked-in-space hull as "in space" and false-passes), so we must
    # NOT treat a failed tow as recovered -- bail for operator handoff instead.
    rlast = next((l.strip() for l in reversed(rr.stdout.splitlines()) if l.strip()), "")
    if rr.returncode == 3 or rlast == "STILL-WRECKED":
        print("  ## tow did NOT recover the ship (rescue.sh STILL-WRECKED) -- "
              "aborting drive for operator handoff", flush=True)
        log(sector, "tow-failed", "rescue.sh reported STILL-WRECKED; aborting")
        sys.exit(EXIT_HANG)
    log(sector, "tow-done", "respawned at last station")
    # A tow drops us DOCKED in a station bay (no warp cluster) -- OR sometimes straight
    # back into space. We must launch back into space or the whole sweep wedges
    # spinning at a dock. undock.sh checks the warp-speed readout every rotation and
    # exits the instant it is in space, so it is a fast no-op when already outside.
    # Its rotate sweep can still run longer than sh()'s default 120s on a bad station,
    # so give it a dedicated longer cap AND catch a timeout -- a stuck undock must NOT
    # take down the whole drive (that uncaught TimeoutExpired stranded the Freya run).
    try:
        res = sh("bash", os.path.join(HERE, "undock.sh"), timeout=300)
        lines = [l for l in res.stdout.splitlines() if l.strip()]
        state = lines[-1].strip() if lines else ""
    except subprocess.TimeoutExpired:
        print("  ## undock.sh timed out -- aborting drive for operator handoff",
              flush=True)
        log(sector, "undock-timeout", "undock.sh exceeded 300s; aborting")
        sys.exit(EXIT_HANG)
    if state == "UNDOCKED" or state == "IN-SPACE":
        log(sector, "undocked", f"back in space after tow ({state})")
    else:
        # Could not get back into space: bail loudly rather than spin forever. A
        # tow can also cross sectors (back to the home station), which this drive
        # cannot auto-route from -- the operator/wrapper handles the handoff.
        print("  ## could not undock after tow -- aborting drive (operator must "
              "undock / re-route to the target sector)", flush=True)
        log(sector, "undock-failed", "still docked after tow; aborting")
        sys.exit(EXIT_HANG)
    open_map()
    # A tow can drop us in a DIFFERENT sector (back at the home station). Confirm we
    # are still in `sector` before resuming the sweep -- otherwise we fly the wrong
    # sector and corrupt its ledger. verify_sector aborts EXIT_STUCK on a mismatch.
    verify_sector(sector, "post-tow")
    _shield["pct"] = None
    return True


def read_target(nameonly=False):
    """Return (raw_name, dist_float_or_None). nameonly=True skips the distance OCR
    (the sweep's fast path while walking past already-done navs); dist comes back None."""
    env = dict(os.environ, ENB_TGT_NAMEONLY="1") if nameonly else None
    r = subprocess.run(["bash", os.path.join(HERE, "read-target.sh")],
                       cwd=HERE, capture_output=True, text=True, timeout=120, env=env)
    raw, dist = "", None
    for line in r.stdout.splitlines():
        if line.startswith("TARGET "):
            raw = line[7:].strip()
        elif line.startswith("DIST "):
            v = line[5:].strip()
            try:
                dist = float(v)
            except ValueError:
                dist = None
    return raw, dist


def match(sector, raw):
    """raw OCR name -> (canonical_name, ratio) or (None, 0)."""
    if not raw:
        return None, 0.0
    r = sh("python3", os.path.join(HERE, "navdata.py"), "match", sector, raw)
    out = r.stdout.strip().split("\t")
    if len(out) == 2:
        try:
            return out[0], float(out[1])
        except ValueError:
            pass
    return None, 0.0


def key(name):
    return navdata.norm(name) if name else ""


def read_matched(sector, retry_low=True, nameonly=False):
    """Read the panel and resolve the name to a canonical node (one retry if the
    name marquee-scrolled into a low-confidence read). Returns (name, ratio, dist).
    nameonly=True skips the distance OCR for speed (dist comes back None)."""
    raw, dist = read_target(nameonly=nameonly)
    name, ratio = match(sector, raw)
    if retry_low and ratio < 0.7:
        time.sleep(0.5)
        raw2, dist2 = read_target()
        name2, ratio2 = match(sector, raw2)
        if ratio2 > ratio:
            name, ratio, dist = name2, ratio2, dist2
    return name, ratio, dist


def visit(sector, name, dist, how):
    st = state.read(sector)
    k = navdata.norm(name)
    for n in st["nodes"]:
        if navdata.norm(n["name"]) == k:
            if not n["visited"]:
                n["visited"] = True
                n["revealed"] = True
                # A nav we physically reached is NOT unreachable: clear any earlier
                # skip so a nav skipped by a stalled direct warp and then bagged
                # en-route is not double-flagged (and never reported "never within
                # {VISIT_K}k" when we just visited it at 1-5k). Without this the
                # ledger counts the same nav as both visited and skipped.
                if n.get("skipped"):
                    n["skipped"] = False
                    n["skip_reason"] = ""
                    log(sector, "unskip", f"{n['name']} reached -- clearing prior skip")
                state.write(st)
                d = "?" if dist is None else f"{dist:.2f}"
                log(sector, "visit", f"{n['name']} @{d}k ({how})")
                print(f"  VISIT {n['name']} @{d}k ({how})", flush=True)
            return True
    return False


def maybe_register(sector, by, want_key, want_name):
    """If the node we just reached is a STATION, register at it (once). Registering
    sets it as our recall/respawn point so a later wreck tows us to THIS closer
    station, and grants explore XP. We are targeted on it and within range here, so
    the HUD shows the Register button; register.sh OCRs+returns it (NOREGISTER if
    it is not shown -- e.g. we concluded too far out, which is safe to skip)."""
    n = by.get(want_key, {})
    if n.get("type") not in ("station", "factioned-station"):
        return
    st = state.read(sector)
    if any(navdata.norm(m["name"]) == want_key and m.get("registered")
           for m in st["nodes"]):
        return  # already registered this station
    out = sh("bash", os.path.join(HERE, "register.sh")).stdout.split()
    if out and out[0] == "REGISTER" and len(out) >= 3:
        bx, byy = out[1], out[2]
        log(sector, "register", f"{want_name} -- clicking Register at ({bx},{byy})")
        print(f"  REGISTER {want_name} at ({bx},{byy})", flush=True)
        sh("bash", os.path.join(HERE, "click-map.sh"), bx, byy)
        st = state.read(sector)
        for m in st["nodes"]:
            if navdata.norm(m["name"]) == want_key:
                m["registered"] = True
        state.write(st)
    else:
        log(sector, "register-skip", f"{want_name} -- no Register button visible")
        print(f"  register-skip {want_name} (no button)", flush=True)


def ledger(sector):
    st = state.read(sector)
    by = {navdata.norm(n["name"]): n for n in st["nodes"]}
    return st, by


def is_done_node(by, k):
    n = by.get(k, {})
    return bool(n.get("visited") or n.get("skipped"))


def enumerate_cycle(sector):
    """Walk the target cycle ONCE in full, reading every slot. Returns an ordered
    list of {index,key,name,dist} (key=None for unreadable/out-of-range slots), or
    None if a shield-danger flee interrupted us. Used by goto.py, which needs the
    whole in-range list to hop toward a specific out-of-range nav. drive.py's own
    sweep uses sweep_round (streaming early-exit) instead, to avoid re-OCRing the
    whole cycle every round."""
    seq = []
    first_key = None
    saw_other = False          # a key != first_key has appeared (real lap, not a dup read)
    nav_seed()                 # W: lock to navs + nearest, so slot 0 is the nearest nav
    for i in range(CYCLE_MAX):
        if shield_guard(sector):
            return None
        name, ratio, dist = read_matched(sector)
        k = key(name)
        if k and ratio >= MATCH_MIN:
            if first_key is None:
                first_key = k
            elif k != first_key:
                saw_other = True
            elif saw_other:
                break          # wrapped back to the nearest after a full lap
            # else: re-read of the nearest before D advanced (stale frame) -- skip it
            if not (k == first_key and seq and seq[-1]["key"] == k):
                seq.append({"index": i, "key": k, "name": name, "dist": dist})
        else:
            seq.append({"index": i, "key": None, "name": name, "dist": dist})
        nav_next()             # D: step to the next-farther nav for the next read
    return seq


def select_named(sector, want_key):
    """W-seed (lock to navs + nearest) then step D until the panel is SELECTED on
    want_key, reading each slot by NAME (NOT by D-count -- counting drifts when a D
    fails to advance and lands on the wrong nav, which then warps nowhere). Returns
    (name, dist) once selected, or (None, None) after a full lap without finding it.
    Used by goto.py and by the sweep to land on the chosen target."""
    first_key = None
    saw_other = False
    nav_seed()                 # W: nearest nav; slot 0 read below before the first D
    for _ in range(CYCLE_MAX):
        name, ratio, dist = read_matched(sector)
        k = key(name)
        if k == want_key:
            return name, dist
        if k and ratio >= MATCH_MIN:
            # wrap detection: stop only when we return to the FIRST matched node after
            # at least one different node has appeared (fuzzy matching collides two
            # slots onto one key, and a stale frame re-reads the nearest, either of
            # which would trip a premature false wrap and give up before the target).
            if first_key is None:
                first_key = k
            elif k != first_key:
                saw_other = True
            elif saw_other:
                break          # wrapped without landing on want_key
        nav_next()             # D: step to the next-farther nav
    return None, None


def enum_navs(sector, save_montage=None):
    """Fast batch-enumerate the in-range nav cycle: enum_fast.py presses W then walks
    D, grabbing the target-name box off the root window via Xlib (~0.6ms/grab) and
    OCRing every frame in ONE tesseract pass. Returns the ordered, DEDUPED in-range
    nav list [{rank,key,name,ratio,dist}] nearest-first, keeping the nearest (first)
    occurrence of each canonical nav. `dist` is the panel distance in k (float) read in
    the same walk, or None if it did not OCR. [] == nothing matched (empty pocket /
    blank HUD).

    The cycle only ever holds navs IN SCANNER RANGE (<= the sector's total), so walking
    total+slack always laps the in-range subset; the dedup collapses the wrap and any
    stale-frame duplicate down to one entry at its nearest rank."""
    total = len(navdata.load(sector))
    count = max(8, min(CYCLE_MAX, total + 4))
    args = ["python3", os.path.join(HERE, "enum_fast.py"),
            "--sector", sector, "--count", str(count),
            "--gap", ENUM_GAP, "--burst", ENUM_BURST]
    if save_montage:
        args += ["--save-montage", save_montage]
    r = sh(*args)
    out, seen, rank = [], set(), 0
    for line in r.stdout.splitlines():
        p = line.split("\t")
        if len(p) < 4:
            continue
        canon = p[2].strip()
        try:
            ratio = float(p[3])
        except ValueError:
            ratio = 0.0
        if not canon or ratio < MATCH_MIN:
            continue
        dist = None
        if len(p) >= 5 and p[4].strip():
            try:
                dist = float(p[4])
            except ValueError:
                dist = None
        k = key(canon)
        if k in seen:                 # dedup -> keep the nearest (first) occurrence
            continue
        seen.add(k)
        out.append({"rank": rank, "key": k, "name": canon, "ratio": ratio, "dist": dist})
        rank += 1
    return out


def mark_visible(sector, navs):
    """Record every nav the cycle just OCR-matched as VISIBLE (ledger flag `revealed`).
    This is the owner's accumulating 'visible' list: a nav stays visible once seen, and
    a nav can ONLY join it if it matched a name in the sector's possible list (enum_navs
    already dropped sub-threshold OCR, so every key here is a real node). Completion is
    judged against this set: done when visited+skipped covers every visible nav. Returns
    the number newly revealed."""
    st = state.read(sector)
    keys = {nv["key"] for nv in navs}
    changed = 0
    for n in st["nodes"]:
        if navdata.norm(n["name"]) in keys and not n.get("revealed"):
            n["revealed"] = True
            changed += 1
    if changed:
        state.write(st)
    return changed


def sweep_round(sector, deferred=frozenset()):
    """Batch-enumerate the in-range nav cycle (W + D-walk + montage OCR), record every
    matched nav as VISIBLE, pick the FARTHEST unvisited non-death-zone nav as the
    target, select it by NAME (W then a verified D-walk onto it -- never by D-count,
    which drifts), and warp to it.

    HYBRID ORDER (owner algorithm, 2026-06-21): the cycle is distance-ordered
    nearest-first. For the FIRST HALF of the sector we walk it BACKWARDS and target the
    farthest unvisited nav we can see -- flying to the farthest sweeps the whole visible
    span in a single warp, and the en-route pickup in warp_to bags every nearer nav
    within ENROUTE_K along the way, so one long hop covers what many short hops would and
    navs that enter scanner range as we cross the sector join `visible` for free. Once
    >=50% of the sector's navs are done the remainder is scattered, so we SWITCH to
    nearest-unvisited: late in the run farthest-first degenerates into an end-to-end
    zigzag (measured on AdrielPrime: pure farthest-first = 3465 units of warp path;
    nearest-first cleanup = 1179). We NEVER target a visited node (owner: 'never ever
    warp to a node already on the visited list directly').

    Returns {status,...}: 'warped' (with key/name/reached) / 'fled' / 'done' / 'blind'.
    """
    if shield_guard(sector):
        return {"status": "fled"}
    st, by = ledger(sector)
    zones = danger_zones(sector)

    navs = enum_navs(sector)
    if not navs:
        # nothing matched the whole walk: blank HUD (just loaded/relogged) or an empty
        # pocket (nothing in scanner range here). main re-seeds / falls back, never
        # declares the sector done on no data.
        return {"status": "blind"}

    mark_visible(sector, navs)          # everything in scanner range now joins "visible"
    st, by = ledger(sector)             # re-read: the revealed flags just changed

    # Credit every enumerated nav already within ENROUTE_K (12k) as visited right now --
    # we are sitting next to it, no warp trip needed (owner: "consider visited already
    # because it's within 8k"). This is the main defence against the dangerous sit: it
    # bags the near cluster from one enumeration so done% jumps and we stop re-targeting
    # near/unreachable navs round after round. Done before target selection so an
    # already-near nav is never chosen as a warp target.
    near = 0
    for nv in navs:
        if (nv.get("dist") is not None and nv["dist"] <= ENROUTE_K
                and not is_done_node(by, nv["key"])):
            if visit(sector, nv["name"], nv["dist"], f"enum within {ENROUTE_K:g}k"):
                near += 1
    if near:
        st, by = ledger(sector)         # re-read: the visited flags just changed

    total = len(navdata.load(sector))
    done_n = sum(1 for n in st["nodes"] if n.get("visited") or n.get("skipped"))
    nearest_phase = total > 0 and done_n / total >= 0.2
    order = navs if nearest_phase else list(reversed(navs))
    print(f"  order: {'nearest-first' if nearest_phase else 'farthest-first'} "
          f"({done_n}/{total} done)", flush=True)

    # Pick the next unvisited nav in order and WARP to it -- there is NO max warp
    # range in this game (owner, 2026-06-22): you can warp 2k or 800k, so distance
    # is NOT a reason to pass over a target. (The old MAX_TARGET_K two-pass cap was
    # built on the false premise that far navs can't be warped and only flailed; the
    # real sit bug was read-speed misreading a parked "000" as "?", which made
    # warp.sh never click the orb -- fixed in read-speed.sh.) Just take the next one.
    target = None
    for nv in order:                    # farthest-first <20% done, then nearest-first
        k = nv["key"]
        if is_done_node(by, k):
            continue
        if k in deferred:
            # direct warp already failed MAX_ATTEMPTS times -- stop re-targeting it,
            # but leave it unvisited so en-route pickup / relocate / map-click can
            # still reach it. If EVERY remaining nav is deferred, target stays None
            # and we fall into the done-path, which relocates to a fresh pocket.
            continue
        if in_danger(node_xyz(by, k), zones):
            # death-zone nav: never sit on it, only warp past -- skip it as a target.
            skip_node(sector, nv["name"],
                      f"within {DANGER_RADIUS} of a death zone -- warp past only")
            st, by = ledger(sector)
            continue
        target = nv
        break
    if target is None:
        # every nav in scanner range here is visited/skipped -> sector done from here.
        return {"status": "done"}

    print(f"  -> target {target['name']} (rank {target['rank']})", flush=True)
    sel_name, _sel_dist = select_named(sector, target["key"])
    if sel_name is None:
        # could not land the panel on it (drifted / OCR garbage) -> report unreached so
        # main's attempt counter eventually skips it.
        return {"status": "warped", "key": target["key"], "name": target["name"],
                "reached": False}
    st, by = ledger(sector)
    _dest["name"] = target["name"]
    _dest["xyz"] = node_xyz(by, target["key"])
    reached = warp_to(sector, target["key"], target["name"])
    if reached:
        maybe_register(sector, by, target["key"], target["name"])
    return {"status": "warped", "key": target["key"], "name": target["name"],
            "reached": reached}


def relocate_far(sector):
    """Fly to the FARTHEST in-range nav so a different set of navs falls into scanner
    range. Called when a sweep found nothing fresh in range yet shown navs remain
    unvisited: the ship is in a pocket where they are simply out of range, and crossing
    the sector changes what is reachable. Reuses the warp machinery, so the destination
    nav is itself recorded visited on arrival. Returns True if it actually warped.

    The W/D cycle is distance-ordered, so the LAST entry of the batch enumerate is the
    farthest in-range nav -- no per-slot distance OCR needed. We skip PLANETS: they sit
    way out at the sector edge with few navs around them, so relocating to one is a long
    flight to a barren spot (owner, 2026-06-21). We want a far nav-point/gate that drags
    a DIFFERENT cluster into range, not the loneliest object in the sector."""
    st, by = ledger(sector)
    navs = enum_navs(sector)
    mark_visible(sector, navs)
    st, by = ledger(sector)
    # The cycle is distance-ordered nearest-first, so the FARTHEST nav is the last entry.
    # Prefer a far UNVISITED nav (still an exploration target). If every nav in range here
    # is already visited, we still REPOSITION toward the farthest visited one: warping
    # that whole distance drags a fresh slice of the sector into scanner range and
    # reveals/visits new navs EN ROUTE. That is reposition-to-extend-range via the cycle
    # -- the fast, screen-only alternative to the slow map-click probe -- NOT re-exploring
    # a visited node as a destination. Planets are skipped (far edge + barren).
    # Exclude PLANETS (far + barren), DEATH-SKIPPED navs, and anything inside a known
    # danger zone. The reposition-toward-visited fallback below otherwise picks the
    # FARTHEST candidate as a waypoint, and a far death-zone nav (Krakow on AkeronsGate)
    # would drag the healthy ship straight back into the spot that already wrecked it.
    # A skipped nav carries skipped=True; a danger zone is a recorded death circle.
    zones = danger_zones(sector)
    cand = [nv for nv in navs
            if by.get(nv["key"], {}).get("type") != "planet"
            and not by.get(nv["key"], {}).get("skipped")
            and not in_danger(node_xyz(by, nv["key"]), zones)]
    if not cand:
        return False
    unvis = [nv for nv in cand if not is_done_node(by, nv["key"])]
    target = (unvis or cand)[-1]            # farthest (list is nearest-first)
    print(f"  RELOCATE -> {target['name']} (rank {target['rank']}, farthest) "
          f"to extend scanner range", flush=True)
    log(sector, "relocate", f"to {target['name']} (farthest in range) to extend scanner range")
    sel_name, _ = select_named(sector, target["key"])
    if sel_name is None:
        return False
    _dest["name"] = target["name"]
    _dest["xyz"] = node_xyz(by, target["key"])
    return warp_to(sector, target["key"], target["name"])


def warp_to(sector, want_key, want_name):
    """Stamp the stop clock on every exit (so the NEXT warp can report the 000->warp
    gap) and delegate to the warp loop."""
    try:
        return _warp_to_impl(sector, want_key, want_name)
    finally:
        _motion["stopped_at"] = time.time()


def _warp_to_impl(sector, want_key, want_name):
    """Warp to the currently-selected node and poll to arrival. Records the node and
    any en-route node within 2k. Returns True if the wanted node was reached.

    PROGRESS IS JUDGED BY DISTANCE OVER A WINDOW, never by the speed readout. The
    green WARP SPEED font renders digits as octagons and tesseract misreads a
    dead-stopped "000" as "9", so a speed-gated re-warp once spun forever at an
    84k target the ship was parked in front of. Distance, by contrast, reads
    cleanly in this panel. Rule: every poll, if the distance has dropped by at
    least CLOSE_MARGIN below the best seen so far, we are closing == warping (reset
    the stall window). If it has NOT improved for STALL_POLLS consecutive polls we
    are not effectively warping -> classify (reached / parked / wrecked) and, if
    still far, RE-WARP (bounded by REWARP_MAX) then give up. This is inherently
    safe against the warp-orb TOGGLE-cancel: a live warp makes the distance fall,
    so we only ever re-click when it is genuinely flat. Every poll prints its full
    decision state so a stuck run is never a black box."""
    warp(sector)
    best = 1e9       # lowest distance-to-target seen so far (the progress baseline)
    best_k = None    # the node `best`/`flat` belong to (the HUD target auto-flips)
    flat = 0         # consecutive polls with no CLOSE_MARGIN improvement on `best`
    blanks = 0
    rewarps = 0
    seen_enroute = set()
    # ABSOLUTE movement tracker (target-flip-independent) -- see FROZEN_POLLS. This is
    # the wreck/not-moving signal that the per-target `best` cannot give us, because a
    # wreck keeps flipping the targeted node (so best_k changes and best resets) while
    # the ship sits dead in place.
    min_any = 1e9    # lowest distance seen this warp toward ANY node
    frozen = 0       # consecutive polls with no absolute improvement on min_any
    for p in range(WARP_POLLS):
        name, ratio, dist = read_matched(sector, retry_low=False)
        if dist is None or not name:
            blanks += 1
            # a run of unreadable panels means one of three things: we were
            # destroyed mid-warp (wreck screen), the panel is briefly blank, or the
            # CLIENT HARD-HUNG (frozen frame). Check the hang first -- it is the one
            # that never recovers and must trigger a kill+relogin.
            print(f"  poll {p:02d} <unreadable panel> blanks={blanks}/5", flush=True)
            if blanks >= 5:
                if client_hung():
                    raise HangDetected()
                if handle_wreck(sector):
                    return False
                # Alive, not hung, not wrecked, yet the target panel has read blank
                # for 5+ polls. A genuine in-flight warp keeps that panel readable
                # AND moves the ship, so a CONFIRMED speed 0 here means we are parked,
                # not warping: the warp never engaged (target under the ~2k floor) or
                # a map-fallback pick left the map overlay covering the panel. Either
                # way, burning the whole WARP_POLLS budget on blank reads is pure dead
                # time -- abort and re-enumerate so the driver closes the map and picks
                # a real target. (Observed live: map-fallback "Shipyard Beltway 2
                # @0.00k" stalled 40+ blank polls. Only a definitive 0 aborts; an
                # unreadable speed -> None -> we keep polling as before.)
                if read_speed() == 0:
                    print("  blank panel + parked (speed 0) -> abort warp, re-enumerate",
                          flush=True)
                    return False
            time.sleep(1)
            continue
        blanks = 0
        k = key(name)
        # The HUD target auto-flips as the ship flies -- the client re-locks the
        # nearest nav on arrival / while crossing a field -- so consecutive polls
        # can report DIFFERENT nodes. `best`/`flat` measure progress toward ONE
        # node, so when the targeted node changes we restart the stall window for
        # the new node. Without this, a far node (the Kailaasa gate at 54k) is
        # judged against a near node's stale 5k baseline and reads as an instant
        # STALL even while genuinely closing -- which stranded the Yokan gate-leave
        # (gate closing 54k->38k counted as "no progress" and gave up).
        if k != best_k:
            best = 1e9
            flat = 0
            best_k = k
        spd = read_speed()
        spd_s = f"{spd:g}" if spd is not None else "?"
        on_target = "->TGT" if k == want_key else ""
        print(f"  poll {p:02d} {name:28} {dist:8.2f}k  best={best:8.2f} "
              f"flat={flat}/{STALL_POLLS} rw={rewarps}/{REWARP_MAX} spd={spd_s} {on_target}",
              flush=True)
        # WRECK / not-moving check -- EVERY poll the ship isn't moving (owner: "check
        # every time the ship isn't moving distance or speed"). Track ABSOLUTE distance
        # so a wreck's target-name flipping cannot mask a frozen hull (the per-target
        # `best` resets on every flip and never stalls). If the absolute distance has
        # not improved for FROZEN_POLLS, OR the speed reads a confirmed 0, and we are
        # NOT simply arriving (dist still > VISIT_K), run the deterministic wreck
        # detector. handle_wreck returns False fast when ALIVE, so this is cheap.
        if dist < min_any - CLOSE_MARGIN:
            min_any = dist
            frozen = 0
        else:
            frozen += 1
        not_moving = (spd == 0) or (frozen >= FROZEN_POLLS)
        if not_moving and dist > VISIT_K:
            if handle_wreck(sector):
                return False
        # en-route pickup: any KNOWN nav we pass within ENROUTE_K (12k) on the way. We
        # are actively warping here (this is the warp poll loop), and the ship blows
        # PAST off-path navs fast, so the closest poll to a passing nav may only read
        # ~3-5k -- crediting only at the 2k target threshold misses navs we clearly
        # flew through (owner: "within 5k just mark it visited so we don't miss"). The
        # in-flight read is name-fast (retry_low=False), so the name can marquee-blip
        # to a low-confidence read at the exact poll we are closest -- and the target
        # cycle flips off the node next poll, so that single blip would PERMANENTLY
        # cost us the credit (the bug that froze the Nav Freya cluster at 25/47).
        # When we are within range but the name is low-confidence, confirm it with one
        # retried read before deciding; only credit a clean (ratio>=0.7) match.
        if (dist is not None and dist <= ENROUTE_K
                and k != want_key and k not in seen_enroute):
            if ratio < 0.7:
                cname, cratio, cdist = read_matched(sector, retry_low=True)
                if cname and cratio >= 0.7 and cdist is not None and cdist <= ENROUTE_K:
                    name, ratio, dist, k = cname, cratio, cdist, key(cname)
            if k and ratio >= 0.7 and k != want_key and k not in seen_enroute:
                seen_enroute.add(k)
                visit(sector, name, dist, f"en-route <{ENROUTE_K:g}k")
        # reached the wanted nav (possibly resolved by the confirm read just above)
        if k == want_key and dist is not None and dist <= VISIT_K:
            visit(sector, want_name, dist, "warp")
            return True
        # distance dropped by CLOSE_MARGIN below the best seen -> we are closing ==
        # warping. Reset the stall window and keep polling. (A live warp makes the
        # distance fall poll-on-poll, so this is the common case while in transit.)
        if dist <= best - CLOSE_MARGIN:
            best = dist
            flat = 0
            time.sleep(POLL_SLEEP)
            continue
        # no improvement this poll. Distance OCR jitters by tenths of a k, so a single
        # flat poll proves nothing -- accumulate. Only after STALL_POLLS consecutive
        # non-improving polls do we conclude the warp is not engaging and act. This is
        # what makes a re-warp safe: if we were really warping the distance WOULD have
        # dropped within the window, so we never re-click (cancel) a live warp.
        flat += 1
        if flat < STALL_POLLS:
            time.sleep(POLL_SLEEP)
            continue
        # Not closing for STALL_POLLS polls -> classify. Reached / parked / wrecked,
        # then (still far + alive) re-warp, then give up.
        if k == want_key and dist <= MINAPPROACH_K:
            print(f"  min-approach {name} {dist:.2f}k (cannot close further)", flush=True)
            visit(sector, want_name, dist, "min-approach")
            return True
        if dist <= VISIT_K:
            print(f"  parked at {name} {dist:.2f}k (not target); re-enumerate", flush=True)
            return False
        # stopped while still FAR from the target. Not "reached", so the other
        # possibility is a WRECK -- check the death panel QUICKLY before re-warping.
        # handle_wreck() is a cheap OCR that returns False fast when alive; blindly
        # re-warping a wrecked ship just burns polls (it stranded the run at Nishara
        # Maru). If wrecked, it tows + undocks and we bail the warp.
        if handle_wreck(sector):
            return False
        # alive but not closing -> the warp dropped / never engaged (toggle cancel, a
        # missed Q keypress). Re-warp, reset the window, and give the warp a moment to
        # spin up before we start judging progress again.
        if rewarps < REWARP_MAX:
            rewarps += 1
            print(f"  STALL at {dist:.2f}k (no progress {STALL_POLLS} polls) "
                  f"-> re-warp {rewarps}/{REWARP_MAX}", flush=True)
            log(sector, "re-warp", f"{name} not closing at {dist:.2f}k")
            warp(sector)
            best = 1e9
            flat = 0
            time.sleep(2)
            continue
        print(f"  STALL: {name} stuck at {dist:.2f}k after {REWARP_MAX} re-warps; giving up",
              flush=True)
        log(sector, "warp-stall", f"{name} unreachable, stuck at {dist:.2f}k")
        return False
    return False


def dump_stuck_shot(sector):
    """Grab the full client window to scratch/stuck-<sector>.png so the operator/LLM can
    SEE the bad state on hand-off. Root-crop with a timeout (an `import -window <id>`
    can wedge the X server -- the documented trap), killing a hung import."""
    os.makedirs(SCRATCH, exist_ok=True)
    path = os.path.join(SCRATCH, f"stuck-{sector}.png")
    sh("bash", "-c",
       f'. "{os.path.join(HERE, "lib.sh")}"; id="$(client_win)" || exit 1; '
       f'read -r x y w h <<< "$(win_abs "$id")"; '
       f'timeout -k 2 8 import -window root -crop "${{w}}x${{h}}+${{x}}+${{y}}" '
       f'+repage "{path}" 2>/dev/null || pkill -9 import 2>/dev/null')
    return path


def escalate_stuck(sector, rounds):
    """No progress for `rounds` rounds: the fast loop is in a bad state it cannot get
    itself out of. Dump a screenshot + the remaining-nav list and exit EXIT_STUCK so
    the wrapper HALTS (no blind relogin) and a human / the LLM can interpret and
    re-route. This is the 'only interpret with the LLM when it goes bad' boundary."""
    st = state.read(sector)
    leftover = [n for n in st["nodes"] if not n["visited"] and not n.get("skipped")]
    shot = dump_stuck_shot(sector)
    print(f"STUCK {sector}: no visit/skip progress for {rounds} rounds -- "
          f"{len(leftover)} nav(s) still unresolved. Handing off.", flush=True)
    print(f"  screenshot: {shot}", flush=True)
    for n in leftover[:20]:
        xy = n.get("xy") or (n.get("x"), n.get("y"))
        print(f"    - {n['name']}  @{xy}  hidden={n.get('hidden')}", flush=True)
    log(sector, "stuck-escalate",
        f"no progress {rounds} rounds; {len(leftover)} unresolved; shot {shot}")
    sys.exit(EXIT_STUCK)


def investigate_stall(sector, attempts, stall_secs):
    """Wall-clock no-progress watchdog (owner ask, 2026-06-21). We have gone
    `stall_secs` with NO nav discovered or visited. Print a WARNING, INVESTIGATE
    (the order we visited in, the navs currently visible-but-unvisited, and a
    screenshot), decide whether we are actually stalled, and if so DROP+SKIP the
    single most-contested unvisited nav so the run advances instead of grinding a
    scanner dead-zone forever. Returns the skipped node name, or None if there was
    nothing left to skip (caller then lets the normal done/escalation path run).

    Unlike escalate_stuck (which HALTS for the operator), this is self-healing: the
    owner set 10 min as the point at which a stuck nav may simply be dropped. Each
    call skips at most one nav and the caller resets the timer, so a dead-zone tail
    costs one window per nav, never the whole run."""
    st = state.read(sector)
    visited = [n["name"] for n in st["nodes"] if n["visited"]]
    unvisited = [n for n in st["nodes"]
                 if not n["visited"] and not n.get("skipped")]
    shown = [n for n in unvisited if not n["hidden"]]
    shot = dump_stuck_shot(sector)
    mins = stall_secs / 60.0
    print(f"  !! WARNING: no nav discovered/visited for {mins:.1f} min "
          f"(threshold {NOPROG_SECS/60:g} min) -- investigating stall", flush=True)
    print(f"     visit order ({len(visited)}): "
          f"{' -> '.join(visited) if visited else '(none)'}", flush=True)
    print(f"     unvisited shown ({len(shown)}): "
          f"{', '.join(n['name'] for n in shown) if shown else '(none)'}", flush=True)
    print(f"     screenshot: {shot}", flush=True)
    log(sector, "stall-investigate",
        f"{mins:.1f}min no progress; {len(shown)} shown unvisited; shot {shot}")
    if not unvisited:
        print("     nothing unvisited left to drop -- not stalled, deferring to "
              "normal completion", flush=True)
        return None
    # The contested nav is the one we have thrown the most warps at without reaching;
    # tie-break / no-attempt fallback = the first remaining shown nav (else any).
    pool = shown or unvisited
    victim = max(pool, key=lambda n: attempts.get(key(n["name"]), 0))
    tries = attempts.get(key(victim["name"]), 0)
    skip_node(sector, victim["name"],
              f"no-progress watchdog: stalled {mins:.1f}min, "
              f"targeted {tries}x without reaching {VISIT_K}k -- dropped")
    print(f"     STALLED -> dropping {victim['name']} "
          f"(attempted {tries}x); resuming", flush=True)
    return victim["name"]


def open_map():
    sh("bash", os.path.join(HERE, "open-map.sh"))


NOT_IN_SPACE = "__NOT_IN_SPACE__"


def actual_sector():
    """Read the AUTHORITATIVE current sector off the map's top-centre title via
    read-sector.sh. Returns the canonical navdata sector name (e.g. 'AdrielPrime'),
    NOT_IN_SPACE if the client is on character-select/login (no game, no map), or
    None if it is in space but the title could not be read/matched. This is ground
    truth: nav-label OCR can be force-matched onto the wrong sector, but the map
    title cannot, and read-sector.sh refuses to OCR a non-game screen."""
    try:
        r = sh("bash", os.path.join(HERE, "read-sector.sh"), timeout=90)
    except subprocess.TimeoutExpired:
        return None
    for ln in r.stdout.splitlines():
        if ln.startswith("SECTOR "):
            return ln.split(None, 1)[1].strip()
        if ln.startswith("FAIL not-in-space"):
            return NOT_IN_SPACE
    return None


def verify_sector(expected, where):
    """Confirm the ship is physically in `expected`. A Request Tow can silently
    carry the ship to a DIFFERENT sector (a wreck in Freya towed us to Adriel
    Prime), after which fuzzy nav matching force-maps foreign nav names onto the
    closest in-sector names and the driver flies the wrong sector reading garbage.
    Reads the map title; if it names a different sector, ABORT for operator/wrapper
    re-route rather than corrupt the ledger. A None read (in space, but one bad
    title OCR) is tolerated -- we do not halt a good run on one bad OCR. But a
    NOT_IN_SPACE read (character-select/login screen) is NEVER tolerated: the whole
    'clicking random shit all over the screen' bug was the driver flying a non-game
    screen because a garbage OCR fuzzy-matched to a real sector. If we are not in
    space, REFUSE to drive -- HALT for the wrapper to re-enter the game."""
    got = actual_sector()
    if got == NOT_IN_SPACE:
        print(f"  ## NOT IN SPACE ({where}): client is on character-select/login, "
              f"not in {expected}. Refusing to drive a non-game screen. HALT.",
              flush=True)
        log(expected, "sector-verify-not-in-space",
            f"{where}: client not in space; aborting EXIT_STUCK")
        sys.exit(EXIT_STUCK)
    if got is None:
        print(f"  sector-verify ({where}): map title unreadable; continuing "
              f"(assuming {expected})", flush=True)
        log(expected, "sector-verify-skip", f"{where}: title unreadable")
        return
    if got == expected:
        log(expected, "sector-verify-ok", f"{where}: map title confirms {expected}")
        return
    print(f"  ## SECTOR MISMATCH ({where}): driving {expected} but the ship is in "
          f"{got} (map title). A tow crossed sectors. Aborting for re-route -- the "
          f"operator/wrapper must return the ship to {expected} (or retarget {got}).",
          flush=True)
    log(expected, "sector-mismatch",
        f"{where}: expected {expected}, map title says {got}; aborting EXIT_STUCK")
    sys.exit(EXIT_STUCK)


# --- map-click navigation (fallback) ---------------------------------------
# The cycle-warp + relocate_far method only ever reaches navs that fall into the
# TARGET CYCLE (scanner range) from some pocket we can stand in. A nav that never
# enters any reachable pocket's range stays invisible to it -- AdrielPrime
# "completed" at 14/23 with 10 such navs skipped. The map, however, draws those
# navs, so we can reach them the way a player does: open the sector map, click the
# nav, and warp to it. We do NOT predict pixels from world coords -- the map
# projection is NON-AFFINE (a least-squares fit collapses distinct navs onto the
# same pixel). Instead we DETECT every marker (detect-nodes), CLICK each to read its
# "Dest:" name (identify_markers), and warp to a marker that names an unvisited nav;
# when none is drawn, REPOSITION to the visible marker nearest a remaining nav to
# drag a fresh slice of the sector into range, then re-survey. Runs only as a
# FALLBACK, after relocate_far is exhausted and BEFORE skipping a nav as unreachable.
MAP_FALLBACK = os.environ.get("ENB_MAP_FALLBACK", "1") == "1"
MAP_MAX_MARKERS = int(os.environ.get("ENB_MAP_MAX_MARKERS", "40")) # markers to probe
MAP_ROUNDS = int(os.environ.get("ENB_MAP_ROUNDS", "30"))           # survey rounds cap
MAP_REPO_DRY = int(os.environ.get("ENB_MAP_REPO_DRY", "3"))        # fruitless repos -> concede
MAP_SETTLE = float(os.environ.get("ENB_MAP_SETTLE", "0.6"))        # post-click settle


def detect_nodes(full):
    """Run detect-nodes.py on a full map shot -> [(wx, wy, colour), ...]."""
    r = sh("python3", os.path.join(HERE, "detect-nodes.py"), full)
    out = []
    for line in r.stdout.splitlines():
        if line.startswith("NODE "):
            p = line.split()
            if len(p) >= 4:
                out.append((int(p[1]), int(p[2]), p[3]))
    return out


def click_map(wx, wy):
    sh("bash", os.path.join(HERE, "click-map.sh"), str(wx), str(wy))
    time.sleep(MAP_SETTLE)


def read_navname(sector):
    """Read the map "Dest: <name>" strip after a map click -> (canonical, ratio)."""
    r = sh("bash", os.path.join(HERE, "read-navname.sh"))
    raw = ""
    for line in r.stdout.splitlines():
        if line.startswith("NAVNAME "):
            raw = line[8:].strip()
    return match(sector, raw)


def map_open_full():
    """Open + center + zoom-out the map and return the FULL screenshot path (None on
    failure)."""
    r = sh("bash", os.path.join(HERE, "open-map.sh"))
    for line in r.stdout.splitlines():
        if line.startswith("FULL "):
            return line[5:].strip()
    return None


# A "glow" marker sits over the planet's bright rim, where pentagons render
# warm-white and MERGE into each other (and the rim) -- one blob centroid can hide
# two stacked navs (verified: a glow blob's centroid landed between Gas Collector
# Ring 1 and the unvisited Gas Collector Ring 36). The rim runs roughly vertical, so
# crack each glow blob by also probing points stepped along it; clicking a spot
# selects the NEAREST nav, so an offset click recovers a merged sibling.
# Merged siblings stack UP-rim from the blob centroid (the centroid sinks to the
# lower marker), so the comb is upward-biased; one downward step catches the rare
# down-stack. 15 probes keeps the survey affordable for a fallback.
GLOW_PROBES = [(dx, dy) for dy in (-30, -20, -10, 0, 12)
               for dx in (-8, 0, 8)]


def _ident_click(sector, wx, wy, ident):
    """Click one pixel, OCR the Dest strip, and record a fresh nav. Returns the key on
    a real read (even if already known), else None."""
    click_map(wx, wy)
    canon, ratio = read_navname(sector)
    if not canon or ratio < MATCH_MIN:
        return None
    k = key(canon)
    if k not in ident:                     # keep the first (real) pixel for this nav
        ident[k] = (wx, wy, canon)
    return k


def identify_markers(sector):
    """Open the map, detect every marker, and click each one to read its "Dest:"
    name. Returns {key: (wx, wy, name)} for the navs drawn on the map RIGHT NOW (a
    nav appears only once it is in scanner range -- PB-22 -- so this is the set the
    target cycle could not reach but the map can). Glow markers (over the planet rim)
    are blob-cracked with offset probes because two navs can merge into one blob."""
    full = map_open_full()
    if not full:
        return {}
    ident = {}             # key -> (wx, wy, canonical name)
    for (wx, wy, col) in detect_nodes(full)[:MAP_MAX_MARKERS]:
        if col == "glow":
            for dx, dy in GLOW_PROBES:     # crack a merged over-rim blob
                _ident_click(sector, wx + dx, wy + dy, ident)
        else:
            _ident_click(sector, wx, wy, ident)
    return ident


def _map_warp(sector, by, ck, cname, how):
    print(f"  MAP-CLICK -> {cname} ({how}); warping", flush=True)
    log(sector, "map-click", f"selected {cname} ({how}); warping")
    _dest["name"] = cname
    _dest["xyz"] = node_xyz(by, ck)
    return warp_to(sector, ck, cname)


def map_reposition(sector, ident, targets, by, used):
    """No unvisited nav is drawn here, but unvisited navs remain. Warp to the visible
    marker that sits CLOSEST (in world coords) to some still-unvisited nav, so each
    reposition walks the ship TOWARD the remainder and drags a new slice of the sector
    into scanner range (verified live: one reposition revealed Radiation Marker 2).
    `used` excludes markers we already repositioned to this call, so we never warp to
    the same spot twice and spin. Returns True if it warped."""
    cand = []
    for mk, (wx, wy, nm) in ident.items():
        if mk in used:
            continue
        if by.get(mk, {}).get("type") == "planet":
            continue            # planets sit at the sector edge; a warp toward one
                                # stalls ~37k out (Earth, HighEarth) and drags in no
                                # new slice -- never a useful reposition waypoint, same
                                # as relocate_far excludes them. The direct-marker
                                # branch still self-skips a planet target after 2 warps.
        mw = node_xyz(by, mk)
        if mw is None:
            continue
        best = min((math.dist(mw, node_xyz(by, tk))
                    for tk in targets if node_xyz(by, tk) is not None),
                   default=None)
        if best is not None:
            cand.append((best, mk, nm, wx, wy))
    if not cand:
        return False
    cand.sort()                            # marker nearest an unvisited nav first
    # Try candidates in order: a single re-click drift (bad OCR / missed marker)
    # must not abort the whole reposition -- fall through to the next-best marker.
    for _d, mk, nm, wx, wy in cand:
        used.add(mk)
        click_map(wx, wy)
        canon, _r = read_navname(sector)
        if key(canon) != mk:               # re-click drifted -- try the next marker
            continue
        print(f"  map-reposition -> {nm} (toward an unvisited nav)", flush=True)
        log(sector, "map-reposition", f"to {nm} to bring unvisited navs into range")
        _dest["name"] = nm
        _dest["xyz"] = node_xyz(by, mk)
        return warp_to(sector, mk, nm)
    return False


def map_click_reach(sector):
    """FALLBACK: reach still-unvisited SHOWN navs via the map instead of skipping
    them. Each round opens the map and identifies the markers drawn right now:

      * If an unvisited target HAS a marker, click it and warp straight to it -- a
        discovered, on-map nav the target cycle simply could not get within range of.
      * Otherwise REPOSITION: warp to the visible marker closest to a remaining nav,
        which walks the ship across the sector and brings a fresh slice into scanner
        range, then re-survey. A reposition that reveals nothing new counts toward a
        dry streak; after MAP_REPO_DRY fruitless repositions we concede.

    Returns the number of navs reached. Bounded by MAP_ROUNDS."""
    reached = 0
    tries = {}
    dry = 0
    used = set()            # markers already repositioned to (don't re-pick and spin)
    seen = set()            # every marker key ever drawn -- our "uncovered sector" set
    for _ in range(MAP_ROUNDS):
        st, by = ledger(sector)
        targets = {key(n["name"]): n["name"] for n in st["nodes"]
                   if not n["visited"] and not n.get("skipped") and not n["hidden"]}
        if not targets:
            break
        ident = identify_markers(sector)
        if not ident:
            print("  map-fallback: no identifiable markers on the map -- conceding",
                  flush=True)
            break

        # A reposition's payoff is delayed: warping to a marker drags a fresh slice
        # of the sector into scanner range, and the VISIT lands on a later round's
        # direct-marker branch. So measure the dry streak by marker DISCOVERY, not by
        # visited-count -- as long as repositioning keeps surfacing navs we have never
        # seen, the walk is still working and must not concede.
        fresh = set(ident) - seen
        seen |= set(ident)
        if fresh:
            dry = 0

        # an unvisited target drawn on the map now -> warp straight to it.
        progressed = False
        for tk, tname in targets.items():
            if tk not in ident:
                continue
            wx, wy, _nm = ident[tk]
            click_map(wx, wy)
            canon, _r = read_navname(sector)
            if key(canon) != tk:           # re-click drifted -- skip this one
                continue
            ok = _map_warp(sector, by, tk, tname, f"marker ({wx},{wy})")
            if ok:
                reached += 1
            else:
                tries[tk] = tries.get(tk, 0) + 1
                if tries[tk] >= MAX_ATTEMPTS:
                    skip_node(sector, tname,
                              f"map-click warped {tries[tk]}x, never within {VISIT_K}k")
            progressed = True
            break
        if progressed:
            dry = 0
            used.clear()                   # moved on progress -> markers are stale
            continue

        # nothing unvisited is drawn here -> reposition toward the remainder.
        if dry >= MAP_REPO_DRY:
            print(f"  map-fallback: {dry} repositions revealed no new marker -- "
                  f"conceding ({len(targets)} left)", flush=True)
            break
        if not map_reposition(sector, ident, targets, by, used):
            print("  map-fallback: nowhere new to reposition to -- conceding",
                  flush=True)
            break
        # The dry counter advances here; the NEXT round's `fresh` check resets it to 0
        # if this reposition brought a previously-unseen marker into range.
        dry += 1
    return reached


# --- gate-leave (sector complete -> cross a gate to the next sector) ---------
# Owner algorithm step 9: once the sector is done (visited covers visible, or visible
# covers possible), find a gate and LEAVE -- preferring a gate to a sector we have not
# visited yet. These node types are usable jump points; disabled-gate is not.
# Crossable gate node types: defined once in gate_route so pick_gates and
# gate_route.has_unexplored never drift out of sync on what counts as a gate.
GATE_TYPES = gate_route.GATE_TYPES

# Set at drive start to the gate we clicked last run that did NOT actually move the
# ship (a flaky/unusable crossing). pick_gates sorts it last so a same-sector retry
# tries a DIFFERENT gate instead of re-clicking the dud. Per-run hint, not persisted.
SOFT_STUCK_GKEY = None
GATE_LEAVE = os.environ.get("ENB_GATE_LEAVE", "0") == "1"


def completed_sectors():
    """Sector names whose ledger is marked complete -- our 'already visited sectors'."""
    done = set()
    sd = WORKDIR
    if not os.path.isdir(sd):
        return done
    import json
    for f in os.listdir(sd):
        if not f.endswith(".json"):
            continue
        try:
            with open(os.path.join(sd, f)) as fh:
                st = json.load(fh)
            if st.get("status") == "complete":
                done.add(st.get("sector"))
        except (OSError, ValueError):
            continue
    return done


def pick_gates(sector):
    """Return ALL usable gates ordered best-first, by their RECORDED destination
    (gate_route), not their display label. A gate's label does NOT name the sector on
    the other side ('Gate to Castor System' in Yokan lands in Ishuan), so the old
    label-vs-completed-name heuristic could not route us out of an already-surveyed
    region -- it re-picked the same first gate and bounced between two completed
    sectors. gate_route remembers where each gate ACTUALLY led, so we try gates whose
    destination is unknown (the frontier) first, then known-but-incomplete, and a
    proven path to an already-complete sector last. A gate with no recorded edge yet
    keeps its ledger order within the unknown band. Returns [] if no usable gate.
    leave_via_gate walks this list so one unreachable/locked gate falls through to the
    next instead of halting."""
    st = state.read(sector)
    gates = [n for n in st["nodes"] if n.get("type") in GATE_TYPES]
    done = {navdata.norm(s) for s in completed_sectors() if s}
    # DOMINANT: a gate node we marked SKIPPED is a death-zone / unreachable gate -- we
    # could not safely fly to it even as a nav target (e.g. 'Gate to Beta Hydri System'
    # in NewEdinburgh wrecked the ship on approach this run). Egress through it would
    # just wreck us repeatedly until the wreck-budget aborts the whole leave, never
    # trying a safe gate. So ALL non-skipped gates are tried before ANY skipped one,
    # regardless of frontier rank: a proven-reachable backtrack beats a death-zone gate.
    # Primary: real-destination rank (unknown frontier first, dead-ends last).
    # Tiebreak 1 (within equal rank, mostly the unknown-frontier band): prefer a gate
    # whose LABEL does not name an already-completed sector. Labels lie about the true
    # destination so this is only a hint, but among two never-crossed gates it biases
    # toward the one pointing at fresh ground over an obvious backtrack.
    # Tiebreak 2: a gate clicked-but-did-not-cross last run sorts last so a retry picks
    # a different gate.
    gates.sort(key=lambda n: (1 if n.get("skipped") else 0,
                              gate_route.gate_rank(sector, n["name"], done),
                              1 if gate_route.label_names_completed(n["name"], done) else 0,
                              1 if key(n["name"]) == SOFT_STUCK_GKEY else 0))
    return gates


def approach_gate(sector, gkey, gname):
    """Park the ship within GATE_USE_K (use-gate-icon range) of gate `gname` so
    leave_via_gate's gate.sh can cross. Returns True once the gate is reached, else False.

    A gate IS a selectable target in the W/D/C nav cycle and warp DOES engage toward it
    (live-verified Ishuan->Yokan, 2026-06-23: 'Gate to Capella System' enumerates at ratio
    0.97 and goto.py warped straight onto it, then gate.sh crossed). So we use the SAME
    proven nav-drive method goto.py uses: enumerate the cycle, select the gate BY NAME, and
    warp to it; when the gate is out of scanner range, hop to the in-range nav whose world
    coords sit nearest the gate (dragging a fresh slice of the sector into range) and retry.
    Stop when the gate is within GATE_USE_K (hand to gate.sh), when no farther hop nav
    remains, or when the round budget is spent.

    (The previous implementation believed 'you cannot warp to a gate' and ONLY hopped
    between gate-adjacent navs -- it never once selected+warped the gate itself, so it
    looped forever between Yokan/Barn's/Traders Run never crossing. That assumption was
    wrong; this is the corrected, live-validated path.)"""
    _, by = ledger(sector)
    gw = node_xyz(by, gkey)
    for _rnd in range(MAP_ROUNDS):
        cyc = enumerate_cycle(sector)
        if cyc is None:                          # fled mid-enumerate; re-round
            continue
        match = next((e for e in cyc if e["key"] == gkey), None)
        if match is None:
            # Gate not in scanner range yet: hop to the in-range nav closest to it.
            if gw is None or not MAP_FALLBACK:
                return False
            inrange = [e for e in cyc if e["key"]
                       and (e["dist"] is None or e["dist"] > VISIT_K)]
            if not inrange:
                print(f"  gate-approach: no farther nav to hop toward {gname}; re-round",
                      flush=True)
                continue

            def gdist(e):
                ew = node_xyz(by, e["key"])
                return math.dist(ew, gw) if ew is not None else float("inf")
            hop = min(inrange, key=gdist)
            print(f"  gate-approach: hop toward {gname} via {hop['name']} "
                  f"({gdist(hop):.1f}k from gate)", flush=True)
            if select_named(sector, hop["key"])[0] is None:
                continue
            warp_to(sector, hop["key"], hop["name"])
            continue
        d = match["dist"]
        if d is not None and d <= GATE_USE_K:
            print(f"  gate-approach: {gname} at {d:.1f}k (<= {GATE_USE_K:g}k use range) "
                  f"-- parked, handing to gate.sh", flush=True)
            return True
        # Gate in range but farther than use range: select it BY NAME and warp onto it.
        if select_named(sector, gkey)[0] is None:
            continue
        if warp_to(sector, gkey, gname):
            print(f"  gate-approach: reached {gname} -- handing to gate.sh", flush=True)
            return True
    print(f"  gate-approach: could not park within {GATE_USE_K:g}k of {gname} in "
          f"{MAP_ROUNDS} rounds", flush=True)
    return False


def leave_via_gate(sector, _wreck_budget=2):
    """Cross a gate out of the finished sector. Walks the usable gates best-first; for
    each it parks the ship as close as possible (approach_gate, map-click for far
    gates), then runs gate.sh which CONFIRMS the use-gate icon is on screen before it
    rotates the capture + clicks. A gate whose icon never appears (locked / not usable
    by this ship) returns exit 2, and we fall through to the next gate rather than
    halting. Returns the gate name on a clicked crossing, else None. Detecting arrival
    + re-identifying the new sector is the caller's job -- this only performs the LEAVE.

    WRECK RECOVERY (Ishuan 2026-06-23): gates draw aggro -- the ship can be DESTROYED
    flying to a far gate. A wrecked hull reads speed 000 forever (the warp orb becomes
    the "<<Distress" beacon), so the old loop saw spd=0 on every gate, concluded "no
    crossable gate," and HALTED on a dead ship. The warp/nav sweep already calls
    handle_wreck everywhere; egress did not. So we now run the SAME deterministic
    detector here -- before egress and after any failed approach -- and on a wreck we
    tow + undock (handle_wreck) and retry the whole egress from the station, bounded by
    _wreck_budget. handle_wreck itself exits cleanly on a tow that fails or crosses
    sectors (the wrapper re-detects + resumes)."""
    # Already wrecked when egress begins? Recover first, then retry from the station.
    if check_wreck():
        if not handle_wreck(sector):
            return None
        if _wreck_budget <= 0:
            print("  gate-leave: repeated wrecks during egress -- aborting", flush=True)
            log(sector, "gate-leave-fail", "repeated wrecks during egress")
            return None
        return leave_via_gate(sector, _wreck_budget - 1)
    gates = pick_gates(sector)
    if not gates:
        print(f"  gate-leave: no usable gate in {sector}; cannot auto-leave", flush=True)
        log(sector, "gate-leave-none", "no usable gate node in sector")
        return None
    for gate in gates:
        gname = gate["name"]
        gkey = key(gname)
        print(f"  gate-leave: heading to {gname} ({gate['type']}) to leave {sector}",
              flush=True)
        if not approach_gate(sector, gkey, gname):
            # The approach may have failed because we were DESTROYED mid-flight (gates
            # cluster mobs), not merely because the gate is far. Distinguish the two:
            # if wrecked, tow + undock and retry the whole egress; else try next gate.
            if check_wreck():
                if not handle_wreck(sector):
                    return None
                if _wreck_budget <= 0:
                    print("  gate-leave: repeated wrecks during egress -- aborting",
                          flush=True)
                    log(sector, "gate-leave-fail", "repeated wrecks during egress")
                    return None
                return leave_via_gate(sector, _wreck_budget - 1)
            print(f"  gate-leave: could not reach {gname} -- trying next gate", flush=True)
            log(sector, "gate-unreachable", f"could not park on gate {gname}")
            continue
        # gate.sh: confirm icon -> rotate capture -> click. exit 0 clicked, 2 no icon.
        r = sh("bash", os.path.join(HERE, "gate.sh"), sector, gname)
        if r.returncode == 0:
            # Remember WHICH gate we left by; the next sector detected resolves this
            # pending crossing into a real (sector,gate)->dest edge (gate_route), so a
            # re-visit prefers an unexplored gate instead of bouncing back through this
            # one. The gate label lies about the destination -- only the edge is truth.
            gate_route.record_cross(sector, gkey)
            log(sector, "gate-left", f"clicked use-gate on {gname} to leave {sector}")
            print(f"  gate-leave: clicked use-gate on {gname}", flush=True)
            return gname
        if r.returncode == 2:
            # No use-gate icon: locked / not usable by this ship. Record it so it stops
            # counting as an unexplored frontier (else the survey never recognises a
            # sector whose only remaining gates it cannot cross).
            gate_route.record_unusable(sector, gkey)
            print(f"  gate-leave: {gname} shows no use-gate icon (locked/unusable) "
                  f"-- trying next gate", flush=True)
            continue
        print(f"  gate-leave: gate.sh error ({r.returncode}) on {gname} -- trying next",
              flush=True)
    print(f"  gate-leave: no crossable gate in {sector} (all unreachable/locked)",
          flush=True)
    log(sector, "gate-leave-fail", "no crossable gate (all unreachable or locked)")
    return None


def main():
    max_rounds = 60
    if "--max-rounds" in sys.argv:
        max_rounds = int(sys.argv[sys.argv.index("--max-rounds") + 1])

    # The sector is OPTIONAL. Omit it (or pass "auto"/"-") and we read the CURRENT
    # sector straight off the map title: read-sector.sh OCRs the title and resolves
    # it to a canonical navdata name -- the same ground truth verify_sector already
    # trusts. An explicit name is still honoured (and cross-checked by verify_sector)
    # for a targeted single-sector run or to override a doubtful OCR. This is what
    # lets a gate crossing flow straight into the next sector with no hand-typed name.
    sector = None
    if len(sys.argv) >= 2:
        a = sys.argv[1]
        if not a.startswith("-") and a != "auto":
            sector = a

    # Map open up front: required both to read the title (auto-detect) and to fly.
    open_map()
    if sector is None:
        sector = actual_sector()
        if sector is None:
            print("AUTO-DETECT FAILED: could not read the map title to identify the "
                  "current sector. Re-run with an explicit name: drive.py <Sector>.",
                  flush=True)
            sys.exit(EXIT_STUCK)
        print(f"auto-detected current sector from map title: {sector}", flush=True)

    # We now know where we actually landed: promote any pending gate crossing into a
    # recorded (from-sector, gate) -> this-sector edge so future gate selection routes
    # by real destinations instead of lying labels (gate_route). If the pending crossing
    # did NOT move us (still the same sector), resolve hands back that gate's key so
    # pick_gates deprioritises it and a retry tries a different gate.
    global SOFT_STUCK_GKEY
    SOFT_STUCK_GKEY = gate_route.resolve(sector)
    if SOFT_STUCK_GKEY:
        print(f"  gate {SOFT_STUCK_GKEY} clicked but did not cross last attempt -- "
              f"deprioritising it this run", flush=True)

    # Refuse sectors that contain a Gravity Well. A gravity well terminates warp
    # mid-flight (server PlayerClass.cpp:1806) and slows the ship -- a screen-only
    # warp-to-the-next-nav run would drift off course and get wrecked. Override
    # only for a deliberate test with ENB_ALLOW_GRAVITY_WELL=1.
    if gravity_wells.has_gravity_well(sector) and os.environ.get("ENB_ALLOW_GRAVITY_WELL") != "1":
        ws = gravity_wells.wells(sector)
        log(sector, "refused", f"gravity well sector ({len(ws)} well(s)) -- skipped")
        print(f"REFUSED: {sector} contains a Gravity Well ({len(ws)} well(s)); "
              f"gravity wells terminate warp and are unsafe for screen-only "
              f"exploration. Skipping this sector.", flush=True)
        for w in ws:
            print(f"    gravity well: {w['name']} ({w['x']},{w['y']},{w['z']}) "
                  f"r={w['radius_k']}k", flush=True)
        sys.exit(3)

    if not os.path.exists(state.path(sector)):
        sh("python3", os.path.join(HERE, "state.py"), "init", sector)
    log(sector, "drive-start", f"autonomous sweep, threshold {VISIT_K}k")
    # GROUND TRUTH before flying a single warp: confirm the ship is actually in the
    # sector we are driving. A prior tow may have left it elsewhere. (In auto-detect
    # mode this re-reads the title, so a tow that lands mid-init is still caught.)
    verify_sector(sector, "drive-start")
    # Ensure a per-sector packet capture is running and labelled for THIS sector
    # (relabels a char-select/gate placeholder, or starts one if none is running).
    # Best-effort: no-ops if the capture worker is not installed. The pcap-start
    # marker lands in actions.log so the UTC frame timestamps line up with events.
    sh("bash", os.path.join(HERE, "pcap.sh"), "ensure", sector)

    attempts = {}             # key -> warps attempted at it without reaching <=2k
    deferred = set()          # keys we stop DIRECTLY targeting (still reachable en-route)
    flee_streak = 0           # consecutive flees with no new visit (wedge detector)
    blind_streak = 0          # consecutive rounds the panel read nothing (HUD/hang)
    last_progress = -1        # visited+skipped seen at the previous round
    stuck_rounds = 0          # consecutive rounds with zero progress (LLM-escalation)
    last_progress_ts = time.time()   # wall-clock of last visit/skip (no-progress watchdog)
    relocs = 0                # cross-sector relocations spent extending scanner range
    rnd = -1
    while True:
        rnd += 1
        # Hard deadman switch (ENB_EXPLORE_KILL_NO_PROGRESS). When armed it is the SOLE
        # no-progress terminator: the round cap and the round-based escalate/blind exits
        # are suppressed below so the run gets its full wall-clock window to recover, and
        # only after KILL_NO_PROGRESS seconds with no visit/skip do we tear the client
        # down and STOP the whole survey (the wrappers do not relogin/continue on this).
        if KILL_NO_PROGRESS and time.time() - last_progress_ts >= KILL_NO_PROGRESS:
            stall = int(time.time() - last_progress_ts)
            log(sector, "kill-no-progress",
                f"no progress {stall}s >= ENB_EXPLORE_KILL_NO_PROGRESS="
                f"{KILL_NO_PROGRESS}s; killing client and stopping")
            print(f"KILL {sector}: no progress for {stall}s >= "
                  f"ENB_EXPLORE_KILL_NO_PROGRESS={KILL_NO_PROGRESS}s; killing client.exe "
                  f"and stopping the run.", flush=True)
            kill_client()
            sys.exit(EXIT_KILL)
        if not KILL_NO_PROGRESS and rnd >= max_rounds:
            break
        st, by = ledger(sector)
        total = len(st["nodes"])
        visited = sum(1 for n in st["nodes"] if n["visited"])
        skipped = sum(1 for n in st["nodes"] if n.get("skipped"))
        print(f"=== round {rnd}: {visited}/{total} visited"
              f"{f', {skipped} skipped' if skipped else ''} ===", flush=True)
        if visited + skipped >= total:
            break

        # any progress since last round means we are not wedged -> reset the detectors.
        # Resetting `relocs` here is what keeps relocate_far from becoming the spin the
        # owner deleted (2026-06-21): RELOC_MAX bounds CONSECUTIVE no-progress
        # relocations only -- the moment a relocation brings a new nav into range and
        # we visit it, the budget refills. A relocation that reveals nothing burns the
        # budget and the sector then completes; it can never loop for 50 minutes.
        progress = visited + skipped
        if progress > last_progress:
            flee_streak = 0
            relocs = 0
            stuck_rounds = 0
            last_progress_ts = time.time()
        else:
            stuck_rounds += 1
            # Wall-clock watchdog: 10 min with no nav discovered/visited -> investigate
            # and drop the most-contested nav so the run advances (owner: "you can drop
            # and skip but only for 10m since last progress"). This advances `progress`,
            # so it also clears the round-based STUCK_ROUNDS escalation below.
            if time.time() - last_progress_ts >= NOPROG_SECS:
                if investigate_stall(sector, attempts, time.time() - last_progress_ts):
                    last_progress_ts = time.time()
                    stuck_rounds = 0
                    last_progress = -1   # force the next round to see the skip as progress
                    continue
            # The round-based escalate halts for operator/LLM re-route -- but if the
            # deadman is armed, the operator has opted into "keep trying, then kill", so
            # we do NOT bail early here; the wall-clock kill at the top of the loop owns
            # termination instead.
            if stuck_rounds >= STUCK_ROUNDS and not KILL_NO_PROGRESS:
                escalate_stuck(sector, stuck_rounds)
        last_progress = progress

        # No forced shield burst here: sweep_round's own throttled shield_guard fires
        # on its first slot (the warp we just finished left _shield["t"] >5s stale), so
        # a separate pre-check just doubled the shield OCR cost (== 000->warp dead time)
        # for no extra safety. Re-add force only if a sector proves too contested.
        res = sweep_round(sector, deferred)

        if res["status"] == "blind":
            # The whole cycle read no nav. Two distinct causes:
            #   (a) HUD not yet settled (just loaded / relogged / arrived) -- a re-open
            #       + settle fixes it within a round or two.
            #   (b) the ship is parked in an EMPTY POCKET with nothing in scanner range
            #       (e.g. interrupted mid-transit) -- retrying the SAME pocket stays
            #       blind forever. relocate_far CANNOT help here: it walks the target
            #       cycle, which is exactly what is empty. The MAP, however, still draws
            #       every reachable nav, so a map-click toward one drags us back into a
            #       populated pocket. That is the only recovery for a blind pocket.
            if client_hung():
                raise HangDetected()
            blind_streak += 1
            print(f"  blind round {blind_streak} (no nav in target range here)",
                  flush=True)
            open_map()
            time.sleep(1.5)
            if MAP_FALLBACK and blind_streak >= 2:
                got = map_click_reach(sector)
                if got:
                    print(f"  blind-recover: map-click reached {got} nav(s); "
                          f"resuming normal sweep", flush=True)
                    log(sector, "blind-recover", f"map-click reached {got} from empty pocket")
                    blind_streak = 0
                    continue
            if blind_streak >= 5 and not KILL_NO_PROGRESS:
                print(f"  blind {blind_streak} rounds and map-click could not recover; "
                      f"stopping WITHOUT marking complete -- needs attention",
                      flush=True)
                log(sector, "blind-stop",
                    f"panel blank {blind_streak} rounds, no map recovery; not completing")
                break
            # Deadman armed: keep retrying map-click recovery every round rather than
            # giving up here; the wall-clock kill at the loop top bounds the spin.
            continue
        blind_streak = 0

        if res["status"] == "fled":
            flee_streak += 1
            if flee_streak >= FLEE_RETRY:
                reroute_from_wedge(sector)  # skip the contested nav + relocate far
                flee_streak = 0
            continue                        # can't explore this round; re-round

        if res["status"] == "warped":
            if res["reached"]:
                attempts.pop(res["key"], None)   # success wipes its failure history
            else:
                k = res["key"]
                attempts[k] = attempts.get(k, 0) + 1
                if attempts[k] >= MAX_ATTEMPTS and k not in deferred:
                    # Do NOT permanently skip here. A direct warp that did not close
                    # within VISIT_K is NOT proof the nav is unreachable -- en-route
                    # pickup (ENROUTE_K), a relocation, or the map-click fallback
                    # routinely bag it minutes later. (Measured on Ishuan: Yokan
                    # Beacon, Menorb Beacon, Sundari, Centar and Traders Run 4 were
                    # all permanently skipped "never within 8.0k" by the old eager
                    # rule, then visited en-route at 1-5k -- the ledger then carried
                    # them as BOTH visited and skipped.) Just stop DIRECTLY
                    # re-targeting it so one stubborn nav cannot eat the run; the real
                    # skip happens only in the done-path below, after relocate +
                    # map-fallback have all failed, with an honest reason.
                    deferred.add(k)
                    log(sector, "defer",
                        f"{res['name']} not reached in {attempts[k]} direct warps -- "
                        f"deferring direct re-targeting (en-route/relocate/map still active)")
                    print(f"  DEFER {res['name']} (direct warp x{attempts[k]} did not "
                          f"close; still reachable en-route/relocate/map)", flush=True)
        else:   # status == "done": no unvisited nav in scanner range from THIS pocket
            # STAY ON THE W/D/C CYCLE (owner). An empty pocket does NOT mean the sector
            # is done -- unvisited navs may simply be out of scanner range from here. The
            # cycle-native fix is to REPOSITION: warp toward the farthest nav we can see
            # so a fresh slice of the sector falls into scanner range (and new navs
            # reveal/visit en route). That is fast and screen-only. The slow map-click
            # probe is a SEVERE last resort, used ONLY after RELOC_MAX cycle repositions
            # have failed to surface anything new -- never the first move.
            #   - HIDDEN navs never reveal by reflying -- skip them now.
            leftover = [n for n in st["nodes"]
                        if not n["visited"] and not n.get("skipped")]
            for n in (x for x in leftover if x["hidden"]):
                skip_node(sector, n["name"],
                          "hidden nav never revealed within scanner range")
            shown = [n for n in leftover if not n["hidden"]]
            if not shown:
                break

            # 1) W/D/C cycle reposition FIRST (fast). relocate_far warps toward the
            #    farthest nav to drag a new slice into range; relocs caps CONSECUTIVE
            #    fruitless repositions and resets on any visit progress.
            if relocs < RELOC_MAX and relocate_far(sector):
                relocs += 1
                print(f"  done-sweep; repositioned via W/D/C to extend scanner range "
                      f"({relocs}/{RELOC_MAX})", flush=True)
                continue

            # 2) cycle repositions exhausted -> SEVERE fallback: the sector map. Reach
            #    still-unvisited shown navs the way a player does (click marker + warp).
            drawn = set()
            if MAP_FALLBACK:
                drawn = set(identify_markers(sector))
                on_map = [n for n in shown if key(n["name"]) in drawn]
                if on_map:
                    print(f"  done-sweep: W/D/C cycle exhausted after {relocs} "
                          f"repositions; {len(on_map)}/{len(shown)} unvisited navs are on "
                          f"the map -- SEVERE-stuck map-click fallback", flush=True)
                    log(sector, "map-fallback",
                        f"cycle exhausted; {len(on_map)} navs on map; map-click")
                    got = map_click_reach(sector)
                    if got:
                        print(f"  map-click reached {got} nav(s); resuming W/D/C sweep",
                              flush=True)
                        relocs = 0
                        continue

            # 3) Neither the cycle nor the map can reach the remainder from any pocket we
            #    can stand in -> genuinely unfindable. Skip + done.
            for n in shown:
                reason = ("drawn on map but warp never closed within "
                          f"{VISIT_K}k" if key(n["name"]) in drawn else
                          "off the W/D/C cycle and the map from every reachable pocket "
                          "-- scanner dead-zone / unfindable")
                skip_node(sector, n["name"], reason)
            print(f"  every reachable nav visited; skipped {len(shown)} off-cycle/"
                  f"off-map after {relocs} repositions -- sector done, moving on",
                  flush=True)
            break

    st, by = ledger(sector)
    visited = sum(1 for n in st["nodes"] if n["visited"])
    skipped = [n["name"] for n in st["nodes"] if n.get("skipped")]
    total = len(st["nodes"])
    if visited + len(skipped) >= total:
        sh("python3", os.path.join(HERE, "state.py"), "complete", sector)
        log(sector, "complete",
            f"{visited}/{total} visited, {len(skipped)} skipped")
        print(f"COMPLETE {sector} {visited}/{total} visited; "
              f"skipped({len(skipped)}): {skipped}", flush=True)
        # owner algorithm step 9: sector done -> find a gate and leave it, preferring a
        # gate to a sector we have not visited yet. Still gated behind ENB_GATE_LEAVE
        # because a crossing lands us in a NEW sector -- drive.py drives exactly one
        # sector and exits. The cross-gate hand-off (re-detect the new sector off the
        # map title, re-init its ledger, drive on) lives in survey.sh, which sets
        # ENB_GATE_LEAVE=1 and loops. A bare `drive.py <S>` leaves GATE_LEAVE off so a
        # one-off run does not silently fly out of the sector you asked for.
        if GATE_LEAVE:
            leave_via_gate(sector)
    else:
        left = [n["name"] for n in st["nodes"]
                if not n["visited"] and not n.get("skipped")]
        print(f"INCOMPLETE {sector} {visited}/{total}; left: {left}; "
              f"skipped: {skipped}", flush=True)


if __name__ == "__main__":
    try:
        main()
    except HangDetected:
        # client wedged (frozen frame). The ledger already persists every visit so
        # far; exit EXIT_HANG so run-sector.sh kills + re-logs-in and resumes here.
        sec = sys.argv[1] if len(sys.argv) > 1 else "?"
        try:
            log(sec, "hang", "hard client hang detected (frozen frame) -- "
                "exiting for kill+relogin")
        except Exception:
            pass
        print(f"HANG {sec}: client hard-hung (frozen frame); exiting {EXIT_HANG} "
              f"for kill+relogin", flush=True)
        sys.exit(EXIT_HANG)
