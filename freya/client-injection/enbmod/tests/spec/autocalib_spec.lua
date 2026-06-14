-- SPDX-License-Identifier: MIT
-- Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
-- License: LICENSES/Freya
--
-- autocalib_spec.lua -- tests the calibration scaffolding: find() locates a
-- planted player pointer in the fake address space, save() applies the offsets
-- live AND serializes a loadable calib_data.lua to the scripts dir.

local mock = require("mock_enb")
mock.install()

-- give calib_data_path() a Windows-style entry to parse (the in-game
-- package.path uses backslashes; the Linux test paths do not match its pattern)
package.path = package.path .. ";Z:\\enbmod\\scripts\\?.lua"

local ac = require("autocalib")

test("find() reports a planted candidate pointer", function()
    -- plant: .data slot 0x00B6C100 -> heap obj 0x05000000 whose +0 dword = 750
    mock.mem_set_u32(0x00B6C100, 0x05000000)
    mock.mem_set_u32(0x05000000, 750)
    ac.find(750, 0, 0x00B6C0F0, 0x00B6C110)
    local hit = false
    for _, l in ipairs(mock.state().logs) do
        if l:find("candidate player_ptr_addr=00b6c100", 1, true) then hit = true end
    end
    ok(hit, "candidate logged")
end)

test("probe() dumps the object", function()
    mock.mem_set_u32(0x05000004, 1000)
    ac.probe(0x05000000, 2)
    local dumped = false
    for _, l in ipairs(mock.state().logs) do
        if l:find("+0x004: 000003e8", 1, true) then dumped = true end
    end
    ok(dumped, "dword at +4 dumped")
end)

test("save() calibrates live and writes a loadable calib_data.lua", function()
    -- capture the file write instead of touching the filesystem
    local wrote_path, wrote_body
    local real_open = io.open
    io.open = function(path, mode)
        if mode == "w" then
            wrote_path = path
            local buf = {}
            return {
                write = function(_, s) buf[#buf + 1] = s end,
                close = function() wrote_body = table.concat(buf) end,
            }
        end
        return real_open(path, mode)
    end

    local okret = ac.save{ player_ptr_addr = 0x00B6C100,
                           hull = 0x124, hull_max = 0x128, combat_lvl = 0x20C }
    io.open = real_open
    eq(okret, true, "save returned true")

    -- live calibration applied
    eq(mock.state().offsets.player_ptr_addr, 0x00B6C100, "live player_ptr_addr")
    eq(mock.state().offsets.hull, 0x124, "live hull offset")

    -- path derived from the Windows-style package.path entry
    eq(wrote_path, "Z:\\enbmod\\scripts\\calib_data.lua", "persist path")

    -- the written file is valid Lua returning exactly the saved fields
    local t = assert(load(wrote_body))()
    eq(t.player_ptr_addr, 0x00B6C100, "persisted player_ptr_addr")
    eq(t.hull, 0x124); eq(t.hull_max, 0x128); eq(t.combat_lvl, 0x20C)
    local n = 0; for _ in pairs(t) do n = n + 1 end
    eq(n, 4, "no extra fields persisted")
end)

test("save() ignores fields outside the known offset set", function()
    local body
    local real_open = io.open
    io.open = function(_, mode)
        if mode == "w" then
            local buf = {}
            return { write = function(_, s) buf[#buf + 1] = s end,
                     close = function() body = table.concat(buf) end }
        end
        return real_open(_, mode)
    end
    ac.save{ hull = 0x10, bogus_field = 0xFF }
    io.open = real_open
    local t = assert(load(body))()
    eq(t.bogus_field, nil, "unknown field dropped from the file")
end)
