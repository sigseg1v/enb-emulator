-- SPDX-License-Identifier: MIT
-- Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
-- License: LICENSES/Freya
--
-- ui_toggle.lua -- Ctrl+U flips the whole Freya cockpit overlay on/off.
--
-- The owner wants a panic switch back to the stock UI: if anything Freya-side
-- misbehaves, one chord restores the native HUD and one more brings Freya back.
-- All the actual visibility logic lives behind the single `enb.freya_ui_on`
-- flag (see freya_hud.H.ui_on): every Freya overlay gates its draw on H.vis()
-- (which returns false when the flag is off), and the hide-ui mod re-shows the
-- native chrome each tick while the flag is off. This module only owns the key
-- chord that flips the flag.
--
-- This handler is DELIBERATELY NOT gated on H.vis()/in-space: it must keep
-- listening while the Freya UI is OFF (otherwise there would be no way to turn
-- it back on), and it works on the front-end / in station too. It swallows only
-- the Ctrl+U keydown it acts on; every other key passes straight through.
--
-- Ctrl is read LIVE via enb.key_down (GetAsyncKeyState), NOT latched from
-- KEYDOWN/KEYUP events. A sector transition steals window focus mid-chord, so
-- the Ctrl KEYUP is dropped and a latch would stay stuck "down" -- after which
-- any later 'U' (even one typed into chat) would silently toggle the UI. Asking
-- the OS for the real key state at the instant U is pressed cannot get stuck.

local H = require("freya_hud")

local VK_CONTROL  = 0x11
local VK_U        = 0x55

-- auto-repeat sends a stream of KEYDOWNs while U is held; only act on the
-- leading edge so holding Ctrl+U doesn't flap the toggle every frame.
local u_held = false

enb.on_input(function(msg, wparam, lparam)
    local M = enb.msg
    local down = (msg == M.KEYDOWN or msg == M.SYSKEYDOWN)
    local up   = (msg == M.KEYUP   or msg == M.SYSKEYUP)

    if wparam ~= VK_U then return false end

    if up then u_held = false; return false end
    if not down then return false end
    if u_held then return true end          -- swallow held-key auto-repeat
    u_held = true

    if not (enb.key_down and enb.key_down(VK_CONTROL)) then
        return false                         -- plain U, not the chord -- pass through
    end

    local on = H.toggle_ui()
    enb.log("freya UI " .. (on and "ON" or "OFF (native HUD restored)"))
    return true    -- swallow so the control-char / any native U binding never fires
end, enb.WANT_KEY)

enb.log("ui_toggle.lua loaded (Ctrl+U toggles Freya UI)")
