#!/usr/bin/env python3
# SPDX-License-Identifier: MIT
# Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
# License: LICENSES/Freya
#
# drive_lua.py -- explore-sector driver over the enbmod Lua command channel.
#
# This is the ZERO-SCREEN-INTERACTION replacement for drive.py's screenshot+OCR
# survey loop. Every fact and every action goes through the injected enbmod
# runtime's enb.* API, never a screenshot:
#
#   identity     enb.navs() names -> navdata.identify()   (was: map-title OCR)
#   gate cross   enb.sector() numeric id change            (was: map-title OCR)
#   nav list     enb.navs()   {gid,name,dist,class}        (was: W/D/C cycle OCR)
#   arrival      nav.dist <= VISIT_K                        (was: speed-readout OCR)
#   select+warp  enb.request_target(gid) + enb.warp()      (was: warp-orb click)
#   gate transit enb.request_target(gid) + enb.gate()      (was: gate-icon click)
#
# It reuses the SAME ledger (state.py) and action log (logaction.sh) as the screen
# driver, so completion accounting is byte-identical: a sector is DONE only when
# our ledger shows every node visited or skipped. The nav universe still comes from
# docs/sectors/json/<Sector>.jsonl via navdata.py; the game supplies live gid + dist,
# and we join game nav -> ledger node by normalized name (navdata.norm).
#
# Hard rules carried over from the skill (SKILL.md):
#   - Target order is HYBRID: farthest-unvisited first until >=20% done, then nearest.
#   - A nav counts visited within VISIT_K (8k); en-route, anything within VISIT_K of
#     the ship is marked visited so a fly-by is never missed.
#   - When the reachable set is dry but unvisited ledger nodes remain, RELOCATE to the
#     farthest present nav (bounded) to drag a fresh slice into scanner range before
#     conceding the remainder unreachable (skip).
#
# It never touches the server/proxy/client wire; it only drives the client the same
# way a player would, through the dev command channel.
import json
import math
import os
import re
import subprocess
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import navdata  # noqa: E402
from gravity_wells import GRAVITY_WELL_SECTORS  # noqa: E402

# Authoritative sector identity: enb.sector() numeric id -> jsonl stem. Nav-name
# identification stays only as the fallback for unmapped sectors (needs >=3 names).
with open(os.path.join(HERE, "..", "..", "..", "..", "docs", "sectors",
                       "sector_ids.json")) as _f:
    SID_MAP = json.load(_f)

# ---- tunables (mirror drive.py's, env-overridable) --------------------------
VISIT_K = float(os.environ.get("ENB_VISIT_K", "8000"))      # within this = visited
# Engage-refusal arrival slop: a warp exits at the target's no-warp standoff,
# which for a big-radius object (Earth Station: ~9.5k) sits OUTSIDE VISIT_K, and
# from inside that ring the next engage refuses (nothing to warp toward). An
# engage refusal this close IS arrival -- without the slop the station burns two
# 90s engage cycles and gets blacklisted as unreachable (attempt 10).
ARRIVE_SLOP = float(os.environ.get("ENB_ARRIVE_SLOP", "12000"))
# Planets/moons/suns halt an approach at the BODY's radius, far outside
# ARRIVE_SLOP (Luna parks the ship at ~17.9k from center): an engage refusal or
# parked stall against a body-class node inside this ring is arrival at the
# body, not a dead target. The old single slop blacklisted Luna as "warp never
# engages" after it had flown 31k -> 17.9k and was sitting on the boundary.
BODY_SLOP = float(os.environ.get("ENB_BODY_SLOP", "30000"))
# A live nav whose CLASS is a celestial body (Planet/Moon/Sun/Star) is the body
# itself, not a waypoint we fly to: warp holds off at its radius ("target too
# close"), so re-picking it just spams a warp that never engages. The ledger types
# these as generic "object" (Ceres, Thule), so the ledger type is NOT enough to
# tell them apart from a real object waypoint -- only the live nav class is.
BODY_CLASSES = frozenset({"planet", "moon", "sun", "star"})


def is_body_class(cls):
    return (cls or "").strip().lower() in BODY_CLASSES


# Some hazard navs cancel the warp drive ~1s after engage (the environment kills
# it), so a warp can NEVER close on them -- re-targeting just burns a self-
# cancelling warp forever, the same ping-pong the planet bodies caused. Unlike a
# body (which the ship can at least coast up to its radius and count as reached),
# these are unreachable by warp outright, so they are skipped, never visited-at-
# range. The client reports no distinct object class for them (they read like an
# ordinary nav), so the NAME is the only discriminator.
UNWARPABLE_NAME_SUBSTRINGS = ("reactor drain zone",)


def is_unwarpable(name):
    n = (name or "").lower()
    return any(s in n for s in UNWARPABLE_NAME_SUBSTRINGS)


NEAR_SWITCH = float(os.environ.get("ENB_NEAR_SWITCH", "0.20"))  # farthest->nearest at 20%
WARP_TIMEOUT = float(os.environ.get("ENB_WARP_TIMEOUT", "90"))  # max s per warp segment
POLL_SLEEP = float(os.environ.get("ENB_POLL_SLEEP", "2"))   # in-flight poll cadence
WARP_COOLDOWN = float(os.environ.get("ENB_WARP_COOLDOWN", "5"))  # >=5s between warps
NAV_SETTLE_S = float(os.environ.get("ENB_NAV_SETTLE", "20"))    # wait for scene to populate
RELOC_MAX = int(os.environ.get("ENB_RELOC_MAX", "4"))       # relocations before conceding
GATE_TIMEOUT = float(os.environ.get("ENB_GATE_TIMEOUT", "30"))  # s to wait for sector flip
MAX_SECTORS = int(os.environ.get("ENB_MAX_SECTORS", "40"))  # stop after this many sectors
LUA_TIMEOUT = float(os.environ.get("ENB_LUA_TIMEOUT", "6"))  # per-command reply wait

# ---- hang recovery / relogin (ports run-sector.sh's role for the Lua path) ---
# The enbmod Lua channel IS the liveness signal: enbmod's command poll is clocked
# off the client's message pump, so a HARD-HUNG client stops answering enbmod.cmd
# entirely (this is also why heavy per-tick instrumentation can freeze a live
# client -- a wedged pump answers nothing). A scene LOAD also silences the channel,
# but only briefly (a gate transit's load screen clears in well under 90s); a wedge
# never clears. So the channel is declared dead only after it has been continuously
# unresponsive for HANG_SECS (comfortably above the worst-case load), and only once
# a direct liveness probe confirms it -- then main() relogins (local stack) or halts
# (manual/live client we do not own), exactly as run-sector.sh did around drive.py.
HANG_SECS = float(os.environ.get("ENB_LUA_HANG_SECS", "150"))
MANUAL_CLIENT = os.environ.get("ENB_EXPLORE_MANUAL_CLIENT", "0") == "1"
MAX_RELOGIN = int(os.environ.get("ENB_MAX_RELOGIN", "4"))
LOGIN_DIR = os.path.join(HERE, "..", "..", "login-to-client", "scripts")
# Optional external recovery command (the explore-live skill's recover.sh). When
# set it OWNS bringing a crashed/wedged client back to in-game -- relaunch via the
# real Net-7 launcher, re-inject enbmod, autologin -- so a MANUAL_CLIENT wedge
# recovers instead of halting. Run with the driver's env (creds/ENB_CLIENT_DIR
# inherited); exit 0 == back in-game. Empty -> old behaviour (relogin/halt).
RECOVER_CMD = os.environ.get("ENB_RECOVER_CMD", "").strip()
EXIT_HANG = 42   # client wedged and we could not (manual mode) relogin-recover
EXIT_WELL = 3    # arrived in a gravity-well sector we cannot auto-route out of
EXIT_INCAP = 44  # ship incapacitated (hull 0) -- no respawn primitive, halt for manual revive


class ClientHang(Exception):
    """The enbmod Lua channel went silent past HANG_SECS and a direct liveness
    probe confirmed the client is wedged (not merely mid scene-load). Raised by
    lua(); caught in main()'s hop loop, which relogins + resumes or halts."""


class ShipRecovered(Exception):
    """The ship was incapacitated (hull 0) and auto distress->tow relocated it to
    a station (a DIFFERENT sector than the one being surveyed). Raised by
    check_incapacitated() after recovery + undock; caught in main()'s hop loop,
    which re-detects the sector from scratch (no hop consumed). survey_sector does
    NOT catch it -- its loop must abort, since the ship is no longer where it was."""


US, RS = "\x1f", "\x1e"  # unit / record separators for the nav dump string

# Ledger jsonl coords are kilo-units: in-game nav range ~= coord distance * 1000
# (verified: Earth ABA-gate -> High-Earth-gate hypot 115.9 vs live d=117377).
COORD_SCALE = 1000.0

# Per-sector flyby memory: norm(name) -> gid for EVERY nav the client has listed
# this sector, in range or not right now. Beltway-style sectors only reveal their
# mid-sector navs DURING a crossing; by the time a leg ends at a gate they are out
# of scanner range again, so "currently listed" targeting alone flies straight
# past 90% of the sector (that false-conceded ABA at 3/41). A gid stays targetable
# after it leaves range, so remembering it is enough to warp back.
SEEN = {}
# Estimated ship position (jsonl kilo-units): the coords of the last ledger node
# we arrived at. Good enough to rank out-of-range candidates by distance.
EST_POS = None
# Per-sector dead targets: norm(name) of every nav whose warp engage failed
# repeatedly (request_target succeeds, warp() answers true, zero motion) and
# that arrival detection could not account for. This is a LAST-RESORT wedge
# breaker, not a physics claim: warp engages toward anything (players, mobs,
# bodies) -- the two "no path" verdicts of the 3-sector demo were both
# ARRIVALS the driver misread (nav de-registered at close range / planet body
# radius outside the slop), which target_dist()/BODY_SLOP now catch first.
# Without the penalty pick_target re-picks the same dead target every loop
# (~90s of verified engage attempts each time) and the sector wedges.
BLACKLIST = set()
WARP_FAILS = {}  # lkey(name) -> consecutive warp_to engage failures
# Live-DB name -> ledger name join for spelling variants (norm(live) -> norm(ledger)).
# The live DB and the ledger dataset spell a handful of navs differently (live
# "Infiniti Campus" / "Systems Express 1" vs ledger "Infinity Campus" /
# "System Express 1"); the exact-norm join left those 5 real reachable navs
# permanently unpickable, so Earth went "dry" at 20/38 and burned its whole
# relocation budget ping-ponging between two far anchors. Rebuilt per sector.
ALIAS = {}
# Per-sector gids whose ledger visit is already recorded. Sectors carry
# DUPLICATE nav names (ABA has three distinct 'Mining Station' objects), so a
# visit must consume exactly one ledger row per physical object: keying
# progress by name alone re-marked the same row forever while the duplicate
# siblings starved (attempt 14 wedged ABA at 28/41 warping the nearest
# 'Mining Station' in place every loop). Seeded from the ledger's visited_gid
# stamps at sector start so restarts stay idempotent.
MARKED_GIDS = set()
# Per-sector gates OBSERVED anywhere during the sweep -> norm(name) -> {gid, name,
# dist}. choose_gate runs on the nav list from the sweep's FINAL parked position,
# but in a large sector the onward gates sit 200k+ out and are not in scanner
# range from wherever the sweep ended -- so "currently listed gates" alone made
# the run false-conclude "no onward gate" and stop while unsurveyed neighbours
# were reachable (ABA stopped at 3 sectors with live gates to Beta 1077 + Saturn
# 1071 both out of range at the parked spot). Every gate that enters range at any
# point in the sweep is remembered here (nearest observed dist kept), and
# choose_gate picks from the union so a far gate is still a candidate.
GATES_SEEN = {}


