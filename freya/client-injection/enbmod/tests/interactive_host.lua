-- SPDX-License-Identifier: MIT
-- Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
-- License: LICENSES/Freya
--
-- interactive_host.lua -- a long-lived REPL that runs the REAL mod scripts
-- (scripts/*.lua) against tests/mock_enb.lua and drives them from line
-- commands on stdin, emitting the resulting draw frame on stdout. The Python
-- preview server (preview_server.py) is the front end: it captures the
-- browser's mouse/keyboard, forwards them here, and renders what comes back
-- over the game-screen background. This is how the UI is exercised
-- INTERACTIVELY -- click handlers, hover, keybinds -- with no client / WINE.
--
-- PROTOCOL (one command per stdin line; whitespace-separated ints):
--   screen <w> <h>            set enb.screen() (defaults to the bg size)
--   state <name>              set enb.state() (space/station/login/charsel/load)
--   self cal | self uncal     toggle a demo player object (stat/xp values)
--   input <msg> <wp> <lp>     dispatch one Win32 message -> on_input handler;
--                             replies one line:  SWALLOW <0|1>
--   tick                      run on_tick once; replies:  FRAME <json>
--   taps                      replies:  TAPS <count> -- synthesized keybinds
--                             fired so far (proves click -> tap path works)
--   ping                      replies:  PONG
-- Unknown lines reply:  ERR <text>.  Errors never crash the host (fail-open).

local here = arg[1] or "."          -- tests/ dir, for require paths
-- After the mod restructure the HUD scripts live under scripts/lib (shared) and
-- scripts/mods/<id>/ (entrypoints), so resolve all three.
local scripts = here .. "/../scripts"
package.path = here .. "/?.lua;" ..
               scripts .. "/?.lua;" ..
               scripts .. "/lib/?.lua;" ..
               scripts .. "/mods/freya-hud/?.lua;" ..
               scripts .. "/mods/hide-ui/?.lua;" .. package.path

local mock = require("mock_enb")
mock.install{ screen = { 1280, 960 } }   -- match enb-mod-bg.png by default

require("xp_overlay")
require("freya_ui")

-- demo player object so the calibrated path (stat fills, xp numbers) is
-- viewable without a live game. Values mirror screenshots_spec scenario 02.
local DEMO = {
    base = 0x05000000,
    name = "JETHREE",
    hull = 750, hull_max = 1000,
    shield = 400, shield_max = 1000,
    energy = 900, energy_max = 1000,
    combat_lvl = 23, combat_pct = 41,
    explore_lvl = 31, explore_pct = 87,
    trade_lvl = 12, trade_pct = 5,
}

local function reply(s) io.write(s, "\n"); io.flush() end

io.stdout:setvbuf("line")
for line in io.lines() do
    local cmd, rest = line:match("^(%S+)%s*(.*)$")
    if cmd == "tick" then
        local ok, frame = pcall(mock.tick)
        if ok then reply("FRAME " .. mock.frame_json(frame))
        else reply("ERR tick: " .. tostring(frame)) end
    elseif cmd == "input" then
        local msg, wp, lp = rest:match("^(%-?%d+)%s+(%-?%d+)%s+(%-?%d+)")
        if msg then
            local ok, sw = pcall(mock.input, tonumber(msg), tonumber(wp), tonumber(lp))
            reply("SWALLOW " .. ((ok and sw) and "1" or "0"))
        else
            reply("ERR bad input args")
        end
    elseif cmd == "screen" then
        local w, h = rest:match("^(%d+)%s+(%d+)")
        if w then mock.set_screen(tonumber(w), tonumber(h)); reply("OK")
        else reply("ERR bad screen args") end
    elseif cmd == "state" then
        local name = rest:match("^(%S+)")
        if name then mock.set_game_state(name); reply("OK")
        else reply("ERR bad state arg") end
    elseif cmd == "self" then
        if rest:match("cal") and not rest:match("uncal") then mock.set_self(DEMO)
        else mock.set_self{ base = 0 } end
        reply("OK")
    elseif cmd == "taps" then
        reply("TAPS " .. #mock.state().taps)
    elseif cmd == "ping" then
        reply("PONG")
    elseif cmd == nil or cmd == "" then
        -- ignore blank lines
    else
        reply("ERR unknown cmd: " .. tostring(cmd))
    end
end
