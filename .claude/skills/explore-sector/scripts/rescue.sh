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
# States:
#   detect   -> print  WRECKED | ALIVE   (fuzzy "<<Distress" on the HUD; the one
#               signal that is independent of every other UI element)
#   tow-xy   -> print  "<x> <y>"  of the "I need a tow" reply option, or nothing.
#               Fuzzy phrase match so a one/two-char OCR slip still locates it.
#   rescue   -> FULL recovery loop (default): detect -> click "I need a tow" ->
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

# --- STATE: tow-xy ----------------------------------------------------------
# Fuzzy-find the "I need a tow" reply option anywhere on screen and print its
# window-rel centre "<x> <y>". The three options are short lines; we anchor on the
# distinctive word "tow" and require a nearby "need" on the same OCR line so we do
# NOT confuse it with "Toggle distress beacon". No hardcoded coord (the dialog can
# render at different Y depending on whether the map is open).
tow_xy() {
    local shot="$1"
    python3 - "$shot" <<'PY'
import sys, subprocess, re
from PIL import Image
im = Image.open(sys.argv[1]).convert("RGB")
g = im.convert("L").resize((im.width*2, im.height*2)); g.save("/tmp/_towocr.png")
out = subprocess.run(["tesseract","/tmp/_towocr.png","stdout","tsv"],
                     capture_output=True, text=True).stdout
# group words by (block,par,line); find the line containing a fuzzy "tow"
lines = {}
for ln in out.splitlines()[1:]:
    f = ln.split("\t")
    if len(f) < 12: continue
    try: conf = float(f[10])
    except ValueError: continue
    word = f[11].strip()
    if conf <= 0 or not re.sub(r'[^A-Za-z]','',word): continue
    key = (f[2], f[4], f[5])     # block, par, line
    l,t,w,h = (int(f[i]) for i in (6,7,8,9))
    lines.setdefault(key, []).append((l,t,w,h,word.lower()))
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
for key, words in lines.items():
    norm = [re.sub(r'[^a-z]','',w) for *_,w in words]
    has_tow  = any(n and lev(n,"tow")/max(len(n),3) <= 0.34 for n in norm)
    has_need = any(n and lev(n,"need")/max(len(n),4) <= 0.34 for n in norm)
    # accept "I need a tow"; reject "Toggle distress beacon"
    if has_tow and has_need and not any("beacon" in n or "distress" in n for n in norm):
        xs = [l + w/2 for l,t,w,h,_ in words]
        ys = [t + h/2 for l,t,w,h,_ in words]
        cx = sum(xs)/len(xs)/2; cy = sum(ys)/len(ys)/2
        print(f"{int(cx)} {int(cy)}"); sys.exit(0)
sys.exit(1)
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
    if [ "$(is_wrecked "$SHOT")" != WRECKED ]; then
        rlog "ALIVE: not wrecked, nothing to do"
        echo "ALIVE"; exit 0
    fi
    rlog "WRECKED: <<Distress detected -- starting tow recovery"
    bash "$SKILL_DIR/logaction.sh" "${ENB_RESCUE_SECTOR:-unknown}" wreck-detected \
        "<<Distress label detected; requesting tow" >/dev/null 2>&1 || true
    # The reply options live at the lower-left of the comm panel; an OPEN sector
    # map (3rd bottom-left HUD button, a toggle) overlays exactly that spot, so the
    # "I need a tow" line is occluded while the map is up. close_map_if_open detects
    # the map state and closes it EXACTLY once only when open -- never a blind toggle
    # (an even number of toggles is a no-op and leaves the map up). Run it up front so
    # the option is reachable; harmless no-op if the map is already closed.
    close_map_if_open "$W"
    tries="${ENB_RESCUE_TRIES:-4}"
    for t in $(seq 1 "$tries"); do
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
        # wait for the Distress label to clear (recovered / towed out)
        waited=0; max="${ENB_RESCUE_WAIT:-90}"
        while [ "$waited" -lt "$max" ]; do
            sleep 3; waited=$((waited+3))
            shot_now "$SHOT" || continue
            if [ "$(is_wrecked "$SHOT")" = ALIVE ]; then
                rlog "RECOVERED: Distress cleared after ${waited}s"
                bash "$SKILL_DIR/logaction.sh" "${ENB_RESCUE_SECTOR:-unknown}" \
                    tow-done "Distress cleared; towed to station" >/dev/null 2>&1 || true
                echo "RECOVERED"; exit 0
            fi
        done
        rlog "still wrecked after ${max}s wait -- retrying tow click"
    done
    rlog "FAILED: still wrecked after $tries tow attempts"
    echo "STILL-WRECKED"; exit 3
    ;;
*)
    rlog "usage: rescue.sh {detect|tow-xy|rescue}"; exit 2 ;;
esac
