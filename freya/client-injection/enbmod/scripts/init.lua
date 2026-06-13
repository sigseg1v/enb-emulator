-- SPDX-License-Identifier: MIT
-- Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
-- License: LICENSES/Freya
--
-- init.lua -- loaded by enbmod.dll on startup, hot-reloaded when this file changes.
--
-- The C++ core gives you:
--   enb.log(s)                     write to enbmod.log
--   enb.base                       0x00400000 (image base)
--   enb.addr.<Name>                static code addresses (see enb.addr table / INJECTION-MAP.md)
--   enb.mem.u8/u16/u32/i32/f32/f64/ptr(addr)     guarded reads (never crash the game)
--   enb.mem.str(addr[,cap]) / enb.mem.wstr(...)   C / UTF-16 strings
--   enb.mem.readable(addr[,n]) -> bool
--   enb.mem.chain(base, off1, off2, ...) -> addr  (pointer-chain walk)
--   enb.mem.write_u32 / write_f32(addr,val) -> bool
--   enb.calibrate{ field=value, ... }   set struct offsets at runtime (see below)
--   enb.offsets()                       read back the current offsets table
--   enb.self() -> { base, hull, shield, energy, combat_lvl, x,y,z, ... }  (nil fields = uncalibrated)
--   enb.target() -> { base, name, distance, ... } or nil
--   enb.on_tick(fn)                     called every message-pump iteration (game thread)
--   enb.on_skill(fn) / enb.on_chat(fn)  raw event hooks (require enable_event_hooks())
--   enb.enable_event_hooks()            installs the game-function hooks (off by default)
--   enb.screen() -> w, h                real backbuffer size (0,0 until first frame)
--   enb.measure(s) -> w, h              text pixel size in the overlay font (0,0 pre-atlas)
--   enb.on_input(fn[, mask])            fn(msg, wparam, lparam) -> truthy SWALLOWS the msg
--                                       (Tier A input kill); fail-open on Lua error.
--                                       mask = enb.WANT_KEY|WANT_CHAR|WANT_MOUSE (default all);
--                                       enb.msg.* holds the raw WM_* ids for classification.

enb.log("init.lua running")

-- ---------------------------------------------------------------------------
-- CALIBRATION (you MUST fill these in at runtime -- the defaults are unknown/-1).
-- Workflow:
--   1. Run the game with a debugger or memory scanner, find the address that HOLDS the pointer
--      to your ship object, and the field offsets within it (hull, shield, ...).
--   2. Put them here. Hot-reload picks them up without restarting the game.
-- Until calibrated, enb.self() returns a table with base=0 and no stat fields.
-- ---------------------------------------------------------------------------
-- enb.calibrate{
--   player_ptr_addr = 0x00B6xxxx,   -- address of the player-object pointer (.data)
--   hull = 0x??, hull_max = 0x??,
--   shield = 0x??, shield_max = 0x??,
--   energy = 0x??, energy_max = 0x??,
--   combat_lvl = 0x??, trade_lvl = 0x??, explore_lvl = 0x??,
--   pos_x = 0x??, pos_y = 0x??, pos_z = 0x??,
--   target_ptr_addr = 0x00B6xxxx,
--   tgt_name = 0x??, tgt_name_is_ptr = 1, tgt_name_wide = 0,
--   tgt_pos_x = 0x??, tgt_pos_y = 0x??, tgt_pos_z = 0x??,
-- }
--
-- The EASY path: run autocalib in-game (see autocalib.lua), then it writes
-- scripts/calib_data.lua (gitignored, install-specific). We load it here if it
-- exists, so calibration survives restarts and hot-reloads without editing init.
do
    local ok, data = pcall(require, "calib_data")
    if ok and type(data) == "table" then
        enb.calibrate(data)
        enb.log("init.lua: applied persisted calib_data.lua")
    end
end

-- ---------------------------------------------------------------------------
-- Example: lightweight throttled status line every ~2s once calibrated.
-- ---------------------------------------------------------------------------
local n = 0
enb.on_tick(function()
    n = n + 1
    if n % 240 ~= 0 then return end   -- throttle (PeekMessageA fires very often)
    local me = enb.self()
    if me.base == 0 then
        enb.log("self() not calibrated yet")
        return
    end
    enb.log(string.format("hull=%s shield=%s energy=%s combat=%s",
        tostring(me.hull), tostring(me.shield), tostring(me.energy), tostring(me.combat_lvl)))
    local t = enb.target()
    if t then
        enb.log(string.format("target=%s dist=%s", tostring(t.name), tostring(t.distance)))
    end
end)

-- Event hooks are opt-in because they patch game code; enable once you trust the build.
-- enb.enable_event_hooks()
-- enb.on_skill(function(this, arg) enb.log(("skill this=%08x arg=%08x"):format(this, arg)) end)
-- enb.on_chat (function(this, arg) enb.log(("chat  this=%08x arg=%08x"):format(this, arg)) end)

