-- SPDX-License-Identifier: MIT
-- Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
-- License: LICENSES/Freya
--
-- json.lua -- a small, dependency-free JSON decoder. Just enough to parse the
-- flat mod.json manifests (objects, strings, numbers, booleans, null, arrays).
-- decode(str) -> value, or raises an error on malformed input.

local M = {}

local function skip_ws(s, i)
    local _, j = s:find("^[ \t\r\n]*", i)
    return (j or i - 1) + 1
end

local decode_value  -- forward decl

local escapes = { ['"'] = '"', ['\\'] = '\\', ['/'] = '/', b = '\b',
                  f = '\f', n = '\n', r = '\r', t = '\t' }

local function decode_string(s, i)
    -- s:sub(i) == '"...'
    local out, j = {}, i + 1
    while j <= #s do
        local c = s:sub(j, j)
        if c == '"' then
            return table.concat(out), j + 1
        elseif c == '\\' then
            local e = s:sub(j + 1, j + 1)
            if e == 'u' then
                local hex = s:sub(j + 2, j + 5)
                local cp = tonumber(hex, 16)
                if not cp then error("bad \\u escape at " .. j) end
                -- minimal: emit UTF-8 for the BMP code point
                if cp < 0x80 then
                    out[#out + 1] = string.char(cp)
                elseif cp < 0x800 then
                    out[#out + 1] = string.char(0xC0 | (cp >> 6), 0x80 | (cp & 0x3F))
                else
                    out[#out + 1] = string.char(0xE0 | (cp >> 12),
                        0x80 | ((cp >> 6) & 0x3F), 0x80 | (cp & 0x3F))
                end
                j = j + 6
            else
                out[#out + 1] = escapes[e] or error("bad escape \\" .. e .. " at " .. j)
                j = j + 2
            end
        else
            out[#out + 1] = c
            j = j + 1
        end
    end
    error("unterminated string")
end

local function decode_number(s, i)
    local num = s:match("^%-?%d+%.?%d*[eE]?[%+%-]?%d*", i)
    if not num or num == "" then error("bad number at " .. i) end
    return tonumber(num), i + #num
end

local function decode_array(s, i)
    local arr, n = {}, 0
    i = skip_ws(s, i + 1)
    if s:sub(i, i) == ']' then return arr, i + 1 end
    while true do
        local v
        v, i = decode_value(s, i)
        n = n + 1; arr[n] = v
        i = skip_ws(s, i)
        local c = s:sub(i, i)
        if c == ']' then return arr, i + 1 end
        if c ~= ',' then error("expected ',' or ']' at " .. i) end
        i = skip_ws(s, i + 1)
    end
end

local function decode_object(s, i)
    local obj = {}
    i = skip_ws(s, i + 1)
    if s:sub(i, i) == '}' then return obj, i + 1 end
    while true do
        if s:sub(i, i) ~= '"' then error("expected key string at " .. i) end
        local key
        key, i = decode_string(s, i)
        i = skip_ws(s, i)
        if s:sub(i, i) ~= ':' then error("expected ':' at " .. i) end
        local v
        v, i = decode_value(s, skip_ws(s, i + 1))
        obj[key] = v
        i = skip_ws(s, i)
        local c = s:sub(i, i)
        if c == '}' then return obj, i + 1 end
        if c ~= ',' then error("expected ',' or '}' at " .. i) end
        i = skip_ws(s, i + 1)
    end
end

decode_value = function(s, i)
    i = skip_ws(s, i)
    local c = s:sub(i, i)
    if c == '{' then return decode_object(s, i) end
    if c == '[' then return decode_array(s, i) end
    if c == '"' then return decode_string(s, i) end
    if c == '-' or c:match("%d") then return decode_number(s, i) end
    if s:sub(i, i + 3) == "true"  then return true,  i + 4 end
    if s:sub(i, i + 4) == "false" then return false, i + 5 end
    if s:sub(i, i + 3) == "null"  then return nil,   i + 4 end
    error("unexpected character '" .. c .. "' at " .. i)
end

function M.decode(s)
    assert(type(s) == "string", "json.decode expects a string")
    local v, i = decode_value(s, 1)
    i = skip_ws(s, i)
    if i <= #s then error("trailing garbage at " .. i) end
    return v
end

return M
