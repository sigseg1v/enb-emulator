-- SPDX-License-Identifier: MIT
-- Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
-- License: LICENSES/Freya
--
-- micromenu.lua -- the four bottom-left micro-menu buttons, restored as a Freya
-- glass button strip at the VERY TOP-LEFT of the game screen (owner ask).
--
-- The native bottom-left micro-menu (Inventory / Character Info / Galaxy Map /
-- Options) is a single atomic CHROME panel ("LevelBarsAndMicroMenu") that the
-- hide-ui mod suppresses as a unit -- its four child buttons cannot be unhidden
-- individually (the panel paints its own children), and even when hidden the hit
-- areas stay live. Rather than fight the panel, we draw our OWN four buttons in
-- the top-left and dispatch each through the game's native screen handlers, so a
-- click does exactly what the original button did:
--
--   Inventory       -> enb.pda_switch(0)   (PDA child screen, compact view)
--   Character Info  -> enb.pda_switch(2)   (PDA child screen)
--   Galaxy Map      -> enb.pda_switch(4)   (PDA child screen, opens at sector zoom)
--   Options         -> enb.shell_screen(1) (in-game screen shell -- NOT a PDA child)
--   Chat            -> H.open_chat()       (raise the Freya chat input box)
--
-- The first three ride the PDA controller captured by the PdaCtor/PdaSwitch hooks
-- (live the moment we enter space). Options is the in-game OPTIONS_MAIN screen,
-- shown by the screen shell captured by the ShellApply hook (its per-frame apply
-- pump runs every frame in-game, so the shell `this` is always live). Until a
-- controller is captured its dispatch is a no-op (returns 0); the button stays
-- drawn and simply does nothing that frame rather than erroring.
--
-- Chat is the one nav-bar button we recreate: the native chat frame is hidden and
-- chat.lua draws our own window, so "Chat" raises the Freya input box (H.open_chat,
-- the same thing Enter does). The other native nav-bar buttons (Group / ChatOptions
-- / Help / Emote / Message) each open a distinct native window through a cockpit
-- button dispatch we have NOT located -- the screen shell (enb.shell_screen) is only
-- the ESC/system-menu family (Options + its sub-pages), and the PDA controller only
-- the PDA tabs. Wiring those five faithfully is a per-window RE + new C++ dispatch
-- task, deliberately not done here rather than shipped wired to guessed ids.
--
-- require("micromenu") from init.lua. Registers its own on_tick (draw) and
-- on_input (click-dispatch + mouse-swallow over the strip).

local H = require("freya_hud")

-- ---- the four buttons (left -> right) --------------------------------------
local BUTTONS = {
    { label = "Inventory", act = function() return enb.pda_switch(0) end },
    { label = "Character", act = function() return enb.pda_switch(2) end },
    { label = "Map",       act = function() return enb.pda_switch(4) end },
    { label = "Options",   act = function() return enb.shell_screen(1) end },
    { label = "Chat",      act = function() if H.open_chat then H.open_chat() end end },
}

local CFG = {
    MARGIN_X    = 46,   -- gap from the screen's left edge (inset 40px right, owner ask)
    MARGIN_Y    = 86,   -- gap from the screen's top edge (down 80px, owner ask)
    BTN_H       = 22,   -- button height
    PAD_X       = 11,   -- text inset inside each button
    GAP         = 4,    -- gap between buttons
    FLASH_TICKS = 10,   -- click-feedback glow duration

    TOP         = 0x223044,  -- glass fill gradient top (matches the hotbar slots)
    BOT         = 0x0c121b,
    LIT_TOP     = 0x2f4a6e,   -- hover/flash fill
    LIT_BOT     = 0x132338,
    INK         = 0xcfe0f2,
}

-- per-button live state: flash countdown (click) + hovered (mouse over)
local state = {}
for i = 1, #BUTTONS do state[i] = { flash = 0, hot = false } end

local function point_in(px, py, x, y, w, h)
    return px >= x and px < x + w and py >= y and py < y + h
end

-- Lay the strip out across the top-left, sizing each button to its label. Returns
-- a table with per-button rects {x,y,w,h}.
local function layout()
    local rects = {}
    local x = CFG.MARGIN_X
    local y = CFG.MARGIN_Y
    for i, b in ipairs(BUTTONS) do
        local w = H.measure(b.label) + 2 * CFG.PAD_X
        rects[i] = { x = x, y = y, w = w, h = CFG.BTN_H }
        x = x + w + CFG.GAP
    end
    return rects
end

-- ===========================================================================
-- DRAW
-- ===========================================================================
enb.on_tick(function()
    local show = H.vis()
    if not show then return end
    local rects = layout()
    for i, b in ipairs(BUTTONS) do
        local r = rects[i]
        local lit = state[i].flash > 0 or state[i].hot
        H.glass(r.x, r.y, r.w, r.h)
        enb.draw.rrect_grad(r.x, r.y, r.w, r.h, H.RADIUS,
                            lit and CFG.LIT_TOP or CFG.TOP,
                            lit and CFG.LIT_BOT or CFG.BOT, 230)
        -- gloss sheen across the top
        enb.draw.rrect_grad(r.x, r.y, r.w, math.floor(r.h * 0.46), H.RADIUS,
                            0xffffff, lit and CFG.LIT_TOP or CFG.TOP,
                            lit and 42 or 24)
        enb.draw.rrect(r.x, r.y, r.w, r.h, H.RADIUS,
                       lit and H.LINE_HOT or H.LINE, lit and 220 or 150, false)
        -- label, vertically centred
        local lw = H.measure(b.label)
        H.otext(r.x + math.floor((r.w - lw) / 2),
                r.y + math.floor((CFG.BTN_H - 11) / 2), b.label, CFG.INK)
        if state[i].flash > 0 then state[i].flash = state[i].flash - 1 end
    end
end)

-- ===========================================================================
-- INPUT  (return true = swallow; fail-open handled in C++)
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

    if msg == M.MOUSEMOVE
       or msg == M.LBUTTONDOWN or msg == M.LBUTTONUP or msg == M.LBUTTONDBLCLK then
        local mx, my = mouse_xy(lparam)
        local rects = layout()
        local over = false
        for i = 1, #BUTTONS do
            local r = rects[i]
            local inside = point_in(mx, my, r.x, r.y, r.w, r.h)
            state[i].hot = inside
            if inside then
                over = true
                if msg == M.LBUTTONDOWN then
                    BUTTONS[i].act()
                    state[i].flash = CFG.FLASH_TICKS
                end
            end
        end
        -- swallow any mouse button over the strip so the click never falls
        -- through to whatever sits behind it; let plain moves pass.
        if over and msg ~= M.MOUSEMOVE then return true end
    end

    return false
end, enb.WANT_MOUSE)

enb.log("micromenu.lua loaded")
