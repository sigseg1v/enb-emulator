#!/usr/bin/env python3
# SPDX-License-Identifier: MIT
# Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
# License: LICENSES/Freya
"""Persistent gate-destination memory for the sector survey.

A gate's DISPLAY label does NOT name the sector on the other side: 'Gate to
Castor System' in Yokan actually lands in Ishuan, 'Gate to Capella System' in
Ishuan lands in Yokan. So label-matching cannot route the survey out of an
already-surveyed region -- it deterministically re-picks the same first gate and
bounces between two completed sectors until STALE_MAX kills the whole survey,
while the OTHER gates (to genuinely new sectors) sit unused.

This records the ACTUAL sector each (sector, gate) crossing led to, so the
survey can prefer gates whose destination is unknown or not-yet-complete, and
recognise when a completed region is genuinely exhausted (every reachable gate
leads somewhere already done).

State lives in ENB_EXPLORE_WORKDIR so it survives across drive.py invocations and
a resumed survey:
  gate_edges.json   {"<sector>|<gkey>": "<dest_sector>"}
  pending_gate.json {"from": "<sector>", "gkey": "<gkey>"}   (one crossing in flight)

The (sector, gkey) key uses the same navdata-normalised gate key drive.py uses
(navdata.norm of the gate name), so lookups line up with pick_gates.
"""
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import navdata  # noqa: E402
import state    # noqa: E402

# The node types that count as a crossable gate. Single source of truth: drive.py
# imports this set for pick_gates, so has_unexplored() and pick_gates() always agree
# on what a gate is (a narrower set here would make has_unexplored undercount frontier
# gates and stop the survey early).
GATE_TYPES = {"gate", "class-specific-gate", "factioned-gate", "wormhole-exit",
              "hidden-gate"}

# Sentinel destination for a gate that is not usable for departure: either it showed
# no use-gate icon (locked / wrong ship), or it was clicked FAIL_LIMIT times and never
# actually moved the ship (a wormhole-exit you arrive through but cannot leave by).
# Recorded like a real edge so it stops counting as an unexplored frontier -- otherwise
# such a gate keeps the survey believing there is always somewhere new to cross and it
# re-tries the dud on every visit / never recognises exhaustion.
LOCKED = "__locked__"

# Non-crossings (click reported success but the ship never left) before a gate is
# marked permanently unusable. >1 so a single slow-load / missed-click does not
# condemn a gate that actually works.
FAIL_LIMIT = 2


def _wd():
    return os.environ.get("ENB_EXPLORE_WORKDIR") or os.path.join(HERE, "..", "state")


def _edges_path():
    return os.path.join(_wd(), "gate_edges.json")


def _pending_path():
    return os.path.join(_wd(), "pending_gate.json")


def _fails_path():
    return os.path.join(_wd(), "gate_fails.json")


def _load(path, default):
    try:
        with open(path) as f:
            return json.load(f)
    except (OSError, ValueError):
        return default


def _save_atomic(path, obj):
    tmp = path + ".tmp"
    with open(tmp, "w") as f:
        json.dump(obj, f, indent=2)
    os.replace(tmp, path)


def edge_key(sector, gkey):
    return f"{sector}|{gkey}"


def load_edges():
    return _load(_edges_path(), {})


def dest_of(sector, gkey):
    """Recorded destination sector for crossing gate `gkey` in `sector`, or None."""
    return load_edges().get(edge_key(sector, gkey))


def record_cross(sector, gkey):
    """Note that we just clicked through gate `gkey` in `sector`. The destination is
    unknown until the NEXT sector is detected, so stash it as the pending crossing;
    resolve() promotes it to a real edge once we know where we landed."""
    _save_atomic(_pending_path(), {"from": sector, "gkey": gkey})


def record_unusable(sector, gkey):
    """Record that gate `gkey` in `sector` is locked/unusable (no use-gate icon). It
    stops being treated as an unexplored frontier so the survey can recognise a sector
    whose only remaining gates it cannot use."""
    edges = load_edges()
    edges[edge_key(sector, gkey)] = LOCKED
    _save_atomic(_edges_path(), edges)


