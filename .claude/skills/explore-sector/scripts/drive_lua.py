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
import subprocess
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import navdata  # noqa: E402

# ---- tunables (mirror drive.py's, env-overridable) --------------------------
VISIT_K = float(os.environ.get("ENB_VISIT_K", "8000"))      # within this = visited
NEAR_SWITCH = float(os.environ.get("ENB_NEAR_SWITCH", "0.20"))  # farthest->nearest at 20%
WARP_TIMEOUT = float(os.environ.get("ENB_WARP_TIMEOUT", "90"))  # max s per warp segment
POLL_SLEEP = float(os.environ.get("ENB_POLL_SLEEP", "2"))   # in-flight poll cadence
WARP_COOLDOWN = float(os.environ.get("ENB_WARP_COOLDOWN", "5"))  # >=5s between warps
NAV_SETTLE_S = float(os.environ.get("ENB_NAV_SETTLE", "20"))    # wait for scene to populate
RELOC_MAX = int(os.environ.get("ENB_RELOC_MAX", "4"))       # relocations before conceding
GATE_TIMEOUT = float(os.environ.get("ENB_GATE_TIMEOUT", "30"))  # s to wait for sector flip
MAX_SECTORS = int(os.environ.get("ENB_MAX_SECTORS", "40"))  # stop after this many sectors
LUA_TIMEOUT = float(os.environ.get("ENB_LUA_TIMEOUT", "6"))  # per-command reply wait

US, RS = "\x1f", "\x1e"  # unit / record separators for the nav dump string


# ---- client command channel -------------------------------------------------
def client_dir():
    """Resolve the folder holding enbmod.cmd/enbmod.log from the launcher settings
    (never hardcode -- there can be more than one WINE prefix)."""
    env = os.environ.get("ENB_CLIENT_DIR")
    if env and os.path.isdir(env):
        return env
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


def lua(expr, timeout=LUA_TIMEOUT):
    """Run one Lua line through the enbmod channel; return the FIRST new [run]
    payload (everything after '[run] '), CR-stripped. None on timeout / no output.
    The DLL writes CRLF, so \\r must be stripped before any compare (bit us once)."""
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


def lua_bool(expr):
    r = lua(expr)
    return bool(r) and "true" in r.lower()


# ---- game reads -------------------------------------------------------------
NAV_DUMP = (
    "local n=enb.navs(); local t={}; "
    "for _,v in ipairs(n) do "
    "t[#t+1]=(v.gid or 0)..'\\31'..(v.name or '')..'\\31'.."
    "string.format('%.1f',v.dist or -1)..'\\31'..(v.class or '') end; "
    "return table.concat(t,'\\30')"
)


def get_navs():
    """Live nav list -> [{gid, name, dist(or None), cls}]. dist None == no range read."""
    r = lua(NAV_DUMP)
    if r is None or r == "":
        return []
    out = []
    for rec in r.split(RS):
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


# ---- ledger (state.py) + log (logaction.sh) subprocess wrappers -------------
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
def mark_in_range(sector, navs):
    """Visit every ledger node whose live nav is within VISIT_K. Returns how many
    fresh visits landed (used to reset the relocation budget)."""
    remaining = ledger_remaining(sector)
    fresh = 0
    for nv in navs:
        if nv["dist"] is None or nv["dist"] > VISIT_K:
            continue
        node = remaining.get(navdata.norm(nv["name"]))
        if node:
            state("visit", sector, node["name"])
            logaction(sector, "visit", f"{node['name']} (d={nv['dist']:.0f})")
            fresh += 1
    return fresh


def is_gate(name, cls):
    n, c = name.lower(), (cls or "").lower()
    return "gate" in n or "gate" in c or "wormhole" in c


def pick_target(sector, navs):
    """Choose the next nav to warp to among PRESENT, unvisited, non-gate navs with a
    known range, using the hybrid farthest/nearest order. Returns a nav dict or None."""
    remaining = ledger_remaining(sector)
    cands = [nv for nv in navs
             if nv["dist"] is not None
             and navdata.norm(nv["name"]) in remaining
             and not is_gate(nv["name"], nv["cls"])]
    if not cands:
        return None
    visited, total = ledger_counts(sector)
    frac = (visited / total) if total else 0.0
    farthest_first = frac < NEAR_SWITCH
    cands.sort(key=lambda nv: nv["dist"], reverse=farthest_first)
    return cands[0]


def warp_to(sector, target):
    """Select + warp to a nav, then poll the flight: mark fly-by navs visited, and
    return when the target is within VISIT_K (arrived) or its range stops closing
    (arrived-or-stuck) or WARP_TIMEOUT. Returns True if the target was reached."""
    gid, name = target["gid"], target["name"]
    if not lua_bool(f"return enb.request_target({gid})"):
        logaction(sector, "target-fail", name)
        return False
    lua("return enb.warp()")
    logaction(sector, "warp", f"to {name} (gid {gid}, d={target['dist']:.0f})")
    deadline = time.time() + WARP_TIMEOUT
    last_d, stall = target["dist"], 0
    key = navdata.norm(name)
    while time.time() < deadline:
        time.sleep(POLL_SLEEP)
        navs = get_navs()
        mark_in_range(sector, navs)
        cur = next((nv for nv in navs if navdata.norm(nv["name"]) == key), None)
        d = cur["dist"] if cur and cur["dist"] is not None else None
        if d is not None and d <= VISIT_K:
            return True
        if d is not None:
            if d >= last_d - 200:      # not closing -> arrived-at-min or stuck
                stall += 1
                if stall >= 3:
                    return d <= VISIT_K
            else:
                stall = 0
            last_d = d
    return False


