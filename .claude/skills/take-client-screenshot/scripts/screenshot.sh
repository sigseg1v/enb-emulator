#!/usr/bin/env bash
# Capture a PNG of the running Earth & Beyond client window (client.exe under
# WINE) on the X11 display, so an agent can SEE whether a change worked.
#
# Picks the client window by its WM_CLASS ("client.exe") -- NOT by title, because
# browser/Discord tabs also contain "Earth & Beyond" -- and among the matches
# takes the one that is actually mapped/viewable with the largest area (the game
# window, not the 1x1 "Default IME" helper window WINE also creates).
#
# Usage: screenshot.sh [output.png]
#   default output: /tmp/enbshot/client-<n>.png (auto-incremented so a sequence
#   of shots in one debug loop doesn't clobber each other)
# Prints the absolute path of the written PNG on success.
set -euo pipefail

OUT="${1:-}"
if [ -z "$OUT" ]; then
    mkdir -p /tmp/enbshot
    n=1
    while [ -e "/tmp/enbshot/client-$n.png" ]; do n=$((n+1)); done
    OUT="/tmp/enbshot/client-$n.png"
fi

export DISPLAY="${DISPLAY:-:0}"

# Multibox override: pin a specific client.exe window by id (two clients of
# identical size are indistinguishable to the largest-viewable heuristic below).
if [ -n "${ENB_CLIENT_WID:-}" ]; then
    import -window "$ENB_CLIENT_WID" "$OUT"
    echo "$OUT"
    exit 0
fi

# Choose the largest viewable client.exe window.
best_id=""; best_area=0
for id in $(xdotool search --class 'client.exe' 2>/dev/null); do
    info="$(xwininfo -id "$id" 2>/dev/null)" || continue
    echo "$info" | grep -qi "Map State: IsViewable" || continue
    w=$(echo "$info" | awk -F': ' '/Width:/{print $2}')
    h=$(echo "$info" | awk -F': ' '/Height:/{print $2}')
    [ -n "$w" ] && [ -n "$h" ] || continue
    area=$((w * h))
    if [ "$area" -gt "$best_area" ]; then best_area=$area; best_id=$id; fi
done

if [ -z "$best_id" ]; then
    echo "ERROR: no viewable client.exe window found. Is the game running (just play-local)?" >&2
    exit 1
fi

import -window "$best_id" "$OUT"
echo "$OUT"
