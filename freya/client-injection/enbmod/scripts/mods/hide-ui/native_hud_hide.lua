-- SPDX-License-Identifier: MIT
-- Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
-- License: LICENSES/Freya
--
-- native_hud_hide.lua -- suppresses the stock in-space 2D HUD chrome (the cockpit
-- bars/dial/throttle/warp cluster, the action + skill buttons, the reactor bars,
-- and the scanner) so the Freya overlay can replace it. The 3D cockpit border mesh
-- and the chat window are left intact.
--
-- HOW IT WORKS (verified live against the running client).
--
-- The in-space HUD is one "interface scene" object (vtable HUD_SCENE_VT). It owns a
-- single intrusive, circular doubly-linked list of 2D widget render objects -- the
-- thing the per-frame draw pass walks. For each list node the draw pass:
--   1) takes the child render object (node+NODE_CHILD points CHILD_ADJ bytes INTO
--      the object, so the object base = that pointer minus CHILD_ADJ),
--   2) calls the child's visibility predicate, and only if it returns nonzero
--   3) calls the child's draw method.
-- The shared leaf visibility predicate is `(flags & 0x700) == 0x700`, reading a
-- flags dword at leaf+LEAF_FLAGS. So clearing ANY one of those three bits makes the
-- draw pass skip that widget entirely -- a side-effect-free hide that touches no
-- vtable and no value field (unlike SetVisible, whose +0x2c "visible" bit this draw
-- path never reads, which is why SetVisible had no visual effect).
--
-- We re-apply every tick because the widgets are rebuilt on sector / dock changes,
-- and the value updaters can re-raise the flag.
--
-- SCOPE -- which leaves we touch. The scene's widget list also contains the chat
-- window (a different leaf class) and a band of inactive/invisible leaves. We clear
-- the hide bit ONLY on leaves whose class is CHROME_VT -- the native cockpit chrome
-- and the top button/menu bar. The chat window (a different class) and everything
-- else are left exactly as the client set them.
--
-- SAFETY: every pointer is range- + readable-checked before use, the list walk is
-- iteration-bounded, and we bail unless the scene's own vtable matches HUD_SCENE_VT
-- (so a stale/!in-space controller pointer can never make us write into a wrong
-- object -- worst case we do nothing and the native UI shows).

local mem = enb.mem

local HUD_SCENE_VT = 0x00aff1f4 -- the in-space HUD interface-scene class
local CHROME_VT    = 0x00b1327c -- native cockpit chrome + top button/menu bar leaves
local SCENE_OFF    = 0x44       -- cockpit controller -> its HUD interface scene
local LIST_BEGIN   = 0x40       -- scene -> first widget list node
local LIST_END     = 0x3c       -- list sentinel == scene + LIST_END (loop-stop)
local NODE_NEXT    = 0x04       -- list node -> next node
local NODE_CHILD   = 0x0c       -- list node -> (child object + CHILD_ADJ)
local CHILD_ADJ    = 0x10       -- subtract to get the child object base
local LEAF_FLAGS   = 0x18       -- leaf -> visibility flags dword
local HIDE_BIT     = 0x400      -- one of the 0x700 "drawn" bits; clearing it hides
local MAX_NODES    = 256        -- iteration bound (corrupt-list guard)

-- Resolve the live in-space HUD scene from a captured cockpit controller. Returns
-- the scene pointer only if it validates (right vtable), else 0.
local function hud_scene()
    -- enb.cockpit_ctrl() yields the captured cockpit controllers; any of them holds
    -- the scene at +SCENE_OFF. Try each captured value.
    local a, b = enb.cockpit_ctrl()
    for _, tc in ipairs({ a or 0, b or 0 }) do
        if tc ~= 0 and mem.readable(tc + SCENE_OFF) then
            local sc = mem.u32(tc + SCENE_OFF)
            if sc > 0x10000 and mem.readable(sc) and mem.u32(sc) == HUD_SCENE_VT then
                return sc
            end
        end
    end
    return 0
end

local function hide_chrome()
    local scene = hud_scene()
    if scene == 0 then return end
    local sentinel = scene + LIST_END
    local node = mem.u32(scene + LIST_BEGIN)
    for _ = 1, MAX_NODES do
        if node == sentinel or node == 0 or not mem.readable(node) then break end
        local cp = mem.u32(node + NODE_CHILD)
        if cp ~= 0 and mem.readable(cp) then
            local leaf = cp - CHILD_ADJ
            if mem.readable(leaf) and mem.u32(leaf) == CHROME_VT then
                local fl = mem.u32(leaf + LEAF_FLAGS)
                if (fl & HIDE_BIT) ~= 0 then
                    mem.write_u32(leaf + LEAF_FLAGS, fl & ~HIDE_BIT)
                end
            end
        end
        node = mem.u32(node + NODE_NEXT)
    end
end

enb.on_tick(hide_chrome)

return {
    hud_scene   = hud_scene,
    hide_chrome = hide_chrome,
}