def lkey(name):
    """Ledger key for any nav name (live or ledger spelling)."""
    k = navdata.norm(name)
    return ALIAS.get(k, k)


# ---- client command channel -------------------------------------------------
def _client_pid_cwd():
    """Resolve the RUNNING client.exe's working dir from /proc/<pid>/cwd, via its
    X window (xdotool getwindowpid). This is the authoritative enbmod-store dir for
    a client we ATTACHED to -- an arbitrary WINE prefix / multibox slot the launcher
    settings.json does NOT know about (settings.json points at whatever OUR launcher
    last wrote, which is the wrong prefix for an attached client; see the
    'enbmod dir from /proc cwd' project note). Returns the dir only if it actually
    holds the enbmod store (enbmod.dll / enbmod.cmd), so it never hands back a
    launcher window's unrelated cwd."""
    try:
        wins = subprocess.run(["xdotool", "search", "--class", "client.exe"],
                              capture_output=True, text=True, timeout=10)
        for win in wins.stdout.split():
            pid = subprocess.run(["xdotool", "getwindowpid", win],
                                 capture_output=True, text=True, timeout=5).stdout.strip()
            if not pid:
                continue
            try:
                cwd = os.path.realpath(f"/proc/{pid}/cwd")
            except OSError:
                continue
            if os.path.isfile(os.path.join(cwd, "enbmod.dll")) or \
               os.path.isfile(os.path.join(cwd, "enbmod.cmd")):
                return cwd
    except Exception:
        pass
    return None


def client_dir():
    """Resolve the folder holding enbmod.cmd/enbmod.log. Order:
      1. ENB_CLIENT_DIR (explicit override; explore-live sets this).
      2. The running client.exe's /proc/<pid>/cwd (correct for an ATTACHED client
         in any WINE prefix -- see _client_pid_cwd).
      3. The launcher settings.json ClientPath (our own launched client).
    (Never hardcode -- there can be more than one WINE prefix.)"""
    env = os.environ.get("ENB_CLIENT_DIR")
    if env and os.path.isdir(env):
        return env
    cwd = _client_pid_cwd()
    if cwd:
        return cwd
    settings = os.environ.get(
        "ENB_LAUNCHER_SETTINGS",
        os.path.join(HERE, "..", "..", "..", "..", "tools", "LaunchFreya",
                     "bin", "Debug", "net10.0", "FreyaLauncher.settings.json"),
    )
    with open(settings) as f:
        return os.path.dirname(json.load(f)["ClientPath"])


CDIR = client_dir()
CMD = os.path.join(CDIR, "enbmod.cmd")
LOG = os.path.join(CDIR, "enbmod.log")


def _log_len():
    try:
        with open(LOG, "rb") as f:
            return sum(1 for _ in f)
    except FileNotFoundError:
        return 0


def _lua_raw(expr, timeout):
    """Send one Lua line and return the FIRST new [run] payload (everything after
    '[run] '), CR-stripped, or None on timeout. The DLL writes CRLF, so \\r must be
    stripped before any compare (bit us once). No hang detection -- used both by the
    normal path (lua) and by the liveness probe, so it must never recurse."""
    start = _log_len()
    with open(CMD, "a") as f:
        f.write(expr + "\n")
    deadline = time.time() + timeout
    while time.time() < deadline:
        try:
            with open(LOG, "r", errors="replace") as f:
                lines = f.readlines()[start:]
        except FileNotFoundError:
            lines = []
        for ln in lines:
            ln = ln.rstrip("\r\n")
            if ln.startswith("[run] "):
                return ln[len("[run] "):]
        time.sleep(0.15)
    return None


# time.time() of the last successful channel reply -- the clock the hang detector
# measures silence against. None until the first reply (so a dead-at-startup client
# fails main()'s sanity check cleanly rather than raising a mid-run hang).
_last_alive = None


def _probe_alive():
    """Direct liveness confirmation: does the channel answer 1+1 at all? A few
    quick tries -- a wedged client (pump stalled) answers none of them."""
    for _ in range(3):
        if _lua_raw("return 1+1", LUA_TIMEOUT) is not None:
            return True
    return False


def lua(expr, timeout=LUA_TIMEOUT):
    """Run one Lua line through the enbmod channel; return the FIRST new [run]
    payload, CR-stripped. None on timeout / no output.

    Raises ClientHang when the channel has been silent past HANG_SECS AND a direct
    liveness probe confirms the client is wedged. A gate-transit load screen (the
    channel's routine silence) stays under HANG_SECS, so it never trips this; only a
    real wedge does. main() catches ClientHang to relogin + resume or halt."""
    global _last_alive
    r = _lua_raw(expr, timeout)
    now = time.time()
    if r is not None:
        _last_alive = now
        return r
    if _last_alive is None:
        _last_alive = now            # first-ever contact: start the clock, don't hang
    elif now - _last_alive > HANG_SECS and not _probe_alive():
        raise ClientHang(f"channel silent {now - _last_alive:.0f}s (> {HANG_SECS:.0f}s)")
    return None


def lua_bool(call):
    """Evaluate a boolean-returning Lua CALL (no 'return' prefix). The channel echoes
    a bare boolean as the literal '<boolean>' with no value, so wrap in tostring()
    to get 'true'/'false' back."""
    r = lua(f"return tostring({call})")
    return bool(r) and "true" in r.lower()


# ---- game reads -------------------------------------------------------------
# The enbmod cmd/log channel truncates each reply line at 2048 bytes. A full
# nav dump for a busy belt sector is ~3KB / 77 records, so a single-string dump
# silently loses everything past record ~53 -- and the lost tail is the DISTANT
# navs, i.e. exactly the onward/exit GATES choose_gate needs. So page the dump:
# snapshot enb.navs() into a Lua global once (consistent view), then read fixed-
# size chunks that each stay well under the 2KB cap.
NAV_PAGE = 24   # records/page; ~40 B each => ~1KB, safely under the 2048 B line cap


def _nav_page_expr(off):
    snap = "_ndump=enb.navs() or {}; " if off == 0 else ""
    return (
        snap + "local t={}; "
        f"for i={off + 1},math.min(#_ndump,{off + NAV_PAGE}) do local v=_ndump[i]; "
        "t[#t+1]=(v.gid or 0)..'\\31'..(v.name or '')..'\\31'.."
        "string.format('%.1f',v.dist or -1)..'\\31'..(v.class or '') end; "
        "return table.concat(t,'\\30')"
    )


def get_navs():
    """Live nav list -> [{gid, name, dist(or None), cls}]. dist None == no range
    read. Paged around the 2KB channel-line cap (see NAV_PAGE): page 0 snapshots
    the list into a Lua global, later pages read that same snapshot so the view is
    consistent even though each page is a separate channel round-trip."""
    out = []
    off = 0
    while off < NAV_PAGE * 60:          # hard bound (1440 navs) -- no runaway loop
        r = lua(_nav_page_expr(off))
        if not r:                       # None (timeout) or "" (past the end)
            break
        recs = r.split(RS)
        for rec in recs:
            parts = rec.split(US)
            if len(parts) < 4:
                continue
            gid, name, dist, cls = parts[0], parts[1], parts[2], parts[3]
            try:
                gid = int(gid)
            except ValueError:
                continue
            try:
                d = float(dist)
                d = None if d < 0 else d
            except ValueError:
                d = None
            out.append({"gid": gid, "name": name, "dist": d, "cls": cls})
            # Remember every gate the instant it enters range, with its nearest
            # observed distance, so choose_gate can still target one that has since
            # left range by the sweep's end (see GATES_SEEN).
            if is_gate(name, cls):
                key = navdata.norm(name)
                prev = GATES_SEEN.get(key)
                best = d if d is not None else (prev or {}).get("dist")
                if prev is not None and prev.get("dist") is not None and d is not None:
                    best = min(prev["dist"], d)
                GATES_SEEN[key] = {"gid": gid, "name": name, "dist": best, "cls": cls}
        if len(recs) < NAV_PAGE:        # short page => that was the last one
            break
        off += NAV_PAGE
    return out


def get_sector_id():
    r = lua("return enb.sector()")
    if r is None:
        return None
    try:
        return int(r.strip())
    except ValueError:
        return None


def settle_navs():
    """Poll until the scene populates and the nav count stops climbing (a fresh
    sector needs ~18s to render its objects). Returns the final nav list."""
    prev, stable, waited = -1, 0, 0.0
    navs = []
    while waited < NAV_SETTLE_S + 15:
        navs = get_navs()
        c = len(navs)
        if c > 0 and c == prev:
            stable += 1
            if stable >= 2:
                break
        else:
            stable = 0
        prev = c
        time.sleep(POLL_SLEEP)
        waited += POLL_SLEEP
    return navs


def settle_well_gates(sector, visited_sids, tried_edges, sid, leaving_defer=False):
    """A well sector is not surveyed, so choose_gate gets only what one nav read
    happened to catch -- and a big belt sector renders in bursts (53 -> 77 navs),
    so the distant EXIT gates (98k-205k units out) populate AFTER settle_navs's
    2-stable-read cutoff. A fresh process then sees only the nearby accelerator
    spheres, excludes them (not routable), and false-concedes 'no gate out'.

    Poll for the full render window, accumulating gates into GATES_SEEN (a side
    effect of get_navs), so choose_gate sees the far exit gate too. Early-exit the
    instant a routable non-well onward gate is in range. GATES_SEEN is cleared
    first: this is a new sector and the prior sector's gate memory must not leak in."""
    GATES_SEEN.clear()
    navs, waited = [], 0.0
    while waited < NAV_SETTLE_S + 30:
        navs = get_navs()
        if choose_gate(sector, navs, visited_sids, tried_edges, sid,
                       leaving_defer=leaving_defer) is not None:
            break
        time.sleep(POLL_SLEEP)
        waited += POLL_SLEEP
    return navs


