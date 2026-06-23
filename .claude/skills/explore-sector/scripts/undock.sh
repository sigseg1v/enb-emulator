#!/usr/bin/env bash
# SPDX-License-Identifier: MIT
# Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
# License: LICENSES/Freya
#
# undock.sh -- launch the ship back into space when it is DOCKED at a station.
#
# Owner-taught method (2026-06-21), validated live:
#
#  * DOCKED DETECTION: the bottom-centre warp cluster (warp dial + speed digits +
#    the green/red/blue bars + the 1..6 action circles) is HIDDEN while docked and
#    only present in space. The cheap, reliable tell is to OCR the warp-speed
#    region: blank (no digits, not even "000") == docked; any chars == in space.
#
#  * STATION LAYOUT: you zone into a T-shaped hangar at one of 3 random catwalk
#    positions/orientations. The station EXIT DOOR is at the top-middle of the T
#    (DO NOT GO THERE). Your SHIP is always parked at the bottom-right of the T.
#    To leave you face the ship and LEFT-CLICK its hull.
#
#  * FINDING THE SHIP: avatar turns with the Left/Right arrow keys (keyboard, sent
#    to the activated client window -- this is the one place we use keys, the warp
#    orb etc. are still clicks). We ROTATE RIGHT in fixed batches until the ship
#    comes into view on the RIGHT HALF of the screen, CLEAR of the chat overlay in
#    the upper-left. The ship is unmistakable: a hull flanked by a GREEN and a RED
#    nav light with a bright cyan/white engine plume below it. If we rotate past it
#    (it slides off to the left), we nudge LEFT to bring it back; fine steps of 5.
#
#  * LAUNCH: click the hull centre (between the nav lights, above the plume) -- NOT
#    the engine glow, NOT the chat box. Then re-check the warp-speed OCR; digits
#    back == we are in space == undocked.
#
# Prints "UNDOCKED" on success, "IN-SPACE" if already in space, "FAILED" otherwise.
set -uo pipefail
SKILL_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"; . "$SKILL_DIR/lib.sh"

WID="$(client_win)" || { echo "FAILED"; elog "no client window"; exit 1; }

# --- docked? : read the warp-speed digit with read-speed.sh ------------------
# CRITICAL: the warp-speed readout is a green 7-segment LCD font that plain
# tesseract CANNOT read (mis-segments the rings -> empty/garbage). read-speed.sh
# isolates the green pixels and classifies the lit segments deterministically.
# In space the readout is present ("SPEED 0" parked, "SPEED 240" warping); while
# docked the cluster is hidden so no green digits exist ("SPEED ?"). So:
#   "SPEED <number>"  == in space (undocked, DONE)
#   "SPEED ?"         == docked / panel obscured
in_space() {
    local out; out="$(bash "$SKILL_DIR/read-speed.sh" 2>/dev/null | tr -d '\r')"
    case "$out" in
        "SPEED ?"|"") return 1 ;;
        SPEED\ *)     return 0 ;;
        *)            return 1 ;;
    esac
}

if in_space; then echo "IN-SPACE"; elog "already in space"; exit 0; fi

