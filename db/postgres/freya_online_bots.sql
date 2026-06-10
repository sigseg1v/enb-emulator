-- freya_online_bots.sql -- the "AhBot" Auction House faucet account + avatar.
--
-- License: MIT (Freya). Companion to freya_online.sql.
--
-- Target database: net7_user.
--
-- The Freya Online AH bots (freya/online/server/bots.go, enabled with
-- FREYA_AH_BOTS=1) post listings under a dedicated seller. A listing needs a
-- seller_avatar_id that resolves to a name via avatar_data, plus the avatar_info
-- / avatar_level_info rows that make the row well-formed. We seed that avatar
-- here, idempotently, so the Go service can reference it by FIXED ids and never
-- has to hand-build a 59-column avatar_data row at runtime.
--
-- Sentinel ids: account 1000000001, avatar 1000000001. These sit FAR above the
-- game server's avatar-id allocation range (it grows from the low double digits)
-- so a real character creation can never collide for the lifetime of the DB.
-- accounts.id is GENERATED ALWAYS AS IDENTITY, so we pin it with OVERRIDING
-- SYSTEM VALUE; pinning a value ~1e9 leaves the identity sequence (which keeps
-- handing out small ids to real signups) undisturbed.
--
-- The AhBot account has a deliberately unusable password_phc -- it is not a
-- valid Argon2id PHC, so verifyPassword can never match it and nobody can log
-- in as AhBot. It exists only to own listings.
--
-- This seed runs unconditionally at boot (schema-init); it does NOT depend on
-- FREYA_AH_BOTS. An AhBot avatar with no listings is inert and invisible (it
-- belongs to a separate account, so it never appears in any player's character
-- select). Only the Go bot, gated on the flag, posts listings.
--
-- Identifiers are double-quoted throughout: avatar_data's cosmetic columns are
-- mixed-case (tattoo_X, hair_H, ...) and were created quoted, so an unquoted
-- reference would fold to lowercase and fail to match.

-- ---- account ----
INSERT INTO "accounts" ("id", "username", "password_phc", "status", "email")
OVERRIDING SYSTEM VALUE
SELECT 1000000001, 'AhBot', '!locked-auction-house-bot-no-login', 0, 'ahbot@freya.invalid'
WHERE NOT EXISTS (SELECT 1 FROM "accounts" WHERE "id" = 1000000001);

-- ---- avatar_data (the 59 NOT NULL / no-default columns) ----
INSERT INTO "avatar_data" (
  "avatar_id", "first_name", "last_name",
  "type", "version", "race", "prof", "gender", "mood", "personality", "nlp",
  "body", "pants", "head", "hair", "ear", "goggle", "beard",
  "weapon_hip", "weapon_unique", "weapon_back", "head_texture", "tattoo_texture",
  "tattoo_X", "tattoo_Y", "tattoo_Z",
  "hair_H", "hair_S", "hair_V",
  "beard_H", "beard_S", "beard_V",
  "eye_H", "eye_S", "eye_V",
  "skin_H", "skin_S", "skin_V",
  "shirt_p_H", "shirt_p_S", "shirt_p_V",
  "shirt_s_H", "shirt_s_S", "shirt_s_V",
  "pants_p_H", "pants_p_S", "pants_p_V",
  "pants_s_H", "pants_s_S", "pants_s_V",
  "shirt_p_metal", "shirt_s_metal", "pants_p_metal", "pants_s_metal",
  "height_weight_0", "height_weight_1", "height_weight_2", "height_weight_3", "height_weight_4"
)
SELECT
  1000000001, 'AhBot', '',
  0, 1, 1, 1, 0, 0, 0, 0,
  0, 0, 0, 0, 0, 0, 0,
  0, 0, 0, 0, 0,
  0, 0, 0,
  0, 0, 0,
  0, 0, 0,
  0, 0, 0,
  0, 0, 0,
  0, 0, 0,
  0, 0, 0,
  0, 0, 0,
  0, 0, 0,
  0, 0, 0, 0,
  0, 0, 0, 0, 0
WHERE NOT EXISTS (SELECT 1 FROM "avatar_data" WHERE "avatar_id" = 1000000001);

-- ---- avatar_info ----
INSERT INTO "avatar_info" (
  "avatar_id", "account_id", "slot", "sector", "galaxy",
  "count", "admin", "combat", "explore", "trade"
)
SELECT 1000000001, 1000000001, 0, 1, 1, 0, 0, 0, 0, 0
WHERE NOT EXISTS (SELECT 1 FROM "avatar_info" WHERE "avatar_id" = 1000000001);

-- ---- avatar_level_info ----
INSERT INTO "avatar_level_info" (
  "avatar_id", "player_rank_name", "hull_upgrade_level", "hull_points",
  "max_hull_points", "credits", "cargo_space", "combat_bar_level",
  "explore_bar_level", "trade_bar_level", "engine_thrust_type", "warp_power_level"
)
SELECT 1000000001, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0
WHERE NOT EXISTS (SELECT 1 FROM "avatar_level_info" WHERE "avatar_id" = 1000000001);
