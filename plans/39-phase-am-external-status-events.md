# Phase AM -- External status events (server -> chat/dashboard relay)

Status: **built 2026-06-07.** AM-0..AM-5 + AM-7 done; AM-6 partially verified (see
"Verification status" at the bottom -- the live server-emit path is proven for
`server_start`; the four player-driven events are NOT yet driven by a real client
login in this shared dev env, tracked as CV-AM-1 in plans/29). **AM-8 (read-only
`/status` Discord bot + emit-only `server_status` heartbeat) built + DB-verified
2026-06-07**; the bot's live Discord round-trip needs an owner-created bot token
(CV-AM-2 in plans/29).

**Naming (owner directive 2026-06-07):** the mechanism is consumer-agnostic. A
Discord webhook is the FIRST consumer, but every code/table/func/env identifier is
generic: function `EmitExternalStatusEvent`, table `external_status_events`,
NOTIFY channel `external_status_event`, flag `NET7_EXTERNAL_STATUS_ENABLED`, sidecar
dir `status-notifier/`, sidecar env `STATUS_WEBHOOK_URL`. The design text below keeps
the original rationale; where it says `discord_outbox` / `NET7_DISCORD_ENABLED` /
`discord-notifier`, read the generic name.

## Goal

Post live server events to a Discord channel:

- `Player <name> (level: Cx/Ty/Ez, class: TE) logged in. (N online)`
- `Player <name> logged out. (N online)`
- `Player <name> leveled up {Combat|Trade|Explore} to ##!`
- `Server broadcast: [<player>] <message>`
- `Server started.`

(The `(N online)` suffix on login/logout is an explicit owner request -- 2026-06-07.
N = the authoritative in-memory player count, `GMemoryHandler::GetPlayerCount()`,
sampled AFTER the join/leave is applied so the number already reflects this event.)

Configurable. The Discord secret (webhook URL or bot token) is a **secret** and is
NEVER committed -- it lives in env / the deploy `.env` only, same as the DB password.

## The one real design decision (recommendation, owner may veto)

The owner's request conflated two separate links:

1. **Bot->Discord delivery.** A "Discord bot" with a **Gateway websocket** is only
   needed to *receive* from Discord (slash commands, presence, reactions). To *post*
   a channel message you do a plain HTTPS POST -- either to a **channel webhook URL**
   (no bot, no gateway) or to the REST API with a bot token. All five of our events
   are one-way server->Discord posts, so a **webhook is sufficient and simplest**;
   the "websocket api" the owner imagined for posting isn't the right primitive --
   you POST. A Gateway bot buys nothing here and adds a always-connected socket,
   token scopes, and a library dependency.
   - **Recommendation: webhook now.** Keep the delivery code behind a small
     `Notifier` interface so a Gateway bot can be added later IF bidirectional
     features (e.g. `!who` from Discord, `/broadcast` from Discord -> game) are
     ever wanted. That is the only thing that would justify the Gateway, and it is
     out of scope for this phase.

2. **Server->sidecar push.** How the C++ server hands an event to the thing that
   talks to Discord. Options:
   - **(A, recommended) Postgres outbox table + `LISTEN/NOTIFY`.** The server
     already holds a Postgres connection; it `INSERT`s a row into a
     `discord_outbox` table (in `net7_user`) and fires `NOTIFY discord_event`.
     The sidecar `LISTEN`s, drains new rows, POSTs them to Discord, marks them
     sent. **Durable** (events survive a sidecar restart -- they sit in the table
     until delivered), **adds zero new outbound network surface to the game
     server** (no HTTP client, no new socket -- the server only ever talks to its
     own Postgres, which it already does), and is trivially parameterized SQL.
   - (B) Server opens a local socket / HTTP to the sidecar. Rejected: pulls an
     outbound HTTP client into the security-sensitive server (none exists today --
     confirmed no libcurl/http in `server/src`), loses events if the sidecar is
     down, and widens the server's attack surface for a tooling convenience --
     exactly what CLAUDE.md forbids.
   - (C) Sidecar tails `docker logs` / a log file. Rejected: brittle string-scrape,
     racy, no structured fields, breaks the moment a log line is reworded.

