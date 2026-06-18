#!/usr/bin/env bash
# SPDX-License-Identifier: MIT
# Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
# License: LICENSES/Freya
#
# login.sh -- orchestrate a full, reliable, repeatable login to the EnB client,
# from a clean slate all the way to in-game. Each step is a numbered building
# block that confirms its own success (window size / ref-crop match / Lua channel)
# before the next runs; any block that gets stuck prints "STUCK=<step> ..." and
# exits non-zero, so a failure tells you exactly where it stopped.
#
# Sequence (matches the per-step files):
#   00-kill          kill client/launcher/proxy + `just down`
#   01-start-stack   `just run-stack-bg` + verify server/net7go/proxy Up
#   02-seed          ensure dev account has a roster (idempotent)
#   03-launch        `just play-local` with FREYA_AUTOPLAY=1 (no mouse Play click)
#   05-eula          accept the Rules-of-Conduct dialog
#   06-login         skip intro, type credentials, Accept -> character select
#   07-charselect    pick first character, Enter -> load screen
#   08-wait-ingame   wait until enb.self() is a table (in-game), report inspace
#
# Modes:
#   (default)         full from-scratch run (00..08)
#   ENB_ATTACH=1      skip 00-03; drive the ALREADY-running client to in-game
#                     (05..08) -- use when a client is up and you don't want to
#                     bounce the stack/session.
#   ENB_SKIP_SEED=1   skip 02 (account already seeded)
# Credentials: ENB_LOGIN_USER / ENB_LOGIN_PASS (default devuser / devpass).
set -uo pipefail
SKILL_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
. "$SKILL_DIR/lib.sh"

run() {
    local step="$1"; shift
    log "==== $step ===="
    if ! bash "$SKILL_DIR/$step"; then
        err "ABORT: $step failed (see STUCK= above)"
        exit 1
    fi
}

if [ "${ENB_ATTACH:-0}" != 1 ]; then
    run 00-kill.sh
    run 01-start-stack.sh
    [ "${ENB_SKIP_SEED:-0}" = 1 ] || run 02-seed.sh
    run 03-launch-launcher.sh
else
    log "ENB_ATTACH=1: using the running client (skipping 00-03)"
fi

run 05-eula-accept.sh
run 06-login.sh
run 07-charselect-enter.sh
run 08-wait-ingame.sh

log "==== LOGIN COMPLETE ===="
