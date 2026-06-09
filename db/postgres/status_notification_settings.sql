-- status_notification_settings.sql -- per-kind relay on/off switches (net7_user).
--
-- Phase AM. The status-notifier sidecar relays login/logout/levelup/broadcast/
-- server_start lines to Discord. This table lets a Discord ADMIN turn individual
-- kinds on or off at runtime via the bot's `/notify` slash command, without a
-- redeploy.
--
-- CRITICAL -- this is a SIDECAR-only delivery filter, NOT a server gate. The game
-- server keeps emitting ALL kinds unconditionally into external_status_events
-- (gated only on NET7_EXTERNAL_STATUS_ENABLED). The sidecar reads THIS table at
-- drain time and simply skips delivering a disabled kind. The game server NEVER
-- reads this table. That is deliberate: if the server read a Discord-writable
-- table it would hand Discord admins a control path INTO the security-sensitive
-- game process, which CLAUDE.md forbids. Keep emit unconditional; filter only in
-- the sidecar.
--
-- Applied UNCONDITIONALLY by schema-init (CREATE TABLE IF NOT EXISTS + the seed's
-- ON CONFLICT DO NOTHING are both idempotent), so it lands on pre-existing
-- volumes too -- same pattern as external_status_events / server_status.
--
--   kind        one of login|logout|levelup|broadcast|server_start|
--               player_destroyed|jumpstarted.
--   enabled     true => relay this kind; false => the sidecar drops it.
--   updated_at  when the flag was last changed.
--   updated_by  the Discord user id that last changed it (audit trail).

CREATE TABLE IF NOT EXISTS status_notification_settings (
    kind        TEXT        PRIMARY KEY,
    enabled     BOOLEAN     NOT NULL DEFAULT TRUE,
    updated_at  TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_by  TEXT
);

-- Seed the known kinds, all enabled, so behaviour is unchanged until an admin
-- toggles one. ON CONFLICT keeps an operator's existing choices on reboot.
INSERT INTO status_notification_settings (kind) VALUES
    ('login'), ('logout'), ('levelup'), ('broadcast'), ('server_start'),
    ('player_destroyed'), ('jumpstarted')
ON CONFLICT (kind) DO NOTHING;
