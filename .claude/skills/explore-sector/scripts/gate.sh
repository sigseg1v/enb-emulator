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
# CLEAN CROSSING ORDER (owner, 2026-06-22): the caller parks the ship as close as
# possible to the gate FIRST, then this script
#   1. CONFIRMS the use-gate icon is actually on screen (= close enough + the gate
#      is usable by this ship). If it is NOT, we do NOT rotate the capture and we
#      do NOT click -- we exit 2 so the caller tries another gate. This is what
#      keeps a single .pcap from being polluted with two sectors: we only ever
#      start the destination capture for a crossing that is going to happen.
#   2. ROTATES the per-sector capture (closes this sector's file, opens a fresh one
#      for the destination) BEFORE opening the gate, so the destination .pcap holds
#      the entry handshake -- captured before we can OCR the new sector's name.
#   3. CLICKS the use-gate icon.
# drive.py then relabels the fresh capture from the gate-label guess to the real
# OCR'd sector name once it is known, and pcap.sh records the cap->sector mapping.
#
# The icon is a blue disc with converging arrows (a jump glyph). At 1280x960 it
# is centred at (1236,641) -- VALIDATED 2026-06-21 by crossing the Equatorial
# "System Gate to Proxima Centauri" into Margesi. That is the default below; the
# env var still overrides if the resolution/HUD ever changes.
#
# NOTE: the gate's DISPLAY name is not the destination sector's navdata name
# (the "...to Proxima Centauri" gate lands in Margesi). Always identify the new
# sector from its navs via navdata.py identify after the jump, never from the
# gate label.
# Optional positional args make the crossing self-logging (owner asked for an
# accurate sector-change record): the gate name and the from->to route are written
# to the action log the instant we click, so the ledger pins WHEN a jump started
# and WHICH gate -- not just that we arrived.
#   gate.sh [<from-sector> "<gate name>" [<to-sector>]]
#
# Exit: 0 = icon confirmed, capture rotated, gate clicked.  2 = no use-gate icon
# (not close enough / gate not usable from here) -- nothing rotated/clicked.
# 1 = hard error (no client window).
set -uo pipefail
SKILL_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"; . "$SKILL_DIR/lib.sh"

from="${1:-}"; gatename="${2:-}"; to="${3:-}"

read -r gx gy <<< "${ENB_GATE_ICON:-1236 641}"
read -r bw bh <<< "${ENB_GATE_ICON_BOX:-64 64}"
# Minimum fraction of blue-dominant pixels in the icon box for the use-gate disc
# to count as present. CALIBRATED against a real parked-at-gate frame (Margesi
# "Sector Gate to Adriel Prime", 2026-06-22): icon present ~0.28, no icon (space
# backdrop) ~0.05. 0.12 sits between them (>2x the miss, <half the hit). The frac
# is always logged so this can be re-calibrated if the HUD ever changes.
THR="${ENB_GATE_ICON_THR:-0.12}"
id="$(client_win)" || { elog "no client window"; exit 1; }

# Measure the blue-pixel fraction in the use-gate icon box. Prints a 0..1 float.
icon_blue_frac() {
    local tmp="$SCRATCH/.gateicon.png"
    raise_win "$id"; sleep 0.15
    shot "$tmp" >/dev/null 2>&1 || { echo "0.0"; return; }
    python3 - "$tmp" "$gx" "$gy" "$bw" "$bh" <<'PY'
import sys, numpy as np
from PIL import Image
p, cx, cy, bw, bh = sys.argv[1], *map(int, sys.argv[2:6])
im = np.asarray(Image.open(p).convert("RGB")).astype(int)
H, W, _ = im.shape
x0, y0 = max(0, cx - bw // 2), max(0, cy - bh // 2)
x1, y1 = min(W, cx + bw // 2), min(H, cy + bh // 2)
b = im[y0:y1, x0:x1]
R, G, B = b[:, :, 0], b[:, :, 1], b[:, :, 2]
# The use-gate disc is a DARK-NAVY disc (low brightness) with GOLD arrows, on a
# near-black HUD/space backdrop. Keying on "bright blue" (an earlier B>110 test)
# saw only the thin rim highlight (~0.7% of the box) and rejected a clearly-present
# icon. Key instead on BLUE-DOMINANT pixels (blue is the largest channel by a
# margin): that catches the whole navy disc. Measured on a real parked-at-gate
# frame: icon present ~0.28, no icon (space backdrop) ~0.05 -- a >5x gap. The gold
# arrows are red/green-dominant so they correctly do NOT count.
blue = (B > R + 10) & (B >= G)
print(f"{blue.sum() / max(1, blue.size):.4f}")
PY
}

frac="$(icon_blue_frac | tail -1)"
if ! awk "BEGIN{exit !($frac+0 >= $THR+0)}" 2>/dev/null; then
    elog "use-gate icon NOT present (blue=$frac < $THR) -- not close enough / gate not usable from here; NOT rotating capture or clicking"
    [ -n "$from" ] && bash "$SKILL_DIR/logaction.sh" "$from" gate-noicon \
        "no use-gate icon for ${gatename:-gate} (blue=$frac); skipping crossing" >/dev/null 2>&1 || true
    echo "GATE-NOICON $frac"
    exit 2
fi
elog "use-gate icon present (blue=$frac >= $THR) -- committing to crossing"

if [ -n "$from" ] && [ -n "$gatename" ]; then
    route="$gatename"; [ -n "$to" ] && route="$gatename ($from -> $to)"
    bash "$SKILL_DIR/logaction.sh" "$from" enter-gate "clicking gate icon now: $route" >/dev/null
fi
# Rotate the per-sector capture BEFORE opening the gate, so the destination
# sector's .pcap includes the entry handshake and NOT the previous sector's. Only
# reached once the icon is confirmed, so we never rotate for a crossing that will
# not happen. Best-effort: pcap.sh no-ops if the capture worker is not installed.
# Named for the destination when known; otherwise the gate label is the best label
# we have until the new sector is identified post-jump.
if [ -n "$to" ] || [ -n "$gatename" ]; then
    bash "$SKILL_DIR/pcap.sh" rotate "${to:-$gatename}" || true
fi
elog "clicking use-gate icon at ($gx,$gy)"
click_win "$id" "$gx" "$gy"
echo "GATE-CLICKED $frac"