**Chosen architecture:** game server `INSERT ... ; NOTIFY` -> `discord_outbox`
(net7_user) -> Go sidecar container `LISTEN`s + drains + POSTs to a Discord
**webhook**. Sidecar config via env. Secret never committed.

```
 game server (C++) --INSERT+NOTIFY--> [net7_user.discord_outbox] <--LISTEN/drain-- discord-notifier (Go sidecar) --HTTPS POST--> Discord webhook
```

Why this respects the server rules: the server change is a **new feature that only
EMITS events**; it does not alter a single byte the EnB client sends or receives, so
the wire-fidelity gate (primary-source citation + CLI byte-pin + plans/29 CV entry)
does not apply. It still must not weaken security -- and writing a row to our own DB
adds no attack surface. The outbox is in `net7_user` (same DB the save-state writes
already use), so the INSERT shares the server's existing connection -- no cross-DB
read.

## Outbox schema (net7_user)

```sql
CREATE TABLE discord_outbox (
    id          bigint GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
    kind        text        NOT NULL,   -- 'login'|'logout'|'levelup'|'broadcast'|'server_start'
    content     text        NOT NULL,   -- the fully-rendered message line
    created_at  timestamptz NOT NULL DEFAULT now(),
    sent_at     timestamptz             -- NULL until the sidecar confirms a 2xx from Discord
);
CREATE INDEX discord_outbox_unsent ON discord_outbox (id) WHERE sent_at IS NULL;
```

Decisions:
- The server renders the **final string** (it has the name/levels/class/count;
  the sidecar should stay dumb). Sidecar only formats nothing -- it POSTs `content`.
  Reconsider only if we later want per-event Discord embeds.
- `NOTIFY discord_event` (no payload, or payload = the new id) fired in the same
  transaction as the INSERT so a committed row is always announced.
- Sidecar on startup does one drain of `sent_at IS NULL` rows (catches anything
  inserted while it was down), then settles into LISTEN. Retains the partial index
  so the drain is cheap.
- **Retention:** a periodic `DELETE FROM discord_outbox WHERE sent_at < now() -
  interval '7 days'` (sidecar or a cron) so the table doesn't grow unbounded.
- **Failure semantics:** if Discord returns non-2xx / times out, leave `sent_at`
  NULL and retry with backoff; respect Discord 429 `Retry-After`. A row never
  delivered just stays unsent (visible, debuggable) -- no silent drop.

## Server hook sites (from the AM-0 exploration, all citable in-repo)

