-- SPDX-License-Identifier: MIT
-- Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
-- License: LICENSES/Freya
--
-- native_hud_hide.lua -- hide the stock in-space stat/XP widgets that the Freya
-- glass cards replace. Loaded as a mod entrypoint by the mod loader; disabling
-- the "hide-ui" mod in the launcher removes this file from the staged
-- scripts, so nothing here runs and the original bars come back.
--
-- The glass cards are translucent, so the native in-space stat bars / xp bars
-- show THROUGH them unless we suppress the routine that draws each.
-- enb.patch_ret(addr [, pop]) overwrites a function entry with a `ret` so it
-- returns immediately (pop = callee stack-cleanup bytes: 0 for caller-clean, the
-- exact arg-byte count for stdcall/thiscall -- a wrong pop corrupts the stack).
--
-- We patch each widget's per-frame PAINT routine -- a small function that, for
-- each child bar, checks the gadget's visible flag and calls the gadget paint
-- primitive, then returns void. It touches no game state, so an early `ret`
-- hides the widget cleanly. The CONSTRUCTOR / value-updater entries are NOT safe
-- (they build or mutate the gadget objects; ret-patching them leaves
-- uninitialised pointers / frozen state and crashes), so we never patch those:
--
--   enb.addr.VitalsPaint -- vitals PAINT (hull/shield/reactor). __fastcall(ECX),
--                           no stack args -> pop 0. Player-card replaces it.
--   enb.addr.XpPaint     -- xp PAINT (combat/trade/explore). __fastcall(ECX),
--                           no stack args -> pop 0. Disc card replaces it.
--   (enb.addr.VitalsBars / EnergyBar / XpBars / SkillButton are the ctor/updater
--    entries -- do NOT patch_ret those.)
--
-- CAVEAT: the paint addresses are pinned by behavioural analysis, but "an early
-- ret hides the widget without side effects" is only fully provable on the real
-- client. If the client crashes on load, disable this mod (CV-AS-HIDE-VITALS /
-- -XP in plans/29-client-verification.md track the confirmation).

enb.patch_ret(enb.addr.VitalsPaint)       -- CV-AS-HIDE-VITALS
enb.patch_ret(enb.addr.XpPaint)           -- CV-AS-HIDE-XP

-- The skill buttons have NO standalone pure-paint entry (render is fused with
-- state mutation), so there is no clean ret-patch for them; hiding them needs a
-- runtime per-gadget visible-flag write, still deferred (CV-AS-HIDE-SKILL).
