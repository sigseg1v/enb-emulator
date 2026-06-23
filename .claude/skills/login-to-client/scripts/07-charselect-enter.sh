#!/usr/bin/env bash
# SPDX-License-Identifier: MIT
# Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
# License: LICENSES/Freya
#
# 07-charselect-enter.sh -- on the character-select screen, pick a character and
# click Enter. Two modes (owner, 2026-06-22):
#  - ENB_EXPLORE_CHARACTER_NAME set (explore-sector survey): OCR the character
#    name list in the TOP-LEFT quadrant, click the matching name, wait 5s, OCR the
#    ENTER button in the BOTTOM-RIGHT quadrant and click it. Click NOTHING else,
#    and if EITHER the name or the ENTER button is not found by OCR, BAIL -- we do
#    not blind-click a fixed coord in this mode. There is no default.
#  - unset (normal login.sh): click the first character's fixed nameplate + Enter.
# Confirms success by the screen LEAVING character-select (the load screen begins).
# The actual "in space / in station, fully loaded" confirmation is 08-wait-ingame.sh.
SKILL_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
. "$SKILL_DIR/lib.sh"

# "Left char-select" must be STABLE: on_charselect flickers OFF for a single
# frame during the rotating 3D model / selection-highlight animation, and a
# single off-read previously made the Enter step report "loading" without ever
# entering (then 08 failed on a client still sitting at char-select). Require TWO
# consecutive off-reads before believing we left.
_left_charselect() {
    on_charselect && return 1
    sleep 0.5
    on_charselect && return 1
    return 0
}

W="$(client_win)" || { err "STUCK=charselect: no client.exe window"; exit 1; }

if ! on_charselect; then
    # Maybe we are already loading / in game.
    err "WARN=charselect: not on character-select screen (already entered? check 08)"
    exit 0
fi

# No default -- set == explore OCR mode, empty == legacy fixed-nameplate mode.
CHAR_NAME="${ENB_EXPLORE_CHARACTER_NAME:-}"

# OCR a single token matching $2 inside the window-pixel region "$3 $4 $5 $6"
# (x0 y0 x1 y1) of screenshot $1; optional $7 selects a threshold MODE:
#   ""/0 -> no threshold (scale 2, psm 6): raw text on a clean background.
#   1    -> bright cyan/white threshold (scale 3, psm 7, single line) so a
#           stylised HUD label over the animated starfield (the "ENTER>>"
#           button) is legible to tesseract.
#   2    -> bright NEUTRAL-grey threshold (scale 3, psm 6, multi-row block) for
#           the character-name list: the names are a light-grey font on the dark
#           hex backdrop and raw OCR (mode 0) does not detect them at all -- it
#           found ZERO tokens and BAILED, so the survey could never pick a
#           character by name. Thresholding to black-on-white reads them (the
#           fuzzy Levenshtein match then absorbs the small-font OCR slips).
#           Echoes "X Y"
# (the token's centre, in window coords) and exits 0 on a hit; exits 1 (no output)
# on a miss so the caller can BAIL rather than blind-click. tesseract TSV gives
# per-word boxes; the match is case/punctuation-insensitive and FUZZY (normalised
# Levenshtein) so a one- or two-char OCR slip in the small HUD font (e.g.
# "Examplename" -> "Examp1ename") still maps to the right row -- and only that row:
# the best-scoring token must beat ENB_OCR_MAXRATIO (default 0.34 = ~1 edit per
# 3 chars) or it is a MISS.
ocr_find() {
    python3 - "$@" <<'PY'
import sys, subprocess, tempfile, os, re
from PIL import Image
shot, target = sys.argv[1], sys.argv[2]
x0, y0, x1, y1 = map(int, sys.argv[3:7])
mode = sys.argv[7] if len(sys.argv) > 7 else "0"
thresh = mode in ("1", "2")
maxratio = float(os.environ.get("ENB_OCR_MAXRATIO", "0.34"))
im = Image.open(shot).convert("RGB").crop((x0, y0, x1, y1))
scale = 3 if thresh else 2
im = im.resize((im.width * scale, im.height * scale))
psm = "7" if mode == "1" else "6"
if thresh:
    import numpy as np
    a = np.asarray(im).astype(int)
    R, G, B = a[:, :, 0], a[:, :, 1], a[:, :, 2]
    if mode == "1":
        # bright cyan/white -- the ENTER button label.
        mask = ((G > 120) & (B > 120) & (R < 200)) | ((R > 180) & (G > 180) & (B > 180))
    else:
        # bright neutral grey -- the character-name list font.
        mask = (R > 120) & (G > 120) & (B > 120)
    px = np.full(a.shape[:2], 255, np.uint8); px[mask] = 0   # black text on white
    im = Image.fromarray(px)
tf = tempfile.NamedTemporaryFile(suffix=".png", delete=False); im.save(tf.name)
try:
    out = subprocess.run(["tesseract", tf.name, "stdout", "--psm", psm, "tsv"],
                         capture_output=True, text=True).stdout
finally:
    os.unlink(tf.name)

def lev(a, b):
    if a == b: return 0
    if not a: return len(b)
    if not b: return len(a)
    prev = list(range(len(b) + 1))
    for i, ca in enumerate(a, 1):
        cur = [i]
        for j, cb in enumerate(b, 1):
            cur.append(min(prev[j] + 1, cur[-1] + 1, prev[j - 1] + (ca != cb)))
        prev = cur
    return prev[-1]

norm = lambda s: re.sub(r'[^a-z0-9]', '', s.lower())
tn = norm(target)
best = None  # (ratio, -conf, cx, cy, raw)
for ln in out.splitlines()[1:]:
    f = ln.split("\t")
    if len(f) < 12:
        continue
    try:
        conf = float(f[10])
    except ValueError:
        continue
    t = norm(f[11])
    if not t or conf <= 0:
        continue
    ratio = lev(t, tn) / max(len(tn), len(t))
    if ratio > maxratio:
        continue
    l, tp, w, h = (int(f[i]) for i in (6, 7, 8, 9))
    cx, cy = x0 + (l + w / 2) / scale, y0 + (tp + h / 2) / scale
    cand = (ratio, -conf, cx, cy, f[11])
    if best is None or cand < best:
        best = cand
if not best:
    sys.exit(1)
sys.stderr.write(f"[ocr] matched {best[4]!r} ratio={best[0]:.2f} at ({int(best[2])},{int(best[3])})\n")
print(f"{int(best[2])} {int(best[3])}")
PY
}

