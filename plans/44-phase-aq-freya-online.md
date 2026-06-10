# Phase AQ -- Freya Online (Go login server + Auction House + Mailbox + React UI)

Status: in progress (started 2026-06-09)

## What this is

Four interlocking deliverables, requested together:

1. **Go login server** -- a from-scratch, memory-safe replacement for the C++
   `login-server/Net7SSL`. MUST preserve the legacy game-login wire protocol
   BYTE-FOR-BYTE (the real Win32 client + the proxy depend on it). Additionally
   serves the "Freya Online" website + JSON API. New code, no Net-7 compile
   dependency -> **Freya / MIT, lives under `freya/online/`**.
2. **Auction House** -- WoW-style (researched from WoW; NOT named in any
   committed file/commit/doc). Post 8/12/24h, optional buyout, opaque
   low/med/high time bucket (deliberately NOT precisely ordered, anti-abuse),
   1%-of-value min bid step, 10% AH cut collected up front, 95% deposit return
   on unsold (5% lost), winnings/returns delivered to the Mailbox.
3. **Mailbox** -- shared per ACCOUNT. Subject/recipient/sender/has-item list,
   read/unread, item + credit attachments looted via clickable squares,
   90-day expiry with a warning on any mail bearing attachments.
4. **React + TypeScript SPA** -- login art page, auth cookie (proper session
   middleware, NOT hand-rolled), Mailbox tab + Auction House tab, an always-on
   ONLINE/OFFLINE + Players:X status (fetched, 60s server-side cache, no SSR).
   Served at https://enb.sigsegv.land index. **Imported from the Claude Design
   artifact "Freya Online.html" -- do NOT hand-design a substitute.**

Plus: `FREYA_AH_BOTS=1` -> an "AhBot" account posts ~128 items/day with the
specified rarity/quality/price distribution; and a login-time in-chat private
system message to a player with unread mail.

## THE #1 ARCHITECTURAL RISK (read before any inventory/credit mutation)

Credits (`net7_user.avatar_level_info.credits`) and item instances
(`net7_user.avatar_{inventory,vault}_items`) are owned IN MEMORY by the C++
game server while a character is logged in, and flushed to Postgres on save.
The Go web service is a SEPARATE authority. If the web service mutates a
character's vault/credits while that character is online, the server's next
save CLOBBERS the web write (item dup / credit loss / desync).

WoW avoids this because the AH and the game are one authority. We are not.

**Default decision (offline-guard):** web operations that mutate per-character
storage (list an item = remove from vault; loot mail = add to vault/credits;
collect winnings) are ONLY applied when the owning character is NOT currently
online. Online status is read from the same signal `server_status` /
`avatar_info.last_login`/`last_logout` expose. If the character is online the
web UI shows "log out this character to manage its items/mail". This is the
safe, correct default and needs no server change.

The fully-seamless alternative (route web mutations through the server over the
AF_UNIX IPC bus so the in-memory copy stays authoritative) is a large server
addition gated by CLAUDE.md "Server integrity rules" -- deferred, recorded as
an open question for the owner. **CV / owner-decision: see plans/29 (add an
entry) before relaxing the offline-guard.**

## Pricing / rarity rules (from the prompt, pinned here so they don't drift)

- Vendor price base = `net7.item_base.buying_price` (what NPCs pay).
- Bot bid-start = 110% vendor; bot buyout = 200% vendor.
- AH min bid step = max(1% of item_value, ...) each bid; item_value snapshot =
  vendor value at list time.
- AH cut = 10% of item_value, collected UP FRONT at listing.
- Unsold return = 95% of deposit (lose 5%), item returned -> mailbox.
- Quality: bots roll 80%..200% on a normal distribution.
- Rarity color BY QUALITY: >=180 epic, 150-179 rare, 130-149 uncommon,
  <=129 common, <100 gray/junk. **Caps by level:** lvl 1-3 cap uncommon
  (>=150 still green), lvl 4-6 cap rare (>=190 still blue), only lvl 7+ can be
  epic. Items with no quality% are always common.
- Bot daily mix (~128/day): rare ~5% (components+weapons+shields+reactors lvl
  5-9, devices lvl 1-9), uncommon ~25% (components lvl 1-4 + weapons/shields/
  reactors), common = rest (ores in stacks of 20, all levels equally; etc.).

## Legacy login wire protocol (MUST preserve byte-for-byte)

Mapped from `login-server/Net7SSL` (see the Phase AQ exploration notes / the
C++ source directly). The Go server must reproduce:

- **TLS :443**, GET-only HTTP/1.1, dispatch by path substring.
- `/AuthLogin?username=&password=&serviceID=&version=` ->
  `HTTP/1.1 200 OK` + `Content-Type: text/plain` + `Server: AuthServer/2.5` +
  body `Valid=TRUE\r\nTicket=<username>-<32-hex>\r\n` or `Valid=FALSE\r\n`.
  Username parse stops at first `&`/space/newline. Ticket split on FIRST `-`.
