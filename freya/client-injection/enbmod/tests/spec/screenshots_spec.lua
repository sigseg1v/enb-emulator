-- SPDX-License-Identifier: MIT
-- Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
-- License: LICENSES/Freya
--
-- screenshots_spec.lua -- renders representative full-HUD frames to JSON dumps
-- that render_frame.py rasterizes into PNGs for visual verification (the point
-- of the suite: see the UI without launching the client). Each scenario also
-- sanity-asserts its frame so a broken dump fails loudly here, not in Python.

local mock = require("mock_enb")
local OUT = os.getenv("SHOT_DIR") or "."

mock.install{ screen = { 1280, 992 } }
require("xp_overlay")
require("freya_ui")

local function shot(name, min_cmds)
    local frame = mock.tick()
    ok(#frame >= (min_cmds or 30), name .. ": frame too small (" .. #frame .. ")")
    mock.dump_frame(frame, OUT .. "/" .. name .. ".json")
end

test("scenario: uncalibrated at 1280x992", function()
    shot("01_uncalibrated_1280x992")
end)

test("scenario: calibrated stats + xp", function()
    mock.set_self{ base = 0x05000000,
        hull = 750, hull_max = 1000,
        shield = 400, shield_max = 1000,
        energy = 900, energy_max = 1000,
        combat_lvl = 23, combat_pct = 41,
        explore_lvl = 31, explore_pct = 87,
        trade_lvl = 12, trade_pct = 5 }
    shot("02_calibrated_1280x992")
end)

test("scenario: button 5 held + button 11 click-flash", function()
    mock.input(enb.msg.KEYDOWN, 0x35, 0)
    -- click the '-' button (#11): centers derive from the same layout the UI uses
    local bar_x = (1280 - (12 * 44 + 11 * 6)) // 2
    local cx = bar_x + 10 * 50 + 22
    local cy = 992 - 44 - 14 + 22
    eq(mock.input(enb.msg.LBUTTONDOWN, 1, mock.xy_lparam(cx, cy)), true, "click hit")
    shot("03_interaction_held5_flash11")
    mock.input(enb.msg.KEYUP, 0x35, 0)
    for _ = 1, 13 do mock.tick() end
end)

test("scenario: small window 1024x768", function()
    mock.set_screen(1024, 768)
    shot("04_calibrated_1024x768")
    mock.set_screen(1280, 992)
end)