| Event | File:line | Function | Data in hand |
|---|---|---|---|
| Login | `UDP_Global.cpp:258-288` | `UDP_Connection::HandleGlobalTicketRequest` (after player node alloc + DB load, name set @288) | `player->Name()`, `Race()`/`Profession()` -> class, `CombatLevel()/TradeLevel()/ExploreLevel()`. Count via `GetPlayerCount()` after join. |
| Logout | `PlayerManager.cpp:119-140` | `PlayerManager::DropPlayerFromGalaxy(Player*)` (the ONE choke point for clean logout AND drop/kick/timeout; name @135-136) | `p->Name()`. Count via `GetPlayerCount()` sampled AFTER the player is pulled. |
| Level up | `PlayerExperience.cpp:501-535` | `Player::AwardXP` skill-points-earned branch (level crossed) | `this` (Player), `xp_type` (XP_COMBAT/EXPLORE/TRADE), new `level`, `Name()`. |
| Broadcast | `PlayerManager.cpp:718` | `PlayerManager::BroadcastChat(GameID, message, ...)` -- sector broadcast WITH sender. (Also `SendGlobalVaMessage` / `ErrorBroadcast` for admin-global; pick whichever the owner's "Server broadcast" actually maps to -- confirm during AM-3.) | sender `GetPlayer(GameID)->Name()`, `message`. |
| Server start | `ServerManager.cpp:398` | `m_SectorAssignmentsComplete = true` (server bound + content loaded + master listener about to start) | none needed. |

Class id -> abbreviation (`Net7.h:332-348`, `ClassIndex() = Race()*3 + Profession()`):
`0 TE, 1 TT, 2 TS, 3 JD, 4 JS, 5 JE, 6 PW, 7 PP, 8 PS`.
(Note: confirm the Profession ordering Enforcer/Trader/Scout vs the header's enum
order during AM-2 -- the exploration reported `1=Trader,2=Scout` for Terran, so the
abbreviation table must be built from the actual enum, not assumed.)

Level-string format `Cx/Ty/Ez` = `C<combat>/T<trade>/E<explore>`.

## A small shared emit helper (server side)

Add ONE function, e.g. `g_DiscordOutbox->Emit(kind, rendered_string)` (a thin class
that owns a parameterized INSERT + NOTIFY on the server's net7_user connection). Every
hook site calls it with an already-rendered line. Keep it:
- **Parameterized** -- `kind` and `content` are bound params, never concatenated
  (CLAUDE.md hard rule). The rendered `content` contains a player-controlled name and
  a player-controlled broadcast message, so this is exactly the injection vector the
  rule exists for.
- **Best-effort / non-blocking** -- a Discord outbox failure must NEVER stall or fail
  a login, logout, level-up, or chat. Wrap in try/catch, log-and-continue. The DB
  write is local and cheap, but the game path's correctness does not depend on it.
- **Feature-flagged** -- `NET7_DISCORD_ENABLED` (default OFF). When off, `Emit` is a
  no-op and no outbox rows are written, so dev/test/CI stacks incur nothing and the
  integration suite is unaffected. The deploy stack sets it on.

## Go sidecar (`discord-notifier/`)

- New top-level dir `discord-notifier/` (Go module), its own `Dockerfile`, added as a
  service in `docker-compose.yml` (dev, default-off via env) and
  `deploy/do/docker-compose.prod.yml` (prod).
- Depends only on Postgres (net7_user) + outbound HTTPS to Discord. No EnB protocol,
  no game ports.
- Libraries: `github.com/jackc/pgx/v5` (LISTEN/NOTIFY support) + stdlib `net/http`.
  No heavyweight Discord library needed for webhook POST (it's one HTTP call). If we
  later choose a Gateway bot, add `bwmarrin/discordgo` then -- not now.
- Config (all env):
  - `DISCORD_WEBHOOK_URL` (**secret**, required; never committed)
  - `NET7_USER_DSN` / reuse the existing PG env the other services use
  - `DISCORD_POLL_BACKOFF_MS`, retention interval, optional per-kind enable flags.
- Rate-limit aware: serialize POSTs, honor 429 `Retry-After`, cap message length to
  Discord's 2000-char limit (truncate a runaway broadcast).
- No secrets logged. The webhook URL must never appear in a log line.

## Checklist

- [x] **AM-0** Locate all hook sites + data model. (exploration above).
- [x] **AM-1** Outbox table `external_status_events` -> `db/postgres/external_status_events.sql`
      (net7_user), applied UNCONDITIONALLY by the schema-init service in BOTH
      `docker-compose.yml` and `deploy/do/compose/docker-compose.prod.yml` (same
      pattern as `login_ticket.sql` -- a new table must land on pre-existing volumes,
      so it is NOT gated on an accounts probe). Partial index
      `external_status_events_unsent ON (id) WHERE sent_at IS NULL`.
- [x] **AM-2** Server emit helper `EmitExternalStatusEvent(kind, content)`
      (`server/src/SaveManager.{h,cpp}`). It rides the existing single-threaded
      `SaveManager` queue (which already owns the one serialized net7_user
      connection): `AddSaveMessage(SAVE_CODE_EXT_STATUS_EVENT, ...)` ->
      `HandleExternalStatusEvent` does the parameterized `INSERT INTO
      external_status_events (kind, content) VALUES (?, ?)` + `NOTIFY
      external_status_event`. Gated on `NET7_EXTERNAL_STATUS_ENABLED` (cached, default
      OFF -> no-op, no rows). Class-abbrev table `{TE,TT,TS,JD,JS,JE,PW,PP,PS}` built
      from `ClassIndex() = Race()*3 + Profession()`. NOT a new outbound socket -- it
      writes the DB the server already holds; no HTTP client added to the server.
- [x] **AM-3** 5 hook sites wired: login `UDP_Global.cpp` (renders
      `level: Cx/Ty/Ez, class: XX` + `(N online)` via `GetPlayerCount()` post-join),
      logout `PlayerManager::DropPlayerFromGalaxy` (+ `(N online)` post-drop), level-up
      `PlayerExperience.cpp` AwardXP skill-point branch, broadcast
      `PlayerManager::BroadcastChat`, server-start `ServerManager.cpp` after
      `m_SectorAssignmentsComplete=true`.
- [x] **AM-4** Go sidecar `status-notifier/` (module
      `github.com/enb-emulator/status-notifier`, pgx/v5 + stdlib net/http):
      `LISTEN external_status_event`, startup catch-up drain of the unsent partial
      index BEFORE settling into LISTEN, per-row deliver-then-`UPDATE sent_at` (stops
      on first failure to preserve order), 429 `Retry-After` honored, 2000-char cap,
      hourly retention sweep (`STATUS_RETENTION_DAYS`, 0 disables). Idles (does not
      crash-loop) when `STATUS_WEBHOOK_URL` is empty. Never logs the URL/secret. Unit
      tests (`main_test.go`) cover jsonString round-trip, deliver success/429/5xx/
      truncate, and DSN assembly+escaping -- all pass; `go vet` clean.
- [x] **AM-5** Compose + prod pipeline: `status-notifier` service in dev
      `docker-compose.yml` (default-off) and prod
      `deploy/do/compose/docker-compose.prod.yml` (image pulled from the `enb` repo as
      `enb:status-notifier-<v>`). `deploy/do/scripts/Build-And-Push.ps1` builds/pushes
      the third image (per-service `Context`: it builds from `status-notifier/`, NOT
      the repo root) and the vN tag regex includes it. `Update-Stack.ps1` threads
      `NET7_EXTERNAL_STATUS_ENABLED` / `STATUS_WEBHOOK_URL` (secret) /
      `STATUS_RETENTION_DAYS` from the operator `.env` into the regenerated droplet
      `.env` (so they survive redeploys). Documented in `deploy/do/.env.example` +
      README registry-layout section. `status-notifier/status-notifier` build artifact
      gitignored.
- [~] **AM-6** End-to-end. **Done:** sidecar verified against the real dev Postgres --
      NOTIFY -> drain -> POST with correct `{"content":...}` body and `sent_at` marked,
      plus a startup catch-up drain (row inserted while the sidecar was down delivered
      on restart without a NOTIFY). The real C++ server, brought up with the flag,
      wrote a live `server_start` row -- proving the full SaveManager queue ->
      HandleExternalStatusEvent -> parameterized INSERT path (no cross-DB issue).
      **NOT yet done:** driving login/logout/levelup/broadcast from a real client in
      this SHARED dev env was too risky (other live sessions hold the proxy ports;
      bouncing the shared server disrupts them). Tracked as **CV-AM-1** in
      `plans/29-client-verification.md`.
- [x] **AM-7** Docs: `docs/18-external-status-events.md` (architecture + config +
      emit-only/no-inbound-trust threat note). The doc states the sidecar talks ONLY
      to Postgres (net7_user) + outbound HTTPS and binds no game port.

## AM-8: read-only `/status` Discord bot + `server_status` heartbeat

Owner asked (2026-06-07) whether `/status` could print players online (with levels),
sectors online, and uptime. A webhook is outbound-only, so the *pull* direction
needs a bot. Built as a SECOND, independent feature inside the same sidecar -- still
no inbound path into the game server: the bot connects OUTBOUND to Discord's gateway
and answers `/status` with plain `SELECT`s.

- [x] **AM-8a** `db/postgres/server_status.sql` -- singleton heartbeat row
      (`id` CHECK=1, `boot_time`, `players_online`, `sectors_online`, `updated_at`),
      applied UNCONDITIONALLY by schema-init in BOTH compose files (same idempotent
      pattern as `login_ticket.sql`). **Verified:** table created in dev net7_user.
- [x] **AM-8b** Server emit-only heartbeat: `g_ServerBootTime` global
      (`Net7.cpp`/`Net7.h`), stamped at server-ready in `ServerManager.cpp`;
      `SaveManager::WriteServerStatusHeartbeat()` UPSERTs the row every ~30s on the
      save thread (parameterized; gated on the SAME `NET7_EXTERNAL_STATUS_ENABLED`
      flag; no-op when off). **Verified live:** with the flag on, the dev server
      wrote + refreshed the row every 30s (`updated_at` advancing), counts honest
      (0 players / 0 warm sectors with nobody on -- `GetSectorCount` is lazy). No DB
      errors in the server log. Needs `MemoryHandler.h`+`ServerManager.h` includes in
      SaveManager.cpp (Net7.h only forward-declares those classes).
- [x] **AM-8c** Bot: `status-notifier/bot.go` (`discordgo`). Idles unless
      `DISCORD_BOT_TOKEN` set (mirrors the webhook idle path). `/status` renders an
      embed: up/down from heartbeat staleness, uptime, in-memory player + warm-sector
      counts, and a per-player table (name, class name+code via race/`prof`, floored
      C/E/T levels, sector name from net7). All queries are bound-param `SELECT`s;
      sector names come from a second connection to net7 (no cross-DB join).
      `main.go` now starts the bot and/or the webhook relay independently.
- [x] **AM-8d** `bot_test.go`: className (all 9 classes incl. JD!=JW), out-of-range
      fallback, renderPlayerLine (entered/unknown-sector/char-select), humanDuration,
      swapDBName. `go build`/`go vet`/`go test` all clean.
- [x] **AM-8e** Config threaded through: `DISCORD_BOT_TOKEN`/`DISCORD_GUILD_ID`
      (secrets) in dev `docker-compose.yml`, prod compose, `Update-Stack.ps1` .env
      regeneration, `.env.example`; `server_status.sql` schema-init in both compose
      files. Docs updated (`docs/18`).
- [ ] **AM-8f** Live Discord round-trip: needs an owner-created bot token (the bot
      cannot be exercised without one). Tracked as **CV-AM-2** in plans/29. The
      rendering + SQL are unit-tested and the SQL was run against the live dev schema;
      what is unverified is purely the discordgo gateway/slash-command handshake.

## Verification status (what is proven vs. pending)

| Path | Proven? | How |
|---|---|---|
| Sidecar NOTIFY -> drain -> POST -> sent_at | YES | live vs dev Postgres + httptest |
| Sidecar startup catch-up drain (no NOTIFY) | YES | row pre-inserted, sidecar restarted |
| Sidecar idle on empty webhook (no crash-loop) | YES | unit + manual |
| Server emit path (queue->INSERT, no cross-DB) | YES | live `server_start` row in net7_user |
| login/logout/levelup/broadcast rendered lines | NO | needs real-client login -> CV-AM-1 |
| `NET7_EXTERNAL_STATUS_ENABLED` empty == no rows | YES | default-off restore verified |
| AM-8 server_status heartbeat UPSERT + 30s refresh | YES | live row, `updated_at` advanced; no DB errors |
| AM-8 bot SQL (player list + sector names) valid | YES | both queries run against live dev schema |
| AM-8 bot render logic (class/levels/sector/uptime) | YES | `bot_test.go` unit tests pass |
| AM-8 bot live Discord `/status` round-trip | NO | needs owner bot token -> CV-AM-2 |

## Constraints carried in (do not relearn the hard way)

- Discord secret = secret. Env only. Never committed (CLAUDE.md no-secrets rule).
- Parameterized SQL only -- the rendered `content` carries player-controlled text.
- Server change is emit-only -- alters no EnB client wire byte, so the wire-fidelity
  gate (CLI byte-pin + plans/29 CV) does NOT apply. But it still must not weaken
  security: writing to our own DB adds no surface; do NOT add an outbound HTTP client
  to the game server (that's the sidecar's job).
- No em-dashes in committed files (`--`/`-`).
- Best-effort: a Discord failure must never break or slow a game operation.
- Default-OFF feature flag so dev/test/CI are untouched.