# --- "Exit Starbase" button (the post-TOW concourse launch path) --------------
# A Request-Tow does NOT drop us on the hangar catwalk -- it drops us in the
# station CONCOURSE (avatar standing, ship parked behind), whose HUD carries an
# "Exit Starbase" button (blue up-arrow + label, top-centre). That button, NOT a
# hull click, launches from here -- so the rotate-and-click-hull sweep below can
# never undock a towed ship (it stranded the automated wreck recovery). Fuzzy-OCR
# the label and click it. Prints "x y" window-rel if found, nothing otherwise.
exit_starbase_xy() {
    python3 - "$1" <<'PY'
import sys, subprocess, re
from PIL import Image
im = Image.open(sys.argv[1]).convert("RGB")
g = im.convert("L").resize((im.width*2, im.height*2)); g.save("/tmp/_exitocr.png")
out = subprocess.run(["tesseract","/tmp/_exitocr.png","stdout","tsv"],
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
lines={}
for ln in out.splitlines()[1:]:
    f=ln.split("\t")
    if len(f)<12: continue
    try: conf=float(f[10])
    except ValueError: continue
    word=re.sub(r'[^A-Za-z]','',f[11])
    if conf<=0 or not word: continue
    key=(f[2],f[4],f[5]); l,t,w,h=(int(f[i]) for i in (6,7,8,9))
    lines.setdefault(key,[]).append((l,t,w,h,word.lower()))
for words in lines.values():
    norm=[w for *_,w in words]
    if any(lev(n,"starbase")/max(len(n),8)<=0.34 for n in norm):
        xs=[l+w/2 for l,t,w,h,_ in words]; ys=[t+h/2 for l,t,w,h,_ in words]
        print(int(sum(xs)/len(xs)/2), int(sum(ys)/len(ys)/2)); sys.exit(0)
sys.exit(1)
PY
}

xdotool windowactivate "$WID" 2>/dev/null; xdotool windowraise "$WID" 2>/dev/null; sleep 0.4
for _try in 1 2 3; do
    shot="$(explore_shot "")" || break
    xy="$(exit_starbase_xy "$shot" || true)"
    [ -z "$xy" ] && break          # not the concourse layout -> fall through to hangar sweep
    set -- $xy
    elog "Exit Starbase button at ($1,$2) -- clicking to launch"
    bash "$SKILL_DIR/click-map.sh" "$1" "$2" >/dev/null 2>&1
    sleep 6
    if in_space; then echo "UNDOCKED"; elog "launched via Exit Starbase"; exit 0; fi
done

# --- find the ship hull in a screenshot (right half, clear of chat) ----------
# Prints "x y" window-relative if a ship is detected, nothing otherwise.
find_ship() {
    python3 - "$1" <<'PY'
import sys
from PIL import Image
im = Image.open(sys.argv[1]).convert("RGB"); w, h = im.size
px = im.load()
# Chat overlay lives in the upper-left; ignore it entirely. Search the right
# portion and upper 2/3 where the parked ship sits.
x0 = int(w*0.40); y1 = int(h*0.70)
reds=[]; greens=[]
for y in range(0, y1, 3):
    for x in range(x0, w, 3):
        r,g,b = px[x,y]
        if max(r,g,b) < 70: continue
        if r>150 and r>g+50 and r>b+50: reds.append((x,y))
        if g>150 and g>r+40 and g>b+30: greens.append((x,y))
def cen(p): return (sum(a for a,_ in p)//len(p), sum(b for _,b in p)//len(p)) if p else None
rd=cen(reds); gr=cen(greens)
# The green+red nav-light pair on the wingtips is the ship's signature: a
# saturated colour the station's bright white light-strips/windows never make, so
# it is pollution-proof. Require both lights, roughly level, wingtip-spaced apart;
# the hull centre is their midpoint. No match -> keep rotating (a false click in a
# station opens menus, so we never guess from a single light).
if rd and gr:
    dx=abs(rd[0]-gr[0]); dy=abs(rd[1]-gr[1])
    if 60 < dx < 450 and dy < 110:
        print((rd[0]+gr[0])//2, (rd[1]+gr[1])//2); sys.exit(0)
PY
}

press() { local k="$1" n="$2" i; for ((i=0;i<n;i++)); do xdotool key "$k" 2>/dev/null; sleep 0.10; done; }

xdotool windowactivate "$WID" 2>/dev/null; xdotool windowraise "$WID" 2>/dev/null; sleep 0.4

# Rotate RIGHT until the ship appears, then click and verify. Bounded to a bit
# more than a full turn. One LEFT correction is allowed if we overshoot it.
total=0; corrected=0
while [ "$total" -lt 110 ]; do
    # CHECK WARP SPEED EVERY ITERATION: a warp-speed readout (e.g. "000") present at
    # all means we are OUTSIDE/in-space -- undocked, DONE (owner rule). This catches
    # both "the tow dropped us straight into space" (no docking at all) and "a hull
    # click just launched us": either way the moment digits appear we stop rotating.
    # Without this top-of-loop recheck a transient blank speed at respawn started a
    # rotate we could never abort, which spun a full turn and crashed the drive.
    if in_space; then echo "UNDOCKED"; elog "in space (warp-speed readout present)"; exit 0; fi
    shot="$(explore_shot "")" || { echo "FAILED"; exit 1; }
    coord="$(find_ship "$shot")"
    if [ -n "$coord" ]; then
        set -- $coord; sx="$1"; sy="$2"
        elog "ship at ($sx,$sy) -- clicking hull"
        bash "$SKILL_DIR/click-map.sh" "$sx" "$sy" >/dev/null 2>&1
        sleep 3
        if in_space; then echo "UNDOCKED"; elog "launched into space"; exit 0; fi
        # click didn't take: maybe slightly off / overshot. nudge a touch right.
        press Right 5; total=$((total+5)); continue
    fi
    press Right 10; total=$((total+10))
done

# Last resort: a finer sweep with a left bias in case lossy presses skipped it.
if [ "$corrected" -eq 0 ]; then
    corrected=1
    for ((j=0;j<14;j++)); do
        press Left 5
        shot="$(explore_shot "")" || break
        coord="$(find_ship "$shot")"
        if [ -n "$coord" ]; then
            set -- $coord
            bash "$SKILL_DIR/click-map.sh" "$1" "$2" >/dev/null 2>&1
            sleep 3
            in_space && { echo "UNDOCKED"; exit 0; }
        fi
    done
fi

echo "FAILED"; elog "could not find/launch ship after full sweep"; exit 1
