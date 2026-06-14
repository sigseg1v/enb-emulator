-- SPDX-License-Identifier: MIT
-- Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
-- License: LICENSES/Freya
--
-- freya_ui_spec.lua -- behavioural tests for scripts/freya_ui.lua against the
-- mock enb host. Pins the player-card + hotbar layout/colors, calibrated vs
-- uncalibrated rendering, the click->tap->flash path, key lighting, the Tier A
-- swallow contract, and enb.state() visibility gating.

local mock = require("mock_enb")
mock.install{ screen = { 1280, 992 } }
local H = require("freya_hud")
require("freya_ui")

local MSG = enb.msg

-- expected geometry at 1280x992 (mirrors freya_ui CFG: SLOT=46 GAP=6 BOTTOM=18
-- PC_W=224 PC_GAP=24, header+3 vitals card).
local SLOT, GAP, PITCH = 46, 6, 52
local BAR_W = 12 * SLOT + 11 * GAP        -- 618
local BAR_X = (1280 - BAR_W) // 2         -- 331
local BAR_Y = 992 - SLOT - 18             -- 928
local PC_H  = 5 + 16 + 3 + 3 * 16 + 2 * 3 + 5   -- 83
local PC_X, PC_W = BAR_X, 224
local TRACK_W = PC_W - 7 * 2 - 52         -- PC_W - PC_PAD_X*2 - PCT_W = 158
local PC_Y  = BAR_Y - 24 - PC_H           -- 821
local LIT   = 0x2f4a6e                     -- CFG.SLOT_LIT_TOP (armed slot body top)
local KEY_INK = 0xcfe0f2

local function slot_x(i) return BAR_X + (i - 1) * PITCH end
local function slot_center(i) return slot_x(i) + SLOT // 2, BAR_Y + SLOT // 2 end

-- the 12 hotbar slot BODIES (gloss sheen + player-card body are also rrect_grad,
-- so pin on a full SLOTxSLOT square).
local function slots(frame)
    return mock.find(frame, "rrect_grad", function(c) return c.w == SLOT and c.h == SLOT end)
end
local function lit_slots(frame)
    return mock.find(frame, "rrect_grad",
        function(c) return c.w == SLOT and c.h == SLOT and c.rgb == LIT end)
end

mock.set_self{ base = 0 }

