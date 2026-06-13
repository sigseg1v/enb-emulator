-- SPDX-License-Identifier: MIT
-- Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
-- License: LICENSES/Freya
--
-- mock_contract_spec.lua -- pins the mock's mirror of the C++ host contract
-- (hooks.cpp msg_class/mask filtering, lua_api.cpp run_input fail-open), so a
-- drift between mock and DLL semantics breaks the suite instead of silently
-- testing against the wrong rules.

local mock = require("mock_enb")
mock.install()

test("input handler error fails OPEN (never swallows) and logs", function()
    enb.on_input(function() error("boom") end)
    eq(mock.input(enb.msg.LBUTTONDOWN, 1, 0), false, "error -> not swallowed")
    local logged = false
    for _, l in ipairs(mock.state().logs) do
        if l:find("input handler error", 1, true) then logged = true end
    end
    ok(logged, "error logged")
end)

test("mask filters classes before the handler runs", function()
    local calls = 0
    enb.on_input(function() calls = calls + 1; return true end, enb.WANT_MOUSE)
    eq(mock.input(enb.msg.KEYDOWN, 0x31, 0), false, "key filtered out")
    eq(calls, 0, "handler not called for masked-out class")
    eq(mock.input(enb.msg.LBUTTONDOWN, 1, 0), true, "mouse passes mask")
    eq(calls, 1, "handler called once")
end)

test("default mask is all three classes", function()
    local seen = {}
    enb.on_input(function(msg) seen[#seen + 1] = msg; return false end)
    mock.input(enb.msg.KEYDOWN, 1, 0)
    mock.input(enb.msg.CHAR, 65, 0)
    mock.input(enb.msg.MOUSEMOVE, 0, 0)
    eq(#seen, 3, "all classes delivered")
end)

test("unclassified messages never reach the handler", function()
    local calls = 0
    enb.on_input(function() calls = calls + 1; return true end)
    eq(mock.input(0x000F --[[WM_PAINT]], 0, 0), false)
    eq(mock.input(0x0010 --[[WM_CLOSE]], 0, 0), false)
    eq(calls, 0, "handler untouched")
end)

test("truthy non-boolean return swallows (mirrors lua_toboolean)", function()
    enb.on_input(function() return 1 end)
    eq(mock.input(enb.msg.MOUSEMOVE, 0, 0), true, "1 is truthy -> swallow")
    enb.on_input(function() return nil end)
    eq(mock.input(enb.msg.MOUSEMOVE, 0, 0), false, "nil -> no swallow")
end)

test("tick callback errors are isolated (other ticks still run)", function()
    mock.install()
    local ran = false
    enb.on_tick(function() error("tick boom") end)
    enb.on_tick(function() ran = true end)
    mock.tick()
    ok(ran, "second tick callback ran despite first erroring")
    eq(#mock.state().tick_errors, 1, "error recorded")
end)

test("xy_lparam round-trips negative client coords", function()
    local lp = mock.xy_lparam(-5, -7)
    local x = lp & 0xFFFF
    local y = (lp >> 16) & 0xFFFF
    if x >= 0x8000 then x = x - 0x10000 end
    if y >= 0x8000 then y = y - 0x10000 end
    eq(x, -5); eq(y, -7)
end)
