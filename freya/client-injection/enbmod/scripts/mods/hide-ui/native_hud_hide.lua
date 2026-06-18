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
--
-- DEFER UNTIL IN SPACE. The widgets these routines paint only exist in space, so
-- patching at mod-load (which happens at the front-end / login screen, where the
-- bars are not even visible) serves no purpose there. We gate the patch behind
-- the in-space heartbeat and apply it exactly once, the first frame we see space.
-- patch_ret has no restore, so this is one-way for the session; a reload re-runs
-- this file and (if already in space) re-applies the same byte, which is
-- idempotent.

local applied = false
enb.on_tick(function()
    if not (enb.inspace and enb.inspace()) then return end

    -- One-shot: the vitals/xp PAINT routines are pure, so an early ret hides them
    -- for good (CV-AS-HIDE-VITALS / -XP). Done once the first frame we see space.
    if not applied then
        enb.patch_ret(enb.addr.VitalsPaint)
        enb.patch_ret(enb.addr.XpPaint)
        applied = true
        enb.log("hide-ui: in space -> patched native vitals/xp paint")
    end

    -- Per-tick: the bottom-center cockpit widgets (throttle/up-down/warp cluster
    -- and the "UI COMMANDS" action buttons) have NO pure-paint entry to ret-patch,
    -- and the game re-shows the throttle gadgets whenever throttle/warp state
    -- changes -- so we re-clear their engine visible flag every frame. The Freya
    -- overlay draws the replacement controls. enb.hide_cockpit is fully guarded and
    -- runs no game code; it returns 0 until the cockpit controllers are captured
    -- (their constructors run on entering space). CV-AS-HIDE-COCKPIT.
    if enb.hide_cockpit then enb.hide_cockpit() end
end)

-- The skill buttons have NO standalone pure-paint entry (render is fused with
-- state mutation), so there is no clean ret-patch for them; hiding them needs a
-- runtime per-gadget visible-flag write, still deferred (CV-AS-HIDE-SKILL).
