-- SPDX-License-Identifier: MIT
-- Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
-- License: LICENSES/Freya
--
-- native_input_block.lua -- PB-64: kill the invisible native cockpit console's
-- ghost input.
--
-- The problem: hide-ui (hide-ui/native_hud_hide.lua) clears the CHROME_VT
-- panels' 0x700 draw gate, which removes the native cockpit chrome from the
-- SCREEN. But the interactive pieces the owner named -- the warp orb, the
-- thrusters, the action-bar slots, and the bottom-left navigation-map button --
-- are child GadgetClass objects registered in a SEPARATE engine input tree that
-- the panel's draw gate never touches. So they stay hit-testable and
-- hover-highlightable even though nothing is drawn: clicking the (invisible)
-- bottom-left map button still opens the nav map and still pops its "Open
-- Navigation Map" tooltip. That is PB-64.
--
-- Why not just hide the child gadgets: their engine SetVisible (gadget vtable
-- +0x40) is a proven no-op on these instances, and their input is dispatched by
-- the PARENT controller walking child rects each frame -- it never calls a
-- per-gadget input virtual -- so a per-instance vtable stub cannot intercept a
-- click either. Both mechanisms the old plan proposed are dead ends.
--
-- What works, and is safe: the native cockpit console occupies a fixed band at
-- the bottom of the screen (bottom-left button cluster + centre warp/throttle/
-- steering/action console). That band is solid HUD / cockpit-frame art -- no 3D
-- world is ever visible through it at the reference layout -- so swallowing
-- mouse input there costs no world-targeting click. This handler swallows every
-- mouse message (move + all button events, so hover-highlights AND clicks both
-- die) inside that band while the Freya UI is on and we are in space.
--
-- Registration order is load-bearing: init.lua requires this module LAST among
-- the mouse handlers, so every Freya interactive element (chat, player card,
-- hotbar, target-frame buttons, party/loot panels, micromenu strip) gets first
-- crack at the message and returns true on its own widgets BEFORE we ever see
-- it. We only catch the leftover -- clicks that land on the dead native console.
-- The band deliberately stops short of the far-right target frame and the
-- bottom-right world corner, which stay clickable.
--
-- Ctrl+U off (enb.freya_ui_on == false) restores the native HUD, so H.vis()
-- returns not-shown and this guard yields every message -- the native cockpit is
-- interactive again, exactly as the panic-switch intends.

local H = require("freya_hud")

-- mirror freya_ui's hotbar geometry so the band tracks the real console centre.
local SLOT, GAP, PAGE_W, NKEYS = 46, 6, 30, 6

-- how far the console band reaches up from the screen bottom (reference px),
-- and how far right of the hotbar the radar/throttle dial sits.
local BAND_H     = 150
local RIGHT_PAD  = 80

local function is_mouse(msg)
    local M = enb.msg
    return msg == M.MOUSEMOVE or msg == M.MOUSEWHEEL
        or msg == M.LBUTTONDOWN or msg == M.LBUTTONUP or msg == M.LBUTTONDBLCLK
        or msg == M.RBUTTONDOWN or msg == M.RBUTTONUP or msg == M.RBUTTONDBLCLK
        or msg == M.MBUTTONDOWN or msg == M.MBUTTONUP or msg == M.MBUTTONDBLCLK
end

enb.on_input(function(msg, wparam, lparam)
    if not is_mouse(msg) then return false end

    -- only guard while the Freya HUD owns the screen AND we are in space (the
    -- native cockpit console exists only in space). show_hotbar is true only in
    -- space; false in station / front-end / with the UI toggled off.
    local _, show_hotbar = H.vis()
    if not show_hotbar then return false end

    local sw, sh = enb.screen()
    if sw == 0 or sh == 0 then return false end

    local x = lparam & 0xFFFF
    local y = (lparam >> 16) & 0xFFFF
    if x >= 0x8000 then x = x - 0x10000 end
    if y >= 0x8000 then y = y - 0x10000 end

    -- band: from the left screen edge across to just right of the radar dial,
    -- and up BAND_H (scaled) from the bottom. Left-anchored so the bottom-left
    -- nav-map button ghost is covered; stops well left of the target frame.
    local bar_w = NKEYS * SLOT + (NKEYS - 1) * GAP
    local bar_x = math.floor((sw - bar_w) / 2)
    local right = bar_x + bar_w + GAP + PAGE_W + RIGHT_PAD
    local top   = sh - (BAND_H + H.sy(30))

    if x >= 0 and x < right and y >= top and y < sh then
        return true   -- swallow the dead native cockpit-console input
    end
    return false
end, enb.WANT_MOUSE)

enb.log("native_input_block.lua loaded (PB-64 cockpit-console input guard)")