# ---- ledger (state.py) + log (logaction.sh) subprocess wrappers -------------
STATE_DIR = os.environ.get("ENB_EXPLORE_WORKDIR") or os.path.join(HERE, "..", "state")


def ledger_nodes(sector):
    """All ledger nodes (visited or not). Reads the ledger file directly because
    state.py has no coords-of-one-node command and the remaining dump excludes
    visited nodes (we arrive at visited gates too)."""
    try:
        with open(os.path.join(STATE_DIR, sector + ".json")) as f:
            st = json.load(f)
    except (FileNotFoundError, json.JSONDecodeError):
        return []
    return st.get("nodes", [])


def ledger_xyz(sector, name):
    """(x, y, z) of a ledger node (jsonl kilo-units), or None."""
    key = lkey(name)
    for n in ledger_nodes(sector):
        if navdata.norm(n["name"]) == key:
            return (n["x"], n["y"], n["z"])
    return None


def ledger_type(sector, name):
    """The ledger node's object type ('planet', 'moon', 'station', ...), or None."""
    key = lkey(name)
    for n in ledger_nodes(sector):
        if navdata.norm(n["name"]) == key:
            return n.get("type")
    return None


def arrive_slop(sector, name):
    """Engage-refusal arrival slop for this node: body-class nodes stop the ship
    at their radius, well outside the ~9.5k station standoff ring."""
    t = (ledger_type(sector, name) or "").lower()
    return BODY_SLOP if t in ("planet", "moon", "sun", "star") else ARRIVE_SLOP


def ledger_name(sector, name):
    """The ledger's own spelling for a (possibly live-DB-spelled) nav name --
    state.py matches names by exact norm, so every visit/skip must carry the
    ledger spelling or it silently lands nowhere."""
    key = lkey(name)
    for n in ledger_nodes(sector):
        if navdata.norm(n["name"]) == key:
            return n["name"]
    return name


def build_alias(sector, navs):
    """Join live nav names to ledger node names: exact norm equality claims
    first, then the unmatched leftovers pair one-to-one by difflib ratio
    (best pair first, >= 0.84). Populates ALIAS in place."""
    import difflib
    ledger = {navdata.norm(n["name"]) for n in ledger_nodes(sector)}
    live = {navdata.norm(nv["name"]) for nv in navs}
    pairs = []
    for lv in live - ledger:
        if lv in ALIAS:
            continue
        for lg in ledger - live:
            r = difflib.SequenceMatcher(None, lv, lg).ratio()
            if r >= 0.84:
                pairs.append((r, lv, lg))
    pairs.sort(reverse=True)
    taken = set(ALIAS.values())
    for r, lv, lg in pairs:
        if lv in ALIAS or lg in taken:
            continue
        ALIAS[lv] = lg
        taken.add(lg)
        print(f"  {sector}: name-join live '{lv}' -> ledger '{lg}' ({r:.2f})")


def state(*args):
    return subprocess.run([sys.executable, os.path.join(HERE, "state.py"), *args],
                          capture_output=True, text=True)


def logaction(sector, action, detail=""):
    # logaction.sh is not chmod +x, so invoke it through bash like drive.py does.
    subprocess.run(["bash", os.path.join(HERE, "logaction.sh"), sector, action, detail],
                   capture_output=True, text=True)


def ledger_remaining(sector):
    """Unvisited ledger nodes -> {norm_name: {name, hidden, x, y, z}}."""
    r = state("remaining", sector)
    out = {}
    for line in r.stdout.splitlines():
        parts = line.split("\t")
        if len(parts) < 5:
            continue
        hidden, x, y, z, name = parts[0], parts[1], parts[2], parts[3], "\t".join(parts[4:])
        out[navdata.norm(name)] = {"name": name, "hidden": hidden == "1",
                                   "x": float(x), "y": float(y), "z": float(z)}
    return out


def ledger_counts(sector):
    """(visited, total) parsed from state.py status header 'S: V/T visited ...'."""
    r = state("status", sector)
    for line in r.stdout.splitlines():
        if "/" in line and "visited" in line:
            try:
                frac = line.split(":", 1)[1].strip().split(" ")[0]
                v, t = frac.split("/")
                return int(v), int(t)
            except (ValueError, IndexError):
                pass
    return 0, 0


# ---- driver core ------------------------------------------------------------
def record_visit(sector, name, gid, dist, note=""):
    """Ledger-visit one nav OBJECT, deduped by gid. state.py consumes the first
    unvisited row of the name and stamps the gid on it, so duplicate-name navs
    each land on their own row; the in-memory set keeps the every-poll re-marks
    of an in-range nav from spamming the log and the ledger. Returns True when
    a fresh visit actually landed."""
    if gid in MARKED_GIDS:
        return False
    MARKED_GIDS.add(gid)
    lname = ledger_name(sector, name)
    state("visit", sector, lname, str(gid))
    logaction(sector, "visit", f"{lname} (d={dist:.0f}{note})")
    return True


def mark_in_range(sector, navs):
    """Visit every ledger node whose live nav is within VISIT_K. Returns how many
    fresh visits landed (used to reset the relocation budget). Also feeds the
    SEEN flyby memory: every nav listed right now stays targetable later."""
    for nv in navs:
        SEEN[lkey(nv["name"])] = nv["gid"]
    # position estimate: the closest in-range ledger node is where we are. Seeds
    # EST_POS right after a gate transit and keeps it fresh on every flyby.
    close = [nv for nv in navs if nv["dist"] is not None and nv["dist"] <= VISIT_K]
    if close:
        _arrived(sector, min(close, key=lambda nv: nv["dist"])["name"])
    remaining = ledger_remaining(sector)
    fresh = 0
    for nv in navs:
        if nv["dist"] is None or nv["dist"] > VISIT_K:
            continue
        if lkey(nv["name"]) not in remaining:
            continue
        if record_visit(sector, nv["name"], nv["gid"], nv["dist"]):
            fresh += 1
    return fresh


def resolve_body_navs(sector, navs):
    """Take celestial-body nav nodes (live class Planet/Moon/Sun/Star) OUT of the
    remaining set so the picker never targets one -- warp holds off at a body's
    radius, so re-picking it just spams 'target too close' forever (Ceres/Thule
    ping-pong). A body already inside BODY_SLOP is as close as the game lets us
    get, so count it visited; a far body cannot be approached and is skipped. The
    real waypoints around it (Ceres 1..6, ...) are separate nav-point nodes the
    survey still visits normally. Returns True if it resolved at least one."""
    remaining = ledger_remaining(sector)
    hit = False
    for nv in navs:
        if not is_body_class(nv["cls"]):
            continue
        key = lkey(nv["name"])
        if key not in remaining or nv["gid"] in MARKED_GIDS:
            continue
        d = nv["dist"]
        if d is not None and d <= BODY_SLOP:
            record_visit(sector, nv["name"], nv["gid"], d, note=" body")
        else:
            lname = ledger_name(sector, nv["name"])
            state("skip", sector, lname,
                  f"{nv['cls'] or 'body'} -- warp holds off at its radius, cannot approach")
            logaction(sector, "skip", f"{lname} ({nv['cls'] or 'body'})")
        hit = True
    return hit


def resolve_unwarpable_navs(sector, navs):
    """Skip warp-cancelling hazard navs (Reactor Drain Zone, ...) out of the
    remaining set so the picker never targets one and the survey flies elsewhere.
    A warp toward one self-cancels ~1s after engage, so it can never be reached by
    warp -- unlike a body it is skipped outright, not counted at range. If the ship
    happened to already be within VISIT_K, mark_in_range (run just before this)
    already visited it, so a still-remaining one is genuinely unreachable."""
    remaining = ledger_remaining(sector)
    hit = False
    for nv in navs:
        if not is_unwarpable(nv["name"]):
            continue
        key = lkey(nv["name"])
        if key not in remaining or nv["gid"] in MARKED_GIDS:
            continue
        lname = ledger_name(sector, nv["name"])
        state("skip", sector, lname, "reactor drain zone -- warp cancels ~1s after "
              "engage, cannot reach; flying elsewhere")
        logaction(sector, "skip", f"{lname} (reactor drain zone)")
        hit = True
    return hit


def is_gate(name, cls):
    """A TRANSIT gate, not a nav that merely mentions one. Every real gate in
    the dataset is named 'Sector Gate to X' / 'Gate to X'; 'Nav Earth Gate' /
    'Nav Centauri Gate' are ordinary navs parked NEAR a gate (live cls is nil
    for both, so the name is all there is). The old substring match sent the
    driver into enb.gate() on a plain nav and the transit no-op'd the run."""
    n, c = name.lower(), (cls or "").lower()
    return "gate to " in n or "wormhole" in n or "gate" in c or "wormhole" in c


def pick_target(sector, navs):
    """Choose the next nav to warp to, using the hybrid farthest/nearest order.
    Gates count: they are ledger nodes, and warping to one does NOT transit it
    (only enb.gate() does). Preference order:
      1. PRESENT unvisited navs with a live range (certain: in scanner range now).
      2. SEEN unvisited navs (flyby memory): listed earlier this sector but out of
         range now; still targetable by gid. Range is estimated from EST_POS and
         the ledger coords. Without this, a beltway sector whose navs only show
         mid-crossing false-concedes at 3/41 (attempt 5's ABA).
    BLACKLIST excludes targets whose warp engage verifiably failed twice AND
    that arrival detection could not account for (see the BLACKLIST comment up
    top: a wedge breaker, not a physics claim). Without the exclusion the same
    dead target is re-picked every loop and the sector never progresses
    (attempt 8 wedged exactly there).
    Returns a nav dict (with 'seen': True on tier 2) or None."""
    remaining = ledger_remaining(sector)
    # MARKED_GIDS excludes an already-visited duplicate whose NAME is still in
    # remaining (its sibling rows are unvisited): without it the picker warps
    # the nearest 'Mining Station' in place forever instead of the next one.
    cands = [nv for nv in navs
             if nv["dist"] is not None
             and lkey(nv["name"]) in remaining
             and lkey(nv["name"]) not in BLACKLIST
             and nv["gid"] not in MARKED_GIDS
             and not is_body_class(nv["cls"])
             and not is_unwarpable(nv["name"])]
    seen_tier = not cands
    if seen_tier:
        for key, node in remaining.items():
            if key in BLACKLIST or is_unwarpable(node["name"]):
                continue
            gid = SEEN.get(key)
            if gid is None or gid in MARKED_GIDS:
                continue
            d = None
            if EST_POS is not None:
                d = math.dist(EST_POS, (node["x"], node["y"], node["z"])) * COORD_SCALE
            cands.append({"gid": gid, "name": node["name"],
                          "dist": d if d is not None else 150000.0,
                          "cls": "", "seen": True})
    if not cands:
        return None
    visited, total = ledger_counts(sector)
    frac = (visited / total) if total else 0.0
    farthest_first = frac < NEAR_SWITCH
    # flyby memory is chased NEAREST-first regardless of phase: these are known
    # points scattered behind us, and the long-crossing rationale (free en-route
    # pickups on a fresh slice) does not apply to a backtrack.
    cands.sort(key=lambda nv: nv["dist"], reverse=farthest_first and not seen_tier)
    return cands[0]


