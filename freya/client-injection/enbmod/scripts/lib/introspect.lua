-- SPDX-License-Identifier: MIT
-- Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
-- License: LICENSES/Freya
--
-- introspect.lua -- look inside a loaded mod from the /run chat console, including
-- its PRIVATE (closed-over local) state.
--
-- enb.mods[id].module only exposes what a mod chose to RETURN. A mod's private
-- locals (e.g. `local privateMap = {}`) are invisible there -- but if any function
-- the mod returned closes over them, they survive as upvalues and the debug
-- library can recover them. introspect() walks the module's functions, harvests
-- their upvalues, and presents them under `.upvalues` so you can drill in:
--
--   /run introspect("freya-hud")                       -- manifest + module + upvalues
--   /run introspect("freya-hud").upvalues              -- just the private state
--   /run introspect("freya-hud").upvalues.privateMap   -- a specific captured local
--   /run introspect("freya-hud").upvalues.privateMap["abc"]
--
-- Limit: a private local only shows up if some RETURNED function closes over it.
-- State captured solely by callbacks handed to enb.on_tick/on_chat/... lives on
-- the C++ side and is not reachable from Lua.

-- Harvest every upvalue from functions reachable in `root` (a module table or a
-- function), descending nested tables up to maxdepth. Returns name -> value.
local function harvest(root, maxdepth)
    local up = {}
    if not (debug and debug.getupvalue) then
        up._note = "debug library unavailable -- cannot read upvalues"
        return up
    end
    local seen_fn, seen_tbl = {}, {}
    local function scan_fn(fn)
        if seen_fn[fn] then return end
        seen_fn[fn] = true
        local i = 1
        while true do
            local name, val = debug.getupvalue(fn, i)
            if not name then break end
            if name ~= "_ENV" then up[name] = val end
            i = i + 1
        end
    end
    local function walk(v, depth)
        local t = type(v)
        if t == "function" then
            scan_fn(v)
        elseif t == "table" and depth <= maxdepth and not seen_tbl[v] then
            seen_tbl[v] = true
            for _, vv in pairs(v) do walk(vv, depth + 1) end
        end
    end
    walk(root, 0)
    return up
end

-- introspect(modid[,opts]) -> a view table (id/name/ok/module/upvalues), or a
-- small {error=..., available={...}} table when the id is unknown.
-- opts.depth: how deep to descend the module looking for functions (default 2).
local function introspect(modid, opts)
    opts = opts or {}
    local reg = (enb and enb.mods) or {}
    local entry = reg[modid]
    if not entry then
        local ids = {}
        for id in pairs(reg) do ids[#ids + 1] = id end
        table.sort(ids)
        return { error = "no such mod: " .. tostring(modid), available = ids }
    end
    return {
        id         = entry.id,
        name       = entry.name,
        author     = entry.author,
        ok         = entry.ok,
        error      = entry.error,
        entrypoint = entry.entrypoint,
        module     = entry.module,
        upvalues   = harvest(entry.module, opts.depth or 2),
    }
end

return introspect
