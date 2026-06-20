-- fix_orphan_mob_spawns.sql
--
-- Corrective data fix for the "Default" lvl 0 crash-mob.
--
-- The upstream content data carries mob_spawn_group rows whose mob_id does
-- NOT exist in mob_base (verified absent from both the original MySQL dump
-- and the converted Postgres -- these mobs have never existed, this is not a
-- conversion loss). When a spawn picks such an id, MOB::SetMOBType bails with
-- m_MOB_Data left NULL: the object still ticks, the client renders it as an
-- unnamed "Default" (faction 0), nearby mobs treat it as hostile, and the
-- per-player range scan dereferences the NULL m_MOB_Data and crashes the whole
-- sector server (server/src/MOBClass.cpp CheckWarningShots / CheckAggro -- now
-- additionally NULL-guarded, but the dangling spawn entries are removed here so
-- the ghost mob never appears in the first place).
--
-- Known orphans at time of writing (mob_id -> sector): 627 Carpenter,
-- 724 Swooping Eagle, 802 Mars Gamma + Saturn, 823 Swooping Eagle (the
-- reported "Field of Glass Point" mob), 1716 Unknown Galaxy, 1727 Lagarto.
--
-- Set-based on purpose: it removes ANY mob_spawn_group row referencing a
-- non-existent mob, so it also covers orphans not enumerated above and any
-- introduced later. A spawn that keeps valid siblings still spawns them; a
-- spawn whose ONLY entry was an orphan (e.g. "Nesshix the Hand") becomes
-- empty, which is strictly better than a crash-ghost (we cannot fabricate the
-- missing mob's stats from any primary source).
--
-- UNCONDITIONAL + idempotent: deleting rows that do not exist is a no-op, so
-- this is safe to re-run on every boot and on pre-existing volumes.

BEGIN;

DELETE FROM mob_spawn_group msg
 WHERE NOT EXISTS (
     SELECT 1 FROM mob_base mb WHERE mb.mob_id = msg.mob_id
 );

COMMIT;
