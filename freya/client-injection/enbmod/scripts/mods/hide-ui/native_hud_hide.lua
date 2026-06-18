-- SPDX-License-Identifier: MIT
-- Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
-- License: LICENSES/Freya
--
-- native_hud_hide.lua -- suppresses the stock in-space HUD elements the Freya
-- overlay replaces, so the native chrome does not bleed through.
--
-- Three independent suppressions, all re-applied every tick (the gadgets are
-- recreated on sector/dock changes, so one-shot writes would not survive) and all
-- idempotent + fully guarded (a bad/short pointer is skipped, never dereferenced
-- blindly):
--
-- 1) VITALS FILL MESHES (the original suppression). The vitals controller
--    (enb.vitals_ctrl()) holds three bar gadgets at +0x1c/+0x20/+0x24; each has
--    two fill-mesh children at +0x64/+0x6c whose +0x8 is a stretch-quad render
--    object (vtable 0xb10f24). The first embedded quad's scale is at +0x10 (the
--    visible foreground fill), the second at +0x50 (a background pulse anim). The
--    per-frame fill sizer only rewrites the quad EDGES (+0x14/+0x18), never the
--    scales, so zeroing both scales collapses fill + anim permanently without
--    touching the stored fraction (+0x68) the value text reads.
--
-- 2) XP / DISCIPLINE BARS. The xp controller (enb.xp_ctrl()) holds three bar
--    GADGETS at +0x1c (combat) / +0x20 (trade) / +0x24 (explore). These are the
--    "subtle empty bars" at the bottom-left that the Freya discipline card
--    replaces. They are genuine GadgetClass widgets, so we hide them with the
--    engine's own GadgetClass::SetVisible (enb.gadget_set_visible) -- it flips the
--    visible bit AND releases the rendered child, the clean engine hide.
--
-- 3) THROTTLE / WARP CLUSTER + its reactor-style bars. The throttle controller
--    (first value of enb.cockpit_ctrl()) stores child gadgets across its int-slots;
--    the throttle/warp/reverse/left/right gadgets are hidden with SetVisible, and
--    the ReactorBar render children (vtable 0xaff1f4, NOT gadgets) are hidden via
--    their inherited base "visible" byte at +0x1c (a side-effect-free flag the
--    parent draw loop checks -- value 0 == hidden).
--
-- SAFETY: enb.gadget_set_visible() dispatches *(vtable+0x40) as SetVisible, but a
-- controller slot may hold a NON-gadget sub-object whose vtable+0x40 is a different
-- virtual (a getter, a list-walk, even a destructor). Calling those would run the
-- wrong code. So we only ever call it on objects whose vtable+0x40 resolves to a
-- known GadgetClass::SetVisible entry (base 0x65f640 / its thunk 0x40757c, and the
-- focus-handling override 0x66b840 / its thunk 0x405f4c). Everything else is left
-- untouched.
--
-- STILL NOT HIDDEN (controllers not capturable this session): the numbered ability
-- hotbar, the 4 skill "orb" buttons, the central reactor dial, and the right radar
-- dial. Their controllers' constructors ran at login before this mod's hooks armed
-- and there is no global to read them back from, so they need a constructor-capture
-- hook (fires on the next login / cockpit rebuild) before they can be suppressed.
-- Tracked as CV-AS-HIDE-COCKPIT.

local mem = enb.mem

-- ---- 1) vitals fill meshes -------------------------------------------------
local GAUGE_OFFS = { 0x1c, 0x20, 0x24 } -- energy, shield, hull bar gadgets
local FILL_OFFS  = { 0x64, 0x6c }       -- the two fill-mesh children per bar
local QUAD_VT    = 0xb10f24             -- stretch-quad render class
local SCALES     = { 0x10, 0x50 }       -- foreground fill + background-anim scales
local MESH_SUB   = 0x08                 -- child -> stretch-quad render object

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

-- ---- shared gadget-hide guard ----------------------------------------------
-- GadgetClass::SetVisible entry points (vtable+0x40). Only dispatch SetVisible on
-- objects whose vtable+0x40 is one of these; any other value means the object is
-- not a gadget and slot 0x40 is a different virtual that must NOT be called.
local SETVISIBLE = {
    [0x40757c] = true, -- thunk -> GadgetClass::SetVisible (0x65f640)
    [0x65f640] = true, -- GadgetClass::SetVisible (base)
    [0x405f4c] = true, -- thunk -> focus-handling SetVisible override (0x66b840)
    [0x66b840] = true, -- SetVisible override (tail-calls base)
}

local function safe_hide_gadget(g)
    if not g or g <= 0x400000 or not mem.readable(g) then return false end
    local vt = mem.u32(g)
    if vt <= 0x400000 or not mem.readable(vt + 0x40) then return false end
    if not SETVISIBLE[mem.u32(vt + 0x40)] then return false end
    return enb.gadget_set_visible(g, false)
end

-- ---- 2) xp / discipline bars -----------------------------------------------
local XP_BARS = { 0x1c, 0x20, 0x24 } -- combat, trade, explore bar gadgets

local function hide_xp_bars()
    local xc = (enb.xp_ctrl and enb.xp_ctrl()) or 0
    if xc == 0 or not mem.readable(xc) then return end
    for _, boff in ipairs(XP_BARS) do
        safe_hide_gadget(mem.u32(xc + boff))
    end
end

-- ---- 3) throttle / warp cluster + reactor bars -----------------------------
local REACTORBAR_VT = 0xaff1f4 -- ReactorBar render object (NOT a gadget)
local RB_VISIBLE    = 0x1c     -- inherited base "visible" byte; 0 == hidden

local function hide_throttle()
    local tc = 0
    if enb.cockpit_ctrl then tc = (select(1, enb.cockpit_ctrl())) or 0 end
    if tc == 0 or not mem.readable(tc) then return end
    -- Walk the controller's int-slots; hide gadget children via SetVisible and
    -- dim ReactorBar children via their visible byte.
    for s = 0, 0x50 do
        local a = tc + s * 4
        if mem.readable(a) then
            local p = mem.u32(a)
            if p > 0x400000 and mem.readable(p) then
                local vt = mem.u32(p)
                if vt == REACTORBAR_VT then
                    local dw = mem.u32(p + RB_VISIBLE)
                    if (dw & 0xff) ~= 0 then
                        mem.write_u32(p + RB_VISIBLE, dw & 0xFFFFFF00)
                    end
                else
                    safe_hide_gadget(p)
                end
            end
        end
    end
end

local function hide_all()
    hide_native_fills()
    hide_xp_bars()
    hide_throttle()
end

enb.on_tick(hide_all)

return {
    hide_native_fills = hide_native_fills,
    hide_xp_bars      = hide_xp_bars,
    hide_throttle     = hide_throttle,
    hide_all          = hide_all,
}
