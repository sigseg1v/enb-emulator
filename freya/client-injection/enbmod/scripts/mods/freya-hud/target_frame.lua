-- SPDX-License-Identifier: MIT
-- Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
-- License: LICENSES/Freya
--
-- target_frame.lua -- the Freya redesign of the bottom-right TARGET frame.
--
-- The native target frame is a single chrome panel (hide-ui "TargetFrame") that
-- paints, as ONE atomic unit: a translucent glass background, a red hull bar + a
-- blue shield bar, the target name, a "Dist:" readout, the two cycle arrows, and
-- the target-action button cluster. The target's 3D model is a SEPARATE PIP scene
-- render, independent of that chrome -- so hiding the chrome panel (done by default
-- in the hide-ui mod) strips every 2D element while KEEPING the spinning 3D model.
-- This module repaints the 2D layer the way the owner asked, anchored BOTTOM-RIGHT
-- and stacked UPWARD from the bottom edge:
--
--   * bars at the BOTTOM: two stacked full-width bars, no gap, hull (red) over
--     shield (blue), in the player-card bar style but ~1/3 the height, with the
--     current/total value printed on the bar.
--   * the DISTANCE readout one line ABOVE the bars.
--   * the target NAME above the distance, wrapping to a second line when too wide,
--     with " (L<level>)" appended (e.g. "Luna Station (L5)").
--   * the Freya verb BUTTONS on TOP: a row of small square single-letter buttons,
--     one per context verb the target's class offers.
--   * NO background -- the model floats over space (the native glass is gone).
--
-- Data comes from enb.target() (the crash-safe, VEH-guarded current-target read
-- in lua_api): name, hull / hull_max (absolute HullPoints / MaxHullPoints),
-- shield_max (MaxShieldPower), shield (current, when the client exposes it), level
-- (TargetThreatLevel), and class (the generic class name, e.g. "Starbase"). A
-- station target has no vitals, so its bars render as empty tracks -- correct, a
-- starbase has no hull/shield to show.

local H = require("freya_hud")

-- ---- geometry (window-relative; the frame is anchored bottom-right) ----------
local CFG = {
    BAR_W    = 250,   -- bar / content width
    MARGIN_R = 12,    -- gap from the right screen edge (scaled in by MARGIN_R_TUNE)
    MARGIN_B = 30,    -- gap from the bottom screen edge to the lowest bar
    -- on a larger-than-1280x960 screen the owner pulls the frame inward from the
    -- right (matching how the other HUD elements scale their border gaps); 0 at
    -- the reference res so the base layout is untouched.
    MARGIN_R_TUNE = 20,

    -- Freya verb buttons: a row of small SQUARE single-letter buttons on TOP of the
    -- frame (owner ask: replace the native target-action button cluster). One per
    -- context verb the target's class offers; the letter is the verb's first glyph
    -- (Dock -> D, Register -> R, Follow -> F, ...). Clicking one fires that verb
    -- through the game's own command path -- the exact action the native button did.
    BTN_SIZE  = 17,   -- square button edge at the reference res (scaled up on a big screen)
    BTN_GAP   = 3,    -- gap between adjacent buttons
    BTN_ROW_GAP = 4,  -- gap from the button row down to the name
    BTN_MAX   = 6,    -- hard cap on buttons drawn
    FLASH_TICKS = 16, -- click-feedback glow duration (held long enough to read)

    -- 1/3 of the player-card vital bar (16px) -> ~5-6px; no gap between the two.
    BAR_H    = 6,
    BAR_GAP  = 0,
    -- the hull/shield bars define the whole frame's width: on a larger-than-reference
    -- screen the owner pulls the bars' right edge further inward and extends their
    -- length to the LEFT, and the name / distance / buttons left-align to the bars'
    -- left edge -- so the entire frame shifts+grows left together. Both authored @
    -- 2560x1440 and scaled in via H.sx (0 at the 1280x960 baseline, so the base layout
    -- is untouched).
    BAR_INSET_R  = 16,   -- extra right-edge inset, 1440p
    BAR_EXTEND_L = 128,  -- extra bar length added to the LEFT, 1440p
    -- lift the whole frame (bars + distance + name + buttons) up, 1440p only.
    FRAME_UP     = 6,
    -- the cur/total value sits ON the bar, right-aligned and vertically centred at
    -- a reduced scale so it fits the thin bar.
    VAL_PAD   = 4,    -- inset of the value text from the bar's right edge
    VAL_SCALE = 0.6,  -- value-text scale so it fits the thin bar

    NAME_GAP = 3,     -- gap from the upper bar up to the distance line
    LINE_H   = 14,    -- name / distance line height
    DIST_GAP = 2,     -- gap between the distance line and the name above it
    DIST_ALPHA = 204, -- distance text opacity (~80% of 255)
}

