#!/usr/bin/env bash
# SPDX-License-Identifier: MIT
# Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
# License: LICENSES/Freya
#
# survey.sh -- chain whole sectors with NO hand-typed sector names. It detects the
# sector the ship is currently in (read-sector.sh, map-title ground truth), drives
# it to completion (run-sector.sh, which auto-recovers client hangs), lets drive.py
# cross a gate into the next sector on completion (ENB_GATE_LEAVE), then re-detects
# and repeats. This is the "what happens when you go to the next sector" the manual
# `run-sector.sh <Sector>` per-sector loop never wired up.
#
#   survey.sh                 -- survey from wherever the ship is, sector by sector
#   survey.sh <Sector>        -- (optional) route assumes you are already in <Sector>;
#                                only used as the first label, then it auto-detects
#
# Honest limits (this drives a LIVE client screen-only and crosses real gates):
#  - Gravity-well sectors are REFUSED, and we cannot auto-route OUT of one (the
#    refusal happens before gate-leave), so arriving in a well sector HALTS for the
#    operator to manually fly to a gate and re-run. Same for EXIT_STUCK.
#  - It stops when it keeps arriving in ALREADY-complete sectors (the reachable
#    graph from here is exhausted) or after ENB_SURVEY_MAX crossings.
#  - The gate crossing (ENB_GATE_LEAVE) is the least-validated link; if a crossing
#    does not move us, we HALT rather than spin.
set -uo pipefail
SKILL_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# Survey work root: per-sector ledgers (finished/remaining truth), the action log,
# pcaps and stuck shots all live here, so a whole survey is one folder. Override with
# ENB_EXPLORE_WORKDIR -- it is inherited by run-sector.sh/drive.py/state.py/logaction/
# pcap. Point it at the same folder to RESUME a survey (completed sectors are skipped).
WORKDIR="${ENB_EXPLORE_WORKDIR:-$SKILL_DIR/../state}"
mkdir -p "$WORKDIR"

EXIT_STUCK=43
MAX_CROSSINGS="${ENB_SURVEY_MAX:-300}"     # hard cap on sectors driven this run
                                           # (galaxy is ~100+ sectors and a greedy
                                           # destination-ranked walk revisits some, so
                                           # a 90 cap stopped mid-galaxy; STALE_MAX and
                                           # has-unexplored are the real exhaustion stop)
STALE_MAX="${ENB_SURVEY_STALE_MAX:-3}"     # consecutive already-done arrivals -> stop

# drive.py only auto-crosses a gate on completion when this is set; survey IS the
# loop that handles the hand-off, so turn it on for the whole chain.
export ENB_GATE_LEAVE="${ENB_GATE_LEAVE:-1}"

# Per-sector packet capture targets the PROXY's network namespace. The default
# (empty name) is the dockerized FreyaProxy container -- correct for a local-stack
# survey. But in MANUAL_CLIENT mode the client the survey drives is connected
# through a HOST WINE proxy (Net7Proxy.exe), NOT the docker proxy, so the worker
# would look for a docker target, find none, and silently skip capture (the empty
# captures/ dir). Point it at the WINE proxy by process name so the worker resolves
# its PID and scopes the capture to the proxy<->server leg. Overridable if the proxy
# binary is named differently.
if [ "${ENB_EXPLORE_MANUAL_CLIENT:-0}" = 1 ]; then
    export ENB_EXPLORE_PROXY_NAME="${ENB_EXPLORE_PROXY_NAME:-Net7Proxy.exe}"
fi

LOGIN_DIR="$SKILL_DIR/../../login-to-client/scripts"

_read_sector() { bash "$SKILL_DIR/read-sector.sh" 2>/dev/null | awk '/^SECTOR /{print $2; exit}'; }

