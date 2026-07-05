-- SPDX-License-Identifier: MIT
-- Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
-- License: LICENSES/Freya
--
-- party_frame.lua -- the Freya redesign of the PARTY / group member frame.
--
-- The native group window showed up to 5 party members as a stacked column, each
-- row a member name over a red hull bar and a blue shield bar, with a button row
-- on top (GRP DISBAND for the leader / GRP LEAVE for a member, plus UI FORMATION).
-- The Phase AS HUD overhaul dropped it; this module repaints it in the Freya glass
-- style, anchored to the RIGHT edge and stacked DOWNWARD (radar sits above it, the
-- target frame below it, so the right-middle band is free).
--
-- The button row is likewise Freya-DRAWN (the native buttons are game-managed
-- transients we do not resurrect -- same conclusion as the target-frame verb row):
--   leader:  [D]isband  [F]ormation  [T]arget      member: [L]eave [F] [T]
-- D/L fire the native avatar-command path (enb.target_action op 0x0d disband /
-- 0x0e leave, self-targeted -- the exact packets the /group slash verbs send).
-- T asks the group to target the clicker's target (enb.group_action 12, the
-- CTARequest packet the native window's Target control sends). F expands a second
-- row of formation actions, asymmetric by role because the server enforces it:
-- setting the formation TYPE is leader-only, members can only join/leave it.
--   leader F row:  [S]lot Back  [B]lock  [P]ipe  [X] break formation
--   member F row:  [U] form up  [X] leave formation
-- The whole row needs the enb.is_leader/enb.group_action DLL support; on an older
-- DLL it degrades to the plain roster panel at its original position.
--
-- Data comes from enb.group() (the crash-safe, VEH-guarded roster read in lua_api):
-- { count = N, members = { { name=, gameid=, [hull=, hull_max=, shield=, shield_max=] }, ... } }.
-- Name + GameID are read straight off the GroupInfo member objects; hull/shield are
-- resolved by looking each member's GameID up in the world entity table and reading its
-- aux bag (same source as the target frame). That only works for members in the current
-- sector / scanner range -- a member elsewhere has no live entity, so the roster omits
-- its vitals and its bars render as empty tracks (the convention target_frame uses for a
-- vitals-less target). The panel only draws while in a group (count > 0); solo players
-- see nothing.

local H = require("freya_hud")

