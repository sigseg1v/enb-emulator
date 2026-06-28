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
    PAD_TOP  = 7,     -- inset of the first bar from the area's top edge (owner: +4px down)
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
    local x = sw - CFG.MARGIN_R - CFG.AREA_W
    local y = sh - CFG.MARGIN_B - CFG.AREA_H
    return x, y, CFG.AREA_W, CFG.AREA_H
end

-- ---- one bar: track + gradient fill + border + cur/total text ---------------
-- Mirrors draw_player_card's vital bar, scaled to BAR_H. cur/max may be nil
-- (unknown current, e.g. a station's hull) -> empty track, no value text.
local function draw_bar(x, y, w, rgb, cur, max)
    enb.draw.rrect(x, y, w, CFG.BAR_H, H.RADIUS, H.TRACK, 220, true)
    local frac
    if cur and max and max > 0 then
        frac = cur / max
        if frac < 0 then frac = 0 elseif frac > 1 then frac = 1 end
        local fw = math.floor(w * frac)
        if fw > 0 then
            enb.draw.rrect_grad(x, y, fw, CFG.BAR_H, H.RADIUS, rgb, H.darker(rgb), 255)
        end
    end
    enb.draw.rrect(x, y, w, CFG.BAR_H, H.RADIUS, H.LINE, 70, false)
    if cur and max then
        local val = string.format("%d / %d", math.floor(cur + 0.5), math.floor(max + 0.5))
        local vw, vh = H.measure(val)
        vw = vw * CFG.VAL_SCALE
        vh = vh * CFG.VAL_SCALE
        local ty = y + (CFG.BAR_H - vh) / 2   -- center the scaled text on the bar
        H.otext(x + w - CFG.VAL_PAD - vw, ty, val, 0xffffff, CFG.VAL_SCALE)
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

local function draw_target()
    local t = enb.target and enb.target()
    if not t then return end

    local ax, ay, aw = area_rect()
    local bx = ax + CFG.PAD_X
    local bw = aw - CFG.PAD_X * 2 - CFG.BAR_TRIM_R

    -- two stacked bars at the top: hull over shield, no gap.
    local by = ay + CFG.PAD_TOP
    draw_bar(bx, by, bw, H.HULL, t.hull, t.hull_max)
    by = by + CFG.BAR_H + CFG.BAR_GAP
    draw_bar(bx, by, bw, H.SHIELD, t.shield, t.shield_max)
    by = by + CFG.BAR_H

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
