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

    # the wanted nav's coords, so when it is out of scanner range we hop to the
    # in-range nav that is CLOSEST to it (heading toward it, not just farthest).
    _, by = drive.ledger(sector)
    gx, gy, gz = drive.node_xyz(by, want_key)

    drive.log(sector, "goto-start", f"heading to {want_name}")
    for rnd in range(rounds):
        cyc = drive.enumerate_cycle(sector)
        if cyc is None:                       # fled mid-enumerate; re-round
            continue
        match = next((e for e in cyc if e["key"] == want_key), None)
        if match is None:
            print(f"round {rnd}: {want_name} not in scanner range "
                  f"({len([e for e in cyc if e['key']])} navs in range)", flush=True)
            # hop to the in-range nav CLOSEST to the wanted nav -- but NEVER one we
            # are already sitting on (dist<=VISIT_K): warping to a 0k target just
            # cancels instantly and we loop. Only nodes we must actually fly to.
            inrange = [e for e in cyc if e["key"]
                       and (e["dist"] is None or e["dist"] > drive.VISIT_K)]
            if not inrange:
                print("  no farther nav to hop via; re-round", flush=True)
                continue

            def gdist(e):
                ex, ey, ez = drive.node_xyz(by, e["key"])
                return math.dist((ex, ey, ez), (gx, gy, gz))
            hop = min(inrange, key=gdist)
            print(f"  hop toward {want_name} via {hop['name']} "
                  f"({gdist(hop):.1f}k from target)", flush=True)
            if drive.select_named(sector, hop["key"])[0] is None:
                print("  hop select failed; re-round", flush=True)
                continue
            drive.warp_to(sector, hop["key"], hop["name"])
            continue
        d = match["dist"]
        print(f"round {rnd}: {want_name} in range at {d}k", flush=True)
        if d is not None and d <= drive.VISIT_K:
            print(f"ARRIVED {want_name} {d}k", flush=True)
            drive.log(sector, "goto-arrived", f"{want_name} @{d}k")
            return 0
        # select the gate BY NAME (index counting drifts onto the wrong nav), then warp
        name, _ = drive.select_named(sector, want_key)
        if name is None:
            print("  select failed; retry", flush=True)
            continue
        if drive.warp_to(sector, want_key, want_name):
            # warp_to records the visit; confirm we are actually close
            print(f"warp_to returned reached for {want_name}", flush=True)
            return 0
    print(f"FAILED to reach {want_name} in {rounds} rounds", flush=True)
    return 1


if __name__ == "__main__":
    sys.exit(main())
