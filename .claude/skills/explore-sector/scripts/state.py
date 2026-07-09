#!/usr/bin/env python3
# SPDX-License-Identifier: MIT
# Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
# License: LICENSES/Freya
#
# state.py -- the persistent, structured progress ledger for the explore-sector
# skill. One JSON file per sector under ../state/<Sector>.json holds, for every
# node in that sector's docs/sectors/json data, whether we have VISITED it
# (flown to/through it) and, for hidden nodes, whether we have REVEALED it (made
# its "?" marker appear by flying within scanner range). The goal of one skill
# run is: every node visited -> the sector is COMPLETE.
#
# The per-sector node universe comes from navdata.py (the authoritative jsonl);
# this file only overlays progress, so there is one source of truth for "what
# exists". progress is derived on demand from the per-sector files (no separate
# summary file to drift).
#
#   init <Sector>                 -- create/refresh the ledger (keeps prior progress)
#   status <Sector>               -- visited/total, and what is left
#   remaining <Sector>            -- unvisited nodes (name, hidden?, x,y,z) for path planning
#   suggest <Sector> <x> <y>      -- rank unvisited nodes by distance from (x,y):
#                                    farthest first == best "long path" warp targets
#   reveal <Sector> "<name>"      -- mark a hidden node's "?" as now visible
#   visit  <Sector> "<name>" [gid]-- mark a node visited (also reveals it); the
#                                    gid disambiguates duplicate-name rows and
#                                    makes re-visits idempotent
#   skip   <Sector> "<name>" [why]-- mark a node UNREACHABLE (never popped on the
#                                    map even after targeting/approaching). Counts
#                                    as resolved so the sector can complete.
#   skipped <Sector>              -- list the skipped (unreachable) nodes
#   complete <Sector>             -- mark complete IFF every node is visited or skipped
#   summary                       -- one line per sector we have a ledger for
import datetime
import json
import math
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
# All persistent work (per-sector ledgers, the action log, pcaps, stuck shots) lives
# under one root so a whole survey can be parked in a chosen folder. Override it with
# ENB_EXPLORE_WORKDIR; default is the skill's own state/ dir. The ledgers here are the
# finished-vs-remaining truth -- point WORKDIR at the same folder to resume a survey.
STATE_DIR = os.environ.get("ENB_EXPLORE_WORKDIR") or os.path.join(HERE, "..", "state")
sys.path.insert(0, HERE)
import navdata  # noqa: E402


def _now():
    """Local-time ISO-8601 timestamp (with offset), seconds resolution."""
    return datetime.datetime.now().astimezone().isoformat(timespec="seconds")


def _elapsed(started, finished):
    """Whole seconds between two ISO-8601 strings, or None if either is unparseable."""
    try:
        a = datetime.datetime.fromisoformat(started)
        b = datetime.datetime.fromisoformat(finished)
    except (TypeError, ValueError):
        return None
    return int((b - a).total_seconds())


def _fmt_dur(secs):
    """'1h23m' / '23m' / '45s' for a second count (or '?' for None)."""
    if secs is None:
        return "?"
    h, rem = divmod(secs, 3600)
    m, s = divmod(rem, 60)
    if h:
        return f"{h}h{m:02d}m"
    if m:
        return f"{m}m{s:02d}s"
    return f"{s}s"


def path(sector):
    return os.path.join(STATE_DIR, sector + ".json")


def read(sector):
    p = path(sector)
    if not os.path.exists(p):
        sys.exit(f"no ledger for {sector}; run: state.py init {sector}")
    with open(p) as f:
        return json.load(f)


def write(st):
    os.makedirs(STATE_DIR, exist_ok=True)
    with open(path(st["sector"]), "w") as f:
        json.dump(st, f, indent=2, sort_keys=True)
        f.write("\n")


def find_node(st, name):
    key = navdata.norm(name)
    for n in st["nodes"]:
        if navdata.norm(n["name"]) == key:
            return n
    sys.exit(f"{st['sector']}: no node matching '{name}'")


def find_nodes(st, name):
    """All nodes matching the name. Sectors legitimately carry DUPLICATE nav
    names (ABA has three distinct 'Mining Station' nodes; Saturn four
    'Abandoned Pathway' rows), so visit/skip must operate per-row, not
    first-match -- first-match re-marked the same visited row forever while
    its duplicate siblings starved (a survey wedged ABA at 28/41 on this)."""
    key = navdata.norm(name)
    hits = [n for n in st["nodes"] if navdata.norm(n["name"]) == key]
    if not hits:
        sys.exit(f"{st['sector']}: no node matching '{name}'")
    return hits


