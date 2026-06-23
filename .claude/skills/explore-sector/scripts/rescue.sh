#!/usr/bin/env bash
# SPDX-License-Identifier: MIT
# Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
# License: LICENSES/Freya
#
# rescue.sh -- DETERMINISTIC state machine for the "ship destroyed -> get towed"
# recovery. Replaces the old "OCR the screen for the word Tow" heuristic, which
# produced a FALSE NEGATIVE on the real death UI: a wrecked ship never shows a
# "Request Tow" button. It shows
#   1. an amber "<<Distress" label on the HUD, immediately right of the speed
#      readout, on the warp-orb row -- ALWAYS present while wrecked, and it does
#      NOT depend on any other panel being open (map/comm can be open or closed).
#      THIS is the wreck trigger.
#   2. a Station-Mechanic comm dialog whose THREE reply options include
#      "I need a tow" (the others are a greeting line and "Toggle distress
#      beacon"). Clicking "I need a tow" tows the ship back to its station.
#
# CRITICAL flow (owner-taught 2026-06-22): the dialog in (2) does NOT exist until
# the distress BUTTON is clicked. That button is the orb immediately LEFT of the
# "<<Distress" label -- the same HUD slot the warp orb occupies in flight. So the
# recovery is a TWO-step click: OCR the "<<Distress" label, click the button to its
# left to OPEN the dialog, THEN click "I need a tow". Looking for "I need a tow"
# without first opening the dialog (the old bug) always fails -- the option is not
# on screen yet.
#
# States:
#   detect   -> print  WRECKED | ALIVE   (fuzzy "<<Distress" on the HUD; the one
#               signal that is independent of every other UI element)
#   tow-xy   -> print  "<x> <y>"  of the "I need a tow" reply option, or nothing.
#               Fuzzy phrase match so a one/two-char OCR slip still locates it.
#   rescue   -> FULL recovery loop (default): detect -> click the distress button
#               (left of <<Distress) to open the dialog -> click "I need a tow" ->
#               wait for the Distress label to clear -> report recovered. Bounded,
#               idempotent, re-entrant: re-running it on an ALIVE ship is a no-op.
#
# Exit (rescue): 0 = not wrecked OR successfully towed/recovered.
#                3 = wrecked but could not locate/confirm the tow option.
#                1 = hard error (no client window / no screenshot).
#
# Tunables (env): ENB_DISTRESS_BOX "x0 y0 x1 y1" (default "615 843 790 875"),
#   ENB_RESCUE_WAIT (max seconds to wait for recovery, default 90),
#   ENB_RESCUE_TRIES (tow-click attempts, default 4).
set -uo pipefail
SKILL_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
. "$SKILL_DIR/../../login-to-client/scripts/lib.sh"

rlog() { printf '[rescue] %s\n' "$*" >&2; }

shot_now() {
    local out="$1" w
    w="$(client_win)" || return 1
    shot_win "$w" "$out"
}

# --- STATE: detect ----------------------------------------------------------
# Fuzzy-match the amber "<<Distress" HUD label. Yellow/orange threshold the fixed
# label band, OCR it, and Levenshtein-compare the longest token to "distress".
# Independent of map/comm panels -- the label is on the warp-orb HUD row whenever
# the ship is wrecked. Prints WRECKED or ALIVE.
is_wrecked() {
    local shot="$1"
    read -r dx0 dy0 dx1 dy1 <<< "${ENB_DISTRESS_BOX:-615 843 790 875}"
    python3 - "$shot" "$dx0" "$dy0" "$dx1" "$dy1" <<'PY'
import sys, subprocess, tempfile, os, re
import numpy as np
from PIL import Image
shot = sys.argv[1]; x0,y0,x1,y1 = map(int, sys.argv[2:6])
a = np.asarray(Image.open(shot).convert("RGB").crop((x0,y0,x1,y1))).astype(int)
R,G,B = a[:,:,0],a[:,:,1],a[:,:,2]
# amber/yellow glyphs: red & green high, blue low.
mask = (R>150)&(G>110)&(B<130)&(R>B+40)&(G>B+25)
if int(mask.sum()) < 12:           # almost no amber pixels -> not the label
    print("ALIVE"); sys.exit(0)
px = np.full(a.shape[:2], 255, np.uint8); px[mask] = 0
im = Image.fromarray(px).resize((px.shape[1]*5, px.shape[0]*5))
tf = tempfile.NamedTemporaryFile(suffix=".png", delete=False); im.save(tf.name)
try:
    out = subprocess.run(["tesseract", tf.name, "stdout", "--psm", "7"],
                         capture_output=True, text=True).stdout
finally:
    os.unlink(tf.name)
def lev(s,t):
    if s==t: return 0
    if not s or not t: return len(s or t)
    prev=list(range(len(t)+1))
    for i,cs in enumerate(s,1):
        cur=[i]
        for j,ct in enumerate(t,1):
            cur.append(min(prev[j]+1,cur[-1]+1,prev[j-1]+(cs!=ct)))
        prev=cur
    return prev[-1]
toks = re.findall(r'[A-Za-z]+', out)
best = 1.0
for tk in toks:
    t = tk.lower()
    r = lev(t, "distress")/max(len(t), len("distress"))
    best = min(best, r)
print("WRECKED" if best <= 0.45 else "ALIVE")
PY
}

