#!/usr/bin/env python3
"""Regression guard for the galaxy gate-routing map + its wiring into drive_lua.

Run: python3 test_galaxy.py   (exit 0 = all pass). No DB or live client needed --
it reads the committed data/galaxy.json and imports drive_lua's pure resolvers.

The Freya death-trap assertion is the load-bearing one: a wrong forbidden-guard
routed the LV122 survey ship into Freya (pirate-lethal) and destroyed it once
already (owner 2026-07-09)."""
import os, sys, json, collections

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import drive_lua as D  # noqa: E402

G = D.GALAXY
fails = []


def check(cond, msg):
    print(("  ok  " if cond else "FAIL  ") + msg)
    if not cond:
        fails.append(msg)


def sid_of(name):
    return next(int(k) for k, v in G["sid2name"].items() if v == name)


print("galaxy.json structure:")
check(G is not None, "galaxy.json loaded")
for key in ("gate_dest", "gate_dest_by_sector", "wormhole_pairs", "system_gate",
            "system_entry", "adjacency", "sector_system", "sid2name", "systems"):
    check(key in G, f"has '{key}'")

VC = sid_of("Vishao's Cove")
FREYA = sid_of("Freya")
SATURN = sid_of("Saturn")
GLENN = sid_of("Glenn")

print("\npaired-name wormhole (Antares Frontier <-> Vishao's Cove):")
check(D.gate_dest_sid("Vishao's Gate", 1505) == VC, "Vishao's Gate from Antares Frontier -> Vishao's Cove")
check(D.gate_dest_sid("Vishao's Gate", VC) == 1505, "Vishao's Gate from Vishao's Cove -> Antares Frontier")

print("\ninter-system gate resolves by source system:")
check(D.gate_dest_sid("Gate to Beta Hydri System", SATURN) == GLENN,
      "Gate to Beta Hydri System from Saturn -> Glenn")

print("\nFreya death-trap forbidden-guard (load-bearing):")
check(D.gate_forbidden("Gate to Aragoth System", SATURN) is True,
      "Gate to Aragoth System from a Sol sector is FORBIDDEN (lands in Freya)")
check(D.gate_dest_sid("Gate to Aragoth System") == FREYA,
      "src-less 'Aragoth System' override still guards Freya")

print("\nlegacy suffix fallback preserved:")
check(D.gate_dest_sid("Sector Gate to Saturn") == SATURN,
      "src-less sector gate still resolves via suffix")

print("\nAntares Frontier is connected to the galaxy:")
adj = {int(k): [int(x) for x in v] for k, v in G["adjacency"].items()}
check(VC in adj.get(1505, []), "Antares Frontier adjacency includes Vishao's Cove")


def route(a, b):
    q, seen = collections.deque([[a]]), {a}
    while q:
        p = q.popleft()
        if p[-1] == b:
            return p
        for nb in adj.get(p[-1], []):
            if nb not in seen:
                seen.add(nb)
                q.append(p + [nb])
    return None


for dest in ("High Earth", "Glenn", "Cooper"):
    check(route(1505, sid_of(dest)) is not None,
          f"route Antares Frontier -> {dest} exists")

print()
if fails:
    print(f"{len(fails)} FAILURE(S)")
    sys.exit(1)
print("all galaxy routing checks passed")
