-- freya_ui.lua -- replacement bottom-center HUD (Phase AS).
--
-- require("freya_ui") from init.lua. Registers its own on_tick (draw) and
-- on_input (Tier A "cover + swallow").
--
-- WHAT IT DOES
--   * Draws an opaque panel over the native bottom-center cluster (the six
--     circular hotkey buttons + the circular warp control + the native stat
--     bars) and SWALLOWS mouse input landing on that panel, so the hidden
--     native widgets are unclickable. This removes them WITHOUT reversing the
--     client's widget code (Tier A). Tier B (a true per-widget hide) is AS-6.
--   * Draws a 12-button action bar (1 2 3 4 5 6 7 8 9 0 - =) of square shiny
--     rounded buttons. Click a button -> taps the matching key (enb.tap), so
--     it triggers whatever that key is bound to. A physical keypress lights the
--     same button. Key messages are NOT swallowed -- the game still needs them.
--   * Draws three equal-length stacked stat bars (hull / shield / energy) fed
--     by enb.self(); until the offsets are calibrated (AS-3) they render as a
--     gray skeleton, same convention as xp_overlay.
--
-- ALL geometry derives from enb.screen() so it bottom-anchors at any resolution.
-- The pixel constants are tuned for the owner's 1280x992 window; they will need
-- an in-game nudge against the real client (that pass is the AS plan's CV gate).

-- ---- virtual-key codes for the twelve action-bar keys ----------------------
local KEYS = {
    { label = "1", vk = 0x31 }, { label = "2", vk = 0x32 }, { label = "3", vk = 0x33 },
    { label = "4", vk = 0x34 }, { label = "5", vk = 0x35 }, { label = "6", vk = 0x36 },
    { label = "7", vk = 0x37 }, { label = "8", vk = 0x38 }, { label = "9", vk = 0x39 },
    { label = "0", vk = 0x30 }, { label = "-", vk = 0xBD }, { label = "=", vk = 0xBB },
}
-- reverse map vk -> index, so a physical keypress lights its button
local VK_TO_IDX = {}
for i, k in ipairs(KEYS) do VK_TO_IDX[k.vk] = i end

-- ---- stat bars (top -> bottom) ---------------------------------------------
local STATS = {
    { key = "hull",   label = "HULL",   rgb = 0x33DD55 },  -- green
    { key = "shield", label = "SHIELD", rgb = 0x3399FF },  -- blue
    { key = "energy", label = "ENERGY", rgb = 0xFF4444 },  -- red
}

local CFG = {
    BTN            = 44,    -- action-button square edge
    GAP            = 6,     -- gap between buttons
    BTN_RADIUS     = 7,     -- rounded-corner radius
    BOTTOM_MARGIN  = 14,    -- gap from screen bottom to the button row

    PANEL_W_FRAC   = 0.62,  -- cover panel width as a fraction of the screen
    PANEL_RADIUS   = 12,
    PANEL_ALPHA    = 236,   -- near-opaque so the native cluster never shows

    STAT_W         = 230,   -- stat-bar track width (all three equal)
    STAT_H         = 12,
    STAT_GAP       = 6,     -- vertical gap between stat bars
    STAT_LABEL_PAD = 8,     -- gap between a stat label and its bar

    FLASH_TICKS    = 12,    -- click-feedback highlight duration (ticks)

    -- colors
    PANEL_TOP      = 0x202832,
    PANEL_BOT      = 0x10151c,
    BTN_TOP        = 0x3a4654,
    BTN_BOT        = 0x1c232c,
    BTN_LIT_TOP    = 0x6fa8ff,
    BTN_LIT_BOT    = 0x2c4870,
    BTN_BORDER     = 0x55657a,
    BTN_LABEL      = 0xE8EEF6,
    TRACK          = 0x0c1014,
    UNCAL          = 0x666f7a,
}

-- per-button live state: held (physical key down) + flash (click countdown)
local state = {}
for i = 1, #KEYS do state[i] = { held = false, flash = 0 } end

-- ---- layout: recomputed each frame from the current screen size ------------
local function layout()
    local sw, sh = enb.screen()
    if sw == 0 or sh == 0 then sw, sh = 1280, 992 end  -- pre-first-present fallback

    local bar_w = #KEYS * CFG.BTN + (#KEYS - 1) * CFG.GAP
    local bar_x = math.floor((sw - bar_w) / 2)
    local bar_y = sh - CFG.BTN - CFG.BOTTOM_MARGIN

    -- stat block sits just above the button row
    local stat_block_h = #STATS * CFG.STAT_H + (#STATS - 1) * CFG.STAT_GAP
    local stat_top = bar_y - 12 - stat_block_h

    local panel_w = math.floor(sw * CFG.PANEL_W_FRAC)
    local panel_x = math.floor((sw - panel_w) / 2)
    local panel_y = stat_top - 12
    local panel_h = sh - panel_y

    return {
        sw = sw, sh = sh,
        bar_x = bar_x, bar_y = bar_y, bar_w = bar_w,
        stat_top = stat_top,
        panel = { x = panel_x, y = panel_y, w = panel_w, h = panel_h },
    }
end

-- button i screen rect
local function btn_rect(L, i)
    local x = L.bar_x + (i - 1) * (CFG.BTN + CFG.GAP)
    return x, L.bar_y, CFG.BTN, CFG.BTN
end

local function point_in(px, py, x, y, w, h)
    return px >= x and px < x + w and py >= y and py < y + h
end

-- ---- text centering (enb.measure falls back to an estimate pre-atlas) ------
local function text_centered(s, cx, cy, rgb)
    local w, h = enb.measure(s)
    if w == 0 then w = #s * 7; h = 14 end
    enb.draw.text(math.floor(cx - w / 2), math.floor(cy - h / 2), s, rgb)
end

-- ===========================================================================
-- DRAW
-- ===========================================================================
enb.on_tick(function()
    local L = layout()
    local p = L.panel

    -- 1) cover panel over the native cluster (Tier A) -- near-opaque so the
    --    hidden circles/warp/bars never bleed through.
    enb.draw.rrect_grad(p.x, p.y, p.w, p.h, CFG.PANEL_RADIUS,
                        CFG.PANEL_TOP, CFG.PANEL_BOT, CFG.PANEL_ALPHA)

    -- 2) stat bars: three equal-length stacked bars. Labels to the LEFT.
    local me = enb.self()
    local calibrated = me.base ~= 0
    local bar_x = p.x + math.floor((p.w - CFG.STAT_W) / 2) + 40  -- leave room for labels
    for i, s in ipairs(STATS) do
        local y = L.stat_top + (i - 1) * (CFG.STAT_H + CFG.STAT_GAP)
        -- label, right-aligned against the bar's left edge
        local lw, lh = enb.measure(s.label)
        if lw == 0 then lw = #s.label * 7; lh = 14 end
        local label_rgb = calibrated and s.rgb or CFG.UNCAL
        enb.draw.text(bar_x - CFG.STAT_LABEL_PAD - lw,
                      y + math.floor((CFG.STAT_H - lh) / 2), s.label, label_rgb)
        -- track
        enb.draw.rrect(bar_x, y, CFG.STAT_W, CFG.STAT_H, 4, CFG.TRACK, 255, true)
        -- fill
        local cur = me[s.key]
        local max = me[s.key .. "_max"]
        if calibrated and cur and max and max > 0 then
            local frac = cur / max
            if frac < 0 then frac = 0 elseif frac > 1 then frac = 1 end
            local fw = math.floor(CFG.STAT_W * frac)
            if fw > 0 then
                enb.draw.rrect_grad(bar_x, y, fw, CFG.STAT_H, 4,
                                    s.rgb, (s.rgb >> 1) & 0x7f7f7f, 255)
            end
        end
        -- thin border
        enb.draw.rrect(bar_x, y, CFG.STAT_W, CFG.STAT_H, 4, CFG.BTN_BORDER, 180, false)
    end

    -- 3) action bar: 12 square shiny rounded buttons
    for i, k in ipairs(KEYS) do
        local x, y, w, h = btn_rect(L, i)
        local lit = state[i].held or state[i].flash > 0
        local top = lit and CFG.BTN_LIT_TOP or CFG.BTN_TOP
        local bot = lit and CFG.BTN_LIT_BOT or CFG.BTN_BOT
        enb.draw.rrect_grad(x, y, w, h, CFG.BTN_RADIUS, top, bot, 250)
        enb.draw.rrect(x, y, w, h, CFG.BTN_RADIUS, CFG.BTN_BORDER, 220, false)
        text_centered(k.label, x + w / 2, y + h / 2, CFG.BTN_LABEL)
        if state[i].flash > 0 then state[i].flash = state[i].flash - 1 end
    end
