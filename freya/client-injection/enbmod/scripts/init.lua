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
--   enb.screen() / enb.measure(s)
--   enb.draw.* (overlay) / enb.tap/key/char/hwnd / enb.call/call_cdecl
--   enb.patch_ret(addr[,pop])      overwrite a function entry with a `ret`
--   enb.cursor(on)

enb.log("init.lua running")

-- Persisted calibration: autocalibrate writes scripts/calib_data.lua (gitignored,
-- install-specific). Apply it here so calibration survives restarts/hot-reloads.
do
    local ok, data = pcall(require, "calib_data")
    if ok and type(data) == "table" then
        enb.calibrate(data)
        enb.log("init.lua: applied persisted calib_data.lua")
    end
end

-- Discover and load every staged (= enabled) mod.
local modloader = require("modloader")
local mods = modloader.load_all(enb.script_dir or "scripts")
enb.log(string.format("init.lua: %d mod(s) loaded", #mods))
