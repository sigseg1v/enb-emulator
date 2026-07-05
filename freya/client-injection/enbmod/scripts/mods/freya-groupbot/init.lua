-- SPDX-License-Identifier: MIT
-- Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
-- License: LICENSES/Freya
--
-- freya-groupbot -- group-formation automation for a multiboxed party.
--
-- Three behaviours the party owner asked for, split by what the exposed enb.* API
-- can actually support reliably:
--
--   (2) reform-after-gate (LEADER):  when the leader re-enters space after a gate,
--       re-issue the group's formation TYPE (enb.group_action 4/5/6). Solid.
--   (3) rejoin-after-gate (MEMBER):  when a member re-enters space after a gate,
--       re-issue "form up" (enb.group_action 7). Solid.
--   (1) follow-gate (MEMBER):        auto-jump a designated ship's stargate when it
--       gates. HEURISTIC + default-OFF -- see the block above follow_gate() for why
--       the API cannot do the "detect the LEADER gated" version cleanly.
--
-- (2)/(3) key off ONE robust signal: enb.inspace() going false->true. A gate runs
-- the client through a loading screen (in-space heartbeat stops), then space
-- resumes -- so the rising edge means "just entered a (new) sector". Undock and the
-- initial login also raise it; re-issuing a formation CTA in those cases is a
-- harmless no-op when solo (group_action no-ops with no group) or already formed.
--
-- SAFETY: this mod SENDS packets on the wire on its own. It therefore runs on a
-- coarse tick divider (never per-frame -- see the no-per-tick-instrumentation rule
-- that froze a live client once), fires each action at most once per sector entry
-- (debounced), and the flaky follow-gate half is a SEPARATE default-OFF switch.
-- Toggle everything from the /run console via the global `groupbot` table (bottom).

local M = {}

-- ---- config / state --------------------------------------------------------
local S = {
    reform_on   = true,   -- (2)/(3): re-issue formation on our own sector entry
    follow_on   = false,  -- (1): follow a designated ship through its gate (heuristic)
    formation   = 4,      -- leader's formation type to re-establish: 4 Slot Back / 5 Block / 6 Pipe
    follow_gid  = 0,      -- the ship this client tail-gates (0 = none set)

    -- runtime
    was_inspace = false,
    tick        = 0,
    -- follow-gate tracking
    seen_gid    = false,  -- was follow_gid a live object last heavy sample?
    near_gate   = 0,      -- gid of the stargate follow_gid was hugging (0 = none)
    miss        = 0,      -- consecutive heavy samples follow_gid has been absent
    cooldown    = 0,      -- heavy-samples remaining before follow-gate may fire again
}

-- how many raw ticks between samples. The pump fires many times/sec, so a large
-- divider keeps this cheap. LIGHT (inspace edge + formation) is safe to sample
-- often; HEAVY (the enb.objects() entity walk for follow-gate) runs far less.
local LIGHT_DIV = 15
local HEAVY_DIV = 45
local GATE_HUG  = 6000   -- follow_gid must be within this of a gate to arm a follow
local MISS_ARM  = 2      -- consecutive absent samples before we treat it as "gated"
local COOLDOWN  = 6      -- heavy samples (~ a few sec) to suppress re-firing after a jump

-- ---- helpers ---------------------------------------------------------------
local function in_group()
    local g = enb.group and enb.group()
    return g and g.count and g.count > 0, g
end

-- (2)/(3): fire the right formation CTA for our role, once, on sector entry.
local function reform()
    local grouped = in_group()
    if not grouped then return end
    if enb.is_leader and enb.is_leader() then
        enb.group_action(S.formation)                 -- re-establish formation type
        enb.log("groupbot: re-established formation " .. tostring(S.formation) .. " (leader)")
    else
        enb.group_action(7)                           -- form up
        enb.log("groupbot: rejoined formation (member)")
    end
end

