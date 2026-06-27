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

# --- board_click <winx> <winy> : DOUBLE-click the ship hull to launch ---------
# From the hangar/concourse layout a SINGLE click only selects/targets the ship
# (stays "SPEED ?" docked); the launch action is a DOUBLE-click on the hull.
# Proven live 2026-06-24: single-click parked, double-click boarded (SPEED 0).
# Convert win-rel -> abs via win_abs (same math as click_win) and emit a fast
# native double-click so the two presses land inside the game's dblclick window.
board_click() {
    local rx="$1" ry="$2" g gx gy gw gh x y
    raise_win "$WID" 2>/dev/null
    g="$(win_abs "$WID")" || { elog "board_click: no geometry"; return 1; }
    read -r gx gy gw gh <<< "$g"
    x=$(( gx + rx )); y=$(( gy + ry ))
    elog "board_click ($rx,$ry) -> abs ($x,$y) double-click"
    xdotool mousemove "$x" "$y" 2>/dev/null; sleep 0.12
    xdotool click --repeat 2 --delay 120 1
}

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

# --- "Exit Starbase" object label (the post-TOW concourse launch path) --------
# A Request-Tow does NOT drop us on the hangar catwalk -- it drops us standing in
# the station hangar with the ship parked right in front, carrying a YELLOW
# "Exit Starbase" world-object label over the hull. CLICKING that label launches
# us into space (verified live, Ishuan/Castor 2026-06-24) -- the rotate-and-click-
# hull sweep below never even runs in this layout.
#
# We detect the label by COLOUR, not OCR. The old grayscale-tesseract detector
# read ZERO "starbase" words here: the label is low-saturation yellow text sitting
# ON the bright-blue ship hull, and grayscale OCR cannot segment yellow-on-blue
# (it also false-matched the docking info-panel "STARBASE" header). Yellow pixels
# (high R, high G, low B, R~=G) are unmistakable against the blue hull and the
# white station strips, so we mask them, cluster on a 60px grid, and take the
# densest blob (the label text is the densest run of yellow in frame; stray chat /
# HUD yellow never approaches the label's pixel count). Restricted to the lower
# 55% of the frame where the parked ship + its label sit, which also drops the
# upper-left chat overlay. Prints "x y" window-rel if found, nothing otherwise.
exit_starbase_xy() {
    python3 - "$1" <<'PY'
import sys
from collections import defaultdict
from PIL import Image
im = Image.open(sys.argv[1]).convert("RGB"); w, h = im.size
px = im.load()
ymin = int(h*0.45)
ys=[]
for y in range(ymin, h):
    for x in range(0, w):
        r,g,b = px[x,y]
        if r>160 and g>140 and b<120 and abs(r-g)<70:
            ys.append((x,y))
if not ys: sys.exit(1)
cell=60
d=defaultdict(list)
for x,y in ys: d[(x//cell, y//cell)].append((x,y))
# merge each cell with its 8 neighbours so a label split across a cell boundary
# (the "Exit"|"Starbase" two-word run) counts as one blob, then take the densest.
best=None; bestn=0
for (cx,cy) in d:
    blob=[]
    for ax in (cx-1,cx,cx+1):
        for ay in (cy-1,cy,cy+1):
            blob += d.get((ax,ay), [])
    if len(blob) > bestn:
        bestn=len(blob); best=blob
if not best or bestn < 30: sys.exit(1)   # below this it is stray yellow, not the label
mx=sum(a for a,_ in best)//len(best); my=sum(b for _,b in best)//len(best)
print(mx, my); sys.exit(0)
PY
}

# --- close the sector/galaxy map if the survey left it open ------------------
# The survey's read-sector step OPENS the map (3rd bottom-left HUD button) to OCR
# the title and leaves it open. While DOCKED that map panel overlays the hangar
# and BLINDS every detector below (the ship hull, the Exit-Starbase label). So
# before we look for anything, detect an open map (its green SYSTEM/SECTOR/STARBASE
# labels) and click the same toggle to close it. The toggle is idempotent only as
# a toggle -- clicking it when the map is CLOSED would OPEN it -- so we ONLY click
# when an open map is actually detected, and verify it cleared.
map_open() {
    # OCR the docked map's vertical label stack; "STARBASE" is the distinctive one
    # (the docked map always shows STARBASE: <station>; it does not appear on the
    # bare hangar view). Returns 0 if the map looks open.
    tesseract "$1" - 2>/dev/null | grep -qiE 'STARBASE|GALAXY MAP'
}
close_map() {
    local shot
    for _m in 1 2 3; do
        shot="$(explore_shot "")" || return 0
        map_open "$shot" || return 0
        elog "map left open by survey -- closing (toggle 132,870)"
        bash "$SKILL_DIR/click-map.sh" 132 870 >/dev/null 2>&1
        sleep 1.2
    done
    # last check
    shot="$(explore_shot "")" || return 0
    if map_open "$shot"; then elog "WARN map still appears open after 3 toggles"; fi
}

xdotool windowactivate "$WID" 2>/dev/null; xdotool windowraise "$WID" 2>/dev/null; sleep 0.4
close_map

# --- find the ship hull in a screenshot (right half, clear of chat) ----------
# Prints "x y" window-relative if a ship is detected, nothing otherwise.
find_ship() {
    python3 - "$1" <<'PY'
import sys
from collections import Counter
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
# The green+red nav-light pair on the wingtips is the ship's signature: a
# saturated colour the station's bright white light-strips/windows never make, so
# it is pollution-proof. Require both lights, roughly level, wingtip-spaced apart;
# the hull centre is their midpoint. No match -> keep rotating (a false click in a
# station opens menus, so we never guess from a single light).
#
# CLUSTER, do not global-centroid (Carpenter tow, 2026-06-24): a station can have a
# stray GREEN decoy light (wall crystal, fixture) far from the ship. Averaging ALL
# greens then pairing against ALL reds drags the green centroid onto the decoy, so a
# genuinely boardable ship (red@733,green-wing near it) reads as a 461px span and the
# 450px cap REJECTS it -- the undock then walks right past a ship that was in frame.
# Fix: bucket each colour into compact 40px blobs and pair the closest red-blob /
# green-blob that fits the wingtip geometry, so a far decoy blob is simply not chosen.
def blobs(pts, cell=40, top=6):
    if not pts: return []
    common = Counter((x//cell, y//cell) for x,y in pts).most_common(top)
    out=[]
    for (cxk,cyk),_ in common:
        bx, by = cxk*cell+cell//2, cyk*cell+cell//2
        near=[(x,y) for x,y in pts if abs(x-bx)<cell and abs(y-by)<cell]
        if near:
            out.append((sum(a for a,_ in near)//len(near), sum(b for _,b in near)//len(near)))
    return out
best=None
for rd in blobs(reds):
    for gr in blobs(greens):
        dx=abs(rd[0]-gr[0]); dy=abs(rd[1]-gr[1])
        if 60 < dx < 450 and dy < 110:
            mx=(rd[0]+gr[0])//2; my=(rd[1]+gr[1])//2
            score=abs(mx - w//2)            # prefer the pair nearest frame centre
            if best is None or score < best[0]: best=(score, mx, my)
if best:
    print(best[1], best[2]); sys.exit(0)
PY
}

# --- find_ship_hint: a STEERING-ONLY single-light heading for far ships -------
# When the ship is far/angled, only ONE of its nav lights resolves, so find_ship's
# green+red PAIR never forms and acquire would FAIL outright (Ishuan tow, 2026-06-23).
# This prints "x y" of a single COMPACT nav-light blob (red OR green) to STEER the
# close-in walk toward the ship -- it NEVER authorises a board click (only the pair
# does). It defends against the two false reds that fooled a naive single-red probe:
#   1. red-lit station RAILINGS -- rejected by SHAPE (a nav light is a compact point;
#      a railing is elongated, so a blob whose span exceeds ~60px either axis is out).
#   2. the player's own red CHEST BADGE -- the third-person avatar is always centred,
#      so a blob inside the centre column / lower frame is excluded.
# Prints nothing when no compact off-avatar light is visible (keep rotating/closing).
find_ship_hint() {
    python3 - "$1" <<'PY'
import sys
from collections import Counter
from PIL import Image
im = Image.open(sys.argv[1]).convert("RGB"); w, h = im.size
px = im.load(); cx = w//2
def excluded(x, y):
    if y < int(h*0.20) and x < int(w*0.45): return True       # chat overlay
    if (cx-160) < x < (cx+160) and y > int(h*0.28): return True  # centred avatar+badge
    return False
pts=[]
for y in range(0, int(h*0.72), 2):
    for x in range(0, w, 2):
        if excluded(x, y): continue
        r,g,b = px[x,y]
        if max(r,g,b) < 80: continue
        if (r>150 and r>g+50 and r>b+50) or (g>150 and g>r+40 and g>b+30):
            pts.append((x,y))
if not pts: sys.exit(1)
cell=40
dense,_ = Counter((x//cell, y//cell) for x,y in pts).most_common(1)[0]
ccx, ccy = dense[0]*cell+cell//2, dense[1]*cell+cell//2
win=[(x,y) for x,y in pts if abs(x-ccx)<80 and abs(y-ccy)<80]
if len(win) < 4: sys.exit(1)
xs=[a for a,_ in win]; ys=[b for _,b in win]
if (max(xs)-min(xs)) > 60 or (max(ys)-min(ys)) > 60: sys.exit(1)  # elongated -> railing
print(sum(xs)//len(xs), sum(ys)//len(ys))
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

WALK_SECS="${ENB_UNDOCK_WALK_SECS:-7}"          # owner 2026-06-23: ~7s burst is best
CENTER_X="${ENB_UNDOCK_CENTER_X:-640}"          # 1280-wide client; hull aim point
APPROACH_MAX="${ENB_UNDOCK_APPROACH_MAX:-9}"    # walk/centre/board iterations
CLOSEIN_MAX="${ENB_UNDOCK_CLOSEIN_MAX:-4}"      # far-ship close-in walks when no pair
CLOSEIN_SECS="${ENB_UNDOCK_CLOSEIN_SECS:-4}"    # shorter than WALK_SECS to avoid overshoot
CAM_UP="${ENB_UNDOCK_CAM_UP:-6}"                # post-tow camera pitches DOWN -> pan it UP first
CAM_UP_STEP="${ENB_UNDOCK_CAM_UP_STEP:-2}"      # extra up-pitch per close-in round (progressive)

# aim_camera_up <n>: pitch the camera UP with the Up arrow. The post-TOW hangar drops
# us with the camera pitched DOWN at the deck, so a ship parked ELEVATED in the bay sits
# ABOVE the top of the frame -- outside find_ship's scan region -- and the yaw sweep alone
# can never see it (owner 2026-06-23: "your camera is facing down, use up arrow to pan it
# up"). Pan up once before acquiring, then a notch more per close-in round so we search
# progressively higher as we also close the distance. Down/Left/Right are camera pitch/yaw;
# Up is camera pitch up.
aim_camera_up() {
    local n="${1:-$CAM_UP}" i
    [ "$n" -le 0 ] && return 0
    xdotool windowactivate "$WID" 2>/dev/null
    for ((i=0;i<n;i++)); do xdotool key Up 2>/dev/null; sleep 0.06; done
}

xdotool windowactivate "$WID" 2>/dev/null; xdotool windowraise "$WID" 2>/dev/null; sleep 0.4
aim_camera_up "$CAM_UP"   # bring an elevated hangar ship down into the scan region

# --- CONCOURSE WALK-OUT + Exit-Starbase launch (the post-TOW path) ------------
# A Request-Tow drops us standing in the station, but at one of TWO sub-layouts:
#   (a) HANGAR -- the parked ship + its yellow "Exit Starbase" world-label are
#       right in front; double-clicking the label launches immediately.
#   (b) CONCOURSE HALLWAY -- we spawn back in an empty corridor with NOTHING in
#       frame (no ship, no label); we must WALK FORWARD into the hangar before the
#       ship/label appear. The old static 3-screenshot check saw nothing here and
#       fell straight through to the hangar nav-light sweep, which ALSO finds
#       nothing in a bare hallway -- so the post-tow undock stalled and the survey
#       mis-reported a frozen-frame hang (it was alive, just standing in the hall;
#       owner had to walk it in by hand, 2026-06-23/24).
# So this is now a WALK-AND-SCAN loop: scan for the yellow label (the COLOUR
# detector, which worked where OCR + the green/red pair both failed); if it is
# there, double-click to launch; if the ship PAIR is visible we are on the catwalk
# (layout a) -> hand off to the rotate/approach sweep below; otherwise we are in the
# hallway -> walk one forward burst (RMB-hold at centre, the only move that lands)
# into the hangar, occasionally turning to re-aim, and rescan. Bounded so a truly
# empty layout still falls through rather than walking forever.
# NOTE: this loop must stay BELOW the find_ship/walk_forward/turn/aim_camera_up
# definitions -- bash needs functions defined before they are called (an earlier
# placement above the defs made every call a "command not found" no-op).
CONCOURSE_MAX="${ENB_UNDOCK_CONCOURSE_MAX:-8}"
CONCOURSE_SECS="${ENB_UNDOCK_CONCOURSE_SECS:-3}"   # shorter than a full burst: the hall is short
for ((cw=0; cw<CONCOURSE_MAX; cw++)); do
    if in_space; then echo "UNDOCKED"; elog "in space"; exit 0; fi
    close_map
    shot="$(explore_shot "")" || break
    xy="$(exit_starbase_xy "$shot" || true)"
    if [ -n "$xy" ]; then
        set -- $xy
        elog "Exit Starbase label at ($1,$2) -- double-clicking to launch (round $cw)"
        board_click "$1" "$2"
        sleep 6
        if in_space; then echo "UNDOCKED"; elog "launched via Exit Starbase"; exit 0; fi
        # label was there but the launch did not register -- walk a hair closer and
        # rescan rather than re-stabbing the exact same pixel.
        walk_forward "$CONCOURSE_SECS"
        continue
    fi
    # No label. If the green+red ship pair is already in view we are on the hangar
    # catwalk (layout a) -> let the rotate/approach sweep below board it.
    [ -n "$(find_ship "$shot")" ] && { elog "ship pair visible -- handing to hangar sweep"; break; }
    # Empty frame: we are in the concourse hallway -> walk forward into the hangar.
    elog "concourse hallway (no ship/label) -- walking forward ${CONCOURSE_SECS}s into the hangar (round $cw)"
    walk_forward "$CONCOURSE_SECS"
    [ $((cw % 2)) -eq 1 ] && turn Right 6   # sweep a little in case the hangar is off-axis
done

# ACQUIRE: rotate RIGHT in batches until the green+red nav-light PAIR is in view.
# A full turn that finds no pair does NOT mean "no ship": when the ship is parked far
# across the hangar only ONE of its nav lights resolves, so the pair never forms (the
# Ishuan tow, 2026-06-23 -- acquire FAILED on a ship sitting 0 green / lots of red).
# So when a full rotation finds no pair we CLOSE THE DISTANCE (walk a burst toward the
# best single compact light, steering by find_ship_hint -- railings/badge rejected)
# and retry the rotation, bounded by CLOSEIN_MAX. As we cross the hangar the second
# light resolves and the normal pair-acquire + approach takes over. The pair is STILL
# the only thing that authorises a board click; the hint only steers the walk.
acquired=""
for ((ci=0; ci<=CLOSEIN_MAX; ci++)); do
    turned=0
    while [ "$turned" -lt 130 ]; do
        if in_space; then echo "UNDOCKED"; elog "in space (warp-speed readout present)"; exit 0; fi
        shot="$(explore_shot "")" || { echo "FAILED"; exit 1; }
        [ -n "$(find_ship "$shot")" ] && { acquired=1; break; }
        # No pair yet: if a single compact light is off-centre, face it so the eventual
        # close-in walk heads at the ship (a centred hint -> ~0 error -> we walk straight).
        hint="$(find_ship_hint "$shot" || true)"
        if [ -n "$hint" ]; then
            set -- $hint; herr=$(( $1 - CENTER_X )); aherr=${herr#-}
            if [ "$aherr" -gt 140 ]; then
                hsteps=$((aherr / 30)); [ "$hsteps" -gt 12 ] && hsteps=12
                if [ "$herr" -gt 0 ]; then turn Right "$hsteps"; else turn Left "$hsteps"; fi
                continue
            fi
        fi
        turn Right 10; turned=$((turned + 10))
    done
    [ -n "$acquired" ] && break
    [ "$ci" -lt "$CLOSEIN_MAX" ] || break
    # Full turn, no pair -> face the best single light (if any) and close the distance.
    shot="$(explore_shot "")" || { echo "FAILED"; exit 1; }
    hint="$(find_ship_hint "$shot" || true)"
    if [ -n "$hint" ]; then
        set -- $hint; herr=$(( $1 - CENTER_X )); aherr=${herr#-}
        if [ "$aherr" -gt 140 ]; then
            hsteps=$((aherr / 30)); [ "$hsteps" -gt 12 ] && hsteps=12
            if [ "$herr" -gt 0 ]; then turn Right "$hsteps"; else turn Left "$hsteps"; fi
        fi
    fi
    elog "no nav-light pair in a full turn -- pan up ${CAM_UP_STEP} + close-in walk ${CLOSEIN_SECS}s (attempt $((ci+1))/$CLOSEIN_MAX)"
    aim_camera_up "$CAM_UP_STEP"   # search progressively higher in case the ship sits elevated
    walk_forward "$CLOSEIN_SECS"
done
[ -n "$acquired" ] || { echo "FAILED"; elog "ship never came into view after close-in"; exit 1; }

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
    elog "ship centred at ($sx,$sy) -- double-clicking hull to board"
    board_click "$sx" "$sy"
    sleep 3
    if in_space; then echo "UNDOCKED"; elog "boarded ship -> in space"; exit 0; fi
    # not in range yet: close the distance and try again.
    elog "not in range -- walking forward ${WALK_SECS}s"
    walk_forward "$WALK_SECS"
done

if in_space; then echo "UNDOCKED"; exit 0; fi
echo "FAILED"; elog "could not board ship after approach budget"; exit 1
