-- SPDX-License-Identifier: MIT
-- Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
-- License: LICENSES/Freya
--
-- introspect_spec.lua -- the /run mod introspector (lib/introspect.lua).

local mock = require("mock_enb")
mock.install{}
local introspect = require("introspect")

-- Build a fake mod that keeps a PRIVATE local captured only via a returned closure.
local function make_mod()
    local privateMap = { abc = 7, def = 8 }   -- never put in the returned table
    local M = {}
    function M.get(k) return privateMap[k] end -- closes over privateMap
    return M
end

enb.mods = {
    foo = { id = "foo", name = "Foo", ok = true, module = make_mod() },
}

test("unknown id returns error + available list", function()
    local v = introspect("nope")
    assert(v.error:find("no such mod"), "expected error, got: " .. tostring(v.error))
    eq(v.available[1], "foo")
end)

test("known id returns manifest fields + module", function()
    local v = introspect("foo")
    eq(v.id, "foo")
    eq(v.name, "Foo")
    eq(v.ok, true)
    eq(type(v.module), "table")
end)

test("private upvalue captured by a returned closure is surfaced", function()
    local v = introspect("foo")
    eq(type(v.upvalues), "table")
    eq(type(v.upvalues.privateMap), "table")
    eq(v.upvalues.privateMap.abc, 7)
    eq(v.upvalues.privateMap.def, 8)
end)

test("_ENV is not harvested as an upvalue", function()
    local v = introspect("foo")
    eq(v.upvalues._ENV, nil)
end)