-- ---- geometry (window-relative; anchored to the right edge, stacked down) -----
local CFG = {
    PANEL_W  = 188,   -- panel / row content width at the reference res
    MARGIN_R = 28,    -- gap from the right screen edge (inset 16px past the base 12)
    MARGIN_T = 228,   -- gap from the top screen edge to the frame top (below the radar)
    -- on a larger-than-1280x960 screen pull the panel inward from the right and down
    -- from the top, matching how the rest of the HUD scales its border gaps (both 0 at
    -- the reference res, so the base layout is untouched).
    MARGIN_R_TUNE = 24,
    MARGIN_T_TUNE = 40,

    PAD      = 7,      -- inner padding inside the glass panel
    ROW_GAP  = 8,      -- gap between two member rows
    NAME_H   = 18,     -- member-name line height (grown to fit the bigger name font)
    NAME_SCALE = 1.15, -- member-name font scale (bigger than the 1.0 default)
    NAME_GAP = 3,      -- gap from the name down to the hull bar
    BAR_H    = 12,     -- vital bar height (taller than target_frame's thin 6px bars)
    BAR_GAP  = 1,      -- 1px gap between the stacked hull / shield bars
    VAL_PAD  = 4,      -- inset of the cur/total value text from the bar's right edge
    VAL_SCALE = 0.78,  -- value-text scale (bigger; the taller bar has room for it)

    BTN_SIZE = 17,     -- square button edge at the reference res (matches target_frame)
    BTN_GAP  = 3,      -- gap between adjacent buttons
    BTN_ROW_GAP = 4,   -- gap between the two button rows / down to the panel
    FLASH_TICKS = 16,  -- click-feedback glow duration (held long enough to read)
}

-- ---- one bar: track + gradient fill + border + optional cur/total text -------
-- Mirrors target_frame.draw_bar. cur/max may be nil (vitals unknown for a remote
-- member) -> empty track, no value text.
local function draw_bar(x, y, w, rgb, cur, max, bar_h, val_scale)
    enb.draw.rrect(x, y, w, bar_h, H.RADIUS, H.TRACK, 220, true)
    if cur and max and max > 0 then
        local frac = cur / max
        if frac < 0 then frac = 0 elseif frac > 1 then frac = 1 end
        local fw = math.floor(w * frac)
        if fw > 0 then
            enb.draw.rrect_grad(x, y, fw, bar_h, H.RADIUS, rgb, H.darker(rgb), 255)
        end
    end
    enb.draw.rrect(x, y, w, bar_h, H.RADIUS, H.LINE, 70, false)
    if cur and max then
        local val = string.format("%d / %d", math.floor(cur + 0.5), math.floor(max + 0.5))
        local vw, vh = H.measure(val)
        local eff = val_scale + (H.sy(2) + 2) / vh
        vw, vh = vw * eff, vh * eff
        local ty = y + (bar_h - vh) / 2
        H.otext(x + w - CFG.VAL_PAD - vw, ty, val, 0xffffff, eff)
    end
end

-- one member row, top edge at `y`, returns the y just below the row.
local function draw_member(m, x, y, w, bar_h, val_scale)
    local name = (type(m.name) == "string" and m.name ~= "") and m.name or "Member"
    -- clip an over-long name to the panel width (measured at the drawn scale).
    while H.measure(name) * CFG.NAME_SCALE > w and #name > 1 do name = name:sub(1, #name - 1) end
    H.otext(x, y, name, H.INK, CFG.NAME_SCALE)
    local hull_y   = y + CFG.NAME_H + CFG.NAME_GAP
    local shield_y = hull_y + bar_h + CFG.BAR_GAP
    draw_bar(x, hull_y,   w, H.HULL,   m.hull,   m.hull_max,   bar_h, val_scale)
    draw_bar(x, shield_y, w, H.SHIELD, m.shield, m.shield_max, bar_h, val_scale)
    return shield_y + bar_h
end

-- ---- the group action-button rows (drawn ABOVE the roster panel) --------------
-- Live button rects (rebuilt every draw) + per-id transient click-flash, so the
-- on_input hit-test and the draw agree on geometry without recomputing it. The
-- same pattern as target_frame's verb row.
local btn_rects = {}
local member_rects = {}  -- one per drawn roster row: { x,y,w,h, gid, name } -- click to target
local flash = {}         -- [id] = remaining ticks of the click-acknowledge glow
local form_open = false  -- F toggles the formation sub-row

local function point_in(px, py, x, y, w, h)
    return px >= x and px < x + w and py >= y and py < y + h
end

-- Button defs by role. A button fires ONE of:
--   op     -> enb.target_action(op, "self")  (native avatar-command, self-targeted)
--   cta    -> enb.group_action(cta)          (CTARequest 0xBC, whole group)
--   toggle -> open/close the formation sub-row (client-side only)
local function build_rows(leader)
    local top = {}
    if leader then
        top[#top + 1] = { id = "disband", letter = "D", label = "Disband group", op = 0x0d }
    else
        top[#top + 1] = { id = "leave", letter = "L", label = "Leave group", op = 0x0e }
    end
    top[#top + 1] = { id = "form",   letter = "F", label = "Formation", toggle = true }
    top[#top + 1] = { id = "target", letter = "T", label = "Target my target", cta = 12 }
    local rows = { top }
    if form_open then
        local sub = {}
        if leader then
            -- the three formation types are leader-only server-side; X breaks it.
            sub[#sub + 1] = { id = "f_slot",  letter = "S", label = "Formation: Slot Back", cta = 4 }
            sub[#sub + 1] = { id = "f_block", letter = "B", label = "Formation: Block",     cta = 5 }
            sub[#sub + 1] = { id = "f_pipe",  letter = "P", label = "Formation: Pipe",      cta = 6 }
            sub[#sub + 1] = { id = "f_break", letter = "X", label = "Break formation",      cta = 9 }
        else
            sub[#sub + 1] = { id = "f_up",    letter = "U", label = "Form up",              cta = 7 }
            sub[#sub + 1] = { id = "f_leave", letter = "X", label = "Leave formation",      cta = 8 }
        end
        rows[#rows + 1] = sub
    end
    return rows
end

-- Draw the button rows top-down starting at (x, y); returns the y just below the
-- last row. Appends to btn_rects. Styling mirrors target_frame.draw_buttons_up.
local function draw_button_rows(rows, x, y, maxw)
    local bsz = CFG.BTN_SIZE + H.sy(7)
    local gap = CFG.BTN_GAP + H.sx(2)
    for _, row in ipairs(rows) do
        local cx = x
        for _, v in ipairs(row) do
            if cx + bsz > x + maxw and cx > x then break end   -- clip to panel width
            btn_rects[#btn_rects + 1] = { x = cx, y = y, w = bsz, h = bsz,
                                          id = v.id, op = v.op, cta = v.cta,
                                          toggle = v.toggle, letter = v.letter,
                                          label = v.label }
            cx = cx + bsz + gap
        end
        y = y + bsz + CFG.BTN_ROW_GAP
    end
    for _, r in ipairs(btn_rects) do
        -- hot = clicked (or, for F, the sub-row is held open): vivid cyan so the
        -- press reads unmistakably even when the server action is quiet.
        local hot = (flash[r.id] or 0) > 0 or (r.toggle and form_open)
        H.glass(r.x, r.y, r.w, r.h)
        local tcol, bcol, fa
        if hot then
            tcol, bcol, fa = 0x7ad4ff, 0x2a8fd6, 255
        else
            tcol, bcol, fa = 0x223044, 0x0c121b, 230
        end
        enb.draw.rrect_grad(r.x, r.y, r.w, r.h, H.RADIUS, tcol, bcol, fa)
        enb.draw.rrect(r.x, r.y, r.w, r.h, H.RADIUS,
                       hot and H.LINE_HOT or H.LINE, hot and 255 or 150, false)
        local scale = H.smul(1.5)
        local lw, lh = H.measure(r.letter)
        lw, lh = lw * scale, lh * scale
        H.otext(r.x + (r.w - lw) / 2, r.y + (r.h - lh) / 2, r.letter,
                hot and 0x06121f or H.INK, scale)
    end
    -- decay the click glow one tick so the feedback fades.
    for id, n in pairs(flash) do flash[id] = (n > 1) and (n - 1) or nil end
    return y - CFG.BTN_ROW_GAP
end

local function draw_party()
    local g = enb.group and enb.group()
    if not g or not g.count or g.count <= 0 then return end
    local members = g.members or {}
    if #members == 0 then return end

    local sw, sh = enb.screen()
    if not sw or sw == 0 then sw, sh = 1280, 960 end

    local bar_h     = CFG.BAR_H + H.sy(6)
    local val_scale = CFG.VAL_SCALE * H.smul(1.5)
    local w = CFG.PANEL_W + H.sx(CFG.PANEL_W * 0.5)   -- widen on a big screen

    local row_h = CFG.NAME_H + CFG.NAME_GAP + bar_h * 2 + CFG.BAR_GAP
    local body_h = #members * row_h + (#members - 1) * CFG.ROW_GAP
    local pad = CFG.PAD
    local panel_w = w + pad * 2
    local panel_h = body_h + pad * 2

    local px = sw - (CFG.MARGIN_R + H.sx(CFG.MARGIN_R_TUNE)) - panel_w
    local py = CFG.MARGIN_T + H.sy(CFG.MARGIN_T_TUNE)

    -- action buttons sit at the frame top; the roster panel moves down below them.
    -- Needs the DLL-side group-action support -- without it (older DLL) the panel
    -- stays at the original position with no buttons.
    if enb.is_leader and enb.group_action then
        py = draw_button_rows(build_rows(enb.is_leader()), px, py, panel_w)
            + CFG.BTN_ROW_GAP + 2
    end

    H.glass(px, py, panel_w, panel_h)

    local x = px + pad
    local y = py + pad
    for _, m in ipairs(members) do
        local top = y
        local bot = draw_member(m, x, y, w, bar_h, val_scale)
        -- Record the row rect so a click can target this member. Only members with a
        -- live GameID are recorded; the on_input hit-test lets enb.request_target
        -- decide targetability (a member in another sector resolves to no entity and
        -- is skipped there).
        if m.gameid and m.gameid ~= 0 then
            member_rects[#member_rects + 1] =
                { x = x, y = top, w = w, h = bot - top, gid = m.gameid, name = m.name }
        end
        y = bot + CFG.ROW_GAP
    end
end

-- ===========================================================================
-- DRAW -- only while the HUD is visible AND the player is in a group.
-- ===========================================================================
enb.on_tick(function()
    btn_rects = {}      -- repopulated by draw_party; empty when hidden/ungrouped,
    member_rects = {}   -- so stale rects never eat clicks
    if not H.vis() then return end
    draw_party()
end)

-- ===========================================================================
-- INPUT -- click a group button to fire its action. A click landing on a button
-- is swallowed so it never falls through to the 3D scene behind it. btn_rects is
-- rebuilt every draw, so draw and input stay in lockstep.
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
    if not H.vis() then return false end
    if msg ~= M.LBUTTONDOWN and msg ~= M.LBUTTONUP and msg ~= M.LBUTTONDBLCLK then
        return false
    end
    local mx, my = mouse_xy(lparam)
    for _, r in ipairs(btn_rects) do
        if point_in(mx, my, r.x, r.y, r.w, r.h) then
            if msg == M.LBUTTONDOWN then
                if r.toggle then
                    form_open = not form_open
                elseif r.op and enb.target_action then
                    enb.target_action(r.op, "self")
                elseif r.cta and enb.group_action then
                    enb.group_action(r.cta)
                    -- keep freya-groupbot's reform-after-gate formation in sync when
                    -- the leader picks a type here (4/5/6). nil-guarded so party_frame
                    -- never hard-depends on the groupbot mod being staged.
                    if (r.cta == 4 or r.cta == 5 or r.cta == 6)
                        and type(groupbot) == "table" and groupbot.formation then
                        groupbot.formation(r.cta)
                    end
                end
                flash[r.id] = CFG.FLASH_TICKS
                enb.log("group: " .. r.label)
            end
            return true   -- swallow any button event over a group button
        end
    end
    -- A click on a member row targets that member (buttons above take priority).
    for _, r in ipairs(member_rects) do
        if point_in(mx, my, r.x, r.y, r.w, r.h) then
            if msg == M.LBUTTONDOWN and enb.request_target then
                local ok = enb.request_target(r.gid)
                enb.log("target: " .. (r.name or "member") ..
                        (ok and "" or " (not in range)"))
            end
            return true   -- swallow so the click never falls through to the 3D scene
        end
    end
    return false
end, enb.WANT_MOUSE)

enb.log("party_frame.lua loaded")
