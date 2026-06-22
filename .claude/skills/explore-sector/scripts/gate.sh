#!/usr/bin/env bash
# SPDX-License-Identifier: MIT
# Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
# License: LICENSES/Freya
#
# gate.sh -- click the "use gate" action icon that appears near the bottom-right
# of the HUD once your ship is targeting a usable gate it is sitting on. This
# jumps to the linked sector; the scene swaps to the new sector almost
# immediately (no long dark load screen -- detect arrival by the scene change,
# not by luma). After arrival, re-run the sector-identify + state.py init flow
# for the NEW sector (see SKILL.md "Crossing to a new sector").
#
# The icon is a blue disc with converging arrows (a jump glyph). At 1280x960 it
# is centred at (1236,641) -- VALIDATED 2026-06-21 by crossing the Equatorial
# "System Gate to Proxima Centauri" into Margesi. That is the default below; the
# env var still overrides if the resolution/HUD ever changes. If you click it and
# NO disc appears when the gate is targeted+reached, this ship cannot use that
# gate -- take a different gate (SKILL.md "Locked gates -> reroute").
#
# NOTE: the gate's DISPLAY name is not the destination sector's navdata name
# (the "...to Proxima Centauri" gate lands in Margesi). Always identify the new
# sector from its navs via navdata.py identify after the jump, never from the
# gate label.
# Optional positional args make the crossing self-logging (owner asked for an
# accurate sector-change record): the timestamp of the gate-icon CLICK, the gate
# name, and the from->to route are written to the action log the instant we click,
# so the ledger pins WHEN a jump started and WHICH gate -- not just that we arrived.
#   gate.sh [<from-sector> "<gate name>" [<to-sector>]]
set -uo pipefail
SKILL_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"; . "$SKILL_DIR/lib.sh"

from="${1:-}"; gatename="${2:-}"; to="${3:-}"

coords="${ENB_GATE_ICON:-1236 641}"
read -r gx gy <<< "$coords"
id="$(client_win)" || { elog "no client window"; exit 1; }

if [ -n "$from" ] && [ -n "$gatename" ]; then
    route="$gatename"; [ -n "$to" ] && route="$gatename ($from -> $to)"
    bash "$SKILL_DIR/logaction.sh" "$from" enter-gate "clicking gate icon now: $route" >/dev/null
fi
elog "clicking use-gate icon at ($gx,$gy)"
click_win "$id" "$gx" "$gy"