if [ -n "$CHAR_NAME" ]; then
    log "explore mode: OCR-locating character '$CHAR_NAME' in the top-left quadrant"
    SHOT="$WORKDIR/charselect.png"
    shot "$SHOT" || { err "STUCK=charselect: screenshot failed"; exit 1; }
    if ! CXY="$(ocr_find "$SHOT" "$CHAR_NAME" 0 0 640 480 2)" || [ -z "$CXY" ]; then
        err "STUCK=charselect: character '$CHAR_NAME' not found by OCR in the top-left quadrant -- BAIL (not blind-clicking)"
        exit 1
    fi
    read -r CX CY <<< "$CXY"
    log "clicking character '$CHAR_NAME' at ($CX,$CY)"
    click_win "$W" "$CX" "$CY"
    sleep 5
else
    log "selecting first character (fixed nameplate)"
    click_coord "$W" charselect first
    # The ENTER button enables a beat after the character is selected; the old
    # 0.6s settle clicked it while still disabled (the click was dropped and we
    # never left char-select). Give it time to enable -- the click-retry loop
    # below is the backstop if it is still late.
    sleep 3
fi

# Per-sector packet capture (explore-sector survey): start the FIRST sector's
# capture BEFORE pressing Enter so it includes the entry handshake. Opt-in via
# ENB_PCAP=1; drive.py relabels this placeholder to the real sector name once the
# sector is identified. Best-effort, cross-skill -- never blocks login.
if [ "${ENB_PCAP:-0}" = 1 ]; then
    PCAP="$SKILL_DIR/../../explore-sector/scripts/pcap.sh"
    [ -x "$PCAP" ] && bash "$PCAP" start "${ENB_PCAP_SECTOR:-entry}" || true
fi

# Resolve the ENTER button's click target ONCE (mode-specific), then click +
# confirm-leave in a retry loop below (shared by both modes).
if [ -n "$CHAR_NAME" ]; then
    # The ENTER button enables a beat AFTER the character is selected (owner:
    # "the button doesn't become clickable right away"). We already slept 5s; now
    # OCR a TIGHT, colour-thresholded box around the button (the full quadrant is
    # too noisy for the stylised label) and RETRY a few times in case it lights up
    # late. Only after every retry misses do we BAIL -- never blind-click.
    SHOT2="$WORKDIR/charselect-enter.png"
    read -r EBX0 EBY0 EBX1 EBY1 <<< "${ENB_ENTER_BOX:-960 865 1180 920}"
    EXY=""
    for try in 1 2 3 4 5 6; do
        shot "$SHOT2" || { err "STUCK=charselect: screenshot failed"; exit 1; }
        if EXY="$(ocr_find "$SHOT2" ENTER "$EBX0" "$EBY0" "$EBX1" "$EBY1" 1)" && [ -n "$EXY" ]; then
            break
        fi
        EXY=""
        log "ENTER button not legible yet (try $try/6); waiting for it to enable ..."
        sleep 1.5
    done
    if [ -z "$EXY" ]; then
        err "STUCK=charselect: ENTER button not found by OCR after retries -- BAIL (not blind-clicking)"
        exit 1
    fi
    read -r EX EY <<< "$EXY"
else
    read -r EX EY <<< "$(coord charselect enter)"
    [ -n "$EX" ] || { err "STUCK=charselect: no charselect.enter coord"; exit 1; }
fi

# Click ENTER and CONFIRM we leave -- with RE-CLICKS. A single click right after
# selecting the character is silently DROPPED if the ENTER button has not enabled
# yet (the "clicked too fast" bug): nothing happens and we never leave, but the
# old single-shot check + flickery leave-detection reported success anyway. So
# click, wait for char-select to be GONE (stably, via _left_charselect), and if
# it is still up, RE-CLICK -- up to a few attempts. Both modes share this path.
log "clicking ENTER at ($EX,$EY) and confirming we leave character-select ..."
for attempt in 1 2 3 4 5 6; do
    click_win "$W" "$EX" "$EY"
    if wait_until 8 2 _left_charselect; then
        log "STATE=loading (left character-select, attempt $attempt)"
        exit 0
    fi
    log "still on character-select after Enter (attempt $attempt/6) -- re-clicking ..."
done
err "STUCK=charselect: still on character-select after 6 Enter clicks"
exit 1