def ship_speed():
    """Units/second estimated from nav-range deltas ~1s apart (enb.self() x/y/z are
    uncalibrated flat-offset reads -- they read 0 in space -- so nav dist closure is
    the OCR-free equivalent of the speed readout). Max radial rate across every nav
    with a range: warp is 2000+ u/s, so even well off-axis it clears the threshold;
    a parked ship shows ~0 against every nav."""
    a = {nv["gid"]: nv["dist"] for nv in get_navs() if nv["dist"] is not None}
    t1 = time.time()
    time.sleep(1.0)
    b = {nv["gid"]: nv["dist"] for nv in get_navs() if nv["dist"] is not None}
    t2 = time.time()
    deltas = [abs(b[g] - a[g]) for g in a if g in b]
    if not deltas:
        return None
    return max(deltas) / max(t2 - t1, 0.5)


def ensure_not_warping():
    """enb.warp() is a TOGGLE: firing it while a warp is active TERMINATES that warp
    (the drive_lua equivalent of the screen driver's only-click-the-orb-at-speed-0
    rule). Before engaging, make sure we are not mid-warp: if the ship is moving at
    warp speed, warp_stop() once and wait for it to slow."""
    for attempt in range(20):
        v = ship_speed()
        if v is None or v < 500:
            return
        if attempt == 0:
            lua("return enb.warp_stop()")
        time.sleep(2)


def get_energy():
    r = lua("return string.format('%.3f', enb.vitals().energy or -1)")
    try:
        v = float(r)
    except (TypeError, ValueError):
        return None
    return None if v < 0 else v


def hull_frac():
    """Live hull fill fraction (0.0 == incapacitated) via enb.vitals(), or None if
    unreadable. A ship at hull 0 is INCAPACITATED: the server refuses every action
    ('COMPUTER: you cannot do this while incapacitated'), so it cannot warp or gate
    -- check_incapacitated() catches this and auto distress->tows it to a station."""
    r = lua("return string.format('%.3f', enb.vitals().hull or -1)")
    try:
        v = float(r)
    except (TypeError, ValueError):
        return None
    return None if v < 0 else v


# Native-HUD click targets for incap recovery (client-window-relative pixels, the
# 1280x960 render). The incapacitation flow is the ONLY native-HUD click path left
# in the (otherwise Lua-only) survey: neither the distress orb nor the comm-dialog
# reply has an enbmod primitive. Coords verified live 2026-07-09.
DISTRESS_ORB_XY = (633, 858)   # native HUD distress orb (the warp-orb slot); a click
                               # opens the Station Mechanic comm dialog when incapacitated
TOW_REPLY_XY = (400, 617)      # 'I need a tow' reply -- click the MIDDLE of the button
                               # bar, not the left text edge (the edge click did not
                               # register live; mid-bar did)
_WIN_ORIGIN = None


def _xwin_int(info, label):
    m = re.search(rf"{re.escape(label)}:\s*(-?\d+)", info)
    return int(m.group(1)) if m else None


def _client_win_origin():
    """(abs_x, abs_y) of the client.exe game window's upper-left, cached. Used only
    by incap recovery for absolute XTEST clicks on the native HUD (no keyboard focus
    needed -- same input path as the login/explore skills). Filters to the 1280x960
    game window, never a small child/launcher window."""
    global _WIN_ORIGIN
    if _WIN_ORIGIN is not None:
        return _WIN_ORIGIN
    ids = subprocess.run(["xdotool", "search", "--class", "client.exe"],
                         capture_output=True, text=True).stdout.split()
    for win in ids:
        info = subprocess.run(["xwininfo", "-id", win],
                              capture_output=True, text=True).stdout
        ax, ay = _xwin_int(info, "Absolute upper-left X"), _xwin_int(info, "Absolute upper-left Y")
        w, h = _xwin_int(info, "Width"), _xwin_int(info, "Height")
        if ax is None or w is None:
            continue
        if w >= 1024 and h >= 720:            # the game window, not a child/launcher
            _WIN_ORIGIN = (ax, ay)
            return _WIN_ORIGIN
    return None


def _click_win(wx, wy):
    """XTEST click at a client-window-relative pixel."""
    o = _client_win_origin()
    if o is None:
        raise RuntimeError("client.exe window not found for incap-recovery click")
    subprocess.run(["xdotool", "mousemove", "--sync",
                    str(o[0] + wx), str(o[1] + wy), "click", "1"])


def recover_incapacitated(sector):
    """Automated distress->tow recovery of an incapacitated (hull 0) ship -- the
    owner-taught in-game revive: hail the Station Mechanic via the native distress
    orb, request a tow, and get towed to the last registered station.

    Our hide-ui mod hides the native distress orb, so we first drop to the native
    HUD (`enb.freya_ui_on = false` -- setting the flag directly is enough to make
    the orb hit-testable; no Ctrl+U handler run needed, verified live). Then two
    clicks (orb -> 'I need a tow'), poll the authoritative state for the station
    arrival, restore the Freya HUD, and undock to resume. Retries the two-click
    sequence a few times (a missed orb/reply click just re-opens the dialog).
    HALTS only if the tow never lands or the station did not repair the hull."""
    logaction(sector, "incap-recover", "hull 0 -- distress->tow to last station")
    print(f"[drive_lua] SHIP INCAPACITATED in {sector} (hull 0) -- auto distress->tow "
          f"to last registered station ...", file=sys.stderr)
    lua("enb.freya_ui_on = false")   # reveal the native HUD (distress orb + comm dialog)
    time.sleep(1.0)
    towed = False
    for attempt in range(1, 4):
        _click_win(*DISTRESS_ORB_XY)          # opens the Station Mechanic comm dialog
        time.sleep(1.5)
        _click_win(*TOW_REPLY_XY)             # 'I need a tow'
        deadline = time.time() + 60           # tow is server-driven -- poll for arrival
        while time.time() < deadline:
            if lua("return enb.state()") == "station":
                towed = True
                break
            time.sleep(3)
        if towed:
            break
        print(f"[drive_lua] tow request #{attempt} did not land -- retrying orb+reply",
              file=sys.stderr)
    lua("enb.freya_ui_on = true")             # restore the Freya HUD regardless of outcome
    if not towed:
        print("[drive_lua] distress->tow did not complete after 3 tries -- HALTING for "
              "manual revive (ledger persists; re-run to resume).", file=sys.stderr)
        pcap_stop()
        sys.exit(EXIT_INCAP)
    logaction(sector, "incap-recovered", "towed to last registered station")
    print("[drive_lua] towed to station -- undocking to resume survey", file=sys.stderr)
    ensure_in_space()
    h = hull_frac()
    if h is not None and h <= 0.0:
        print("[drive_lua] hull STILL 0 after tow+undock -- station did not repair; "
              "HALTING to avoid a tow loop (manual revive needed).", file=sys.stderr)
        pcap_stop()
        sys.exit(EXIT_INCAP)


def check_incapacitated(sector):
    """If the ship is incapacitated (hull 0), auto distress->tow it back to a
    station and RAISE ShipRecovered so the caller re-detects the sector. Only fires
    in space (a docked ship also reads hull 0) and debounces one second (a single
    stale 0 during a hiccup must not trigger a tow). No-op when the ship is alive."""
    if not lua_bool("enb.inspace()"):
        return                               # docked / loading -- hull 0 is not incap here
    h = hull_frac()
    if h is None or h > 0.0:
        return
    time.sleep(1.0)                          # debounce a transient/stale 0 read
    h2 = hull_frac()                          # 0.0 is a REAL incap value: test `is None`
    if not lua_bool("enb.inspace()") or h2 is None or h2 > 0.0:  # explicitly -- `h2 or 1.0`
        return                               # would coerce a genuine 0.0 hull to 1.0 and bail
    recover_incapacitated(sector)
    raise ShipRecovered(f"towed out of {sector} after hull-0 incapacitation")


def wait_energy(target, timeout=150):
    """Block until reactor energy >= target (or timeout / unreadable). Warp engage
    eats ~half the pool up front, and a drained reactor is the main cause of failed
    engages and mid-leg drop-outs on back-to-back 300k legs; parked recharge is
    ~1-2%/s, so topping up first is cheap insurance against a wedged leg."""
    deadline = time.time() + timeout
    while time.time() < deadline:
        e = get_energy()
        if e is None or e >= target:
            return
        time.sleep(3)


def target_dist(gid):
    """Object-level distance to gid via the TARGET readout (enb.request_target +
    enb.dist), or None. This survives nav DE-REGISTRATION: some navs (Luna's
    shooting ranges) drop off the nav registry once the ship closes below ~15k,
    so the registry range read goes blind exactly when arrival must be judged --
    the survey then misread "parked on top of the target, engage refuses under
    the ~2k floor" as "warp never engages" and blacklisted a reached node.
    enb.dist() reads the CURRENT target and lags a retarget by a tick, so
    request first and read on a later command."""
    if not lua_bool(f"enb.request_target({gid})"):
        return None
    time.sleep(0.5)
    r = lua("return tostring(enb.dist())")
    try:
        return float(r)
    except (TypeError, ValueError):
        return None


