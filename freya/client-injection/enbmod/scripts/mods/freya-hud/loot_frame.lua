-- SPDX-License-Identifier: MIT
-- Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
-- License: LICENSES/Freya
--
-- loot_frame.lua -- the Freya redesign of the hulk / salvage LOOT window.
--
-- The native loot window pops on the RIGHT the moment a hulk's cargo grid is
-- granted, directly on top of the Freya party frame (also right-anchored). This
-- module repaints the contents Freya-style on the LEFT edge so the two never
-- collide, and shows the full per-slot detail the native grid does: Slot, Name,
-- Stack size, and (once its texture offset is confirmed live) the item Icon.
--
-- Data comes from enb.loot() -- the VEH-guarded read in lua_api that replays the
-- three hulk cargo accessors off the container captured read-only from the
-- per-slot ItemTemplateID accessor. Each row is
--   { slot=<0-based>, name=<string>, stack=<qty>, tid=<templateId>,
--     tmpl=<template ptr>, asset=<template+icon-asset ptr> }
-- and the array carries a top-level `src` = the container's inventory-name
-- ("Cargo" for a hulk). Because that accessor fires for ANY inventory grid
-- (a hulk, your own ship cargo, a vault), the panel only trusts the rows while
-- the latch is FRESH -- enb.loot_age() reports ms since the grid last repainted;
-- a closed loot window goes stale and the panel disappears.
--
-- READ-ONLY for now: this panel DISPLAYS loot; the actual take/loot-all still
-- runs through the native window (kept alive underneath). Wiring a Freya
-- take-action means emitting the generic inventory-move packet (opcode 0x27),
-- whose field order is not yet capture-pinned -- so we do NOT fabricate it blind
-- against a live session, and we do NOT hide the native window until it is. See
-- plans/49 (loot-UI) + plans/29 client-verification.

local H = require("freya_hud")

-- ---- geometry (window-relative; anchored to the LEFT edge, stacked down) ------
local CFG = {
    PANEL_W  = 210,   -- row content width (icon + name + stack) at the reference res
    MARGIN_L = 28,    -- gap from the left screen edge (mirror of party_frame MARGIN_R)
    MARGIN_T = 300,   -- gap from the top screen edge (left-middle band, clear of chat)
    MARGIN_L_TUNE = 24,
    MARGIN_T_TUNE = 40,

    PAD      = 8,     -- inner padding inside the glass panel
    HDR_H    = 18,    -- header line height
    HDR_SCALE = 1.05, -- header font scale
    HDR_GAP  = 6,     -- gap from header down to the first row

    ROW_H    = 24,    -- one loot row height (icon box drives it)
    ROW_GAP  = 4,     -- gap between rows
    ICON     = 22,    -- square icon box edge at the reference res
    ICON_GAP = 7,     -- gap from icon to the name text
    NAME_SCALE = 1.0,
    STACK_SCALE = 0.95,
    STACK_PAD = 4,    -- inset of the "xN" stack text from the row's right edge

    STALE_MS = 5000,  -- hide the panel once the container latch is this stale (window closed)
    MAX_ROWS = 24,    -- hard cap on drawn rows (a hulk never carries this many; guards a bad read)
}

