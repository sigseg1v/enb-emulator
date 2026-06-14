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
--   * Hotbar -- twelve square glass slots for keys 1 2 3 4 5 6 7 8 9 0 - =, with
--     the design's small radius + gloss + hairline border and a cyan armed glow.
--     Click a slot -> taps its key (enb.tap); a physical keypress lights it.
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

-- ---- the twelve action-bar keys --------------------------------------------
local KEYS = {
    { label = "1", vk = 0x31 }, { label = "2", vk = 0x32 }, { label = "3", vk = 0x33 },
    { label = "4", vk = 0x34 }, { label = "5", vk = 0x35 }, { label = "6", vk = 0x36 },
    { label = "7", vk = 0x37 }, { label = "8", vk = 0x38 }, { label = "9", vk = 0x39 },
    { label = "0", vk = 0x30 }, { label = "-", vk = 0xBD }, { label = "=", vk = 0xBB },
}
local VK_TO_IDX = {}
for i, k in ipairs(KEYS) do VK_TO_IDX[k.vk] = i end

-- ---- the three vitals (top -> bottom: hull, shield, reactor) ----------------
local VITALS = {
    { key = "hull",   max = "hull_max",   rgb = H.HULL },
    { key = "shield", max = "shield_max", rgb = H.SHIELD },
    { key = "energy", max = "energy_max", rgb = H.REACTOR },   -- reactor
}

local CFG = {
    SLOT          = 46,    -- hotbar slot square edge
    GAP           = 6,     -- gap between slots
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

-- per-slot live state: held (physical key down) + flash (click countdown)
local state = {}
for i = 1, #KEYS do state[i] = { held = false, flash = 0 } end

-- ---- layout (recomputed each frame from enb.screen()) ----------------------
local function layout()
    local sw, sh = enb.screen()
    if sw == 0 or sh == 0 then sw, sh = 1280, 992 end  -- pre-first-present fallback

    local bar_w = #KEYS * CFG.SLOT + (#KEYS - 1) * CFG.GAP
    local bar_x = math.floor((sw - bar_w) / 2)
    local bar_y = sh - CFG.SLOT - CFG.BOTTOM

    local pc_h = CFG.PC_PAD_Y + CFG.HEAD_H + CFG.VITAL_GAP
               + #VITALS * CFG.VITAL_H + (#VITALS - 1) * CFG.VITAL_GAP + CFG.PC_PAD_Y
    local pc = { x = bar_x, y = bar_y - CFG.PC_GAP - pc_h, w = CFG.PC_W, h = pc_h }

    return { sw = sw, sh = sh, bar_x = bar_x, bar_y = bar_y, bar_w = bar_w, pc = pc }
end

local function slot_rect(L, i)
    return L.bar_x + (i - 1) * (CFG.SLOT + CFG.GAP), L.bar_y, CFG.SLOT, CFG.SLOT
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
        lv = "LV " .. math.max(me.combat_lvl or 0, me.explore_lvl or 0, me.trade_lvl or 0)
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
        local cur, max = me[v.key], me[v.max]
        -- prefer the live gadget fraction; fall back to flat cur/max if present.
        local frac = vit[v.key]
        if frac == nil and has_flat and cur and max and max > 0 then frac = cur / max end
        local known = frac ~= nil
        if known then frac = frac < 0 and 0 or (frac > 1 and 1 or frac) end
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
            -- raw "cur / max" only when the flat struct gives it; always the %.
            if has_flat and cur and max then
                local val = string.format("%d / %d", cur, max)
                H.otext(track_x + track_w - CFG.VAL_PAD - H.measure(val), ty + 2, val, 0xffffff)
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

-- ---- hotbar ----------------------------------------------------------------
local function draw_hotbar(L)
    for i, k in ipairs(KEYS) do
        local x, y, w, h = slot_rect(L, i)
        local lit = state[i].held or state[i].flash > 0
        local top = lit and CFG.SLOT_LIT_TOP or CFG.SLOT_TOP
        local bot = lit and CFG.SLOT_LIT_BOT or CFG.SLOT_BOT
        enb.draw.rrect_grad(x, y, w, h, H.RADIUS, top, bot, 235)
        -- gloss sheen across the top
        enb.draw.rrect_grad(x, y, w, math.floor(h * 0.46), H.RADIUS, 0xffffff, top,
                            lit and 42 or 24)
        -- border (cyan armed glow when lit)
        enb.draw.rrect(x, y, w, h, H.RADIUS, lit and H.LINE_HOT or H.LINE,
                       lit and 220 or 150, false)
        -- key label, top-right corner
        local lw, lh = H.measure(k.label)
        H.otext(x + w - lw - 5, y + 3, k.label, CFG.KEY_INK)
        if state[i].flash > 0 then state[i].flash = state[i].flash - 1 end
    end
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
    if show_hotbar then draw_hotbar(L) end
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

    -- physical key down/up lights the matching slot (only meaningful when the
    -- hotbar is up). Never swallow -- the game still needs the bind.
    if msg == M.KEYDOWN or msg == M.SYSKEYDOWN then
        local i = VK_TO_IDX[wparam]
        if i and show_hotbar then state[i].held = true end
        return false
    elseif msg == M.KEYUP or msg == M.SYSKEYUP then
        local i = VK_TO_IDX[wparam]
        if i then state[i].held = false end
        return false
    end

    -- mouse: act on / swallow within our cards
    if msg == M.MOUSEMOVE or msg == M.MOUSEWHEEL
       or msg == M.LBUTTONDOWN or msg == M.LBUTTONUP or msg == M.LBUTTONDBLCLK
       or msg == M.RBUTTONDOWN or msg == M.RBUTTONUP or msg == M.RBUTTONDBLCLK
       or msg == M.MBUTTONDOWN or msg == M.MBUTTONUP or msg == M.MBUTTONDBLCLK then
        local mx, my = mouse_xy(lparam)
        local L = layout()

        if show_hotbar and msg == M.LBUTTONDOWN then
            for i = 1, #KEYS do
                local x, y, w, h = slot_rect(L, i)
                if point_in(mx, my, x, y, w, h) then
                    enb.tap(KEYS[i].vk)
                    state[i].flash = CFG.FLASH_TICKS
                    return true
                end
            end
        end

        -- swallow mouse over the player card (always) and the hotbar (when up)
        local p = L.pc
        if point_in(mx, my, p.x, p.y, p.w, p.h) then return true end
        if show_hotbar and point_in(mx, my, L.bar_x, L.bar_y, L.bar_w, CFG.SLOT) then
            return true
        end
    end

    return false
end, enb.WANT_KEY | enb.WANT_MOUSE)

enb.log("freya_ui.lua loaded")
