#!/usr/bin/env bash
# SPDX-License-Identifier: MIT
# Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
# License: LICENSES/Freya
#
# explore-live.sh -- run the sector survey against a LIVE client you attached
# enbmod into (native client.exe under WINE + Net7Proxy.exe, NO Freya docker
# stack), with unattended crash recovery.
#
# Difference from the explore-sector skill's own explore-live.sh: that one assumes
# OUR launcher wrote FreyaLauncher.settings.json so it can find the client dir, and
# it HALTS when the client wedges. This entry is built for a client we did NOT
# launch:
#   * it resolves the enbmod store dir from the RUNNING client's /proc/<pid>/cwd
#     (correct for any WINE prefix / mbox slot -- settings.json would be wrong), and
#   * on a crash/wedge it re-launches the real Net-7 launcher (LaunchNet7.exe),
#     clicks Play, re-injects enbmod, and lets enbmod autologin return to in-game --
#     then the survey resumes (recover.sh, wired via ENB_RECOVER_CMD).
#
# It reuses the explore-sector survey engine (drive_lua.py) unchanged; this skill
# only owns the live/attach wiring + recovery.
#
#   bash .claude/skills/explore-live/scripts/explore-live.sh
#
# Config comes from a gitignored .env next to this skill (copy .env.example). Exit
# codes: 2 = a preflight precondition failed (nothing was driven); otherwise the
# drive_lua.py exit code (0 done, 3 gravity well, 42 wedged-and-unrecoverable).
set -uo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SKILL_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"
EXPLORE_SECTOR_DIR="$(cd "$SKILL_DIR/../explore-sector" && pwd)"

# client_win()/win_abs()/wait_until() come from the login-to-client skill's lib.sh
# (shared WINE window helpers); it has its own REPO_ROOT and no symbol clash.
. "$SCRIPT_DIR/../../login-to-client/scripts/lib.sh"
die()  { printf '[explore-live] FATAL: %s\n' "$*" >&2; exit 2; }
note() { printf '[explore-live] %s\n' "$*" >&2; }

# ---- 1. load .env -----------------------------------------------------------
ENV_FILE="$SKILL_DIR/.env"
if [ -f "$ENV_FILE" ]; then
    set -a; . "$ENV_FILE"; set +a
else
    die "no .env found. Copy the template and fill it in:
    cp $SKILL_DIR/.env.example $ENV_FILE
    \$EDITOR $ENV_FILE     # account name/pass, character, workdir"
fi

# ---- 2. resolve the WINE prefix + client dir (attached client) --------------
# WINE prefix: explicit ENB_WINEPREFIX wins; else read it off a running client;
# else default to the conventional per-user prefix. Never hardcode a home path.
resolve_wineprefix() {
    [ -n "${ENB_WINEPREFIX:-}" ] && { printf '%s\n' "$ENB_WINEPREFIX"; return; }
    local win pid wp
    for win in $(xdotool search --class client.exe 2>/dev/null); do
        pid="$(xdotool getwindowpid "$win" 2>/dev/null)" || continue
        [ -n "$pid" ] || continue
        wp="$(tr '\0' '\n' < "/proc/$pid/environ" 2>/dev/null | sed -n 's/^WINEPREFIX=//p' | head -1)"
        [ -n "$wp" ] && { printf '%s\n' "$wp"; return; }
    done
    printf '%s\n' "${HOME}/.wine-enb"
}
export ENB_WINEPREFIX="$(resolve_wineprefix)"

# enbmod store dir = the running client.exe's /proc/<pid>/cwd (only if it holds the
# enbmod store). If no client is up yet, recovery will launch one and this resolves
# on the retry; for now fall back to the conventional release dir so preflight can
# still report paths.
resolve_client_dir() {
    local win pid cwd
    for win in $(xdotool search --class client.exe 2>/dev/null); do
        pid="$(xdotool getwindowpid "$win" 2>/dev/null)" || continue
        [ -n "$pid" ] || continue
        cwd="$(readlink -f "/proc/$pid/cwd" 2>/dev/null)" || continue
        if [ -f "$cwd/enbmod.dll" ] || [ -f "$cwd/enbmod.cmd" ]; then
            printf '%s\n' "$cwd"; return 0
        fi
    done
    return 1
}
if CDIR="$(resolve_client_dir)"; then
    export ENB_CLIENT_DIR="$CDIR"
    note "client dir (from /proc cwd): $ENB_CLIENT_DIR"
else
    export ENB_CLIENT_DIR="${ENB_CLIENT_DIR:-$ENB_WINEPREFIX/drive_c/Program Files/EA GAMES/Earth & Beyond/release}"
    note "no attached client yet; client dir defaults to $ENB_CLIENT_DIR (recovery will launch one)"
fi

# ---- 3. credentials for enbmod autologin (from the gitignored .env) ----------
# enbmod's in-client autologin reads these from the client PROCESS environment
# (autologin.cpp: FREYA_/ENB_ ACC_NAME/ACC_PASS/CHARACTER/EULA). recover.sh exports
# them onto LaunchNet7 so client.exe inherits them; no OCR, no typed input.
[ -n "${ENB_ACC_NAME:-}" ]  || die "ENB_ACC_NAME not set in $ENV_FILE (the account to log in)."
[ -n "${ENB_ACC_PASS:-}" ]  || die "ENB_ACC_PASS not set in $ENV_FILE."
[ -n "${ENB_CHARACTER:-}" ] || die "ENB_CHARACTER not set in $ENV_FILE (the character to enter)."
export ENB_ACC_NAME ENB_ACC_PASS ENB_CHARACTER
export ENB_EULA="${ENB_EULA:-ACCEPT}"