-- one loot row, top edge at `y`; returns the y just below the row. `slot_disp` is
-- the 1-based slot number shown to the user (enb.loot() reports 0-based indices).
local function draw_row(r, x, y, w, slot_disp)
    local icon = CFG.ICON + H.sy(6)
    -- icon box: a glass tile. The item's icon texture is blitted here once its
    -- offset off row.asset is confirmed live; until then it is an empty tile so
    -- the layout is final and only the pixels drop in later.
    enb.draw.rrect_grad(x, y, icon, icon, H.RADIUS, H.PANEL_TOP, H.PANEL_BOT, 200)
    enb.draw.rrect(x, y, icon, icon, H.RADIUS, H.LINE, 90, false)

    local tx = x + icon + CFG.ICON_GAP
    local tw = w - icon - CFG.ICON_GAP

    -- slot badge (dim) then the item name (bright), clipped to the text column
    -- leaving room for the right-aligned stack count.
    local badge = string.format("%d.", slot_disp)
    H.otext(tx, y, badge, H.INK_DIM, CFG.NAME_SCALE)
    local bw = H.measure(badge) * CFG.NAME_SCALE
    local name_x = tx + bw + H.sx(4) + 6

    local stack = (r.stack and r.stack > 1) and ("x" .. tostring(r.stack)) or nil
    local sw = 0
    if stack then sw = H.measure(stack) * CFG.STACK_SCALE + CFG.STACK_PAD end

    local name = (type(r.name) == "string" and r.name ~= "") and r.name or "Unknown Item"
    local name_room = (tx + tw - CFG.STACK_PAD - sw) - name_x
    while H.measure(name) * CFG.NAME_SCALE > name_room and #name > 1 do
        name = name:sub(1, #name - 1)
    end
    -- vertically centre the name against the icon tile.
    local _, nh = H.measure(name)
    nh = nh * CFG.NAME_SCALE
    local name_y = y + (icon - nh) / 2
    H.otext(name_x, name_y, name, H.INK, CFG.NAME_SCALE)

    if stack then
        local _, sh = H.measure(stack)
        sh = sh * CFG.STACK_SCALE
        H.otext(tx + tw - CFG.STACK_PAD - (sw - CFG.STACK_PAD), y + (icon - sh) / 2,
                stack, H.REACTOR, CFG.STACK_SCALE)
    end
    return y + icon
end

local panel_rect = nil  -- last drawn panel bounds, for the click-swallow hit test

local function draw_loot()
    panel_rect = nil
    if not enb.loot then return end
    -- freshness gate: an open loot grid repaints continuously, re-latching the
    -- container; a closed one goes stale. Never read a stale (possibly freed)
    -- container.
    local age = enb.loot_age and enb.loot_age() or -1
    if age < 0 or age > CFG.STALE_MS then return end

    local rows = enb.loot()
    if type(rows) ~= "table" or #rows == 0 then return end

    local sw, sh = enb.screen()
    if not sw or sw == 0 then sw, sh = 1280, 960 end

    local w = CFG.PANEL_W + H.sx(CFG.PANEL_W * 0.5)
    local pad = CFG.PAD
    local icon = CFG.ICON + H.sy(6)
    local n = math.min(#rows, CFG.MAX_ROWS)

    -- header text: "SALVAGE" plus, during bring-up, the container's inventory-name
    -- so a live read can confirm the hulk-vs-ship-inventory discriminator (`src`).
    local hdr = "SALVAGE"
    if type(rows.src) == "string" and rows.src ~= "" then
        hdr = "SALVAGE  (" .. rows.src .. ")"
    end
    local hdr_h = CFG.HDR_H + H.sy(4)

    local body_h = n * icon + (n - 1) * CFG.ROW_GAP
    local panel_w = w + pad * 2
    local panel_h = hdr_h + CFG.HDR_GAP + body_h + pad * 2

    local px = CFG.MARGIN_L + H.sx(CFG.MARGIN_L_TUNE)
    local py = CFG.MARGIN_T + H.sy(CFG.MARGIN_T_TUNE)

    H.glass(px, py, panel_w, panel_h)
    panel_rect = { x = px, y = py, w = panel_w, h = panel_h }

    local x = px + pad
    local y = py + pad
    H.otext(x, y, hdr, H.INK, CFG.HDR_SCALE)
    y = y + hdr_h + CFG.HDR_GAP

    for i = 1, n do
        local r = rows[i]
        y = draw_row(r, x, y, w, (r.slot or (i - 1)) + 1) + CFG.ROW_GAP
    end
end

-- ===========================================================================
-- DRAW -- only while the HUD is visible AND a fresh hulk cargo grid exists.
-- ===========================================================================
enb.on_tick(function()
    panel_rect = nil        -- clear so a stale rect never eats a click while hidden
    if not H.vis() then return end
    draw_loot()
end)

-- ===========================================================================
-- INPUT -- swallow a click that lands on the panel so it never falls through to
-- the 3D scene behind it (deselecting the target / rotating the camera). The
-- panel is read-only, so there is nothing else to do with the click yet.
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
    if not H.vis() or not panel_rect then return false end
    if msg ~= M.LBUTTONDOWN and msg ~= M.LBUTTONUP and msg ~= M.LBUTTONDBLCLK then
        return false
    end
    local mx, my = mouse_xy(lparam)
    local r = panel_rect
    if mx >= r.x and mx < r.x + r.w and my >= r.y and my < r.y + r.h then
        return true  -- swallow: keep clicks off the scene behind the panel
    end
    return false
end, enb.WANT_MOUSE)

enb.log("loot_frame.lua loaded")
