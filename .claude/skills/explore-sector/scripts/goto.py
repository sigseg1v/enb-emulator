#!/usr/bin/env python3
# SPDX-License-Identifier: MIT
# Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
# License: LICENSES/Freya
#
# goto.py <Sector> "<nav name>" [--rounds N] -- fly the live client to ONE named
# nav and stop within 2k of it, reusing the validated drive.py screen-only method
# (enumerate the target cycle -> select the wanted nav -> warp -> poll to arrival).
# Unlike drive.py this targets a SPECIFIC nav regardless of visited state, so it can
# return to an already-visited gate to cross it. Each round re-enumerates, so a long
# warp that lands short (min-approach stall) simply retries from the closer spot.
#
# Exit 0 once within 2k; exit 1 if the nav never came into scanner range / never
# got within 2k after N rounds.
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import math    # noqa: E402

import drive    # noqa: E402  (reuses its module-level helpers; main() is guarded)
import navdata  # noqa: E402


def main():
    if len(sys.argv) < 3:
        sys.exit('usage: goto.py <Sector> "<nav name>" [--rounds N]')
    sector, want_name = sys.argv[1], sys.argv[2]
    rounds = 12
    if "--rounds" in sys.argv:
        rounds = int(sys.argv[sys.argv.index("--rounds") + 1])
    want_key = navdata.norm(want_name)

    # Beyond this range a DIRECT warp to the target does not land on it: the moment
    # warp engages inside a dense nav field the client auto-re-locks the NEAREST nav
    # (the auto-flip documented in drive._warp_to_impl), so the ship warps to a local
    # nav instead of the far gate and the gate distance never shrinks. So even when
    # the target IS in scanner range, if it is farther than this we HOP via the
    # in-range nav closest to it (walking the ship toward the gate's neighbourhood)
    # rather than blindly direct-warping and getting stolen. Only once we are within
    # HOP_THRESHOLD do we commit a direct warp at the target itself.
    HOP_THRESHOLD = float(os.environ.get("ENB_GOTO_HOP_K", "30.0"))

    # the wanted nav's coords, so when it is out of scanner range we hop to the
    # in-range nav that is CLOSEST to it (heading toward it, not just farthest).
    _, by = drive.ledger(sector)
    g_xyz = drive.node_xyz(by, want_key)
    if g_xyz is None:
        sys.exit(f"goto: {want_name} has no coords in {sector} ledger -- cannot route")
    gx, gy, gz = g_xyz

    def gdist(e):
        xyz = drive.node_xyz(by, e["key"])
        if xyz is None:                   # coordless nav -> never prefer it as a hop
            return 1e9
        return math.dist(xyz, (gx, gy, gz))

    def direct_warp():
        """Select the gate by name and warp straight at it. Returns True if reached."""
        # select the gate BY NAME (index counting drifts onto the wrong nav), then warp
        if drive.select_named(sector, want_key)[0] is None:
            print("  select failed; retry", flush=True)
            return False
        if drive.warp_to(sector, want_key, want_name):
            print(f"warp_to returned reached for {want_name}", flush=True)
            return True
        return False

    # PROGRESS IS JUDGED BY THE REAL GATE DISTANCE `d`, not by hop-node geometry.
    # Scanner range here is effectively unlimited (a 422k gate still enumerates), so
    # the gate is almost always in the cycle and `d` is readable every round. We walk
    # toward the gate via the in-range nav CLOSEST to it; each hop drags a fresh slice
    # of the sector into scanner range, so `d` shrinks round over round. We do NOT gate
    # hops on per-node monotonicity (that broke the across-sector walk: after landing on
    # the closest nav it is excluded, the next-closest is farther, and the walk wrongly
    # concedes). Instead, if `d` itself stops improving for STALL rounds the hop graph
    # is oscillating (a 2-cycle between the gate's two nearest navs) -> direct-warp the
    # gate and let warp_to's rewarp loop grind the rest. `d<=HOP_THRESHOLD` always
    # direct-warps (close enough that auto-flip cannot steal it).
    best_d = float("inf")
    stall = 0
    STALL = int(os.environ.get("ENB_GOTO_STALL", "3"))
    drive.log(sector, "goto-start", f"heading to {want_name}")
    for rnd in range(rounds):
        cyc = drive.enumerate_cycle(sector)
        if cyc is None:                       # fled mid-enumerate; re-round
            continue
        match = next((e for e in cyc if e["key"] == want_key), None)
        d = match["dist"] if match else None
        # ARRIVED: target in range AND within the visit threshold.
        if match is not None and d is not None and d <= drive.VISIT_K:
            print(f"round {rnd}: {want_name} in range at {d}k", flush=True)
            print(f"ARRIVED {want_name} {d}k", flush=True)
            drive.log(sector, "goto-arrived", f"{want_name} @{d}k")
            return 0
        # DIRECT WARP: target in range and close enough that a direct warp lands.
        if match is not None and d is not None and d <= HOP_THRESHOLD:
            print(f"round {rnd}: {want_name} in range at {d}k -- direct warp", flush=True)
            if direct_warp():
                return 0
            continue
        # Track progress on the real gate distance.
        if d is not None:
            if d < best_d - 1.0:
                best_d = d
                stall = 0
            else:
                stall += 1
        # Hop oscillating (d not improving) -> fall back to a direct warp at the gate.
        if stall >= STALL and match is not None:
            print(f"round {rnd}: {want_name} at {d}k -- hops stalled "
                  f"({stall} rounds no progress); direct-warp", flush=True)
            stall = 0
            if direct_warp():
                return 0
            continue
        # Walk toward the gate via the in-range nav CLOSEST to it (excluding the gate
        # itself and any nav we are already sitting on).
        if match is None:
            print(f"round {rnd}: {want_name} not in scanner range "
                  f"({len([e for e in cyc if e['key']])} navs in range)", flush=True)
        else:
            print(f"round {rnd}: {want_name} in range at {d}k -- too far for a direct "
                  f"warp (>{HOP_THRESHOLD:.0f}k); hop closer (best={best_d:.0f}k)",
                  flush=True)
        inrange = [e for e in cyc if e["key"] and e["key"] != want_key
                   and (e["dist"] is None or e["dist"] > drive.VISIT_K)]
        if not inrange:
            print("  no farther nav to hop via; re-round", flush=True)
            continue
        hop = min(inrange, key=gdist)
        print(f"  hop toward {want_name} via {hop['name']} "
              f"({gdist(hop):.1f}k from target)", flush=True)
        if drive.select_named(sector, hop["key"])[0] is None:
            print("  hop select failed; re-round", flush=True)
            continue
        drive.warp_to(sector, hop["key"], hop["name"])
    print(f"FAILED to reach {want_name} in {rounds} rounds", flush=True)
    return 1


if __name__ == "__main__":
    sys.exit(main())
