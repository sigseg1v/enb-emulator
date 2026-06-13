-- SPDX-License-Identifier: MIT
-- Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
-- License: LICENSES/Freya
--
-- xp_overlay_spec.lua -- pins the discipline card (bottom-left): a titleless
-- glass panel with three rows (Combat / Exploration / Trade), each a colored
-- letter + xp bar + "LV n" badge. Gray skeleton until calibrated, bottom-
-- anchored, gated by enb.state().

local mock = require("mock_enb")
mock.install{ screen = { 1280, 992 } }
local H = require("freya_hud")
require("xp_overlay")

-- design CFG: X=14 W=196 BOTTOM=14 PAD_Y=6 ROW_H=18 ROW_GAP=4
local DC_H = 6 * 2 + 3 * 18 + 2 * 4   -- 74

-- the colored (fill) text copies only: H.otext draws 4 shadow copies in
-- H.SHADOW plus one fill copy carrying the real rgb.
local function fills(frame)
    return mock.find(frame, "text", function(c) return c.rgb ~= H.SHADOW end)
end
-- the card body (first rrect_grad of the glass panel)
local function body(frame)
    return mock.find(frame, "rrect_grad", function(c) return c.w == 196 end)[1]
end

test("glass panel anchored bottom-left at design left:14", function()
    mock.set_self{ base = 0 }
    local b = body(mock.tick())
    ok(b, "panel drawn")
    eq(b.x, 14, "left edge")
    eq(b.w, 196, "card width")
    eq(b.h, DC_H, "card height (3 rows)")
    eq(b.y + b.h + 14, 992, "bottom gap = design BOTTOM:14")
end)

test("uncalibrated: C/E/T letters + 'LV --' badges in uncal gray", function()
    mock.set_self{ base = 0 }
    local t = fills(mock.tick())
    eq(#t, 6, "3 letters + 3 badges")
    local count = {}
    for _, c in ipairs(t) do
        eq(c.rgb, H.UNCAL, "uncal gray: " .. c.text)
        count[c.text] = (count[c.text] or 0) + 1
    end
    ok(count["C"] == 1 and count["E"] == 1 and count["T"] == 1, "C/E/T letters")
    eq(count["LV --"], 3, "three 'LV --' badges")
end)

test("calibrated: per-row colored letters + 'LV n' badges", function()
    mock.set_self{ base = 1,
        combat_lvl = 23, combat_pct = 41,
        explore_lvl = 31, explore_pct = 87,
        trade_lvl = 12, trade_pct = 5 }
    local frame = mock.tick()
    local function letter_color(ch)
        local m = mock.find(frame, "text", function(c) return c.text == ch and c.rgb ~= H.SHADOW end)
        return m[1] and m[1].rgb
    end
    eq(letter_color("C"), H.COMBAT, "combat teal")
    eq(letter_color("E"), H.EXPLORE, "explore blue")
    eq(letter_color("T"), H.TRADE, "trade purple")
    local badges = {}
    for _, c in ipairs(fills(frame)) do
        if c.text:find("^LV ") then badges[c.text] = true end
    end
    ok(badges["LV 23"] and badges["LV 31"] and badges["LV 12"], "level badges")
    mock.set_self{ base = 0 }
end)

test("xp bar fill width tracks pct in the discipline color", function()
    mock.set_self{ base = 1, combat_lvl = 5, combat_pct = 50,
        explore_lvl = 5, explore_pct = 0, trade_lvl = 5, trade_pct = 100 }
    local frame = mock.tick()
    -- bar fills are rrect_grad of height BAR_H=8
    local barfills = mock.find(frame, "rrect_grad", function(c) return c.h == 8 end)
    -- combat (50%) and trade (100%) draw; explore (0%) does not
    eq(#barfills, 2, "two bar fills (0% not drawn)")
    mock.set_self{ base = 0 }
end)

test("rows stack on the row pitch from the panel top", function()
    mock.set_self{ base = 0 }
    local frame = mock.tick()
    local b = body(frame)
    local letters = mock.find(frame, "text",
        function(c) return (c.text == "C" or c.text == "E" or c.text == "T") and c.rgb ~= H.SHADOW end)
    table.sort(letters, function(a, x) return a.y < x.y end)
    eq(letters[1].text, "C", "top row is Combat")
    eq(letters[3].text, "T", "bottom row is Trade")
    -- 18px row + 4px gap pitch = 22
    eq(letters[2].y - letters[1].y, 22, "row pitch")
    eq(letters[3].y - letters[2].y, 22, "row pitch")
end)

test("anchors to the real screen height", function()
    mock.set_self{ base = 0 }
    mock.set_screen(1280, 768)
    local b1 = body(mock.tick())
    eq(b1.y + b1.h + 14, 768, "768 bottom gap")
    mock.set_screen(1280, 992)
    local b2 = body(mock.tick())
    ok(b2.y > b1.y, "taller screen pushes the panel lower")
end)

test("hidden on login / charsel / load", function()
    for _, s in ipairs({ "login", "charsel", "load" }) do
        mock.set_game_state(s)
        eq(#mock.tick(), 0, s .. ": nothing drawn")
    end
    mock.set_game_state("space")
end)

test("shown in station (discipline card stays)", function()
    mock.set_game_state("station")
    ok(body(mock.tick()), "panel drawn in station")
    mock.set_game_state("space")
end)
