-- SPDX-License-Identifier: MIT
-- Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
-- License: LICENSES/Freya
--
-- freya_hud.lua -- shared HUD toolkit for the Freya cockpit-glass overlay.
--
-- require("freya_hud") from the HUD scripts (freya_ui, xp_overlay). Holds the
-- single source of truth for:
--   * the palette (vital/discipline colors, panel + ink colors),
--   * visibility gating off enb.state() (in-space-only, station hides hotbar),
--   * the glass-panel primitive (gradient body + gloss sheen + hairline border
--     + cyan corner ticks), and
--   * outlined text (the overlay font is a single flat RGB with no shadow, so a
--     legible-over-bright-space label is faked by drawing the glyphs 4x in a
--     dark ink offset by 1px, then once in the fill color on top).
--
-- The look ports the "Earth & Beyond HUD" design (cockpit glass: thin glowing
-- cyan hairlines, glossy translucent panels, vertical-gradient bars). Fidelity
-- gaps vs the HTML/CSS prototype, by overlay limitation: ONE fixed ~14px font
-- (no Michroma/IBM-Plex, no per-element sizing), no real backdrop blur, and
-- single-stop shape alpha. Geometry/colors track the design's final tweaks.

local H = {}

-- ---- palette (design HUD_DEFAULTS) -----------------------------------------
H.HULL    = 0xe8453c   -- red
H.SHIELD  = 0x3d8bff   -- blue
H.REACTOR = 0x37d27a   -- green
H.COMBAT  = 0x2cc4b0   -- teal
H.EXPLORE = 0x3d8bff   -- blue
H.TRADE   = 0x9b6cf0   -- purple

H.INK      = 0xdfe9f4   -- bright label
H.INK_DIM  = 0x9fb4cc   -- secondary label (LV badges)
H.UNCAL    = 0x6a7686   -- uncalibrated placeholder
H.TRACK    = 0x070b12   -- empty bar track
H.SHADOW   = 0x05080c   -- outlined-text dark ink

H.PANEL_TOP   = 0x1a2638
H.PANEL_BOT   = 0x080d16
H.PANEL_ALPHA = 140      -- glass body (design panelOpacity 0.44 reads ~112; bumped
                         -- for legibility since we have no backdrop blur)
H.LINE     = 0x5f86b0    -- hairline border
H.LINE_HOT = 0x78e1ff    -- glowing cyan corner ticks
H.RADIUS   = 2           -- design corner radius

-- ---- visibility off the game state -----------------------------------------
-- enb.state() (C++) returns one of: "space","station","login","charsel",
-- "load","none","unknown". Two sources feed it:
--   * the calibrated game-state code (game_state_addr) -- the full state set,
--     but currently UNCALIBRATED (game.h, game_state_addr == 0), so it names
--     nothing yet;
--   * the in-space heartbeat (init.lua calls enb.enable_inspace()) -- a
--     zero-calibration signal that positively reports "space" while the
--     per-frame vitals updater is firing. It can ONLY say space-vs-not-space,
--     so it never names station/login/charsel.
-- Net effect today: "space" when in space, "unknown" everywhere else. So
-- "unknown" CANNOT mean "hide" -- that would hide the HUD on the front-end AND
-- in station. "unknown" is the pre-calibration default and SHOWS the HUD. Only
-- the screens we can POSITIVELY identify as non-gameplay (login/charsel/load/
-- none) hide it -- and those are only ever reported once state is calibrated.
function H.state()
    return (enb.state and enb.state()) or "unknown"
end

-- returns: show_hud, show_hotbar
--   space            -> full HUD (cards + hotbar)
--   station          -> cards, NO hotbar (owner ask: hide skill buttons in
--                       station). Only reported once game_state_addr is
--                       calibrated; today station reads as "unknown".
--   anything else    -> nothing. The in-space heartbeat gives a RELIABLE
--   (incl. unknown)     positive "space" signal, so "not space" means hide --
--                       including "unknown" (= heartbeat not firing = we are not
--                       in space: front-end, char select, load, or docked).
-- This is the deliberate flip from the old "unknown -> show" default: that
-- default existed only because enb.state() could never positively detect space.
-- Now that it can, showing the HUD outside space is wrong.
function H.vis()
    local s = H.state()
    if s == "space" then
        return true, true
    elseif s == "station" then
        return true, false
    end
    -- unknown / login / charsel / load / none: not in space -> hide.
    return false, false
end

