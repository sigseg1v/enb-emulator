-- SPDX-License-Identifier: MIT
-- Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
-- License: LICENSES/Freya
--
-- native_hud_hide.lua -- hides the stock in-space self-status vitals bars (the
-- red energy / blue shield / green hull fills in the JEFIVE panel) so they do
-- not bleed through the translucent Freya glass overlay that replaces them.
--
-- MECHANISM (foreground fills):
--   The vitals controller (enb.vitals_ctrl()) holds three bar gadgets:
--       +0x1c energy, +0x20 shield, +0x24 hull.
--   Each bar gadget has two fill-mesh children at +0x64 and +0x6c. Each child's
--   +0x8 is a stretch-quad render object (vtable 0xb10f24) holding two embedded
--   quads: the first (scale at +0x10) is the visible foreground fill, the second
--   (scale at +0x50) is a background ANIMATION layer that pulses behind the fill.
--
--   The per-frame fill sizer writes ONLY the quad edges (+0x14/+0x18) from the
--   current fraction; it never touches +0x10 or +0x50. So writing both scales = 0
--   collapses the foreground fill AND the background animation permanently,
--   WITHOUT altering the stored fraction (+0x68) the value/percentage text reads,
--   and WITHOUT disturbing the Freya HUD (which reads the controller, not these
--   meshes).
--
-- STILL OPEN: the static background troughs are NOT 0xb10f24 quads (nuking every
-- reachable quad leaves them on screen) -- they are drawn by another render class
-- still being identified. Until then this mod hides the fills + their animations
-- only, not the static troughs. CV-AS-HIDE-COCKPIT.

local GAUGE_OFFS = { 0x1c, 0x20, 0x24 }   -- energy, shield, hull bar gadgets
local FILL_OFFS  = { 0x64, 0x6c }         -- the two fill-mesh children per bar
local QUAD_VT    = 0xb10f24               -- stretch-quad render class
local SCALES     = { 0x10, 0x50 }         -- foreground fill + background-anim scales (0 == hidden)
local MESH_SUB   = 0x08                   -- child -> stretch-quad render object

local mem = enb.mem

-- Re-applied every tick: the bar gadgets are recreated on sector/dock changes,
-- so a one-shot write would not survive. Guarded + idempotent: only writes a
-- mesh whose vtable matches and whose scale is not already zero.
local function hide_native_fills()
    local vc = (enb.vitals_ctrl and enb.vitals_ctrl()) or 0
    if vc == 0 or not mem.readable(vc) then return end
    for _, goff in ipairs(GAUGE_OFFS) do
        local g = mem.u32(vc + goff)
        if g ~= 0 and mem.readable(g) then
            for _, coff in ipairs(FILL_OFFS) do
                local child = mem.u32(g + coff)
                if child ~= 0 and mem.readable(child) then
                    local mesh = mem.u32(child + MESH_SUB)
                    if mesh ~= 0 and mem.readable(mesh)
                       and mem.u32(mesh) == QUAD_VT then
                        for _, soff in ipairs(SCALES) do
                            if mem.f32(mesh + soff) ~= 0.0 then
                                mem.write_f32(mesh + soff, 0.0)
                            end
                        end
                    end
                end
            end
        end
    end
end

enb.on_tick(hide_native_fills)

return { hide_native_fills = hide_native_fills }
