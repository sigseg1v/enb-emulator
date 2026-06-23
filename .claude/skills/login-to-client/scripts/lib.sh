#!/usr/bin/env bash
# SPDX-License-Identifier: MIT
# Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
# License: LICENSES/Freya
#
# lib.sh -- shared helpers for the login-to-client skill. Source it:
#   SKILL_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"; . "$SKILL_DIR/lib.sh"
#
# Everything talks to X11 via XTEST (xdotool) at ABSOLUTE root coordinates: we
# raise the target window, read its real geometry from xwininfo, then move the
# real pointer + click. XSendEvent (`xdotool --window`) is unreliable for WINE
# and Avalonia, so we never use it. Screenshots come from `import -window` so a
# window's screenshot pixel coords map 1:1 to window-relative click coords.
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
SHOTSH="$REPO_ROOT/.claude/skills/take-client-screenshot/scripts/screenshot.sh"
DETECT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/detect.py"
REFS="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/refs"
COORDS="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/coords.json"

LOGIN_USER="${ENB_LOGIN_USER:-devuser}"
LOGIN_PASS="${ENB_LOGIN_PASS:-devpass}"

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

client_win()   { win_by_class 'client.exe'; }
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

# Raise + focus a window so the click is not eaten by an occluding window.
raise_win() {
    local id="$1"
    wmctrl -i -a "$id" 2>/dev/null
    xdotool windowactivate "$id" 2>/dev/null
    xdotool windowraise "$id" 2>/dev/null
}

# ---- input ------------------------------------------------------------------
# Click at window-relative (relx,rely) using absolute XTEST. Raises first.
#
# We do NOT use `xdotool mousemove --sync`: --sync waits for a MotionNotify that the
# WINE client never confirms here, so it blocked ~15s PER CLICK (the whole reason a
# nav-cycle crawled at ~10s/nav). Plain mousemove lands in ~3ms. --sync was meant to
# beat a pointer-warping VM, but the cure for THAT is pausing the VM (see the
# login-to-client "QEMU/KVM pointer warp" note), not blocking on every click. We
# instead move, verify the pointer actually landed (cheap XQueryPointer), retry once
# if a stray warp bumped it, then click.
click_win() {
    local id="$1" rx="$2" ry="$3" g x y loc px py
    local gx gy gw gh
    raise_win "$id"
    g="$(win_abs "$id")" || { err "click_win: no geometry for $id"; return 1; }
    read -r gx gy gw gh <<< "$g"
    x=$(( gx + rx ))
    y=$(( gy + ry ))
    log "click ($rx,$ry) -> abs ($x,$y) on win $id"
    xdotool mousemove "$x" "$y" 2>/dev/null
    # verify the pointer landed; one cheap retry covers a stray warp.
    loc="$(xdotool getmouselocation --shell 2>/dev/null)"; eval "$loc" 2>/dev/null
    px="${X:-}"; py="${Y:-}"
    if [ "$px" != "$x" ] || [ "$py" != "$y" ]; then
        xdotool mousemove "$x" "$y" 2>/dev/null
    fi
    xdotool click 1
}

click_abs() { xdotool mousemove --sync "$1" "$2"; sleep 0.12; xdotool click 1; sleep 0.15; }

# Type into the currently-focused window (click a field first).
type_text() { xdotool type --delay 60 -- "$1"; }
send_key()  { xdotool key "$1"; }

# ---- screenshots ------------------------------------------------------------
# Full client window screenshot -> path. Returns 1 if no client window.
shot() { local out="$1"; [ -x "$SHOTSH" ] || { err "no screenshot.sh"; return 1; }; "$SHOTSH" "$out" >/dev/null 2>&1; }

# Screenshot an arbitrary window id (e.g. the launcher) -> path.
shot_win() { import -window "$1" "$2" >/dev/null 2>&1; }

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

# ---- coordinates (coords.json) ----------------------------------------------
# Echo "X Y" for a named coord, e.g. `coord login user` -> "300 232".
coord() {
    python3 -c "import json;d=json.load(open('$COORDS'));print(*d['$1']['$2'])" 2>/dev/null
}
# Click a named coord on a window: `click_coord <winid> login accept`.
click_coord() {
    local id="$1" rx ry
    read -r rx ry <<< "$(coord "$2" "$3")"
    [ -n "$rx" ] || { err "click_coord: no coord $2.$3"; return 1; }
    click_win "$id" "$rx" "$ry"
}

# ---- detection (PIL+numpy via detect.py) ------------------------------------
# All return 0/1 exit codes and may print a value.
detect() { python3 "$DETECT" "$@"; }

# Is the live client window currently showing screen <name>? Uses the ref crop
# refs/<name>.png matched at its known (X,Y). Captures a fresh shot first.
# Usage: is_screen <name> <x> <y> [tol]  -> exit 0 if matched.
is_screen() {
    local name="$1" x="$2" y="$3" tol="${4:-18}" w p
    w="$(client_win)" || return 1
    p="$WORKDIR/is_${name}.png"
    shot_win "$w" "$p" || return 1
    detect match "$p" "$REFS/$name.png" "$x" "$y" "$tol" >/dev/null 2>&1
}
# tol 12, NOT 22: the login_field crop at (165,228) is only a 90x24 opaque-UI
# region, and an IN-SPACE frame's chat panel scores MAD ~18 there -- under 22 it
# false-matched as "login" while genuinely in space (which made read-sector's
# in-space gate refuse valid sectors). A real login screen scores ~3-4 against its
# own ref (cf. charselect_frame at 3.68), so 12 cleanly separates real login (~4)
# from in-space (~18) and from char-select (~37).
on_login()      { is_screen login_field 165 228 "${1:-12}"; }
# tol 12, NOT 22 (owner, 2026-06-22): same false-positive class as on_login. A real
# char-select scores MAD ~3.68 against charselect_frame, but an IN-SPACE frame scores
# ~20.9 at (70,50) -- under the old tol 22 that read as char-select, so detect_sector's
# recovery ran 07-charselect-enter on a client that was actually in space and
# blind-clicked the HUD ("still on character-select" loop). 12 cleanly separates real
# char-select (~4) from in-space (~21).
on_charselect() { is_screen charselect_frame 70 50 "${1:-12}"; }

# Convenience: capture a fresh client shot to $WORKDIR/cur.png and echo path.
cur_shot() { local p="$WORKDIR/cur.png"; shot "$p" && echo "$p"; }
