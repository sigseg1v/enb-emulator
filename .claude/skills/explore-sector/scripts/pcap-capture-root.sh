#!/usr/bin/env bash
# SPDX-License-Identifier: MIT
# Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
# License: LICENSES/Freya
#
# pcap-capture-root.sh -- the PRIVILEGED half of per-sector packet capture.
#
# Capturing the proxy traffic means running tcpdump inside the proxy CONTAINER's
# network namespace (the cleartext proxy<->server UDP leg plus the encrypted
# client<->proxy TCP leg), which needs root. This script is meant to be installed
# ROOT-OWNED to /usr/local/sbin/freya-pcap-capture by pcap-install.sh and granted
# a SCOPED NOPASSWD sudoers rule for that one path -- so the survey can rotate one
# capture per sector unattended WITHOUT a blanket `nsenter`/`tcpdump` sudo grant
# (which would be root-for-anything). Do not NOPASSWD this file in its repo
# location: a user-writable sudo target is a privilege-escalation footgun. The
# unprivileged control wrapper is pcap.sh; users/skills call that, not this.
#
#   freya-pcap-capture start  <sector> <outdir>   -> start a capture, echo the .pcap path
#   freya-pcap-capture stop            <outdir>   -> SIGINT the running tcpdump (flushes), echo the file
#   freya-pcap-capture status          <outdir>   -> "running <file>" | "idle"
#
# One capture at a time per <outdir>; rotation (stop+start) is the control
# wrapper's job. Capture files are chown'd to the invoking user via tcpdump -Z so
# they are readable/deletable without root.
set -uo pipefail

action="${1:-}"; sector="${2:-}"; outdir="${3:-}"

die() { echo "[pcap-root][ERR] $*" >&2; exit 1; }
[ "$(id -u)" = 0 ] || die "must run as root (via sudo)"

# Files are written by tcpdump -Z <user> so they belong to the survey user, not
# root. SUDO_USER is the human who invoked sudo; fall back to root if absent.
owner="${SUDO_USER:-root}"

valid_dir() { case "$1" in /*) [ -d "$1" ];; *) return 1;; esac; }
# filename-safe sector token: keep alnum/_/- , collapse everything else to '_'.
safe_token() { printf '%s' "$1" | tr -c 'A-Za-z0-9_-' '_' | sed 's/__*/_/g; s/^_//; s/_$//'; }

proxy_pid() {
    local cid
    cid="$(docker ps -q --filter 'label=com.docker.compose.service=proxy' 2>/dev/null | head -1)"
    [ -n "$cid" ] || return 1
    docker inspect -f '{{.State.Pid}}' "$cid" 2>/dev/null
}

case "$action" in
    start)
        [ -n "$sector" ] || die "start needs a sector name"
        valid_dir "$outdir" || die "outdir must be an existing absolute dir: '$outdir'"
        pidf="$outdir/.pcap-capture.pid"
        if [ -f "$pidf" ] && kill -0 "$(cat "$pidf" 2>/dev/null)" 2>/dev/null; then
            die "a capture is already running (pid $(cat "$pidf")); stop it first"
        fi
        pid="$(proxy_pid)" || die "proxy container not running (no com.docker.compose.service=proxy)"
        [ -n "$pid" ] && [ "$pid" != 0 ] || die "could not resolve proxy container PID"
        ts="$(date -u +%Y%m%dT%H%M%SZ)"
        out="$outdir/$(safe_token "$sector")__${ts}.pcap"
        # setsid: new session detached from sudo's pty (their sudoers sets use_pty,
        # which would otherwise reap a backgrounded child when sudo exits). The
        # inner shell writes its OWN pid (== tcpdump after exec) to the pidfile so
        # `stop` can SIGINT exactly the tcpdump. -U = packet-buffered so no frames
        # are lost if it is killed; -Z hands the output file to the survey user.
        setsid sh -c '
            echo $$ > "'"$pidf"'"
            exec nsenter -t '"$pid"' -n \
                 tcpdump -i any -nn -s0 -U -Z "'"$owner"'" -w "'"$out"'" "udp or tcp"
        ' </dev/null >/dev/null 2>&1 &
        # give the inner shell a moment to write the pidfile + tcpdump to open the file
        for _ in 1 2 3 4 5 6 7 8 9 10; do [ -s "$out" ] || [ -f "$pidf" ] && break; sleep 0.2; done
        printf '%s\n' "$out" > "$outdir/.pcap-capture.current"
        chown "$owner" "$pidf" "$outdir/.pcap-capture.current" 2>/dev/null || true
        echo "$out"
        ;;
    stop)
        valid_dir "$outdir" || die "outdir must be an existing absolute dir: '$outdir'"
        pidf="$outdir/.pcap-capture.pid"
        cur="$(cat "$outdir/.pcap-capture.current" 2>/dev/null || true)"
        [ -f "$pidf" ] || { echo "${cur:-(idle)}"; rm -f "$outdir/.pcap-capture.current"; exit 0; }
        p="$(cat "$pidf" 2>/dev/null)"
        if [ -n "$p" ]; then kill -INT "$p" 2>/dev/null || true; fi
        for _ in $(seq 1 15); do kill -0 "$p" 2>/dev/null || break; sleep 0.3; done
        kill -0 "$p" 2>/dev/null && kill -TERM "$p" 2>/dev/null || true
        rm -f "$pidf" "$outdir/.pcap-capture.current"
        echo "${cur:-stopped}"
        ;;
    relabel)
        # Rename the in-progress capture to a new sector token, KEEPING the
        # original UTC timestamp. Safe while tcpdump is writing: mv preserves the
        # inode its fd points at, so capture continues uninterrupted. Used when the
        # capture was opened with a placeholder name (char-select, before the first
        # sector is known) and drive.py has now identified the real sector.
        [ -n "$sector" ] || die "relabel needs a sector name"
        valid_dir "$outdir" || die "outdir must be an existing absolute dir: '$outdir'"
        cur="$(cat "$outdir/.pcap-capture.current" 2>/dev/null || true)"
        [ -n "$cur" ] && [ -f "$cur" ] || { echo "(nothing to relabel)"; exit 0; }
        base="$(basename "$cur")"; ts="${base##*__}"        # <token>__<ts>.pcap -> <ts>.pcap
        new="$outdir/$(safe_token "$sector")__${ts}"
        if [ "$new" != "$cur" ]; then
            mv -f "$cur" "$new"
            printf '%s\n' "$new" > "$outdir/.pcap-capture.current"
            chown "$owner" "$outdir/.pcap-capture.current" 2>/dev/null || true
        fi
        echo "$new"
        ;;
    status)
        valid_dir "$outdir" || die "outdir must be an existing absolute dir: '$outdir'"
        pidf="$outdir/.pcap-capture.pid"
        if [ -f "$pidf" ] && kill -0 "$(cat "$pidf" 2>/dev/null)" 2>/dev/null; then
            echo "running $(cat "$outdir/.pcap-capture.current" 2>/dev/null)"
        else
            echo "idle"
        fi
        ;;
    *)
        die "usage: $(basename "$0") {start <sector> <outdir>|stop <outdir>|relabel <sector> <outdir>|status <outdir>}"
        ;;
esac
