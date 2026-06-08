-- server_status.sql -- single-row live server heartbeat (net7_user).
--
-- Phase AM (AM-8). The status-notifier sidecar grew a read-only Discord
-- `/status` command. Players-online (with levels) and the sectors those players
-- occupy are derivable straight from net7_user, but three facts are NOT in any
-- table: the authoritative in-memory player count, the number of sectors the
-- server has actually STARTED (loaded in memory -- distinct from "occupied"),
-- and the server's boot time (for uptime). Those live only in server memory
-- (GMemoryHandler::GetPlayerCount, ServerManager::GetSectorCount, the process
-- boot time).
--
-- Rather than open an inbound query path into the security-sensitive game
-- process (which CLAUDE.md forbids), the server periodically UPSERTs those three
-- numbers into this single row on the net7_user connection it already holds via
-- SaveManager. The sidecar reads the row to answer `/status`. This is emit-only,
-- exactly like external_status_events: no inbound control path into the server.
--
-- Singleton: id is pinned to 1 by a CHECK, so the UPSERT target is always the
-- same row (ON CONFLICT (id)). boot_time is rewritten on every heartbeat with
-- THIS process's boot time, so a server restart naturally refreshes it.
--
-- Applied UNCONDITIONALLY by schema-init (CREATE TABLE IF NOT EXISTS is
-- idempotent), so it lands on pre-existing volumes too.
--
--   boot_time       when the current server process became ready (uptime base).
--   players_online  authoritative in-memory player count at last heartbeat.
--   sectors_online  sectors STARTED in memory at last heartbeat (>= occupied).
--   updated_at      when the server last wrote this row (staleness check: if
--                   this is old, the server is down / not heartbeating).

CREATE TABLE IF NOT EXISTS server_status (
    id              INTEGER     PRIMARY KEY DEFAULT 1 CHECK (id = 1),
    boot_time       TIMESTAMPTZ NOT NULL,
    players_online  INTEGER     NOT NULL DEFAULT 0,
    sectors_online  INTEGER     NOT NULL DEFAULT 0,
    updated_at      TIMESTAMPTZ NOT NULL DEFAULT now()
);
