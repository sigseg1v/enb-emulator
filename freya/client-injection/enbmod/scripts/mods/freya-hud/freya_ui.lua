-- SPDX-License-Identifier: MIT
-- Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
-- License: LICENSES/Freya
--
-- freya_ui.lua -- the player card + action hotbar of the Freya HUD (Phase AS).
--
-- require("freya_ui") from init.lua. Registers its own on_tick (draw) and
-- on_input (click-to-tap + mouse-swallow over our cards).
--
-- Ports the "Earth & Beyond HUD" design:
--   * PlayerCard -- a glass panel anchored above the hotbar's left edge, with a
--     name + level header and three vitals (hull / shield / reactor). Each vital
--     is a track with a vertical-gradient fill, the current/max printed INSIDE
--     the bar (right-aligned, white, outlined) and the percentage to its right.
--   * Hotbar -- six square glass slots for keys 1 2 3 4 5 6, with the design's
--     small radius + gloss + hairline border and a cyan armed glow. Six is the
--     real count: the native in-space action bar is two 3-box bank controllers
--     (six boxes total). The "alternate bar" the player toggles to is a second
--     PAGE swapped into those same six boxes, not six more live slots; a page
--     button at the right of the bar flips both banks together, so the six slots
--     page as a unit between actions 1-6 and 7-12 (their labels follow). Click a
--     slot -> dispatches the native action through the captured controller; a
--     physical keypress lights it (the native keybind still fires it).
--
-- VISIBILITY (owner ask): the HUD draws only in space and station; nothing on
-- the login / character-select / load screens. In station the hotbar is hidden
-- (skill buttons off) but the cards stay. Gating is enb.state()-driven via
-- freya_hud.vis(); until calibrated it reads "unknown" -> shows everything, so
-- dev / the headless previewer still render the full HUD.
--
-- This script owns the replacement pixels + input, and swallows mouse input over
-- the cards so anything behind them is never clickable. The native widgets these
-- cards replace can be suppressed via enb.patch_ret (see init.lua's opt-in HIDE
-- block); that is OFF by default and needs real-client verification, so until
-- the owner enables it the native widgets remain visible through the glass.

local H = require("freya_hud")

-- ---- the six action-bar keys -----------------------------------------------
local KEYS = {
    { label = "1", vk = 0x31 }, { label = "2", vk = 0x32 }, { label = "3", vk = 0x33 },
    { label = "4", vk = 0x34 }, { label = "5", vk = 0x35 }, { label = "6", vk = 0x36 },
}
local VK_TO_IDX = {}
for i, k in ipairs(KEYS) do VK_TO_IDX[k.vk] = i end

-- ---- native action-bar binding ---------------------------------------------
-- The native in-space action bar is two adjacent 3-box "bank" controllers that
-- share one vtable (AB_VTABLE) and sit 0x60 bytes apart: bank0 owns the first
-- three slots, bank1 the next three -- six slots total, our keys 1-6. Each bank
-- holds its three box pointers at +0x10/+0x14/+0x18.
--
-- The dispatcher FUN_006120e0(controller, id) is PRESS/RELEASE-keyed, not
-- primary/alternate. A bank exposes a press-id base (+0x50) and a release-id
-- base (+0x54); box k (k in 0..2) has press id (v50+k) and release id (v54+k):
--   * press  (id in [v50, v50+3)) fires the action ONLY when the box is idle
--     (box +0x6c == 0), then marks it active (+0x6c = 1).
--   * release (id in [v54, v54+3)) fires ONLY when the box is active
--     (box +0x6c != 0), then clears it (+0x6c = 0).
-- For an instant ability/weapon a press fires it; the game clears +0x6c again on
-- cooldown. For a toggle (a sustained buff / continuous weapon) press turns it on
-- and a second activation must use the RELEASE id to turn it off. So replaying a
-- keypress means: read the box's live +0x6c and pick press-or-release exactly as
-- the native keybind handler does. Calling a press id on an already-active box
-- (or a release id on an idle box) hits neither branch and FUN_006120e0 returns
-- 0 -- a silent no-op. That mis-pick (computing an id outside both ranges) is why
-- the previous mapping never actually dispatched. The id+box layout here is read
-- live from the controller and verified against the running client.
--
-- The "alternate bar" the player toggles to is NOT six more slots: the bar-toggle
-- id (v54+3) advances a page index (controller +0x4c) that swaps the action bound
-- to each of the same six boxes. We drive that toggle (see toggle_page below) from
-- a page button on the bar, flipping both banks together so all six slots page as
-- a unit between actions 1-6 and 7-12.
--
-- We capture one bank pointer read-only via the Use-Slot hook (enb.actionbar);
-- the sibling bank is that pointer +/-0x60 (normalized via the +0x3c bank flag).
-- Capture happens the first time any slot is used in space.
local RECORD_MODE = false

