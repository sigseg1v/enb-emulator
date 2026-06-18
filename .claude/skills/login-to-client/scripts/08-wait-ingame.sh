#!/usr/bin/env bash
# SPDX-License-Identifier: MIT
# Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
# License: LICENSES/Freya
#
# 08-wait-ingame.sh -- wait through the load screen until the character is fully
# loaded into the world, confirmed via the enbmod Lua channel (NOT a screenshot:
# the load screen is unreliable to classify, but the in-game DLL is authoritative).
#
# "In game" == enb.self() returns a player table. We then report state/inspace:
#   state="unknown" + inspace=false  -> docked in a station bay
#   inspace=true                     -> in open space
# Exit 0 once in-game. Prints "INGAME inspace=<bool> state=<s>" on success.
SKILL_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
. "$SKILL_DIR/lib.sh"

CDIR="$(client_dir)" || { err "STUCK=ingame: cannot resolve client dir"; exit 1; }
CMD="$CDIR/enbmod.cmd"; LOG="$CDIR/enbmod.log"
[ -f "$LOG" ] || { err "STUCK=ingame: no enbmod.log at $LOG (mods not loaded?)"; exit 1; }

# Run a Lua expression through the cmd channel; echo the first [run] result line.
# enbmod.log is written by the Win32 DLL under WINE, so its lines end CRLF -- we
# strip the \r (tr -d '\r') or string comparisons against the value silently fail.
lua_run() {
    local expr="$1" start new
    start=$(wc -l < "$LOG" 2>/dev/null || echo 0)
    printf '%s\n' "$expr" >> "$CMD"
    for _ in $(seq 1 25); do
        new="$(tail -n +$((start + 1)) "$LOG" 2>/dev/null | tr -d '\r' | grep -m1 '^\[run\]')"
        [ -n "$new" ] && { echo "${new#\[run\] }"; return 0; }
        sleep 0.3
    done
    return 1
}

is_ingame() { [ "$(lua_run 'return type(enb.self())')" = "table" ]; }

log "waiting for character to load into the world (Lua: type(enb.self())==table) ..."
if ! wait_until 120 3 is_ingame; then
    err "STUCK=ingame: enb.self() never became a table in 120s (load hang?)"
    exit 1
fi

INSPACE="$(lua_run 'return tostring(enb.inspace())')"
STATE="$(lua_run 'return enb.state()')"
log "STATE=ingame inspace=$INSPACE state=$STATE"
echo "INGAME inspace=$INSPACE state=$STATE"
[ "$INSPACE" = true ] || log "NOTE: character is docked (not in space). See start-in-space handling."
exit 0
