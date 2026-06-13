-- SPDX-License-Identifier: MIT
-- Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
-- License: LICENSES/Freya
--
-- xp_overlay.lua -- the discipline card of the Freya HUD (bottom-left).
--
-- require("xp_overlay") from init.lua. Registers its own on_tick.
--
-- Ports the design's DiscCard: a titleless glass panel anchored bottom-left
-- with three rows -- Combat / Exploration / Trade. Each row is a colored letter
-- (C / E / T in the discipline color), a thin xp-progress bar (fill width =
-- xp%), and an "LV n" badge on the right.
--
-- DATA: enb.self().{combat,explore,trade}_{lvl,pct}. Until the player offsets
-- are calibrated those are nil and the card renders a gray skeleton (letters
-- uncalibrated-gray, "LV --", empty bars) -- still proves the overlay path is
-- alive before the numbers are real.
--
-- VISIBILITY: shown in space and station, hidden on login/charsel/load -- same
-- freya_hud.vis() gate as the rest of the HUD (the discipline card is part of
-- both the space and station HUD; only the hotbar drops in station).

local H = require("freya_hud")

local CFG = {
    X         = 14,    -- left edge (design left:14)
    W         = 196,   -- card width (design)
    BOTTOM    = 14,    -- gap from screen bottom (design bottom:14)
    PAD_X     = 8,
    PAD_Y     = 6,
    ROW_H     = 18,
    ROW_GAP   = 4,
    LETTER_W  = 14,    -- colored C/E/T column
    BADGE_W   = 40,    -- "LV n" column on the right
    BAR_H     = 8,     -- xp bar height (design disc__bar)
}

-- top -> bottom: Combat, Exploration, Trade.
local ROWS = {
    { key = "combat",  letter = "C", rgb = H.COMBAT },
    { key = "explore", letter = "E", rgb = H.EXPLORE },
    { key = "trade",   letter = "T", rgb = H.TRADE },
}

enb.on_tick(function()
    if not H.vis() then return end
    local _, sh = enb.screen()
    if sh == 0 then sh = 992 end

    local me = enb.self()
    local cal = me.base ~= 0

    local dc_h = CFG.PAD_Y * 2 + #ROWS * CFG.ROW_H + (#ROWS - 1) * CFG.ROW_GAP
    local x = CFG.X
    local y = sh - CFG.BOTTOM - dc_h
    H.glass(x, y, CFG.W, dc_h)

    local bar_x = x + CFG.PAD_X + CFG.LETTER_W + 4
    local bar_w = CFG.W - CFG.PAD_X * 2 - CFG.LETTER_W - 4 - CFG.BADGE_W - 4
    local ry = y + CFG.PAD_Y
    for _, row in ipairs(ROWS) do
        local lvl = me[row.key .. "_lvl"]
        local pct = me[row.key .. "_pct"]
        local text_y = ry + math.floor((CFG.ROW_H - 14) / 2)

        -- colored discipline letter
        H.otext(x + CFG.PAD_X, text_y, row.letter, cal and row.rgb or H.UNCAL)

        -- xp bar, vertically centered in the row
        local by = ry + math.floor((CFG.ROW_H - CFG.BAR_H) / 2)
        enb.draw.rrect(bar_x, by, bar_w, CFG.BAR_H, H.RADIUS, H.TRACK, 220, true)
        if cal and pct and pct > 0 then
            local fw = math.floor(bar_w * math.min(pct, 100) / 100)
            if fw > 0 then
                enb.draw.rrect_grad(bar_x, by, fw, CFG.BAR_H, H.RADIUS,
                                    row.rgb, H.darker(row.rgb), 255)
            end
        end
        enb.draw.rrect(bar_x, by, bar_w, CFG.BAR_H, H.RADIUS, H.LINE, 70, false)

        -- "LV n" badge, right-aligned
        local badge = "LV " .. (cal and lvl and tostring(lvl) or "--")
        local bw = H.measure(badge)
        H.otext(x + CFG.W - CFG.PAD_X - bw, text_y, badge, cal and H.INK_DIM or H.UNCAL)

        ry = ry + CFG.ROW_H + CFG.ROW_GAP
    end
end)

enb.log("xp_overlay.lua loaded")
