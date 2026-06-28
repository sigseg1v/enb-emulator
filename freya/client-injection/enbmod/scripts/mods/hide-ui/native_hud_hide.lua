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
-- Clearing one of those three bits makes the draw pass skip that widget -- a
-- side-effect-free hide that touches no vtable and no value field (unlike
-- SetVisible, whose +0x2c "visible" bit this draw path never reads).
--
-- WHICH bit matters. The three bits are Visible (0x100), Enabled (0x200), and
-- in-Layout (0x400). Clearing 0x400 alone hides the resting widget but NOT its
-- hover state: the rollover code RE-SETS 0x400 when the mouse is over the panel,
-- and that re-set lands after our per-tick clear but before the draw -- so on
-- hover the gate completes again and the native chrome (e.g. the bottom-center
-- thruster/warp cluster's rollover highlight) flashes back. We therefore clear
-- ENABLE_BIT (0x200) instead: it is set in the resting state and, verified live,
-- is never re-asserted per frame, so once cleared the gate can never reach 0x700
-- again -- the leaf stays dead in BOTH the normal and hover states. This is the
-- same lesson the Freya action-bar glyph hide learned (freya_ui.hide_native_glyphs).
--
-- The native cockpit chrome panels are the leaves whose class is CHROME_VT. The chat
-- TEXT window is a different leaf class (CHAT_TEXT_VT, 0x00afbf28) in the same list;
-- the Freya chat window (chat.lua) draws its own scrollback + input, so we hide every
-- CHAT_TEXT_VT leaf too (same 0x700 draw gate -> clear ENABLE_BIT). The chat FRAME +
-- scroll and the chat/combat-log arrow BUTTONS are CHROME_VT leaves and are hidden via
-- the named-element list below. A band of inactive leaves (0x00b111a4) is skipped; the
-- 3D cockpit border is a separate PIP scene list (scene+0x58 / +0x70) and is untouched.
--
-- TAIL ANCHORING (how we name panels stably). The scene's widget list is NOT static:
-- opening a window (inventory, target frame + its loot button, comm/portrait) inserts
-- more CHROME_VT leaves. Verified live against the running client: those transient
-- leaves ALWAYS insert at the FRONT, ahead of the persistent cockpit block -- the
-- inventory adds 5 leaves before the block, a target adds 1, etc., and the persistent
-- panels keep their order with ActionBarBackground always last. So the persistent
-- panels are the LAST #ELEMENTS leaves of the list, in fixed order, regardless of how
-- many transient windows are open. We therefore key each name off the END of the list
-- (leaves[count - #ELEMENTS + k]); there is no baseline-capture or ordinal-timing step
-- to get wrong, and a target selected when the mod first runs no longer mis-names
-- everything by one (the bug the old "bind at the smallest leaf count" scheme had).
--
-- CRUCIAL consequence we verified by experiment: the inventory ship+equipment
-- paperdoll is one of the transient FRONT leaves, a DIFFERENT object from
-- ActionBarIcons (the native ability glyph by hotbar slot 1). Hiding ActionBarIcons
-- does NOT blank the equipment frame, so we hide it unconditionally -- no
-- "restore while a modal is open" hack (that hack is what made the ability glyph
-- flash back when the inventory opened).
--
-- WHAT WE FOUND while building this (all verified live against the running client,
-- so the next person does not re-walk the same dead ends):
--
--  * THE PANEL IS THE ATOMIC UNIT. A CHROME_VT leaf is a multi-widget panel
--    (e.g. NavButtonBar = several buttons, TargetFrame = 2 bars + 2 arrows + a
--    background). You CANNOT hide a sub-widget of a panel with this flag trick: the
--    panel paints its own children in its draw method and ignores each child's draw
--    gate. Sub-widget control would need a different mechanism (DLL hook of the panel
--    draw routine, or per-gadget mesh scale-zero) -- deliberately out of scope here.
--
--  * THE PANELS HAVE NO STABLE NAME/ID. There is no UI-name C-string or id at any
--    fixed offset on a CHROME_VT panel object (only the panels' *child* gadgets carry
--    UI names like "UI_CPIT_WARP_0" at gadget+0x24 -- and those are the children we
--    cannot control). So position (tail-anchored) is the key we have.
--
--  * GEOMETRY IS NOT A USABLE KEY. The float fields on a panel (+0x1c..+0xc0) are
--    parent-relative sizes/offsets, not absolute screen coordinates, and several
--    panels share values, so a screen-rect fingerprint would not disambiguate them.
--
--  * THE 0x700 GATE IS V|E|L AND GATES INPUT TOO. The three bits are Visible
--    (0x100), Enabled (0x200), and in-Layout (0x400); a leaf draws only when all
--    three are set. Clearing any of them ALSO drops the leaf from hit-testing, not
--    just drawing -- which is why hiding panels can break 3D click-to-select if the
--    panel routes those clicks. If click-to-select stops working, show panels back
--    one at a time (enb.hud.show) to find the one that owns the click region.
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
--   enb.hud.names()           -- array of element names, in tail-anchored order
--   enb.hud.dump()            -- live position -> name/leaf/flags (diagnostic)
--
-- SAFETY: every pointer is range- + readable-checked before use, the list walk is
-- iteration-bounded, and we bail unless the scene's own vtable matches HUD_SCENE_VT
-- (so a stale/!in-space controller pointer can never make us write a wrong object --
-- worst case we do nothing and the native UI shows).

local mem = enb.mem

local HUD_SCENE_VT = 0x00aff1f4 -- the in-space HUD interface-scene class
local CHROME_VT    = 0x00b1327c -- native cockpit chrome panel leaves
local CHAT_TEXT_VT = 0x00afbf28 -- native chat TEXT window leaf (own class, hidden wholesale)
local SCENE_OFF    = 0x44       -- cockpit controller -> its HUD interface scene
local LIST_BEGIN   = 0x40       -- scene -> first widget list node
local LIST_END     = 0x3c       -- list sentinel == scene + LIST_END (loop-stop)
local NODE_NEXT    = 0x04       -- list node -> next node
local NODE_CHILD   = 0x0c       -- list node -> (child object + CHILD_ADJ)
local CHILD_ADJ    = 0x10       -- subtract to get the child object base
local LEAF_FLAGS   = 0x18       -- leaf -> visibility flags dword
local ENABLE_BIT   = 0x200      -- Enabled; clearing it drops draw + INPUT/hover and is
                                -- never re-asserted (0x400 is, on rollover -- see header)
local DRAW_GATE    = 0x700      -- Visible|Enabled|inLayout; a leaf draws only when all set
local REST_DISABLED = 0x100 | 0x400  -- Visible|inLayout but NOT Enabled: the "drawn only
                                -- while the mouse is over me" state the native target-action
                                -- button panel rests in (see show_target_buttons below)
local MAX_NODES    = 256        -- iteration bound (corrupt-list guard)

-- The persistent cockpit chrome panels, in list order. These are the LAST #ELEMENTS
-- CHROME_VT leaves of the scene list (tail anchoring, see header). Names were
-- assigned by observing which on-screen widget vanished when each leaf's draw bit was
-- cleared; the 5 default-hidden ones (ords 0,7,9,13,15) were re-confirmed live.
local ELEMENTS = {
    "ActionBarIcons",               -- 0  native ability glyph poking out by hotbar slot 1
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

-- Default config: which elements start hidden. Edit to taste. The Freya HUD replaces
-- the native chat (chat.lua draws its own window), so ALL chat chrome is hidden by
-- default: the frame/scroll, both chat arrows, and both combat-log arrows; the chat
-- TEXT itself is a separate leaf class hidden by hide_chat_text below. The bottom
-- cockpit chrome is also hidden. If hiding an element breaks something (e.g. 3D
-- click-to-select), show it again and adjust here.
local hidden = {
    ActionBarIcons               = true,
    LevelBarsAndMicroMenu        = true,
    BottomHudChrome              = true,
    ActionBarBackground          = true,
    ChatFrameBackgroundAndScroll = true,
    ChatDownArrow                = true,
    ChatUpArrow                  = true,
    CombatLogDownArrow           = true,
    CombatLogUpArrow             = true,
    -- the native chat-frame button row (Chat/Group/ChatOptions/Help/Emote/Message);
    -- the Freya top strip recreates Chat and the rest are reachable elsewhere, so the
    -- whole old chat frame is now hidden (owner ask: "kill the entire old chat frame").
    NavButtonBar                 = true,
    -- the two boxed down-chevron arrows that float at the top-mid/right edge (the
    -- chat + combat-log "scroll to bottom" arrows, detached from the hidden frame).
    -- Identified live by hiding each and watching the arrow vanish (owner ask).
    Unknown1                     = true,
    Unknown2                     = true,
    -- the bottom-right target frame: native glass bg + hull/shield bars + name +
    -- "Dist:" + cycle arrows + the round scan/cycle buttons above it. The Freya
    -- HUD's target_frame.lua repaints the 2D layer (thin stacked bars + name +
    -- level); the 3D target model is a separate PIP scene and stays visible.
    TargetFrame                  = true,
}

-- Hide the native chat TEXT leaves (CHAT_TEXT_VT) wholesale -- Freya draws chat now.
local hide_chat_text = true

-- Keep the native TARGET ACTION buttons (Follow / Approach / Dock / Register /
-- Loot ...) ALWAYS visible. They are a TRANSIENT chrome leaf the game inserts when
-- a target is selected, and at rest it leaves that panel's Enabled bit (0x200)
-- CLEAR -- so the 0x700 draw gate never completes and the cluster is invisible
-- until the mouse rolls over it, when the native rollover sets Enabled and the
-- buttons flash in. The owner wants them up at all times, so each frame we re-assert
-- Enabled on any TRANSIENT front leaf sitting in that exact resting state (gate ==
-- REST_DISABLED: Visible|inLayout, Enabled clear). Scoped to the transient block so
-- it never disturbs a named persistent panel, and self-limiting -- those hover-gated
-- transients are essentially just these buttons. (A later pass restyles them; for
-- now this just makes them stop hiding.)
local show_target_buttons = true

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

-- Walk the scene list and collect the CHROME_VT leaves, in order.
local function chrome_leaves()
    local scene = hud_scene()
    if scene == 0 then return nil end
    local sentinel = scene + LIST_END
    local node = mem.u32(scene + LIST_BEGIN)
    local leaves = {}
    for _ = 1, MAX_NODES do
        if node == sentinel or node == 0 or not mem.readable(node) then break end
        local cp = mem.u32(node + NODE_CHILD)
        if cp ~= 0 and mem.readable(cp) then
            local leaf = cp - CHILD_ADJ
            if mem.readable(leaf) and mem.u32(leaf) == CHROME_VT then
                leaves[#leaves + 1] = leaf
            end
        end
        node = mem.u32(node + NODE_NEXT)
    end
    return leaves
end

-- Walk the scene list and collect the CHAT_TEXT_VT leaves (the native chat text
-- window -- its own class, not CHROME_VT, so it is not a named tail-anchored panel).
local function chat_text_leaves()
    local scene = hud_scene()
    if scene == 0 then return nil end
    local sentinel = scene + LIST_END
    local node = mem.u32(scene + LIST_BEGIN)
    local leaves = {}
    for _ = 1, MAX_NODES do
        if node == sentinel or node == 0 or not mem.readable(node) then break end
        local cp = mem.u32(node + NODE_CHILD)
        if cp ~= 0 and mem.readable(cp) then
            local leaf = cp - CHILD_ADJ
            if mem.readable(leaf) and mem.u32(leaf) == CHAT_TEXT_VT then
                leaves[#leaves + 1] = leaf
            end
        end
        node = mem.u32(node + NODE_NEXT)
    end
    return leaves
end

-- Map name -> leaf for THIS frame by tail anchoring: the persistent panels are the
-- last #ELEMENTS chrome leaves of the list (transients always insert ahead of them).
-- Returns map, the live ordered leaf list, and the transient front-block size, or nil
-- if the scene is not up / has fewer leaves than we have names for.
local function map_panels()
    local leaves = chrome_leaves()
    if not leaves then return nil end
    local n = #leaves
    if n < #ELEMENTS then return nil end
    local base = n - #ELEMENTS   -- transient leaves ahead of the persistent block
    local map = {}
    for k = 1, #ELEMENTS do
        map[ELEMENTS[k]] = leaves[base + k]
    end
    return map, leaves, base
end

-- Set or clear a leaf's enable bit, no-op if already in the wanted state. Clearing
-- ENABLE_BIT (0x200) hides AND removes the panel from hover/hit-testing, and the
-- game never re-asserts it (so the hide survives rollover -- see header).
local function set_draw(leaf, on)
    if not (leaf and mem.readable(leaf + LEAF_FLAGS)) then return end
    local fl = mem.u32(leaf + LEAF_FLAGS)
    if on then
        if (fl & ENABLE_BIT) == 0 then mem.write_u32(leaf + LEAF_FLAGS, fl | ENABLE_BIT) end
    else
        if (fl & ENABLE_BIT) ~= 0 then mem.write_u32(leaf + LEAF_FLAGS, fl & ~ENABLE_BIT) end
    end
end

local function apply()
    local map, leaves, base = map_panels()
    if not map then return end
    for name in pairs(hidden) do
        set_draw(map[name], false)
    end
    for name in pairs(force_show) do
        set_draw(map[name], true)
        force_show[name] = nil
    end
    -- keep the native target-action button cluster always visible: re-assert Enabled
    -- on any TRANSIENT front leaf the game is holding in the hover-only resting state.
    if show_target_buttons and leaves then
        for i = 1, base do
            local leaf = leaves[i]
            if leaf and mem.readable(leaf + LEAF_FLAGS) then
                local fl = mem.u32(leaf + LEAF_FLAGS)
                if (fl & DRAW_GATE) == REST_DISABLED then
                    mem.write_u32(leaf + LEAF_FLAGS, fl | ENABLE_BIT)
                end
            end
        end
    end
    -- native chat text (separate leaf class) -- hidden wholesale, Freya draws chat
    if hide_chat_text then
        for _, leaf in ipairs(chat_text_leaves() or {}) do
            set_draw(leaf, false)
        end
    end
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

-- Diagnostic: live position -> tail-anchored name + leaf address + flags, plus the
-- transient front-block size (leaves inserted ahead of the persistent panels).
local function dump()
    local map, leaves, base = map_panels()
    if not leaves then return "no scene" end
    local rev = {}
    for name, leaf in pairs(map or {}) do rev[leaf] = name end
    local out = {}
    for i = 1, #leaves do
        local leaf = leaves[i]
        local fl = mem.readable(leaf + LEAF_FLAGS) and mem.u32(leaf + LEAF_FLAGS) or 0
        out[#out + 1] = string.format("%2d %-30s leaf=%08x fl=%08x",
            i - 1, rev[leaf] or "(transient)", leaf, fl)
    end
    out[#out + 1] = string.format("chrome=%d front=%d", #leaves, base or -1)
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
