-- SPDX-License-Identifier: MIT
-- Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
-- License: LICENSES/Freya
--
-- freya_ui_spec.lua -- behavioural tests for scripts/freya_ui.lua against the
-- mock enb host. Pins layout geometry, calibrated/uncalibrated rendering, the
-- click->tap->flash path, key lighting, and the Tier A swallow contract.

local mock = require("mock_enb")
mock.install{ screen = { 1280, 992 } }
require("freya_ui")

local MSG = enb.msg

-- expected geometry at 1280x992 (mirrors freya_ui CFG: BTN=44 GAP=6
-- BOTTOM_MARGIN=14 PANEL_W_FRAC=0.62 STAT_W=230 STAT_H=12 STAT_GAP=6)
local BAR_W = 12 * 44 + 11 * 6          -- 594
local BAR_X = (1280 - BAR_W) // 2       -- 343
local BAR_Y = 992 - 44 - 14             -- 934
local STAT_TOP = BAR_Y - 12 - (3 * 12 + 2 * 6)  -- 874
local PANEL_W = math.floor(1280 * 0.62) -- 793
local PANEL_X = (1280 - PANEL_W) // 2   -- 243
local PANEL_Y = STAT_TOP - 12           -- 862

local function btn_center(i)
    return BAR_X + (i - 1) * 50 + 22, BAR_Y + 22
end

