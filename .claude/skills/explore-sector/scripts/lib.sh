#!/usr/bin/env bash
# SPDX-License-Identifier: MIT
# Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
# License: LICENSES/Freya
#
# lib.sh -- shared helpers for the explore-sector skill. We reuse the
# login-to-client skill's lib.sh wholesale (window discovery, XTEST click at
# absolute coords, screenshots, the enbmod cmd channel client_dir, etc.) and add
# only the map/warp-specific helpers on top.
#
#   SKILL_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"; . "$SKILL_DIR/lib.sh"
set -uo pipefail

SKILL_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SKILL_DIR/../../../.." && pwd)"
LOGIN_LIB="$REPO_ROOT/.claude/skills/login-to-client/scripts/lib.sh"
# shellcheck source=/dev/null
. "$LOGIN_LIB"

WORKDIR="${ENB_EXPLORE_WORKDIR:-/tmp/enbexplore}"
mkdir -p "$WORKDIR"

# Map overlay crop, window-relative (left top width height). The client renders a
# fixed 1280x960, so this is stable. Overridable via env if the UI ever moves.
MAP_CROP="${ENB_MAP_CROP:-0 0 1280 960}"

elog() { printf '[explore] %s\n' "$*" >&2; }

# Robust screenshot. `import -window` can grab the X server and hang forever (the
# documented wedge), which freezes every later xdotool/import and silently kills a
# long warp-poll loop. Guard each capture with a hard timeout and, on failure, kill
# any lingering `import` (which is what holds the grab) before retrying. This
# OVERRIDES the login lib's shot() for all explore-sector screenshots.
shot() {
    local out="$1" i
    for i in 1 2 3; do
        if timeout -k 2 10 "$SHOTSH" "$out" >/dev/null 2>&1 && [ -s "$out" ]; then
            return 0
        fi
        pkill -9 import 2>/dev/null; sleep 0.5
    done
    elog "shot failed after 3 tries (import wedge?)"
    return 1
}

# ---- enbmod cmd channel -----------------------------------------------------
# Send a Lua line and echo any new [run] output (best-effort, ~5s).
cmd_run() {
    local dir line start new i
    dir="$(client_dir)" || { elog "no client dir"; return 1; }
    start=$(wc -l < "$dir/enbmod.log" 2>/dev/null || echo 0)
    printf '%s\n' "$1" >> "$dir/enbmod.cmd"
    for i in $(seq 1 25); do
        new=$(tail -n +$((start + 1)) "$dir/enbmod.log" 2>/dev/null | grep '\[run\]')
        [ -n "$new" ] && { printf '%s\n' "$new"; return 0; }
        sleep 0.2
    done
    return 0
}

# Player x,y (sector coords) as "X Y", or empty if not in space / uncalibrated.
self_xy() {
    local out
    out="$(cmd_run 'local s=enb.self(); if s and s.base~=0 then return s.x, s.y else return "nil" end')"
    # "[run] 12.3  [run] -4.5" style: pull the two numbers.
    printf '%s\n' "$out" | grep -oE '\[run\] [-0-9.]+' | awk '{print $2}' | head -2 | tr '\n' ' '
}

# ---- input ------------------------------------------------------------------
# Press an in-game key by keysym (e.g. m, q). Game keybinds are read via the
# focused window, so raise first then XTEST. (Channel post_key uses PostMessage
# which DirectInput-style keybinds may ignore -- XTEST is the reliable path.)
press_key() {
    local k="$1" id
    id="$(client_win)" || { elog "no client window"; return 1; }
    raise_win "$id"; sleep 0.25
    elog "key $k"
    xdotool key --clearmodifiers "$k"
    sleep 0.25
}

# ---- screenshots ------------------------------------------------------------
# Full client shot -> path (auto-numbered under WORKDIR if no arg).
explore_shot() {
    local out="${1:-}"
    if [ -z "$out" ]; then
        local n=1
        while [ -e "$WORKDIR/shot-$n.png" ]; do n=$((n + 1)); done
        out="$WORKDIR/shot-$n.png"
    fi
    shot "$out" && echo "$out"
}

# Crop a full client shot to the map region. Prints "CROPPED_PATH X0 Y0" so the
# caller can convert a pixel inside the crop back to a window-relative click:
# winx = X0 + cropx, winy = Y0 + cropy.
crop_map() {
    local src="$1" dst="$2" l t w h
    read -r l t w h <<< "$MAP_CROP"
    convert "$src" -crop "${w}x${h}+${l}+${t}" +repage "$dst" 2>/dev/null \
        || { elog "crop failed"; return 1; }
    echo "$dst $l $t"
}
