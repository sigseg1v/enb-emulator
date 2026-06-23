#!/usr/bin/env bash
# SPDX-License-Identifier: MIT
# Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
# License: LICENSES/Freya
#
# undock.sh -- launch the ship back into space when it is DOCKED at a station.
#
# Owner-taught method (2026-06-21, extended 2026-06-23), validated live:
#
#  * DOCKED DETECTION: the bottom-centre warp cluster (warp dial + speed digits +
#    the green/red/blue bars + the 1..6 action circles) is HIDDEN while docked and
#    only present in space. The cheap, reliable tell is to OCR the warp-speed
#    region: blank (no digits, not even "000") == docked; any chars == in space.
#
#  * STATION LAYOUT: you zone into a T-shaped hangar at one of 3 random catwalk
#    positions/orientations. The station EXIT DOOR is at the top-middle of the T
#    (DO NOT GO THERE). Your SHIP is parked on the hangar deck. You may zone in on
#    the FAR arm of the T, too far to board from a standing position -- so seeing
#    the ship is not enough, we have to WALK to it (see APPROACH below).
#
#  * FINDING THE SHIP: avatar turns with the Left/Right arrow keys (keyboard, sent
#    to the activated client window -- the warp orb etc. are still clicks). We
#    ROTATE RIGHT in fixed batches until the ship comes into view, CLEAR of the
#    chat overlay in the upper-left. The ship is unmistakable: a hull flanked by a
#    GREEN and a RED nav light. The detection is colour-only: a saturated green
#    blob + a saturated red blob, roughly level and wingtip-spaced apart -- a pair
#    the station's white light-strips never produce. The hull is their midpoint.
#
#  * APPROACH (the piece that makes a FAR ship reachable): rotating alone never
#    closes distance. There is NO keyboard walk that lands under XTEST here; the
#    one forward-move input that works is HOLDING THE RIGHT MOUSE BUTTON at screen
#    centre (~7s bursts -- owner, 2026-06-23). So we loop: re-detect the ship, TURN
#    to centre the hull (~1 arrow key per 30px of horizontal error), CLICK the hull
#    (boards it once we are in range), and if that did not undock, WALK FORWARD one
#    burst and repeat. Re-detecting every iteration self-corrects an overshoot (the
#    ship slides off-centre as we move); if it leaves view entirely we rotate to
#    reacquire.
#
#  * LAUNCH: clicking the hull centre (between the nav lights) while in range boards
#    the ship and drops us into space. Then the warp-speed OCR reads digits == we
#    are in space == undocked.
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
# Scan the whole frame (upper ~72%). As we walk toward the ship it can sit centre
# or even LEFT of us, so we cannot restrict to the right half (the old bug that
# made a far ship unreachable). Exclude only the chat overlay box in the upper-left,
# whose text can flash saturated colours.
y1 = int(h*0.72)
reds=[]; greens=[]
for y in range(0, y1, 3):
    for x in range(0, w, 3):
        if y < int(h*0.20) and x < int(w*0.45): continue
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

# --- locomotion + aiming helpers ---------------------------------------------
# turn <Left|Right> <count>: rotate the avatar via the arrow keys (focus-based, so
# raise/activate first). walk_forward <secs>: the ONLY forward move that lands here
# -- hold the right mouse button at window centre.
turn() {
    local k="$1" n="$2" i
    xdotool windowactivate "$WID" 2>/dev/null
    for ((i=0;i<n;i++)); do xdotool key "$k" 2>/dev/null; sleep 0.08; done
}
walk_forward() {
    local secs="$1" g gx gy gw gh
    g="$(win_abs "$WID")" || return 1
    read -r gx gy gw gh <<< "$g"
    xdotool windowactivate "$WID" 2>/dev/null
    xdotool mousemove $((gx + gw/2)) $((gy + gh/2)) 2>/dev/null
    xdotool mousedown 3; sleep "$secs"; xdotool mouseup 3
}

WALK_SECS="${ENB_UNDOCK_WALK_SECS:-7}"        # owner 2026-06-23: ~7s burst is best
CENTER_X="${ENB_UNDOCK_CENTER_X:-640}"        # 1280-wide client; hull aim point
APPROACH_MAX="${ENB_UNDOCK_APPROACH_MAX:-9}"  # walk/centre/board iterations

xdotool windowactivate "$WID" 2>/dev/null; xdotool windowraise "$WID" 2>/dev/null; sleep 0.4

# ACQUIRE: rotate RIGHT in batches until the green+red nav-light pair is in view.
# Bounded to a bit more than a full turn. (Recheck in_space each batch: a tow may
# have dropped us straight into space, or a respawn blanked the speed transiently.)
acquired=""; turned=0
while [ "$turned" -lt 130 ]; do
    if in_space; then echo "UNDOCKED"; elog "in space (warp-speed readout present)"; exit 0; fi
    shot="$(explore_shot "")" || { echo "FAILED"; exit 1; }
    [ -n "$(find_ship "$shot")" ] && { acquired=1; break; }
    turn Right 10; turned=$((turned + 10))
done
[ -n "$acquired" ] || { echo "FAILED"; elog "ship never came into view in a full turn"; exit 1; }

# APPROACH: centre the hull, board if in range, else walk a burst and repeat.
lost=0
for ((a=0; a<APPROACH_MAX; a++)); do
    if in_space; then echo "UNDOCKED"; elog "boarded ship -> in space"; exit 0; fi
    shot="$(explore_shot "")" || { echo "FAILED"; exit 1; }
    coord="$(find_ship "$shot")"
    if [ -z "$coord" ]; then
        # lost it (walked past / turned off it): nudge right to reacquire, widen
        # the nudge after a few misses.
        lost=$((lost + 1))
        if [ "$lost" -ge 3 ]; then turn Right 10; lost=0; else turn Right 4; fi
        continue
    fi
    lost=0
    set -- $coord; sx="$1"; sy="$2"
    err=$((sx - CENTER_X)); aerr=${err#-}
    if [ "$aerr" -gt 120 ]; then
        steps=$((aerr / 30)); [ "$steps" -lt 2 ] && steps=2; [ "$steps" -gt 12 ] && steps=12
        if [ "$err" -gt 0 ]; then turn Right "$steps"; else turn Left "$steps"; fi
        continue
    fi
    # centred: clicking the hull boards the ship once we are in range.
    elog "ship centred at ($sx,$sy) -- clicking hull"
    bash "$SKILL_DIR/click-map.sh" "$sx" "$sy" >/dev/null 2>&1
    sleep 3
    if in_space; then echo "UNDOCKED"; elog "boarded ship -> in space"; exit 0; fi
    # not in range yet: close the distance and try again.
    elog "not in range -- walking forward ${WALK_SECS}s"
    walk_forward "$WALK_SECS"
done

if in_space; then echo "UNDOCKED"; exit 0; fi
echo "FAILED"; elog "could not board ship after approach budget"; exit 1
