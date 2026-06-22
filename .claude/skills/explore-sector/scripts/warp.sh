#!/usr/bin/env bash
# SPDX-License-Identifier: MIT
# Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
# License: LICENSES/Freya
#
# warp.sh -- engage warp toward the currently-selected target by CLICKING the round
# warp button in the bottom-centre HUD, then CONFIRM the warp actually engaged by
# reading the green speed readout and re-clicking if it is still 000.
#
# Why a click, not the Q key (changed 2026-06-21, owner-directed): the Q keybind
# only registers when the client window holds keyboard focus, and on this desktop
# focus is silently stolen (VM pointer-warp, screenshot grabs), so Q was being
# DROPPED -- the ship sat still while the driver "warped" nav after nav, and the
# stall got mis-recorded as visits. A synthetic XTEST click at the warp orb's fixed
# window coord is the reliable input path here (same path the map button uses), and
# it does not need focus.
#
# Engagement is not instant: the drive spins up over ~5s before speed leaves 000.
# We click, then poll read-speed.sh; the moment speed > 0 we are warping and return.
#
# The warp orb is a TOGGLE: clicking it while the ship is moving CANCELS the warp.
# So a re-click is only ever safe when the ship is CONFIRMED stopped -- otherwise we
# would cancel a warp that is merely still spinning up (or whose speed digit blinked
# a noisy read) and the ship "keeps stopping halfway through warp". Before any
# re-click we therefore re-read the speed twice and only re-click when BOTH reads are
# a solid numeric 000 (a "?" read -- panel obscured / mid-present -- is NOT treated
# as stopped; we wait it out rather than risk a cancelling click). "Always be
# warping": a stop we cannot clear is reported (exit 1) so the caller re-selects a
# farther target rather than sitting still.
#
#   ENB_WARP_BTN="x y"     warp orb, window-relative   (default "633 868")
#   ENB_WARP_ENGAGE=N      speed polls per attempt     (default 8; each ~1s)
#   ENB_WARP_RETRY=N       extra re-clicks if 000      (default 2)
#
# Prints:
#   WARP <speed>     engaged (speed > 0)
#   WARP 0           never engaged after all attempts (target too close / dropped)
set -uo pipefail
SKILL_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"; . "$SKILL_DIR/lib.sh"

read -r WBX WBY <<< "${ENB_WARP_BTN:-633 868}"
ENGAGE="${ENB_WARP_ENGAGE:-8}"
RETRY="${ENB_WARP_RETRY:-2}"

id="$(client_win)" || { elog "no client window"; echo "WARP 0"; exit 1; }

speed() { bash "$SKILL_DIR/read-speed.sh" 2>/dev/null | awk '/^SPEED/{print $2}'; }

# OWNER RULE (2026-06-21): NEVER click the warp orb while the ship has ANY warp
# speed -- the orb is a TOGGLE, so a click while moving CANCELS the warp ("keeps
# cancelling warp"). Only ever click at a CONFIRMED solid 000. The green speed
# digit washes out to a noisy read, so we sample N times and classify with a bias
# toward "moving": a SINGLE positive read means moving (do NOT click); we only call
# it stopped when EVERY read is a solid 000 (no positives, no "?").
#
# Sets globals: SPD_STATE = moving | stopped | unknown ; SPD_LAST = last speed seen.
SPD_STATE=unknown; SPD_LAST=0
SAMPLES="${ENB_WARP_SPEED_SAMPLES:-3}"
classify_speed() {
    local s pos=0 zero=0 unk=0 i
    SPD_LAST=0
    for ((i=0;i<SAMPLES;i++)); do
        s="$(speed)"
        if [ -z "$s" ] || [ "$s" = "?" ]; then unk=$((unk + 1))
        elif [ "$s" -gt 0 ] 2>/dev/null; then pos=$((pos + 1)); SPD_LAST="$s"
        elif [ "$s" = "0" ]; then zero=$((zero + 1)); fi
    done
    if   [ "$pos" -gt 0 ];           then SPD_STATE=moving
    elif [ "$zero" -ge "$SAMPLES" ]; then SPD_STATE=stopped
    else                                  SPD_STATE=unknown; fi
    elog "speed sample (n=$SAMPLES): pos=$pos zero=$zero unk=$unk -> $SPD_STATE (last=$SPD_LAST)"
}

attempt=0
while [ "$attempt" -le "$RETRY" ]; do
    # GATE EVERY CLICK on a confirmed 000 (first click included). Single chokepoint
    # enforcing the owner rule for ALL callers.
    classify_speed
    if [ "$SPD_STATE" = "moving" ]; then
        elog "ALREADY WARPING (speed $SPD_LAST) -- NOT clicking (a click would cancel it)"
        echo "WARP $SPD_LAST"
        exit 0
    fi
    if [ "$SPD_STATE" = "unknown" ]; then
        elog "speed inconclusive (? / mixed) -- NOT clicking this round, waiting it out"
        sleep 1
        attempt=$((attempt + 1))
        continue
    fi
    # SPD_STATE == stopped: confirmed solid 000, safe to engage.
    elog "speed confirmed 000 -> clicking warp orb ($WBX,$WBY), attempt $((attempt + 1))"
    click_win "$id" "$WBX" "$WBY" >/dev/null 2>&1
    for ((i=1;i<=ENGAGE;i++)); do
        s="$(speed)"   # read-speed grabs a fresh shot (~1s), so this is the poll delay
        elog "  engage poll $i/$ENGAGE: speed=${s:-?}"
        if [ -n "$s" ] && [ "$s" != "?" ] && [ "$s" -gt 0 ] 2>/dev/null; then
            elog "warp ENGAGED, speed $s"
            echo "WARP $s"
            exit 0
        fi
    done
    elog "no positive speed within engage window -- re-classifying before any re-click"
    attempt=$((attempt + 1))
done
elog "warp never engaged after $((RETRY + 1)) attempts (target too close, or speed washed out)"
echo "WARP 0"
exit 1
