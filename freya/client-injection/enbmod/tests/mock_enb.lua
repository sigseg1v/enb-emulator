-- SPDX-License-Identifier: MIT
-- Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
-- License: LICENSES/Freya
--
-- mock_enb.lua -- a pure-Lua mock of the C++ `enb` host API that enbmod.dll
-- provides to scripts. It lets the scripts under scripts/ run headless on a
-- desktop Lua (no client, no WINE, no D3D8), so mods can be debugged, tested,
-- and visually verified programmatically:
--
--   * enb.draw.* records every draw command into a per-tick frame list that
--     specs can assert on and that render_frame.py rasterizes to a PNG.
--   * mock.input() replicates the C++ input dispatch CONTRACT exactly:
--     the hooks.cpp msg_class/mask filter, and the fail-open rule (a Lua
--     error in the handler logs and never swallows).
--   * enb.self()/enb.target() return spec-controlled tables; enb.tap/key/char
--     record instead of posting messages; enb.mem reads a spec-planted fake
--     address space.
--
-- CONTRACT MIRRORS (keep in sync with the C++ -- each cites its source):
--   WANT_* values            src/hooks.h    (1, 2, 4)
--   msg_class()              src/hooks.cpp  msg_class
--   enb.msg ids              src/lua_api.cpp open() ENBM block
--   fail-open input          src/lua_api.cpp run_input
--   self() uncalibrated      returns { base = 0 }
--   measure fallback metric  7 px/char x 14 px -- the same estimate the
--     scripts themselves use pre-atlas, so layout is self-consistent.

local M = {}

M.WANT_KEY, M.WANT_CHAR, M.WANT_MOUSE = 1, 2, 4

local MSG = {
    KEYDOWN = 0x0100, KEYUP = 0x0101, SYSKEYDOWN = 0x0104, SYSKEYUP = 0x0105,
    CHAR = 0x0102,
    MOUSEMOVE = 0x0200, MOUSEWHEEL = 0x020A,
    LBUTTONDOWN = 0x0201, LBUTTONUP = 0x0202, LBUTTONDBLCLK = 0x0203,
    RBUTTONDOWN = 0x0204, RBUTTONUP = 0x0205, RBUTTONDBLCLK = 0x0206,
    MBUTTONDOWN = 0x0207, MBUTTONUP = 0x0208, MBUTTONDBLCLK = 0x0209,
}

-- mirror of hooks.cpp msg_class()
local function msg_class(msg)
    if msg == MSG.KEYDOWN or msg == MSG.KEYUP
       or msg == MSG.SYSKEYDOWN or msg == MSG.SYSKEYUP then return M.WANT_KEY end
    if msg == MSG.CHAR then return M.WANT_CHAR end
    if msg >= 0x0200 and msg <= 0x020A then return M.WANT_MOUSE end
    return 0
end

-- ---- mutable mock state -----------------------------------------------------
local S  -- created by reset()