-- ---- verb table (the target-action letter buttons) --------------------------
-- Our emulator server pushes the per-target verb list but we have never located the
-- client-side verb store, and the native on-click dispatcher needs a transient verb-UI
-- object only reachable through the button we are replacing. So we drive the buttons
-- from a fixed table gated by the target's CLASS (enb.target().class), mirroring the
-- SERVER's per-class verb sets (AddVerb calls in NavTypeClass / ResourceClass / etc.):
--
--   Starbase/Station  -> Dock + Register      Asteroid/Resource -> Prospect + Tractor
--   Ship (mob/player) -> Follow
--
-- Each verb maps to the SAME wire action the native button performs --
-- enb.target_action(op, mode) builds the avatar-command and pushes it through M's
-- sector-server Connection:
--   op   = the server avatar-command case the verb drives (NOT a guess: cross-checked
--          against the server's command switch -- Dock 0x1c, Register 0x19, Prospect
--          0x11, Tractor 0x01, Trade 0x14, Group 0x0a, Follow 0x0c).
--   mode = "target" act on the locked target | "self" command targets the player's own
--          id (Tractor) | "follow" the Follow builder (no target field).
--
-- NOTE -- Loot is deliberately absent. The husk LOOT verb is NOT an avatar-command op
-- (the server's command switch has no loot case; looting rides the proxy-handled loot
-- band) and the client dispatches it through a local HulkUI panel object we cannot reach
-- without the native button. Shipping an "L" that silently does nothing would be worse
-- than omitting it, so a husk shows no button until the loot path is wired (plans/49).
local function cls_has(t, word)
    return type(t.class) == "string" and t.class:lower():find(word, 1, true) ~= nil
end
local function is_station(t)
    return cls_has(t, "starbase") or cls_has(t, "station") or cls_has(t, "outpost")
end
local function is_gate(t)     return cls_has(t, "gate") end
local function is_planet(t)   return cls_has(t, "planet") end
local function is_resource(t)
    -- The generic class the client reports for a prospectable body is "Harvestable"
    -- (asteroids, gas/dust clouds, ore fields all share it); the specific words below
    -- are kept as belt-and-suspenders for any object that names its class differently.
    return cls_has(t, "harvestable")
        or cls_has(t, "asteroid") or cls_has(t, "resource")
        or cls_has(t, "field") or cls_has(t, "ore") or cls_has(t, "cloud")
end
-- "ship-like" = a mob/player ship: anything with a hull (or an explicit Ship class)
-- that is not a station / gate / planet / resource. Unknown-class targets with a hull
-- default here so Follow still appears rather than the frame going button-less.
local function is_ship(t)
    if is_station(t) or is_gate(t) or is_planet(t) or is_resource(t) then return false end
    return cls_has(t, "ship") or t.hull_max ~= nil
end
-- A PLAYER (not a mob). Both are hull-bearing "Ship"-class objects, so the class name
-- at container+0x3c cannot tell them apart -- but player GameIDs live in the high id
-- range (>= 0x40000000) while mob ids are low. t.gid is the target's GameID, read off
-- entity+0x90 in draw_target. Player-only verbs (Trade/Group/JumpStart) gate on this.
local function is_player(t)
    return is_ship(t) and type(t.gid) == "number" and t.gid >= 0x40000000
