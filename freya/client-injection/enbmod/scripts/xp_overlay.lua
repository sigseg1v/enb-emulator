-- xp_overlay.lua -- draw "<level> <pct>%" to the LEFT of the three XP bars
-- (Combat / Exploration / Trade) in the bottom-left of the HUD.
--
-- require("xp_overlay") from init.lua. It registers its own on_tick and draws
-- every frame, screen-anchored from the bottom-left via enb.screen() so it
-- tracks any window size.
--
-- DATA SOURCE: enb.self().{combat,explore,trade}_{lvl,pct}, which read the
-- player object via the calibrated offsets (game.h Offsets). UNTIL you
-- calibrate player_ptr_addr + combat_lvl/combat_pct/... the values are nil and
-- this draws "L? ?%" in gray -- still useful: it proves the overlay path is
-- alive before the numbers are real. See calib.lua + the static anchors
-- XpBars(0x0058c450), LevelText(0x00548d60), RpgLevels(0x0074bfb0).

-- ---- geometry, anchored from the bottom-left (1280x992 reference) ----------
-- The three native XP bars sit stacked at the very bottom-left: they start at
-- x~27, are ~165px wide, ~12px tall, ~20px apart, the bottom one ~17px off the
-- screen bottom. The label is drawn just LEFT of each bar, right-aligned so it
-- ends a fixed pad before the bar's left edge.
local CFG = {
    BAR_X       = 27,    -- left edge of the bars
    BAR_W       = 165,   -- bar width (label right-aligns to BAR_X)
    BAR_H       = 12,    -- bar height (for vertical centering of the label)
    ROW_DY      = 20,    -- vertical pitch between bars
    BOTTOM_PAD  = 17,    -- gap from screen bottom to the BOTTOM bar's top
    LABEL_PAD   = 8,     -- gap between the label's right edge and BAR_X
}

-- top -> bottom order matches the HUD: Combat, Exploration, Trade.
local ROWS = {
    { key = "combat",  rgb = 0xFF6644 },  -- red   (combat)
    { key = "explore", rgb = 0x55DD55 },  -- green (exploration)
    { key = "trade",   rgb = 0x55AAFF },  -- blue  (trade)
}

local function fmt(lvl, pct)
    return string.format("L%s %s%%", lvl and tostring(lvl) or "?",
                                     pct and tostring(pct) or "?")
end

enb.on_tick(function()
    local sw, sh = enb.screen()
    if sh == 0 then sh = 992 end  -- pre-first-present fallback

    local me = enb.self()
    local calibrated = me.base ~= 0
    -- bottom bar's top y; rows stack upward from there
    local y_bottom = sh - CFG.BOTTOM_PAD - CFG.BAR_H
    for i, row in ipairs(ROWS) do
        local y   = y_bottom - (#ROWS - i) * CFG.ROW_DY
        local lvl = me[row.key .. "_lvl"]
        local pct = me[row.key .. "_pct"]
        local s   = fmt(lvl, pct)
        local lw, lh = enb.measure(s)
        if lw == 0 then lw = #s * 7; lh = 14 end
        -- right-aligned to the bar start; the screen edge is only ~19px away,
        -- so a real label cannot fully fit LEFT of the bar -- clamp to x=2 and
        -- let it overlap the bar's left portion (value-on-the-bar style)
        -- rather than clip off-screen.
        local x   = math.max(2, CFG.BAR_X - CFG.LABEL_PAD - lw)
        local ty  = y + math.floor((CFG.BAR_H - lh) / 2)
        enb.draw.text(x, ty, s, calibrated and row.rgb or 0x888888)
    end
end)

enb.log("xp_overlay.lua loaded")