function M.reset()
    S = {
        screen_w = 1280, screen_h = 992,
        self_tbl = { base = 0 },
        target_tbl = nil,
        state = "unknown",       -- enb.state(): space/station/login/charsel/load/unknown
        cursor = false,          -- last enb.cursor(on) value

        offsets = {},            -- accumulated enb.calibrate{} fields
        ticks = {},              -- registered on_tick fns, in order
        input_fn = nil, input_mask = 0,
        frame = {},              -- draw cmds recorded during the current tick
        taps = {}, keys = {}, chars = {},   -- synthesized-input records
        logs = {},
        calls = {},              -- enb.call / call_cdecl records
        patches = {},            -- enb.patch_ret records
        mem = {},                -- fake address space: [addr] = u32 (4-aligned)
        tick_errors = {},        -- errors raised by tick callbacks (pcall'd like C++)
    }
end

function M.state() return S end
function M.set_screen(w, h) S.screen_w, S.screen_h = w, h end
function M.set_self(t) S.self_tbl = t end
function M.set_game_state(s) S.state = s end
function M.set_target(t) S.target_tbl = t end
function M.mem_set_u32(addr, v) S.mem[addr] = v end

-- run one tick: clear the frame, run every on_tick (pcall'd, like the C++
-- tick driver), return the recorded frame.
function M.tick()
    S.frame = {}
    for _, fn in ipairs(S.ticks) do
        local ok, err = pcall(fn)
        if not ok then S.tick_errors[#S.tick_errors + 1] = tostring(err) end
    end
    return S.frame
end

-- dispatch one input message exactly like hooks.cpp + lua_api.cpp run_input:
-- mask filter first, then pcall the handler; error => log + fail open (false).
function M.input(msg, wparam, lparam)
    if not S.input_fn or S.input_mask == 0 then return false end
    local cls = msg_class(msg)
    if cls == 0 or (cls & S.input_mask) == 0 then return false end
    local ok, swallow = pcall(S.input_fn, msg, wparam, lparam)
    if not ok then
        S.logs[#S.logs + 1] = "lua input handler error: " .. tostring(swallow)
        return false   -- FAIL OPEN, mirror of run_input
    end
    return not not swallow
end

-- convenience: build an lparam from client coords the way Win32 packs them
function M.xy_lparam(x, y)
    return ((y & 0xFFFF) << 16) | (x & 0xFFFF)
end

-- find recorded draw cmds by kind (and optional predicate)
function M.find(frame, kind, pred)
    local out = {}
    for _, c in ipairs(frame) do
        if c.kind == kind and (not pred or pred(c)) then out[#out + 1] = c end
    end
    return out
end

-- ---- frame -> JSON (consumed by render_frame.py) ---------------------------
local function jstr(s) return '"' .. s:gsub('[\\"]', '\\%0'):gsub('\n', '\\n') .. '"' end
local function jval(v)
    local t = type(v)
    if t == "string" then return jstr(v) end
    if t == "number" then return string.format("%.17g", v) end
    if t == "boolean" then return tostring(v) end
    error("unserializable: " .. t)
end

-- serialize a frame (the recorded draw cmds) to a JSON string. Used both by
-- dump_frame (-> file, for render_frame.py) and the interactive host (-> stdout).
function M.frame_json(frame)
    local out = { string.format('{"w":%d,"h":%d,"cmds":[', S.screen_w, S.screen_h) }
    for i, c in ipairs(frame) do
        local parts = {}
        -- stable key order for diffable dumps
        for _, k in ipairs({"kind","x","y","w","h","x0","y0","x1","y1",
                            "radius","rgb","rgb2","alpha","filled","text","path"}) do
            if c[k] ~= nil then parts[#parts + 1] = jstr(k) .. ":" .. jval(c[k]) end
        end
        out[#out + 1] = "{" .. table.concat(parts, ",") .. "}" .. (i < #frame and "," or "")
    end
    out[#out + 1] = "]}"
    return table.concat(out)
end

function M.dump_frame(frame, path)
    local f = assert(io.open(path, "w"))
    f:write(M.frame_json(frame))
    f:write("\n")
    f:close()
end

-- ---- install the global `enb` ----------------------------------------------
function M.install(opts)
    M.reset()
    if opts and opts.screen then S.screen_w, S.screen_h = opts.screen[1], opts.screen[2] end

    local enb = {
        base = 0x00400000,
        -- mirror the real enb.addr entries the scripts reference (see src/game.h)
        addr = {
            VitalsBars   = 0x005dbfc0,  -- ctor (NOT a hide target)
            VitalsPaint  = 0x005dcae0,  -- vitals per-frame paint (hide target)
            XpPaint      = 0x0058cf60,  -- xp per-frame paint (hide target)
            SkillLifecycle = 0x0060f1a0,
        },
        WANT_KEY = M.WANT_KEY, WANT_CHAR = M.WANT_CHAR, WANT_MOUSE = M.WANT_MOUSE,
        msg = MSG,

        log = function(s) S.logs[#S.logs + 1] = tostring(s) end,
        screen = function() return S.screen_w, S.screen_h end,
        measure = function(s) return #tostring(s) * 7, 14 end,

        self = function() return S.self_tbl end,
        target = function() return S.target_tbl end,
        state = function() return S.state end,
        cursor = function(on) S.cursor = not not on end,
        patch_ret = function(addr, pop)   -- no-op headless; records the request
            S.patches[#S.patches + 1] = { addr = addr, pop = pop or 0 }
            return true
        end,
        calibrate = function(t)
            for k, v in pairs(t) do S.offsets[k] = v end
        end,
        offsets = function() return S.offsets end,

        on_tick = function(fn) S.ticks[#S.ticks + 1] = fn end,
        on_input = function(fn, mask)
            S.input_fn = fn
            S.input_mask = mask or (M.WANT_KEY | M.WANT_CHAR | M.WANT_MOUSE)
        end,
        on_skill = function() end,
        on_chat = function() end,
        enable_event_hooks = function() end,

        tap  = function(vk) S.taps[#S.taps + 1] = vk; return true end,
        key  = function(vk, down) S.keys[#S.keys + 1] = { vk = vk, down = down }; return true end,
        char = function(cp) S.chars[#S.chars + 1] = cp; return true end,
        hwnd = function() return 0xBEEF end,
        call = function(...) S.calls[#S.calls + 1] = { ... }; return 0 end,
        call_cdecl = function(...) S.calls[#S.calls + 1] = { ... }; return 0 end,

        mem = {
            u32 = function(a) return S.mem[a] or 0 end,
            i32 = function(a) local v = S.mem[a] or 0
                  return v >= 0x80000000 and v - 0x100000000 or v end,
            u16 = function(a) return (S.mem[a] or 0) & 0xFFFF end,
            u8  = function(a) return (S.mem[a] or 0) & 0xFF end,
            f32 = function(a) return ("<f"):unpack(("<I4"):pack(S.mem[a] or 0)) end,
            f64 = function() return 0 end,
            ptr = function(a) return S.mem[a] or 0 end,
            str = function() return "" end,
            wstr = function() return "" end,
            readable = function(a) return S.mem[a] ~= nil end,
            chain = function(base, ...)
                local a = base
                for _, off in ipairs({ ... }) do
                    a = (S.mem[a] or 0); if a == 0 then return 0 end; a = a + off
                end
                return a
            end,
            write_u32 = function(a, v) S.mem[a] = v; return true end,
            write_f32 = function() return true end,
        },

        draw = {
            text = function(x, y, s, rgb)
                S.frame[#S.frame + 1] = { kind = "text", x = x, y = y,
                    text = tostring(s), rgb = rgb or 0xFFFFFF }
            end,
            rect = function(x, y, w, h, rgb, filled, alpha)
                S.frame[#S.frame + 1] = { kind = "rect", x = x, y = y, w = w, h = h,
                    rgb = rgb or 0xFFFFFF, filled = filled ~= false, alpha = alpha or 255 }
            end,
            line = function(x0, y0, x1, y1, rgb, alpha)
                S.frame[#S.frame + 1] = { kind = "line", x0 = x0, y0 = y0, x1 = x1, y1 = y1,
                    rgb = rgb or 0xFFFFFF, alpha = alpha or 255 }
            end,
            rect_grad = function(x, y, w, h, rgb_top, rgb_bot, alpha)
                S.frame[#S.frame + 1] = { kind = "rect_grad", x = x, y = y, w = w, h = h,
                    rgb = rgb_top, rgb2 = rgb_bot, alpha = alpha or 255 }
            end,
            rrect = function(x, y, w, h, radius, rgb, alpha, filled)
                S.frame[#S.frame + 1] = { kind = "rrect", x = x, y = y, w = w, h = h,
                    radius = radius, rgb = rgb or 0xFFFFFF, alpha = alpha or 255,
                    filled = filled ~= false }
            end,
            rrect_grad = function(x, y, w, h, radius, rgb_top, rgb_bot, alpha)
                S.frame[#S.frame + 1] = { kind = "rrect_grad", x = x, y = y, w = w, h = h,
                    radius = radius, rgb = rgb_top, rgb2 = rgb_bot, alpha = alpha or 255 }
            end,
            image = function(path, x, y, w, h, alpha)
                S.frame[#S.frame + 1] = { kind = "image", path = path, x = x, y = y,
                    w = w or 0, h = h or 0, alpha = alpha or 255 }
            end,
        },
    }

    _G.enb = enb
    return M
end

return M