end

-- DOCKING_RANGE is the server's verb-activation range (PlanetClass / NavTypeClass
-- AddVerb(..., DOCKING_RANGE), 5000.0f), measured EDGE-to-edge -- the server's
-- RangeFrom subtracts the body's m_ObjectRadius before comparing. We only have the
-- CENTRE-to-centre distance client-side (t.dist = |player_wpos - target_wpos|), and
-- we cannot read the target body's radius, so the docking-class verbs (D/R/G/L) carry
-- a `gate` = a CENTRE-distance ceiling chosen as (largest body radius for that class
-- + DOCKING_RANGE). Beyond `gate` the verb can never be active, so firing it would
-- only wedge the server's one-shot m_Gating flag (case 29/28/18 set it true and the
-- handoff never completes when the client did not auto-approach) -- that is exactly
-- the "I clicked L and it did nothing" the owner hit at 150k from Inverness. The
-- ceilings never false-disable an in-range button (they exceed the real activation
-- distance) and stay loose on the safe side; only the wedge-prone verbs are gated.
-- Stations / gates are small bodies (~<=15k radius) -> 20k. Planets are huge (the
-- stock planet radii run ~35k-120k) -> 130k. P/T/F/Trade/Group/JumpStart do NOT set
-- m_Gating (out-of-range just no-ops server-side), so they are deliberately ungated.
local DOCK_GATE   = 20000   -- station / gate / register centre-distance ceiling
local LAND_GATE   = 130000  -- planet centre-distance ceiling (radius up to ~120k + 5k)

local VERBS = {
    { letter = "D", label = "Dock",     op = 0x1c, mode = "target", show = is_station, gate = DOCK_GATE },
    { letter = "R", label = "Register", op = 0x19, mode = "target", show = is_station, gate = DOCK_GATE },
    { letter = "G", label = "Gate",     op = 0x12, mode = "target", show = is_gate,    gate = DOCK_GATE },
    -- Land on a planet. Landing is a gate: a TWO-step server exchange, not one. op 0x1d
    -- (server case 29) only ARMS it -- it sets StargateDestination to the planet's
    -- landing zone (e.g. Inverness -> Inverness Planet) and plays the "Confirmed" sound;
    -- the actual transit happens when op 0x08 (server case 8) does the SectorServerHandoff.
    -- The native client sends 0x1d then 0x08 (there is NO fly-in -- it transits instantly
    -- like a stargate, just a different label + animation). mode "land" sends BOTH back to
    -- back. Sends the planet's GameID (entity+0x90), the same id Dock uses, so case 29
    -- resolves the planet via myAction->Target. A planet with no landing Destination
    -- falls through server-side to the deploy/scan branch (effect 10007 -- the old "hack").
    { letter = "L", label = "Land",     op = 0x1d, mode = "land",   show = is_planet,  gate = LAND_GATE },
    { letter = "P", label = "Prospect", op = 0x11, mode = "target", show = is_resource },
    { letter = "T", label = "Tractor",  op = 0x01, mode = "self",   show = is_resource },
    { letter = "F", label = "Follow",   op = 0x0c, mode = "follow", show = is_ship },
    { letter = "T", label = "Trade",    op = 0x14, mode = "target", show = is_player },
    { letter = "G", label = "Group",    op = 0x0a, mode = "target", show = is_player },
    { letter = "J", label = "JumpStart",op = 0x1a, mode = "target", show = is_player },
}

-- Build the list of verbs to show for the current target (gated by class). A verb with
-- a `gate` (the docking-class D/R/G/L) is still SHOWN out of range but marked
-- enabled=false -- it renders dimmed and refuses to fire, the way the native client
-- greys an out-of-range target-action button, so it can never wedge the server's
-- gating flag. Ungated verbs are always enabled. A nil t.dist (distance not yet
-- resolved) disables a gated verb rather than firing it blind.
local function read_verbs(t)
    local out = {}
    for _, v in ipairs(VERBS) do
        if #out >= CFG.BTN_MAX then break end
        if v.show(t) then
            local enabled = (v.gate == nil) or (t.dist ~= nil and t.dist <= v.gate)
            out[#out + 1] = { letter = v.letter, label = v.label, op = v.op,
                              mode = v.mode, enabled = enabled }
        end
    end
    return out
