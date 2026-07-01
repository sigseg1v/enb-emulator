-- SPDX-License-Identifier: MIT
-- Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
-- License: LICENSES/Freya
--
-- party_frame.lua -- the Freya redesign of the PARTY / group member frame.
--
-- The native group window showed up to 5 party members as a stacked column, each
-- row a member name over a red hull bar and a blue shield bar. The Phase AS HUD
-- overhaul dropped it; this module repaints it in the Freya glass style, anchored
-- to the RIGHT edge and stacked DOWNWARD (radar sits above it, the target frame
-- below it, so the right-middle band is free).
--
-- Data comes from enb.group() (the crash-safe, VEH-guarded roster read in lua_api):
-- { count = N, members = { { name=, gameid=, [hull=, hull_max=, shield=, shield_max=] }, ... } }.
-- Name + GameID are read straight off the GroupInfo member objects; hull/shield are
-- resolved by looking each member's GameID up in the world entity table and reading its
-- aux bag (same source as the target frame). That only works for members in the current
-- sector / scanner range -- a member elsewhere has no live entity, so the roster omits
-- its vitals and its bars render as empty tracks (the convention target_frame uses for a
-- vitals-less target). The panel only draws while in a group (count > 0); solo players
-- see nothing.

local H = require("freya_hud")

-- ---- geometry (window-relative; anchored to the right edge, stacked down) -----
local CFG = {
    PANEL_W  = 188,   -- panel / row content width at the reference res
    MARGIN_R = 28,    -- gap from the right screen edge (inset 16px past the base 12)
    MARGIN_T = 228,   -- gap from the top screen edge to the panel top (below the radar)
    -- on a larger-than-1280x960 screen pull the panel inward from the right and down
    -- from the top, matching how the rest of the HUD scales its border gaps (both 0 at
    -- the reference res, so the base layout is untouched).
    MARGIN_R_TUNE = 24,
    MARGIN_T_TUNE = 40,

    PAD      = 7,      -- inner padding inside the glass panel
    ROW_GAP  = 8,      -- gap between two member rows
    NAME_H   = 18,     -- member-name line height (grown to fit the bigger name font)
    NAME_SCALE = 1.15, -- member-name font scale (bigger than the 1.0 default)
    NAME_GAP = 3,      -- gap from the name down to the hull bar
    BAR_H    = 12,     -- vital bar height (taller than target_frame's thin 6px bars)
    BAR_GAP  = 1,      -- 1px gap between the stacked hull / shield bars
    VAL_PAD  = 4,      -- inset of the cur/total value text from the bar's right edge
    VAL_SCALE = 0.78,  -- value-text scale (bigger; the taller bar has room for it)
}

-- ---- one bar: track + gradient fill + border + optional cur/total text -------
-- Mirrors target_frame.draw_bar. cur/max may be nil (vitals unknown for a remote
-- member) -> empty track, no value text.
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
        local eff = val_scale + (H.sy(2) + 2) / vh
        vw, vh = vw * eff, vh * eff
        local ty = y + (bar_h - vh) / 2
        H.otext(x + w - CFG.VAL_PAD - vw, ty, val, 0xffffff, eff)
    end
end

-- one member row, top edge at `y`, returns the y just below the row.
local function draw_member(m, x, y, w, bar_h, val_scale)
    local name = (type(m.name) == "string" and m.name ~= "") and m.name or "Member"
    -- clip an over-long name to the panel width (measured at the drawn scale).
    while H.measure(name) * CFG.NAME_SCALE > w and #name > 1 do name = name:sub(1, #name - 1) end
    H.otext(x, y, name, H.INK, CFG.NAME_SCALE)
    local hull_y   = y + CFG.NAME_H + CFG.NAME_GAP
    local shield_y = hull_y + bar_h + CFG.BAR_GAP
    draw_bar(x, hull_y,   w, H.HULL,   m.hull,   m.hull_max,   bar_h, val_scale)
    draw_bar(x, shield_y, w, H.SHIELD, m.shield, m.shield_max, bar_h, val_scale)
    return shield_y + bar_h
end

local function draw_party()
    local g = enb.group and enb.group()
    if not g or not g.count or g.count <= 0 then return end
    local members = g.members or {}
    if #members == 0 then return end

    local sw, sh = enb.screen()
    if not sw or sw == 0 then sw, sh = 1280, 960 end

    local bar_h     = CFG.BAR_H + H.sy(6)
    local val_scale = CFG.VAL_SCALE * H.smul(1.5)
    local w = CFG.PANEL_W + H.sx(CFG.PANEL_W * 0.5)   -- widen on a big screen

    local row_h = CFG.NAME_H + CFG.NAME_GAP + bar_h * 2 + CFG.BAR_GAP
    local body_h = #members * row_h + (#members - 1) * CFG.ROW_GAP
    local pad = CFG.PAD
    local panel_w = w + pad * 2
    local panel_h = body_h + pad * 2

    local px = sw - (CFG.MARGIN_R + H.sx(CFG.MARGIN_R_TUNE)) - panel_w
    local py = CFG.MARGIN_T + H.sy(CFG.MARGIN_T_TUNE)

    H.glass(px, py, panel_w, panel_h)

    local x = px + pad
    local y = py + pad
    for _, m in ipairs(members) do
        y = draw_member(m, x, y, w, bar_h, val_scale) + CFG.ROW_GAP
    end
end

-- ===========================================================================
-- DRAW -- only while the HUD is visible AND the player is in a group.
-- ===========================================================================
enb.on_tick(function()
    if not H.vis() then return end
    draw_party()
end)

enb.log("party_frame.lua loaded")