# --- locate the distress BUTTON (the orb to the LEFT of the <<Distress label) -
# The Station-Mechanic comm dialog (with "I need a tow") does NOT exist until the
# distress beacon button is clicked. That button sits immediately LEFT of the amber
# "<<Distress" label -- it is the same HUD slot the warp orb occupies in flight
# (the warp orb is replaced by the distress button on a wreck). So: find the amber
# label's LEFT EDGE, step left onto the orb, and that is the click point.
# Prints "<x> <y>" (window-rel) of the button, or nothing.
distress_xy() {
    local shot="$1"
    read -r dx0 dy0 dx1 dy1 <<< "${ENB_DISTRESS_BOX:-615 843 790 875}"
    local btn_dx="${ENB_DISTRESS_BTN_DX:-30}"
    python3 - "$shot" "$dx0" "$dy0" "$dx1" "$dy1" "$btn_dx" <<'PY'
import sys
import numpy as np
from PIL import Image
shot = sys.argv[1]; x0,y0,x1,y1 = map(int, sys.argv[2:6]); btn_dx = int(sys.argv[6])
a = np.asarray(Image.open(shot).convert("RGB").crop((x0,y0,x1,y1))).astype(int)
R,G,B = a[:,:,0],a[:,:,1],a[:,:,2]
mask = (R>150)&(G>110)&(B<130)&(R>B+40)&(G>B+25)
if int(mask.sum()) < 12:
    sys.exit(1)
cols = np.where(mask.any(axis=0))[0]
rows = np.where(mask.any(axis=1))[0]
left_x = x0 + int(cols.min())              # left edge of the "<<" arrows
cy     = y0 + int((rows.min()+rows.max())//2)
print(left_x - btn_dx, cy)                 # step left onto the orb/button
PY
}

# --- STATE: tow-xy ----------------------------------------------------------
# Fuzzy-find the "I need a tow" reply option and print its window-rel centre
# "<x> <y>". TOKEN-based, not line-based: the reply text is faint cyan and OCRs
# poorly -- the word "tow" is frequently DROPPED entirely while "need" reads at
# high confidence, and tesseract scatters the option's words across separate
# "line" groups, so the old "tow AND need on one line" test never matched. So we
# anchor on whichever of "tow"/"need" is read, and disqualify the OTHER two
# options by their reliable tokens (greeting has no tow/need; "Toggle distress
# beacon" is excluded by its own words). Robust fallback: if only the rock-solid
# "Toggle distress beacon" row is found, the tow option is the row directly ABOVE
# it (ENB_TOW_ROW_DY px, default 29) at the same left margin. No hardcoded Y.
tow_xy() {
    local shot="$1"
    ENB_TOW_ROW_DY="${ENB_TOW_ROW_DY:-29}" python3 - "$shot" <<'PY'
import sys, subprocess, re, os
from PIL import Image
im = Image.open(sys.argv[1]).convert("RGB")
g = im.convert("L").resize((im.width*2, im.height*2)); g.save("/tmp/_towocr.png")
out = subprocess.run(["tesseract","/tmp/_towocr.png","stdout","tsv"],
                     capture_output=True, text=True).stdout
def lev(s,t):
    if s==t: return 0
    if not s or not t: return len(s or t)
    prev=list(range(len(t)+1))
    for i,cs in enumerate(s,1):
        cur=[i]
        for j,ct in enumerate(t,1):
            cur.append(min(prev[j]+1,cur[-1]+1,prev[j-1]+(cs!=ct)))
        prev=cur
    return prev[-1]
def fuzzy(n, w, thr):
    return bool(n) and lev(n, w)/max(len(n), len(w)) <= thr
toks = []   # (cx_full, cy_full, left_full, norm)
for ln in out.splitlines()[1:]:
    f = ln.split("\t")
    if len(f) < 12: continue
    try: conf = float(f[10])
    except ValueError: continue
    n = re.sub(r'[^a-z]','', f[11].lower())
    if conf <= 0 or not n: continue
    l,t,w,h = (int(f[i]) for i in (6,7,8,9))
    toks.append((( l+w/2)/2, (t+h/2)/2, l/2, n))
# ANCHOR on the rock-solid "Toggle distress beacon" row first -- it OCRs at
# conf ~95 every time. Everything keys off it, so we never false-match a stray
# "too"/"need" in the chat log far from the dialog.
beacon = [(lx, cy) for cx, cy, lx, n in toks if fuzzy(n,"beacon",0.30) or fuzzy(n,"toggle",0.30)]
if not beacon:
    sys.exit(1)                       # dialog not open / no reply list -> nothing to click
blx = min(b[0] for b in beacon); bcy = sum(b[1] for b in beacon)/len(beacon)
# 1) prefer a real "tow"/"need" token that sits JUST ABOVE the beacon row and at a
#    similar left margin (the "I need a tow" option). The proximity gate is what
#    rejects the chat-log "too" 550px higher up.
best = None
for cx, cy, lx, n in toks:
    if n in ("beacon","toggle","distress"): continue
    if not (bcy-90 < cy < bcy-6): continue
    if abs(lx - blx) > 150: continue
    if fuzzy(n,"tow",0.34) or fuzzy(n,"need",0.40):
        if best is None or cy > best[1]:   # closest row above the beacon
            best = (cx, cy)
if best:
    print(f"{int(best[0])} {int(best[1])}"); sys.exit(0)
# 2) fallback: tow option is the row directly above the beacon row, same margin.
dy = int(os.environ.get("ENB_TOW_ROW_DY","29"))
print(f"{int(blx+18)} {int(bcy-dy)}"); sys.exit(0)
PY
}

# --- map overlay (occludes the lower-left reply options) --------------------
# The three reply options render at the lower-left comm area. An OPEN sector map
# overlays exactly that spot, so the "I need a tow" line is hidden while the map is
# up. The open-map button (132,870) is a TOGGLE, so we must NOT click it blindly
# (an even number of clicks is a no-op): detect the map state and close it ONCE.
# Map-open probe = the blue "center" toolbar button at (84,232), same test
# open-map.sh uses.
map_is_open() {
    local shot="$1"
    python3 - "$shot" <<'PY'
import sys, numpy as np
from PIL import Image
im = np.asarray(Image.open(sys.argv[1]).convert("RGB")).astype(int)
H, W, _ = im.shape
cx, cy = 84, 232
x0,y0,x1,y1 = max(0,cx-14),max(0,cy-14),min(W,cx+14),min(H,cy+14)
b = im[y0:y1, x0:x1]; R,G,B = b[:,:,0],b[:,:,1],b[:,:,2]
blue = (B>90)&(B-R>25)&(B-G>15)&(R>20)&(R<150)
print("open" if blue.sum() > 25 else "closed")
PY
}

close_map_if_open() {
    local w="$1"
    shot_now "$SHOT" || return 0
    if [ "$(map_is_open "$SHOT")" = open ]; then
        read -r mx my <<< "${ENB_MAP_TOGGLE:-132 870}"
        rlog "map is open -- closing it once to reveal the reply options"
        click_win "$w" "$mx" "$my"; sleep 1.2
    fi
}

# ---------------------------------------------------------------------------
CMD="${1:-rescue}"
SHOT="$WORKDIR/rescue.png"

case "$CMD" in
detect)
    shot_now "$SHOT" || { rlog "screenshot failed"; exit 1; }
    is_wrecked "$SHOT"
    ;;
