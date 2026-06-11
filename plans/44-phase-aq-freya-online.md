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
- [x] Find/confirm online-status signal for the offline-guard -- it is the
      server's own avatar_info.last_login > last_logout (AccountManager.h;
      crash-robust via the boot reset in ServerManager.cpp). Implemented in
      assertAvatarOffline (store_ah_write.go).

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
- [x] OFFLINE-GUARD ENFORCED (AQ open question #1 resolved, commit pending):
      assertAvatarOffline gates PostListing(seller)/PlaceBid(bidder)/
      Buyout(buyer); online = server's own last_login>last_logout
      (AccountManager.h; crash-robust via ServerManager.cpp boot reset);
      SELECT ... FOR UPDATE serializes vs the server's login UPDATE;
      errCharacterOnline -> HTTP 409. RUNTIME-VERIFIED 409-online/200-offline.
      Freya-side only -- no server wire change, no integrity citation needed.

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
  with no characters.
- [x] OFFLINE-GUARD ENFORCED on LootAttachment(loot-target) too -- loot adds
      credits/items to the live wallet/vault. RUNTIME-VERIFIED 409-online,
      200-offline (item lands in vault). Mailbox delivery stays unguarded by
      design (mail tables are not held in server memory).

### AQ-4 Bots (FREYA_AH_BOTS=1)  -- DONE, RUNTIME-VERIFIED (commit 16e229ad)
- [x] Create "AhBot" account/avatar -- seeded idempotently at schema-init via
      `db/postgres/freya_online_bots.sql` (reserved-but-SAFE ids account
      9000001 / avatar 45000006; unusable password_phc so login is impossible;
      all avatar_data cosmetic cols double-quoted). Inert without the flag
      (separate account, never in any player's char-select).
- [!] **2026-06-09 zone-in bug + fix (was the play-local blocker).** The
      original AhBot used sentinel ids 1000000001 on the THEORY they were
      "far above the server's avatar-id range" and thus harmless. WRONG: a
      player's on-wire GameID is `avatar_id | PLAYER_TAG` (PLAYER_TAG=1<<30) in
      a 32-bit field (`UDP_Master.cpp` ProcessHandoff casts to int32_t), and
      `avatar_id = account_id*5 + slot + 1` (login-server AVATAR_ID macro). So
      account_id MUST be <= 214748363 (avatar_id < 2^30) or the GameID's high
      bit is truncated off the wire and the master handoff logs "Unable to find
      player" forever (loading-screen hang). AhBot itself happened to work only
      because its avatar id equals its account id (1000000001 < 2^30) instead of
      going through *5. The real break: `just seed-account` /
      `seed-dev-account.sh` ran `setval(accounts_id_seq, MAX(id))`, which pulled
      the sequence UP to AhBot's 1e9 sentinel, so the next signup got
      1000000002 -> avatar 5000000011 (> 2^32) -> truncated -> dead. Fix, all
      idempotent: (1) AhBot -> account 9000001 / avatar 45000006 (formula-
      consistent, under ceiling) in `freya_online_bots.sql` + `bots.go`;
      (2) `db/postgres/account_id_guard.sql` (new, wired into BOTH compose
      schema-init blocks after freya_online.sql, before freya_online_bots.sql)
      re-ids the old AhBot AND any over-ceiling account down into the reserved
      band (re-keying every *avatar_id / *account_id column incl. AH listings),
      resyncs the sequence ignoring the reserved band, then installs CHECK
      constraints `accounts_id_gameid_fits` / `avatar_info_id_gameid_fits` so
      the DB now REJECTS an out-of-range id (the user's "why didn't the DB
      error?"); same constraints added to `seed.sql` for fresh installs;
      (3) `seed-account` (justfile) + `seed-dev-account.sh` setval now excludes
      `id >= 9000001` so a real signup never resyncs to the bot sentinel.
      Applied to the local net7_user DB (AhBot 1e9->9000001/45000006,
      Starstrukk 1000000002->9000002/45000011); verified no avatar over 2^30,
      both constraints present, AH listings remapped, go build green.
      `[!]` pending: owner retests `just play-local` zone-in with the rescued
      char, and the prod path runs on next `cd deploy/do && just update`.
- [x] ~128 listings with the rarity/quality/price distribution -- `bots.go`
      shelf-target model (128 active, +16/tick/15min). Buckets: common ~70%
      ores (cat 80/81, stack 20, no quality); uncommon ~25% equipment L1-4;
      rare ~5% equipment L5-9 + devices L1-9. Quality normal(140,25) clamp
      [80,200] for equipment only. item_value=buying_price*stack, open bid
      110%, buyout 200%. Faucet pays no deposit; proceeds -> AhBot mailbox.
- [x] Idempotent / rate-limited -- top-up reads active count first, so a
      restart with a full shelf posts nothing; per-tick batch cap prevents
      a cold-start dump.
- [x] RUNTIME-VERIFIED: FREYA_AH_BOTS=1 -> "posted 16 listing(s) (0 -> 16)";
      /api/bootstrap renders them (seller=AhBot, ores common/no-quality/band
      only, equipment quality-driven rarity correctly level-capped, bid=1.1x
      buyout=2.0x).
- Also fixed two correctness bugs on the SHARED resolve path (player AH too):
  categoryToCat mis-mapped cat 10 Weapon (1116 items) -> component; Vendor
  used item_base.price (~10x too high) instead of buying_price -- inflated
  every min-bid step and the 10% deposit by an order of magnitude.

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
- [x] Wire to the real Go JSON API -- USE_MOCK is now `VITE_MOCK==='1'`
      (real default, mock opt-in), api.ts/Dockerfile/README updated. AQ-1/2/3
      are live, so the SPA shows real data by default (2026-06-09).
- [x] Wire the WRITE path (2026-06-10). Previously login+bootstrap read real
      data but every action (bid/buyout/list/mark-read/loot) was a fake
      optimistic local-state mutation that never hit the backend. Now every
      action calls the real endpoint and re-pulls `bootstrap` as the source of
      truth: App holds a single `reload()`; Mailbox calls `markRead`
      (optimistic dot + background persist) and `lootAttachment` (await +
      reload); AuctionHouse Buy calls `placeBid`/`buyout`, Sell calls
      `postListing`. Buttons disable while a mutation is in flight. api.ts now
      surfaces the server's `{error}` body verbatim (e.g. 409 "character is
      online ...", 402 "insufficient credits") instead of a bare status code.
      Removed the now-dead optimistic setters (setListings/setVault/
      setMyListings/onCharge) and the fake random listing-id generator.
- [x] Serve built dist/ from the Go binary at the index (spa.go static serve +
      SPA fallback; Dockerfile bakes web/dist -> /app/web). RUNTIME-VERIFIED
      (2026-06-10): rebuilt freya-online image serves the new bundle
      (index hash matches the local `npm run build`), `/api/status` responds,
      unauth `/api/bootstrap` -> 401.
- Note: `npm audit` flags the esbuild dev-server advisory (GHSA-67mh-4wv8-2f99),
  dev-server only, NOT in the shipped static build; fix is a breaking vite@8
  bump -- deferred deliberately.

### AQ-9 Test suites (2026-06-09)
- [x] Go backend integration tests against the live two-DB Postgres
      (`freya/online/server/*_integration_test.go` + `itest_*_test.go`),
      gated on `FREYA_TEST_DB` so plain `go test ./...` runs only unit tests.
      Cover: Argon2id auth + accountID, liveCharacters/credit-sum, PostListing
      10% deposit debit + vault removal, the FOR-UPDATE offline-guard (store +
      HTTP 409), bid prior-bidder refund, buyout item/proceeds delivery, mail
      loot credit. API-level test drives the real `session.LoadAndSave(routes())`
      handler over TLS httptest with a cookie jar: login(bad)->401,
      login(good)->200 + cookie, bootstrap->real characters/credits/vault.
      Each test seeds + wipes a reserved id band (testIDBase).
- [x] HTTP-level mutation flow tests (2026-06-10) pinning the exact routes /
      JSON shapes / status codes the wired SPA depends on: post-listing ->
      cross-account buyout -> item in buyer mailbox + gone from browse; mail
      mark-read -> loot -> wallet grew on next bootstrap. 19 integration +
      7 unit tests pass.
- [x] Web SPA unit tests (`freya/online/web/src/**/*.test.ts`, vitest): rarity
      tier+level-cap table (mirrors server/rarity.go), bid/buyout/min-increment
      math, and the real-vs-mock api.ts dispatch (default GETs/POSTs with
      `credentials:'include'`; VITE_MOCK=1 serves mock without fetch). 16 tests.
- [x] justfile: `test-online-it` (docker postgres up + FREYA_TEST_DB go test)
      and `test-online-web` (npm test).

### AQ-10 Account tab -- password reset (2026-06-09)
- [x] Third SPA tab "Account" (after Mailbox + Auction House), visible only when
      logged in. Reset Password form: current password, new password x2, min
      length 8, confirm-match + differ-from-current checks client-side, themed
      with the existing `.field`/`.hud-panel`/`.btn--primary` classes
      (`web/src/screens/Account.tsx` + `.account*` CSS). Account name comes from
      the session cookie, never typed/sent.
- [x] `api.resetPassword(current,new)` -> `POST /api/account/password`
      (credentials:'include', no username in body).
- [x] Go `POST /api/account/password` (`handleChangePassword`): requires the
      session acctID (401 unauth), re-verifies the CURRENT password (401 wrong),
      enforces new length >= 8 server-side (400), hashes the new password, and
      UPDATEs `accounts.password_phc` (parameterized `updatePassword`). Rotates
      the session token after the change.
- [x] `hashPassword` (auth.go) emits the SAME libsodium Argon2id PHC the game
      login verifies (m=65536,t=2,p=1, argon2id v=19, crypto/rand 16-byte salt,
      32-byte hash, RawStdEncoding) -- byte-compatible with `just seed-account`
      (PyNaCl) and the C++ `crypto_pwhash_str_verify`. So a website reset works
      for BOTH the website and the game client. This is NOT a server wire change
      (no game protocol touched) -- it writes the same credential store the C++
      login already reads.
- [x] Tests: Go `TestHashPassword` (format + round-trip + random-salt), Go
      `TestIT_API_ChangePassword` (unauth 401 / wrong-current 401 / short 400 /
      success 200, then OLD pw fails login + NEW pw succeeds), web vitest
      (resetPassword posts current+new without username; surfaces server error).
      Full suite green: 20 Go integration + 8 Go unit + 18 web.

### AQ-11 Vault transfer + Send mail + dup-safe transactionality (2026-06-10)
- [x] **Vault transfer page (SPA `web/src/screens/Vault.tsx`, new "Vault" tab,
      glyph ⊟).** Two character dropdowns (left/right) that must pick two
      DIFFERENT characters you own (each side disables the other's pick). Click
      an item on the left arms `Transfer ->` (into the right vault); click on the
      right arms `<- Transfer`. `VAULT_SLOT_COUNT = 96` const lives in BOTH
      `web/src/types.ts` (client) and the Go server (`vaultSlotCount` in
      `store_mail_write.go`). Shows "need at least two characters" when
      `avatars.length < 2`. Bootstrap gained `vaultStorage map[string][]VaultSlotView`
      (`accountVaultStorage`) so each owned char's full vault is available client-side.
- [x] **`POST /api/vault/transfer` (`handleVaultTransfer` + `TransferVaultItem`
      in `store_vault_write.go`).** Validates: both chars owned by the session
      account (acctID from cookie, never body), not the same char
      (`errSameCharacter`), item exists in the named source slot with the
      expected item id (`lockVaultSlot` does `SELECT ... FOR UPDATE` + id
      cross-check -> `errItemNotFound`), destination has a free slot
      (`freeVaultSlot`, else `errDestVaultFull`). The whole move runs in ONE
      `serialTx` (SERIALIZABLE + 40001/40P01 retry): lock source row, insert into
      lowest free dest slot, DELETE source row. Offline-guard on BOTH chars
      (sorted ascending to avoid deadlock) -- a web vault mutation of an ONLINE
      char is refused (the game server owns its vault in memory).
- [x] **Send mail from the Mailbox tab (`Compose` in `web/src/screens/Mailbox.tsx`
      + `POST /api/mail` -> `SendMail` in `store_mail_send.go`).** Subject required
      (`errNoSubject`, <=128), To = a player name, body OR up to 6 items (both
      allowed; empty body && no items -> `errEmptyMail`). Cannot send to the
      SENDING char's own name but CAN send between two chars you own
      (`from==to -> errSameCharacter`). Items are picked from the sender's VAULT
      only (AH-sell-style grid). An included item is REMOVED from the vault into
      mail-slot storage inside the same `serialTx`: per item `lockVaultSlot` +
      DELETE, then `insertMessage` + `insertItemAttachment(kind=0)`. Pre-tx
      `resolveItems` rejects no-trade items (`errItemUntradable`) and dup slots
      (`errDupMailSlot`). `MAX_MAIL_ITEMS = 6` const client + server.
- [x] **All web mutation engines made SERIALIZABLE + offline-guarded.** AH
      PostListing/PlaceBid/Buyout/expiry and mailbox LootAttachment were converted
      to run inside `serialTx` (so inventory/vault -> AH and mailbox looting carry
      the same dup-safety + ownership guard as the new vault/mail paths).
- [x] **C++ server vault-opcode atomicity (the in-game half of the same
      request).** HONEST framing: at the C++ layer there is NO concurrency race
      (one client per char, in-memory swap under `m_Mutex`, single FIFO
      `SaveManager` thread). The real defect was CRASH-ATOMICITY: a vault move
      emitted TWO independent `SaveInventoryChange`/`SaveVaultChange` messages,
      each its own autocommit DB write, so a crash between the two commits dupes
      (item in both slots) or loses (item in neither) on next login. Fix:
      - `server/src/db/sqlplus.{h,cpp}`: added `begin()/commit()/rollback()/
        in_transaction()` to `sql_query_c`. Between begin/commit every
        execute*/run_query* on that query joins ONE `pqxx::work` on the borrowed
        pooled connection and defers its commit; the destructor rolls back a
        dangling open tx so a leaked transaction can't poison the pool.
      - `server/src/SaveManager.{h,cpp}`: new `SAVE_CODE_MOVE_INVENTORY` (0x0031)
        carrying TWO 86-byte slot records; `HandleMoveInventory` upserts both in
        ONE transaction (`UpsertInventorySlot` refactored out of
        `HandleChangeInventory` and shared). On a begin() failure it degrades to
        two independent writes (reopens the window, never loses the change); on
        any write failure it rolls back and logs.
      - `server/src/PlayerSaves.cpp` + `PlayerClass.h`: `SaveInventoryMove(from_type,
        from_slot, to_type, to_slot)` packs both slots into one move message
        (`PackInventorySlot` mirrors the existing record layout AND the cargo-slot
        `OBTAIN_ITEMS` mission-check side effect exactly).
      - `server/src/PlayerConnection.cpp` `HandleInventoryMove`: the cargo->vault
        (1->3), vault->cargo explicit (3->1), and vault->vault (3->3) branches now
        emit ONE `SaveInventoryMove` instead of two separate save calls.
      RESIDUAL GAP -- CLOSED in AQ-11b (2026-06-11): the vault->cargo auto-stack
      path (ToSlot == -1) now ALSO commits atomically. The fixed 2-slot move
      message was generalised to a variable-length N-slot message:
      - `SaveManager.h`: `SAVE_CODE_MOVE_INVENTORY` payload is now N back-to-back
        86-byte records (not exactly two); `SAVE_MOVE_MAX_RECORDS = 15` caps it to
        what one save message buffer holds (floor((1306-12)/86)).
      - `SaveManager::HandleMoveInventory` loops `count = bytes/86` UPSERTs inside
        ONE transaction (validates bytes%86==0; begin-fail still degrades to
        independent writes; any slot failure rolls back the whole batch).
      - `Player::CargoAddItem(_Item*, std::vector<int>* touched_slots=nullptr)`:
        when `touched_slots` is non-null it APPENDS each cargo slot it would have
        saved instead of saving it independently (all other callers pass nullptr
        and keep the old behaviour). `Player::SaveInventoryMoveSlots(refs, n)`
        packs the emptied vault source + every touched cargo slot into one atomic
        move. The pathological >14-cargo-slot spread (needs that many partial
        stacks of the identical item already in cargo) falls back to per-slot
        saves, logged.
      This is NOT a wire-behaviour change: no opcode/packet/DB-content changed,
      only the transaction GROUPING of two existing writes. So the CLI-byte-pin +
      plans/29 wire gate does not strictly apply; a light real-client sanity check
      is tracked anyway as **CV-AQ-VAULT-1** (plans/29) since it touches inherited
      item persistence.
- [x] **Tests.** Go: 18 new integration tests in
      `freya/online/server/store_vault_mail_test.go` (transfer + send-mail happy
      paths, ownership/same-char/offline/no-trade/full-vault rejections, and
      CONCURRENT dup-safety: two goroutines moving the SAME source slot -> exactly
      one wins, total item count across vaults stays exactly 1). C++: 4 new gtests
      in `freya/tests/server/db/sqlplus_wrapper_test.cpp` proving commit persists
      both writes, rollback discards, a failed second write leaves NOTHING (the
      dup/loss scenario), and the destructor cleans a dangling tx so the pooled
      connection stays usable. Server builds clean (167/167, -Wall -Wextra);
      all 8 wrapper gtests green against live Postgres; web typecheck + 20 vitest +
      production build clean.
- [x] **AQ-11b variable-length move tests (2026-06-11).** 3 more gtests in
      `sqlplus_wrapper_test.cpp` (`SqlplusWrapperTx`): an N-slot (5-record) move
      commits all-or-nothing; a partial failure on the last of N slots rolls back
      EVERY slot (no half-applied wide move); the cap-sized 15-record batch commits
      as one transaction. Server rebuilt no-cache: clean, no new warnings at any
      edited line range; all 7 `SqlplusWrapperTx` + 11 total wrapper gtests green
      against live Postgres (port 5434).

### AQ-12 Profile tab (2026-06-11)
- [x] **Profile tab -- first tab, per-avatar character sheet.** New website tab
      (before Mailbox) showing the selected avatar's identity, starship, and
      learned skills. Switching the topbar avatar dropdown reloads it on demand.
      - [x] Three split, ownership-scoped read APIs (`store_avatar.go`):
            `GET /api/avatar/{name}/profile` (name, race/class via race*3+prof,
            overall level = combat+explore+trade, 3 discipline bars with
            fractional progress, sector name, credits),
            `GET /api/avatar/{name}/ship` (ship_data name + avatar_level_info
            hull/cargo/thrust/warp + fitted avatar_equipment, item_id>0 so the
            -1 empty / -2 locked sentinels are excluded, resolved from the
            content catalogue), `GET /api/avatar/{name}/skills` (learned skills
            with name/category from content + per-class max-level column picked
            by classIndex). Every read joins the two pools in Go (user save-state
            + net7 content); no cross-DB statement. All values bound; identifiers
            from catalog only. Indexed lookups only (idx_avatar_info_account_live,
            PKs, sectors_pkey, skills_pkey) -- no migration needed.
      - [x] React Profile screen (`web/src/screens/Profile.tsx`): identity card
            (overall level + stylized class + sector), teal/blue/purple discipline
            bars, starship card with hull/cargo/thrust/warp + equipment tiles
            (hover popover reuses ItemDisplay tooltip), skills grouped with
            filled/empty level dots. Loads its 3 slices in parallel per selected
            avatar with an alive-guard.
      - [x] Tests: `TestIT_AvatarProfile` / `_AvatarShip` / `_AvatarSkills`
            (class/level/disciplines/credits, ship name + sentinel exclusion +
            catalogue resolution, skill name/category/level/class-max), plus
            cross-account 404 ownership negatives; `TestIT_API_Profile` (401
            unauth / 200 owned / 404 cross-account). gofmt + go vet clean, full
            `go test ./...` green, web vitest 20/20 + build clean. Website-only
            read feature -- no C++/proxy/wire change, so the Server integrity /
            CV-gate rules do not apply.

### AQ-13 Galaxy tab (2026-06-11)
- [x] **Galaxy tab -- first/default tab, live sector map.** New website tab
      (before Profile) showing every named sector in the game, grouped by star
      system and linked by the gate graph, with each sector lighting up and
      glowing by how many players are currently in it. Glow is RELATIVE-max
      normalized (count / busiest-sector count), so 1-vs-2 players reads the
      same as 50-vs-100 -- the owner's explicit requirement.
      - [x] Two split read APIs (`store_galaxy.go`): `GET /api/galaxy` (static
            topology: 25 systems with normalized faction token, 130 named
            sectors with system + faction, gate edges from
            `sector_objects.gate_to` -- deduped undirected, self-loops dropped,
            dock-gate interior targets remapped to parent then collapsed),
            cached 5min; `GET /api/galaxy/occupancy` (live per-sector online
            counts), cached 15s. Both auth-gated (401 unauth).
      - [x] Live counts derived EXACTLY like the Discord status bot
            (`status-notifier/bot.go readOnlinePlayers`): account online while
            `accounts.last_login > last_logout`, avatar online while
            `avatar_info.last_login > last_logout`, location = `avatar_info.sector`.
            Docked players (sector = starbase INTERIOR id) remapped to the parent
            sector via `starbases.sector_id`, else they light up nothing. No
            server/C++ change -- the data is fully derivable website-side, so the
            Server integrity / CV-gate rules do not apply.
      - [x] `normalizeFaction` folds freeform `systems.notes` prose into the
            stable token set (jenquai/progen/terran/pirate/contested/neutral/
            deepspace; "various"->neutral, default->deepspace) the SPA colors by.
      - [x] React Galaxy screen (`web/src/screens/Galaxy.tsx`): deterministic
            radial cluster layout (no Math.random, no real coords exist --
            galaxy_x/y are all 0), systems on a ring, sectors fanned in a
            sub-ring, gate edges as SVG lines, faction-colored nodes whose
            radius + glow halo scale with relative occupancy, hover info panel
            (sector / system / faction / live pilot count), total-online header,
            faction legend. Topology fetched once; occupancy polled every 15s
            with an alive-guard.
      - [x] Made the default + first tab in App.tsx (glyph). Tab union extended,
            Galaxy rendered before Profile.
      - [x] Tests: `TestIT_GalaxyMap` (non-empty topology, every sector has a
            known faction + name, edges self-loop-free / normalized / deduped /
            endpoints in set), `TestGalaxyNormalizeFaction` (table), 
            `TestIT_GalaxyOccupancy` (in-space + docked-remap + offline-excluded,
            interior id not its own key, total excludes offline),
            `TestIT_API_Galaxy` (401 both endpoints unauth, 200 topology, live
            count after set-online). Web: 4 new api.test.ts cases (real GET +
            mock). gofmt + go vet clean, `FREYA_TEST_DB=1 go test ./...` green,
            web vitest 23/23 + build clean.

### AQ-6 In-game notify
- [ ] On player login, if unread mail: private system chat message with the
      website URL. (Server-side -- governed by Server integrity rules; cite
      the chat opcode path. Likely a 0x001D system message to that player only.)

### AQ-7 Cutover & cleanup
- [ ] Real-client login through Go server confirmed (plans/29)
- [ ] Delete C++ login-server (Net7Mysql/Net7SSL) ONLY after confirmed
- [ ] docs/ pages; decisions-log entry

### AQ-8 PB-2 distribution -- ship the MVAS injection pair to every channel (commit 0b46f3d8)
The MVAS position-feed pair (`bin/FreyaPosFeed.dll` + `bin/FreyaInject.exe`,
PE32/i386) was built but no distribution channel carried it. They run identically
on native Windows and under WINE (FreyaInject.exe remote-thread LoadLibrary into
client.exe), so the artifacts are the SAME everywhere. Now distributed via:
- [x] Launcher injection is platform-agnostic (dropped the `!OnWindows` gate;
      FreyaInject.exe runs directly on Windows, under the wine prefix elsewhere;
      WinePathToDos short-circuits on native Windows). Launcher.cs.
- [x] `just package-client-windows` depends on `build-posfeed-dll` and stages
      both into `bin/` -> 5-file bundle. Verified: bundle has all 5 files.
- [x] Auto-updater carries them with their OWN independent hash, exactly like
      the proxy (owner decision: "it shouldnt go with launcher. it should have
      its own hash") -- an MVAS-feed-only patch reaches existing installs without
      a launcher bump. Request body is now {launcherHash, proxyHash, posFeedHash,
      injectHash}. Stored FLAT in the bucket, mapped to `bin/` on download where
      the launcher already looks (LocateInjectorExe/LocatePositionFeedDll search
      bin/ first).
      - [x] Client: UpdateCheckRequest gains posFeedHash/injectHash;
            ComputeLocalHashes hashes bin/FreyaPosFeed.dll + bin/FreyaInject.exe;
            BuildRequestJson sends all four. UpdateLogicTests updated (34 pass).
      - [x] PatcherManifest parses them as OPTIONAL add-ons (an older 3-file
            manifest still loads; no fail-closed window on deploy ordering).
      - [x] LinuxAuth HandleUpdateCheck checks each INDEPENDENTLY (posFeedOk/
            injectOk; an unpublished file == up-to-date, never offered) and ships
            each on its own mismatch, in its own block beside the proxy's.
      - [x] Push-ClientPatch.ps1 builds/hashes/uploads/invalidates them
            generically off the $artifacts list -- CloudFront invalidation paths
            are derived from $artifacts so both MVAS keys are invalidated.
- [x] login-server C++ + launcher both compile clean; 34 launcher tests pass.
- [x] **2026-06-09 online MVAS was dead despite shipping the pair -- the feed
      was disabled by default.** The artifacts shipped, but `UsePositionFeed`
      defaulted to FALSE (`UserSettings.cs`), there is NO UI checkbox to flip it,
      and `package-client-windows` writes no settings.json -- so every packaged
      player launched with the feed off and `ConfigurePositionFeedInjection`
      returned early (no injection, no 0x1004/0x3005 to the droplet). play-local
      worked only because its justfile heredoc forced `UsePositionFeed:true`;
      play-online's heredoc forced `false` and never built the DLL. Fix:
      (1) `UsePositionFeed` default -> true (inert-safe: a checkout with no DLL
      just warns and launches plain; the feed sends nothing until the engine read
      is present); (2) a version-gated one-shot migration in `UserSettings.Load()`
      (`SettingsVersion` 0->1) flips the stale persisted `false` to true on
      existing client installs exactly once -- needed because changing the default
      alone does NOT help machines that already wrote a settings.json (the owner's
      catch); (3) `play-online` now builds `build-posfeed-dll` and writes
      `UsePositionFeed:true`. The offsets header
      (`freya/client-injection/ClientEngineOffsets.h`) is committed and compiled
      into the DLL. Launcher builds clean, 34 tests still pass.
- Blast radius of `cd deploy/do && just update`: builds server/login/status-
      notifier/db-backup images + the client patch. The droplet runs
      docker-compose.PROD.yml, which references NONE of the AQ Go-online stack
      (freya-online, freya_online*.sql, FREYA_AH_BOTS, VITE_MOCK) -- so the AH/
      bots/SPA/test-accounts/mock cannot ship via this push. Only the C++ login
      /updateCheck change + the MVAS client artifacts actually deploy.
- Linux installer (client/linux-installer) needs NO change -- it only stands up
  the base client + WINE prefix; Freya artifacts reach it via bundle + updater.
- [ ] **Go /updateCheck port (legacy.go, deferred to AQ-1/AQ-7):** when the Go
      login takes over /updateCheck it must mirror this 5-file manifest set.
      Today legacy.go's handleUpdateCheck returns 503 (fail-closed), so the C++
      login serves the live updater during coexistence -- the C++ edits above are
      the ones that actually ship the pair.

## Open questions for the owner
1. [RESOLVED -- shipped the offline-guard] Offline-guard vs route-through-server
   for web inventory mutations. Decision: offline-guard (the safe default that
   needs no server change). Players manage AH/mail only while the character is
   logged out; the UI gets HTTP 409 + "log it out" otherwise. The seamless
   route-through-server-over-IPC alternative stays deferred (large server
   addition, integrity-gated). Revisit only if the owner wants in-game mgmt.
2. Mailbox is per-account but credits/items are per-character -- attachments
   loot into `recipient_avatar_id`. Confirm that's the intended target.
3. Deleting the C++ login server: confirmed yes, but only after real-client
   cutover. Until then both run.