end)

-- ===========================================================================
-- INPUT  (return true = swallow; fail-open is handled in C++)
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

    -- physical key down/up: light the matching button. Do NOT swallow -- the
    -- game still needs the key for the actual ability bind.
    if msg == M.KEYDOWN or msg == M.SYSKEYDOWN then
        local i = VK_TO_IDX[wparam]
        if i then state[i].held = true end
        return false
    elseif msg == M.KEYUP or msg == M.SYSKEYUP then
        local i = VK_TO_IDX[wparam]
        if i then state[i].held = false end
        return false
    end

    -- mouse: act on / swallow within our UI region
    if msg == M.MOUSEMOVE or msg == M.MOUSEWHEEL
       or msg == M.LBUTTONDOWN or msg == M.LBUTTONUP or msg == M.LBUTTONDBLCLK
       or msg == M.RBUTTONDOWN or msg == M.RBUTTONUP or msg == M.RBUTTONDBLCLK
       or msg == M.MBUTTONDOWN or msg == M.MBUTTONUP or msg == M.MBUTTONDBLCLK then
        local mx, my = mouse_xy(lparam)
        local L = layout()

        -- left-click on an action button -> tap its key + flash
        if msg == M.LBUTTONDOWN then
            for i = 1, #KEYS do
                local x, y, w, h = btn_rect(L, i)
                if point_in(mx, my, x, y, w, h) then
                    enb.tap(KEYS[i].vk)
                    state[i].flash = CFG.FLASH_TICKS
                    return true
                end
            end
        end

        -- any mouse event inside the cover panel -> swallow, so clicks/hover
        -- never reach the hidden native widgets underneath.
        local p = L.panel
        if point_in(mx, my, p.x, p.y, p.w, p.h) then
            return true
        end
    end

    return false
end, enb.WANT_KEY | enb.WANT_MOUSE)

enb.log("freya_ui.lua loaded")
