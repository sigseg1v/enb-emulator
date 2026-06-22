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
MAX_CROSSINGS="${ENB_SURVEY_MAX:-90}"      # hard cap on sectors driven this run
STALE_MAX="${ENB_SURVEY_STALE_MAX:-3}"     # consecutive already-done arrivals -> stop

# drive.py only auto-crosses a gate on completion when this is set; survey IS the
# loop that handles the hand-off, so turn it on for the whole chain.
export ENB_GATE_LEAVE="${ENB_GATE_LEAVE:-1}"

detect_sector() {
    bash "$SKILL_DIR/read-sector.sh" 2>/dev/null | awk '/^SECTOR /{print $2; exit}'
}
# ledger status for a sector ("complete" | "in-progress" | "" if no ledger yet)
status_of() {
    python3 "$SKILL_DIR/state.py" status "$1" 2>/dev/null \
        | grep -o 'status=[a-z-]*' | head -1 | cut -d= -f2
}

prev=""
stale=0
for i in $(seq 1 "$MAX_CROSSINGS"); do
    sector="$(detect_sector)"
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

    if [ "$sector" = "$prev" ]; then
        echo "[survey] gate crossing did not move the ship (still in $sector) -- HALT" \
             "rather than spin. Cross a gate manually and re-run." >&2
        exit "$EXIT_STUCK"
    fi

    if [ "$(status_of "$sector")" = "complete" ]; then
        stale=$((stale + 1))
        echo "[survey] $sector already complete (arrival $stale/$STALE_MAX); crossing a gate onward." >&2
        if [ "$stale" -ge "$STALE_MAX" ]; then
            echo "[survey] $STALE_MAX consecutive already-complete arrivals -- the reachable" \
                 "graph from here looks fully surveyed. Stopping." >&2
            exit 0
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
done

echo "[survey] hit ENB_SURVEY_MAX=$MAX_CROSSINGS crossings; stopping." >&2
