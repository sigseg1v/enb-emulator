-- SPDX-License-Identifier: MIT
-- Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
-- License: LICENSES/Freya
--
-- chat.lua -- the Freya chat window (bottom-left) + input line.
--
-- require("chat") from init.lua, BEFORE freya_ui, so this on_input handler is
-- registered first and gets first crack at every key while the input box is open
-- (multi-handler on_input runs in registration order; first truthy swallows).
--
-- WHAT IT REPLACES: the native chat frame/buttons/text are hidden (hide-ui). This
-- draws our own scrollback + input. It reads the client's chat RING BUFFER
-- (enb.chat_buf() -> the ring object captured by the ChatLineAppend hook) and
-- renders every line in chronological order, so SYSTEM text and CHAT text are
-- naturally interleaved (the game funnels every line type through this one ring --
-- the same thing the native "combine channels" option does, but always on).
--
-- INPUT (owner ask): Enter opens the input box; while it is open ALL keyboard
-- input is captured by the box (the action bar / other UI never see a key -- we
-- swallow every key/char and also raise H.chat_capturing so freya_ui yields).
-- Enter again submits the typed line (enb.chat_send -> the client's own
-- slash-command/channel parser) and closes the box, returning keyboard to the
-- action bar. Esc cancels without sending. No scrollback scrolling yet.

local H = require("freya_hud")

-- ---- ring-buffer layout (see game.h chat::) --------------------------------
local RING_CAP   = 0x04   -- ring obj -> uint slot capacity
local RING_COUNT = 0x08   -- ring obj -> uint monotonic write count
local RING_BASE  = 0x0c   -- ring obj -> ptr to slot array
local SLOT_STRIDE = 0x14
local SLOT_TYPE  = 0x00   -- uint channel/type id
local SLOT_TEXT  = 0x08   -- char* (NUL-terminated ASCII)

-- line color by channel/type (decomp FUN_0074d990 + live observation). Unknown
-- types fall back to the normal ink.
local TYPE_RGB = {
    [0x00] = 0xc9d6e6,  -- COMPUTER / normal
    [0x05] = 0x9fb4cc,  -- info
    [0x06] = 0xe0c060,  -- TIP / usage
    [0x08] = 0x9b6cf0,  -- guild MOTD
    [0x0f] = 0x8fe3ff,  -- broadcast / network chat
    [0x11] = 0xe8453c,  -- text error
    [0x13] = 0x6fd28a,  -- system
    [0x14] = 0x6a7686,  -- outdated
    [0x15] = 0xe8a23c,  -- warning
}

local CFG = {
    PAD     = 7,
    LINE_H  = 15,
    INPUT_PAD = 6,
}

-- VK codes
local VK_BACK = 0x08
local VK_RETURN = 0x0D
local VK_ESCAPE = 0x1B

-- ---- input state -----------------------------------------------------------
local editing = false
local buf = ""
local frame = 0   -- for cursor blink

local function start_edit()
    editing = true
    buf = ""
    H.chat_capturing = true
end

local function stop_edit()
    editing = false
    buf = ""
    H.chat_capturing = false
end

local function submit()
    local line = buf
    stop_edit()
    if line ~= "" and enb.chat_send then
        enb.chat_send(line)
    end
end

-- ---- ring read -------------------------------------------------------------
-- returns an array of {type=, text=} oldest..newest, at most `want` entries.
local function read_lines(want)
    local out = {}
    local ring = enb.chat_buf and enb.chat_buf() or 0
    if not ring or ring == 0 then return out end
    if not enb.mem.readable(ring, 0x10) then return out end
    local cap = enb.mem.u32(ring + RING_CAP)
    local count = enb.mem.u32(ring + RING_COUNT)
    local base = enb.mem.u32(ring + RING_BASE)
    if cap == 0 or cap > 100000 or base == 0 then return out end

    local first = count - cap
    if first < 0 then first = 0 end
    if want and (count - first) > want then first = count - want end

    for i = first, count - 1 do
        local slot = base + (i % cap) * SLOT_STRIDE
        if enb.mem.readable(slot, SLOT_STRIDE) then
            local ty = enb.mem.u32(slot + SLOT_TYPE)
            local tp = enb.mem.u32(slot + SLOT_TEXT)
            local txt = (tp ~= 0 and enb.mem.readable(tp, 1)) and enb.mem.str(tp) or ""
            out[#out + 1] = { type = ty, text = txt }
        end
    end
    return out
end

-- trim `s` to fit `maxw` pixels, appending a ".." when clipped.
local function clip(s, maxw)
    if H.measure(s) <= maxw then return s end
    while #s > 1 and H.measure(s .. "..") > maxw do
        s = s:sub(1, #s - 1)
    end
    return s .. ".."
end

-- ---- render ----------------------------------------------------------------
enb.on_tick(function()
    if not H.vis() then return end
    frame = frame + 1

    local x, y, w, h = H.chat_rect()

    -- input line (above the box body) -- only while capturing
    if editing then
        local iy = y - H.CHAT.INPUT_H - 3
        H.glass(x, iy, w, H.CHAT.INPUT_H, 170)
        local caret = (frame % 30 < 15) and "_" or " "
        local shown = clip("> " .. buf, w - CFG.INPUT_PAD * 2 - 8)
        H.otext(x + CFG.INPUT_PAD, iy + 3, shown .. caret, H.INK)
    end

    -- scrollback box
    H.glass(x, y, w, h)
    local maxw = w - CFG.PAD * 2
    local max_lines = math.floor((h - CFG.PAD * 2) / CFG.LINE_H)
    if max_lines < 1 then return end
    local lines = read_lines(max_lines)
    local ly = y + CFG.PAD
    for _, ln in ipairs(lines) do
        local rgb = TYPE_RGB[ln.type] or H.INK
        H.otext(x + CFG.PAD, ly, clip(ln.text, maxw), rgb)
        ly = ly + CFG.LINE_H
    end
end)

-- ---- input -----------------------------------------------------------------
enb.on_input(function(msg, wparam, lparam)
    if not H.vis() then return false end
    local M = enb.msg

    if not editing then
        -- Enter opens the input box (and swallows, so native chat never opens).
        if (msg == M.KEYDOWN or msg == M.SYSKEYDOWN) and wparam == VK_RETURN then
            start_edit()
            return true
        end
        return false
    end

    -- editing: capture ALL keyboard. Mouse is left alone (keyboard-only spec).
    if msg == M.KEYDOWN or msg == M.SYSKEYDOWN then
        if wparam == VK_RETURN then
            submit()
        elseif wparam == VK_ESCAPE then
            stop_edit()
        elseif wparam == VK_BACK then
            if #buf > 0 then buf = buf:sub(1, #buf - 1) end
        end
        return true   -- swallow every key while the box is open
    elseif msg == M.KEYUP or msg == M.SYSKEYUP then
        return true   -- swallow the up too (no leak to action-bar release)
    elseif msg == M.CHAR then
        local c = wparam
        if c >= 0x20 and c < 0x7f and #buf < 200 then
            buf = buf .. string.char(c)
        end
        return true
    end

    return false
end, enb.WANT_KEY | enb.WANT_CHAR)

enb.log("chat.lua loaded")
