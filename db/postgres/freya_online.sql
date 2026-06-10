-- freya_online.sql -- Auction House + Mailbox tables for "Freya Online".
--
-- License: MIT (Freya). This is new self-contained schema for the Freya
-- Online web service (freya/online), not inherited Net-7 content.
--
-- Target database: net7_user (the save-state DB -- it already holds
-- accounts, avatar_*, and server_status). Item *templates* live in the
-- OTHER database (net7.item_base); Postgres cannot FK across databases,
-- so item_id below is an UNCONSTRAINED reference into net7.item_base and
-- is resolved by the Go service which holds a pool to each DB. Do NOT add
-- a FK on item_id -- it will fail (net7.item_base is not visible here).
--
-- Applied unconditionally at boot by the schema-init service. Every
-- statement is idempotent (IF NOT EXISTS / ADD COLUMN IF NOT EXISTS) so a
-- re-apply on an existing volume is a near-instant no-op. See
-- docs/06-database-schema.md and the schema-init block in docker-compose.yml.

-- ---------------------------------------------------------------------------
-- Soft-delete for characters.
--
-- "If a player is deleted, their items still list on the AH; the row is
-- marked deleted_at, filtered out of character select, and no longer counts
-- against character slots." A character is avatar_info (account_id + slot);
-- deletion is a soft mark here, NOT a row delete, so AH listings that point
-- at seller_avatar_id keep resolving a name.
-- ---------------------------------------------------------------------------
ALTER TABLE "avatar_info"
  ADD COLUMN IF NOT EXISTS "deleted_at" timestamptz DEFAULT NULL;

-- Character-select / slot-count queries filter on this; partial index keeps
-- the live set cheap to scan per account.
CREATE INDEX IF NOT EXISTS "idx_avatar_info_account_live"
  ON "avatar_info" ("account_id")
  WHERE "deleted_at" IS NULL;

-- ---------------------------------------------------------------------------
-- Auction listings.
--
-- status: 0=active, 1=sold (buyout or won at expiry), 2=expired-unsold
--         (95% deposit returned, item returned to mailbox), 3=cancelled.
-- The item instance fields (quality/item_cost/builder_name/structure) are a
-- SNAPSHOT taken off the avatar_vault_items / avatar_inventory_items row at
-- listing time -- the instance is removed from the owner's storage when
-- listed, so this row IS the authoritative copy until it is delivered.
-- ---------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS "auction_listings" (
  "id"                     bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  "seller_avatar_id"       bigint  NOT NULL,
  "seller_account_id"      bigint  NOT NULL,
  "item_id"                integer NOT NULL,              -- -> net7.item_base.id (cross-DB, no FK)
  "stack_size"             integer NOT NULL DEFAULT 1 CHECK ("stack_size" >= 1),
  "quality"               real    DEFAULT NULL,           -- NULL => no variable quality => always common
  "item_cost"              bigint  DEFAULT NULL,          -- builder cost snapshot
  "builder_name"           text    DEFAULT NULL,
  "structure"              real    DEFAULT NULL,
  "item_value"             bigint  NOT NULL,              -- vendor-value snapshot: fee base + min-bid-step base
  "start_bid"              bigint  NOT NULL,
  "current_bid"            bigint  NOT NULL,              -- = start_bid until first bid lands
  "buyout"                 bigint  DEFAULT NULL,          -- optional
  "high_bidder_avatar_id"  bigint  DEFAULT NULL,
  "high_bidder_account_id" bigint  DEFAULT NULL,
  "deposit_paid"           bigint  NOT NULL DEFAULT 0,    -- 10% of item_value collected up front
  "status"                 smallint NOT NULL DEFAULT 0 CHECK ("status" BETWEEN 0 AND 3),
  "created_at"             timestamptz NOT NULL DEFAULT now(),
  "expires_at"             timestamptz NOT NULL,
  "resolved_at"            timestamptz DEFAULT NULL       -- set when status leaves 0
);

