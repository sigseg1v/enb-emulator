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

-- ---- live character stats (vitals numbers / discipline levels) -------------
-- None of these live at flat struct offsets -- the client keeps them in
-- string-keyed property bags, read through its own getters (NO flat-struct
-- calibration needed). They split across TWO objects:
--   * numeric vitals (hull/shield/reactor cur+max) are on the ship entity, read
--     via enb.aux(key) (float). nil out of space (controller == 0).
--   * discipline levels are on the RPG manager, read via enb.rpg_level(key)
--     (int). nil until the client's RPG level-reader has run at least once.
-- Each is range-guarded so a stale read never shows junk.
--
-- Hull is stored absolute (HullPoints / MaxHullPoints). Shield + reactor store
-- only the max (MaxShieldPower / MaxEnergyPower); their current is max * the live
-- gadget fill fraction (enb.vitals()), so the card computes cur from frac * max.
--
-- Overall level is the SUM of the three discipline levels (the EnB definition),
-- so out.level is derived from the three RPGInfo level reads -- no separate key.
-- Still NOT resolved here: per-discipline xp progress is read separately by
-- xp_overlay (the XpBars controller), not via these AuxData keys.
--
-- Discipline levels come from enb.rpg_level(key): the C++ wrapper interns the key
-- on the RPG manager's RPGInfo AuxData container and reads the int level off the
-- matching entry. The key string MUST be the client's own dotted form
-- ("RPGInfo.CombatLevel", with a DOT) -- the space-form "RPGInfo CombatLevel"
-- never matches an entry and reads nothing. Returns 0 (not nil) on a fresh
-- character: a real, valid level of 0. nil only until the client's level-reader
-- has run once (rpg_mgr == 0) or on a genuine miss.
H.RPG_KEY = {
    combat  = "RPGInfo.CombatLevel",
    trade   = "RPGInfo.TradeLevel",
    explore = "RPGInfo.ExploreLevel",
}
function H.discipline_level(which)
    local key = H.RPG_KEY[which]
    if not (key and enb.rpg_level) then return nil end
    return enb.rpg_level(key)
end

function H.stats()
    local out = {}
    if not enb.aux then return out end

    local function num(key, lo, hi)
        local v = enb.aux(key)
        if v and v >= lo and v <= hi then return v end
    end

    out.hull       = num("HullPoints",     0, 1e6)
    out.hull_max   = num("MaxHullPoints",  0, 1e6)
    out.shield_max = num("MaxShieldPower", 0, 1e6)
    out.energy_max = num("MaxEnergyPower", 0, 1e6)
    out.combat  = H.discipline_level("combat")
    out.explore = H.discipline_level("explore")
    out.trade   = H.discipline_level("trade")
    -- overall level = sum of the three disciplines (EnB definition). Only when all
    -- three resolved, so a partial read never shows a too-low total.
    if out.combat and out.explore and out.trade then
        out.level = out.combat + out.explore + out.trade
    end
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

-- ---- chat window geometry + capture flag (shared) --------------------------
-- The Freya chat box occupies the very bottom-left corner. chat.lua owns its
-- render/input; xp_overlay anchors its discipline card ABOVE the box via
-- H.chat_top(). The geometry lives here so both modules agree without a require
-- cycle. The input line is drawn ABOVE the box body (an extra row) only while
-- capturing, so it does not steal a body row.
H.CHAT = {
    X         = 14,   -- left edge (matches the discipline card)
    BOTTOM    = 14,   -- gap from screen bottom
    W         = 360,  -- box width
    H         = 168,  -- box body height (no input row)
    GAP_ABOVE = 8,    -- gap between the box (or its input row) and the card above
    INPUT_H   = 20,   -- the input line height, shown above the box while capturing
}

-- returns x, y, w, h of the chat box BODY (the scrollback area).
function H.chat_rect()
    local sw, sh = enb.screen()
    if not sh or sh == 0 then sw, sh = 1280, 960 end
    local c = H.CHAT
    return c.X, sh - c.BOTTOM - c.H, c.W, c.H
end

-- the y a sibling card should sit ABOVE (its BOTTOM edge). Leaves room for the
-- input row so the discipline card never overlaps the input line when it opens.
function H.chat_top(gap)
    local _, y = H.chat_rect()
    return y - H.CHAT.INPUT_H - (gap or H.CHAT.GAP_ABOVE)
end

-- shared "chat input box is capturing keyboard" flag. chat.lua sets it; freya_ui
-- reads it to defer ALL keyboard handling while the input box is open (so action
-- bar / other UI keys are never captured mid-typing). Order-independent: even if
-- freya_ui's on_input ran before chat's, this flag makes it yield.
H.chat_capturing = false

return H