def warp_engage():
    """Fire enb.warp() and VERIFY the ship actually entered warp. The return value
    proves NOTHING: warp() is a toggle that answers true on both engage and
    terminate, and an engage can silently no-op (lingering warp state after a kill,
    low reactor energy -- engage eats ~half the pool up front). Only motion is
    proof. First attempt fires if the pool covers one engage; retries demand a
    near-full recharge (fast retries against an empty reactor just spin)."""
    for attempt in range(5):
        wait_energy(0.45 if attempt == 0 else 0.85)
        lua("return enb.warp()")
        time.sleep(4.0)  # spin-up: closure shows within a few seconds
        for _ in range(3):
            v = ship_speed()
            if v is not None and v >= 300:
                return True
            time.sleep(1.0)
        time.sleep(WARP_COOLDOWN)
    return False


def warp_timeout(dist):
    """Seconds to allow for a warp segment: distance / effective speed, never below
    WARP_TIMEOUT. Effective speed is well under the 2000 u/s warp floor because in
    an obstructed sector (asteroid belts) the warp drops out repeatedly and each
    re-engage costs cooldown + spin-up (~50% duty cycle)."""
    return max(WARP_TIMEOUT, dist / 800.0 + 45.0)


def _arrived(sector, name):
    """Record the arrival position estimate (ledger coords of the reached node)."""
    global EST_POS
    xyz = ledger_xyz(sector, name)
    if xyz is not None:
        EST_POS = xyz


def _visit_at_standoff(sector, name, gid, dist):
    """Ledger-visit a node reached by engage-refusal inside its no-warp standoff
    ring (> VISIT_K, so mark_in_range will never mark it -- attempt 11 looped
    forever re-picking Earth Station because the slop path only set EST_POS)."""
    record_visit(sector, name, gid, dist, note=", at no-warp standoff")
    _arrived(sector, name)


def warp_to(sector, target):
    """Select + warp to a nav, then poll the flight: mark fly-by navs visited, and
    return when the target is within VISIT_K (arrived) or its range stops closing
    (arrived-or-stuck) or the distance-scaled timeout. Returns True if reached.
    Works for SEEN (out-of-range) targets too: the gid is still targetable, the
    live range simply stays unknown until the ship closes into scanner range."""
    gid, name = target["gid"], target["name"]
    # already there: warp will not engage toward anything closer than ~2k, and
    # anything within VISIT_K already counts as visited -- do not burn 5 verified
    # engage attempts (with energy waits) on a target 1.8k away. The visit MUST
    # land here too: returning without recording spun attempt 14 in place on a
    # duplicate-name nav (arrive -> nothing marked -> re-picked -> arrive ...).
    if target["dist"] is not None and target["dist"] <= VISIT_K:
        record_visit(sector, name, gid, target["dist"])
        _arrived(sector, name)
        return True
    if not lua_bool(f"enb.request_target({gid})"):
        # a SEEN gid can go stale (object despawned); forget it so pick_target
        # does not offer it forever.
        SEEN.pop(lkey(name), None)
        logaction(sector, "target-fail", name)
        return False
    ensure_not_warping()
    if not warp_engage():
        # Refusal INSIDE the target's no-warp standoff ring is arrival, not
        # failure (see ARRIVE_SLOP/BODY_SLOP) -- re-read the range before
        # judging. The registry can be blind here (nav de-registered at close
        # range), so fall back to the object-level target readout.
        cur = next((nv for nv in get_navs()
                    if lkey(nv["name"]) == lkey(name)), None)
        d = cur["dist"] if cur and cur["dist"] is not None else target_dist(gid)
        if d is not None:
            if d <= VISIT_K:
                record_visit(sector, name, gid, d)
                _arrived(sector, name)
                return True
            if d <= arrive_slop(sector, name):
                _visit_at_standoff(sector, name, gid, d)
                return True
        # warp_engage already burned 5 verified attempts: this target is not
        # engaging and we are provably NOT on top of it. Two straight warp_to
        # failures (10 attempts) = blacklist so pick_target stops offering it.
        key = lkey(name)
        WARP_FAILS[key] = WARP_FAILS.get(key, 0) + 1
        logaction(sector, "warp-fail", name)
        if WARP_FAILS[key] >= 2:
            BLACKLIST.add(key)
            logaction(sector, "blacklist", f"{name} (warp never engages)")
            # persist the verdict: without the ledger skip a restarted run
            # re-burns ~3min of verified engage attempts on the same dead target
            state("skip", sector, ledger_name(sector, name),
                  "warp never engages (repeated verified engage failure)")
        return False
    logaction(sector, "warp", f"to {name} (gid {gid}, d={target['dist']:.0f})")
    WARP_FAILS.pop(lkey(name), None)
    deadline = time.time() + warp_timeout(target["dist"])
    grace = time.time() + 20  # warp spin-up barely closes distance; don't call it a stall
    last_d, stall, rewarps, blind = target["dist"], 0, 0, 0
    key = lkey(name)
    while time.time() < deadline:
        time.sleep(POLL_SLEEP)
        navs = get_navs()
        mark_in_range(sector, navs)
        cur = next((nv for nv in navs if lkey(nv["name"]) == key), None)
        d = cur["dist"] if cur and cur["dist"] is not None else None
        if d is not None and d <= VISIT_K:
            _arrived(sector, name)
            return True
        if d is None:
            # No registry range to watch: either the target is still out of
            # scanner range, or it DE-REGISTERED because we are on top of it
            # (Luna's shooting ranges vanish from the nav list below ~15k). So
            # when the ship stops moving, judge by the object-level target
            # readout before calling it a drop-out.
            blind += 1
            if blind % 5 == 0 and time.time() > grace:
                v = ship_speed()
                if v is not None and v < 300:
                    td = target_dist(gid)
                    if td is not None and td <= VISIT_K:
                        record_visit(sector, name, gid, td)
                        _arrived(sector, name)
                        return True
                    if td is not None and td <= arrive_slop(sector, name):
                        _visit_at_standoff(sector, name, gid, td)
                        return True
                    if rewarps < 12 and warp_engage():
                        rewarps += 1
                        grace = time.time() + 15
                        continue
                    return False
            continue
        blind = 0
        if d >= last_d - 200:          # not closing -> warp dropped out, or arrived
            if time.time() > grace:
                stall += 1
                if stall >= 3:
                    # Not closing is NOT proof the warp dropped: the client
                    # pathfinds buoy-to-buoy, and a multi-hop leg can fly AWAY
                    # from the final target for a while (the Earth Station path
                    # opens with a 65k->149k divergence). Firing enb.warp() --
                    # a TOGGLE -- while still at warp speed CANCELS the leg
                    # (attempt 12 aborted every 99k relocate ~26s in). Only
                    # treat a stall as a drop-out when the ship is genuinely
                    # slow; otherwise keep riding the path (the distance-scaled
                    # deadline still bounds the whole flight).
                    v = ship_speed()
                    if v is not None and v >= 300:
                        stall = 0
                        continue
                    # Still far from the target: the warp dropped mid-leg
                    # (obstruction / drop-out, routine in an asteroid belt).
                    # Re-engage IN PLACE -- exiting to the outer loop pays
                    # ~15s of ledger churn per 16k hop. ALWAYS BE WARPING.
                    if rewarps < 12 and warp_engage():
                        rewarps += 1
                        grace = time.time() + 15
                        stall = 0
                        continue
                    # re-engage refused: inside the target's no-warp standoff
                    # ring (a station's is ~9.5k, a planet/moon's its body
                    # radius -- Luna parks the ship at ~17.9k) = arrival
                    if d <= arrive_slop(sector, name):
                        _visit_at_standoff(sector, name, gid, d)
                        return True
                    return False
        else:
            stall = 0
        last_d = d
    return False


def _seg_dist(p, a, b):
    """Distance from point p to segment a-b (3D, kilo-units)."""
    ax, ay, az = a
    vx, vy, vz = b[0] - ax, b[1] - ay, b[2] - az
    wx, wy, wz = p[0] - ax, p[1] - ay, p[2] - az
    vv = vx * vx + vy * vy + vz * vz
    t = 0.0 if vv == 0 else max(0.0, min(1.0, (wx * vx + wy * vy + wz * vz) / vv))
    return math.dist(p, (ax + t * vx, ay + t * vy, az + t * vz))


def corridor_score(sector, anchor, remaining, corridor=18.0):
    """How many unvisited ledger nodes lie within `corridor` kilo-units of the
    flight line from EST_POS to this anchor nav. 0 when we cannot place either
    endpoint (falls back to plain farthest-first via the sort tiebreak)."""
    if EST_POS is None:
        return 0
    dst = ledger_xyz(sector, anchor["name"])
    if dst is None:
        return 0
    return sum(1 for node in remaining.values()
               if _seg_dist((node["x"], node["y"], node["z"]), EST_POS, dst)
               <= corridor)


def _dest_sids():
    """norm(sector name) -> sid, for gate-destination resolution. Gate navs
    name their destination in DISPLAY spelling ('Sector Gate to Asteroid Belt
    Alpha') while SID_MAP values are ledger keys ('ABA'); the committed sid
    catalog docs/sectors/sector-ids.md carries the display names."""
    out = {}
    for sid, key in SID_MAP.items():
        out[navdata.norm(key)] = int(sid)
    md = os.path.join(HERE, "..", "..", "..", "..", "docs", "sectors",
                      "sector-ids.md")
    try:
        with open(md) as f:
            for m in re.finditer(r"(\d{3,5})\s+([^\W\d][^·\n]*)", f.read()):
                out.setdefault(navdata.norm(m.group(2)), int(m.group(1)))
    except OSError:
        pass
    return out


DEST_SIDS = _dest_sids()

# Gates whose NAME resolves to no committed sid but that physically deposit the
# ship in a KNOWN sector -- map the destination phrase to the sid it lands in, so
# the resolver flags it (else the frontier walker reads it as unexplored space and
# routes straight in). 'Gate to Aragoth System' from Akerons Gate lands in Freya
# (1750), a pirate-lethal NEVER_EXPLORE sector; without this the survey ship was
# routed into it and destroyed (owner 2026-07-09).
GATE_DEST_OVERRIDES = {navdata.norm("Aragoth System"): 1750}

# Owner-directed NEVER-EXPLORE sectors: hostile-spawn-lethal sectors the auto
# survey must never enter or survey -- only defer to a manual/supervised pass.
# Distinct from a type-41 gravity well (gravity_wells.py): these have no OT_GWELL
# object, the hazard is pirate density. Freya (owner 2026-07-09): "huge grav well
# with pirates, basically no safe nodes, even Brisings is dangerous within seconds
# -- only pass thru, never explore"; it destroyed the LV122 survey ship. Keyed by
# ledger key. Any sector reachable ONLY through one of these is deferred too.
NEVER_EXPLORE_SECTORS = frozenset({"Freya"})


