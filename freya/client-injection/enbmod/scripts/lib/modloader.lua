-- SPDX-License-Identifier: MIT
-- Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
-- License: LICENSES/Freya
--
-- modloader.lua -- discovers and loads the mods staged under scripts/mods/.
--
-- Each mod is a folder under scripts/mods/<id>/ holding a mod.json manifest and
-- its entrypoint Lua file. The launcher's "Configure Mods" UI controls which mod
-- folders get STAGED next to the client, so a disabled mod simply is not present
-- here and never loads -- discovery only ever sees enabled mods.
--
-- mod.json fields:
--   id          stable identifier (defaults to the folder name)
--   name        display name (launcher UI + log)
--   author      display author (launcher UI)
--   description shown as the launcher row tooltip
--   entrypoint  Lua file in the mod folder to run (default "init.lua")

local json = require("json")

local M = {}

local function read_file(path)
    local f = io.open(path, "rb")
    if not f then return nil end
    local s = f:read("*a")
    f:close()
    return s
end

-- Discover mods under <scripts_dir>/mods. Returns an array of manifest tables,
-- each augmented with .id and .dir, sorted by folder name for stable order.
function M.discover(scripts_dir)
    local mods_dir = scripts_dir .. "/mods"
    local out = {}
    local names = enb.list_dir(mods_dir) or {}
    table.sort(names)
    for _, name in ipairs(names) do
        local dir = mods_dir .. "/" .. name
        local txt = read_file(dir .. "/mod.json")
        if txt then
            local ok, meta = pcall(json.decode, txt)
            if ok and type(meta) == "table" then
                meta.id = meta.id or name
                meta.dir = dir
                out[#out + 1] = meta
            else
                enb.log("modloader: bad mod.json in " .. name .. ": " .. tostring(meta))
            end
        end
    end
    return out
end

-- Discover and run every staged mod. Each entrypoint runs in a pcall so one bad
-- mod cannot take the whole HUD down. Returns the discovered manifest list.
function M.load_all(scripts_dir)
    local mods = M.discover(scripts_dir)
    for _, m in ipairs(mods) do
        local entry = m.entrypoint or "init.lua"
        local path = m.dir .. "/" .. entry
        -- let a mod require its own sibling files by short name
        package.path = m.dir .. "/?.lua;" .. package.path
        local ok, err = pcall(dofile, path)
        if ok then
            enb.log("mod loaded: " .. (m.name or m.id))
        else
            enb.log("mod FAILED [" .. tostring(m.id) .. "]: " .. tostring(err))
        end
    end
    return mods
end

return M
