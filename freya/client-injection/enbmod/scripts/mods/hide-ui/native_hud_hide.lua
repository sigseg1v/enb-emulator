-- SPDX-License-Identifier: MIT
-- Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
-- License: LICENSES/Freya
--
-- native_hud_hide.lua -- per-element control over the stock in-space 2D HUD chrome.
--
-- The in-space HUD is one "interface scene" object (vtable HUD_SCENE_VT), reachable
-- at cockpit controller +SCENE_OFF. It owns one intrusive, circular doubly-linked
-- list of 2D widget render objects -- the list the per-frame draw pass walks. For
-- each list node the draw pass:
--   1) takes the child render object (node+NODE_CHILD points CHILD_ADJ bytes INTO
--      the object, so the object base = that pointer minus CHILD_ADJ),
--   2) draws it only when (flags & 0x700) == 0x700, reading a flags dword at
--      leaf+LEAF_FLAGS.
-- Clearing one of those three bits (we use DRAW_BIT) makes the draw pass skip that
-- widget -- a side-effect-free hide that touches no vtable and no value field
-- (unlike SetVisible, whose +0x2c "visible" bit this draw path never reads).
--
-- The native cockpit chrome panels are the leaves whose class is CHROME_VT. There
-- are several of them; their order in the list is stable within a session, so we
-- address each by its ORDINAL among the CHROME_VT leaves (0-based, in list order)
-- and give each ordinal a human name in ELEMENTS below. The chat window (a
-- different leaf class) and the 3D cockpit border (a separate PIP scene list) are
-- never touched.
--
-- WHAT WE FOUND while building this (all verified live against the running client,
-- so the next person does not re-walk the same dead ends):
--
--  * THE PANEL IS THE ATOMIC UNIT. A CHROME_VT leaf is a multi-widget panel
--    (e.g. NavButtonBar = several buttons, TargetFrame = 2 bars + 2 arrows + a
--    background, LevelBarsAndMicroMenu = the level bars AND the micro-menu). You
--    CANNOT hide a sub-widget of a panel with this flag trick: the panel paints
--    its own children in its draw method and ignores each child's draw gate. Test
--    that proves it: a child gadget at panel+0x14 (an associative/tree container of
--    child render objects) had its own (flags & 0x700) bits set; clearing them
--    changed nothing on screen. Sub-widget control would need a different
--    mechanism (DLL hook of the panel draw routine, or per-gadget mesh scale-zero)
--    -- deliberately out of scope here.
--
--  * THE PANELS HAVE NO STABLE NAME/ID. There is no UI-name C-string or id at any
--    fixed offset on a CHROME_VT panel object (only the panels' *child* gadgets
--    carry UI names like "UI_CPIT_WARP_0" at gadget+0x24 -- and those are the
--    children we cannot control, see above). So ordinal is the key we have.
--
--  * GEOMETRY IS NOT A USABLE KEY. The float fields on a panel (+0x1c..+0xc0) are
--    parent-relative sizes/offsets, not absolute screen coordinates, and several
--    panels share values, so a screen-rect fingerprint would not reliably
--    disambiguate them. Ordinal stays the key.
--
--  * THE 0x700 GATE IS V|E|L AND GATES INPUT TOO. The three bits are Visible
--    (0x100), Enabled (0x200), and in-Layout (0x400); a leaf draws only when all
--    three are set. Clearing any of them ALSO drops the leaf from hit-testing, not
--    just drawing -- which is why hiding panels can break 3D click-to-select if the
--    panel routes those clicks. If click-to-select stops working, show panels back
--    one at a time (enb.hud.show) to find the one that owns the click region and
--    leave it shown.
--
-- NAMING CAVEAT: ordinals are positional. If the client ever reorders/adds/removes
-- a chrome panel (e.g. a target-dependent panel that only exists while a target is
-- selected), the name->ordinal mapping can drift. enb.hud.dump() prints the live
-- ordinal list so the mapping can be re-checked, and is the first thing to run if
-- the names look wrong.
--
-- PUBLIC API (added to the global `enb` table, callable from any mod or /run):
--   enb.hud.hide(name)        -- hide one element
--   enb.hud.show(name)        -- show one element (re-raises the draw bit once,
--                                then hands the flag back to the game)
--   enb.hud.toggle(name)
--   enb.hud.set(name, hidden) -- hidden = true to hide, false to show
--   enb.hud.hide_all()        -- hide every named chrome element
--   enb.hud.show_all()
--   enb.hud.list()            -- "name = hidden|shown" for every element
--   enb.hud.names()           -- array of element names, in ordinal order
--   enb.hud.dump()            -- live ordinal -> leaf-address/flags (diagnostic)
--
-- SAFETY: every pointer is range- + readable-checked before use, the list walk is
-- iteration-bounded, and we bail unless the scene's own vtable matches HUD_SCENE_VT
-- (so a stale/!in-space controller pointer can never make us write a wrong object --
-- worst case we do nothing and the native UI shows).

local mem = enb.mem

local HUD_SCENE_VT = 0x00aff1f4 -- the in-space HUD interface-scene class
local CHROME_VT    = 0x00b1327c -- native cockpit chrome panel leaves
local SCENE_OFF    = 0x44       -- cockpit controller -> its HUD interface scene
local LIST_BEGIN   = 0x40       -- scene -> first widget list node
local LIST_END     = 0x3c       -- list sentinel == scene + LIST_END (loop-stop)
local NODE_NEXT    = 0x04       -- list node -> next node
local NODE_CHILD   = 0x0c       -- list node -> (child object + CHILD_ADJ)
local CHILD_ADJ    = 0x10       -- subtract to get the child object base
local LEAF_FLAGS   = 0x18       -- leaf -> visibility flags dword
local DRAW_BIT     = 0x400      -- one of the 0x700 "drawn" bits; clearing it hides
local MAX_NODES    = 256        -- iteration bound (corrupt-list guard)

