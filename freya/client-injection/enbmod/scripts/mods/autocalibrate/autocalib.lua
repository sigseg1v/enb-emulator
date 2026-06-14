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

-- ---- continuous vitals change-watcher --------------------------------------
-- A single in-space snapshot can't tell WHICH 0..1 float is the live bar fill
-- when every bar is full (they all read 1.0 = cur/max with cur==max). This
-- watcher re-reads the gadget + entity objects every frame and logs any dword
-- that CHANGES, so the live offset reveals itself the instant a bar moves --
-- fire weapons / boost (drains energy), or take a hit (shield/hull). Each change
-- is shown as both float and int, so a flat cur/max integer is caught too if one
-- exists. Per-offset throttle + a hard log cap keep animation tweens from
-- spamming the log.
local W = { last = {}, log_n = {}, log_t = {}, tick = 0 }

local function classify(u, f)
    if f == f and f > -0.01 and f <= 1.5 then
        return ("%.4f (frac)"):format(f)
    elseif f == f and math.abs(f) > 1e-3 and math.abs(f) < 1e9 then
        return ("%.3f (f) / %d"):format(f, u)
    else
        return ("%d (0x%x)"):format(u, u)
    end
end

-- watch one object; skip_ptrs drops pointer-looking dwords (entity is full of
-- them and they churn as the heap moves -- only numeric fields interest us).
local function watch_obj(name, base, count, skip_ptrs)
    if not base or base == 0 then return end
    local lt = W.last[name];  if not lt then lt = {}; W.last[name]  = lt end
    local ln = W.log_n[name]; if not ln then ln = {}; W.log_n[name] = ln end
    local lg = W.log_t[name]; if not lg then lg = {}; W.log_t[name] = lg end
    for i = 0, count - 1 do
        local a = base + i * 4
        if not enb.mem.readable(a, 4) then break end
        local u = enb.mem.u32(a)
        local rec = lt[i]
        if rec and rec.u ~= u then
            local ptrish = (u >= 0x00400000) and enb.mem.readable(u, 4)
            local under_cap = (ln[i] or 0) < 16
            local cooled = (lg[i] == nil) or (W.tick - lg[i] > 6)
            if not (skip_ptrs and ptrish) and under_cap and cooled then
                local f = enb.mem.f32(a)
                enb.log(("[watch] %s +0x%03x: %s -> %s"):format(
                    name, i * 4, classify(rec.u, rec.f), classify(u, f)))
                ln[i] = (ln[i] or 0) + 1
                lg[i] = W.tick
            end
            lt[i] = { u = u, f = enb.mem.f32(a) }
        elseif not rec then
            lt[i] = { u = u, f = enb.mem.f32(a) }
        end
    end
end

function M.watch_vitals()
    local ctrl = enb.vitals_ctrl and enb.vitals_ctrl() or 0
    if ctrl == 0 then return end
    W.tick = W.tick + 1
    for _, g in ipairs({ { "ENERGY", 0x1c }, { "SHIELD", 0x20 }, { "HULL", 0x24 } }) do
        local gp = enb.mem.readable(ctrl + g[2], 4) and enb.mem.u32(ctrl + g[2]) or 0
        watch_obj(g[1], gp, 0x28, false)   -- gadget body ~0xa0 bytes
    end
    local data = enb.mem.readable(ctrl + 4, 4) and enb.mem.u32(ctrl + 4) or 0
    local entity = (data ~= 0 and enb.mem.readable(data + 0x88, 4)) and enb.mem.u32(data + 0x88) or 0
    watch_obj("ENTITY", entity, 0x60, true)   -- 0x180 bytes, numeric fields only
end

-- ---- discipline level / xp probe -------------------------------------------
-- The player level + Combat/Explore/Trade levels & xp% are NOT flat fields in
-- the dumped vitals objects -- they live behind the RPGInfo / AuxData property
-- bag, so value-typing a single snapshot can't find them (the levels read 0 on
-- a low character, and the on-screen xp% is computed from cur/next-xp ints, not
-- stored as a float). This walks the pointer graph out from the player entity a
-- couple of levels deep and flags: word-like string keys (structure anchors
-- like "Combat"/"Explore"/"Level"/"Experience"), small ints (candidate levels),
-- and integer PAIRS whose ratio matches a known on-screen xp% (pin cur/next xp).
-- Run by hand with the on-screen % to filter precisely:
--   require("autocalib").probe_levels(54)   -- explore at 54%
local function wordlike(c) return c and #c >= 3 and #c <= 40 and c:match("^[%w _'%-]+$") end

