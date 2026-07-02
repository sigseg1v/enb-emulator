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
--   Help            -> cockpit(0x10)       (native Online Manual window)
--   Message         -> cockpit(4)          (native mission/dialog popup; conditional)
--
-- The first three ride the PDA controller captured by the PdaCtor/PdaSwitch hooks
-- (live the moment we enter space). Options is the in-game OPTIONS_MAIN screen,
-- shown by the screen shell captured by the ShellApply hook (its per-frame apply
-- pump runs every frame in-game, so the shell `this` is always live). Until a
-- controller is captured its dispatch is a no-op (returns 0); the button stays
-- drawn and simply does nothing that frame rather than erroring.
--
-- Chat is the one nav-bar button we recreate as Freya UI: the native chat frame is
-- hidden and chat.lua draws our own window, so "Chat" raises the Freya input box
-- (H.open_chat, the same thing Enter does).
--
-- Help and Message both route through the native chat/cockpit nav-bar dispatcher
-- (see cockpit() below). Command 0x10 (Help) builds the native Online Manual
-- window, which renders fine on its own. Command 4 (Message) runs the native
-- mission-message consume path (0x00750800): it pops the next pending entry off
-- the chat uiRoot's confirmed-message list and opens that message's dialog -- the
-- exact path the native Message button fires. This one is CONDITIONAL and load-
-- bearing for quests: some missions only advance when the player opens the pending
-- message dialog. The native code raises a per-message flag at controller+0x264
-- (set by 0x0056baa0 when a message arrives, cleared on consume); we read that
-- same flag each frame, so the Freya Message button is drawn ONLY while a message
-- is pending and GLOWS (pulsing amber) the whole time it is up, then disappears
-- once clicked/consumed -- mirroring the native lit indicator.
--
-- The remaining native nav-bar buttons (Group / chat-Options / Emote) are NOT
-- recreated here: each is a native combo-box DROPDOWN whose menu is painted by the
-- button gadget itself on a real gadget click and anchored to the gadget's own
-- (bottom-of-screen) screen rect. The dispatcher only handles the post-pick
-- command/selection -- it sets the controller's active-popup pointer but never
-- paints the dropdown -- so calling it does nothing visible. Reproducing those
-- three as top-left Freya buttons needs them rebuilt as Freya glass menus (one
-- menu-item -> native command id each), a per-item task tracked separately; they
-- are intentionally omitted rather than shipped wired to a call that no-ops.
--
-- require("micromenu") from init.lua. Registers its own on_tick (draw) and
-- on_input (click-dispatch + mouse-swallow over the strip).

local H = require("freya_hud")

-- The native chat/cockpit nav-bar buttons route through one dispatcher,
-- __thiscall(chatController, command_id). The controller hangs off the captured
-- chat uiRoot at +0x127c. Until the chat uiRoot is captured (set by the chat
-- hooks once in-game) this is a no-op rather than an error.
local DISPATCH = 0x00563280
local CHAT_CTRL = 0x127c   -- chat uiRoot -> chat/cockpit controller
local MSG_FLAG  = 0x264    -- controller -> "message pending" byte (set/cleared natively)

-- the chat/cockpit controller, or 0 until the chat uiRoot is captured in-game.
local function controller()
    local root = enb.chat_panel and enb.chat_panel() or 0
    if not root or root == 0 then return 0 end
    local ctrl = enb.mem.u32(root + CHAT_CTRL)
    return (ctrl and ctrl ~= 0) and ctrl or 0
end

local function cockpit(cmd)
    local ctrl = controller()
    if ctrl == 0 then return end
    return enb.call(DISPATCH, ctrl, cmd)
end

-- True while a mission/dialog message is pending (the native lit condition). The
-- client raises controller+0x264 when a message arrives and clears it on consume.
local function msg_pending()
    local ctrl = controller()
    if ctrl == 0 then return false end
    return (enb.mem.u32(ctrl + MSG_FLAG) & 0xff) ~= 0
end

-- ---- the strip buttons (left -> right) -------------------------------------
-- `cond` (optional) gates whether a button is drawn/hit this frame; `glow` makes
-- it pulse amber the whole time it is visible.
local BUTTONS = {
    { label = "Inventory", act = function() return enb.pda_switch(0) end },
    { label = "Character", act = function() return enb.pda_switch(2) end },
    { label = "Map",       act = function() return enb.pda_switch(4) end },
    { label = "Options",   act = function() return enb.shell_screen(1) end },
    { label = "Chat",      act = function() if H.open_chat then H.open_chat() end end },
    { label = "Help",      act = function() return cockpit(0x10) end },
    { label = "Message",   glow = true, cond = msg_pending,
                           act = function() return cockpit(4) end },
}

