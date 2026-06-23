#!/usr/bin/env bash
# SPDX-License-Identifier: MIT
# Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
# License: LICENSES/Freya
#
# hangcheck.sh -- detect a HARD HANG of the live client: client.exe is still alive
# but wedged in an internal busy-loop, rendering a FROZEN frame and ignoring input.
# Prints exactly one of: HUNG | ALIVE | NOWINDOW.
#
# The discriminator is the FROZEN FRAMEBUFFER, not CPU. A healthy in-space client
# always animates the starfield/nebula, so two full-window screenshots ~1s apart
# differ. A hung client renders byte-identical frames. CPU is deliberately NOT the
# signal: the normal in-space render also pegs ~980-1100% under WINE (verified
# 2026-06-21), so high CPU alone would false-trigger. We sample the frame twice;
# only if BOTH pairs are identical (3 shots, ~1s apart) do we call it HUNG, to
# avoid a one-off capture coincidence on a momentarily static scene.
set -uo pipefail
SKILL_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"; . "$SKILL_DIR/lib.sh"

win="$(client_win)" || { echo "NOWINDOW"; exit 0; }
read -r gx gy gw gh <<< "$(win_abs "$win")" || { echo "ALIVE"; exit 0; }

shot() {
    pkill -9 import 2>/dev/null
    timeout -k 2 8 import -window root -crop "${gw}x${gh}+${gx}+${gy}" +repage png:- \
        2>/dev/null | md5sum | cut -d' ' -f1
}

a="$(shot)"; sleep 1; b="$(shot)"; sleep 1; c="$(shot)"
# a capture failure (empty md5 of nothing) must NOT look like a frozen match
if [ -z "$a" ] || [ -z "$b" ] || [ -z "$c" ]; then echo "ALIVE"; exit 0; fi
if [ "$a" = "$b" ] && [ "$b" = "$c" ]; then echo "HUNG"; else echo "ALIVE"; fi
