#!/usr/bin/env bash
# SPDX-License-Identifier: MIT
# Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
# License: LICENSES/Freya
#
# survey-sweep.sh <Sector> -- one pass of the map survey from the ship's CURRENT
# position. Opens+centers the map, detects node markers, and for each candidate:
# clicks it, settles, and reads the "Dest:" name (diffing before/after so a miss --
# which leaves Dest unchanged -- is discarded). Each confirmed name is fuzzy-matched
# to the authoritative node, recorded as visited (state.py) and logged. Prints the
# distinct nodes identified this pass and the sector's running progress.
#
# This is ONE in-range cluster. Nodes out of scanner range do not render; warp
# toward them and re-run. Usage:
#   survey-sweep.sh Equatorial
set -uo pipefail
SKILL_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"; . "$SKILL_DIR/lib.sh"

S="$SKILL_DIR"
sector="${1:?usage: survey-sweep.sh <Sector>}"
getdest(){ bash "$S/read-navname.sh" 2>&1 | sed -n 's/^NAVNAME //p'; }

read -r _ FULL <<< "$(bash "$S/open-map.sh" 2>/dev/null | grep '^FULL')"
[ -n "${FULL:-}" ] || { elog "open-map failed"; exit 1; }
elog "sweep $sector on $FULL"

seen=""
while read -r _ wx wy col px; do
    [ -z "${wx:-}" ] && continue
    before="$(getdest)"
    bash "$S/click-map.sh" "$wx" "$wy" >/dev/null 2>&1; sleep 0.6
    after="$(getdest)"
    [ "$after" = "$before" ] && continue          # miss: Dest unchanged
    [ -z "$after" ] && continue
    name="$(python3 "$S/navdata.py" match "$sector" "$after" 2>/dev/null | cut -f1)"
    ratio="$(python3 "$S/navdata.py" match "$sector" "$after" 2>/dev/null | cut -f2)"
    [ -z "$name" ] && continue
    # skip low-confidence matches (clicked label text / non-nav)
    awk "BEGIN{exit !($ratio < 0.55)}" && { elog "drop low-conf '$after' ($ratio)"; continue; }
    case "$seen" in *"|$name|"*) continue;; esac
    seen="$seen|$name|"
    python3 "$S/state.py" visit "$sector" "$name" >/dev/null 2>&1
    bash "$S/logaction.sh" "$sector" map-identify "$name (sweep; OCR='$after' r=$ratio)" >/dev/null 2>&1
    echo "IDENTIFIED ($wx,$wy) $col -> $name"
done < <(python3 "$S/detect-nodes.py" "$FULL" 2>/dev/null)

echo "----"
python3 "$S/state.py" status "$sector" 2>&1 | head -1