end

-- live button rects (rebuilt every draw) + per-id transient click-flash, so the
-- on_input hit-test and the draw agree on geometry without recomputing it.
local btn_rects = {}
local flash = {}   -- [op] = remaining ticks of the ACCEPT glow (cyan: verb fired)
local deny  = {}   -- [op] = remaining ticks of the DENY glow (amber: out of range)

local function point_in(px, py, x, y, w, h)
    return px >= x and px <= x + w and py >= y and py <= y + h
end

-- ---- one bar: track + gradient fill + border + cur/total text ---------------
-- Mirrors draw_player_card's vital bar, scaled to BAR_H. cur/max may be nil
-- (unknown current, e.g. a station's hull) -> empty track, no value text.
local function draw_bar(x, y, w, rgb, cur, max, bar_h, val_scale)
    enb.draw.rrect(x, y, w, bar_h, H.RADIUS, H.TRACK, 220, true)
    if cur and max and max > 0 then
        local frac = cur / max
        if frac < 0 then frac = 0 elseif frac > 1 then frac = 1 end
        local fw = math.floor(w * frac)
        if fw > 0 then
            enb.draw.rrect_grad(x, y, fw, bar_h, H.RADIUS, rgb, H.darker(rgb), 255)
        end
    end
    enb.draw.rrect(x, y, w, bar_h, H.RADIUS, H.LINE, 70, false)
    if cur and max then
        local val = string.format("%d / %d", math.floor(cur + 0.5), math.floor(max + 0.5))
        local vw, vh = H.measure(val)
        -- owner: hull/shield value font 2px larger. Resolution-scaled (H.sy(2) is 0
        -- at 1280x960, 2 at 2560x1440), added to the rendered glyph height.
        local eff = val_scale + H.sy(2) / vh
        vw = vw * eff
        vh = vh * eff
        local ty = y + (bar_h - vh) / 2   -- center the scaled text on the bar
        H.otext(x + w - CFG.VAL_PAD - vw, ty, val, 0xffffff, eff)
    end
end

-- ---- name wrap (<= 2 lines, breaking on spaces) -----------------------------
local function wrap_two(s, max_w)
    if H.measure(s) <= max_w then return { s } end
    local words, line, lines = {}, "", {}
    for word in s:gmatch("%S+") do words[#words + 1] = word end
    for _, word in ipairs(words) do
        local cand = (line == "") and word or (line .. " " .. word)
        if H.measure(cand) <= max_w or line == "" then
            line = cand
        else
            lines[#lines + 1] = line
            line = word
            if #lines == 1 then break end   -- only ever two lines; rest goes on line 2
        end
    end
    if #lines == 1 then
        local consumed = lines[1]
        local rest = s:sub(#consumed + 2)   -- skip the space after line 1
        while H.measure(rest) > max_w and #rest > 1 do
            rest = rest:sub(1, #rest - 1)
        end
        lines[2] = rest
    else
        lines[1] = line
    end
    return lines
end

-- ---- the Freya verb-button row (grows UP from a bottom edge) -----------------
-- Lay one square single-letter button per context verb in a single row whose BOTTOM
-- sits at bottom_y, left-aligned at x and clipped to maxw. Rebuilds btn_rects (the
-- hit-test geometry the on_input handler reads). No verbs -> nothing drawn.
local function draw_buttons_up(verbs, x, bottom_y, maxw)
    btn_rects = {}
    if #verbs == 0 then return end

    -- square edge + gap, grown on a bigger-than-reference screen so the buttons
    -- track the enlarged bars/value font (both 0 at the 1280x960 baseline).
    local bsz = CFG.BTN_SIZE + H.sy(7)
    local gap = CFG.BTN_GAP + H.sx(2)
    local top = bottom_y - bsz

    local cx = x
    for _, v in ipairs(verbs) do
        if cx + bsz > x + maxw and cx > x then break end   -- single row; clip to width
        btn_rects[#btn_rects + 1] = { x = cx, y = top, w = bsz, h = bsz,
                                      op = v.op, mode = v.mode,
                                      letter = v.letter, label = v.label,
                                      enabled = v.enabled ~= false }
        cx = cx + bsz + gap
    end

    for _, r in ipairs(btn_rects) do
        local hot     = (flash[r.op] or 0) > 0   -- accept glow (cyan)
        local denied  = (deny[r.op]  or 0) > 0   -- deny glow (amber: out of range)
        H.glass(r.x, r.y, r.w, r.h)
        -- hot = a click fired the verb: vivid cyan fill + bright border so the press
        -- reads unmistakably (the idle glass is near-flat, so a subtle shift is easy to
        -- miss -- the click must feel acknowledged even when the action is a no-op, e.g.
        -- Register at a station you are already registered at). denied = a click on an
        -- out-of-range docking verb: amber so "registered but too far" reads distinctly
        -- from a successful cyan fire. A disabled (out-of-range) idle button paints
        -- dimmer than an active one, the way the native client greys an inactive verb.
        local tcol, bcol, fa
        if denied then
            tcol, bcol, fa = 0xffb020, 0x3a2606, 255
        elseif hot then
            tcol, bcol, fa = 0x7ad4ff, 0x2a8fd6, 255
        elseif r.enabled then
            tcol, bcol, fa = 0x223044, 0x0c121b, 230
        else
            tcol, bcol, fa = 0x161d28, 0x080c12, 150   -- dimmed: out of range
        end
        enb.draw.rrect_grad(r.x, r.y, r.w, r.h, H.RADIUS, tcol, bcol, fa)
        local glow = hot or denied
        enb.draw.rrect(r.x, r.y, r.w, r.h, H.RADIUS,
                       glow and H.LINE_HOT or H.LINE,
                       glow and 255 or (r.enabled and 150 or 80), false)
        -- the single letter, centred and scaled up to fill a big-screen button.
        local scale = H.smul(1.5)
        local lw, lh = H.measure(r.letter)
        lw, lh = lw * scale, lh * scale
        local ink = H.INK
        if denied then ink = 0x1a1206
        elseif hot then ink = 0x06121f
        elseif not r.enabled then ink = H.darker(H.INK) end
        H.otext(r.x + (r.w - lw) / 2, r.y + (r.h - lh) / 2, r.letter, ink, scale)
    end

    -- decay both glows one tick so the feedback fades.
    for id, n in pairs(flash) do flash[id] = (n > 1) and (n - 1) or nil end
    for id, n in pairs(deny)  do deny[id]  = (n > 1) and (n - 1) or nil end
end

local function draw_target()
    local t = enb.target and enb.target()
    if not t then return end
    -- the target's GameID (entity+0x90) -- distinguishes a player (high id range) from a
    -- mob so the player-only verbs can gate on it. Cheap, crash-safe (mem.u32 is guarded).
    if enb.target_obj and enb.mem then
        local obj = enb.target_obj()
        if obj and obj ~= 0 then t.gid = enb.mem.u32(obj + 0x90) end
    end

    local sw, sh = enb.screen()
    if not sw or sw == 0 then sw, sh = 1280, 960 end

    local bar_h     = CFG.BAR_H + H.sy(6)
    local val_scale = CFG.VAL_SCALE * H.smul(1.5)

    -- content rect: bottom-right anchored with scaled edge gaps. The bars define the
    -- frame's width and left anchor -- on a larger screen their right edge pulls inward
    -- (BAR_INSET_R) and the track extends to the LEFT (BAR_EXTEND_L); `bx`/`bw` are the
    -- resulting bar left edge + width, and the distance / name / buttons left-align to
    -- `bx` so the whole frame shifts+grows left together. The frame also lifts up by
    -- FRAME_UP. All three offsets are 0 at the 1280x960 baseline (base layout untouched).
    local right  = sw - (CFG.MARGIN_R + H.sx(CFG.MARGIN_R_TUNE))
    local bottom = sh - CFG.MARGIN_B - H.sy(CFG.FRAME_UP)
    local bw = CFG.BAR_W + H.sx(CFG.BAR_EXTEND_L)
    local bx = right - H.sx(CFG.BAR_INSET_R) - bw

    -- ---- bottom: two stacked bars (hull over shield), shield bottom at `bottom`.
    local shield_y = bottom - bar_h
    local hull_y   = shield_y - bar_h - CFG.BAR_GAP
    draw_bar(bx, hull_y,   bw, H.HULL,   t.hull,   t.hull_max,   bar_h, val_scale)
    draw_bar(bx, shield_y, bw, H.SHIELD, t.shield, t.shield_max, bar_h, val_scale)

    -- a single cursor walking UPWARD from the top of the bars; each element places
    -- its top edge then moves the cursor above it.
    local cursor = hull_y - CFG.NAME_GAP

    -- ---- distance: one line above the bars.
    if t.dist then
        cursor = cursor - CFG.LINE_H
        H.otext(bx, cursor, string.format("Dist %.2fk", t.dist / 1000),
                H.darker(H.INK), nil, CFG.DIST_ALPHA)
        cursor = cursor - CFG.DIST_GAP
    end

    -- ---- name + " (L<level>)" above the distance, <= 2 lines (drawn bottom-up).
    local label = t.name or "Target"
    if t.level then label = label .. string.format(" (L%d)", t.level) end
    local lines = wrap_two(label, bw)
    for i = #lines, 1, -1 do
        cursor = cursor - CFG.LINE_H
        H.otext(bx, cursor, lines[i], H.INK)
    end

    -- ---- buttons on top, their row bottom a gap above the name.
    draw_buttons_up(read_verbs(t), bx, cursor - CFG.BTN_ROW_GAP, bw)
end

-- ===========================================================================
-- DRAW  -- only in space (the target frame is an in-space cockpit element).
-- ===========================================================================
enb.on_tick(function()
    if not H.vis() then return end
    draw_target()
end)

-- ===========================================================================
-- INPUT  -- click a verb button to fire that verb. enb.target_action(op, mode)
-- builds the avatar-command and pushes it through the world manager's sector-server
-- Connection -- the wire action the native target-action button click performs. A
-- click landing on a button is swallowed so it never falls through to the 3D scene
-- behind it. btn_rects is rebuilt every draw, so draw and input stay in lockstep.
-- ===========================================================================
local function mouse_xy(lp)
    local x = lp & 0xFFFF
    local y = (lp >> 16) & 0xFFFF
    if x >= 0x8000 then x = x - 0x10000 end
    if y >= 0x8000 then y = y - 0x10000 end
    return x, y
end

enb.on_input(function(msg, wparam, lparam)
    local M = enb.msg
    if not H.vis() then return false end
    if msg ~= M.LBUTTONDOWN and msg ~= M.LBUTTONUP and msg ~= M.LBUTTONDBLCLK then
        return false
    end
    local mx, my = mouse_xy(lparam)
    for _, r in ipairs(btn_rects) do
        if point_in(mx, my, r.x, r.y, r.w, r.h) then
            if msg == M.LBUTTONDOWN then
                if r.enabled and enb.target_action then
                    enb.target_action(r.op, r.mode)
                    flash[r.op] = CFG.FLASH_TICKS
                else
                    -- out of range: acknowledge the click (amber) but do NOT fire --
                    -- firing a docking verb out of range only wedges the server's
                    -- gating flag. The owner must fly closer for it to activate.
                    deny[r.op] = CFG.FLASH_TICKS
                    enb.log(string.format("%s: out of range -- fly closer", r.label))
                end
            end
            return true   -- swallow any button event over a verb button
        end
    end
    return false
end, enb.WANT_MOUSE)

enb.log("target_frame.lua loaded")
