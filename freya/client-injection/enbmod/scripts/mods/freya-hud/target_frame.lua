-- SPDX-License-Identifier: MIT
-- Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
-- License: LICENSES/Freya
--
-- target_frame.lua -- the Freya redesign of the bottom-right TARGET frame.
--
-- The native target frame is a single chrome panel (hide-ui "TargetFrame") that
-- paints, as ONE atomic unit: a translucent glass background, a red hull bar + a
-- blue shield bar, the target name, a "Dist:" readout, the two cycle arrows, and
-- the two round scan/cycle buttons above it. The target's 3D model is a SEPARATE
-- PIP scene render, independent of that chrome -- so hiding the chrome panel
-- (done by default in the hide-ui mod) strips every 2D element while KEEPING the
-- spinning 3D model. This module repaints the 2D layer the way the owner asked:
--
--   * NO background -- the model floats over space (the native glass is gone).
--   * Two stacked bars at the top of the target area, full width, no gap between
--     them: hull (red) over shield (blue). Same player-card bar style (track +
--     vertical-gradient fill + hairline border) but ~1/3 the player-card bar
--     height, with the current/total value printed on the bar.
--   * Below the bars, the target name, wrapping to a second line when too wide,
--     with " (L<level>)" appended (e.g. "Luna Station (L5)").
--
-- Data comes from enb.target() (the crash-safe, VEH-guarded current-target read
-- in lua_api): name, hull / hull_max (absolute HullPoints / MaxHullPoints),
-- shield_max (MaxShieldPower), shield (current, when the client exposes it), and
-- level (the target's TargetThreatLevel). A station target has no vitals, so its
-- bars render as empty tracks -- correct, a starbase has no hull/shield to show.

local H = require("freya_hud")

-- ---- geometry (window-relative; the native frame sits bottom-right) ---------
-- Measured against the live native frame in a 1280x960 render: the chrome panel
-- spans x 1002..1273 / top y 663, the bar strip sits at the panel's top inner
-- edge, and the name sits just below it. We anchor to the screen's bottom-right
-- so a different backbuffer size keeps the frame pinned where the native one is.
local CFG = {
    AREA_W   = 271,   -- target-area width  (native panel width)
    AREA_H   = 267,   -- target-area height (native panel height)
    MARGIN_R = 7,     -- gap from the right screen edge to the area
    MARGIN_B = 30,    -- gap from the bottom screen edge to the area

    PAD_X    = 4,     -- inset of the bars/name from the area's left/right edges
    PAD_TOP  = 7,     -- inset of the first element from the area's top edge (owner: +4px down)

    -- Freya verb buttons: a row of small SQUARE single-letter buttons sitting just
    -- ABOVE the hull/shield bars (owner ask: replace the native target-action button
    -- cluster). One per context-menu verb the server gave the current target; the
    -- letter is the first character of the verb's display label (Follow -> F, Loot ->
    -- L, Dock -> D, ...). Clicking one fires that verb through the game's own verb
    -- dispatcher -- the exact action the native button click performed.
    BTN_SIZE  = 17,   -- square button edge at the reference res (scaled up on a big screen)
    BTN_GAP   = 3,    -- gap between adjacent buttons
    BTN_ROW_GAP = 4,  -- gap from the button row down to the first bar
    BTN_MAX   = 12,   -- hard cap on buttons drawn (the dispatcher defines 13 verbs)
    FLASH_TICKS = 8,  -- click-feedback glow duration
    BAR_TRIM_R = 12,  -- trim the bars this many px on the RIGHT (owner ask): keeps
                      -- the bars off the screen edge and pulls the right-aligned
                      -- cur/total value inward so its total no longer clips.

    -- 1/3 of the player-card vital bar (16px) -> ~5-6px; no gap between the two.
    BAR_H    = 6,
    BAR_GAP  = 0,
    -- the cur/total value sits ON the bar (owner: "text on them ... but only 1/3
    -- as tall"). The one overlay font is ~14px, far taller than a 6px bar, so the
    -- value is drawn at a reduced scale, right-aligned and vertically centered on
    -- the bar, so the two bars' values do not collide.
    VAL_PAD   = 4,    -- inset of the value text from the bar's right edge
    VAL_SCALE = 0.6,  -- value-text scale so it fits the thin bar

    NAME_GAP = 3,     -- gap from the lower bar to the name
    LINE_H   = 14,    -- name line height for the 2-line wrap
    DIST_GAP = 2,     -- extra gap above the distance line (owner: move it down 2px)
    DIST_ALPHA = 204, -- distance text opacity (~80% of 255)
}

-- resolve the area rect in current backbuffer coords.
local function area_rect()
    local sw, sh = enb.screen()
    if not sw or sw == 0 then sw, sh = 1280, 960 end
    -- on a larger-than-1280x960 screen the owner pulls the whole frame inward
    -- (32px @ 2560x1440, scaled); at the reference res H.sx is 0 -> no change.
    local x = sw - (CFG.MARGIN_R + H.sx(32)) - CFG.AREA_W
    local y = sh - CFG.MARGIN_B - CFG.AREA_H
    return x, y, CFG.AREA_W, CFG.AREA_H
end

-- ---- verb table (the target-action letter buttons) --------------------------
-- The native client decides a target's verb set from a server-pushed verb list our
-- emulator server does not currently emit, and the on-click dispatcher needs a transient
-- verb-UI object that is only reachable through the native button we are replacing. So we
-- drive the buttons from a fixed verb table instead and gate which ones show by the
-- target's CLASS (enb.target().class -- "Ship"/"Starbase"/"Stargate"/...). Each verb maps
-- to the SAME wire action the native button performs: enb.target_action(op[, no_target])
-- builds the command and pushes it through the world manager's sector-server Connection.
--
--   op       = the per-verb action byte read out of the client's verb dispatcher.
--   no_target= the follow verb carries no target field (server follows current target).
--   show(t)  = predicate on the live target table deciding whether to draw this button.
--
-- letter = first char of the label (owner ask: "Follow -> F, Loot -> L"). Dock collides
-- with no other shown verb per class; where two would collide we pick a distinct glyph.
local function cls_has(t, word)
    return type(t.class) == "string" and t.class:lower():find(word, 1, true) ~= nil
end
local function is_station(t) return cls_has(t, "starbase") or cls_has(t, "station") end
local function is_gate(t)    return cls_has(t, "gate") end
-- "ship-like" = anything with a hull (mob/player ship), or an explicit Ship class, and
-- not a station/gate. Unknown-class targets default to ship-like so the combat verbs
-- still appear rather than the frame going button-less.
local function is_ship(t)
    if is_station(t) or is_gate(t) then return false end
    return cls_has(t, "ship") or t.hull_max ~= nil or t.class == nil
end

local VERBS = {
    { letter = "A", label = "Attack", op = 0x0a,                show = is_ship },
    { letter = "F", label = "Follow", op = 0x0c, no_target = true, show = is_ship },
    { letter = "L", label = "Loot",   op = 0x19,                show = is_ship },
    { letter = "B", label = "Board",  op = 0x12,                show = is_ship },
    { letter = "T", label = "Trade",  op = 0x14,
      show = function(t) return is_station(t) or is_ship(t) end },
    { letter = "D", label = "Dock",   op = 0x1d,
      show = function(t) return is_station(t) or is_gate(t) end },
}

-- Build the list of verbs to show for the current target (gated by class).
local function read_verbs(t)
    local out = {}
    for _, v in ipairs(VERBS) do
        if #out >= CFG.BTN_MAX then break end
        if v.show(t) then
            out[#out + 1] = { letter = v.letter, label = v.label,
                              op = v.op, no_target = v.no_target }
        end
    end
    return out
end

-- live button rects (rebuilt every draw) + per-id transient click-flash, so the
-- on_input hit-test and the draw agree on geometry without recomputing it.
local btn_rects = {}
local flash = {}   -- [op] = remaining ticks

local function point_in(px, py, x, y, w, h)
    return px >= x and px <= x + w and py >= y and py <= y + h
end

-- ---- one bar: track + gradient fill + border + cur/total text ---------------
-- Mirrors draw_player_card's vital bar, scaled to BAR_H. cur/max may be nil
-- (unknown current, e.g. a station's hull) -> empty track, no value text.
-- bar_h and val_scale are resolution-scaled by the caller (taller bars + larger
-- value font on a big screen), so the bar height and its inner text grow together.
local function draw_bar(x, y, w, rgb, cur, max, bar_h, val_scale)
    enb.draw.rrect(x, y, w, bar_h, H.RADIUS, H.TRACK, 220, true)
    local frac
    if cur and max and max > 0 then
        frac = cur / max
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
        -- at the 1280x960 baseline, 2 at 2560x1440), added to the rendered glyph
        -- height so the baseline value size is unchanged.
        local eff = val_scale + H.sy(2) / vh
        vw = vw * eff
        vh = vh * eff
        local ty = y + (bar_h - vh) / 2   -- center the scaled text on the bar
        H.otext(x + w - CFG.VAL_PAD - vw, ty, val, 0xffffff, eff)
    end
end

-- ---- name wrap (<= 2 lines, breaking on spaces) -----------------------------
-- Returns up to two strings. The level suffix is part of the string to wrap, so
-- "(L5)" wraps onto the second line with the tail of a long name.
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
    -- whatever is left (current word run) is line 2; collect the remaining words.
    if #lines == 1 then
        -- rebuild line 2 from where line 1 ended, then clip to width with an ellipsis.
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

-- ---- the Freya verb-button row ----------------------------------------------
-- Lay one square single-letter button per context verb across the top of the
-- target area, wrapping to a new row when the row fills the bar width, and paint
-- each as a small glass button with its letter centred. Rebuilds btn_rects (the
-- hit-test geometry the on_input handler reads) and returns the total vertical
-- space consumed, so the caller pushes the bars down below the row. No verbs ->
-- no row, zero consumed (the bars sit where they always did).
local function draw_buttons(verbs, x, y, maxw)
    btn_rects = {}
    if #verbs == 0 then return 0 end

    -- square edge + gap, grown on a bigger-than-reference screen so the buttons
    -- track the enlarged bars/value font (both 0 at the 1280x960 baseline).
    local bsz = CFG.BTN_SIZE + H.sy(7)
    local gap = CFG.BTN_GAP + H.sx(2)

    local cx, cy = x, y
    for _, v in ipairs(verbs) do
        if cx + bsz > x + maxw and cx > x then
            cx = x
            cy = cy + bsz + gap
        end
        btn_rects[#btn_rects + 1] = { x = cx, y = cy, w = bsz, h = bsz,
                                      op = v.op, no_target = v.no_target,
                                      letter = v.letter, label = v.label }
        cx = cx + bsz + gap
    end

    for _, r in ipairs(btn_rects) do
        local hot = (flash[r.op] or 0) > 0
        H.glass(r.x, r.y, r.w, r.h)
        local top = hot and 0x2f4a6e or 0x223044
        local bot = hot and 0x132338 or 0x0c121b
        enb.draw.rrect_grad(r.x, r.y, r.w, r.h, H.RADIUS, top, bot, 230)
        enb.draw.rrect(r.x, r.y, r.w, r.h, H.RADIUS,
                       hot and H.LINE_HOT or H.LINE, hot and 220 or 150, false)
        -- the single letter, centred. The overlay font is ~14px; on a big screen
        -- the button is larger, so scale the glyph up to fill it.
        local scale = H.smul(1.5)
        local lw, lh = H.measure(r.letter)
        lw, lh = lw * scale, lh * scale
        H.otext(r.x + (r.w - lw) / 2, r.y + (r.h - lh) / 2, r.letter, H.INK, scale)
    end

    -- decay every flash one tick so the click glow fades.
    for id, n in pairs(flash) do
        flash[id] = (n > 1) and (n - 1) or nil
    end

    -- total height = last row's bottom - top, then a gap down to the first bar.
    local rows_bottom = (#btn_rects > 0) and (btn_rects[#btn_rects].y + bsz) or y
    return (rows_bottom - y) + CFG.BTN_ROW_GAP
end

local function draw_target()
    local t = enb.target and enb.target()
    if not t then return end

    local ax, ay, aw = area_rect()
    local bx = ax + CFG.PAD_X
    local bw = aw - CFG.PAD_X * 2 - CFG.BAR_TRIM_R

    -- On a larger-than-1280x960 screen: each bar +6px tall and the on-bar value
    -- font 1.5x (owner ask, target frame only); both 0/1.0 at the reference res.
    local bar_h    = CFG.BAR_H + H.sy(6)
    local val_scale = CFG.VAL_SCALE * H.smul(1.5)

    -- Freya verb buttons across the top, then the bars BELOW them (the row pushes
    -- the bars down by exactly the space it used; no verbs -> no shift).
    local by = ay + CFG.PAD_TOP
    by = by + draw_buttons(read_verbs(t), bx, by, bw)

    -- two stacked bars: hull over shield, no gap.
    draw_bar(bx, by, bw, H.HULL, t.hull, t.hull_max, bar_h, val_scale)
    by = by + bar_h + CFG.BAR_GAP
    draw_bar(bx, by, bw, H.SHIELD, t.shield, t.shield_max, bar_h, val_scale)
    by = by + bar_h

    -- name + " (L<level>)", wrapped to <= 2 lines under the bars.
    local label = t.name or "Target"
    if t.level then label = label .. string.format(" (L%d)", t.level) end
    local lines = wrap_two(label, bw)
    local ny = by + CFG.NAME_GAP
    for _, ln in ipairs(lines) do
        H.otext(bx, ny, ln, H.INK)
        ny = ny + CFG.LINE_H
    end

    -- distance: straight-line world range to the target (|player_wpos - target_wpos|),
    -- printed under the name. Dimmer than the name so it reads as secondary.
    if t.dist then
        H.otext(bx, ny + CFG.DIST_GAP, string.format("Dist %.2fk", t.dist / 1000),
                H.darker(H.INK), nil, CFG.DIST_ALPHA)
    end
end

-- ===========================================================================
-- DRAW  -- only in space (the target frame is an in-space cockpit element).
-- ===========================================================================
enb.on_tick(function()
    local show = H.vis()
    if not show then return end
    draw_target()
end)

-- ===========================================================================
-- INPUT  -- click a verb button to fire that verb. enb.target_action(op[, no_target])
-- builds the target-action command and pushes it through the world manager's sector-
-- server Connection -- byte-for-byte the wire action the native target-action button
-- click performs. A click landing on a button is swallowed so it never falls through to
-- the 3D scene behind it. btn_rects is rebuilt every draw; the geometry the hit-test
-- reads is whatever was last drawn, so draw and input stay in lockstep. (true = swallow.)
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
            if msg == M.LBUTTONDOWN and enb.target_action then
                enb.target_action(r.op, r.no_target)
                flash[r.op] = CFG.FLASH_TICKS
            end
            return true   -- swallow any button event over a verb button
        end
    end
    return false
end, enb.WANT_MOUSE)

enb.log("target_frame.lua loaded")
