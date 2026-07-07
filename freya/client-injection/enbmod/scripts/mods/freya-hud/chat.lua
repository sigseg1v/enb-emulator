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

-- line color by channel/type (0x0074d990 + live observation). Unknown
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
    -- The overlay has exactly ONE fixed ~14px bitmap font (see freya_hud.lua).
    -- There is no smaller real font; downscaling the bitmap (SCALE < 1) just
    -- renders blurry/unreadable glyphs, so chat stays at native size.
    SCALE   = 1.0,
    LINE_H  = 16,     -- row pitch at native font size
    INPUT_PAD = 6,
    INDENT  = "  ",   -- 2-space indent on every wrapped continuation row
}

-- VK codes
local VK_BACK = 0x08
local VK_RETURN = 0x0D
local VK_ESCAPE = 0x1B
local VK_OEM_2 = 0xBF  -- the '/' key on a US layout

-- ---- input state -----------------------------------------------------------
local editing = false
local buf = ""
local frame = 0   -- for cursor blink

local function start_edit()
    editing = true
    buf = ""
    H.chat_capturing = true
end

-- expose the opener so the top-strip "Chat" button (micromenu.lua) can raise the
-- Freya input box -- the native chat frame is hidden, so this is how a mouse user
-- starts typing without pressing Enter.
H.open_chat = start_edit

local function stop_edit()
    editing = false
    buf = ""
    H.chat_capturing = false
end

-- Channel byte for enb.chat_send: 3 = Local/Sector, 4 = Broadcast.
local CH_LOCAL = 3
local CH_BROADCAST = 4

-- Front-end channel shortcuts typed in the Freya box. These are OUR convenience
-- aliases (not native slash commands): we strip the leading token and route the
-- rest with the right channel byte / native command. Anything that is not one of
-- these falls through unchanged so real native slash commands (/gen /ooc /tell ..)
-- still reach the client's own parser.
--   /b  <msg> | /broadcast <msg>  -> broadcast (channel 4)
--   /s  <msg> | /sector <msg>     -> local/sector (channel 3)
--   /w  <name> <msg>              -> whisper (rewritten to native /tell <name> <msg>)
local function route(line)
    if not enb.chat_send then return end
    -- split leading token (the command) from the remainder
    local cmd, rest = line:match("^(%S+)%s*(.*)$")
    if not cmd then enb.chat_send(line, CH_LOCAL); return end
    local lc = cmd:lower()
    if lc == "/b" or lc == "/broadcast" then
        if rest ~= "" then enb.chat_send(rest, CH_BROADCAST) end
    elseif lc == "/s" or lc == "/sector" then
        if rest ~= "" then enb.chat_send(rest, CH_LOCAL) end
    elseif lc == "/w" or lc == "/whisper" then
        -- /w <name> <msg> -> native /tell <name> <msg> (handled by the dispatcher)
        local name, msg = rest:match("^(%S+)%s+(.*)$")
        if name and msg ~= "" then enb.chat_send("/tell " .. name .. " " .. msg) end
    elseif cmd:sub(1, 1) == "/" then
        -- some other slash command: hand the whole line to the native parser
        enb.chat_send(line)
    else
        -- plain text -> local/sector
        enb.chat_send(line, CH_LOCAL)
    end
end

local function submit()
    local line = buf
    stop_edit()
    if line ~= "" then
        route(line)
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

-- trim `s` to fit `maxw` pixels, appending a ".." when clipped (input line only).
local function clip(s, maxw)
    if H.measure(s) <= maxw then return s end
    while #s > 1 and H.measure(s .. "..") > maxw do
        s = s:sub(1, #s - 1)
    end
    return s .. ".."
end

-- rendered width of `s` at the chat font scale.
local function fitw(s) return H.measure(s) * CFG.SCALE end

-- word-wrap `s` into rows that each fit `maxw` rendered pixels. The first row
-- starts flush; every continuation row is prefixed with CFG.INDENT (2 spaces).
-- A single word wider than the box is hard-broken at the character level.
local function wrap(s, maxw)
    if fitw(s) <= maxw then return { s } end
    local rows = {}
    local prefix, cur = "", ""
    local function push()
        rows[#rows + 1] = prefix .. cur
        prefix, cur = CFG.INDENT, ""
    end
    for word in s:gmatch("%S+") do
        local cand = (cur == "") and word or (cur .. " " .. word)
        if fitw(prefix .. cand) <= maxw then
            cur = cand
        else
            if cur ~= "" then push() end
            if fitw(prefix .. word) > maxw then
                -- the word alone overflows: hard-break it char by char
                local piece = ""
                for i = 1, #word do
                    local ch = word:sub(i, i)
                    if piece ~= "" and fitw(prefix .. piece .. ch) > maxw then
                        rows[#rows + 1] = prefix .. piece
                        prefix, piece = CFG.INDENT, ch
                    else
                        piece = piece .. ch
                    end
                end
                cur = piece
            else
                cur = word
            end
        end
    end
    if cur ~= "" then push() end
    if #rows == 0 then return { s } end
    return rows
end

-- ---- render ----------------------------------------------------------------
enb.on_tick(function()
    if not H.vis() then return end
    frame = frame + 1

    local x, y, w, h = H.chat_rect()
    -- on a bigger-than-1280x960 screen the owner slides the chat box up 20px and
    -- right 32px (0 at the reference res). Applied here, NOT in H.chat_rect, so
    -- the xp_overlay "player frame" anchored at H.chat_top() keeps its own offset
    -- and the two do not compound.
    x = x + H.sx(32)
    y = y - H.sy(20)

    -- input line (above the box body) -- only while capturing
    if editing then
        local iy = y - H.CHAT.INPUT_H - 3
        H.glass(x, iy, w, H.CHAT.INPUT_H, 170)
        local caret = (frame % 30 < 15) and "_" or " "
        -- clip works in full-size px, so divide the available width by the scale
        local shown = clip("> " .. buf, (w - CFG.INPUT_PAD * 2 - 8) / CFG.SCALE)
        H.otext(x + CFG.INPUT_PAD, iy + 3, shown .. caret, H.INK, CFG.SCALE)
    end

    -- scrollback box
    H.glass(x, y, w, h)
    local maxw = w - CFG.PAD * 2
    local max_rows = math.floor((h - CFG.PAD * 2) / CFG.LINE_H)
    if max_rows < 1 then return end
    -- read the newest raw lines, then word-wrap each into one-or-more rows.
    -- Reading max_rows raw lines yields at least max_rows rows (each line is >=1
    -- row); we then keep only the newest max_rows so the box stays full.
    local lines = read_lines(max_rows)
    local rows = {}
    for _, ln in ipairs(lines) do
        local rgb = TYPE_RGB[ln.type] or H.INK
        for _, seg in ipairs(wrap(ln.text, maxw)) do
            rows[#rows + 1] = { text = seg, rgb = rgb }
        end
    end
    local first_row = math.max(1, #rows - max_rows + 1)
    local ly = y + CFG.PAD
    for i = first_row, #rows do
        H.otext(x + CFG.PAD, ly, rows[i].text, rows[i].rgb, CFG.SCALE)
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
        -- '/' must open OUR box, never the native chat window (PB-67). Swallow the
        -- keydown here so the game wndproc never sees it; hk_GetMessageA then
        -- synthesizes the WM_CHAR for '/' and re-feeds it, and since we are now
        -- editing the CHAR branch below appends it -- so the box opens showing "/".
        -- (Do NOT pre-seed buf with "/" or that synthesized char would double it.)
        if (msg == M.KEYDOWN or msg == M.SYSKEYDOWN) and wparam == VK_OEM_2 then
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
