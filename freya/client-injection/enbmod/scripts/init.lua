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
-- OVERLAY (DirectDraw Flip hook). Drawing API, rebuilt every tick:
--   enb.draw.text(x, y, str [, 0xRRGGBB])
--   enb.draw.rect(x, y, w, h [, 0xRRGGBB [, filled_bool]])
--   enb.draw.line(x0, y0, x1, y1 [, 0xRRGGBB])
--   enb.draw.image(path, x, y [, w, h [, alpha0_255]])   -- PNG/TGA/BMP/JPG via stb_image
-- NOTE: unverified in-game (Flip vs Blt / Wine ddraw may need tuning) -- see README.
-- ---------------------------------------------------------------------------
enb.on_tick(function()
    -- a simple always-on HUD box + label
    enb.draw.rect(20, 20, 220, 70, 0x00FF88, false)
    enb.draw.text(28, 26, "enbmod overlay", 0x00FF88)
    local me = enb.self()
    if me.base ~= 0 then
        enb.draw.text(28, 46, ("hull %s  shield %s"):format(tostring(me.hull), tostring(me.shield)), 0xFFFFFF)
        enb.draw.text(28, 64, ("energy %s"):format(tostring(me.energy)), 0xFFFFFF)
    else
        enb.draw.text(28, 46, "self() not calibrated", 0xFF5555)
    end
    -- enb.draw.image([[C:\\path\\icon.png]], 250, 20, 32, 32, 255)
end)

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
-- XP bars HUD: draws "<level> <pct>%" next to the Combat/Exploration/Trade bars
-- in the bottom-left. Shows "L? ?%" until the level/pct offsets are calibrated.
-- ---------------------------------------------------------------------------
require("xp_overlay")