-- The hot path: "show me active listings, soonest-expiring first" and the
-- sweeper's "find active listings whose expires_at has passed".
CREATE INDEX IF NOT EXISTS "idx_auction_active_expiry"
  ON "auction_listings" ("expires_at")
  WHERE "status" = 0;
CREATE INDEX IF NOT EXISTS "idx_auction_seller"
  ON "auction_listings" ("seller_avatar_id");
CREATE INDEX IF NOT EXISTS "idx_auction_item"
  ON "auction_listings" ("item_id");
CREATE INDEX IF NOT EXISTS "idx_auction_high_bidder"
  ON "auction_listings" ("high_bidder_avatar_id")
  WHERE "high_bidder_avatar_id" IS NOT NULL;

-- ---------------------------------------------------------------------------
-- Auction bids -- append-only history. The previous high bidder is refunded
-- (via mailbox credits) when outbid; the winner is charged the held amount.
-- ---------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS "auction_bids" (
  "id"               bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  "listing_id"       bigint  NOT NULL REFERENCES "auction_listings" ("id"),
  "bidder_avatar_id" bigint  NOT NULL,
  "bidder_account_id" bigint NOT NULL,
  "amount"           bigint  NOT NULL,
  "created_at"       timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS "idx_bids_listing"
  ON "auction_bids" ("listing_id", "created_at" DESC);

-- ---------------------------------------------------------------------------
-- Mailbox -- a shared screen PER ACCOUNT (keyed by account_id). Lists
-- subject, recipient (the character it was addressed to), sender, has-item.
-- ---------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS "mailbox_messages" (
  "id"                  bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  "account_id"          bigint  NOT NULL,
  "recipient_avatar_id" bigint  DEFAULT NULL,   -- which character it was sent to (loot target + display)
  "recipient_name"      text    DEFAULT NULL,    -- denormalized for display when char is gone
  "sender_name"         text    NOT NULL,        -- "Auction House" or an avatar name
  "subject"             text    NOT NULL,
  "body"                text    NOT NULL DEFAULT '',
  "is_read"             boolean NOT NULL DEFAULT false,
  "created_at"          timestamptz NOT NULL DEFAULT now(),
  -- Items in the mailbox expire after 90 days; warn the player on any mail
  -- bearing attachments (incl. credits). The sweeper deletes expired mail
  -- and forfeits unlooted attachments.
  "expires_at"          timestamptz NOT NULL DEFAULT (now() + interval '90 days')
);
CREATE INDEX IF NOT EXISTS "idx_mail_account_unread"
  ON "mailbox_messages" ("account_id", "is_read");
CREATE INDEX IF NOT EXISTS "idx_mail_expiry"
  ON "mailbox_messages" ("expires_at");

-- ---------------------------------------------------------------------------
-- Mail attachments -- each renders as a clickable square that loots into the
-- recipient character. kind: 0=item, 1=credits.
-- ---------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS "mailbox_attachments" (
  "id"           bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  "message_id"   bigint  NOT NULL REFERENCES "mailbox_messages" ("id") ON DELETE CASCADE,
  "kind"         smallint NOT NULL CHECK ("kind" IN (0, 1)),
  -- kind=0 item instance snapshot:
  "item_id"      integer DEFAULT NULL,                  -- -> net7.item_base.id (cross-DB, no FK)
  "stack_size"   integer DEFAULT NULL,
  "quality"      real    DEFAULT NULL,
  "item_cost"    bigint  DEFAULT NULL,
  "builder_name" text    DEFAULT NULL,
  "structure"    real    DEFAULT NULL,
  -- kind=1 credits:
  "credits"      bigint  DEFAULT NULL,
  "looted"       boolean NOT NULL DEFAULT false
);
CREATE INDEX IF NOT EXISTS "idx_attach_message"
  ON "mailbox_attachments" ("message_id");

-- ---------------------------------------------------------------------------
-- server_status already exists (server_status.sql) with players_online and a
-- singleton PK (id=1), so the "Players: X / ONLINE|OFFLINE" query is already
-- a PK lookup. The Go service caches it for 60s. No extra index needed; the
-- staleness check reads updated_at from the same single row.
-- ---------------------------------------------------------------------------