local CFG = {
    MARGIN_X    = 22,   -- gap from the screen's left edge (top-left, inset in the dark border)
    MARGIN_Y    = 24,   -- gap from the screen's top edge (top-left, inset in the dark border)
    BTN_H       = 22,   -- button height
    PAD_X       = 11,   -- text inset inside each button
    GAP         = 4,    -- gap between buttons
    FLASH_TICKS = 10,   -- click-feedback glow duration

    TOP         = 0x223044,  -- glass fill gradient top (matches the hotbar slots)
    BOT         = 0x0c121b,
    LIT_TOP     = 0x2f4a6e,   -- hover/flash fill
    LIT_BOT     = 0x132338,
    INK         = 0xcfe0f2,

    GLOW_TOP    = 0x4a3914,   -- amber fill for a glowing (pending) button
    GLOW_BOT    = 0x241a06,
    GLOW_LINE   = 0xffcc44,   -- amber border, alpha pulses
    GLOW_INK    = 0xffe9b0,   -- warm label on a glowing button
}

-- per-button live state: flash countdown (click) + hovered (mouse over)
local state = {}
for i = 1, #BUTTONS do state[i] = { flash = 0, hot = false } end
local frame = 0   -- monotonic tick counter, drives the glow pulse

local function point_in(px, py, x, y, w, h)
    return px >= x and px < x + w and py >= y and py < y + h
end

-- Lay the strip out across the top-left, sizing each button to its label. Only
-- buttons whose `cond` passes (or have none) are placed, so a hidden conditional
-- button leaves no gap. Returns an array of { idx, x, y, w, h } left -> right.
local function layout()
    local out = {}
    -- on a bigger-than-1280x960 screen the owner slides the whole strip 32px
    -- right and 16px down (0 at the reference res). Applied to the strip origin
    -- here so draw and hit-test stay in lockstep.
    local x = CFG.MARGIN_X + H.sx(32)
    local y = CFG.MARGIN_Y + H.sy(16)
    for i, b in ipairs(BUTTONS) do
        if (not b.cond) or b.cond() then
            local w = H.measure(b.label) + 2 * CFG.PAD_X
            out[#out + 1] = { idx = i, x = x, y = y, w = w, h = CFG.BTN_H }
            x = x + w + CFG.GAP
        end
    end
    return out
end

-- ===========================================================================
-- DRAW
-- ===========================================================================
enb.on_tick(function()
    if not H.vis() then return end
    frame = frame + 1
    for _, e in ipairs(layout()) do
        local b = BUTTONS[e.idx]
        local s = state[e.idx]
        local glowing = b.glow == true
        local hot = s.flash > 0 or s.hot
        local lit = hot or glowing

        local top, bot
        if glowing then        top, bot = CFG.GLOW_TOP, CFG.GLOW_BOT
        elseif lit then        top, bot = CFG.LIT_TOP, CFG.LIT_BOT
        else                   top, bot = CFG.TOP, CFG.BOT end

        H.glass(e.x, e.y, e.w, e.h)
        enb.draw.rrect_grad(e.x, e.y, e.w, e.h, H.RADIUS, top, bot, 230)
        -- gloss sheen across the top
        enb.draw.rrect_grad(e.x, e.y, e.w, math.floor(e.h * 0.46), H.RADIUS,
                            0xffffff, top, lit and 42 or 24)
        -- border: glowing buttons pulse an amber outline, others static
        if glowing then
            local pulse = 0.4 + 0.6 * math.abs(math.sin(frame * 0.10))
            enb.draw.rrect(e.x, e.y, e.w, e.h, H.RADIUS, CFG.GLOW_LINE,
                           math.floor(255 * pulse), false)
        else
            enb.draw.rrect(e.x, e.y, e.w, e.h, H.RADIUS,
                           hot and H.LINE_HOT or H.LINE, hot and 220 or 150, false)
        end
        -- label, centred
        local lw = H.measure(b.label)
        H.otext(e.x + math.floor((e.w - lw) / 2),
                e.y + math.floor((CFG.BTN_H - 11) / 2), b.label,
                glowing and CFG.GLOW_INK or CFG.INK)
        if s.flash > 0 then s.flash = s.flash - 1 end
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
        for i = 1, #BUTTONS do state[i].hot = false end
        local over = false
        for _, e in ipairs(layout()) do
            if point_in(mx, my, e.x, e.y, e.w, e.h) then
                state[e.idx].hot = true
                over = true
                if msg == M.LBUTTONDOWN then
                    BUTTONS[e.idx].act()
                    state[e.idx].flash = CFG.FLASH_TICKS
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