-- Named chrome elements in scene-list order. Index here = ordinal + 1; the Nth
-- CHROME_VT leaf encountered while walking the list gets the Nth name. Names were
-- assigned by observing which on-screen widget vanished when each leaf was hidden.
local ELEMENTS = {
    "ActionBarIcons",               -- 0
    "Unknown1",                     -- 1
    "ChatDownArrow",                -- 2
    "ChatUpArrow",                  -- 3
    "Unknown2",                     -- 4
    "CombatLogDownArrow",           -- 5
    "CombatLogUpArrow",             -- 6
    "ChatFrameBackgroundAndScroll", -- 7
    "NavButtonBar",                 -- 8
    "LevelBarsAndMicroMenu",        -- 9
    "Unknown3",                     -- 10
    "Unknown4",                     -- 11
    "ScannerScreenBorder",          -- 12
    "BottomHudChrome",              -- 13
    "TargetFrame",                  -- 14
    "ActionBarBackground",          -- 15
}

-- name -> ordinal
local ORD = {}
for i, n in ipairs(ELEMENTS) do ORD[n] = i - 1 end

-- Default config: which elements start hidden. Edit to taste. Chat-related leaves
-- (the chat/combat-log arrows + chat frame/scroll) are deliberately left shown so
-- chat stays usable; the bottom cockpit chrome is hidden by default. If hiding an
-- element breaks something (e.g. 3D click-to-select), show it again and adjust here.
local hidden = {
    ActionBarIcons               = true,
    LevelBarsAndMicroMenu        = true,
    BottomHudChrome              = true,
    ActionBarBackground          = true,
    ChatFrameBackgroundAndScroll = true,
}

-- one-shot "force the draw bit back on" queue for show() after a hide
local force_show = {}

-- Resolve the live in-space HUD scene from a captured cockpit controller. Returns
-- the scene pointer only if it validates (right vtable), else 0.
local function hud_scene()
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

-- Walk the scene list and call fn(ordinal, leaf) for each CHROME_VT leaf, in order.
local function each_chrome(fn)
    local scene = hud_scene()
    if scene == 0 then return end
    local sentinel = scene + LIST_END
    local node = mem.u32(scene + LIST_BEGIN)
    local ord = 0
    for _ = 1, MAX_NODES do
        if node == sentinel or node == 0 or not mem.readable(node) then break end
        local cp = mem.u32(node + NODE_CHILD)
        if cp ~= 0 and mem.readable(cp) then
            local leaf = cp - CHILD_ADJ
            if mem.readable(leaf) and mem.u32(leaf) == CHROME_VT then
                fn(ord, leaf)
                ord = ord + 1
            end
        end
        node = mem.u32(node + NODE_NEXT)
    end
end

local function apply()
    each_chrome(function(ord, leaf)
        local name = ELEMENTS[ord + 1]
        if not name then return end
        if not mem.readable(leaf + LEAF_FLAGS) then return end
        local fl = mem.u32(leaf + LEAF_FLAGS)
        if hidden[name] then
            if (fl & DRAW_BIT) ~= 0 then mem.write_u32(leaf + LEAF_FLAGS, fl & ~DRAW_BIT) end
        elseif force_show[name] then
            if (fl & DRAW_BIT) == 0 then mem.write_u32(leaf + LEAF_FLAGS, fl | DRAW_BIT) end
            force_show[name] = nil
        end
    end)
end

local function set(name, hide)
    if ORD[name] == nil then return false, "unknown element: " .. tostring(name) end
    if hide then
        hidden[name] = true
    else
        hidden[name] = nil
        force_show[name] = true
    end
    return true
end

local function hide(name)   return set(name, true)  end
local function show(name)   return set(name, false) end
local function toggle(name) return set(name, not hidden[name]) end

local function hide_all()
    for _, n in ipairs(ELEMENTS) do hidden[n] = true end
    return true
end

local function show_all()
    for _, n in ipairs(ELEMENTS) do hidden[n] = nil; force_show[n] = true end
    return true
end

local function names()
    local t = {}
    for i, n in ipairs(ELEMENTS) do t[i] = n end
    return t
end

local function list()
    local out = {}
    for _, n in ipairs(ELEMENTS) do
        out[#out + 1] = n .. " = " .. (hidden[n] and "hidden" or "shown")
    end
    return table.concat(out, "\n")
end

-- Diagnostic: live ordinal -> leaf address + flags, for re-checking the mapping.
local function dump()
    local out = {}
    each_chrome(function(ord, leaf)
        local fl = mem.readable(leaf + LEAF_FLAGS) and mem.u32(leaf + LEAF_FLAGS) or 0
        out[#out + 1] = string.format("%2d %-30s leaf=%08x fl=%08x",
            ord, ELEMENTS[ord + 1] or "(unnamed)", leaf, fl)
    end)
    return table.concat(out, "\n")
end

enb.hud = {
    hide     = hide,
    show     = show,
    toggle   = toggle,
    set      = set,
    hide_all = hide_all,
    show_all = show_all,
    list     = list,
    names    = names,
    dump     = dump,
}

enb.on_tick(apply)

return enb.hud