test("uncalibrated: player card + 12 hotbar slots, no vital fills", function()
    local frame = mock.tick()
    eq(#slots(frame), 12, "twelve hotbar slots")
    -- player-card body is the wide rrect_grad
    local card = mock.find(frame, "rrect_grad", function(c) return c.w == PC_W and c.h == PC_H end)
    eq(#card, 1, "one player-card body")
    eq(card[1].x, PC_X, "card x = hotbar left edge")
    eq(card[1].y, PC_Y, "card y above the hotbar")
    -- no vital fills when uncalibrated (track h=16, fill width <=TRACK_W)
    local fills = mock.find(frame, "rrect_grad", function(c) return c.h == 16 and c.w <= TRACK_W end)
    eq(#fills, 0, "no vital fills uncalibrated")
end)

test("hotbar: 12 slots, 46px square, 52px pitch, centered", function()
    local frame = mock.tick()
    local sl = slots(frame)
    table.sort(sl, function(a, b) return a.x < b.x end)
    for i, s in ipairs(sl) do
        eq(s.x, slot_x(i), "slot " .. i .. " x")
        eq(s.y, BAR_Y, "slot " .. i .. " y")
    end
    near(sl[1].x, 1280 - (sl[12].x + SLOT), 1, "centering")
end)

test("slot labels 1..9 0 - = drawn inside their slots", function()
    local frame = mock.tick()
    local labels = { "1","2","3","4","5","6","7","8","9","0","-","=" }
    for i, want in ipairs(labels) do
        local bx = slot_x(i)
        local hits = mock.find(frame, "text", function(c)
            return c.text == want and c.rgb == KEY_INK
                   and c.x >= bx and c.x < bx + SLOT
                   and c.y >= BAR_Y and c.y < BAR_Y + SLOT
        end)
        eq(#hits, 1, "label '" .. want .. "' in slot " .. i)
    end
end)

test("uncalibrated: card header reads PILOT / LV -- in uncal gray", function()
    local frame = mock.tick()
    local pilot = mock.find(frame, "text", function(c) return c.text == "PILOT" end)
    ok(#pilot >= 1, "PILOT header drawn")
    -- the fill (non-shadow) copy is uncal gray
    local fill = mock.find(frame, "text", function(c) return c.text == "PILOT" and c.rgb ~= H.SHADOW end)
    eq(fill[1].rgb, H.UNCAL, "PILOT in uncal gray")
    local lv = mock.find(frame, "text", function(c) return c.text == "LV --" and c.rgb ~= H.SHADOW end)
    eq(#lv, 1, "LV -- placeholder")
    eq(lv[1].rgb, H.UNCAL, "LV -- uncal gray")
end)

test("name comes live from enb.vitals() without flat calibration", function()
    mock.set_self{ base = 0 }                      -- flat struct uncalibrated
    mock.set_vitals{ name = "Jefive", hull = 0.5 }
    local frame = mock.tick()
    local nm = mock.find(frame, "text", function(c) return c.text == "JEFIVE" and c.rgb == H.INK end)
    eq(#nm, 1, "live name shown (uppercased, INK) without flat calibration")
    mock.set_vitals{}
end)

test("calibrated: vital fills sized cur/max in vital colors", function()
    mock.set_self{ base = 0x05000000, name = "JETHREE",
        hull = 750, hull_max = 1000, shield = 400, shield_max = 1000,
        energy = 900, energy_max = 1000, combat_lvl = 23, explore_lvl = 31, trade_lvl = 12 }
    local frame = mock.tick()
    local fills = mock.find(frame, "rrect_grad", function(c) return c.h == 16 and c.w <= TRACK_W end)
    eq(#fills, 3, "three vital fills")
    table.sort(fills, function(a, b) return a.y < b.y end)
    eq(fills[1].w, math.floor(TRACK_W * 0.75), "hull fill width")
    eq(fills[2].w, math.floor(TRACK_W * 0.40), "shield fill width")
    eq(fills[3].w, math.floor(TRACK_W * 0.90), "reactor fill width")
    eq(fills[1].rgb, H.HULL, "hull red")
    eq(fills[2].rgb, H.SHIELD, "shield blue")
    eq(fills[3].rgb, H.REACTOR, "reactor green")
    -- header shows the (uppercased) name + LV = max(levels)
    local name = mock.find(frame, "text", function(c) return c.text == "JETHREE" and c.rgb == H.INK end)
    eq(#name, 1, "name header colored")
    local lv = mock.find(frame, "text", function(c) return c.text == "LV 31" and c.rgb == H.INK_DIM end)
    eq(#lv, 1, "LV = max discipline level")
    mock.set_self{ base = 0 }
end)

test("vital fill clamps to [0,1] on out-of-range values", function()
    mock.set_self{ base = 1, hull = 2000, hull_max = 1000,
        shield = -50, shield_max = 1000, energy = 0, energy_max = 0 }
    local frame = mock.tick()
    local fills = mock.find(frame, "rrect_grad", function(c) return c.h == 16 and c.w <= TRACK_W end)
    -- hull clamps full; shield clamps to 0 -> not drawn; reactor max=0 -> not drawn
    eq(#fills, 1, "only hull drawn")
    eq(fills[1].w, TRACK_W, "overfull hull clamps to track width")
    mock.set_self{ base = 0 }
end)

test("click on slot 3 taps VK '3', swallows, flashes for FLASH_TICKS", function()
    local cx, cy = slot_center(3)
    eq(mock.input(MSG.LBUTTONDOWN, 1, mock.xy_lparam(cx, cy)), true, "click swallowed")
    eq(mock.state().taps[#mock.state().taps], 0x33, "tapped VK '3'")
    for i = 1, 10 do
        local frame = mock.tick()
        local lit = lit_slots(frame)
        eq(#lit, 1, "tick " .. i .. ": slot lit")
        eq(lit[1].x, slot_x(3), "lit slot is #3")
    end
    eq(#lit_slots(mock.tick()), 0, "flash expired after FLASH_TICKS")
end)

test("click on '=' (slot 12) taps VK_OEM_PLUS", function()
    local cx, cy = slot_center(12)
    eq(mock.input(MSG.LBUTTONDOWN, 1, mock.xy_lparam(cx, cy)), true)
    eq(mock.state().taps[#mock.state().taps], 0xBB, "VK_OEM_PLUS")
    for _ = 1, 11 do mock.tick() end   -- drain the flash
end)

test("physical keydown lights the slot and is NOT swallowed", function()
    eq(mock.input(MSG.KEYDOWN, 0x35, 0), false, "keydown not swallowed")
    local lit = lit_slots(mock.tick())
    eq(#lit, 1, "slot 5 lit while held")
    eq(lit[1].x, slot_x(5), "lit slot is #5")
    eq(mock.input(MSG.KEYUP, 0x35, 0), false, "keyup not swallowed")
    eq(#lit_slots(mock.tick()), 0, "unlit after keyup")
end)

test("SYSKEYDOWN lights too (alt held)", function()
    eq(mock.input(MSG.SYSKEYDOWN, 0x31, 0), false)
    eq(#lit_slots(mock.tick()), 1, "slot 1 lit via syskey")
    mock.input(MSG.SYSKEYUP, 0x31, 0)
    mock.tick()
end)

test("non-action keys pass through untouched", function()
    eq(mock.input(MSG.KEYDOWN, 0x41, 0), false, "'A' not swallowed")
    eq(#lit_slots(mock.tick()), 0, "no slot lit")
end)

test("mouse over the player card / hotbar is swallowed; outside is not", function()
    eq(mock.input(MSG.MOUSEMOVE, 0, mock.xy_lparam(PC_X + 10, PC_Y + 10)), true,
       "move over player card swallowed")
    eq(mock.input(MSG.MOUSEMOVE, 0, mock.xy_lparam(BAR_X + 5, BAR_Y + 5)), true,
       "move over hotbar swallowed")
    eq(mock.input(MSG.MOUSEMOVE, 0, mock.xy_lparam(5, 5)), false, "top-left passes")
    eq(mock.input(MSG.LBUTTONDOWN, 1, mock.xy_lparam(PC_X - 5, 990)), false,
       "click left of the cards passes")
end)

test("negative mouse coords decode correctly and pass through", function()
    eq(mock.input(MSG.MOUSEMOVE, 0, mock.xy_lparam(-5, -7)), false,
       "negative coords not swallowed")
end)

test("WM_CHAR is masked out (handler registered KEY|MOUSE only)", function()
    eq(mock.input(MSG.CHAR, string.byte("3"), 0), false, "char passes")
    eq(#lit_slots(mock.tick()), 0, "char does not light a slot")
end)

-- ---- visibility gating (enb.state()) --------------------------------------
test("station: cards drawn but hotbar hidden", function()
    mock.set_game_state("station")
    local frame = mock.tick()
    eq(#slots(frame), 0, "no hotbar slots in station")
    local card = mock.find(frame, "rrect_grad", function(c) return c.w == PC_W and c.h == PC_H end)
    eq(#card, 1, "player card still drawn in station")
    -- a click where a slot would be taps nothing (hotbar gone)
    local before = #mock.state().taps
    local cx, cy = slot_center(3)
    mock.input(MSG.LBUTTONDOWN, 1, mock.xy_lparam(cx, cy))
    eq(#mock.state().taps, before, "no tap with hotbar hidden")
    mock.set_game_state("space")
end)

test("login / charsel / load: nothing drawn, input untouched", function()
    for _, s in ipairs({ "login", "charsel", "load" }) do
        mock.set_game_state(s)
        eq(#mock.tick(), 0, s .. ": empty frame")
        eq(mock.input(MSG.MOUSEMOVE, 0, mock.xy_lparam(PC_X + 10, PC_Y + 10)), false,
           s .. ": input passes through")
    end
    mock.set_game_state("space")
end)

test("cursor follows visibility: on in space, off on login", function()
    mock.set_game_state("space"); mock.tick()
    eq(mock.state().cursor, true, "cursor on in space")
    mock.set_game_state("login"); mock.tick()
    eq(mock.state().cursor, false, "cursor off on login")
    mock.set_game_state("space")
end)

test("layout adapts: 1024x768 and 1920x1080 keep the hotbar on-screen", function()
    for _, res in ipairs({ { 1024, 768 }, { 1920, 1080 } }) do
        mock.set_screen(res[1], res[2])
        local sl = slots(mock.tick())
        eq(#sl, 12, res[1] .. "x" .. res[2] .. " slot count")
        table.sort(sl, function(a, b) return a.x < b.x end)
        ok(sl[1].x >= 0, "leftmost slot on-screen")
        ok(sl[12].x + SLOT <= res[1], "rightmost slot on-screen")
        eq(sl[1].y + SLOT + 18, res[2], "bottom margin preserved")
    end
    mock.set_screen(1280, 992)
end)

test("pre-first-present (0x0 screen) falls back to 1280x992", function()
    mock.set_screen(0, 0)
    local sl = slots(mock.tick())
    table.sort(sl, function(a, b) return a.x < b.x end)
    eq(sl[1].x, BAR_X, "fallback layout matches 1280x992")
    mock.set_screen(1280, 992)
end)

test("no tick errors over a long run", function()
    for _ = 1, 100 do mock.tick() end
    eq(#mock.state().tick_errors, 0, "tick errors")
end)
