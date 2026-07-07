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
-- DATA: discipline levels come from H.stats() (enb.rpg_level off the RPG manager);
-- the per-discipline xp-bar fill fraction comes from enb.xp_frac(which) -- the live
-- 0..1 value the native bar paints, read off the XpBars controller. Until those
-- resolve the card renders a gray skeleton (letters uncalibrated-gray, "LV --",
-- empty bars) -- still proving the overlay path is alive before the numbers are real.
--
-- VISIBILITY: shown in space and station, hidden on login/charsel/load -- same
-- freya_hud.vis() gate as the rest of the HUD (the discipline card is part of
-- both the space and station HUD; only the hotbar drops in station).

local H = require("freya_hud")

local CFG = {
    X         = 22,    -- left edge (in 8px from the edge; matches the chat box). Up
                       -- follows H.chat_top(), which the raised chat BOTTOM moves.
    W         = 210,   -- card width (widened for the larger letter + badge fonts)
    BOTTOM    = 14,    -- gap from screen bottom (design bottom:14)
    PAD_X     = 8,
    PAD_Y     = 6,
    ROW_H     = 20,    -- row height (raised to fit the enlarged discipline letter)
    ROW_GAP   = 4,
    LETTER_W  = 16,    -- colored C/E/T column (widened for the larger letter)
    BADGE_W   = 46,    -- "LV n" column on the right (widened for the larger badge)
    -- the base overlay font (14px) reads too small on the discipline card; the
    -- owner wants the letters + LV badges larger. These are real FONT SIZES in
    -- pixels -- the overlay bakes a real atlas per size and blits 1:1 (crisp, no
    -- scaling/magnification). LETTER is the C/E/T glyph, BADGE the "LV n" text (a
    -- touch smaller). Both grow on a bigger-than-1280x960 screen via H.smul (0
    -- growth at the reference res), matching target_frame's name / distance text.
    LETTER_PX = 18,
    BADGE_PX  = 13,  -- "LV n" badge font size
    TEXT_SMUL = 1.4, -- big-screen growth factor (1.0 at ref res)
    TEXT_DY   = 3,   -- raise both the C/E/T letter AND the "LV n" badge this many px
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

    local me = enb.self()
    -- Discipline levels come live from the controller->data chain (H.stats), so
    -- the card works with no flat-struct calibration; fall back to the flat self()
    -- fields, then the gray skeleton. (Per-discipline xp% is not pinned yet -- the
    -- one xp pair we have feeds the overall card -- so the bars stay empty.)
    local st = (H.stats and H.stats()) or {}
    local cal = (st.combat or st.explore or st.trade) ~= nil or me.base ~= 0

    local dc_h = CFG.PAD_Y * 2 + #ROWS * CFG.ROW_H + (#ROWS - 1) * CFG.ROW_GAP
    -- on a bigger-than-1280x960 screen slide the disc card RIGHT 32px, the same
    -- shift chat.lua applies to the chat box (0 at the reference res).
    local x = CFG.X + H.sx(32)
    -- raised ABOVE the Freya chat box (bottom-left): the card's bottom edge sits
    -- at H.chat_top(), so its top is that minus its own height. On a bigger-than-
    -- 1280x960 screen the owner lifts this "player frame" 20px more (0 at the
    -- reference res). chat.lua applies the chat's own move separately, so the two
    -- offsets do not compound here.
    local y = H.chat_top() - dc_h - H.sy(20)
    H.glass(x, y, CFG.W, dc_h)

    local bar_x = x + CFG.PAD_X + CFG.LETTER_W + 4
    local bar_w = CFG.W - CFG.PAD_X * 2 - CFG.LETTER_W - 4 - CFG.BADGE_W - 4
    local ry = y + CFG.PAD_Y
    for _, row in ipairs(ROWS) do
        local lvl = st[row.key] or me[row.key .. "_lvl"]
        -- per-discipline xp fill: live 0..1 fraction off the XpBars controller,
        -- shown as a percent. Falls back to a flat-struct pct if ever calibrated.
        local frac = enb.xp_frac and enb.xp_frac(row.key)
        local pct = frac and frac * 100 or me[row.key .. "_pct"]
        local letter_px = math.floor(CFG.LETTER_PX * H.smul(CFG.TEXT_SMUL) + 0.5)
        local badge_px  = math.floor(CFG.BADGE_PX * H.smul(CFG.TEXT_SMUL) + 0.5)
        -- vertically center each glyph on the row at its own rendered height,
        -- then raise the whole row's text a few px to the owner's taste.
        local text_y = ry + math.floor((CFG.ROW_H - letter_px) / 2) - CFG.TEXT_DY

        -- colored discipline letter
        H.otext_px(x + CFG.PAD_X, text_y, row.letter, cal and row.rgb or H.UNCAL, letter_px)

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

        -- "LV n" badge, right-aligned, a touch smaller than the discipline letter
        local badge = "LV " .. (cal and lvl and tostring(lvl) or "--")
        local bw = H.measure_px(badge, badge_px)
        H.otext_px(x + CFG.W - CFG.PAD_X - bw - 8, text_y + 3, badge,
                   cal and H.INK_DIM or H.UNCAL, badge_px)

        ry = ry + CFG.ROW_H + CFG.ROW_GAP
    end
end)

enb.log("xp_overlay.lua loaded")
