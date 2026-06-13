-- SPDX-License-Identifier: MIT
-- Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
-- License: LICENSES/Freya
--
-- init_spec.lua -- the whole startup path: init.lua loads cleanly under the
-- mock, pulls in xp_overlay + freya_ui, and applies persisted calib_data.lua
-- when one is on the package path.

local mock = require("mock_enb")
mock.install{ screen = { 1280, 992 } }

-- make the calib_data fixture resolvable BEFORE init.lua runs its pcall-require
package.path = package.path .. ";" .. (os.getenv("FIXTURES") or "fixtures") .. "/?.lua"

test("init.lua loads without error", function()
    local okret, err = pcall(require, "init")
    ok(okret, "init.lua error: " .. tostring(err))
end)

test("persisted calib_data.lua was applied via enb.calibrate", function()
    eq(mock.state().offsets.player_ptr_addr, 0x00B6C0A0, "fixture player_ptr_addr")
    eq(mock.state().offsets.hull, 0x124, "fixture hull offset")
    local applied = false
    for _, l in ipairs(mock.state().logs) do
        if l:find("applied persisted calib_data.lua", 1, true) then applied = true end
    end
    ok(applied, "apply logged")
end)

test("all HUD modules registered their tick callbacks", function()
    -- init status-line logger, xp_overlay, freya_ui = at least 3
    ok(#mock.state().ticks >= 3, "tick callbacks registered: " .. #mock.state().ticks)
    local frame = mock.tick()
    ok(#frame > 20, "combined frame draws the full HUD (" .. #frame .. " cmds)")
    eq(#mock.state().tick_errors, 0, "no tick errors")
end)

test("freya_ui input handler is live after init", function()
    ok(mock.state().input_fn ~= nil, "input handler registered")
    eq(mock.state().input_mask, enb.WANT_KEY | enb.WANT_MOUSE, "KEY|MOUSE mask")
end)
