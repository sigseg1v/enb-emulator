-- login_ticket.sql -- shared auth-ticket handoff table (net7_user).
--
-- Phase AH-9. The login-server (login-server/Net7SSL/LinuxAuth.cpp) and the
-- game server (server/src/AccountManager.cpp) both mint session tickets of the
-- form  username-<32 hex chars>  after a successful password check. The Win32
-- server validated a presented ticket via a login-server -> game-server SSL
-- handoff (RegisterSectorServer); the Linux port dropped that handoff, so the
-- game server's ProcessTicketInfo (server/src/UDP_Global.cpp) never checked the
-- token suffix at all -- any host could present  victim-anything  and be served
-- that account. This table restores the handoff via the net7_user database both
-- processes already share: whichever process mints a ticket UPSERTs the token
-- here, and ProcessTicketInfo validates the presented suffix against it
-- (constant-time) before serving the avatar list.
--
-- Applied UNCONDITIONALLY by the schema-init service (CREATE TABLE IF NOT
-- EXISTS is idempotent), so it lands on pre-existing volumes too -- it is NOT
-- gated behind the accounts probe.
--
--   username    the account name (the part before the first '-' of the ticket).
--   token       the 32-hex CSPRNG suffix (the part after the first '-').
--   expires_at  unix epoch milliseconds; a presented ticket is rejected once
--               now() passes this. Matches the 5-minute in-memory TTL.

CREATE TABLE IF NOT EXISTS login_ticket (
    username    TEXT   PRIMARY KEY,
    token       TEXT   NOT NULL,
    expires_at  BIGINT NOT NULL
);