# ---- 4. live/attach invariants + recovery hook ------------------------------
export ENB_EXPLORE_MANUAL_CLIENT=1                # never touch a docker stack
export ENB_EXPLORE_PROXY_NAME="${ENB_EXPLORE_PROXY_NAME:-Net7Proxy.exe}"
export ENB_PCAP="${ENB_PCAP:-1}"
export ENB_RECOVER_CMD="bash '$SCRIPT_DIR/recover.sh'"   # drive_lua runs this on a wedge

# ---- 5. preflight (fail LOUD, before anything is driven) --------------------
[ -n "${ENB_EXPLORE_WORKDIR:-}" ] || die "ENB_EXPLORE_WORKDIR not set in $ENV_FILE (persistent path outside the repo)."
mkdir -p "$ENB_EXPLORE_WORKDIR" 2>/dev/null || die "cannot create ENB_EXPLORE_WORKDIR=$ENB_EXPLORE_WORKDIR"
[ -w "$ENB_EXPLORE_WORKDIR" ] || die "ENB_EXPLORE_WORKDIR not writable: $ENB_EXPLORE_WORKDIR"
command -v xdotool >/dev/null 2>&1 || die "xdotool not found (need it to find the client window / drive recovery)."
: "${DISPLAY:=:0}"; export DISPLAY
xdotool getdisplaygeometry >/dev/null 2>&1 || die "cannot reach X on DISPLAY=$DISPLAY. Is the desktop session up?"

# The launcher must be resolvable NOW (recovery needs it). Resolve + fail early
# with the exact fix rather than only discovering it after a mid-run crash.
export ENB_LAUNCHNET7="${ENB_LAUNCHNET7:-$ENB_WINEPREFIX/drive_c/Program Files (x86)/Net-7/bin/LaunchNet7.exe}"
if [ ! -f "$ENB_LAUNCHNET7" ]; then
    FOUND="$(find "$ENB_WINEPREFIX/drive_c" -iname 'LaunchNet7.exe' 2>/dev/null | head -1)"
    if [ -n "$FOUND" ]; then
        export ENB_LAUNCHNET7="$FOUND"
        note "LaunchNet7.exe found at $ENB_LAUNCHNET7 (set ENB_LAUNCHNET7 in .env to pin it)"
    else
        die "LaunchNet7.exe not found (looked at ENB_LAUNCHNET7 + under $ENB_WINEPREFIX/drive_c).
    Set ENB_LAUNCHNET7=<path to LaunchNet7.exe> in $ENV_FILE so recovery can relaunch the client."
    fi
fi

# ---- 6. proxy detect: LIVE (WINE Net7Proxy) vs LOCAL (docker net7proxy) ------
if pgrep -x -- "$ENB_EXPLORE_PROXY_NAME" >/dev/null 2>&1; then
    note "proxy running (LIVE): $ENB_EXPLORE_PROXY_NAME -- capture stays ENB_PCAP=$ENB_PCAP"
elif pgrep -x net7proxy >/dev/null 2>&1 || pgrep -f '/app/net7proxy' >/dev/null 2>&1; then
    export ENB_EXPLORE_PROXY_NAME=net7proxy ENB_PCAP=0
    note "WINE proxy down but docker net7proxy is up -> LOCAL run: capture disabled (ENB_PCAP=0)"
else
    note "no proxy running yet; recovery/LaunchNet7 Play is expected to start $ENB_EXPLORE_PROXY_NAME"
fi
if [ "$ENB_PCAP" = 1 ]; then
    WORKER="${ENB_PCAP_WORKER:-/usr/local/sbin/freya-pcap-capture}"
    OUTDIR="${ENB_PCAP_DIR:-$ENB_EXPLORE_WORKDIR/captures}"
    mkdir -p "$OUTDIR" 2>/dev/null
    [ -x "$WORKER" ] && sudo -n "$WORKER" status "$OUTDIR" >/dev/null 2>&1 || \
        die "ENB_PCAP=1 but the capture worker is not usable.
    Run: bash $EXPLORE_SECTOR_DIR/scripts/pcap-install.sh   (once, with sudo)
       or set ENB_PCAP=0 in $ENV_FILE to survey without capturing."
fi

# ---- 7. ensure in-game, recovering (launch+inject+autologin) if needed ------
INGAME="$SCRIPT_DIR/../../login-to-client/scripts/08-wait-ingame.sh"
if ! ENB_CLIENT_DIR="$ENB_CLIENT_DIR" bash "$INGAME" >/dev/null 2>&1; then
    note "client not in-game -- running recovery to bring it up (LaunchNet7 -> Play -> inject -> autologin)"
    if ! bash "$SCRIPT_DIR/recover.sh"; then
        die "could not bring the client to in-game (see recover.sh output above)."
    fi
    # recovery may have created the client dir; re-resolve so drive_lua uses it.
    if CDIR="$(resolve_client_dir)"; then export ENB_CLIENT_DIR="$CDIR"; fi
fi

note "handing off to the survey engine (drive_lua.py): char=$ENB_CHARACTER proxy=$ENB_EXPLORE_PROXY_NAME pcap=$ENB_PCAP"
exec python3 "$EXPLORE_SECTOR_DIR/scripts/drive_lua.py" "$@"
