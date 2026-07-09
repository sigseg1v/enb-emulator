#!/usr/bin/env bash
# SPDX-License-Identifier: MIT
# Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
# License: LICENSES/Freya
#
# pcap.sh -- UNPRIVILEGED control for per-sector packet capture. One .pcap file
# per sector, each started BEFORE we enter that sector (at char-select for the
# first sector, or at the gate just before we open it for subsequent ones) so the
# capture includes the sector's entry handshake. The actual tcpdump needs root +
# the proxy netns, so this delegates to the installed privileged worker
# (/usr/local/sbin/freya-pcap-capture) via `sudo -n` and drops a correlation
# marker into the action log -- pcap frames are UTC, actions.log is UTC, so the
# log timestamps line up with the capture directly.
#
#   pcap.sh start  <sector>   -- begin a capture for <sector> (errors if one runs)
#   pcap.sh rotate <sector>   -- stop the current capture, begin one for <sector>
#   pcap.sh stop   [sector]   -- end the current capture
#   pcap.sh status            -- running file or idle
#
# Capture is BEST-EFFORT: if the worker is not installed / the sudo rule is
# missing, it warns once and no-ops with exit 0 so a missing capture never blocks
# the survey. Run scripts/pcap-install.sh ONCE (with sudo) to enable it.
set -uo pipefail
SKILL_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# pcaps live under the survey work root (ENB_EXPLORE_WORKDIR) in captures/, alongside
# the ledgers + action log so one folder holds a whole survey. ENB_PCAP_DIR still wins
# if set explicitly. Default work root: the skill's own state/ dir.
OUTDIR="${ENB_PCAP_DIR:-${ENB_EXPLORE_WORKDIR:-$SKILL_DIR/../state}/captures}"
WORKER="${ENB_PCAP_WORKER:-/usr/local/sbin/freya-pcap-capture}"
# Which proxy to capture: empty/"freya" == the dockerized FreyaProxy container;
# anything else (e.g. "Net7Proxy.exe") is captured as a host process under WINE.
PROXY_NAME="${ENB_EXPLORE_PROXY_NAME:-}"
mkdir -p "$OUTDIR"; OUTDIR="$(cd "$OUTDIR" && pwd)"

action="${1:-}"; sector="${2:-}"

# Capture is for the LIVE reference server ONLY -- NEVER for a local freya run
# (owner rule, 2026-06-22). explore-live.sh forces ENB_PCAP=0 the moment it
# detects the docker proxy, but that downgrade only matters if THIS script
# honours it: drive_lua.py calls `pcap.sh ensure <sector>` unconditionally, so the
# gate has to live here, at the single chokepoint every capture-starting action
# passes through. With ENB_PCAP=0 the start/rotate/ensure actions no-op (exit 0,
# no tcpdump ever spawns); `stop` still runs so a capture left over from a prior
# live run can always be torn down.
if [ "${ENB_PCAP:-1}" = 0 ]; then
    case "$action" in
        start|rotate|ensure)
            echo "[pcap] disabled (ENB_PCAP=0) -- not capturing '$sector' (local freya run)" >&2
            exit 0
            ;;
    esac
fi

# True only if the passwordless worker is actually callable (installed + sudoers).
worker_ready() { [ -x "$WORKER" ] && sudo -n "$WORKER" status "$OUTDIR" >/dev/null 2>&1; }

mark() { bash "$SKILL_DIR/logaction.sh" "$1" "$2" "$3" >/dev/null 2>&1 || true; }

# Durable cap-file -> sector mapping so we always know which sector a .pcap belongs
# to, independent of the filename rename (owner: "rename the cap OR keep a mapping
# file"). One TSV row per start/relabel: <UTC>  <sector>  <cap-basename>. Append-only
# (the last row for a cap basename is its current sector after any relabels).
map_record() {
    local sector="$1" cappath="$2" ts
    [ -n "$cappath" ] || return 0
    ts="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
    printf '%s\t%s\t%s\n' "$ts" "$sector" "$(basename "$cappath")" >> "$OUTDIR/sector-map.tsv"
}

case "$action" in
    start|rotate)
        [ -n "$sector" ] || { echo "usage: pcap.sh $action <sector>" >&2; exit 2; }
        if ! worker_ready; then
            echo "[pcap] SKIP -- capture worker not configured; run scripts/pcap-install.sh once (sudo). Survey continues without capture." >&2
            exit 0
        fi
        sudo -n "$WORKER" stop "$OUTDIR" >/dev/null 2>&1 || true   # rotate: close prev sector's file
        out="$(sudo -n "$WORKER" start "$sector" "$OUTDIR" "$PROXY_NAME" 2>/dev/null | tail -1)"
        if [ -n "$out" ] && [ -e "$out" ]; then
            mark "$sector" pcap-start "capture -> $(basename "$out")"
            map_record "$sector" "$out"
            echo "[pcap] capturing '$sector' -> $out"
        else
            echo "[pcap] WARN -- worker did not start a capture (proxy down? stack not up?)" >&2
            exit 0
        fi
        ;;
    ensure)
        # Make sure SOME capture is running and labelled for <sector>: relabel an
        # already-running capture (e.g. the char-select placeholder) to <sector>,
        # or start a fresh one. Idempotent -- safe to call at drive_lua.py startup
        # whether we just logged in, resumed mid-sector, or just crossed a gate.
        [ -n "$sector" ] || { echo "usage: pcap.sh ensure <sector>" >&2; exit 2; }
        worker_ready || { echo "[pcap] SKIP -- capture worker not configured (scripts/pcap-install.sh)." >&2; exit 0; }
        if sudo -n "$WORKER" status "$OUTDIR" 2>/dev/null | grep -q '^running'; then
            out="$(sudo -n "$WORKER" relabel "$sector" "$OUTDIR" 2>/dev/null | tail -1)"
            mark "$sector" pcap-relabel "capture -> $(basename "${out:-?}")"
            map_record "$sector" "${out:-}"
            echo "[pcap] capture relabelled '$sector' -> ${out:-?}"
        else
            out="$(sudo -n "$WORKER" start "$sector" "$OUTDIR" "$PROXY_NAME" 2>/dev/null | tail -1)"
            if [ -n "$out" ] && [ -e "$out" ]; then
                mark "$sector" pcap-start "capture -> $(basename "$out")"
                map_record "$sector" "$out"
                echo "[pcap] capturing '$sector' -> $out"
            else
                echo "[pcap] WARN -- could not start capture (proxy/stack down?)" >&2
            fi
        fi
        ;;
    stop)
        worker_ready || { echo "[pcap] (worker unavailable)"; exit 0; }
        out="$(sudo -n "$WORKER" stop "$OUTDIR" 2>/dev/null | tail -1)"
        [ -n "$sector" ] && mark "$sector" pcap-stop "ended $(basename "${out:-?}")"
        echo "[pcap] stopped ${out:-}"
        ;;
    status)
        if worker_ready; then sudo -n "$WORKER" status "$OUTDIR" 2>/dev/null
        else echo "idle (worker not configured -- run scripts/pcap-install.sh)"; fi
        ;;
    *)
        echo "usage: pcap.sh {start|rotate|ensure|stop|status} [sector]" >&2; exit 2
        ;;
esac