-- ---------------------------------------------------------------------------
-- OVERLAY (D3D8 Present hook). Drawing API, rebuilt every tick:
--   enb.draw.text(x, y, str [, 0xRRGGBB])
--   enb.draw.rect(x, y, w, h [, 0xRRGGBB [, filled [, alpha]]])
--   enb.draw.line(x0, y0, x1, y1 [, 0xRRGGBB [, alpha]])
--   enb.draw.rect_grad(x, y, w, h, rgbTop, rgbBottom [, alpha])     -- vertical gradient
--   enb.draw.rrect(x, y, w, h, radius [, rgb [, alpha [, filled]]]) -- rounded rect
--   enb.draw.rrect_grad(x, y, w, h, radius, rgbTop, rgbBottom [, alpha])
--   enb.draw.image(path, x, y [, w, h [, alpha0_255]])   -- PNG/TGA/BMP/JPG via stb_image
-- ---------------------------------------------------------------------------
-- ---------------------------------------------------------------------------
-- ACTIONS -- triggering things.
--
-- (a) Input synthesis (works today): post the key bound to an ability slot.
--   enb.tap(vk)             tap a virtual-key (down+up) to the game window
--   enb.key(vk[, down])     hold/release; no 2nd arg = tap
--   enb.char(codepoint)     WM_CHAR (e.g. typing into chat)
--   enb.hwnd()              the game window handle (0 if not found)
-- Example: map ability slots 1..5 to the number keys (VK '1'=0x31 ...).
local ABILITY_KEYS = { [1]=0x31, [2]=0x32, [3]=0x33, [4]=0x34, [5]=0x35 }
function use_ability(slot)
    local vk = ABILITY_KEYS[slot]
    if vk then return enb.tap(vk) end
end
-- use_ability(1)

-- (b) Direct member-function call (mechanism; YOU supply the verified this/args at runtime):
--   enb.call(func_addr, this_ptr, arg1, arg2, ...)  -> EAX   (__thiscall)
--   enb.call_cdecl(func_addr, arg1, ...)            -> EAX
-- e.g. once you've reversed the skill-activation signature at enb.addr.SkillLifecycle:
--   enb.call(enb.addr.SkillLifecycle, skill_this, slot_or_id)
-- WRONG args can corrupt game state -- test on a throwaway character. The asm caller is verified
-- correct (this in ECX, args ordered, ESP balanced); the *arguments* are your responsibility.

-- ---------------------------------------------------------------------------
-- FREYA HUD (Phase AS): the Earth & Beyond cockpit-glass HUD. Three pieces, all
-- visibility-gated on enb.state() (space + station only; hotbar hidden in
-- station). See freya_hud.lua (shared toolkit), xp_overlay.lua (discipline
-- card, bottom-left) and freya_ui.lua (player card + action hotbar).
-- ---------------------------------------------------------------------------
require("xp_overlay")
require("freya_ui")

-- ---------------------------------------------------------------------------
-- HIDE THE NATIVE WIDGETS WE REPLACE (opt-in; OFF by default).
--
-- The glass cards are translucent, so the native in-space stat bars / xp bars /
-- skill buttons show THROUGH them unless we suppress the routine that draws each.
-- enb.patch_ret(addr [, pop]) overwrites a function entry with a `ret` so it
-- returns immediately (pop = callee stack-cleanup bytes: 0 for caller-clean, the
-- exact arg-byte count for stdcall/thiscall -- a wrong pop corrupts the stack).
--
-- The RIGHT entry to patch is the widget's per-frame PAINT routine -- a small
-- function that, for each child bar, checks the gadget's visible flag and calls
-- the gadget paint primitive, then returns void. It touches no game state, so an
-- early `ret` hides the widget cleanly. The CONSTRUCTOR / value-updater entries
-- are NOT safe: they build or mutate the gadget objects, and ret-patching them
-- leaves uninitialised pointers / frozen state and crashes. The address book
-- distinguishes them:
--
--   enb.addr.VitalsPaint -- vitals PAINT (hull/shield/reactor). __fastcall(ECX),
--                           no stack args -> pop 0. SAFE. Player-card replaces it.
--   enb.addr.XpPaint     -- xp PAINT (combat/trade/explore). __fastcall(ECX),
--                           no stack args -> pop 0. SAFE. Disc card replaces it.
--   (enb.addr.VitalsBars / EnergyBar / XpBars / SkillButton are the ctor/updater
--    entries -- do NOT patch_ret those.)
--
-- The skill buttons have NO standalone pure-paint entry (render is fused with
-- state mutation), so there is no clean ret-patch for them; hiding them needs a
-- runtime per-gadget visible-flag write, deferred to CV-AS-HIDE-SKILL.
--
-- Left OFF until verified against the real client (that an early `ret` hides the
-- widget without side effects is only knowable there). Tracked in
-- plans/29-client-verification.md. To enable after the owner confirms, uncomment:
--
-- enb.patch_ret(enb.addr.VitalsPaint)       -- CV-AS-HIDE-VITALS
-- enb.patch_ret(enb.addr.XpPaint)           -- CV-AS-HIDE-XP
-- ---------------------------------------------------------------------------
