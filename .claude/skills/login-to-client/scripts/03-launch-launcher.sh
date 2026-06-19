#!/usr/bin/env bash
# SPDX-License-Identifier: MIT
# Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
# License: LICENSES/Freya
#
# 03-launch-launcher.sh -- start the FreyaLauncher GUI with autoplay and wait for
# the client.exe window to appear. We DO NOT click Play with the mouse: the
# launcher is our own Avalonia app, so it auto-clicks Play once the server is
# ONLINE when FREYA_AUTOPLAY=1 is set (see MainWindow.MaybeAutoPlay). This is far
# more reliable than synthetic clicks -- the desktop may have another app (a VM
# or a fullscreen game) holding a pointer grab that recenters the cursor and
# defeats XTEST clicks on the launcher.
#
# The stack is already up (01-start-stack) so we run with ENB_NOREBUILD=1 to skip
# the docker rebuild/bounce; the inject/mod DLL builds + settings merge still run.
# The launcher must OUTLIVE this script (it owns the proxy + auth relay), so we
# fully detach it with setsid -- a plain `&`/disown still dies when the calling
# shell's process group is reaped.
SKILL_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
. "$SKILL_DIR/lib.sh"

# Already in-game / client already up? (idempotent / repeatable)
if client_win >/dev/null 2>&1; then
    log "STATE=client-up (already running: $(client_win))"
    exit 0
fi

LAUNCH_LOG="$WORKDIR/play-local.log"
log "launching FreyaLauncher (FREYA_AUTOPLAY=1, ENB_NOREBUILD=1 just play-local) -> $LAUNCH_LOG"
setsid bash -c "cd '$REPO_ROOT' && FREYA_AUTOPLAY=1 ENB_NOREBUILD=1 just play-local" \
    >"$LAUNCH_LOG" 2>&1 < /dev/null &
echo $! > "$WORKDIR/play-local.pid"
disown 2>/dev/null || true

# The launcher first builds the DLLs + merges settings, then opens, then waits up
# to ~30s for ONLINE before auto-clicking Play, then WINE spawns client.exe. Give
# the whole chain a generous window. We wait for the CLIENT window (the real goal)
# -- the launcher window appearing first is just a progress checkpoint we log.
log "waiting for client.exe window (build + autoplay + WINE spawn) ..."
launcher_seen=0
end=$((SECONDS + 240))
while [ "$SECONDS" -lt "$end" ]; do
    if [ "$launcher_seen" -eq 0 ] && launcher_win >/dev/null 2>&1; then
        launcher_seen=1
        log "  checkpoint: launcher window up ($(launcher_win))"
    fi
    if client_win >/dev/null 2>&1; then
        CW="$(client_win)"
        log "STATE=client-up (window $CW, geom $(win_abs "$CW"))"
        exit 0
    fi
    sleep 2
done

err "STUCK=launch: no client.exe window appeared in 240s"
err "  launcher window seen: $launcher_seen"
grep -iE 'AUTOPLAY|status=|Launch|patch|error|fail' "$LAUNCH_LOG" | tail -15 >&2
exit 1
