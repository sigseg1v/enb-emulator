-- SPDX-License-Identifier: MIT
-- Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
-- License: LICENSES/Freya
--
-- init.lua -- entrypoint for the Freya HUD mod. Pulls in its draw components, in
-- draw + input order (registration order = paint order AND on_input order):
--   chat        -- bottom-left chat window + input box. FIRST so its on_input
--                  handler gets first crack at every key while the input box is
--                  open (multi-handler on_input: first truthy swallows).
--   xp_overlay  -- discipline card (combat/trade/explore + xp), raised ABOVE chat
--   freya_ui    -- player card (hull/shield/energy) + 12-slot hotbar, on top
--   micromenu   -- the four top-left micro-menu buttons (Inventory / Character /
--                  Map / Options), restoring the hidden bottom-left chrome band
--   ui_toggle   -- Ctrl+U master switch: flips the whole Freya overlay off (and
--                  the native HUD back on) and on again
--
-- Suppressing the stock in-space widgets that would show through the glass is the
-- job of the separate `hide-ui` mod, declared as a dependency in mod.json. The
-- launcher flags freya-hud red if hide-ui is not enabled; we do not require it
-- here (a missing dep must not abort the HUD -- it just renders over the natives).
require("chat")
require("xp_overlay")
require("freya_ui")
require("target_frame")
require("micromenu")
require("ui_toggle")
