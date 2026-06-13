-- calib.lua -- runtime helpers for FINDING the offsets you must put in enb.calibrate{}.
-- require it from init.lua:  local calib = require("calib")
-- Everything here is read-only and crash-safe (uses guarded enb.mem.*).

local M = {}

-- Dump a block of an object as hex dwords + plausible interpretations (int/float/ptr).
-- Use after you've found a candidate base address to eyeball which offset holds hull/shield/etc.
function M.dump(base, count)
    count = count or 64
    enb.log(("--- dump %08x (%d dwords) ---"):format(base, count))
    for i = 0, count - 1 do
        local a = base + i * 4
        if not enb.mem.readable(a, 4) then break end
        local u = enb.mem.u32(a)
        local f = enb.mem.f32(a)
        local note = ""
        if f == f and f ~= 0 and math.abs(f) > 1e-4 and math.abs(f) < 1e9 then
            note = ("  ~f=%.3f"):format(f)
        end
        if u >= 0x00400000 and u <= 0x01000000 and enb.mem.readable(u, 4) then
            note = note .. "  ptr?->code/data"
        elseif u >= 0x01000000 and enb.mem.readable(u, 4) then
            note = note .. "  ptr?->heap"
        end
        enb.log(("+0x%03x: %08x (%d)%s"):format(i * 4, u, u, note))
    end
end

-- Scan the .data segment for dwords that point into the heap and whose target, at +probe_off,
-- holds the value you expect (e.g. you know hull is 1000 right now). Helps locate player_ptr_addr.
-- data_lo/hi default to the observed .data range.
function M.find_ptr_to_value(expected, probe_off, data_lo, data_hi)
    data_lo = data_lo or 0x00B6C000
    data_hi = data_hi or 0x00C00000
    probe_off = probe_off or 0
    local hits = 0
    for a = data_lo, data_hi - 4, 4 do
        local p = enb.mem.u32(a)
        if p >= 0x01000000 and enb.mem.readable(p + probe_off, 4) then
            if enb.mem.u32(p + probe_off) == expected then
                enb.log(("candidate player_ptr_addr=%08x -> obj %08x"):format(a, p))
                hits = hits + 1
                if hits >= 20 then enb.log("...stopping at 20") return end
            end
        end
    end
    enb.log(("scan done, %d hits"):format(hits))
end

-- Watch an address and log when it changes -- good for confirming a field is what you think.
local watches = {}
function M.watch(addr, label)
    watches[addr] = { label = label or ("%08x"):format(addr), last = nil }
end
function M.pump_watches()
    for addr, w in pairs(watches) do
        local v = enb.mem.u32(addr)
        if v ~= w.last then
            enb.log(("watch %s @%08x: %d (%08x)"):format(w.label, addr, v, v))
            w.last = v
        end
    end
end

return M