def gate_dest_sid(name):
    """Destination sid named by a gate nav ('Sector Gate to Asteroid Belt
    Alpha' -> 1076), or None when the suffix resolves to no known sector. A few
    gate phrases that resolve to no committed sid but land in a known sector are
    pinned via GATE_DEST_OVERRIDES."""
    low = name.lower()
    if " to " not in low:
        return None
    suffix = navdata.norm(name[low.index(" to ") + 4:])
    return DEST_SIDS.get(suffix) or GATE_DEST_OVERRIDES.get(suffix)


def routable_gate(name):
    """A gate we can actually CROSS to another sector. Every real transit gate in
    the dataset is named 'Gate to X' / 'Sector Gate to X' / a 'Wormhole'; an
    in-sector device (Accelerator Sphere, ...) is picked up by is_gate only
    because its object CLASS matches 'gate', yet warping to it never flips the
    sector. Routing via one wastes a warp and, in a gravity-well sector, sends the
    ship toward the well (warp terminates mid-flight, can strand it). Route only
    via names that read as an inter-sector gate."""
    n = name.lower()
    return " to " in n or "wormhole" in n


def _sector_forbidden(key):
    """True when a sector KEY must never be routed INTO by the auto survey: a
    gravity well (warp terminates mid-flight, can strand the ship) or an owner-
    directed NEVER_EXPLORE sector (pirate-lethal, e.g. Freya). Both are deferred
    to a manual pass -- the traversal only ever transits OUT of one, never in."""
    return key in GRAVITY_WELL_SECTORS or key in NEVER_EXPLORE_SECTORS


def gate_forbidden(name):
    """True when this gate's DESTINATION is a forbidden sector (gravity well or
    NEVER_EXPLORE). Warp terminates mid-flight in a well and a NEVER_EXPLORE sector
    kills the ship, so the traversal never routes INTO either; both are deferred to
    a manual pass. Only the destination is tested, so the reverse gate OUT of a
    forbidden sector (dest = a normal sector) is never excluded -- escaping stays
    possible."""
    dest = gate_dest_sid(name)
    return dest is not None and _sector_forbidden(SID_MAP.get(str(dest)))


def _sector_status(key):
    """Persisted status of a ledger BY KEY: 'complete', 'in-progress', or None when
    no ledger exists yet (an undiscovered sector)."""
    try:
        with open(os.path.join(STATE_DIR, key + ".json")) as f:
            return json.load(f).get("status")
    except (OSError, json.JSONDecodeError):
        return None


def _sector_done(sid):
    """True when the destination's ledger is already complete (persisted
    knowledge, unlike visited_sids which only spans this process)."""
    key = SID_MAP.get(str(sid))
    return key is not None and _sector_status(key) == "complete"


# --- Persistent locked-gate memory -----------------------------------------
# A gate that will not transit (a level/faction/quest-gated crossing the survey
# ship cannot pass) is remembered ACROSS runs so we do not waste the first pick
# on it every single run and, worse, keep reading it as a border to unexplored
# space (a locked gate whose destination name does not resolve otherwise looks
# like a frontier gate -> the walker fixates on it forever). Stored in the
# workdir as {"<SectorKey>|<norm(gateName)>": "__locked__"} -- keyed by NAME (the
# gid is session-scoped and changes run to run, so it cannot key the memory).
GATE_EDGES_PATH = os.path.join(STATE_DIR, "gate_edges.json")
GATE_LOCKED = "__locked__"


def _load_gate_edges():
    try:
        with open(GATE_EDGES_PATH) as f:
            d = json.load(f)
            return d if isinstance(d, dict) else {}
    except (OSError, json.JSONDecodeError):
        return {}


GATE_EDGES = _load_gate_edges()


def _gate_edge_key(sector_key, gate_name):
    return f"{sector_key}|{navdata.norm(gate_name)}"


def gate_is_locked(sector_key, gate_name):
    """True when this (sector, gate) crossing is remembered as non-transitable."""
    return GATE_EDGES.get(_gate_edge_key(sector_key, gate_name)) == GATE_LOCKED


def _save_gate_edges():
    try:
        tmp = GATE_EDGES_PATH + ".tmp"
        with open(tmp, "w") as f:
            json.dump(GATE_EDGES, f, indent=2, sort_keys=True)
        os.replace(tmp, GATE_EDGES_PATH)
    except OSError:
        pass


def record_gate_lock(sector_key, gate_name):
    """Persist that this gate would not transit, so future runs skip it first."""
    k = _gate_edge_key(sector_key, gate_name)
    if GATE_EDGES.get(k) == GATE_LOCKED:
        return
    GATE_EDGES[k] = GATE_LOCKED
    _save_gate_edges()


def clear_gate_lock(sector_key, gate_name):
    """A gate we thought was locked just transited -- drop the stale lock so it is
    no longer deferred to last every run (self-heals a false positive or a gate
    that has since become passable)."""
    k = _gate_edge_key(sector_key, gate_name)
    if GATE_EDGES.get(k) != GATE_LOCKED:
        return
    del GATE_EDGES[k]
    _save_gate_edges()


def _gate_graph():
    """Undirected sector graph from EVERY ledger's gate list, plus the FRONTIER set.
    adj: sector key -> set(neighbour keys). A key is FRONTIER when it is a gate
    destination that still needs surveying (no ledger yet = undiscovered, or a
    ledger not marked complete) and is NOT forbidden (a gravity well or a
    NEVER_EXPLORE sector -- we never route into either). Gates are two-way in EnB,
    so edges are undirected -- this is what lets
    the BFS below measure how far a frontier lies BEYOND a completed sector, so the
    walker heads toward real unexplored space instead of into a completed dead-end
    (a done sector whose gates all loop back among done sectors, e.g. Ceres)."""
    import glob
    adj, frontier = {}, set()
    for f in glob.glob(os.path.join(STATE_DIR, "*.json")):
        key = os.path.basename(f)[:-5]
        try:
            with open(f) as fh:
                d = json.load(fh)
        except (OSError, json.JSONDecodeError):
            continue
        if not isinstance(d, dict) or "nodes" not in d:
            continue
        for n in d["nodes"]:
            if n.get("type") != "gate":
                continue
            name = n["name"]
            # only gates we would actually cross count for routing: an in-sector
            # accelerator sphere is not a route, and a gate INTO a forbidden sector
            # (well or NEVER_EXPLORE) is refused.
            if not routable_gate(name) or gate_forbidden(name):
                continue
            # A gate we KNOW will not transit is not an edge and does NOT border
            # the frontier -- skip it, or its unresolved dest name falsely marks
            # this sector as bordering unexplored space and the walker fixates.
            if gate_is_locked(key, name):
                continue
            dsid = gate_dest_sid(name)
            if dsid is None:
                # destination name resolves to no known sid -> almost certainly an
                # UNDISCOVERED sector. Standing in `key` you are one gate from the
                # unknown, so `key` itself borders the frontier (a BFS dist-0 source).
                frontier.add(key)
                continue
            dkey = SID_MAP.get(str(dsid))
            if dkey is None:
                frontier.add(key)
                continue
            adj.setdefault(key, set()).add(dkey)
            adj.setdefault(dkey, set()).add(key)
            if not _sector_forbidden(dkey) and _sector_status(dkey) != "complete":
                frontier.add(dkey)
    return adj, frontier


def _frontier_hops(adj, frontier):
    """Multi-source BFS: min hop count from every sector key to the nearest frontier
    sector over the undirected gate graph. A key absent from the result cannot reach
    any frontier (a completed dead-end) and is treated as infinitely far."""
    from collections import deque
    dist, dq = {}, deque()
    for k in frontier:
        dist[k] = 0
        dq.append(k)
    while dq:
        u = dq.popleft()
        for v in adj.get(u, ()):
            if v not in dist:
                dist[v] = dist[u] + 1
                dq.append(v)
    return dist


def choose_gate(sector, navs, visited_sids, tried_edges, sid, leaving_defer=False):
    """Pick the next gate to cross from the CURRENT sector (sid), for a graph
    traversal that reaches unexplored sectors even when they sit BEHIND already-
    completed ones. The old linear walk picked only gates to a fresh destination
    and the main loop stopped dead the first time a crossing looped back into a
    surveyed sector -- so from a spawn boxed in by done sectors (Ishuan/Yokan)
    it took two internal gates, looped, and quit while the frontier gates
    (Gate to Sol, Gate to Sirius System) sat untried.

    Now: gates are ranked so a FRESH destination is tried first, then a done
    sector we have NOT yet entered this run (transit through it to reach what is
    beyond), then -- last resort -- a sector we already entered this run. Each
    (sid, gid) edge is crossed at most once (tried_edges), so the traversal is
    finite: it exhausts the reachable gate graph and only then concedes."""
    # Union the gates in range NOW with every gate observed during the sweep: a
    # 200k+ onward gate is out of range at the parked spot but was seen (and its
    # gid captured) mid-sweep, and a gid stays targetable after it leaves range.
    gates = [nv for nv in navs if is_gate(nv["name"], nv["cls"])]
    live_keys = {navdata.norm(g["name"]) for g in gates}
    for key, g in GATES_SEEN.items():
        if key not in live_keys:
            gates.append(g)
    # Never re-cross a gate we already took from this sector (loop guard), and
    # never route INTO a gravity-well sector (can't auto-survey it, can strand
    # the ship). Well sectors are deferred to a manual pass; the reverse gate
    # OUT of a well is not a well destination, so escaping stays possible.
    gates = [g for g in gates
             if (sid, g["gid"]) not in tried_edges
             and routable_gate(g["name"])
             and not gate_forbidden(g["name"])]
    if leaving_defer:
        # Leaving a DEFERRED sector (gravity well / NEVER_EXPLORE): only exit toward
        # a KNOWN, non-forbidden sector, so we retreat to surveyed space instead of
        # diving deeper into an unknown (possibly lethal) cluster behind it -- the
        # Freya cluster (Ragnarok/Nifleheim/...) is deferred to a manual pass.
        gates = [g for g in gates
                 if (d := gate_dest_sid(g["name"])) is not None
                 and SID_MAP.get(str(d)) is not None
                 and not _sector_forbidden(SID_MAP.get(str(d)))]
    if not gates:
        return None

    # Persistent locked-gate memory: a crossing remembered as non-transitable is
    # held back so we do not waste the first pick on it every run. It is only a
    # DEFERRAL, never a hard exclusion -- if every fresh gate is exhausted we fall
    # back to retrying a locked one (a persistent lock can be stale, and retrying
    # it beats a false "graph exhausted" concession that strands the run).
    fresh = [g for g in gates if not gate_is_locked(sector, g["name"])]
    if not fresh:
        g = gates[0]
        print(f"[drive_lua] {sector}: only locked gates remain -- retrying "
              f"'{g['name']}' (persistent lock may be stale).")
        return g
    gates = fresh

    # Frontier-directed routing: a one-hop tier walk wanders into completed
    # dead-ends (Mercury -> Venus -> Ceres, where Ceres only gates back to Venus)
    # before backtracking to the real unexplored frontier (Saturn -> Asteroid Belt
    # Alpha, Akerons Gate -> Aragoth). Rank a transit through a DONE sector by how
    # few hops the nearest frontier lies BEYOND it, so the walker heads toward
    # unexplored space; a done sector with no reachable frontier sorts last.
    adj, frontier = _gate_graph()
    fhops = _frontier_hops(adj, frontier)
    INF = float("inf")

    def rank(g):
        """Lower is better."""
        phys = (g["dist"] is None, g["dist"] or 1e18)
        dest = gate_dest_sid(g["name"])
        if dest is None:
            return (0, 0, 0, *phys)          # unresolved dest name: likely a frontier gate
        if not _sector_done(dest) and dest not in visited_sids:
            return (0, 0, 0, *phys)          # a sector still needing survey, reached directly
        dkey = SID_MAP.get(str(dest))
        hops = fhops.get(dkey, INF)          # frontier distance BEYOND this done sector
        revisit = 1 if dest in visited_sids else 0
        return (1, hops, revisit, *phys)

    gates.sort(key=rank)
    return gates[0]