test("uncalibrated frame: 12 buttons + panel + 3 tracks, nothing else gradient", function()
    local frame = mock.tick()
    local grads = mock.find(frame, "rrect_grad")
    eq(#grads, 13, "rrect_grad count (1 panel + 12 buttons, no stat fills)")
    local tracks = mock.find(frame, "rrect", function(c) return c.filled and c.w == 230 end)
    eq(#tracks, 3, "stat track count")
end)

test("panel covers the bottom-center cluster down to the screen edge", function()
    local frame = mock.tick()
    local panel = mock.find(frame, "rrect_grad", function(c) return c.w > 700 end)[1]
    ok(panel, "panel found")
    eq(panel.x, PANEL_X, "panel x")
    eq(panel.y, PANEL_Y, "panel y")
    eq(panel.w, PANEL_W, "panel w")
    eq(panel.y + panel.h, 992, "panel reaches screen bottom")
    ok(panel.alpha >= 230, "panel near-opaque")
end)

test("action bar: 12 buttons, 44px square, evenly spaced, centered", function()
    local frame = mock.tick()
    local btns = mock.find(frame, "rrect_grad", function(c) return c.w == 44 end)
    eq(#btns, 12, "button count")
    table.sort(btns, function(a, b) return a.x < b.x end)
    for i, b in ipairs(btns) do
        eq(b.x, BAR_X + (i - 1) * 50, "button " .. i .. " x")
        eq(b.y, BAR_Y, "button " .. i .. " y")
        eq(b.h, 44, "button " .. i .. " h")
    end
    -- centered: equal margins either side (within the 1px floor rounding)
    near(btns[1].x, 1280 - (btns[12].x + 44), 1, "centering")
end)

test("button labels 1..9 0 - = drawn inside their buttons", function()
    local frame = mock.tick()
    local labels = { "1","2","3","4","5","6","7","8","9","0","-","=" }
    for i, want in ipairs(labels) do
        local bx = BAR_X + (i - 1) * 50
        local hits = mock.find(frame, "text", function(c)
            return c.text == want and c.x >= bx and c.x < bx + 44
                   and c.y >= BAR_Y and c.y < BAR_Y + 44
        end)
        eq(#hits, 1, "label '" .. want .. "' in button " .. i)
    end
end)

test("uncalibrated: stat labels gray, no fill bars", function()
    local frame = mock.tick()
    for _, name in ipairs({ "HULL", "SHIELD", "ENERGY" }) do
        local t = mock.find(frame, "text", function(c) return c.text == name end)[1]
        ok(t, name .. " label drawn")
        eq(t.rgb, 0x666f7a, name .. " label uncalibrated gray")
        ok(t.x >= 0, name .. " label on-screen")
    end
end)

test("calibrated: fills sized cur/max, labels colored", function()
    mock.set_self{ base = 0x05000000,
        hull = 750, hull_max = 1000, shield = 400, shield_max = 1000,
        energy = 900, energy_max = 1000 }
    local frame = mock.tick()
    local fills = mock.find(frame, "rrect_grad", function(c)
        return c.h == 12 and c.w <= 230
    end)
    eq(#fills, 3, "three stat fills")
    table.sort(fills, function(a, b) return a.y < b.y end)
    eq(fills[1].w, math.floor(230 * 0.75), "hull fill width")
    eq(fills[2].w, math.floor(230 * 0.40), "shield fill width")
    eq(fills[3].w, math.floor(230 * 0.90), "energy fill width")
    eq(fills[1].rgb, 0x33DD55, "hull green")
    eq(fills[2].rgb, 0x3399FF, "shield blue")
    eq(fills[3].rgb, 0xFF4444, "energy red")
    local hull = mock.find(frame, "text", function(c) return c.text == "HULL" end)[1]
    eq(hull.rgb, 0x33DD55, "HULL label colored when calibrated")
    -- stacked on the 18px pitch starting at stat_top
    eq(fills[1].y, STAT_TOP, "row 1 y")
    eq(fills[2].y, STAT_TOP + 18, "row 2 y")
    eq(fills[3].y, STAT_TOP + 36, "row 3 y")
    mock.set_self{ base = 0 }
end)

test("fill clamps to [0,1] on out-of-range values", function()
    mock.set_self{ base = 1, hull = 2000, hull_max = 1000,
        shield = -50, shield_max = 1000, energy = 0, energy_max = 0 }
    local frame = mock.tick()
    local fills = mock.find(frame, "rrect_grad", function(c)
        return c.h == 12 and c.w <= 230
    end)
    -- hull clamps to full; shield clamps to 0 -> not drawn; energy max=0 -> not drawn
    eq(#fills, 1, "only hull drawn")
    eq(fills[1].w, 230, "overfull hull clamps to track width")
    mock.set_self{ base = 0 }
end)

test("click on button 3 taps VK '3', swallows, and flashes for FLASH_TICKS", function()
    local cx, cy = btn_center(3)
    local swallowed = mock.input(MSG.LBUTTONDOWN, 1, mock.xy_lparam(cx, cy))
    eq(swallowed, true, "click swallowed")
    eq(#mock.state().taps, 1, "one tap")
    eq(mock.state().taps[1], 0x33, "tapped VK '3'")
    -- lit for exactly FLASH_TICKS(12) frames, then unlit
    for i = 1, 12 do
        local frame = mock.tick()
        local lit = mock.find(frame, "rrect_grad", function(c)
            return c.w == 44 and c.rgb == 0x6fa8ff
        end)
        eq(#lit, 1, "tick " .. i .. ": button lit")
        eq(lit[1].x, BAR_X + 2 * 50, "lit button is #3")
    end
    local frame = mock.tick()
    eq(#mock.find(frame, "rrect_grad", function(c)
        return c.w == 44 and c.rgb == 0x6fa8ff
    end), 0, "flash expired after 12 ticks")
end)

test("click on '=' (button 12) taps VK_OEM_PLUS", function()
    local cx, cy = btn_center(12)
    eq(mock.input(MSG.LBUTTONDOWN, 1, mock.xy_lparam(cx, cy)), true)
    eq(mock.state().taps[#mock.state().taps], 0xBB, "VK_OEM_PLUS")
    for _ = 1, 13 do mock.tick() end   -- drain the flash
end)

test("physical keydown lights the button and is NOT swallowed", function()
    eq(mock.input(MSG.KEYDOWN, 0x35, 0), false, "keydown not swallowed")
    local frame = mock.tick()
    local lit = mock.find(frame, "rrect_grad", function(c)
        return c.w == 44 and c.rgb == 0x6fa8ff
    end)
    eq(#lit, 1, "button 5 lit while held")
    eq(lit[1].x, BAR_X + 4 * 50, "lit button is #5")
    eq(mock.input(MSG.KEYUP, 0x35, 0), false, "keyup not swallowed")
    frame = mock.tick()
    eq(#mock.find(frame, "rrect_grad", function(c)
        return c.w == 44 and c.rgb == 0x6fa8ff
    end), 0, "unlit after keyup")
end)

test("SYSKEYDOWN lights too (alt held)", function()
    eq(mock.input(MSG.SYSKEYDOWN, 0x31, 0), false)
    local frame = mock.tick()
    eq(#mock.find(frame, "rrect_grad", function(c)
        return c.w == 44 and c.rgb == 0x6fa8ff
    end), 1, "button 1 lit via syskey")
    mock.input(MSG.SYSKEYUP, 0x31, 0)
end)

test("non-action keys pass through untouched", function()
    eq(mock.input(MSG.KEYDOWN, 0x41, 0), false, "'A' not swallowed")
    local frame = mock.tick()
    eq(#mock.find(frame, "rrect_grad", function(c)
        return c.w == 44 and c.rgb == 0x6fa8ff
    end), 0, "no button lit")
end)

test("mouse inside the cover panel is swallowed; outside is not", function()
    eq(mock.input(MSG.MOUSEMOVE, 0, mock.xy_lparam(PANEL_X + 10, PANEL_Y + 10)), true,
       "move in panel swallowed")
    eq(mock.input(MSG.LBUTTONDOWN, 1, mock.xy_lparam(PANEL_X + 10, PANEL_Y + 10)), true,
       "click in panel (off-button) swallowed")
    eq(#mock.state().taps, 2, "off-button panel click taps nothing")
    eq(mock.input(MSG.MOUSEMOVE, 0, mock.xy_lparam(100, 100)), false,
       "move outside panel passes")
    eq(mock.input(MSG.LBUTTONDOWN, 1, mock.xy_lparam(PANEL_X - 5, 990)), false,
       "click left of panel passes")
    eq(mock.input(MSG.RBUTTONDOWN, 1, mock.xy_lparam(640, PANEL_Y - 1)), false,
       "click above panel passes")
end)

test("negative mouse coords decode correctly and pass through", function()
    eq(mock.input(MSG.MOUSEMOVE, 0, mock.xy_lparam(-5, -7)), false,
       "negative coords not swallowed")
end)

test("WM_CHAR is masked out (handler registered KEY|MOUSE only)", function()
    eq(mock.input(MSG.CHAR, string.byte("3"), 0), false, "char passes")
    local frame = mock.tick()
    eq(#mock.find(frame, "rrect_grad", function(c)
        return c.w == 44 and c.rgb == 0x6fa8ff
    end), 0, "char does not light a button")
end)

test("layout adapts: 1024x768 and 1920x1080 keep everything on-screen", function()
    for _, res in ipairs({ { 1024, 768 }, { 1920, 1080 } }) do
        mock.set_screen(res[1], res[2])
        local frame = mock.tick()
        local btns = mock.find(frame, "rrect_grad", function(c) return c.w == 44 end)
        eq(#btns, 12, res[1] .. "x" .. res[2] .. " button count")
        table.sort(btns, function(a, b) return a.x < b.x end)
        ok(btns[1].x >= 0, "leftmost button on-screen")
        ok(btns[12].x + 44 <= res[1], "rightmost button on-screen")
        eq(btns[1].y + 44 + 14, res[2], "bottom margin preserved")
        local panel = mock.find(frame, "rrect_grad", function(c)
            return c.w == math.floor(res[1] * 0.62)
        end)[1]
        ok(panel, "panel sized to resolution")
        eq(panel.y + panel.h, res[2], "panel anchored to bottom")
    end
    mock.set_screen(1280, 992)
end)

test("pre-first-present (0x0 screen) falls back to 1280x992", function()
    mock.set_screen(0, 0)
    local frame = mock.tick()
    local btns = mock.find(frame, "rrect_grad", function(c) return c.w == 44 end)
    table.sort(btns, function(a, b) return a.x < b.x end)
    eq(btns[1].x, BAR_X, "fallback layout matches 1280x992")
    mock.set_screen(1280, 992)
end)

test("no tick errors over a long run", function()
    for _ = 1, 100 do mock.tick() end
    eq(#mock.state().tick_errors, 0, "tick errors")
end)