-- ---- live character stats (level / xp / discipline levels) -----------------
-- Read straight off the same controller->data chain enb.vitals() walks for the
-- bar fractions, so this needs NO flat-struct calibration and NO DLL rebuild --
-- it works the instant we are in space, exactly like the name + bars do.
--
-- Offsets pinned from the in-space level/xp probe (autocalibrate) against a fresh
-- character. CAVEAT, read before trusting these:
--   * level     (dat+0x5c) read 3 on a 1/1/1 character == combat+explore+trade,
--                i.e. the OVERALL level (sum of the three), not max.
--   * xp_cur/xp_next (dat+0x0c / dat+0x10) read 4/10 == the current-level XP
--     progress bar. Which discipline it tracks is not yet proven, so it feeds the
--     overall card, not a per-discipline bar.
--   * combat/explore/trade (dat+0x104 / +0x108 / +0x10c) are three consecutive
--     ints that all read 1 on a fresh char. The letter->offset MAPPING is a
--     HYPOTHESIS: while the disciplines are equal it cannot be proven, so if one
--     levels and the WRONG letter moves, swap these three offsets.
-- Every read is guarded (readable + sane range) so a bad pointer never faults;
-- unknown fields come back nil and the cards fall back to the "--" skeleton.
function H.stats()
    local out = {}
    local m = enb.mem
    local ctrl = (enb.vitals_ctrl and enb.vitals_ctrl()) or 0
    if ctrl == 0 or not (m and m.readable(ctrl + 4, 4)) then return out end
    local data = m.u32(ctrl + 4)
    if data == 0 or not m.readable(data + 0x114, 4) then return out end

    local function int_at(off, lo, hi)
        local v = m.u32(data + off)
        if v >= lo and v <= hi then return v end
    end

    out.level = int_at(0x5c, 1, 300)             -- overall level (sum of disciplines)
    local cur, need = m.u32(data + 0x0c), m.u32(data + 0x10)
    if need > 0 and cur <= need then             -- current-level xp progress
        out.xp_cur, out.xp_next = cur, need
        out.xp_pct = need > 0 and (cur / need * 100) or nil
    end
    out.combat  = int_at(0x104, 1, 60)           -- mapping unproven on a 1/1/1 char
    out.explore = int_at(0x108, 1, 60)
    out.trade   = int_at(0x10c, 1, 60)
    return out
end

-- ---- text helpers ----------------------------------------------------------
function H.measure(s)
    local w, h = enb.measure(s)
    if w == 0 then w = #tostring(s) * 7; h = 14 end
    return w, h
end

-- outlined text: 4 dark offset copies + 1 fill copy on top. The fill copy is
-- drawn LAST and is the only one carrying `rgb` (the offsets are H.SHADOW), so
-- specs can match the single colored copy by (text, rgb). Optional `scale`
-- (default 1.0) shrinks/grows the glyphs uniformly.
function H.otext(x, y, s, rgb, scale)
    x = math.floor(x); y = math.floor(y)
    enb.draw.text(x - 1, y,     s, H.SHADOW, scale)
    enb.draw.text(x + 1, y,     s, H.SHADOW, scale)
    enb.draw.text(x,     y - 1, s, H.SHADOW, scale)
    enb.draw.text(x,     y + 1, s, H.SHADOW, scale)
    enb.draw.text(x,     y,     s, rgb, scale)
end

-- darker shade of an 0xRRGGBB color, for the bottom of a vertical-gradient fill
function H.darker(rgb)
    return (rgb >> 1) & 0x7f7f7f
end

-- ---- glass panel -----------------------------------------------------------
-- cyan corner ticks at the top-left and bottom-right, like the design's
-- `.corner` accents.
local function ticks(x, y, w, h)
    local n, a, c = 7, 170, H.LINE_HOT
    enb.draw.line(x, y, x + n, y, c, a)
    enb.draw.line(x, y, x, y + n, c, a)
    enb.draw.line(x + w - 1 - n, y + h - 1, x + w - 1, y + h - 1, c, a)
    enb.draw.line(x + w - 1, y + h - 1 - n, x + w - 1, y + h - 1, c, a)
end

function H.glass(x, y, w, h, alpha)
    alpha = alpha or H.PANEL_ALPHA
    -- body: vertical gradient, dark blue glass
    enb.draw.rrect_grad(x, y, w, h, H.RADIUS, H.PANEL_TOP, H.PANEL_BOT, alpha)
    -- gloss sheen across the top ~46% (design `.gloss-top`)
    local gh = math.floor(h * 0.46)
    if gh > 2 then
        enb.draw.rrect_grad(x, y, w, gh, H.RADIUS, 0xffffff, H.PANEL_TOP, 26)
    end
    -- hairline border
    enb.draw.rrect(x, y, w, h, H.RADIUS, H.LINE, 90, false)
    ticks(x, y, w, h)
end

return H