def cmd_init(args):
    sector = args[0]
    nodes_src = navdata.load(sector)
    # duplicate names are distinct rows: carry prior progress per
    # (name, position) so a re-init cannot smear one duplicate's visited
    # flag across its siblings; the name-only map is the fallback for a
    # node whose coords changed in the jsonl.
    prior = {}
    prior_by_name = {}
    if os.path.exists(path(sector)):
        with open(path(sector)) as f:
            for n in json.load(f).get("nodes", []):
                k = navdata.norm(n["name"])
                prior[(k, n["x"], n["y"], n["z"])] = n
                prior_by_name.setdefault(k, n)
    nodes = []
    for s in nodes_src:
        k = navdata.norm(s["name"])
        p = prior.get((k, s["x"], s["y"], s["z"])) or prior_by_name.get(k, {})
        node = {
            "name": s["name"],
            "type": s["type"],
            "hidden": s["hidden"],
            "x": s["x"], "y": s["y"], "z": s["z"],
            # hidden nodes are not on the map until revealed; shown nodes are
            # "revealed" by definition.
            "revealed": p.get("revealed", not s["hidden"]),
            "visited": p.get("visited", False),
            # unreachable: targeted/approached but never popped on the map.
            "skipped": p.get("skipped", False),
            "skip_reason": p.get("skip_reason", ""),
        }
        if "visited_gid" in p:
            node["visited_gid"] = p["visited_gid"]
        nodes.append(node)
    # danger zones (places we were wrecked) are permanent -- never wipe them.
    # `started` is the first-touch timestamp: preserve it across re-inits so a
    # resume does not reset the clock; only stamp it the very first time.
    dangers = []
    started = _now()
    if os.path.exists(path(sector)):
        with open(path(sector)) as f:
            prior_st = json.load(f)
        dangers = prior_st.get("dangers", [])
        started = prior_st.get("started", started)
    st = {"sector": sector, "status": "in-progress", "nodes": nodes,
          "dangers": dangers, "started": started}
    write(st)
    _print_status(st)


def _counts(st):
    total = len(st["nodes"])
    visited = sum(1 for n in st["nodes"] if n["visited"])
    skipped = sum(1 for n in st["nodes"] if n.get("skipped"))
    hidden_unrevealed = [n for n in st["nodes"] if n["hidden"] and not n["revealed"]]
    return total, visited, skipped, hidden_unrevealed


def _print_status(st):
    total, visited, skipped, _ = _counts(st)
    tail = f", {skipped} skipped" if skipped else ""
    # elapsed so far: against `finished` if complete, else against now.
    end = st.get("finished") or _now()
    dur = _elapsed(st.get("started"), end)
    dtail = f"  elapsed={_fmt_dur(dur)}" if dur is not None else ""
    print(f"{st['sector']}: {visited}/{total} visited{tail}  "
          f"status={st['status']}{dtail}")
    # "left" == unresolved: neither visited nor skipped.
    left = [n for n in st["nodes"] if not n["visited"] and not n.get("skipped")]
    if left:
        print(f"  unvisited ({len(left)}):")
        for n in left:
            tag = "HIDDEN/unrevealed" if (n["hidden"] and not n["revealed"]) else \
                  ("HIDDEN/revealed" if n["hidden"] else "shown")
            print(f"    [{tag:17}] {n['name']}  ({n['x']},{n['y']},{n['z']})")
    skiplist = [n for n in st["nodes"] if n.get("skipped")]
    if skiplist:
        print(f"  skipped ({len(skiplist)}) -- unreachable:")
        for n in skiplist:
            why = f" -- {n['skip_reason']}" if n.get("skip_reason") else ""
            print(f"    [SKIPPED] {n['name']}  ({n['x']},{n['y']},{n['z']}){why}")
    dangers = st.get("dangers", [])
    if dangers:
        print(f"  danger zones ({len(dangers)}) -- never conclude a warp within 10:")
        for d in dangers:
            print(f"    [DANGER] ({d['x']},{d['y']},{d['z']}) -- {d.get('reason','')}")


def cmd_status(args):
    _print_status(read(args[0]))


def cmd_remaining(args):
    st = read(args[0])
    for n in st["nodes"]:
        if not n["visited"]:
            print(f"{1 if n['hidden'] else 0}\t{n['x']}\t{n['y']}\t{n['z']}\t{n['name']}")


def cmd_suggest(args):
    st = read(args[0])
    cx, cy = float(args[1]), float(args[2])
    left = [n for n in st["nodes"] if not n["visited"]]
    left.sort(key=lambda n: math.hypot(n["x"] - cx, n["y"] - cy), reverse=True)
    for n in left:
        d = math.hypot(n["x"] - cx, n["y"] - cy)
        tag = "hidden" if n["hidden"] else "shown"
        print(f"{d:8.1f}\t{tag}\t{n['x']}\t{n['y']}\t{n['name']}")


def _set(args, **flags):
    st = read(args[0])
    n = find_node(st, args[1])
    n.update(flags)
    write(st)
    _print_status(st)


def cmd_reveal(args):
    _set(args, revealed=True)


