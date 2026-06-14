-- SPDX-License-Identifier: MIT
-- Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
-- License: LICENSES/Freya
--
-- init.lua -- entrypoint for the Freya HUD mod. Pulls in its two draw
-- components, in draw order (registration order = paint order):
--   xp_overlay  -- bottom-left discipline card (combat/trade/explore + xp), under
--   freya_ui    -- player card (hull/shield/energy) + 12-slot hotbar, on top
--
-- Suppressing the stock in-space widgets that would show through the glass is the
-- job of the separate `hide-ui` mod, declared as a dependency in mod.json. The
-- launcher flags freya-hud red if hide-ui is not enabled; we do not require it
-- here (a missing dep must not abort the HUD -- it just renders over the natives).
require("xp_overlay")
require("freya_ui")