def cross_gate(gate):
    """Warp to the gate, wait for the warp to drop out (a gate() fired mid-warp at
    8k is outside activation range and is silently ignored), then fire enb.gate()
    and poll enb.sector() for the flip -- retrying the transit a few times since
    the first fire can land while still decelerating. Returns the new sector id,
    or None if it never changed."""
    before = get_sector_id()
    if not lua_bool(f"enb.request_target({gate['gid']})"):
        return None
    # A remembered gate (GATES_SEEN) carries its nearest OBSERVED distance, which
    # may be far smaller than where the ship is parked now; refresh from the live
    # target readout so warp_to_gate's distance-scaled timeout fits the real leg.
    cur_d = target_dist(gate["gid"])
    if cur_d is not None:
        gate = dict(gate, dist=cur_d)
    # close on the gate first so the transit arms cleanly
    warp_to_gate(gate)
    # let the warp drop out at the gate before firing the transit
    slow_deadline = time.time() + 30
    while time.time() < slow_deadline:
        v = ship_speed()
        if v is None or v < 300:
            break
        time.sleep(2)
    for _ in range(3):
        lua(f"return enb.request_target({gate['gid']})")
        lua("return enb.gate()")
        deadline = time.time() + GATE_TIMEOUT
        while time.time() < deadline:
            time.sleep(2)
            now = get_sector_id()
            if now is not None and now != before:
                return now
    return None


def warp_to_gate(gate):
    """Like warp_to but for a gate (no ledger join needed): warp and close to <VISIT_K."""
    lua(f"return enb.request_target({gate['gid']})")
    ensure_not_warping()
    if not warp_engage():
        return
    deadline = time.time() + warp_timeout(gate["dist"] or 100000.0)
    grace = time.time() + 20
    last_d, stall, rewarps = gate["dist"] or 1e18, 0, 0
    key = navdata.norm(gate["name"])
    while time.time() < deadline:
        time.sleep(POLL_SLEEP)
        cur = next((nv for nv in get_navs() if navdata.norm(nv["name"]) == key), None)
        d = cur["dist"] if cur and cur["dist"] is not None else None
        if d is not None and d <= VISIT_K:
            return
        if d is not None:
            if d >= last_d - 200:
                if time.time() > grace:
                    stall += 1
                    if stall >= 3:
                        # multi-hop legs can diverge before closing; a toggle
                        # mid-warp cancels the leg (see warp_to). Re-engage
                        # only when genuinely slow.
                        v = ship_speed()
                        if v is not None and v >= 300:
                            stall = 0
                            continue
                        # mid-leg drop-out: re-engage in place (verified)
                        if rewarps < 12 and warp_engage():
                            rewarps += 1
                            grace = time.time() + 15
                            stall = 0
                            continue
                        return
            else:
                stall = 0
            last_d = d


def survey_sector(sector):
    """Drive one sector to completion via the Lua channel. Returns the final nav
    list (so the caller can pick a gate) once every reachable node is resolved."""
    logaction(sector, "sector-enter", "")
    # resume support: rows visited by a prior run carry their gid stamp; seed
    # the dedup set so a restart neither re-logs them nor lets a duplicate-name
    # visit land on an already-consumed row.
    for n in ledger_nodes(sector):
        if n.get("visited_gid") is not None:
            MARKED_GIDS.add(n["visited_gid"])
    reloc = 0
    used_anchors = set()
    while True:
        check_incapacitated(sector)   # hull 0 -> auto distress->tow (raises ShipRecovered)
        navs = get_navs()
        build_alias(sector, navs)
        mark_in_range(sector, navs)
        resolve_body_navs(sector, navs)
        resolve_unwarpable_navs(sector, navs)
        visited, total = ledger_counts(sector)
        remaining = ledger_remaining(sector)
        if not remaining:
            print(f"  {sector}: all {total} nodes resolved")
            break
        target = pick_target(sector, navs)
        if target:
            print(f"  {sector}: {visited}/{total} -> warp {target['name']} "
                  f"(d={target['dist']:.0f})")
            warp_to(sector, target)
            reloc = 0
            used_anchors.clear()
            time.sleep(WARP_COOLDOWN)
            continue
        # nothing reachable in range but unvisited nodes remain: relocate to drag a
        # fresh slice of the sector into scanner range, bounded by RELOC_MAX.
        # Gates are fine relocation anchors too (warping to one does not transit it).
        # Anchor choice is CORRIDOR-SCORED: prefer the present nav whose flight line
        # from our estimated position passes the most unvisited ledger nodes --
        # perimeter (gate-to-gate) hops never scan a sector's center, but a crossing
        # leg lists mid-sector navs as it passes and SEEN remembers them.
        # Each anchor is used at most once per dry spell: corridor-scored farthest
        # from anchor A is B and from B is A, so without the memory the budget
        # burns ping-ponging one A<->B pair (attempt 13 bounced Accelerator <->
        # ABA gate three times).
        far = [nv for nv in navs if nv["dist"] is not None and nv["dist"] > VISIT_K
               and lkey(nv["name"]) not in BLACKLIST
               and lkey(nv["name"]) not in used_anchors
               and not is_body_class(nv["cls"])
               and not is_unwarpable(nv["name"])]
        if far and reloc < RELOC_MAX:
            far.sort(key=lambda nv: (corridor_score(sector, nv, remaining),
                                     nv["dist"]), reverse=True)
            reloc += 1
            used_anchors.add(lkey(far[0]["name"]))
            print(f"  {sector}: dry -- relocate {reloc}/{RELOC_MAX} to "
                  f"{far[0]['name']} (d={far[0]['dist']:.0f})")
            logaction(sector, "relocate", f"{far[0]['name']} (d={far[0]['dist']:.0f})")
            if warp_to(sector, far[0]):
                # payoff (a new nav entering range) resets the budget in mark_in_range
                if mark_in_range(sector, get_navs()) > 0:
                    reloc = 0
                    used_anchors.clear()
            time.sleep(WARP_COOLDOWN)
            continue
        # relocation budget spent: the remainder is unreachable. Skip it, saying why.
        for key, node in remaining.items():
            if key in BLACKLIST:
                reason = "warp never engages (no nav path to target)"
            elif key in SEEN:
                reason = "unreachable (never entered visit range)"
            else:
                reason = "not in live nav registry (absent from this sector's spawns)"
            state("skip", sector, node["name"], reason)
            logaction(sector, "skip", node["name"])
        print(f"  {sector}: conceded {len(remaining)} unreachable node(s)")
        break
    state("complete", sector)
    logaction(sector, "complete", f"{ledger_counts(sector)[0]} visited")
    return get_navs()


def ensure_in_space():
    """Undock if the character loaded docked. enb.undock() sends the client's own
    STARBASE_REQUEST(action=1) through StationExit; the server launches us. No-op
    (returns false) when already in space.

    The authoritative "am I actually flying" signal is enb.inspace(), NOT enb.state().
    Right after login enb.state() reads "unknown" for a few seconds and then flickers
    "space" WHILE STILL DOCKED (inspace=false) before it settles to "station" -- the
    old check keyed on state=="space" and returned early on that transient, skipping
    the undock. The driver then read the docked sub-sector id (2005 -> 20051) with an
    empty nav registry and stopped. Key entirely off inspace(): true == flying (done),
    false-once-settled == docked (undock)."""
    settle = time.time() + 90
    inspace = False
    while time.time() < settle:
        inspace = lua_bool("enb.inspace()")
        if inspace:
            return                       # already flying -- nothing to undock
        if lua("return enb.state()") == "station":
            break                        # settled docked -- undock below
        time.sleep(2)                    # else state still "unknown" -- keep settling
    if inspace:
        return
    print("[drive_lua] docked -- undocking")
    if not lua_bool("enb.undock()"):
        sys.exit("[drive_lua] enb.undock() refused (world mgr not captured?)")
    deadline = time.time() + 90
    while time.time() < deadline:
        time.sleep(2)
        if lua_bool("enb.inspace()"):
            print("[drive_lua] in space")
            time.sleep(5)  # let the sector scene start populating
            return
    sys.exit("[drive_lua] undock never reached in space")


