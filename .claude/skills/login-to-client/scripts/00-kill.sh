#!/usr/bin/env bash
# SPDX-License-Identifier: MIT
# Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
# License: LICENSES/Freya
#
# 00-kill.sh -- clean slate. Kills any running client.exe + its WINE server,
# the FreyaLauncher, leftover FreyaProxy / auth relay, and tears the docker
# stack down (containers + network; pgdata survives -- `just down`). Idempotent.
SKILL_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
. "$SKILL_DIR/lib.sh"

log "killing client / launcher / proxy processes"
# WINE client + its services. SIGKILL: a hung/wedged client never exits cleanly.
pkill -9 -f 'client.exe'        2>/dev/null && log "  killed client.exe"
pkill -9 -f 'FreyaInject'       2>/dev/null
pkill -9 -f 'FreyaProxy'        2>/dev/null && log "  killed FreyaProxy"
pkill -9 -f 'FreyaLauncher'     2>/dev/null && log "  killed FreyaLauncher"
pkill -9 -f 'dotnet run.*LaunchFreya' 2>/dev/null
pkill -9 -f 'LaunchFreya'       2>/dev/null
# WINE bookkeeping so the next launch starts a fresh prefix server.
pkill -9 -f 'winedbg'           2>/dev/null && log "  killed winedbg"
# Best-effort: ask wineserver to die (it exits on its own once clients are gone).
WINEPREFIX="${WINEPREFIX:-$HOME/.wine-enb}" wineserver -k 2>/dev/null || true

# Clear any pending enbmod command so a stale line can't fire into a fresh client.
cd_dir="$(client_dir)"
if [ -n "$cd_dir" ] && [ -f "$cd_dir/enbmod.cmd" ]; then
    : > "$cd_dir/enbmod.cmd"; log "  cleared enbmod.cmd"
fi

log "tearing down docker stack (just down -- pgdata survives)"
( cd "$REPO_ROOT" && just down ) >/dev/null 2>&1 || err "just down returned nonzero (continuing)"

# Verify the client window is really gone.
sleep 1
if client_win >/dev/null 2>&1; then
    err "STUCK=kill: a client.exe window is still present"
    exit 1
fi
log "STATE=clean (no client window; stack down)"
