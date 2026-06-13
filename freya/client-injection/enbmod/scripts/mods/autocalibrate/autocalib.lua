-- autocalib.lua -- guided runtime calibration for enb.self() (Phase AS-3).
--
-- The struct offsets in game.h default to "unknown", so enb.self() returns
-- { base = 0 } until you find the player-object pointer and field offsets for
-- THIS client build. They are install-specific, so they are not committed:
-- this helper finds them at runtime and writes scripts/calib_data.lua (gitignored),
-- which init.lua loads on the next start / hot-reload.
--
-- AUTOMATIC (preferred): just enter space. This mod auto-runs probe_vitals()
-- ~1s after you enter space and dumps the live hull/shield/energy gadget chain
-- (reached from the heartbeat-captured controller, no scanning) to enbmod.log
-- with per-offset float/int/ptr annotations. Read the constant field offsets off
-- that dump -- they are the same for every install of this client build.
--
-- MANUAL fallback (older flat-field scan, in-game with the chat HUD visible):
--   local ac = require("autocalib")
--   ac.probe_vitals()    -- re-run the automatic probe on demand
--   ac.find(1000)        -- pass your CURRENT hull value; scans .data for the
--                        -- pointer whose object holds that number. Prints
--                        -- candidate player_ptr_addr lines to enbmod.log.
--   ac.probe(0xADDR)     -- hex-dump the candidate object; eyeball which +0x..
--                        -- offsets hold hull/shield/energy/levels (match the HUD).
--   ac.save{             -- once you know them, persist + apply:
--       player_ptr_addr = 0x00B6xxxx,
--       hull = 0x.., hull_max = 0x.., shield = 0x.., shield_max = 0x..,
--       energy = 0x.., energy_max = 0x.., ... }
--
-- save() both calls enb.calibrate{...} (live now) AND writes calib_data.lua so
-- it survives a restart. There is no fully-automatic path: the field-offset
-- identification needs a human matching dumped values against the on-screen HUD.

local calib = require("calib")

local M = {}

-- Scan .data for the pointer leading to an object whose +probe_off dword equals
-- `hull_now`. probe_off defaults to 0 (try a few if the first guess misses).
function M.find(hull_now, probe_off, lo, hi)
    enb.log(("autocalib: scanning for hull=%d (probe_off=0x%x)"):format(
        hull_now, probe_off or 0))
    calib.find_ptr_to_value(hull_now, probe_off or 0, lo, hi)
end

-- Hex-dump a candidate object so you can read off field offsets.
function M.probe(base, count)
    calib.dump(base, count or 64)
end

-- ---- automatic in-space vitals probe ---------------------------------------
-- The vitals (hull/shield/energy) are NOT flat fields on a player struct: the
-- client reads them through a named-property "auxdata" system. But the live
-- vitals-controller gadget is captured every frame by the in-space heartbeat
-- hook (enb.vitals_ctrl()), and the controller's layout is known:
--   ctrl                         the vitals controller (this)
--   [ctrl + 0x04]                its data object
--   [[ctrl + 0x04] + 0x88]       the player ship ENTITY (source of the values)
--   [ctrl + 0x1c]                ENERGY gadget (progress bar)
--   [ctrl + 0x20]                SHIELD gadget
--   [ctrl + 0x24]                HULL   gadget
-- Each gadget is fed its current fraction via a SetValue vtable call, so after a
-- frame the gadget stores a 0.0..1.0 float = the live bar fill. probe_vitals()
-- walks this chain and dumps each object with float/int/ptr annotations and a
-- "<= 0..1 (percent?)" flag, so the constant field offsets can be read off in ONE
-- in-space run and baked into game.h -- no value-typing, no memory scan.
local function annotate(a)
    if not enb.mem.readable(a, 4) then return nil end
    local u = enb.mem.u32(a)
    local f = enb.mem.f32(a)
    local note = ("%08x (%d)"):format(u, u)
    if f == f and f > 0.0 and f <= 1.0001 then
        note = note .. ("  <= %.4f  PERCENT?"):format(f)
    elseif f == f and f ~= 0 and math.abs(f) > 1e-3 and math.abs(f) < 1e9 then
        note = note .. ("  ~f=%.3f"):format(f)
    end
    if u >= 0x00400000 and u <= 0x01000000 and enb.mem.readable(u, 4) then
        note = note .. "  ptr->code/data"
    elseif u >= 0x01000000 and enb.mem.readable(u, 8) then
        note = note .. "  ptr->heap"
    end
    return note
end

local function dump_obj(label, base, count)
    if not base or base == 0 or not enb.mem.readable(base, 4) then
        enb.log(("[probe] %s @%s -- not readable"):format(label, tostring(base and ("%08x"):format(base) or "nil")))
        return
    end
    enb.log(("[probe] %s @%08x:"):format(label, base))
    for i = 0, (count or 48) - 1 do
        local n = annotate(base + i * 4)
        if not n then break end
        enb.log(("    +0x%03x: %s"):format(i * 4, n))
    end
