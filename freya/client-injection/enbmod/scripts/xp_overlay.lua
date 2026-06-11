-- xp_overlay.lua -- draw "<level>  <pct>%" next to the three XP bars (Combat /
-- Exploration / Trade) in the bottom-left of the HUD.
--
-- require("xp_overlay") from init.lua (or paste this in). It registers its own
-- on_tick and draws every frame.
--
-- DATA SOURCE: enb.self().{combat,explore,trade}_{lvl,pct}. Those read the player
-- object via the calibrated offsets (game.h Offsets). UNTIL you calibrate
-- player_ptr_addr + combat_lvl/combat_pct/... the values are nil and this draws
-- "L?  ?%" -- which is still useful: it proves the overlay path is alive before
-- the numbers are real. See calib.lua + the static anchors XpBars(0x0058c450),
-- LevelText(0x00548d60), RpgLevels(0x0074bfb0) for finding the offsets.

-- ---- geometry (screen/surface pixels). Tune to your resolution. ------------
-- The three bars in the screenshot sit stacked at the very bottom-left. Defaults
-- below target the 1280x768 window in the reference shot; the bars are ~160px
-- wide starting near the left edge, ~12px apart. Text is drawn just past the
-- right end of each bar. Nudge BAR_X/BAR_Y/ROW_DY/TEXT_DX until it lines up.
local CFG = {
    BAR_X    = 30,     -- left edge of the bars
    BAR_TOP  = 731,    -- y of the TOP (Combat) bar
    ROW_DY   = 12,     -- vertical gap between bars
    TEXT_DX  = 168,    -- text x = BAR_X + TEXT_DX (just right of the bar end)
    TEXT_DY  = -4,     -- nudge text up so it centres on the thin bar
}

-- top -> bottom order matches the screenshot: Combat, Exploration, Trade.
local ROWS = {
    { key = "combat",  label = "Combat",  rgb = 0xFF6644 },  -- red  (combat)
    { key = "explore", label = "Explore", rgb = 0x55DD55 },  -- green(exploration)
    { key = "trade",   label = "Trade",   rgb = 0x55AAFF },  -- blue (trade)
}

local function fmt(lvl, pct)
    local l = lvl and tostring(lvl) or "?"
    local p = pct and tostring(pct) or "?"
    return string.format("L%s  %s%%", l, p)
end

enb.on_tick(function()
    local me = enb.self()
    local calibrated = me.base ~= 0
    for i, row in ipairs(ROWS) do
        local y   = CFG.BAR_TOP + (i - 1) * CFG.ROW_DY + CFG.TEXT_DY
        local x   = CFG.BAR_X + CFG.TEXT_DX
        local lvl = me[row.key .. "_lvl"]
        local pct = me[row.key .. "_pct"]
        local rgb = calibrated and row.rgb or 0x888888
        enb.draw.text(x, y, fmt(lvl, pct), rgb)
    end
end)

enb.log("xp_overlay.lua loaded")