def relogin_or_halt(relogins):
    """Recover a wedged client, mirroring run-sector.sh's hang branch. In
    manual-client mode we do NOT own the client (the operator launched it under
    WINE against the live server), so we cannot relaunch it -- HALT for the
    operator to relaunch + re-run (the ledger persists, so a re-run resumes where
    it left off), exactly as run-sector.sh does. Otherwise (local stack) kill +
    relogin through the login-to-client skill and resume. Exits the process on an
    unrecoverable state; returns normally once the client is back in-game."""
    global _last_alive
    # An external recovery command (explore-live's recover.sh) supersedes both the
    # manual-halt and the local relogin: it relaunches the real client via the
    # Net-7 launcher and re-injects enbmod, then autologin returns us to in-game.
    if RECOVER_CMD:
        if relogins > MAX_RELOGIN:
            print(f"[drive_lua] {relogins} recoveries; giving up.", file=sys.stderr)
            pcap_stop()
            sys.exit(1)
        print(f"[drive_lua] client wedged -> external recovery (#{relogins}): "
              f"{RECOVER_CMD}", file=sys.stderr)
        rc = subprocess.run(["bash", "-c", RECOVER_CMD]).returncode
        if rc != 0:
            print(f"[drive_lua] recovery command exited {rc}; aborting so the "
                  f"operator can step in (ledger persists -- re-run to resume).",
                  file=sys.stderr)
            pcap_stop()
            sys.exit(EXIT_HANG)
        _last_alive = time.time()   # channel is fresh again; reset the hang clock
        return
    if MANUAL_CLIENT:
        print("[drive_lua] client hard-hung and ENB_EXPLORE_MANUAL_CLIENT=1 -- not "
              "relaunching a client we do not own. Relaunch the client (and your "
              "proxy) yourself, then re-run to resume (the ledger persists).",
              file=sys.stderr)
        pcap_stop()
        sys.exit(EXIT_HANG)
    if relogins > MAX_RELOGIN:
        print(f"[drive_lua] {relogins} hangs; giving up.", file=sys.stderr)
        pcap_stop()
        sys.exit(1)
    print(f"[drive_lua] hard hang -> kill + relogin (#{relogins}) ...", file=sys.stderr)
    subprocess.run(["bash", os.path.join(LOGIN_DIR, "login.sh")])
    # login.sh's in-client auto-login owns EULA/credentials/char-enter; if 08 timed
    # out the client is most likely still loading the scene -- just re-poll it.
    ok = False
    for _ in range(4):
        if subprocess.run(["bash", os.path.join(LOGIN_DIR, "08-wait-ingame.sh")],
                          capture_output=True).returncode == 0:
            ok = True
            break
        time.sleep(5)
    if not ok:
        print(f"[drive_lua] relogin #{relogins} failed; aborting.", file=sys.stderr)
        pcap_stop()
        sys.exit(1)
    _last_alive = time.time()   # channel is fresh again; reset the hang clock


def pcap_ensure(sector):
    """Best-effort: start/relabel a per-sector packet capture (pcap.sh self-gates
    on ENB_PCAP and worker availability, so this no-ops on a local run or an
    uninstalled worker). Called at each sector entry so the capture spans the
    sector's entry handshake, exactly as drive.py did."""
    subprocess.run(["bash", os.path.join(HERE, "pcap.sh"), "ensure", sector])


def pcap_stop():
    subprocess.run(["bash", os.path.join(HERE, "pcap.sh"), "stop"])


def has_gravity_well(sector):
    """True when the sector contains a gravity well (gravity_wells.py exits 2).
    Warp terminates mid-flight in a well sector and we cannot auto-route out, so
    the survey refuses it -- same policy as the screen survey (survey.sh)."""
    return subprocess.run(
        [sys.executable, os.path.join(HERE, "gravity_wells.py"), sector],
        capture_output=True, text=True).returncode != 0


def main():
    global EST_POS
    print(f"[drive_lua] client dir: {CDIR}")
    # sanity: channel alive?
    if lua("return 1+1") != "2":
        sys.exit("[drive_lua] enbmod channel not answering -- is the client in-game with mods?")
    ensure_in_space()

    visited_sids = set()
    tried_edges = set()   # (sid, gid) gates crossed this run -- the traversal loop guard
    hop = 0
    relogins = 0
    # A hop advances on every gate crossing (a survey OR a transit through a done
    # sector to reach the frontier beyond it); a client hang re-enters the SAME
    # hop (the ledger persists, so survey_sector resumes) after relogin, so the
    # counter is not consumed by recovery. run-sector.sh's role, inlined.
    while hop < MAX_SECTORS:
        try:
            sid = get_sector_id()
            # Observed-gate memory is PER-SECTOR: clear it before settle_navs
            # repopulates it for THIS sector, so a pure transit hop (which never
            # hits the survey-path reset below) cannot carry a prior sector's gate
            # gids into choose_gate and fire request_target on a stale target.
            GATES_SEEN.clear()
            navs = settle_navs()
            sector = SID_MAP.get(str(sid))
            if sector is None:
                # no map entry (unsurveyed sector): fall back to identify-by-nav-
                # names, but only with real evidence -- a single shared name like
                # "Sector Gate to Earth" matches many sectors and misidentifying
                # corrupts a ledger (attempt 6 surveyed High Earth into ABA off 1 nav).
                names = [nv["name"] for nv in navs]
                if len(names) < 3:
                    print(f"[drive_lua] sid={sid}: unmapped and only {len(names)} nav "
                          f"name(s) -- not enough to identify safely; stopping.")
                    break
                r = subprocess.run([sys.executable, os.path.join(HERE, "navdata.py"),
                                    "identify", *names], capture_output=True, text=True)
                if r.returncode != 0 or not r.stdout.strip():
                    print(f"[drive_lua] sid={sid}: could not identify sector from "
                          f"{len(names)} navs; stopping.")
                    break
                sector = r.stdout.split("\t")[0].strip()

            # A gravity-well sector cannot be auto-surveyed (warp terminates mid-
            # flight); DEFER it to a manual pass instead of killing the whole run --
            # skip the survey, log it, and let choose_gate route back out (well-dest
            # gates are excluded, so we leave toward a normal sector). Only SURVEY a
            # sector that is not a well, not already complete, and not surveyed this
            # run; otherwise we are just TRANSITING it to reach fresh space.
            check_incapacitated(sector)   # hull 0 -> auto distress->tow (raises ShipRecovered)

            well = has_gravity_well(sector)
            no_explore = sector in NEVER_EXPLORE_SECTORS
            defer = well or no_explore   # skip-survey; only transit OUT to safe space
            transit = defer or sid in visited_sids or _sector_done(sid)
            tag = ("well-skip" if well else "no-explore-skip" if no_explore
                   else "transit" if transit else "survey")
            print(f"[drive_lua] hop {hop}: sid={sid} -> {sector} ({len(navs)} navs) "
                  f"[{tag}]")

            visited_sids.add(sid)
            if defer:
                logaction(sector, "skip-well" if well else "skip-no-explore",
                          "gravity well -- deferred to manual pass" if well else
                          "never-explore (hostile) -- deferred to manual pass")
                # Not surveyed, so let the distant exit gates finish rendering
                # (a belt sector loads in bursts) before choose_gate runs; route out
                # only toward known safe space (never deeper into the cluster).
                navs = settle_well_gates(sector, visited_sids, tried_edges, sid,
                                         leaving_defer=True)
            elif not transit:
                pcap_ensure(sector)   # per-sector capture spanning the entry handshake
                state("init", sector)
                SEEN.clear()      # flyby memory + position estimate are per-sector
                EST_POS = None
                BLACKLIST.clear()  # dead-target memory is per-sector too
                WARP_FAILS.clear()
                ALIAS.clear()      # live<->ledger name join is per-sector
                MARKED_GIDS.clear()  # gid-visit dedup is per-sector (reseeded from ledger)
                navs = survey_sector(sector)

            gate = choose_gate(sector, navs, visited_sids, tried_edges, sid,
                               leaving_defer=defer)
            if not gate:
                if defer:
                    # Boxed inside a deferred (well / never-explore) sector with no
                    # safe exit gate in range -- a manual flight to a gate is needed.
                    # Halt loudly (distinct exit) rather than pretend it finished.
                    kind = "gravity-well" if well else "never-explore (hostile)"
                    print(f"[drive_lua] {sector}: no untried safe gate OUT of this "
                          f"{kind} sector in range -- needs a manual flight to a gate. "
                          f"Fly out and re-run.", file=sys.stderr)
                    pcap_stop()
                    sys.exit(EXIT_WELL)
                print(f"[drive_lua] {sector}: no untried onward gate -- reachable gate "
                      f"graph exhausted; done.")
                break
            print(f"[drive_lua] {sector}: gating via {gate['name']} (gid {gate['gid']})")
            logaction(sector, "enter-gate", gate["name"])
            tried_edges.add((sid, gate["gid"]))   # mark BEFORE crossing so a failed transit is not retried
            new_sid = cross_gate(gate)
            if new_sid is None:
                # Transit failed (gate would not fire / locked / undersized approach).
                # The edge is already marked tried; stay in this sector and pick a
                # different gate next iteration instead of killing the whole run.
                # Persist the lock so future runs skip it first -- but ONLY when the
                # hull is healthy: a transit failure at low/zero hull is a FALSE
                # failure (incapacitated -> the server refuses EVERY action), and
                # ShipRecovered clears tried_edges for exactly that reason; poisoning
                # the persistent cache there would permanently blacklist a good gate.
                hp = hull_frac()
                if hp is not None and hp <= 0.25:
                    print(f"[drive_lua] {sector}: gate '{gate['name']}' did not "
                          f"transit (hull {hp:.2f} -- not recording a lock); "
                          f"trying another gate.")
                else:
                    record_gate_lock(sector, gate["name"])
                    print(f"[drive_lua] {sector}: gate '{gate['name']}' did not "
                          f"transit -- recorded locked; trying another gate.")
                continue
            clear_gate_lock(sector, gate["name"])   # transited -> drop any stale lock
            time.sleep(WARP_COOLDOWN)
            hop += 1
        except ShipRecovered as e:
            # Ship was incapacitated and auto-towed to a station (often the SAME
            # sector's station -- the tow lands at the last registered station).
            # Recovery already undocked us; re-enter the loop to re-detect the
            # sector fresh -- no hop consumed (the tow is not a survey step).
            # Drop tried-gate memory: any gate marked "tried" while incapacitated
            # was a FALSE failure (at hull 0 the server refuses EVERY action, not
            # just that gate), so those edges must be retryable now the hull is
            # restored -- else a tow back into the same sector immediately hits the
            # "graph exhausted" false-completion on its own already-tried gates.
            tried_edges.clear()
            print(f"[drive_lua] {e}; re-detecting sector.", file=sys.stderr)
            continue
        except ClientHang as e:
            # The client wedged mid-hop. Recover (relogin, local stack) or halt
            # (manual/live client). On success, re-enter the SAME hop: get_sector_id
            # + survey_sector resume from the persisted ledger, no hop consumed.
            print(f"[drive_lua] CLIENT HANG: {e}", file=sys.stderr)
            relogins += 1
            relogin_or_halt(relogins)
            print(f"[drive_lua] resumed in-game after hang #{relogins}; "
                  f"re-detecting sector.", file=sys.stderr)
            continue
    pcap_stop()
    print("[drive_lua] survey run finished.")
    print(state("summary").stdout)


if __name__ == "__main__":
    main()