local ACTIONBAR_DISPATCH = 0x006120e0
local AB_VTABLE = 0x00af7484

-- Return (bank0, bank1) controller pointers, or nil if not captured yet.
local function ab_banks()
    local ab = enb.actionbar and enb.actionbar() or 0
    if not ab or ab == 0 then return nil end
    if not enb.mem.readable(ab, 0x58) then return nil end
    if enb.mem.u32(ab) ~= AB_VTABLE then return nil end  -- not the controller we expect
    local bank = enb.mem.u32(ab + 0x3c)
    local b0 = (bank == 1) and (ab - 0x60) or ab
    if not enb.mem.readable(b0, 0x58) or not enb.mem.readable(b0 + 0x60, 0x58) then return nil end
    return b0, b0 + 0x60
end

-- (bank, box) backing Freya hotbar slot i (1..6), or nil. Slots 1-3 are bank0's
-- three boxes, 4-6 bank1's. A box is occupied iff it holds an action object
-- (box +0x64 != 0); an empty slot returns nil.
local function slot_bank_box(i)
    local m = enb.mem
    local b0, b1 = ab_banks()
    if not b0 then return nil end
    local bank = (i <= 3) and b0 or b1
    local k = (i - 1) % 3
    local box = m.u32(bank + 0x10 + k * 4)
    if box == 0 or not m.readable(box, 0x70) or m.u32(box + 0x64) == 0 then return nil end
    return bank, box, k
end

-- The native keybind drives slot i (1..6) through the dispatcher FUN_006120e0 with
-- a PRESS id on key-down and a RELEASE id on key-up: the ability fires on the
-- press, and the release just resets the box's pressed flag (+0x6c) back to 0. The
-- dispatcher gates each branch on that byte (press acts only when byte==0 then sets
-- 1; release acts only when byte!=0 then clears 0), so a physical tap must be a
-- FULL press+release cycle or the byte sticks at 1 and the next press is read as a
-- release (= every-other-tap fires). press_slot/release_slot mirror that exactly;
-- tap_slot does a whole cycle for one-shot triggers (clicks).
local function press_slot(i)
    local bank, _, k = slot_bank_box(i)
    if not bank then return false end
    enb.call(ACTIONBAR_DISPATCH, bank, enb.mem.u32(bank + 0x50) + k) -- press base +0x50
    return true
end

local function release_slot(i)
    local bank, _, k = slot_bank_box(i)
    if not bank then return false end
    enb.call(ACTIONBAR_DISPATCH, bank, enb.mem.u32(bank + 0x54) + k) -- release base +0x54
    return true
end

-- One full press+release cycle: fires the ability once and leaves the box idle.
local function tap_slot(i)
    if not press_slot(i) then return false end
    release_slot(i)
    return true
end

-- ---- paging (the "alternate bar": slots 7-12) ------------------------------
-- The native bar holds TWO pages of actions in the same six boxes; the player
-- flips between them. The page is a per-bank index at controller +0x4c, advanced
-- by a bank-local toggle id = (release base at +0x54) + 3 (8 for bank0, 15 for
-- bank1). The six press ids are fixed across pages, so once a bank is flipped its
-- three boxes dispatch the OTHER page's actions through the same ids -- our keys
-- and clicks need no remap, they fire whatever page is live. We just drive the
-- toggle (both banks together, so all six slots page as a unit) and relabel the
-- slots 1-6 <-> 7-12 to match. Verified live: dispatching 8+15 flips both pages
-- 0->1 and a second 8+15 restores them.
local function current_page()
    local b0 = ab_banks()
    if not b0 then return 0 end
    local pg = enb.mem.u32(b0 + 0x4c)
    return (pg ~= 0) and 1 or 0
