#!/usr/bin/env bash
# SPDX-License-Identifier: MIT
# Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
# License: LICENSES/Freya
#
# warp.sh -- engage warp toward the currently-selected target by CLICKING the round
# warp button in the bottom-centre HUD, then CONFIRM the warp actually engaged by
# reading BOTH the green speed readout AND the target-distance delta, re-clicking
# only when both signals confirm the ship is still stopped.
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
# So a click is only ever safe when the ship is DOUBLE-CONFIRMED stopped, using two
# independent signals because either OCR can wash out: the green speed digit (>0 ==
# moving) AND the target distance over two reads ~120ms apart (a change == moving;
# a stationary distance never moves). MOVING needs positive evidence from EITHER;
# only when speed reads 0 AND distance is flat do we click. Inside the post-click
# engage window we wait for either signal to show motion before allowing a re-click,
# so a warp merely spinning up (~5s) is never cancelled. "Always be warping": a stop
# we cannot clear is reported (exit 1) so the caller re-selects a farther target.
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

speed() { bash "$SKILL_DIR/read-speed.sh"  2>/dev/null | awk '/^SPEED/{print $2}'; }
dist()  { bash "$SKILL_DIR/read-target.sh" 2>/dev/null | awk '/^DIST/{print $2}'; }

# Smallest target-distance change (in k) that counts as real motion. A STATIONARY
# target distance is rock-steady (sampled 85.53k x5, zero jitter), so anything over
# this tiny epsilon is genuine movement, not OCR noise.
DIST_EPS="${ENB_WARP_DIST_EPS:-0.05}"
SAMPLES="${ENB_WARP_SPEED_SAMPLES:-3}"

# Is |a-b| > eps?  (float compare via awk; false if either is missing/"?")
_changed() {
    [ -n "$1" ] && [ -n "$2" ] && [ "$1" != "?" ] && [ "$2" != "?" ] || return 1
    awk -v a="$1" -v b="$2" -v e="$3" 'BEGIN{d=a-b; if(d<0)d=-d; exit !(d>e)}'
}

# OWNER RULES (2026-06-21 / -06-22): the warp orb is a TOGGLE -- clicking it while
# the ship is MOVING CANCELS the warp -- and the ship must ALWAYS be warping, never
# sit. So we click ONLY when DOUBLE-CONFIRMED stuck, using BOTH independent signals,
# because either OCR can wash out:
#   * speed readout (read-speed.sh): >0 == moving.
#   * target-distance delta (read-target.sh) over two reads ~120ms apart: a change
#     == moving (stationary distance does not move at all).
# MOVING needs only POSITIVE evidence from EITHER signal. With NO positive evidence
# from EITHER (speed 0 AND distance unchanged) the ship is STUCK -> click to engage.
# This is what rescues the old sit bug: when the speed digit read "?" the ship sat
# forever; now a flat distance confirms the stop and we warp anyway.
# Sets globals: MOTION = moving|stuck ; SPD_LAST = last positive speed (0 if none).
MOTION=stuck; SPD_LAST=0
classify_motion() {
    local d1 d2 s pos=0 i
    d1="$(dist)"; sleep 0.12; d2="$(dist)"
    if _changed "$d1" "$d2" "$DIST_EPS"; then
        MOTION=moving
        elog "distance changed ${d1}k -> ${d2}k (> ${DIST_EPS}k) -> MOVING"
        return
    fi
    SPD_LAST=0
    for ((i=0;i<SAMPLES;i++)); do
        s="$(speed)"
        if [ -n "$s" ] && [ "$s" != "?" ] && [ "$s" -gt 0 ] 2>/dev/null; then
            pos=$((pos + 1)); SPD_LAST="$s"
        fi
    done
    if [ "$pos" -gt 0 ]; then
        MOTION=moving
        elog "speed positive ($SPD_LAST, $pos/$SAMPLES reads) -> MOVING"
    else
        MOTION=stuck
        elog "STUCK double-confirmed: distance flat (${d1:-?}k/${d2:-?}k) AND speed 0"
    fi
}

attempt=0
while [ "$attempt" -le "$RETRY" ]; do
    # Single chokepoint enforcing the owner rules for ALL callers.
    classify_motion
    if [ "$MOTION" = "moving" ]; then
        elog "ALREADY WARPING -- NOT clicking (a click would cancel it)"
        echo "WARP ${SPD_LAST:-1}"     # non-zero == moving (distance may prove it even if speed read 0)
        exit 0
    fi
    # STUCK: double-confirmed not moving -> safe (and required) to engage.
    elog "clicking warp orb ($WBX,$WBY), attempt $((attempt + 1))"
    d0="$(dist)"
    click_win "$id" "$WBX" "$WBY" >/dev/null 2>&1
    # Engage window: warp spins up over ~5s. Watch for POSITIVE motion -- speed>0 OR
    # the distance dropping away from d0 -- and do NOT re-click inside the window (a
    # re-click mid-spin-up would cancel). Only after the whole window with no motion
    # do we loop and re-click.
    for ((i=1;i<=ENGAGE;i++)); do
        s="$(speed)"; dc="$(dist)"   # each read grabs a fresh shot (~1s) -> poll delay
        elog "  engage poll $i/$ENGAGE: speed=${s:-?} dist=${dc:-?}k (from ${d0:-?}k)"
        if { [ -n "$s" ] && [ "$s" != "?" ] && [ "$s" -gt 0 ] 2>/dev/null; } \
           || _changed "$d0" "$dc" "$DIST_EPS"; then
            elog "warp ENGAGED (speed=${s:-?}, dist ${d0:-?}k -> ${dc:-?}k)"
            [ -n "$s" ] && [ "$s" != "?" ] && [ "$s" -gt 0 ] 2>/dev/null && echo "WARP $s" || echo "WARP 1"
            exit 0
        fi
    done
    elog "no motion within engage window -- re-confirming before any re-click"
    attempt=$((attempt + 1))
done
elog "warp never engaged after $((RETRY + 1)) attempts (target too close, or input dropped)"
echo "WARP 0"
exit 1