def choose_gate(sector, navs, visited_sids):
    """Prefer a present gate we have range on; deprioritise the gate we arrived
    through where we can tell (name mentions the previous sector's planet is hard to
    know, so for v1 just take the first gate with a known range)."""
    gates = [nv for nv in navs if is_gate(nv["name"], nv["cls"])]
    gates = [g for g in gates if g["dist"] is not None] or gates
    gates.sort(key=lambda g: (g["dist"] is None, g["dist"] or 1e18))
    return gates[0] if gates else None


def cross_gate(gate):
    """Warp to the gate, fire enb.gate(), and poll enb.sector() for the flip.
    Returns the new sector id, or None if it never changed."""
    before = get_sector_id()
    if not lua_bool(f"return enb.request_target({gate['gid']})"):
        return None
    # close on the gate first so the transit arms cleanly
    warp_to_gate(gate)
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
    lua("return enb.warp()")
    deadline = time.time() + WARP_TIMEOUT
    last_d, stall = gate["dist"] or 1e18, 0
    key = navdata.norm(gate["name"])
    while time.time() < deadline:
        time.sleep(POLL_SLEEP)
        cur = next((nv for nv in get_navs() if navdata.norm(nv["name"]) == key), None)
        d = cur["dist"] if cur and cur["dist"] is not None else None
        if d is not None and d <= VISIT_K:
            return
        if d is not None:
            if d >= last_d - 200:
                stall += 1
                if stall >= 3:
                    return
            else:
                stall = 0
            last_d = d


def survey_sector(sector):
    """Drive one sector to completion via the Lua channel. Returns the final nav
    list (so the caller can pick a gate) once every reachable node is resolved."""
    logaction(sector, "sector-enter", "")
    reloc = 0
    while True:
        navs = get_navs()
        mark_in_range(sector, navs)
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
            time.sleep(WARP_COOLDOWN)
            continue
        # nothing reachable in range but unvisited nodes remain: relocate to drag a
        # fresh slice of the sector into scanner range, bounded by RELOC_MAX.
        far = [nv for nv in navs if nv["dist"] is not None
               and not is_gate(nv["name"], nv["cls"])]
        if far and reloc < RELOC_MAX:
            far.sort(key=lambda nv: nv["dist"], reverse=True)
            reloc += 1
            print(f"  {sector}: dry -- relocate {reloc}/{RELOC_MAX} to "
                  f"{far[0]['name']} (d={far[0]['dist']:.0f})")
            logaction(sector, "relocate", f"{far[0]['name']} (d={far[0]['dist']:.0f})")
            if warp_to(sector, far[0]):
                # payoff (a new nav entering range) resets the budget in mark_in_range
                if mark_in_range(sector, get_navs()) > 0:
                    reloc = 0
            time.sleep(WARP_COOLDOWN)
            continue
        # relocation budget spent: the remainder is a scanner dead-zone. Skip it.
        for key, node in remaining.items():
            state("skip", sector, node["name"], "unreachable (never entered scanner range)")
            logaction(sector, "skip", node["name"])
        print(f"  {sector}: conceded {len(remaining)} unreachable node(s)")
        break
    state("complete", sector)
    logaction(sector, "complete", f"{ledger_counts(sector)[0]} visited")
    return get_navs()


def main():
    print(f"[drive_lua] client dir: {CDIR}")
    # sanity: channel alive?
    if lua("return 1+1") != "2":
        sys.exit("[drive_lua] enbmod channel not answering -- is the client in-game with mods?")

    visited_sids = set()
    for hop in range(MAX_SECTORS):
        sid = get_sector_id()
        navs = settle_navs()
        names = [nv["name"] for nv in navs]
        # identify the sector from the (exact, non-OCR) nav names
        r = subprocess.run([sys.executable, os.path.join(HERE, "navdata.py"),
                            "identify", *names], capture_output=True, text=True)
        if r.returncode != 0 or not r.stdout.strip():
            print(f"[drive_lua] sid={sid}: could not identify sector from "
                  f"{len(names)} navs; stopping.")
            break
        sector = r.stdout.split("\t")[0].strip()
        print(f"[drive_lua] hop {hop}: sid={sid} -> {sector} ({len(navs)} navs)")
        state("init", sector)
        visited_sids.add(sid)

        navs = survey_sector(sector)

        gate = choose_gate(sector, navs, visited_sids)
        if not gate:
            print(f"[drive_lua] {sector}: no gate in range -- done.")
            break
        print(f"[drive_lua] {sector}: gating via {gate['name']} (gid {gate['gid']})")
        logaction(sector, "enter-gate", gate["name"])
        new_sid = cross_gate(gate)
        if new_sid is None:
            print(f"[drive_lua] gate transit did not flip the sector id -- stopping.")
            break
        if new_sid in visited_sids:
            print(f"[drive_lua] gated into already-surveyed sector {new_sid} -- stopping "
                  f"(v1 has no multi-gate route planner).")
            break
        time.sleep(WARP_COOLDOWN)
    print("[drive_lua] survey run finished.")
    print(state("summary").stdout)


if __name__ == "__main__":
    main()