- Ticket suffix = 16 CSPRNG bytes -> 32 hex; UPSERT into
  `net7_user.login_ticket (username, token, expires_at)` with 30-min expiry
  (`TICKET_EXPIRE_TIME` 1800000ms -- MUST match server `AccountManager.h`).
- Auth: `SELECT password_phc FROM accounts WHERE username=$1` (net7_user),
  Argon2id verify in-process (libsodium-compatible; Go `golang.org/x/crypto/
  argon2` can verify the same PHC, or cgo libsodium). No user-enumeration
  difference between bad-user and bad-pass.
- `/sectorserver.cgi` (version must == "0.2", port >= 3500) -> `Success=TRUE`.
- `/touchsession.jsp` (needs `lkey=`) -> chunked `Success`.
- `/certificate.html` -> "<domain> certificate successfully installed!".
- `/updateCheck` (POST JSON launcher/proxy SHA512) -> patcher-manifest compare
  -> `{"status":"UP_TO_DATE"}` or `UPDATE_NEEDED` with files; 503 if manifest
  not loaded. Env: `NET7_PATCHER_MANIFEST_URL`, `NET7_PATCHER_DL_BASE`.
- `/who.cgi` -> 404 (deliberate no-op). Unknown -> 404.
- **AF_UNIX SOCK_DGRAM keepalive**: recv `/run/net7-ipc/net7SSL.sock`, send
  `/run/net7-ipc/net7.sock`; send `"Ping"` ~10s, expect `"pong"`, exit if
  silent ~60s. (Server side already speaks this.)
- Env contract: `DB_HOST` (def postgres:5432), `DB_USER` (net7), `DB_PASS`
  (net7), `DB_NAME` (net7_user), `DOMAIN` (localhost -> `<domain>.cer/.pem`),
  `NET7SSL_BIND_ADDR`.
- UDP 0x4000-0x4004 player-count opcodes are between server<->login; the C++
  login used them to learn max/current player count. The Go server can either
  speak them or read `server_status.players_online` directly (simpler, and the
  web status needs that row anyway). **Decision: read server_status; only add
  the UDP opcode client if the server depends on login answering it.** VERIFY.

## Verification gates

- The Go login server is NOT done until a real `client.exe` logs in through it
  (plans/29 -- add CV entries). Until then, the C++ `login` service STAYS in
  docker-compose; bring the Go one up alongside on a different internal name
  and cut over only after the CLI auth test + real-client login both pass.