function M.probe_levels(target_pct)
    local ctrl = enb.vitals_ctrl and enb.vitals_ctrl() or 0
    if ctrl == 0 then enb.log("[lvl] vitals_ctrl 0 -- not in space"); return end
    local data   = enb.mem.readable(ctrl + 4, 4) and enb.mem.u32(ctrl + 4) or 0
    local entity = (data ~= 0 and enb.mem.readable(data + 0x88, 4)) and enb.mem.u32(data + 0x88) or 0
    enb.log(("[lvl] ===== level/xp probe; entity=%08x target_pct=%s ====="):format(
        entity, tostring(target_pct)))
    local seen, lines = {}, 0
    local LINECAP, QCAP = 160, 300
    local function logln(s) if lines < LINECAP then enb.log(s); lines = lines + 1 end end
    local queue, qi = { { entity, "ent", 0 }, { data, "dat", 0 } }, 1
    while qi <= #queue do
        local node = queue[qi]; qi = qi + 1
        local base, path, depth = node[1], node[2], node[3]
        if base ~= 0 and not seen[base] and enb.mem.readable(base, 4) then
            seen[base] = true
            for i = 0, 0x5f do
                local a = base + i * 4
                if not enb.mem.readable(a, 4) then break end
                local u = enb.mem.u32(a)
                if u >= 0x00400000 and enb.mem.readable(u, 4) then
                    local c = enb.mem.str(u)
                    if wordlike(c) then logln(("[lvl] %s+0x%03x -> %q"):format(path, i * 4, c)) end
                end
                if u >= 1 and u <= 99 then logln(("[lvl] %s+0x%03x = %d (lvl?)"):format(path, i * 4, u)) end
                if enb.mem.readable(a + 4, 4) then
                    local b = enb.mem.u32(a + 4)
                    if u > 0 and b > u and b < 5e7 then
                        local r = u / b
                        local hit = target_pct and math.abs(r * 100 - target_pct) <= 1.5
                        if hit or (not target_pct and r >= 0.05 and r <= 0.95 and b < 100000) then
                            logln(("[lvl] %s+0x%03x cur=%d next=%d -> %.1f%%%s"):format(
                                path, i * 4, u, b, r * 100, hit and " XP-MATCH" or ""))
                        end
                    end
                end
                if depth < 2 and #queue < QCAP and u >= 0x00400000
                   and enb.mem.readable(u, 8) and not seen[u] then
                    queue[#queue + 1] = { u, ("%s+0x%03x"):format(path, i * 4), depth + 1 }
                end
            end
        end
    end
    enb.log(("[lvl] ===== end level/xp probe (%d lines, %d objs) ====="):format(lines, qi - 1))
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

-- Auto-run the vitals probe ONCE ~1s after first entering space (gadgets need a
-- few frames to populate), THEN keep the change-watcher running every frame.
-- Fully automatic: enter space (one-shot [probe] dump), then move a bar -- fire
-- weapons / boost (drains energy) or take a hit (shield/hull) -- and the live
-- fill offset shows up in the [watch] lines. Re-run the snapshot by hand with
-- require("autocalib").probe_vitals().
do
    local probed = false
    local space_ticks = 0
    enb.on_tick(function()
        if not (enb.inspace and enb.inspace()) then space_ticks = 0; return end
        space_ticks = space_ticks + 1
        if not probed and space_ticks >= 60 then
            probed = true
            local ok, err = pcall(M.probe_vitals)
            if not ok then enb.log("[probe] error: " .. tostring(err)) end
            local lok, lerr = pcall(M.probe_levels)
            if not lok then enb.log("[lvl] error: " .. tostring(lerr)) end
            enb.log("[watch] armed -- fire weapons / boost / take a hit to move a bar")
        end
        if probed then
            local ok, err = pcall(M.watch_vitals)
            if not ok then enb.log("[watch] error: " .. tostring(err)) end
        end
    end)
    enb.log("autocalib: armed -- auto-probe + change-watch after entering space")
end

return M
