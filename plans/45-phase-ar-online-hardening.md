# Phase AR -- Freya Online auth/abuse hardening

Security-hardening follow-ups for the Go `freya/online/` service (Phase AQ).
None of these block the AQ cutover gate, but the login-flood item (AR-1) SHOULD
land before `freya-online` ever fronts game auth in production (AQ-7), because it
is the one finding with a denial-of-service consequence.

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

- [ ] Neither `/api/login` (`api.go` `handleLogin`) nor `/AuthLogin`
      (`legacy.go` `handleAuthLogin`) throttles attempts today, so credential
      brute-forcing is unbounded.
- [ ] Worse: each verification runs Argon2id at 64 MiB
      (`auth.go` `phcMemKiB = 65536`), so an UNAUTHENTICATED login flood is a
      memory/CPU exhaustion vector -- a few hundred concurrent requests can eat
      tens of GB and stall the box. This is the real reason to prioritise it.
- [ ] Add a **per-IP rate limiter** (`golang.org/x/time/rate`, a small
      `map[ip]*rate.Limiter` with periodic eviction, or an equivalent
      token-bucket) in front of BOTH handlers. Key by remote IP; honour the
      relay's forwarded-for if the prod topology puts a proxy in front (verify
      what the LocalAuthRelay / any reverse proxy sets before trusting a header
      -- a spoofable XFF is worse than none).
- [ ] Add a **semaphore capping concurrent Argon2id verifications** (a buffered
      channel of N, sized to a safe fraction of box RAM: N * 64 MiB must stay
      well under available memory). This is the actual exhaustion backstop and is
      independent of the per-IP limiter (one attacker from many IPs still can't
      exceed N concurrent hashes). Requests over the cap wait briefly then get a
      cheap rejection -- they must NOT queue unboundedly (that's the DoS again).
- [ ] Tune `/AuthLogin` limits so a legitimate NAT'd group still logs in (see
      scope note). Prefer a generous per-IP burst + a global concurrency cap over
      a tight per-IP rate.
- [ ] Tests: a flood of N+K concurrent `/AuthLogin` requests never exceeds N
      in-flight Argon2 calls; a per-IP burst beyond the limit gets throttled; a
      single legitimate login still succeeds under load. Add to the Go suite.
- [ ] If `/AuthLogin` behaviour visibly changes on the wire under throttle
      (e.g. a new status / delay the real client sees), add a `plans/29` CV
      entry. A pure 429/slowdown that the client already tolerates on a busy
      server probably needs none -- confirm what the client does on a throttled
      auth response first.

### AR-2 -- int64 overflow in AH item-value math (Minor / low priority)

- [ ] `itemValue := vendor * int64(stack)` (`store_ah_write.go:236`) can
      theoretically overflow int64, but only with absurd catalogue prices, so
      low priority. Add a bounds/`math.MaxInt64`-aware guard (or a checked
      multiply) and reject the listing rather than wrap. Cheap; do it when next
      in that file.

### AR-3 -- Website sessions are in-memory (Operational, not security)

- [ ] Sessions use the scs default in-memory store, so every website login drops
      on a service restart. Operational annoyance, not a vulnerability. If
      persistence is wanted, back scs with a Postgres session store (the service
      already has a `net7_user` pool). Decide whether it's worth the dependency;
      this is fine to leave as-is.

### AR-4 -- Vite/esbuild dev-server advisory (Upgrade when convenient)

- [ ] GHSA-67mh-4wv8-2f99 affects the Vite **dev server only**, not the shipped
      `dist/` the Go binary serves, so there is no production exposure. Bump Vite
      whenever convenient (a routine `freya/online/web` dep update), not urgently.

### AR-5 -- Prod listener hygiene at cutover (fold into AQ-7)

- [ ] `FREYA_HTTP_ADDR` defaults to `:8080` (plain HTTP, `config.go:74`) and the
      Dockerfile EXPOSEs it. Do NOT publish 8080 in the prod compose `ports:`
      (the DO firewall would block it anyway, but defence in depth) and set
      `FREYA_HTTP_ADDR=""` in prod so the plain-HTTP listener does not bind at
      all -- prod terminates TLS itself on 443. This is a cutover checklist item;
      it lands with AQ-7, not before.

### AR-6 -- SSH `accept-new` host-key handling (No action)

- [ ] The deploy tooling's `accept-new` SSH host-key policy is a documented,
      reasonable tradeoff for cattle droplets (a fresh droplet has no known key
      yet). Recorded here so a future audit doesn't re-flag it as an oversight.
      No change planned.
