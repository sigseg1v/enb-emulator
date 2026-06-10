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
      `UsePositionFeed:true`. The offsets header (`ClientEngineOffsets.local.h`)
      stays gitignored and is NOT committed -- it is baked into the DLL at package
      time on the build machine; committing it would leak client memory layout
      (CLAUDE.md disclosure rule). Launcher builds clean, 34 tests still pass.
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