end

-- Scan an object for a pointer to a wide (UTF-16) string -- anchors the player
-- entity by matching the on-screen character name.
local function scan_for_name(label, base, count)
    if not base or base == 0 then return end
    for i = 0, (count or 64) - 1 do
        local a = base + i * 4
        if not enb.mem.readable(a, 4) then break end
        local p = enb.mem.u32(a)
        if p >= 0x00400000 and enb.mem.readable(p, 4) then
            local w = enb.mem.wstr(p)
            local c = enb.mem.str(p)
            if w and #w >= 2 and #w <= 32 and w:match("^[%w%s%-_']+$") then
                enb.log(("    +0x%03x -> wstr %q"):format(i * 4, w))
            elseif c and #c >= 2 and #c <= 32 and c:match("^[%w%s%-_']+$") then
                enb.log(("    +0x%03x -> cstr %q"):format(i * 4, c))
            end
        end
    end
end

-- The fully-automatic probe. Pulls the live controller from the hook and dumps
-- the whole known chain. Run it in space (init.lua auto-runs it once on first
-- space entry; re-run by hand with `require("autocalib").probe_vitals()`).
function M.probe_vitals()
    local ctrl = enb.vitals_ctrl and enb.vitals_ctrl() or 0
    enb.log(("[probe] ===== vitals probe; vitals_ctrl=%08x ====="):format(ctrl))
    if ctrl == 0 then
        enb.log("[probe] vitals_ctrl is 0 -- not in space yet, or heartbeat hook off. Aborting.")
        return
    end
    dump_obj("controller", ctrl, 16)
    local data = enb.mem.readable(ctrl + 4, 4) and enb.mem.u32(ctrl + 4) or 0
    dump_obj("data [ctrl+4]", data, 48)
    local entity = (data ~= 0 and enb.mem.readable(data + 0x88, 4)) and enb.mem.u32(data + 0x88) or 0
    dump_obj("ENTITY [[ctrl+4]+0x88]", entity, 96)
    enb.log("[probe] scanning ENTITY for name strings (match your character name):")
    scan_for_name("entity", entity, 96)
    for _, g in ipairs({ { "ENERGY", 0x1c }, { "SHIELD", 0x20 }, { "HULL", 0x24 } }) do
        local gp = enb.mem.readable(ctrl + g[2], 4) and enb.mem.u32(ctrl + g[2]) or 0
        dump_obj(("%s gadget [ctrl+0x%x]"):format(g[1], g[2]), gp, 48)
    end
    enb.log("[probe] ===== end vitals probe; copy the +0x.. PERCENT? offsets =====")
end

-- The fields autocalib knows how to persist (mirror of game.h Offsets).
local FIELDS = {
    "player_ptr_addr",
    "hull", "hull_max", "shield", "shield_max", "energy", "energy_max",
    "combat_lvl", "trade_lvl", "explore_lvl",
    "combat_pct", "trade_pct", "explore_pct", "skill_points",
    "pos_x", "pos_y", "pos_z",
    "target_ptr_addr", "tgt_name", "tgt_name_is_ptr", "tgt_name_wide",
    "tgt_hull", "tgt_pos_x", "tgt_pos_y", "tgt_pos_z",
}

local function calib_data_path()
    -- package.path points at <mod>\scripts\?.lua; derive the dir from it.
    local p = package.path:match("([^;]*)\\scripts\\%?%.lua") or "."
    return p .. "\\scripts\\calib_data.lua"
end

-- Apply offsets live (enb.calibrate) and persist them to calib_data.lua.
function M.save(t)
    enb.calibrate(t)
    local path = calib_data_path()
    local f, err = io.open(path, "w")
    if not f then
        enb.log("autocalib: cannot write " .. path .. ": " .. tostring(err))
        return false
    end
    f:write("-- calib_data.lua -- generated by autocalib.save(). Gitignored.\n")
    f:write("-- Install-specific offsets for enb.self(); loaded by init.lua.\n")
    f:write("return {\n")
    for _, k in ipairs(FIELDS) do
        if t[k] ~= nil then
            f:write(("    %s = 0x%X,\n"):format(k, t[k]))
        end
    end
    f:write("}\n")
    f:close()
    enb.log("autocalib: wrote " .. path)
    return true
end

-- Auto-run the vitals probe ONCE, ~1s after first entering space (gadgets need a
-- few frames to populate). Fully automatic: enter space, read the annotated dump
-- in enbmod.log. Re-run any time by hand with require("autocalib").probe_vitals().
do
    local done = false
    local space_ticks = 0
    enb.on_tick(function()
        if done then return end
        if enb.inspace and enb.inspace() then
            space_ticks = space_ticks + 1
            if space_ticks >= 60 then
                done = true
                local ok, err = pcall(M.probe_vitals)
                if not ok then enb.log("[probe] error: " .. tostring(err)) end
            end
        else
            space_ticks = 0
        end
    end)
    enb.log("autocalib: armed -- auto-probe ~1s after entering space")
end

return M
