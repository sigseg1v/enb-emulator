-- SPDX-License-Identifier: MIT
-- Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
-- License: LICENSES/Freya
--
-- xp_overlay_spec.lua -- pins the XP-bar label placement: one label per bar,
-- right-aligned LEFT of the bar, screen-bottom-anchored, gray until calibrated.

local mock = require("mock_enb")
mock.install{ screen = { 1280, 992 } }
require("xp_overlay")

test("three labels drawn, bottom-anchored on the 20px pitch", function()
    local frame = mock.tick()
    local texts = mock.find(frame, "text")
    eq(#texts, 3, "label count")
    table.sort(texts, function(a, b) return a.y < b.y end)
    -- bottom bar top = 992 - 17 - 12 = 963; label vertically centered in 12px
    -- bar with 14px text: ty = y + floor((12-14)/2) = y - 1
    eq(texts[3].y, 962, "bottom row y")
    eq(texts[2].y, 942, "middle row y")
    eq(texts[1].y, 922, "top row y")
end)

test("labels stay on-screen (right-aligned left of the bar at x=27)", function()
    local frame = mock.tick()
    for _, t in ipairs(mock.find(frame, "text")) do
        ok(t.x >= 0, ("label '%s' clipped off-screen (x=%d)"):format(t.text, t.x))
    end
end)

test("uncalibrated: 'L? ?%' in gray", function()
    local frame = mock.tick()
    for _, t in ipairs(mock.find(frame, "text")) do
        eq(t.text, "L? ?%", "placeholder text")
        eq(t.rgb, 0x888888, "uncalibrated gray")
    end
end)

test("calibrated: per-row level/pct with per-row colors", function()
    mock.set_self{ base = 1,
        combat_lvl = 23, combat_pct = 41,
        explore_lvl = 31, explore_pct = 87,
        trade_lvl = 12, trade_pct = 5 }
    local frame = mock.tick()
    local texts = mock.find(frame, "text")
    table.sort(texts, function(a, b) return a.y < b.y end)
    eq(texts[1].text, "L23 41%", "combat row (top)")
    eq(texts[1].rgb, 0xFF6644, "combat red")
    eq(texts[2].text, "L31 87%", "explore row")
    eq(texts[2].rgb, 0x55DD55, "explore green")
    eq(texts[3].text, "L12 5%", "trade row (bottom)")
    eq(texts[3].rgb, 0x55AAFF, "trade blue")
    mock.set_self{ base = 0 }
end)

test("anchors to the real screen height", function()
    mock.set_screen(1280, 768)
    local frame = mock.tick()
    local texts = mock.find(frame, "text")
    table.sort(texts, function(a, b) return a.y < b.y end)
    eq(texts[3].y, 768 - 17 - 12 - 1, "bottom row tracks screen height")
    mock.set_screen(1280, 992)
end)
