#!/usr/bin/env bash
# SPDX-License-Identifier: MIT
# Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
# License: LICENSES/Freya
#
# reroute.sh -- chain a known multi-gate route, identifying the sector after each
# crossing so a scrambled gate name or a mid-load OCR miss does not derail the run.
# Each leg is "<expected-from> <gate-name> <expected-to>"; the script flies to the
# gate (goto.py), crosses it (gate.sh), then settles + verifies the new sector via
# read-sector.sh before the next leg. Aborts (exit 2) if a leg lands somewhere other
# than its expected destination -- the gate graph is only knowable by crossing, so a
# surprise means the route is stale and must be re-derived, not blindly continued.
#
# Usage: reroute.sh "FROM|GATE|TO" "FROM|GATE|TO" ...
#   reroute.sh "AdrielPrime|Sector Gate to Margesi|Margesi" \
#              "Margesi|System Gate to Sol|Equatorial" \
#              "Equatorial|Accelerator to Earth|Earth"
set -uo pipefail
SKILL_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"; . "$SKILL_DIR/lib.sh"

identify() {   # echo the identified sector key (or empty); $1 = sector to wait to LEAVE
    local leave="${1:-}" out prev="" stable=0
    # poll up to ~40s: the scene swap shows the OLD title mid-load, so require a
    # non-empty read that (a) differs from the sector we are leaving and (b) repeats.
    for _ in $(seq 1 20); do
        out=$(bash "$SKILL_DIR/read-sector.sh" 2>/dev/null | awk '/^SECTOR /{print $2; exit}')
        if [ -n "$out" ] && { [ -z "$leave" ] || [ "$out" != "$leave" ]; }; then
            [ "$out" = "$prev" ] && stable=$((stable+1)) || stable=0
            prev="$out"
            [ "$stable" -ge 1 ] && { echo "$out"; return 0; }
        fi
        sleep 2
    done
    echo "$prev"
}

cur=$(identify)
echo "[reroute] start sector: ${cur:-<unknown>}"

for leg in "$@"; do
    IFS='|' read -r from gate to <<< "$leg"
    echo "[reroute] leg: $from --($gate)--> $to"
    if [ -n "$cur" ] && [ "$cur" != "$from" ]; then
        echo "[reroute][ERR] expected to be in $from but am in $cur -- route stale, aborting"; exit 2
    fi
    if ! timeout 700 python3 "$SKILL_DIR/goto.py" "$from" "$gate" --rounds 14; then
        echo "[reroute][ERR] could not reach gate '$gate' in $from"; exit 1
    fi
    bash "$SKILL_DIR/gate.sh" "$from" "$gate" "$to" >/dev/null 2>&1
    cur=$(identify "$from")
    echo "[reroute] arrived: ${cur:-<unknown>} (wanted $to)"
    if [ "$cur" != "$to" ]; then
        echo "[reroute][ERR] crossing '$gate' landed in '${cur:-<unknown>}', expected '$to' -- aborting"; exit 2
    fi
done
echo "[reroute] DONE -- now in $cur"