end

-- Flip both banks to the other page. Uses the live per-bank toggle id so it works
-- regardless of which bank enb.actionbar() last captured (ab_banks normalizes).
local function toggle_page()
    local m = enb.mem
    local b0, b1 = ab_banks()
    if not b0 then return false end
    enb.call(ACTIONBAR_DISPATCH, b0, m.u32(b0 + 0x54) + 3)
    enb.call(ACTIONBAR_DISPATCH, b1, m.u32(b1 + 0x54) + 3)
    return true
end

-- The key/label a Freya hotbar slot shows for the current page: 1-6 on page 0,
-- 7-12 on page 1.
local function slot_label(i)
    return tostring(i + current_page() * 6)
end

-- Slot art. A slot's action object lives at box +0x64 (the "slot data" gadget);
-- its bound item/ability card is one hop further at +0x64 again, and that card's
-- vtable tells weapon from ability: WEAPON_VT for a beam/projectile group,
-- ABILITY_VT for an activated ability / device. (Verified live: the only slot
-- reading WEAPON_VT was the equipped beam weapon; every ability read ABILITY_VT.
-- The earlier 3D-render-node test flagged every slot and was wrong.)
--
-- ABILITY ICON: the card carries the real per-ability icon and surfaces it through
-- its own vtable. The card's slot-render uses a "get icon" virtual at vtable +0x34
-- (the engine's preset-texture branch, taken when the card has a 2D glyph): called
-- thiscall on the card it returns the ability's TextureClass (vtable TEXCLASS_VT),
-- a 64x64 already-realized texture whose IDirect3DTexture8* lives at +0x34. We blit
-- that texture straight into the slot -- it is the client's OWN runtime-loaded art
-- (resolved from the skill's icon name by the engine), drawn on the user's machine,
-- never redistributed. Re-resolved every frame from the captured controller; the
-- texture pointer is never cached. Verified live: the virtual returns a distinct
-- texture per ability (two boxes holding the same ability return the same texture).
--
-- A weapon group has no 2D glyph in the native bar (it renders a live 3D model) and
-- its card's +0x34 virtual returns only shared item-state chrome, not a per-weapon
-- icon, so we draw an original Freya placeholder (weapon_icon.png -- an MIT asset we
-- authored, no EA art) for it. Per-weapon icons (off the item record) are a separate
-- follow-up (see plans/29 CV-AS-ACTIONBAR-ICONS).
local SLOT_VT     = 0x00afac78   -- a slot box ("Shortcut Box")
local WEAPON_VT   = 0x00afcd04   -- item card: weapon group
local ABILITY_VT  = 0x00afd664   -- item card: activated ability / device
local TEXCLASS_VT = 0x00b119f0   -- engine TextureClass (IDirect3DTexture8* at +0x34)
local ICON_VFN    = 0x34         -- card vtable slot: thiscall -> icon TextureClass
local TEX_D3D     = 0x34         -- TextureClass -> IDirect3DTexture8*
local TEX_INIT    = 0x0094b920   -- TextureClass::Init (thiscall) -- realize +0x34 if NULL
local WEAPON_ICON = "scripts/mods/freya-hud/weapon_icon.png"
local ICON_TINT   = 0xAFD4FF     -- light-blue HUD hue multiplied into slot icons

-- The IDirect3DTexture8* of the per-ability icon for occupied Freya hotbar slot i
-- (1..6), or nil. Only ability cards (ABILITY_VT) carry a 2D icon; weapons return
-- nil (placeholder handled by the caller). Re-resolved each frame; never cached.
local function slot_icon_tex(i)
    local m = enb.mem
    local bank, box = slot_bank_box(i)
    if not bank then return nil end
    if m.u32(box) ~= SLOT_VT then return nil end
    local card = m.u32(box + 0x64)
    if card == 0 or not m.readable(card + 0x64, 4) then return nil end
    card = m.u32(card + 0x64)
    if card == 0 or not m.readable(card, 4) or m.u32(card) ~= ABILITY_VT then return nil end
    local vfn = m.u32(m.u32(card) + ICON_VFN)
    if vfn == 0 then return nil end
    local tc = enb.call(vfn, card)
    if tc == 0 or not m.readable(tc + TEX_D3D, 4) or m.u32(tc) ~= TEXCLASS_VT then return nil end
    local d3d = m.u32(tc + TEX_D3D)
    if d3d == 0 then                       -- not realized yet -> realize it (we run in Present)
        enb.call(TEX_INIT, tc)
        d3d = m.u32(tc + TEX_D3D)
    end
    return (d3d ~= 0) and d3d or nil
end

-- True when occupied Freya hotbar slot i (1..6) holds a weapon group: its action
-- object's bound card (box +0x64 -> +0x64) carries WEAPON_VT. Re-resolved each
-- frame from the captured controller (never cached).
local function slot_is_weapon(i)
    local m = enb.mem
    local bank, box = slot_bank_box(i)
    if not bank then return false end
    if m.u32(box) ~= SLOT_VT then return false end
    local card = m.u32(box + 0x64)
    if card == 0 or not m.readable(card + 0x64, 4) then return false end
    card = m.u32(card + 0x64)
    if card == 0 or not m.readable(card, 4) then return false end
    return m.u32(card) == WEAPON_VT
end

-- Suppress the native action-bar slot chrome that would otherwise bleed through
-- our glass slots (and around them -- the native layout spreads its icons either
-- side of the reticle, not in a tidy row). Each box owns three renderable chrome
-- leaves: the 2D icon glyph at box +0x74 and two highlight/frame leaves at +0x30
-- and +0x58. The in-space HUD draw + input/hover pass acts on a leaf only when
-- (flags & 0x700) == 0x700 at leaf +0x18 (Visible|Enabled|inLayout).
--
-- The earlier code cleared only the 0x400 (inLayout) bit on the glyph. That hid
-- the static icon but NOT the hover highlight: the game RE-SETS 0x400 when the
-- mouse rolls over a box, and that re-set lands after our per-frame clear (which
-- runs early, on the message-pump tick) but before the draw -- so on hover the
-- gate completes again and the native frame flashes back. The fix is to clear a
-- bit the rollover code does NOT touch: 0x200 (Enabled) is set in the resting
-- state and, verified live, is never re-asserted per frame, so once cleared the
-- gate can never reach 0x700 again -- the leaf stays dead in BOTH the normal and
-- hover states. We clear it on all three leaves of all six boxes every frame, so
-- re-slotting / paging can never revive a native leaf. Re-resolved each frame.
local CHROME_VT_LO   = 0x00a00000  -- a renderable UI leaf vtable lands in this band
local CHROME_VT_HI   = 0x00c00000
local LEAF_FLAGS     = 0x18         -- leaf -> visibility flags dword
local LEAF_ENABLE_BIT = 0x200       -- Enabled; clearing it drops the leaf from draw + input
local LEAF_OFFSETS   = { 0x30, 0x58, 0x74 }  -- the box's renderable chrome leaves
local function hide_native_glyphs()
    local m = enb.mem
    local b0, b1 = ab_banks()
    if not b0 then return end
    for _, bank in ipairs({ b0, b1 }) do
        for k = 0, 2 do
            local box = m.u32(bank + 0x10 + k * 4)
            if box ~= 0 then
                for _, off in ipairs(LEAF_OFFSETS) do
                    if m.readable(box + off, 4) then
                        local gl = m.u32(box + off)
                        if gl ~= 0 and m.readable(gl + LEAF_FLAGS, 4) then
                            local vt = m.u32(gl)
                            if vt >= CHROME_VT_LO and vt < CHROME_VT_HI then
                                local fl = m.u32(gl + LEAF_FLAGS)
                                if (fl & LEAF_ENABLE_BIT) ~= 0 then
                                    m.write_u32(gl + LEAF_FLAGS, fl & ~LEAF_ENABLE_BIT)
                                end
                            end
                        end
                    end
                end
            end
        end
    end
end

-- ---- the three vitals (top -> bottom: hull, shield, reactor) ----------------
local VITALS = {
    { key = "hull",   max = "hull_max",   rgb = H.HULL },
    { key = "shield", max = "shield_max", rgb = H.SHIELD },
    { key = "energy", max = "energy_max", rgb = H.REACTOR },   -- reactor
}

local CFG = {
    SLOT          = 46,    -- hotbar slot square edge
    GAP           = 6,     -- gap between slots
    PAGE_W        = 30,    -- width of the page-toggle button at the bar's right
    BOTTOM        = 18,    -- gap from screen bottom to the hotbar (design bottom:18)

    PC_W          = 224,   -- player-card width (design)
    PC_PAD_X      = 7,
    PC_PAD_Y      = 5,
    HEAD_H        = 16,    -- name/level header row
    VITAL_H       = 16,    -- vital bar height (tall enough for the inside value text)
    VITAL_GAP     = 3,
    PCT_W         = 52,    -- percentage column to the right of each vital bar
    VAL_PAD       = 4,     -- inset of the inside value text from the bar's right edge
    PC_GAP        = 24,    -- gap between the player card's bottom and the hotbar top

    FLASH_TICKS   = 10,    -- click-feedback (armed) glow duration in ticks

    SLOT_TOP      = 0x223044,
    SLOT_BOT      = 0x0c121b,
    SLOT_LIT_TOP  = 0x2f4a6e,
    SLOT_LIT_BOT  = 0x132338,
    KEY_INK       = 0xcfe0f2,
}

-- per-slot live state: held (physical key down) + owned (we dispatched this key
-- cycle, so swallow it) + flash (click countdown)
local state = {}
for i = 1, #KEYS do state[i] = { held = false, owned = false, flash = 0 } end
local page_flash = 0   -- click-feedback glow on the page-toggle button

-- ---- layout (recomputed each frame from enb.screen()) ----------------------
local function layout()
    local sw, sh = enb.screen()
    if sw == 0 or sh == 0 then sw, sh = 1280, 992 end  -- pre-first-present fallback

    local bar_w = #KEYS * CFG.SLOT + (#KEYS - 1) * CFG.GAP
    local bar_x = math.floor((sw - bar_w) / 2)
    -- base hotbar Y (1280x960 layout). On a bigger screen the owner lifts the
    -- action bar 20px and the player card 12px. Both offsets derive from
    -- base_bar_y so they do not compound, and all are 0 at the reference res.
    -- They scale @ 2560x1440.
    local base_bar_y = sh - CFG.SLOT - CFG.BOTTOM
    local bar_y = base_bar_y - H.sy(20)

    local pc_h = CFG.PC_PAD_Y + CFG.HEAD_H + CFG.VITAL_GAP
               + #VITALS * CFG.VITAL_H + (#VITALS - 1) * CFG.VITAL_GAP + CFG.PC_PAD_Y
    -- The player card is anchored so its RIGHT edge meets the screen centerline
    -- (owner layout): back off from the centre by the card's fixed design width.
    local pc = { x = math.floor(sw / 2) - CFG.PC_W,
                 y = base_bar_y - CFG.PC_GAP - pc_h - H.sy(12),
                 w = CFG.PC_W, h = pc_h }

    return { sw = sw, sh = sh, bar_x = bar_x, bar_y = bar_y, bar_w = bar_w, pc = pc }
end

local function slot_rect(L, i)
    return L.bar_x + (i - 1) * (CFG.SLOT + CFG.GAP), L.bar_y, CFG.SLOT, CFG.SLOT
end
-- The page-toggle button, a narrow cell just right of slot 6.
local function page_rect(L)
    return L.bar_x + L.bar_w + CFG.GAP, L.bar_y, CFG.PAGE_W, CFG.SLOT
end
local function point_in(px, py, x, y, w, h)
    return px >= x and px < x + w and py >= y and py < y + h
end

-- ---- player card -----------------------------------------------------------
local function draw_player_card(L)
    local me = enb.self()
    -- Live bar fractions from the vitals controller (game::vitals chain). These
    -- work whenever we are in space, independent of the flat-struct player_ptr
    -- calibration (me.base), which models cur/max ints we cannot read yet.
    local vit = (enb.vitals and enb.vitals()) or {}
    local has_flat = me.base ~= 0
    local p = L.pc
    H.glass(p.x, p.y, p.w, p.h)

    -- header: NAME (left) + LV n (right). The name comes live from the vitals
    -- controller's player-entity chain (enb.vitals().name), so it works without
    -- the flat-struct calibration; fall back to the flat name, then PILOT.
    local name
    if vit.name and vit.name ~= "" then name = vit.name
    elseif has_flat and me.name and me.name ~= "" then name = me.name end
    H.otext(p.x + CFG.PC_PAD_X, p.y + CFG.PC_PAD_Y, (name or "PILOT"):upper(),
            name and H.INK or H.UNCAL)
    -- overall level + xp%: live from the controller->data chain (no flat-struct
    -- calibration needed), falling back to the flat struct, then the skeleton.
    local st = (H.stats and H.stats()) or {}
    local lv, lv_known = "LV --", false
    if st.level then
        lv, lv_known = "LV " .. st.level, true
    elseif has_flat then
        lv = "LV " .. ((me.combat_lvl or 0) + (me.explore_lvl or 0) + (me.trade_lvl or 0))
        lv_known = true
    end
    if lv_known and st.xp_pct then
        lv = lv .. string.format("  %d%%", math.floor(st.xp_pct + 0.5))
    end
    local lw = H.measure(lv)
    H.otext(p.x + p.w - CFG.PC_PAD_X - lw, p.y + CFG.PC_PAD_Y, lv,
            lv_known and H.INK_DIM or H.UNCAL)

    -- vitals
    local track_x = p.x + CFG.PC_PAD_X
    local track_w = p.w - CFG.PC_PAD_X * 2 - CFG.PCT_W
    local vy = p.y + CFG.PC_PAD_Y + CFG.HEAD_H + CFG.VITAL_GAP
    for _, v in ipairs(VITALS) do
        -- numeric cur/max come live from the AuxData property bag (H.stats):
        -- hull is stored absolute; shield/reactor store only the max, so their
        -- current is max * the live gadget fill fraction. Fall back to the flat
        -- struct only when aux gave nothing.
        local frac = vit[v.key]
        local max  = st[v.max]
        local cur
        if v.key == "hull" then cur = st.hull
        elseif max and frac then cur = max * frac end
        if (cur == nil or max == nil) and has_flat then
            cur, max = cur or me[v.key], max or me[v.max]
        end
        -- prefer the live gadget fraction; fall back to cur/max if present.
        if frac == nil and cur and max and max > 0 then frac = cur / max end
        local known = frac ~= nil
        if known then frac = frac < 0 and 0 or (frac > 1 and 1 or frac) end
        local have_nums = cur ~= nil and max ~= nil
        -- track + fill + border
        enb.draw.rrect(track_x, vy, track_w, CFG.VITAL_H, H.RADIUS, H.TRACK, 220, true)
        if known and frac > 0 then
            local fw = math.floor(track_w * frac)
            if fw > 0 then
                enb.draw.rrect_grad(track_x, vy, fw, CFG.VITAL_H, H.RADIUS,
                                    v.rgb, H.darker(v.rgb), 255)
            end
        end
        enb.draw.rrect(track_x, vy, track_w, CFG.VITAL_H, H.RADIUS, H.LINE, 70, false)
        if known then
            local _, vh = H.measure("0%")
            local ty = vy + math.floor((CFG.VITAL_H - vh) / 2)
            -- raw "cur / max" whenever we have the numbers (aux or flat); always the %.
            if have_nums then
                local val = string.format("%d / %d", math.floor(cur + 0.5), math.floor(max + 0.5))
                H.otext(track_x + track_w - CFG.VAL_PAD - H.measure(val), ty, val, 0xffffff)
            end
            local ps = math.floor(frac * 100 + 0.5) .. "%"
            -- left-align the % in its own column just right of the bar, so a wide
            -- glyph run can't bleed back over the bar (right-aligning at the card
            -- edge pushed "100%" left onto the bar).
            H.otext(track_x + track_w + 8, ty + 2, ps, H.INK)
        end
        vy = vy + CFG.VITAL_H + CFG.VITAL_GAP
    end
end

-- The page-toggle button. Shows the page it will switch TO (7-12 while on page 0,
-- 1-6 while on page 1) so the label always reads as the action it performs.
local function draw_page_button(L)
    local x, y, w, h = page_rect(L)
    local lit = page_flash > 0
    local top = lit and CFG.SLOT_LIT_TOP or CFG.SLOT_TOP
    local bot = lit and CFG.SLOT_LIT_BOT or CFG.SLOT_BOT
    enb.draw.rrect_grad(x, y, w, h, H.RADIUS, top, bot, 235)
    enb.draw.rrect_grad(x, y, w, math.floor(h * 0.46), H.RADIUS, 0xffffff, top,
                        lit and 42 or 24)
    enb.draw.rrect(x, y, w, h, H.RADIUS, lit and H.LINE_HOT or H.LINE,
                   lit and 220 or 150, false)
    local target = (current_page() == 0) and "7-12" or "1-6"
    local tw, th = H.measure(target)
    H.otext(x + math.floor((w - tw) / 2), y + math.floor((h - th) / 2) + 3,
            target, CFG.KEY_INK)
    H.otext(x + math.floor((w - H.measure("PG")) / 2), y + 3, "PG", H.INK_DIM)
    if page_flash > 0 then page_flash = page_flash - 1 end
end

-- ---- hotbar ----------------------------------------------------------------
local function draw_hotbar(L)
    for i, k in ipairs(KEYS) do
        local x, y, w, h = slot_rect(L, i)
        local lit = state[i].held or state[i].flash > 0
        local top = lit and CFG.SLOT_LIT_TOP or CFG.SLOT_TOP
        local bot = lit and CFG.SLOT_LIT_BOT or CFG.SLOT_BOT
        enb.draw.rrect_grad(x, y, w, h, H.RADIUS, top, bot, 235)
        -- slot icon. An ability draws its real per-ability icon (the client's own
        -- runtime texture, blitted via texture_quad with the icon's black background
        -- keyed out and a light-blue HUD tint); a weapon group has no 2D glyph so we
        -- draw our original Freya placeholder. Both re-resolved each frame.
        local inset = 3
        if slot_is_weapon(i) then
            enb.draw.image(WEAPON_ICON, x + inset, y + inset, w - 2 * inset,
                           h - 2 * inset, 255)
        else
            local tex = slot_icon_tex(i)
            if tex then
                enb.draw.texture_quad(tex, x + inset, y + inset, w - 2 * inset,
                                      h - 2 * inset, 255, ICON_TINT)
            end
        end
        -- gloss sheen across the top
        enb.draw.rrect_grad(x, y, w, math.floor(h * 0.46), H.RADIUS, 0xffffff, top,
                            lit and 42 or 24)
        -- border (cyan armed glow when lit)
        enb.draw.rrect(x, y, w, h, H.RADIUS, lit and H.LINE_HOT or H.LINE,
                       lit and 220 or 150, false)
        -- key label, top-right corner -- follows the live page (1-6 or 7-12)
        local label = slot_label(i)
        local lw = H.measure(label)
        H.otext(x + w - lw - 5, y + 3, label, CFG.KEY_INK)
        if state[i].flash > 0 then state[i].flash = state[i].flash - 1 end
    end
    draw_page_button(L)
end

-- ===========================================================================
-- DRAW
-- ===========================================================================
enb.on_tick(function()
    local show, show_hotbar = H.vis()
    -- draw our cursor on TOP of the overlay whenever the HUD is up (owner ask:
    -- "draw mouse pointer on top not under"); hide it when the HUD is hidden.
    if enb.cursor then enb.cursor(show) end
    if not show then return end
    local L = layout()
    draw_player_card(L)
    if show_hotbar and not RECORD_MODE then
        hide_native_glyphs()   -- kill the native slot icons before painting ours
        draw_hotbar(L)
    end
end)

-- ===========================================================================
-- INPUT  (return true = swallow; fail-open handled in C++)
-- ===========================================================================
local function mouse_xy(lp)
    local x = lp & 0xFFFF
    local y = (lp >> 16) & 0xFFFF
    if x >= 0x8000 then x = x - 0x10000 end
    if y >= 0x8000 then y = y - 0x10000 end
    return x, y
end

enb.on_input(function(msg, wparam, lparam)
    local M = enb.msg
    local show, show_hotbar = H.vis()
    if not show then return false end   -- HUD hidden -> never touch input

    -- chat input box open -> ALL keyboard belongs to it; yield every key/char so
    -- the action bar never fires mid-typing. (chat.lua's handler runs first and
    -- already swallows; this is the order-independent belt-and-suspenders.) Mouse
    -- still works -- chat captures keyboard only.
    if H.chat_capturing
       and (msg == M.KEYDOWN or msg == M.SYSKEYDOWN
            or msg == M.KEYUP or msg == M.SYSKEYUP or msg == M.CHAR) then
        return false
    end

    -- physical key down/up lights the matching slot (only meaningful when the
    -- hotbar is up). Never swallow -- the game still needs the bind.
    if msg == M.KEYDOWN or msg == M.SYSKEYDOWN then
        local i = VK_TO_IDX[wparam]
        if i and show_hotbar and not RECORD_MODE then
            -- OWN the keybind: on the leading edge dispatch the PRESS id (fires the
            -- ability, sets box +0x6c) and SWALLOW the key so the native bar never
            -- sees it -- no native light, no double-fire. The matching RELEASE is
            -- sent on key-up so the box flag resets and every tap fires (see
            -- press_slot/tap_slot note). If the controller is not captured yet
            -- press_slot returns false; we fall through (return false) so the native
            -- keybind fires it AND warms the capture, and we don't swallow this up.
            if not state[i].held then
                state[i].held = true
                state[i].owned = press_slot(i)
            end
            if state[i].owned then return true end   -- swallow leading edge + auto-repeat
        end
        return false
    elseif msg == M.KEYUP or msg == M.SYSKEYUP then
        local i = VK_TO_IDX[wparam]
        if i then
            state[i].held = false
            local owned = state[i].owned
            state[i].owned = false
            -- dispatch the matching RELEASE (resets box +0x6c -> 0) and swallow the
            -- up, but only when we owned the down (a native-handled key keeps its up;
            -- a Freya-owned key never reaches native). Without this the box flag
            -- sticks at 1 and the next press reads as a release -> every-other-tap.
            if owned and show_hotbar and not RECORD_MODE then
                release_slot(i)
                return true
            end
        end
        return false
    end

    -- mouse: act on / swallow within our cards
    if msg == M.MOUSEMOVE or msg == M.MOUSEWHEEL
       or msg == M.LBUTTONDOWN or msg == M.LBUTTONUP or msg == M.LBUTTONDBLCLK
       or msg == M.RBUTTONDOWN or msg == M.RBUTTONUP or msg == M.RBUTTONDBLCLK
       or msg == M.MBUTTONDOWN or msg == M.MBUTTONUP or msg == M.MBUTTONDBLCLK then
        local mx, my = mouse_xy(lparam)
        local L = layout()

        if show_hotbar and not RECORD_MODE and msg == M.LBUTTONDOWN then
            -- page-toggle button: flip both banks to the other page (7-12 <-> 1-6)
            local px, py, pw, ph = page_rect(L)
            if point_in(mx, my, px, py, pw, ph) then
                if toggle_page() then page_flash = CFG.FLASH_TICKS end
                return true
            end
            for i = 1, #KEYS do
                local x, y, w, h = slot_rect(L, i)
                if point_in(mx, my, x, y, w, h) then
                    -- dispatch through the captured controller (our own path, the
                    -- proof the Freya bar drives the action). Before any slot has
                    -- been used in space the bank is not captured yet, so fall
                    -- back to tapping the native key -- which fires it AND warms
                    -- the capture so subsequent clicks take our path.
                    if not tap_slot(i) then enb.tap(KEYS[i].vk) end
                    state[i].flash = CFG.FLASH_TICKS
                    return true
                end
            end
        end

        -- swallow mouse over the player card (always) and the hotbar + page
        -- button (when up)
        local p = L.pc
        if point_in(mx, my, p.x, p.y, p.w, p.h) then return true end
        if show_hotbar and not RECORD_MODE
           and point_in(mx, my, L.bar_x, L.bar_y,
                        L.bar_w + CFG.GAP + CFG.PAGE_W, CFG.SLOT) then
            return true
        end
    end

    return false
end, enb.WANT_KEY | enb.WANT_MOUSE)

enb.log("freya_ui.lua loaded")
