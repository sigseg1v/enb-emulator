#!/usr/bin/env bash
# SPDX-License-Identifier: MIT
# Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
# License: LICENSES/Freya
#
# 01-start-stack.sh -- bring the docker stack up with a RETURNING command
# (`just run-stack-bg`), then verify the core services report Up. We do NOT
# launch the (blocking) launcher here -- that is 03-launch-launcher.sh, run
# AFTER any seeding, so the seed (which needs the stack) happens before the GUI
# blocks. Sequence overall: 00-kill -> 01-start-stack -> 02-seed -> 03-launch.
SKILL_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
. "$SKILL_DIR/lib.sh"

log "bringing up stack: just run-stack-bg (project $COMPOSE_PROJECT_NAME)"
if ! ( cd "$REPO_ROOT" && just run-stack-bg ) >"$WORKDIR/run-stack.log" 2>&1; then
    err "STUCK=start-stack: just run-stack-bg failed"
    tail -30 "$WORKDIR/run-stack.log" >&2
    exit 1
fi

core_up() {
    local ps; ps="$(cd "$REPO_ROOT" && docker compose ps --format '{{.Service}}={{.Status}}' 2>/dev/null)"
    echo "$ps" | grep -q '^server=Up'  && \
    echo "$ps" | grep -q '^net7go=Up'  && \
    echo "$ps" | grep -q '^proxy=Up'
}
log "verifying core services (server, net7go, proxy) Up ..."
if ! wait_until 180 3 core_up; then
    err "STUCK=start-stack: core services not all Up in 180s"
    ( cd "$REPO_ROOT" && docker compose ps ) >&2
    exit 1
fi
log "STATE=stack-up"
( cd "$REPO_ROOT" && docker compose ps --format '{{.Service}}={{.Status}}' ) >&2