# Read the current sector off the map title, RECOVERING the two states that make the
# OCR fail (owner, 2026-06-22): (1) the client dropped to character-select (crash /
# disconnect) -- there is no game and no map to read, so we re-enter by clicking the
# first character + Enter (07, a no-op when not on char-select) and waiting in-game
# (08); (2) we are in-game but the map is not open -- read-sector.sh -> open-map.sh
# already opens it, so a second attempt after settling usually reads. Only after both
# recoveries fail do we report empty (caller HALTs).
detect_sector() {
    local want_diff="${1:-}"
    local s i
    # Just-crossed-a-gate case: the scene swap is NOT instant. For a few seconds after
    # the use-gate click the map title still reads the OLD sector, then blanks for the
    # load, then reads the NEW one. A single quick read here catches that stale OLD
    # title, and the caller's `sector == prev` guard then mis-reports "gate crossing
    # did not move the ship" and HALTS a crossing that actually worked (seen 2026-06-22:
    # crossed Yokan->Ishuan but halted on a stale Yokan read). So when the caller tells
    # us which sector we are LEAVING, poll for the title to actually CHANGE first. If it
    # never changes we fall through to the normal read and the caller halts as before --
    # so a genuinely-failed crossing is still caught, we just stop false-halting real ones.
    if [ -n "$want_diff" ]; then
        local tries="${ENB_CROSS_TRIES:-20}"
        for i in $(seq 1 "$tries"); do
            s="$(_read_sector)"
            [ -n "$s" ] && [ "$s" != "$want_diff" ] && { echo "$s"; return 0; }
            sleep 3
        done
    fi
    s="$(_read_sector)"; [ -n "$s" ] && { echo "$s"; return 0; }
    echo "[survey] sector OCR failed -- recovering (confirm in-game, re-open map)" >&2
    # 1) confirm we are IN-GAME before touching the screen. The old screen-driven
    #    07-charselect-enter step is gone (login is OCR-free; the in-client
    #    auto-login owns char-enter), so the enbmod Lua channel is the guard now:
    #    08 exiting non-zero means the character is NOT loaded into the world
    #    (load hang, or parked at character-select where read-sector's map-open
    #    click would land on random UI -- the "clicking random shit all over the
    #    place" bug). HALT for the operator instead of clicking blindly.
    #    Screen-only manual-client mode has no Lua channel to consult (no mods
    #    injected); there the read-sector poll below is the (weaker) in-space
    #    signal -- read-sector refuses char-select/login and only prints SECTOR
    #    once the map title actually reads.
    if [ "${ENB_EXPLORE_MANUAL_CLIENT:-0}" != 1 ]; then
        if ! bash "$LOGIN_DIR/08-wait-ingame.sh" >&2; then
            echo "[survey] 08-wait-ingame failed -- not in-game (char-select or load hang), NOT map-clicking. HALT." >&2
            return 1
        fi
    fi
    # 2) poll read-sector.sh until the load screen clears and the map title reads.
    #    read-sector.sh re-opens the map each call; during the load screen it returns
    #    empty (no title) and we just retry. Each try is one read-sector call (a few
    #    seconds) plus a 3s settle, so ~20 tries covers a ~60-90s scene load.
    local i tries
    tries="${ENB_LOAD_TRIES:-20}"
    for i in $(seq 1 "$tries"); do
        s="$(_read_sector)"; [ -n "$s" ] && { echo "$s"; return 0; }
        sleep 3
    done
    return 1
}
# ledger status for a sector ("complete" | "in-progress" | "" if no ledger yet)
status_of() {
    python3 "$SKILL_DIR/state.py" status "$1" 2>/dev/null \
        | grep -o 'status=[a-z-]*' | head -1 | cut -d= -f2
}

prev=""
stale=0
samestuck=0
CROSS_RETRY="${ENB_CROSS_RETRY:-3}"   # same-sector re-cross attempts that learn NOTHING new before HALT

