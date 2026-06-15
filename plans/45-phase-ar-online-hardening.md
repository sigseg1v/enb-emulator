# Phase AR -- Freya Online auth/abuse hardening

Security-hardening follow-ups for the Go `freya/online/` service (Phase AQ).
None of these block the AQ cutover gate, but the login-flood item (AR-1) SHOULD
land before `freya-online` ever fronts game auth in production (AQ-7), because it
is the one finding with a denial-of-service consequence.

**Status 2026-06-14: AR-1, AR-2, AR-5 landed.** AR-1 was implemented across BOTH
auth binaries (the AQ-7 cutover split game-auth into net7go and the website
login into freya-online): a per-IP token bucket + a global Argon2id concurrency
cap on each. The per-IP key for game-auth comes from a relay-injected, edge-
overwritten `X-Freya-Client-IP` header (net7go only ever sees the relay as its
peer), so a NAT'd household keeps per-IP buckets while a brute-forcer is bounded.
The Argon2 cap is the real memory backstop (N*64 MiB). AR-3/AR-4/AR-6 remain
as-was (AR-3 fine to leave, AR-4 dev-server-only bump when convenient, AR-6 no
action). See `plans/29` CV-AR-1 for the (low-risk) real-client check.

Source: post-AQ review of `freya/online/server/`. Each item cites the file/line
it was found at.

## Scope note (read first)

`/AuthLogin` is **game-client-facing wire** (the real `client.exe` /
`authlogin.dll` dials it through the launcher's LocalAuthRelay). `/api/login` is
**website-only** (browser session cookie, never touched by the game). Any change
to `/AuthLogin` is therefore governed by the CLAUDE.md server-integrity rules: a
throttle is a *tightening* (it only ever rejects/slows traffic), which is the
always-welcome direction, BUT it must not reject or stall a **legitimate** client
login. In particular several real players behind one NAT (shared public IP) must
still be able to log in -- a naive per-IP hard cap would lock out a household /
LAN party. Tune for "stop a brute-force/flood" not "one login per IP per minute".

## Items

### AR-1 -- Rate limiting + concurrency cap on both login paths (Medium; do before AQ-7)

- [x] Neither `/api/login` (`api.go` `handleLogin`) nor `/AuthLogin`
      (`legacy.go` `handleAuthLogin`) throttles attempts today, so credential
      brute-forcing is unbounded. -- DONE: both now throttle.
- [x] Worse: each verification runs Argon2id at 64 MiB
      (`auth.go` `phcMemKiB = 65536`), so an UNAUTHENTICATED login flood is a
      memory/CPU exhaustion vector -- a few hundred concurrent requests can eat
      tens of GB and stall the box. This is the real reason to prioritise it.
      -- DONE: capped by the Argon2 semaphore on both binaries.
- [x] Add a **per-IP rate limiter** in front of BOTH handlers. -- DONE as a
      dependency-free token bucket (`golang.org/x/time/rate` is NOT in the module
      cache and CI builds Go only via docker, so a new dep was avoided):
      `ratelimit.go` `ipLimiter` (map[ip]*tokenBucket, refill = elapsed*rate
      capped at burst, periodic GC eviction of buckets idle > 15m) in BOTH
      `login-server/net7go` (CC, game auth) and `freya/online/server` (MIT,
      website). net7go keys on a **relay-injected** `X-Freya-Client-IP`
      (`clientIP()`), set in `freya/online/server/legacy_proxy.go` from the relay's
      OWN RemoteAddr and OVERWRITTEN there so a client-supplied header can't spoof
      it; freya-online keys on RemoteAddr directly (`remoteIP()`). Defaults: 60/min
      sustained, burst 20.
- [x] Add a **semaphore capping concurrent Argon2id verifications**. -- DONE:
      `argonGate` (buffered channel of N) in both binaries; acquire() tries
      non-blocking, then waits up to 2s, then sheds with a cheap rejection (never
      queues unboundedly). Default N=4 (4*64 MiB peak). `handleChangePassword`
      (which runs Argon2 twice) also passes through the gate.
- [x] Tune `/AuthLogin` limits so a legitimate NAT'd group still logs in (see
      scope note). -- DONE: generous per-IP burst (20) + global concurrency cap is
      the real backstop, exactly as the scope note prefers. All knobs are env-
      tunable (`NET7GO_AUTH_RATE_PER_MIN`/`_BURST`/`_ARGON_MAX_CONCURRENT`,
      `FREYA_LOGIN_RATE_PER_MIN`/`_BURST`/`FREYA_ARGON_MAX_CONCURRENT`).
- [x] Tests: per-IP burst-then-throttle, disabled-when-rate<=0, stale-bucket
      eviction, Argon2 gate caps concurrency under flood, never exceeds cap, and
      client-IP header precedence/port-strip (incl. IPv6). -- DONE in
      `ratelimit_test.go` in both modules (go test green).
- [x] If `/AuthLogin` behaviour visibly changes on the wire under throttle, add a
      `plans/29` CV entry. -- DONE: throttle returns the BYTE-IDENTICAL failed-
      login response (`Valid=False`), so no new wire surface; logged CV-AR-1 as a
      low-risk real-client UX check anyway.

### AR-2 -- int64 overflow in AH item-value math (Minor / low priority)

- [x] `itemValue := vendor * int64(stack)` (`store_ah_write.go`) can
      theoretically overflow int64. -- DONE: checked multiply
      (`if vendor != 0 && itemValue/vendor != int64(stack) { return errBadInput }`)
      rejects the listing instead of wrapping, before the deposit math.

### AR-3 -- Website sessions are in-memory (Operational, not security)

- [~] Sessions use the scs default in-memory store, so every website login drops
      on a service restart. Operational annoyance, not a vulnerability. If
      persistence is wanted, back scs with a Postgres session store (the service
      already has a `net7_user` pool). -- DEFERRED (no action): explicitly fine to
      leave as-is per the item's own conclusion; would add a dependency for no
      security gain.

### AR-4 -- Vite/esbuild dev-server advisory (Upgrade when convenient)

- [~] GHSA-67mh-4wv8-2f99 affects the Vite **dev server only**, not the shipped
      `dist/` the Go binary serves, so there is no production exposure. Bump Vite
      whenever convenient (a routine `freya/online/web` dep update), not urgently.
      -- DEFERRED: dev-server-only, no prod exposure; a dep bump is network-
      fragile to do autonomously here. Left for a routine web dep update.

### AR-5 -- Prod listener hygiene at cutover (fold into AQ-7)

- [x] `FREYA_HTTP_ADDR` defaults to `:8080` (plain HTTP) and the Dockerfile
      EXPOSEs it. -- DONE: prod compose (`deploy/do/compose/docker-compose.prod.yml`)
      never published 8080 (only `443:443/tcp`), and now sets `FREYA_HTTP_ADDR=""`
      so the plain-HTTP listener does not bind at all. Required fixing a latent bug
      first: `env()` treated blank as "use default", so the empty value was
      unreachable; added `lookupOr()` (honours an explicitly-set "") and switched
      HTTPAddr to it.

### AR-6 -- SSH `accept-new` host-key handling (No action)

- [~] The deploy tooling's `accept-new` SSH host-key policy is a documented,
      reasonable tradeoff for cattle droplets (a fresh droplet has no known key
      yet). Recorded here so a future audit doesn't re-flag it as an oversight.
      -- NO ACTION (by design): intentional tradeoff, nothing to fix.