-- (1) follow-gate. HONEST LIMITS -- why this is heuristic and default-OFF:
--   * enb.group() does NOT flag which roster member is the leader (only is_leader()
--     tells whether WE are), so "follow the LEADER" is not expressible -- the ship
--     to tail must be DESIGNATED by the user (groupbot.follow(gid)).
--   * there is no leader-sector / leader-position signal, so "the leader gated" is
--     inferred from the designated ship's live object: it was hugging a stargate,
--     then it dropped out of scanner range. That is a race (it could have just
--     warped off, or gone out of range for another reason), hence the debounce +
--     cooldown and the default-OFF gate.
-- When armed, we target the remembered gate and enb.gate() through it.
local function follow_gate()
    if S.follow_gid == 0 then return end
    if S.cooldown > 0 then S.cooldown = S.cooldown - 1 end

    -- find the designated ship + the nearest stargate to it, in one object walk.
    local objs = enb.objects and enb.objects() or {}
    local me, gates = nil, {}
    for _, o in ipairs(objs) do
        if o.gid == S.follow_gid and o.x then
            me = o
        elseif o.class and o.x then
            local c = o.class:lower()
            if c:find("gate") or c:find("wormhole") then gates[#gates + 1] = o end
        end
    end

    if me then
        -- present: remember whether it is hugging a gate, reset the absence counter.
        S.seen_gid = true
        S.miss = 0
        S.near_gate = 0
        local best = GATE_HUG
        for _, gt in ipairs(gates) do
            local d = enb.dist(me.x, me.y, me.z, gt.x, gt.y, gt.z)
            if d and d < best then best = d; S.near_gate = gt.gid end
        end
        return
    end

    -- absent this sample. Only meaningful if we HAD it and it was hugging a gate.
    if not S.seen_gid or S.near_gate == 0 then return end
    S.miss = S.miss + 1
    if S.miss < MISS_ARM then return end
    if S.cooldown > 0 then return end

    -- treat it as "the designated ship gated": target that gate and jump.
    local gate_gid = S.near_gate
    S.seen_gid = false
    S.near_gate = 0
    S.miss = 0
    S.cooldown = COOLDOWN
    if enb.request_target and enb.request_target(gate_gid) then
        if enb.gate and enb.gate() then
            enb.log("groupbot: follow-gate -> jumped gate " .. string.format("0x%08x", gate_gid))
        else
            enb.log("groupbot: follow-gate targeted gate but enb.gate() refused")
        end
    else
        enb.log("groupbot: follow-gate could not target the gate (out of range)")
    end
end

-- ---- tick ------------------------------------------------------------------
enb.on_tick(function()
    if not (S.reform_on or S.follow_on) then return end
    S.tick = S.tick + 1

    if S.reform_on and (S.tick % LIGHT_DIV == 0) then
        local now = enb.inspace and enb.inspace()
        if now and not S.was_inspace then
            reform()                       -- rising edge: just entered a sector
        end
        S.was_inspace = now and true or false
    end

    if S.follow_on and (S.tick % HEAVY_DIV == 0) then
        follow_gate()
    end
end)

-- ---- console control (drive from the /run channel) -------------------------
-- e.g.  /run groupbot.status()
--       /run groupbot.follow(0x40001234); groupbot.follow_on(true)
--       /run groupbot.formation(5)          -- switch reform to Block
groupbot = {
    on = function() S.reform_on = true; enb.log("groupbot: reform ON") end,
    off = function() S.reform_on = false; enb.log("groupbot: reform OFF") end,
    follow_on = function(v) S.follow_on = v and true or false
        enb.log("groupbot: follow-gate " .. (S.follow_on and "ON" or "OFF")) end,
    follow = function(gid) S.follow_gid = gid or 0
        S.seen_gid = false; S.near_gate = 0; S.miss = 0
        enb.log("groupbot: follow ship = " .. string.format("0x%08x", S.follow_gid)) end,
    formation = function(n) if n == 4 or n == 5 or n == 6 then S.formation = n
        enb.log("groupbot: reform formation = " .. n)
        else enb.log("groupbot: formation must be 4|5|6") end end,
    status = function()
        enb.log(string.format(
            "groupbot: reform=%s follow=%s formation=%d follow_gid=0x%08x inspace=%s cooldown=%d",
            tostring(S.reform_on), tostring(S.follow_on), S.formation,
            S.follow_gid, tostring(enb.inspace and enb.inspace()), S.cooldown))
    end,
    _state = S,
}

-- Let freya-hud's party_frame keep the reform formation in sync when the LEADER
-- picks a type through the HUD (nil-guarded so neither mod hard-depends on the
-- other): party_frame calls groupbot.formation(n) if this table is present.
M.set_formation = groupbot.formation

enb.log("freya-groupbot loaded (reform " .. (S.reform_on and "ON" or "OFF") ..
        ", follow-gate " .. (S.follow_on and "ON" or "OFF") .. ")")
return M
