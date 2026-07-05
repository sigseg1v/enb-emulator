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
# This login is OCR-FREE: the client logs ITSELF in via the injected enbmod
# auto-login (EULA + credentials + character-enter, driven off game memory, no
# pixels or synthetic input -- see 03-launch-launcher.sh / autologin.cpp). The old
# screen-driven steps 05 (EULA click) / 06 (type credentials) / 07 (char-select
# click) are gone; the only "reading" left is the enbmod Lua channel in 08.
#
# Sequence (matches the per-step files):
#   00-kill          kill client/launcher/proxy + `just down`
#   01-start-stack   `just run-stack-bg` + verify server/net7go/proxy Up
#   02-seed          ensure dev account has a roster (idempotent)
#   03-launch        `just play-local <user> <pass> <char>` -- arms auto-login
#                    (FREYA_AUTOPLAY + in-client EULA/login/char-enter, no OCR)
#   08-wait-ingame   wait until enb.self() is a table (in-game), report inspace
#
# Modes:
#   (default)         full from-scratch run (00 -> 03 -> 08)
#   ENB_ATTACH=1      skip 00-03; just confirm the ALREADY-running client reached
#                     in-game (08). The running client must have been launched with
#                     auto-login armed (03 / `just play-local <user> <pass> <char>`)
#                     -- with no OCR there is no way to drive a client parked at the
#                     login screen, so attach only verifies, it cannot log in.
#   ENB_SKIP_SEED=1   skip 02 (account already seeded)
# Credentials: ENB_LOGIN_USER / ENB_LOGIN_PASS (default devuser / devpass);
# character: ENB_LOGIN_CHAR (default <user>te, the seeded slot-0 character).
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
    log "ENB_ATTACH=1: confirming the running (auto-login-armed) client reached in-game (08 only)"
fi

run 08-wait-ingame.sh

log "==== LOGIN COMPLETE ===="
