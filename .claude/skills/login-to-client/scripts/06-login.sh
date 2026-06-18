#!/usr/bin/env bash
# SPDX-License-Identifier: MIT
# Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
# License: LICENSES/Freya
#
# 06-login.sh -- skip the intro cinematic, enter credentials, click Accept, and
# wait for the character-select screen.
#
# After "I Agree" the client plays an intro movie before the login dialog. A
# click anywhere skips it; we click the centre and poll the login-screen ref
# (refs/login_field.png) until the field box is up. Then we clear+type the user
# and pass fields and click Accept. We do NOT trust a blind sleep: every step is
# confirmed by a ref-crop match (on_login / on_charselect).
#
# Credentials come from $ENB_LOGIN_USER / $ENB_LOGIN_PASS (default devuser/devpass).
SKILL_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
. "$SKILL_DIR/lib.sh"

W="$(client_win)" || { err "STUCK=login: no client.exe window"; exit 1; }
read -r _ _ ww _ <<< "$(win_abs "$W")"
[ "$ww" -ge 1000 ] || { err "STUCK=login: window ${ww}px wide, expected 1280 (EULA not accepted?)"; exit 1; }

# Already on character-select? then login is done.
if on_charselect; then log "STATE=charselect (login already done)"; exit 0; fi

# --- skip the intro movie until the login dialog is up -----------------------
log "skipping intro, waiting for login dialog ..."
end=$((SECONDS + 90))
while [ "$SECONDS" -lt "$end" ]; do
    on_login && break
    click_coord "$W" intro skip
    sleep 2
done
if ! on_login; then
    err "STUCK=login: login dialog never appeared (intro skip failed)"
    exit 1
fi
log "login dialog up"

# --- username ----------------------------------------------------------------
log "entering username '$LOGIN_USER'"
click_coord "$W" login user
xdotool key End; sleep 0.1
for i in $(seq 1 24); do xdotool key BackSpace; done; sleep 0.2
type_text "$LOGIN_USER"; sleep 0.3

# --- password ----------------------------------------------------------------
log "entering password"
click_coord "$W" login pass
xdotool key End; sleep 0.1
for i in $(seq 1 24); do xdotool key BackSpace; done; sleep 0.2
type_text "$LOGIN_PASS"; sleep 0.3

# --- accept ------------------------------------------------------------------
log "clicking Accept"
click_coord "$W" login accept

# --- wait for character select ----------------------------------------------
log "waiting for character-select screen ..."
if wait_until 40 2 on_charselect; then
    log "STATE=charselect"
    exit 0
fi
err "STUCK=login: character-select did not appear (bad credentials, or auth/login down?)"
shot_win "$W" "$WORKDIR/login_fail.png" && err "  see $WORKDIR/login_fail.png"
exit 1
