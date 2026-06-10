-- account_id_guard.sql -- enforce the 32-bit GameID ceiling on account ids and
-- rescue any account that was already created above it.
--
-- License: MIT (Freya). Companion to freya_online_bots.sql + seed.sql.
--
-- Target database: net7_user. Idempotent: safe to run on every boot.
--
-- WHY THIS EXISTS
-- A player's on-wire GameID is avatar_id | PLAYER_TAG (PLAYER_TAG = 1<<30),
-- carried in a 32-bit field (server/src/UDP_Master.cpp ProcessHandoff casts the
-- game id to int32_t). avatar_id = account_id*5 + slot + 1 (the login-server
-- AVATAR_ID macro, computed fresh at every login). For the GameID to survive the
-- 32-bit wire and for the server to recover its key via
-- GameID & ~(PLAYER_TAG|MANU_TAG), avatar_id must fit in bits 0..29, i.e.
-- avatar_id < 2^30 (1073741823). With slot up to 4 that bounds
-- account_id <= 214748363. An account above that (e.g. the OLD AhBot sentinel
-- 1000000001, or any signup the poisoned seed-account recipe created just above
-- it) computes an avatar_id ~5e9 whose high bit is truncated off the wire; the
-- master handoff then logs "Unable to find player ..." forever and the client
-- hangs at the loading screen.
--
-- This migration (1) moves AhBot to the reserved-but-safe id pair
-- (account 9000001 / avatar 45000006), (2) remaps any other over-ceiling account
-- down into the reserved band, re-keying every avatar-owned row, (3) resyncs the
-- identity sequence ignoring the reserved band, and (4) installs CHECK
-- constraints so an out-of-range id can never be inserted again.

-- Remap a single avatar id everywhere it is referenced. Covers avatar_id and the
-- *_avatar_id columns (seller/high_bidder/bidder/recipient) by name pattern, so
-- it stays correct if new owner columns are added later.
CREATE OR REPLACE FUNCTION pg_temp._freya_remap_avatar(p_old bigint, p_new bigint)
RETURNS void LANGUAGE plpgsql AS $fn$
DECLARE r record;
BEGIN
    FOR r IN
        SELECT table_name, column_name FROM information_schema.columns
        WHERE table_schema='public' AND column_name LIKE '%avatar_id'
    LOOP
        EXECUTE format('UPDATE public.%I SET %I = $1 WHERE %I = $2',
                       r.table_name, r.column_name, r.column_name)
        USING p_new, p_old;
    END LOOP;
END $fn$;

-- Remap a single account id everywhere: accounts.id plus the *account_id columns.
CREATE OR REPLACE FUNCTION pg_temp._freya_remap_account(p_old bigint, p_new bigint)
RETURNS void LANGUAGE plpgsql AS $fn$
DECLARE r record;
BEGIN
    EXECUTE 'UPDATE public.accounts SET id = $1 WHERE id = $2' USING p_new, p_old;
    FOR r IN
        SELECT table_name, column_name FROM information_schema.columns
        WHERE table_schema='public' AND column_name LIKE '%account_id'
    LOOP
        EXECUTE format('UPDATE public.%I SET %I = $1 WHERE %I = $2',
                       r.table_name, r.column_name, r.column_name)
        USING p_new, p_old;
    END LOOP;
END $fn$;

DO $driver$
DECLARE
    r       record;
    s       int;
    nextid  bigint := 9000002;   -- 9000001 is reserved for AhBot
BEGIN
    -- (1) AhBot: its stored avatar id is NOT formula-derived (it equals the old
    -- account id), so remap it explicitly to the new reserved pair.
    IF EXISTS (SELECT 1 FROM accounts WHERE id = 1000000001) THEN
        PERFORM pg_temp._freya_remap_avatar(1000000001, 45000006);
        PERFORM pg_temp._freya_remap_account(1000000001, 9000001);
        RAISE NOTICE 'account_id_guard: remapped AhBot 1000000001 -> account 9000001 / avatar 45000006';
    END IF;

    -- (2) Any other over-ceiling account: remap down into the reserved band.
    -- Its avatars ARE formula-derived, so re-key each slot old=acct*5+s+1 ->
    -- new=nextid*5+s+1 before moving the account row.
    FOR r IN SELECT id FROM accounts WHERE id > 214748363 ORDER BY id LOOP
        WHILE EXISTS (SELECT 1 FROM accounts WHERE id = nextid) LOOP
            nextid := nextid + 1;
        END LOOP;
        FOR s IN 0..4 LOOP
            PERFORM pg_temp._freya_remap_avatar(r.id * 5 + s + 1, nextid * 5 + s + 1);
        END LOOP;
        PERFORM pg_temp._freya_remap_account(r.id, nextid);
        RAISE NOTICE 'account_id_guard: rescued over-ceiling account % -> %', r.id, nextid;
        nextid := nextid + 1;
    END LOOP;

    -- (3) Resync the identity sequence to the highest REAL account id, ignoring
    -- the reserved bot band so it can never hand the sentinel id to a signup.
    PERFORM setval('accounts_id_seq',
                   GREATEST((SELECT COALESCE(MAX(id), 0) FROM accounts WHERE id < 9000001), 1));

    -- (4) Install the guards if absent (ADD CONSTRAINT has no IF NOT EXISTS).
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'accounts_id_gameid_fits') THEN
        ALTER TABLE accounts ADD CONSTRAINT accounts_id_gameid_fits
            CHECK (id BETWEEN 1 AND 214748363);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'avatar_info_id_gameid_fits') THEN
        ALTER TABLE avatar_info ADD CONSTRAINT avatar_info_id_gameid_fits
            CHECK (avatar_id BETWEEN 1 AND 1073741823 AND account_id BETWEEN 1 AND 214748363);
    END IF;
END $driver$;
