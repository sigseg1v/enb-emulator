-- SPDX-License-Identifier: MIT
-- Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
-- License: LICENSES/Freya
--
-- dump_spec.lua -- the /run value pretty-printer (lib/dump.lua).

local dump = require("dump")

test("scalars print as their literal text", function()
    eq(dump(42), "42")
    eq(dump(true), "true")
    eq(dump(nil), "nil")
    eq(dump("hi"), '"hi"')
end)

test("empty table is {}", function()
    eq(dump({}), "{}")
end)

test("flat table: sorted keys, numbers before strings", function()
    local s = dump({ z = 1, a = 2, [1] = "x" })
    -- numeric key 1 sorts first, then a, then z
    local p1 = s:find("%[1%]", 1, false)
    local pa = s:find("a =", 1, true)
    local pz = s:find("z =", 1, true)
    assert(p1 and pa and pz, "missing a key in: " .. s)
    assert(p1 < pa and pa < pz, "wrong key order in: " .. s)
end)

test("string keys that aren't identifiers are bracket-quoted", function()
    local s = dump({ ["has space"] = 1 })
    assert(s:find('%["has space"%]'), "expected bracketed key, got: " .. s)
end)

test("nested tables descend and indent", function()
    local s = dump({ outer = { inner = 5 } })
    assert(s:find("outer = {"), "expected nested open, got: " .. s)
    assert(s:find("inner = 5"), "expected nested value, got: " .. s)
end)

test("empty table at the depth boundary prints {} not {...}", function()
    -- {a={}} dumped at depth=1: the inner empty table sits exactly at the cap.
    local s = dump({ a = {} }, { depth = 1 })
    assert(s:find("a = {}"), "expected empty inner as {}, got: " .. s)
    assert(not s:find("{%.%.%.}"), "empty table must not collapse to {...}: " .. s)
end)

test("depth limit collapses deeper tables to {...}", function()
    local s = dump({ a = { b = { c = { d = 1 } } } }, { depth = 2 })
    assert(s:find("{%.%.%.}"), "expected depth collapse, got: " .. s)
end)

test("cycles are detected, not infinite-looped", function()
    local t = {}
    t.self = t
    local s = dump(t)
    assert(s:find("<cycle>"), "expected cycle marker, got: " .. s)
end)

test("maxkeys caps entries and reports the remainder", function()
    local t = {}
    for i = 1, 10 do t["k" .. i] = i end
    local s = dump(t, { maxkeys = 3 })
    assert(s:find("%(%+7 more%)"), "expected '+7 more', got: " .. s)
end)
