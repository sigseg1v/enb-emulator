-- SPDX-License-Identifier: MIT
-- Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
-- License: LICENSES/Freya
--
-- dump.lua -- pretty-print any Lua value (nested tables, sorted keys, cycle-safe,
-- depth-limited) to a multi-line string. Built for inspecting live mod / game
-- data structures from the "/run" chat console: e.g. `/run enb.vitals()` (the
-- console auto-dumps a returned table) or `/run dump(some.module.state, {depth=2})`.
--
-- opts.depth   max table nesting to descend (default 4); deeper -> "{...}".
-- opts.maxkeys max entries printed per table (default 80); extra -> "... (+N more)".

local function fmt_key(k)
    if type(k) == "string" and k:match("^[%a_][%w_]*$") then return k end
    if type(k) == "string" then return "[" .. string.format("%q", k) .. "]" end
    return "[" .. tostring(k) .. "]"
end

local function key_lt(a, b)
    local ta, tb = type(a), type(b)
    if ta == "number" and tb == "number" then return a < b end
    if ta == "number" then return true end       -- numbers before strings
    if tb == "number" then return false end
    return tostring(a) < tostring(b)
end

local function dump(value, opts)
    opts = opts or {}
    local maxdepth = opts.depth or 4
    local maxkeys  = opts.maxkeys or 80
    local seen, out = {}, {}
    local function emit(s) out[#out + 1] = s end

    local function rec(v, depth)
        local t = type(v)
        if t == "table" then
            if seen[v] then emit("<cycle>"); return end
            if next(v) == nil then emit("{}"); return end   -- before the depth cap: an
                                                            -- empty table is "{}", not "{...}"
            if depth >= maxdepth then emit("{...}"); return end
            seen[v] = true
            local keys = {}
            for k in pairs(v) do keys[#keys + 1] = k end
            table.sort(keys, key_lt)
            emit("{\n")
            local pad = string.rep("  ", depth + 1)
            local shown = math.min(#keys, maxkeys)
            for i = 1, shown do
                local k = keys[i]
                emit(pad .. fmt_key(k) .. " = ")
                rec(v[k], depth + 1)
                emit(",\n")
            end
            if #keys > shown then
                emit(pad .. "... (+" .. (#keys - shown) .. " more)\n")
            end
            emit(string.rep("  ", depth) .. "}")
            seen[v] = nil
        elseif t == "string" then
            emit(string.format("%q", v))
        elseif t == "number" or t == "boolean" or t == "nil" then
            emit(tostring(v))
        else
            -- function / userdata / thread: show type + address for identity
            emit("<" .. tostring(v) .. ">")
        end
    end

    rec(value, 0)
    return table.concat(out)
end

return dump