- Keep the C++ login-server source in-tree until cutover is owner-confirmed.
  ("We can delete the old one" -> yes, but AFTER it's proven, not before.)
- CLI: add an integration test that drives `/AuthLogin` against the Go server
  and asserts the exact response bytes + a working ticket handoff.

## Checklist

### AQ-0 Foundation
- [x] Map legacy login protocol (exploration agent, 2026-06-09)
- [x] Map DB schema for AH/mailbox (exploration agent, 2026-06-09)
- [x] `db/postgres/freya_online.sql` -- auction_listings, auction_bids,
      mailbox_messages, mailbox_attachments, avatar_info.deleted_at, indexes
- [x] Wire freya_online.sql into schema-init (docker-compose.yml)
- [ ] Verify the UDP 0x4000-0x4004 dependency (does the server REQUIRE login to
      answer, or is it optional?) before deciding to port it
- [ ] Find/confirm online-status signal for the offline-guard

### AQ-1 Go login server (legacy parity)
- [x] `freya/online/` Go module, MIT header (`server/`, go.mod go 1.25.0,
      scs/v2 + pgx/v5 + x/crypto). Two pgxpool pools (net7_user + net7 content).
- [x] TLS :443 GET HTTP dispatch (main.go startServers; cert/key from
      NET7SSL_CERT_DIR/<domain>.cer|.pem). Runtime-verified: TLS + HTTP listen.
- [x] /AuthLogin (Argon2id verify, ticket UPSERT) -- byte-exact response.
      RUNTIME-VERIFIED 2026-06-09: bad creds -> 13-byte `Valid=False\r\n`
      (Server: AuthServer/2.5); good creds (freyatest) -> 63-byte
      `Valid=TRUE\r\nTicket=freyatest-<32hex>\r\n` + login_ticket row upserted
      (token = post-first-dash suffix, ms expiry). legacy.go + ticket.go.
- [x] /sectorserver.cgi, /touchsession.jsp, /certificate.html, /updateCheck,
      /who.cgi, 404 fallback (legacy.go tryLegacy dispatch in C++ order)
- [~] AF_UNIX Ping/pong keepalive -- gated OFF (logs WARNING if
      NET7_IPC_KEEPALIVE=1; not implemented yet). Server start does not depend
      on it for the web/login path. Revisit at AQ-7 cutover.
- [x] Dockerfile + docker-compose service (alongside C++ login, not replacing).
      Runs as container-root to read the 0600 TLS key (rootless Docker:
      container-root = unprivileged host user, same as sibling `login`).
      Ports 8443:443, 8088:8080.
- [ ] CLI integration test: /AuthLogin byte-pin + ticket handoff

### AQ-2 Auction House engine (Go)
- [x] List item (PostListing: primary-live-char wallet debits 10% deposit up
      front; removes instance from vault/inventory; 8/12/24h). store_ah_write.go
- [x] Search/browse (openListings: opaque low/med/high band via computeBand,
      ordered by name/id NOT expiry so band is the only time signal; cross-DB
      item_base resolve via content pool). store_ah.go + store_items.go
- [x] Bid (PlaceBid: first bid=startBid else +max(1,ceil(1% value)); refund
      prior bidder via mailbox; FOR UPDATE row lock; high-bidder shown)
- [x] Buyout (Buyout: FOR UPDATE, full payment, item to buyer mailbox)
- [x] Expiry sweeper (sweepExpiredAuctions/resolveOneExpired): sold->item to
      winner + proceeds to seller mailbox; unsold->95% deposit + item back to
      seller mailbox. sweepers.go ticks every 5min + once at boot.
- [x] Deleted-seller listings stay live (avatarNames resolves soft-deleted
      avatars; status filter is on listing, not seller)
- API wired: handlePostListing/handleBid/handleBuyout in api.go.
  NOTE: offline-guard NOT yet enforced on the write path -- AQ open question #1
  (default offline-guard) still needs the online-status check added before any
  real-client use. TODO tracked here.

### AQ-3 Mailbox (Go)
- [x] List per account (accountMail: subject/recipient/sender/has-item/read,
      newest first, expiresInDays). store_mail.go
- [x] Open -> body + mark read (MarkRead); attachment squares (attachmentsFor)
- [x] Loot item attachment -> recipient char vault (LootAttachment ->
      freeVaultSlot; recipient-if-live-else-primary). store_mail_write.go
- [x] Loot credits -> recipient char credits (deliverCredits / wallet)
- [x] 90-day expiry sweeper (sweepExpiredMail DELETE expires_at<now) + warning
      flag (expiresInDays surfaced in MailView for the UI).
- API wired: handleMarkRead/handleLoot in api.go. RUNTIME-VERIFIED: bootstrap
  returns {mail:[],listings:[],vault:{},myListings:[]} cleanly for an account
  with no characters. Same offline-guard caveat as AQ-2.

### AQ-4 Bots (FREYA_AH_BOTS=1)
- [ ] Create "AhBot" account/avatar on first run
- [ ] Daily ~128 listings with the rarity/quality/price distribution above
- [ ] Idempotent / rate-limited so it doesn't flood on restart

### AQ-5 React + TS SPA
- [x] Import Freya Online.html design (fetched via owner-provided API URL,
      extracted to `freya/online/design/`; all .jsx/.css read in full)
- [x] Port to real React 18 + TS + Vite under `freya/online/web/` -- tokens.css
      /app.css/screens.css copied verbatim; every .jsx -> typed .tsx; CSS
      variable styles cast via a `Vars` helper. `npm run build` clean (tsc
      strict, 42 modules, 0 errors).
- [x] Login art page (procedural starfield + planet rim) -> async onSignIn
- [x] Mailbox tab + Auction House tab (Buy/Sell), opaque low/med/high band
- [x] Always-on ONLINE/OFFLINE + Players:X (api.fetchStatus, 60s poll, no SSR)
- [x] Rarity-by-quality rule lives in `src/lib/rarity.ts` (level-capped),
      applied per-listing in the mock so it obeys the spec, not the design's
      hand-picked rarities. Server must mirror this.
- [x] API client (`src/api.ts`): fetch + `credentials:'include'`, VITE_MOCK
      default-on so the SPA renders standalone until the Go backend lands.
- [ ] Wire to the real Go JSON API (flip VITE_MOCK=0) -- needs AQ-1/2/3.
      Backend now live; flip + browser-verify the real login->bootstrap path.
- [x] Serve built dist/ from the Go binary at the index (spa.go static serve +
      SPA fallback; Dockerfile bakes web/dist -> /app/web). RUNTIME-VERIFIED:
      GET / returns the built index.html.
- Note: `npm audit` flags the esbuild dev-server advisory (GHSA-67mh-4wv8-2f99),
  dev-server only, NOT in the shipped static build; fix is a breaking vite@8
  bump -- deferred deliberately.

### AQ-6 In-game notify
- [ ] On player login, if unread mail: private system chat message with the
      website URL. (Server-side -- governed by Server integrity rules; cite
      the chat opcode path. Likely a 0x001D system message to that player only.)

### AQ-7 Cutover & cleanup
- [ ] Real-client login through Go server confirmed (plans/29)
- [ ] Delete C++ login-server (Net7Mysql/Net7SSL) ONLY after confirmed
- [ ] docs/ pages; decisions-log entry

## Open questions for the owner
1. Offline-guard vs route-through-server for web inventory mutations (default:
   offline-guard). Affects whether players can manage AH/mail while in-game.
2. Mailbox is per-account but credits/items are per-character -- attachments
   loot into `recipient_avatar_id`. Confirm that's the intended target.
3. Deleting the C++ login server: confirmed yes, but only after real-client
   cutover. Until then both run.
