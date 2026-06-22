#!/usr/bin/env bash
# SPDX-License-Identifier: MIT
# Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
# License: LICENSES/Freya
#
# logaction.sh <sector> <action> [detail...] -- append one timestamped line to
# the PERSISTENT, GITIGNORED action log (../state/actions.log). Every concrete
# step gets a line: open-map, select-node, warp, reveal, select-gate, enter-gate,
# load-screen, sector-enter, complete, reroute, etc. This is the auditable record
# the owner asked for: a sector counts as DONE only when our log + ledger show we
# visited every node ourselves (a player having flown it before does NOT count).
#
#   logaction.sh Equatorial select-node "Nav Equatorial 3"
#   logaction.sh Equatorial warp "to Nav Equatorial 3 (plotted path of 4 nodes)"
#   logaction.sh Equatorial enter-gate "Sector Gate to Earth"
set -uo pipefail
SKILL_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# Single work root (ledgers + this log + pcaps + stuck shots); override per survey
# with ENB_EXPLORE_WORKDIR. Default: the skill's own state/ dir.
LOG="${ENB_EXPLORE_WORKDIR:-$SKILL_DIR/../state}/actions.log"
mkdir -p "$(dirname "$LOG")"

[ "$#" -ge 2 ] || { echo "usage: logaction.sh <sector> <action> [detail...]" >&2; exit 2; }
sector="$1"; action="$2"; shift 2
detail="$*"
ts="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
printf '%s\t%s\t%s\t%s\n' "$ts" "$sector" "$action" "$detail" >> "$LOG"
echo "logged: $ts $sector $action $detail"