tow-xy)
    shot_now "$SHOT" || { rlog "screenshot failed"; exit 1; }
    tow_xy "$SHOT" || { rlog "no 'I need a tow' option visible"; exit 1; }
    ;;
rescue)
    W="$(client_win)" || { rlog "no client window"; exit 1; }
    shot_now "$SHOT" || { rlog "screenshot failed"; exit 1; }
    # WRECKED == the amber "<<Distress" label is on the HUD. BUT once the comm
    # dialog is open the label is hidden behind it, so is_wrecked false-reports
    # ALIVE even though the ship is still destroyed and the tow dialog is sitting
    # right there. So treat "tow dialog already on screen" as wrecked too -- this
    # is what makes rescue re-entrant after a prior partial run left the dialog up.
    if [ "$(is_wrecked "$SHOT")" != WRECKED ]; then
        if tow_xy "$SHOT" >/dev/null 2>&1; then
            rlog "tow dialog already open (label hidden behind it) -- proceeding to tow"
        else
            rlog "ALIVE: not wrecked, nothing to do"
            echo "ALIVE"; exit 0
        fi
    fi
    rlog "WRECKED: starting tow recovery"
    bash "$SKILL_DIR/logaction.sh" "${ENB_RESCUE_SECTOR:-unknown}" wreck-detected \
        "<<Distress label detected; requesting tow" >/dev/null 2>&1 || true
    # The reply options live at the lower-left of the comm panel; an OPEN sector
    # map (3rd bottom-left HUD button, a toggle) overlays exactly that spot, so the
    # "I need a tow" line is occluded while the map is up. close_map_if_open detects
    # the map state and closes it EXACTLY once only when open -- never a blind toggle
    # (an even number of toggles is a no-op and leaves the map up). Run it up front so
    # the comm area is reachable; harmless no-op if the map is already closed.
    close_map_if_open "$W"
    tries="${ENB_RESCUE_TRIES:-4}"
    for t in $(seq 1 "$tries"); do
        shot_now "$SHOT" || true
        # STEP 1: the Station-Mechanic dialog (with "I need a tow") does NOT exist
        # until the distress BUTTON is clicked. That button is the orb immediately
        # LEFT of the amber "<<Distress" label (same HUD slot as the in-flight warp
        # orb). Locate the label, click the button to its left to OPEN the dialog,
        # then look for the tow option. Idempotent: if the dialog is already up,
        # tow_xy below already finds it; an extra button click just re-opens it.
        if BXY="$(distress_xy "$SHOT")" && [ -n "$BXY" ]; then
            read -r bx by <<< "$BXY"
            rlog "clicking distress button at ($bx,$by) to open dialog [try $t/$tries]"
            click_win "$W" "$bx" "$by"
            sleep 2
        else
            rlog "could not locate <<Distress label this frame [try $t/$tries]"
        fi
        # STEP 2: the dialog is up -- find and click "I need a tow".
        shot_now "$SHOT" || true
        if ! XY="$(tow_xy "$SHOT")" || [ -z "$XY" ]; then
            rlog "tow option not visible [try $t/$tries] -- ensuring map closed + re-shotting"
            close_map_if_open "$W"
            shot_now "$SHOT" || true
            XY="$(tow_xy "$SHOT" || true)"
        fi
        if [ -n "${XY:-}" ]; then
            read -r tx ty <<< "$XY"
            rlog "clicking 'I need a tow' at ($tx,$ty) [try $t/$tries]"
            click_win "$W" "$tx" "$ty"
        else
            rlog "tow option still not visible [try $t/$tries]"
            sleep 1.5; continue
        fi
        # Wait for genuine recovery. CAUTION: is_wrecked==ALIVE alone is NOT proof
        # of a tow -- an OPEN dialog also hides the amber label and reads ALIVE, so
        # if the tow click missed, a still-open dialog would fake "recovered". Real
        # recovery == no amber label AND the tow dialog is GONE (tow_xy fails). After
        # a successful tow we are docked in the station (dialog closed, no label).
        waited=0; max="${ENB_RESCUE_WAIT:-90}"
        while [ "$waited" -lt "$max" ]; do
            sleep 3; waited=$((waited+3))
            shot_now "$SHOT" || continue
            [ "$(is_wrecked "$SHOT")" = ALIVE ] || continue
            if tow_xy "$SHOT" >/dev/null 2>&1; then
                continue          # dialog still up -> tow not taken yet, keep waiting
            fi
            rlog "RECOVERED: dialog closed + Distress cleared after ${waited}s"
            bash "$SKILL_DIR/logaction.sh" "${ENB_RESCUE_SECTOR:-unknown}" \
                tow-done "towed to station; dialog closed" >/dev/null 2>&1 || true
            echo "RECOVERED"; exit 0
        done
        rlog "still wrecked after ${max}s wait -- retrying tow click"
    done
    rlog "FAILED: still wrecked after $tries tow attempts"
    echo "STILL-WRECKED"; exit 3
    ;;
*)
    rlog "usage: rescue.sh {detect|tow-xy|rescue}"; exit 2 ;;
esac
