#!/usr/bin/env bash
# SPDX-License-Identifier: MIT
# Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
# License: LICENSES/Freya
#
# 02-seed.sh -- ensure the dev account ($ENB_LOGIN_USER) exists with a roster.
# Runs `just seed-dev-account` ONLY if the account has no characters yet (the
# seed is slow -- it drives the CLI to create+enter 5 characters server-side).
# Force a re-seed with ENB_FORCE_SEED=1. Needs the stack up (run after 01).
SKILL_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
. "$SKILL_DIR/lib.sh"

char_count() {
    psql_user \
        "SELECT count(*) FROM avatar_info i JOIN accounts a ON a.id=i.account_id WHERE a.username=:'u';" \
        -v u="$LOGIN_USER" | tr -d '[:space:]'
}

# Start the slot-0 character IN SPACE rather than docked. The server reads
# avatar_info.sector at login (AccountManager.cpp:694) and docks the player when
# it is > 9999 (PlayerManager.cpp:209). A station sector is space_sector*10+index
# (ServerManager.cpp divides >9999 by 10), so dividing by 10 yields the matching
# space sector (e.g. 10251 -> 1025, that char's start space sector). We mirror it
# into avatar_position.sector_id and zero the position so the avatar spawns at a
# sane in-space origin. Idempotent: only rows with sector > 9999 are touched, so
# re-running (or an already-in-space char) is a no-op. MUST run while no session
# is alive (00-kill ran first) or a logout/autosave would rewrite sector back to
# the docked value. ENB_START_IN_SPACE=0 disables (leaves the char docked).
ensure_slot0_in_space() {
    [ "${ENB_START_IN_SPACE:-1}" = 1 ] || { log "start-in-space disabled (ENB_START_IN_SPACE=0)"; return 0; }
    local aid
    aid="$(psql_user \
        "SELECT i.avatar_id FROM avatar_info i JOIN accounts a ON a.id=i.account_id WHERE a.username=:'u' ORDER BY i.slot LIMIT 1;" \
        -v u="$LOGIN_USER" | tr -d '[:space:]')"
    [ -n "$aid" ] || { err "WARN=in-space: no slot-0 avatar for '$LOGIN_USER' -- skipping"; return 0; }
    local before after
    before="$(psql_user "SELECT sector FROM avatar_info WHERE avatar_id=:aid;" -v aid="$aid" | tr -d '[:space:]')"
    if [ -n "$before" ] && [ "$before" -le 9999 ] 2>/dev/null; then
        log "slot-0 (avatar $aid) already in space (sector $before)"
        return 0
    fi
    psql_user "UPDATE avatar_info SET sector = sector/10 WHERE avatar_id=:aid AND sector > 9999;" -v aid="$aid" >/dev/null
    psql_user "UPDATE avatar_position SET sector_id = sector_id/10, posx=0, posy=0, posz=0 WHERE avatar_id=:aid AND sector_id > 9999;" -v aid="$aid" >/dev/null
    after="$(psql_user "SELECT sector FROM avatar_info WHERE avatar_id=:aid;" -v aid="$aid" | tr -d '[:space:]')"
    log "slot-0 (avatar $aid) sector $before -> $after (start in space)"
}

n="$(char_count)"
log "account '$LOGIN_USER' currently has ${n:-?} character(s)"

if [ "${ENB_FORCE_SEED:-0}" != 1 ] && [ -n "$n" ] && [ "$n" -gt 0 ] 2>/dev/null; then
    log "STATE=seeded (skip -- account already has $n chars; ENB_FORCE_SEED=1 to re-seed)"
    ensure_slot0_in_space
    exit 0
fi

# NOTE: do NOT force ENB_NOREBUILD here -- the seed drives the C# CLI to create
# characters, and a STALE CLI image has old/wrong class codes (it once rejected
# the valid 'JD' Defender code with a "try TW, PT" message from a build that
# predated the current codes). Let the seed build-if-stale so it uses current
# class codes.
log "seeding account (just seed-dev-account $LOGIN_USER) -- this drives the CLI, ~minutes"
seed_rc=0
( cd "$REPO_ROOT" && just seed-dev-account "$LOGIN_USER" "$LOGIN_PASS" ) \
    >"$WORKDIR/seed.log" 2>&1 || seed_rc=$?
n="$(char_count)"
if [ "$seed_rc" -ne 0 ]; then
    # A partial seed still leaves usable characters (create+enter runs per-char);
    # the login skill only needs >=1 flyable char. Accept that; only fail if the
    # account has NO characters at all.
    if [ -n "$n" ] && [ "$n" -gt 0 ] 2>/dev/null; then
        err "WARN=seed: seed-dev-account returned $seed_rc but account has $n char(s) -- continuing"
        tail -15 "$WORKDIR/seed.log" >&2
    else
        err "STUCK=seed: seed-dev-account failed and account has no characters"
        tail -30 "$WORKDIR/seed.log" >&2
        exit 1
    fi
fi
ensure_slot0_in_space
log "STATE=seeded ($n chars)"
