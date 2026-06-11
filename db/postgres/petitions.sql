-- petitions.sql -- in-game petition / GM-ticket store (net7_user).
--
-- The retail Net-7 server submitted player petitions (the in-game
-- petition / bug-report UI, SAVE_CODE_PETITION) by calling a MySQL stored
-- procedure  TicketViaServer(username, name, email, subject, complaint,
-- playerlist, problemtype)  that lived in Net-7's separate web/support
-- database. That database was never part of this preservation tree, so the
-- procedure exists nowhere in our schema and the CALL fails at parse time
-- ("schema net7_user does not exist") -- every petition a player submitted
-- was silently lost and logged an error.
--
-- Phase N inlined the other dead MySQL procs (accLogin, avaLogin, incWarn)
-- into the C++ libpqxx path; this table is the same treatment for the
-- petition proc. SaveManager::HandlePetition now INSERTs a row here instead
-- of CALLing the missing procedure. The columns mirror the seven proc
-- arguments plus the petition's game id and a server-side timestamp; a
-- future GM tool reads them. No game-visible behaviour changes -- the
-- client receives the same (empty) reply it always did.
--
-- Applied UNCONDITIONALLY by the schema-init service (CREATE TABLE IF NOT
-- EXISTS is idempotent), so it lands on pre-existing volumes too, exactly
-- like login_ticket.sql.
--
--   id            surrogate key.
--   game_id       the submitting avatar's game id (wire field, may be 0).
--   username      account name of the submitter.
--   avatar_name   submitting avatar's first name.
--   email         account email at submission time.
--   subject       petition subject line.
--   complaint     petition body.
--   player_list   client-supplied list of involved players (free text).
--   problem_type  petition category code (client enum).
--   created_at    server receive time.

CREATE TABLE IF NOT EXISTS petitions (
    id            BIGSERIAL   PRIMARY KEY,
    game_id       BIGINT      NOT NULL DEFAULT 0,
    username      TEXT        NOT NULL,
    avatar_name   TEXT        NOT NULL,
    email         TEXT        NOT NULL,
    subject       TEXT        NOT NULL,
    complaint     TEXT        NOT NULL,
    player_list   TEXT        NOT NULL,
    problem_type  INTEGER     NOT NULL,
    created_at    TIMESTAMP   NOT NULL DEFAULT now()
);
