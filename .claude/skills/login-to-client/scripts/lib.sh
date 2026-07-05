#!/usr/bin/env bash
# SPDX-License-Identifier: MIT
# Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
# License: LICENSES/Freya
#
# lib.sh -- shared helpers for the login-to-client skill. Source it:
#   SKILL_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"; . "$SKILL_DIR/lib.sh"
#
# The skill is OCR-free (the client auto-logs-in; 08 confirms over the enbmod Lua
# channel), so the only X11 use left is window DISCOVERY -- we look up the client /
# launcher window and read its geometry from xwininfo to answer "is it up?". No
# synthetic input, no screenshots, no ref matching.
set -uo pipefail

export DISPLAY="${DISPLAY:-:0}"
# xdotool/import authenticate to the X server with the MIT-MAGIC-COOKIE in
# XAUTHORITY. A caller env that sets DISPLAY but not XAUTHORITY (e.g. a detached
# background task) makes every xdotool call die with "Invalid MIT-MAGIC-COOKIE-1
# key" / "Can't open display: (null)" -- which silently breaks the EULA/login
# clicks while the earlier docker steps still pass. Default it to the user's
# cookie so the skill works regardless of how it was launched.
export XAUTHORITY="${XAUTHORITY:-$HOME/.Xauthority}"

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../../.." && pwd)"

# This skill is OCR-FREE. The client logs itself in via the injected enbmod
# auto-login and we confirm in-game over the enbmod Lua channel (08-wait-ingame),
# so there are no screenshot / ref-match / synthetic-input helpers here -- only
# window discovery (for "is the client up?"), docker/psql, and the wait loop.

# Docker compose project name is BRANCH-DERIVED by the justfile (so per-branch
# stacks don't collide): main/master -> "freya", any other branch -> "freya-<branch>"
# (lowercased, non-alnum -> '-'). Mirror that EXACTLY here so our bare `docker
# compose` calls target the SAME project `just` brings up -- otherwise we query
# the empty default "freya" project and think the stack is down. (This bit us once.)
if [ -z "${COMPOSE_PROJECT_NAME:-}" ]; then
    _b="$(cd "$REPO_ROOT" && git branch --show-current 2>/dev/null)"
    if [ -z "$_b" ] || [ "$_b" = main ] || [ "$_b" = master ]; then
        COMPOSE_PROJECT_NAME=freya
    else
        _s="$(printf '%s' "$_b" | tr 'A-Z' 'a-z' | tr -c 'a-z0-9_-' '-' | tr -s '-' | sed 's/^-//;s/-$//')"
        case "$_s" in freya-*) COMPOSE_PROJECT_NAME="$_s";; *) COMPOSE_PROJECT_NAME="freya-$_s";; esac
    fi
fi
export COMPOSE_PROJECT_NAME
LOGIN_USER="${ENB_LOGIN_USER:-devuser}"
LOGIN_PASS="${ENB_LOGIN_PASS:-devpass}"
# Character to auto-enter. seed-dev-account names characters <user><class-suffix>
# and creates them te/je/jd/tt/ts in order, so slot 0 is "<user>te" (and 02-seed
# forces slot 0 to start in space). Override with ENB_LOGIN_CHAR.
LOGIN_CHAR="${ENB_LOGIN_CHAR:-${LOGIN_USER}te}"

# Where the work screenshots land (timestamped run dir for post-mortems).
WORKDIR="${ENB_LOGIN_WORKDIR:-/tmp/enblogin}"
mkdir -p "$WORKDIR"

log()  { printf '[login] %s\n' "$*" >&2; }
err()  { printf '[login][ERR] %s\n' "$*" >&2; }

