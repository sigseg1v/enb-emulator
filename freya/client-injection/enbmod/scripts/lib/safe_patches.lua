-- SPDX-License-Identifier: MIT
-- Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
-- License: LICENSES/Freya
--
-- safe_patches.lua -- always-on client correctness patches applied at startup by
-- init.lua (NOT an opt-in mod). These are not cosmetic; each one prevents the
-- stock client from faulting on a code path that has no valid data on a private
-- preservation server. Keep this list tiny and every entry justified.
--
-- apply() is idempotent: patch_ret overwrites a function entry with a `ret`, so
-- re-running it (e.g. after reload()) re-writes the same byte.

local M = {}

-- ---------------------------------------------------------------------------
-- account-notice / subscription-expiry dialog -- LOGIN-SCREEN CRASH FIX
--
-- FUN_005aea60 is the retail "account notice" dialog the client pops during
-- LoginTask (right after auth, before character select). It reads a block of
-- account-status fields the retail billing server used to populate -- subscription
-- strings at account+0x1098.. and an expiry Unix timestamp at account+0x1130 --
-- and feeds that timestamp to the date formatter at 0x00a2a68f.
--
-- Our server never sends that account-status packet (it has no subscription/
-- billing concept), so the block is left as whatever heap bytes the account
-- object was allocated over. When the garbage byte at account+0x1d happens to be
-- nonzero the dialog fires, and when the garbage at account+0x1130 happens to be
-- a NEGATIVE int32 the date formatter returns NULL (it bails on negative input)
-- -- after which the dialog does `rep movs` of 9 dwords FROM that null pointer
-- and the client access-violates at 0x005aebce. It is intermittent precisely
-- because it depends on the heap contents at allocation.
--
-- There is no correct data for this dialog on a server with no subscriptions, so
-- the faithful behaviour is "no account notice". We ret-patch the dialog entry so
-- it returns immediately and never reads the uninitialised block. The caller
-- already sets its shown-once guard (login+0xda) before the call and clears the
-- account+0x1d flag afterwards, so skipping the body is clean. __thiscall(ECX) =>
-- no stack args => pop 0. (CV-AS-NOTICE in plans/29-client-verification.md.)
--
-- The proper long-term fix is server-side: have the global-login flow initialise
-- this account-status block (notice flag 0, valid/empty expiry) so the retail
-- code path sees sane data instead of heap garbage. Tracked separately; until
-- then this keeps the client off the login screen crash.
local NOTICE_DIALOG = 0x005aea60

function M.apply()
    local ok = enb.patch_ret(NOTICE_DIALOG)   -- CV-AS-NOTICE
    enb.log("safe_patches: account-notice dialog ret-patched -> " .. tostring(ok))
end

return M