# Signature of gate_route's persistent memory (edges + fail/lock tallies). When a
# crossing attempt fails to move the ship but DOES lock a dud gate or record an edge,
# this string changes -- proof we are converging, not spinning. A sector with several
# un-crossable gates (an arrival-only wormhole AND a class-specific gate this ship cannot
# use, e.g. Kailaasa: Maeldun's Weft + Gate to Sol System) needs more failed attempts
# than CROSS_RETRY just to lock each dud before it reaches the one crossable gate -- so we
# only count a retry against the budget when it taught us NOTHING (same signature).
state_sig() { cat "$WORKDIR/gate_edges.json" "$WORKDIR/gate_fails.json" 2>/dev/null | cksum; }
last_sig="$(state_sig)"
for i in $(seq 1 "$MAX_CROSSINGS"); do
    sector="$(detect_sector "$prev")"
    if [ -z "$sector" ]; then
        echo "[survey] could not read the current sector off the map title -- HALT." \
             "Open the map / confirm the client is in space, then re-run survey.sh." >&2
        exit "$EXIT_STUCK"
    fi

    if python3 "$SKILL_DIR/gravity_wells.py" "$sector" >/dev/null 2>&1; then
        : # exit 0 == no well, safe
    else
        echo "[survey] arrived in $sector, which contains a GRAVITY WELL -- refusing." \
             "Cannot auto-route out of a well sector (warp terminates mid-flight)." \
             "Manually fly to a gate and re-run survey.sh from the next sector." >&2
        exit 3
    fi

    # Same sector as the one we just tried to leave: the gate.sh click reported success
    # but the ship did not actually cross within the detect window. This happens
    # intermittently (a slow wormhole load that outlasts the poll, or a use-gate click
    # that missed) and an immediate HALT here means a single flaky crossing kills the
    # whole unattended survey. So RE-DRIVE (the sector is already complete, so this goes
    # straight back to gate-leave and re-attempts the crossing) up to CROSS_RETRY times
    # before giving up. gate_route's pending crossing was left unresolved (from==current
    # records no edge), so nothing false is learned by a non-crossing.
    if [ "$sector" = "$prev" ]; then
        cur_sig="$(state_sig)"
        if [ "$cur_sig" != "$last_sig" ]; then
            # The last attempt failed to cross but updated gate memory (locked a dud or
            # recorded an edge) -- we are converging on the crossable gate, not spinning.
            # Reset the budget so a corner with multiple un-crossable gates does not HALT us.
            samestuck=0
            echo "[survey] still in $sector but the last attempt locked/learned a gate --" \
                 "converging, retry budget reset." >&2
        else
            samestuck=$((samestuck + 1))
            if [ "$samestuck" -gt "$CROSS_RETRY" ]; then
                echo "[survey] gate crossing still stuck in $sector after $CROSS_RETRY" \
                     "no-progress retries -- HALT rather than spin. Cross a gate manually and re-run." >&2
                exit "$EXIT_STUCK"
            fi
            echo "[survey] gate crossing did not move the ship (still in $sector) and learned" \
                 "nothing new -- re-attempting the crossing ($samestuck/$CROSS_RETRY)." >&2
        fi
    else
        samestuck=0
    fi

    if [ "$(status_of "$sector")" = "complete" ]; then
        # Arriving in a sector we already finished is only "stale" when EVERY gate out
        # of it is a proven path to another already-surveyed sector (or locked). If an
        # unexplored gate remains, crossing a completed sector is productive traversal
        # toward fresh ground, NOT spinning -- gate_route routes by real destinations,
        # so we keep going without burning the stale budget. The old code counted every
        # completed arrival and stopped after 3, killing the survey while 4 unused gates
        # to new sectors sat right there (the Yokan<->Ishuan bounce).
        if python3 "$SKILL_DIR/gate_route.py" has-unexplored "$sector"; then
            echo "[survey] $sector already complete but an unexplored gate remains -- crossing onward." >&2
            stale=0
        else
            stale=$((stale + 1))
            echo "[survey] $sector already complete and every gate leads to surveyed/locked space (dead $stale/$STALE_MAX); crossing a gate onward." >&2
            if [ "$stale" -ge "$STALE_MAX" ]; then
                echo "[survey] $STALE_MAX consecutive dead-end arrivals -- the reachable" \
                     "graph from here looks fully surveyed. Stopping." >&2
                exit 0
            fi
        fi
    else
        stale=0
        echo "[survey] === driving $sector (crossing #$i) ===" >&2
    fi

    # run-sector.sh auto-detects + verifies the sector itself; we pass the name only
    # as a hint/label. It drives to completion, recovers hangs, and (ENB_GATE_LEAVE)
    # crosses a gate when the sector finishes. Any non-zero that is not a clean finish
    # is a halt condition we must not paper over.
    bash "$SKILL_DIR/run-sector.sh" "$sector" "$@"
    rc=$?
    case "$rc" in
        0) : ;;  # done + (gate crossed) -> loop re-detects the new sector
        3)  echo "[survey] $sector refused (gravity well); HALT." >&2; exit 3 ;;
        43) echo "[survey] $sector STUCK (see $WORKDIR/scratch/stuck-$sector.png); HALT for re-route." >&2; exit 43 ;;
        44) echo "[survey] ENB_EXPLORE_KILL_NO_PROGRESS fired in $sector -- client killed; STOP." >&2; exit 44 ;;
        *)  echo "[survey] run-sector.sh exited $rc on $sector; HALT." >&2; exit "$rc" ;;
    esac
    prev="$sector"
    last_sig="$(state_sig)"   # snapshot gate memory AFTER this attempt for the next same-sector check
done

echo "[survey] hit ENB_SURVEY_MAX=$MAX_CROSSINGS crossings; stopping." >&2