# ---- postgres (net7_user save-state DB) -------------------------------------
# Run SQL piped on stdin against the running stack's net7_user DB. psql does NOT
# interpolate :'var' / :var in a -c string, only in SQL read from stdin -- so we
# always pipe the query and pass values as bound psql variables (-v k=v), never
# string-concatenated (see CLAUDE.md "no string-concat SQL"). Extra -v bindings
# are forwarded as args: psql_user "SELECT ... :aid" -v aid=71
psql_user() {
    local sql="$1"; shift
    printf '%s\n' "$sql" | ( cd "$REPO_ROOT" && docker compose exec -T \
        -e PGPASSWORD=net7 postgres psql -U net7 -d net7_user -tA "$@" ) 2>/dev/null
}

# ---- client dir (enbmod.cmd / enbmod.log live next to client.exe) -----------
client_dir() {
    local s="$REPO_ROOT/tools/LaunchFreya/bin/Debug/net10.0/FreyaLauncher.settings.json"
    [ -f "$s" ] || { echo ""; return 1; }
    python3 -c "import json,os;print(os.path.dirname(json.load(open('$s'))['ClientPath']))" 2>/dev/null
}

# ---- window discovery -------------------------------------------------------
# Largest VIEWABLE window of a given WM_CLASS (skips the 1x1 IME helper).
win_by_class() {
    local cls="$1" best="" barea=0 id info w h area
    for id in $(xdotool search --class "$cls" 2>/dev/null); do
        info="$(xwininfo -id "$id" 2>/dev/null)" || continue
        echo "$info" | grep -qi "Map State: IsViewable" || continue
        w=$(echo "$info" | awk -F': ' '/Width:/{print $2}')
        h=$(echo "$info" | awk -F': ' '/Height:/{print $2}')
        [ -n "$w" ] && [ -n "$h" ] || continue
        area=$((w * h))
        if [ "$area" -gt "$barea" ]; then barea=$area; best=$id; fi
    done
    [ -n "$best" ] && { echo "$best"; return 0; }
    return 1
}

# Resolve the client window id, RETRYING briefly. Scene transitions (dock -> concourse,
# undock, gate crossing) momentarily unmap/remap the WINE window, so a single search can
# transiently miss it and return empty. Retrying for ~1.5s rides out the transition
# instead of conceding "no window" on a blink. A genuinely dead/closed client still
# fails after the retries.
client_win() {
    # Multibox override: pin a specific client.exe window by id (two clients of
    # identical size are indistinguishable to the largest-viewable heuristic).
    [ -n "${ENB_CLIENT_WID:-}" ] && { printf '%s\n' "$ENB_CLIENT_WID"; return 0; }
    local i id
    for i in 1 2 3 4 5 6; do
        id="$(win_by_class 'client.exe')" && { printf '%s\n' "$id"; return 0; }
        sleep 0.25
    done
    return 1
}
launcher_win() {
    # Avalonia reports WM_CLASS "FreyaLauncher"; fall back to the title.
    local id; id="$(win_by_class 'FreyaLauncher')" && { echo "$id"; return 0; }
    id="$(xdotool search --name '^LaunchFreya$' 2>/dev/null | head -1)" && [ -n "$id" ] && { echo "$id"; return 0; }
    return 1
}

# "X Y W H" absolute geometry of a window id (clean single-spaced integers --
# xwininfo pads values with two spaces, so we extract the trailing integer).
win_abs() {
    local id="$1" info x y w h
    info="$(xwininfo -id "$id" 2>/dev/null)" || return 1
    x=$(echo "$info" | awk '/Absolute upper-left X:/{print $NF}')
    y=$(echo "$info" | awk '/Absolute upper-left Y:/{print $NF}')
    w=$(echo "$info" | awk '/Width:/{print $NF}')
    h=$(echo "$info" | awk '/Height:/{print $NF}')
    [ -n "$x" ] && echo "$x $y $w $h"
}

# ---- waiting ----------------------------------------------------------------
# Poll a command until it succeeds or timeout (seconds). Usage:
#   wait_until <timeout> <interval> <cmd...>
wait_until() {
    local timeout="$1" interval="$2"; shift 2
    local end=$((SECONDS + timeout))
    while [ "$SECONDS" -lt "$end" ]; do
        if "$@"; then return 0; fi
        sleep "$interval"
    done
    return 1
}