def cmd_visit(args):
    # visit <S> "<name>" [gid] -- mark ONE node of this name visited. With
    # duplicate names the optional live-object gid picks the row: a row
    # already stamped with this gid makes the call an idempotent no-op (the
    # driver re-marks in-range navs every poll and across restarts);
    # otherwise the first unvisited row is consumed and stamped.
    st = read(args[0])
    hits = find_nodes(st, args[1])
    gid = int(args[2]) if len(args) > 2 else None
    if gid is not None and any(n.get("visited_gid") == gid for n in hits):
        _print_status(st)
        return
    n = next((h for h in hits if not h["visited"]), hits[0])
    n.update(revealed=True, visited=True)
    if gid is not None:
        n["visited_gid"] = gid
    write(st)
    _print_status(st)


def cmd_skip(args):
    reason = args[2] if len(args) > 2 else "unreachable (never popped on the map)"
    # Never mark a nav we already reached as unreachable -- a visited nav is done,
    # not skipped. With duplicate names a skip is a NAME-level concession
    # ("nothing by this name is reachable now"), so it resolves every
    # remaining row of the name at once -- the concede loop issues one skip
    # per name, and leaving a sibling duplicate unresolved would make
    # `complete` refuse.
    st = read(args[0])
    for n in find_nodes(st, args[1]):
        if not n.get("visited"):
            n.update(skipped=True, skip_reason=reason)
    write(st)
    _print_status(st)


def cmd_skipped(args):
    st = read(args[0])
    for n in st["nodes"]:
        if n.get("skipped"):
            print(f"{n['x']}\t{n['y']}\t{n['z']}\t{n['name']}\t{n.get('skip_reason','')}")


def cmd_danger_add(args):
    """danger-add <S> <x> <y> <z> [reason] -- record a place we were wrecked. We
    must never CONCLUDE a warp within DANGER_RADIUS of it (warping past is fine)."""
    sector = args[0]
    x, y, z = float(args[1]), float(args[2]), float(args[3])
    reason = args[4] if len(args) > 4 else "wrecked here"
    st = read(sector)
    st.setdefault("dangers", [])
    # de-dup: merge if a zone already sits within 5 units
    for d in st["dangers"]:
        if math.dist((d["x"], d["y"], d["z"]), (x, y, z)) <= 5:
            d["reason"] = reason
            write(st)
            print(f"{sector}: danger zone updated near ({x},{y},{z})")
            return
    st["dangers"].append({"x": x, "y": y, "z": z, "reason": reason})
    write(st)
    print(f"{sector}: DANGER zone added ({x},{y},{z}) -- {reason}")


def cmd_danger_list(args):
    st = read(args[0])
    for d in st.get("dangers", []):
        print(f"{d['x']}\t{d['y']}\t{d['z']}\t{d.get('reason','')}")


def cmd_complete(args):
    st = read(args[0])
    total, visited, skipped, _ = _counts(st)
    resolved = visited + skipped
    if resolved < total:
        print(f"NOT complete: {resolved}/{total} resolved "
              f"({visited} visited, {skipped} skipped). Remaining:")
        _print_status(st)
        sys.exit(1)
    st["status"] = "complete"
    st["finished"] = _now()
    secs = _elapsed(st.get("started"), st["finished"])
    if secs is not None:
        st["elapsed_seconds"] = secs
    write(st)
    tail = f" ({skipped} unreachable, skipped)" if skipped else ""
    print(f"{st['sector']}: COMPLETE ({visited}/{total} visited{tail}) "
          f"in {_fmt_dur(secs)}")


def cmd_summary(_args):
    if not os.path.isdir(STATE_DIR):
        print("(no ledgers yet)")
        return
    for f in sorted(os.listdir(STATE_DIR)):
        if not f.endswith(".json"):
            continue
        with open(os.path.join(STATE_DIR, f)) as fh:
            st = json.load(fh)
        # The survey workdir holds non-ledger JSON too (gate_edges.json,
        # gate_fails.json, gate_route pending). Those have no "nodes" key; skip
        # them rather than crash the whole summary on the first one we hit.
        if not isinstance(st, dict) or "nodes" not in st:
            continue
        total, visited, skipped, _ = _counts(st)
        tail = f" (+{skipped} skipped)" if skipped else ""
        end = st.get("finished") or _now()
        dur = _elapsed(st.get("started"), end)
        dtail = f"  {_fmt_dur(dur)}" if dur is not None else ""
        print(f"{st['sector']:24} {visited:3}/{total:<3} "
              f"{st['status']}{tail}{dtail}")


def main():
    if len(sys.argv) < 2:
        sys.exit("usage: state.py {init|status|remaining|suggest|reveal|visit|"
                 "skip|skipped|danger-add|danger-list|complete|summary} ...")
    cmds = {
        "init": cmd_init, "status": cmd_status, "remaining": cmd_remaining,
        "suggest": cmd_suggest, "reveal": cmd_reveal, "visit": cmd_visit,
        "skip": cmd_skip, "skipped": cmd_skipped,
        "danger-add": cmd_danger_add, "danger-list": cmd_danger_list,
        "complete": cmd_complete, "summary": cmd_summary,
    }
    cmd = sys.argv[1]
    if cmd not in cmds:
        sys.exit(f"unknown command: {cmd}")
    cmds[cmd](sys.argv[2:])


if __name__ == "__main__":
    main()