def resolve(current_sector):
    """If a crossing is pending, record from|gkey -> current_sector, then clear it.
    Returns the gate key that was CLICKED-BUT-DID-NOT-CROSS (pending pointed back at
    the same sector we are still in) so the caller can deprioritise that gate on the
    retry and try a different one; returns None on a real crossing or no pending.

    A non-crossing also bumps a PERSISTENT per-gate fail tally; once it reaches
    FAIL_LIMIT the gate is recorded LOCKED so it stops being treated as a frontier on
    every future visit (a dud wormhole-exit otherwise re-tried forever). A real
    crossing clears that gate's tally."""
    pend = _load(_pending_path(), None)
    stuck = None
    if pend:
        frm, gkey = pend.get("from"), pend.get("gkey")
        if frm and gkey and current_sector:
            if current_sector != frm:
                edges = load_edges()
                edges[edge_key(frm, gkey)] = current_sector
                _save_atomic(_edges_path(), edges)
                _clear_fail(frm, gkey)
            else:
                stuck = gkey  # clicked this gate but never left -- soft-stuck this run
                if _bump_fail(frm, gkey) >= FAIL_LIMIT:
                    edges = load_edges()
                    edges[edge_key(frm, gkey)] = LOCKED
                    _save_atomic(_edges_path(), edges)
    try:
        os.remove(_pending_path())
    except OSError:
        pass
    return stuck


def _bump_fail(sector, gkey):
    fails = _load(_fails_path(), {})
    k = edge_key(sector, gkey)
    fails[k] = int(fails.get(k, 0)) + 1
    _save_atomic(_fails_path(), fails)
    return fails[k]


def _clear_fail(sector, gkey):
    fails = _load(_fails_path(), {})
    if fails.pop(edge_key(sector, gkey), None) is not None:
        _save_atomic(_fails_path(), fails)


def _completed():
    """Normalised names of sectors whose ledger is complete (mirrors
    drive.completed_sectors but kept here so survey.sh can call us standalone)."""
    done = set()
    sd = _wd()
    if not os.path.isdir(sd):
        return done
    for f in os.listdir(sd):
        if not f.endswith(".json"):
            continue
        st = _load(os.path.join(sd, f), None)
        if isinstance(st, dict) and st.get("status") == "complete" and st.get("sector"):
            done.add(navdata.norm(st["sector"]))
    return done


def _gates(sector):
    st = state.read(sector)
    return [n for n in st.get("nodes", []) if n.get("type") in GATE_TYPES]


def gate_rank(sector, gname, done=None):
    """Sort key for a gate, lowest = try first:
        0  destination unknown      -- the exploration frontier, take it first
        1  destination known, NOT complete  -- somewhere we have not finished
        2  destination known + complete     -- a proven dead-end, last resort
    """
    if done is None:
        done = _completed()
    dest = dest_of(sector, navdata.norm(gname))
    if dest is None:
        return 0
    if dest == LOCKED:
        return 3
    return 2 if navdata.norm(dest) in done else 1


def label_names_completed(gname, done=None):
    """Soft hint: does this gate's display label loosely name a sector we have already
    completed? Labels LIE about the true destination, so this is NOT used for primary
    routing -- only as a tiebreak among equal-rank unknown gates, to prefer one whose
    label points at fresh ground ('Sector Gate to Kitara's Veil') over an obvious
    backtrack ('Sector Gate to Yokan') when we have no recorded edge for either yet."""
    if done is None:
        done = _completed()
    label = navdata.norm(gname)
    return any(d and d in label for d in done)


def has_unexplored(sector):
    """True if `sector` has at least one gate whose recorded destination is unknown
    or not-yet-complete -- i.e. crossing onward from here can still discover ground.
    False means every gate out of here is a proven path to an already-done sector,
    so this sector is exhausted (the survey's real stop signal)."""
    done = _completed()
    return any(gate_rank(sector, g["name"], done) < 2 for g in _gates(sector))


def _main(argv):
    if len(argv) < 2:
        print("usage: gate_route.py {resolve|record-cross|has-unexplored|dump} ...",
              file=sys.stderr)
        return 2
    cmd = argv[1]
    if cmd == "resolve":
        resolve(argv[2])
        return 0
    if cmd == "record-cross":
        record_cross(argv[2], navdata.norm(argv[3]))
        return 0
    if cmd == "has-unexplored":
        return 0 if has_unexplored(argv[2]) else 1
    if cmd == "dump":
        print(json.dumps({"edges": load_edges(),
                          "pending": _load(_pending_path(), None)}, indent=2))
        return 0
    print(f"unknown command: {cmd}", file=sys.stderr)
    return 2


if __name__ == "__main__":
    sys.exit(_main(sys.argv))
