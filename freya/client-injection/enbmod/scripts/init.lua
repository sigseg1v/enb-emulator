-- SPDX-License-Identifier: MIT
-- Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
-- License: LICENSES/Freya
--
-- init.lua -- loaded by enbmod.dll on startup, hot-reloaded when this file changes.
--
-- This file is the bootstrap: it applies persisted calibration and then hands off
-- to the mod loader, which discovers and runs every mod staged under
-- scripts/mods/. The launcher's "Configure Mods" window controls which mods are
-- staged; a disabled mod is simply absent from scripts/mods/ and never loads.
--
-- The C++ core gives you (see lib/ and the mod entrypoints for real usage):
--   enb.log(s)                     write to enbmod.log
--   enb.base                       0x00400000 (image base)
--   enb.script_dir                 absolute path of the staged scripts/ folder
--   enb.addr.<Name>                static code addresses (see enb.addr table / INJECTION-MAP.md)
--   enb.list_dir(path) -> {names}  directory listing (used by the mod loader)
--   enb.mem.u8/u16/u32/i32/f32/f64/ptr(addr)     guarded reads (never crash the game)
--   enb.mem.str/wstr/readable/chain/write_u32/write_f32
--   enb.calibrate{...} / enb.offsets()           runtime struct offsets
--   enb.self() / enb.target()                     player + target tables
--   enb.state() -> space/station/login/charsel/load/unknown
--   enb.on_tick(fn) / enb.on_input(fn[,mask]) / enb.on_skill(fn) / enb.on_chat(fn)
--   enb.enable_event_hooks()
--   enb.enable_inspace() / enb.inspace()         in-space heartbeat (zero-calib)
--   enb.screen() / enb.measure(s)
--   enb.draw.* (overlay) / enb.tap/key/char/hwnd / enb.call/call_cdecl
--   enb.patch_ret(addr[,pop])      overwrite a function entry with a `ret`
--   enb.cursor(on)

enb.log("init.lua running")

-- dump(v[,opts]): global value pretty-printer for the "/run" chat console. The
-- console auto-dumps a returned table, but `dump` is also callable directly
-- (e.g. `/run dump(enb.self(), {depth=2})`) to inspect any live data structure.
dump = require("dump")

-- Persisted calibration: autocalibrate writes scripts/calib_data.lua (gitignored,
-- install-specific). Apply it here so calibration survives restarts/hot-reloads.
do
    local ok, data = pcall(require, "calib_data")
    if ok and type(data) == "table" then
        enb.calibrate(data)
        enb.log("init.lua: applied persisted calib_data.lua")
    end
end

-- In-space heartbeat: install the read-only hook on the per-frame vitals updater
-- so enb.state()/enb.inspace() can positively report "space" with NO offset
-- calibration. The HUD's visibility gate depends on this; without it state stays
-- "unknown" forever (the pre-calibration default) and we can't tell space apart
-- from the front-end. Safe to call even if it fails (logged) -- the HUD just
-- falls back to the "unknown" skeleton.
if enb.enable_inspace then
    enb.log("init.lua: enable_inspace() -> " .. tostring(enb.enable_inspace()))
end

-- State-transition logger. The game exposes no single game-state global we've
-- calibrated yet, so log every change in what we CAN observe -- enb.state() and
-- the raw inspace heartbeat -- to discover the actual transitions the game fires
-- (front-end -> char select -> load -> space -> station -> ...). One line per
-- change only, so the log isn't spammed every frame.
do
    local last_state, last_inspace
    enb.on_tick(function()
        local s = (enb.state and enb.state()) or "unknown"
        local ins = (enb.inspace and enb.inspace()) or false
        if s ~= last_state then
            enb.log(string.format("state: %s -> %s (inspace=%s)",
                tostring(last_state), s, tostring(ins)))
            last_state = s
        end
        if ins ~= last_inspace then
            enb.log("inspace heartbeat -> " .. tostring(ins))
            last_inspace = ins
        end
    end)
end

-- Discover and load every staged (= enabled) mod.
local modloader = require("modloader")
local mods = modloader.load_all(enb.script_dir or "scripts")
enb.log(string.format("init.lua: %d mod(s) loaded", #mods))

-- Mod registry for the /run console. enb.mods is keyed by mod id; each entry is
-- the manifest plus runtime state (.ok / .module / .error). Introspect from chat:
--   /run dump(enb.mods, {depth = 1})        -- list every loaded mod by id
--   /run enb.mods["player-hud"]             -- one mod's manifest + state
--   /run enb.mods["player-hud"].module      -- that mod's returned module table
enb.mods = {}
for _, m in ipairs(mods) do enb.mods[m.id] = m end

-- introspect(modid): inspect a loaded mod incl. its private (closed-over) state.
--   /run introspect("player-hud").upvalues.privateMap["abc"]
introspect = require("introspect")
