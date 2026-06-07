# Phase AH -- DTLS + per-packet auth for the proxy<->server UDP leg

**Status: OWNER-AUTHORIZED (2026-06-07). In progress.** The owner explicitly
asked to "plan and impl DTLS" and to add a per-packet auth token so a hostile
party on a shared NAT cannot spoof commands as another player. This file is the
design + checklist. The earlier "parked / approval-gated / probably not worth
it" framing is superseded -- see "Why the threat model is NOT thin" below.

## Two separate problems, two separate layers

This phase fixes two things that are easy to conflate. Keep them distinct:

1. **Confidentiality + channel integrity (DTLS).** The proxy<->server UDP leg is
   cleartext today. An on-path observer (the hosting provider, anyone sharing the
   path) can read gameplay and, worse, *inject* datagrams. DTLS (both directions)
   encrypts and authenticates the channel: after the handshake every datagram is
   AEAD-protected and an off-path/on-path attacker cannot read or forge records
   without the session keys.

2. **Per-packet authentication of the player (auth token).** DTLS authenticates
   *the channel*, not *the account*. Because we ship the SAME proxy binary (with
   the SAME pinned server cert) to every player, a valid DTLS session only proves
   "a legitimate proxy is talking" -- it does NOT prove which account. The server
   demuxes many players over its UDP listeners by `GameID` carried in each
   packet, and `GameID` is just `CharacterID | PLAYER_TAG` -- enumerable, not a
   secret. So we add a per-packet **auth token** on the **C->S direction only**,
   carried at the front of every inbound datagram, that the server checks against
   the token it bound to that account at login. S->C does not carry the token
   (nobody is impersonating the client to itself, and DTLS already authenticates
   the server to the proxy).

DTLS without the token still lets a malicious *proxy build* impersonate any
account; the token without DTLS is sniffable and replayable. You need both.
WireGuard would cover (1) but NOT (2) for the same reason DTLS doesn't: a shared
proxy binary means tunnel-endpoint auth != account auth. The token is required
regardless of which transport encrypts the leg.

## Why the threat model is NOT thin (the impersonation hole)

The previous version of this plan called the threat model thin because the only
*secret* on this leg is login credentials, and those ride real TLS on the auth
leg (:443). That reasoning missed an integrity hole that is live right now:

- **The ticket suffix is never validated by the game server.** The login-server
  issues a ticket `username-<token>` after a successful password check
  (`login-server/Net7SSL/LinuxAuth.cpp`, `BuildTicketLocked`). The Win32 server
  then validated that ticket via a login-server -> game-server handoff
  (`RegisterSectorServer` pushed issued tickets over SSL). **The Linux port
  dropped that handoff** (see the "intentionally NOT ported" comment in
  `LinuxAuth.cpp`). The game server's `ProcessTicketInfo`
  (`server/src/UDP_Global.cpp`) splits the ticket on `-`, takes the username,
  checks the account exists / isn't banned / isn't already in use -- and never
  compares the `<token>` suffix against anything. So **any internet host can
  present `victim_username-anything` to the global UDP port and be served that
  account's avatar list / logged in** (subject only to the account-in-use check).
  No password, no sniffing, no shared NAT required.

- **In-game packets are gated on IP only.** `ProcessClientOpcode`
  (`server/src/UDP_Client.cpp`) accepts a packet if
  `GetPlayer(hdr->player_id)` resolves and `player->PlayerIPAddr() == source_addr`.
  No port check, no token. On a shared NAT (gaming cafe, CGNAT, same household)
  the source IP matches the victim, so a sniffed-or-guessed `GameID` lets you
  spoof chat / inventory / movement as them. This is exactly the owner's cafe
  scenario, and it works.

So the motivation is integrity/impersonation, not confidentiality. The fixes:

- **CSPRNG ticket** (done, AH-0): the suffix is now a 16-byte kernel-CSPRNG draw
  (libsodium `randombytes_buf` -> hex), not glibc `rand()`. Predicting it from one
  observed ticket or the boot time is no longer possible. Necessary but not
  sufficient -- useless until the server actually checks it.

- **Server-side token validation + per-packet token** (AH-8..AH-10): the server
  must bind the presented token to the player at login and require it on every
  C->S packet, dropping + warning on mismatch.

## How DTLS would work here

DTLS is TLS adapted to datagrams (UDP). Same X.509 / PKI cert model, same crypto
shape as TLS:

- **Asymmetric handshake, symmetric bulk.** The handshake uses the cert's
  asymmetric key (RSA or ECDSA) to authenticate the endpoint and run an ephemeral
  ECDHE key exchange; that derives a shared **symmetric** session key, and every
  datagram after is AEAD-encrypted (AES-GCM / ChaCha20-Poly1305).
- **OpenSSL is already linked** in both the server and the proxy (OpenSSL 3.x,
  Phase E/O). DTLS is `DTLS_method()` (DTLS 1.2, RFC 6347) or DTLS 1.3 (RFC 9147)
  instead of `TLS_method()`, plus the datagram BIO wiring. No new third-party dep.

### Cert strategy: the real axis is "pin the key" vs "CA + hostname"

There is NO technical blocker to reusing the existing Let's Encrypt cert (the one
already serving the auth/web endpoint) for DTLS. OpenSSL DTLS uses the identical
`SSL_CTX` cert/key plumbing as TLS; an LE cert validates against the system trust
store with no custom callback. So "reuse LE" is the *simpler* option and is the
right call under one condition. The decision is NOT "LE vs self-signed" -- it is
**validate-by-CA+hostname vs pin-the-key**, and it hinges on how the proxy
addresses the server:

- **DECIDING FACTOR -- name vs IP.** The redirect/handoff flow traffics in IP
  addresses (`ServerRedirect.m_IpAddress`, `inet_addr`), not hostnames. If the
  proxy dials the DTLS endpoint **by IP**, standard LE hostname validation FAILS
  (the cert is bound to the DNS name, and LE will not SAN an arbitrary/private
  IP). If the leg can instead be **name-addressed** with the LE-covered hostname,
  LE + trust-store validation is clean and is the preferred choice -- no pin
  machinery, no self-signed cert.

- **Pinning is strictly stronger but the threat is thin.** A pin defeats CA
  mis-issuance (any of ~150 roots can otherwise issue for our name); since we
  control both ends that CA flexibility is pure downside. But CA mis-issuance is a
  thin threat for a hobby preservation server, so "pinning is stronger" is true,
  not decisive.

- **Do NOT combine LE + a pin.** LE rotates every 90 days and certbot generates a
  fresh keypair per renewal by default, so a pinned SPKI breaks on every renewal
  unless you force `--reuse-key` or pin the CA. It is either *LE + trust-store
  validation (no pin)* or *self-signed + pin* -- never a mix.

- **Key isolation, minor.** Reusing the LE cert loads the web/auth server's
  production private key into the game-server DTLS context. Same host: fine.
  Different host: it copies that key onto another box. A dedicated cert (self-
  signed, or a separate LE name) keeps the web key isolated.

**Recommendation:** if the DTLS leg can be name-addressed with the LE hostname,
**reuse LE with standard CA validation** -- simplest, no renewal-vs-pin conflict.
If the leg stays IP-addressed (what the current redirect flow implies), use a
**long-lived self-signed cert + SPKI pin** (a self-signed cert sidesteps the
90-day-renewal-breaks-the-pin problem). Decide this in AH-1 by first settling
whether the proxy reaches the DTLS endpoint by name or by IP. Either way the cert
layer is NOT what carries account auth -- the per-packet token is -- so optimize
the cert choice for operational simplicity, not crypto purism.

- **Server auth only is enough** (proxy verifies the server cert, pinned or via
  CA). Mutual DTLS (server also demands a proxy client cert) does NOT solve the
  account-auth problem -- every player has the same proxy, so a client cert
  shared across the player base authenticates the *binary*, not the *account*.
  The per-packet token is what binds to the account; skip mutual DTLS for v1.

## Implementation checklist

Do them in order; verify the integration suite stays green at every step.

- [x] **AH-0. CSPRNG ticket.** `LinuxAuth.cpp` `BuildTicketLocked`: ticket suffix
  is a libsodium `randombytes_buf` 16-byte draw rendered as 32 hex chars, no `-`,
  so the server's `strtok(ticket, "-")` still splits username from token. (Commit
  pending.)

- [x] **AH-1. Decide endpoints + cert strategy + key material. DECIDED BY OWNER
  2026-06-07: reuse Let's Encrypt PKI + NAME addressing** (connect-by-IP, verify
  cert against `g_DomainName` via `SSL_set1_host`). No self-signed cert, no SPKI
  pin. Investigation (2026-06-07): the
  proxy connects the UDP game leg by **IP** (from the server redirect --
  `SectorServerManager::LookupSectorServer` -> `ServerRedirect.m_IpAddress`,
  `inet_addr`), but it already holds a domain name `g_DomainName` (used today to
  resolve the auth/SSL leg, `ServerManager.cpp:423` `gethostbyname`). So we do NOT
  need self-signed: connect the DTLS BIO to the redirect IP and call
  `SSL_set1_host(ssl, g_DomainName)` so OpenSSL validates the server's LE cert
  against the NAME it actually covers (connect-by-IP, authenticate-by-name -- the
  CDN pattern). No pin, no renewal-vs-pin conflict, no new key material. **Valid
  precondition:** every game-server UDP endpoint the proxy dials (auth, sector,
  global, MVAS) must be covered by `g_DomainName`'s cert -- TRUE in the current
  single-host docker deploy (all ports are `enb.sigsegv.land`). Server side just
  points the DTLS `SSL_CTX` at the existing LE cert/key. Fall back to self-signed +
  SPKI pin ONLY if sector servers are later split onto separate hosts/IPs outside
  that one cert name (then a wildcard/multi-SAN LE cert is the other option).
  Document the chosen path in `docs/17-traffic-and-ports.md`.

- [ ] **AH-2. Server side: DTLS-wrap the externally-reachable UDP listeners.**
  The server binds multiple UDP ports (MVAS 3806, sector 3501-3800, master 3808,
  global 3810; see `common/include/net7/Ports.h`). Each datagram socket the
  remote proxy dials needs a DTLS layer (`SSL_CTX` with `DTLS_method()`, per-peer
  `SSL` over a datagram BIO, cookie-exchange anti-DoS via `DTLSv1_listen`). Scope
  which ports the remote proxy actually dials vs. which stay docker-internal --
  only the externally-reachable ones need DTLS; do not wrap a loopback/internal
  socket for nothing.

- [ ] **AH-3. Proxy side: DTLS-connect with pin verification.** The proxy opens
  DTLS to the server, verifies the server cert against the pinned SPKI hash
  (custom verify callback -- NOT the system trust store), then runs the existing
  cleartext UDP framing INSIDE the DTLS channel. One source tree, two build
  targets (Linux-native + Win32-PE/WINE) -- do NOT fork it.

- [ ] **AH-4. CLI / integration-suite escape hatch.** The CLI (`CliClient.Core`)
  and the Phase T suite speak cleartext UDP (the integration stack runs over
  loopback/docker, on a private bridge with no untrusted network). DTLS + token
  enforcement on the server's UDP ports break them unless gated. Make BOTH DTLS
  and token-enforcement a single server config flag, OFF for the docker
  integration stack and ON for the public deploy. The secure mode is the default
  for the public deploy; the cleartext mode is the explicit, loopback-only test
  topology. **Do NOT** weaken any server auth/state guard to make tooling easier
  (CLAUDE.md) -- a config-gated transport+token toggle on a private test bridge is
  a topology choice, not a loosened check.

- [ ] **AH-5. Preserve the capture workflow.** Keep the config flag OFF on the
  local-debug stack so `proxy/local-debug/` dumps stay cleartext, or wire an
  `SSLKEYLOGFILE` keylog + a Wireshark DTLS decode recipe. Non-optional: losing
  readable captures silently is a real regression on the project's core workflow.

- [ ] **AH-6. Firewall / hosting note.** Update the hosting write-up -- the
  externally-open UDP game ports are unchanged in NUMBER, just DTLS-wrapped. No
  new ports.

- [ ] **AH-7. plans/29 client-verification entry.** The END-TO-END path (real
  client.exe -> local proxy -> DTLS -> server, with the token on C->S) must be
  confirmed against the actual Win32 client before this is DONE. Add a CV-NN
  entry.

### Auth-token layer (the C->S per-packet token)

- [ ] **AH-8. Token wire envelope.** Define in `common/include/net7/` a fixed-size
  token prefix prepended to C->S datagrams *inside* the DTLS channel (so it is
  never on the wire in cleartext). The token is the binary form of the CSPRNG
  ticket suffix (16 bytes). S->C datagrams do NOT carry it. Add the struct to the
  common header so server + proxy + CLI share one definition. Pin the bytes in a
  CLI test.

- [ ] **AH-9. Server binds + validates the token.** At ticket login
  (`ProcessTicketInfo`) the server stores the presented token against the
  resolved player (closing the dropped-handoff hole at the same time). Then every
  C->S datagram on the affected listeners is checked: strip the token prefix,
  constant-time compare against the bound token for that `GameID`; on mismatch
  drop the packet and log a single rate-limited warning (do NOT log the token).
  Decide token source of truth: either (a) login-server -> game-server handoff of
  issued tokens (restores the Win32 design), or (b) a **stateless signed token** --
  login-server issues `token = nonce || HMAC(shared_secret, account||nonce||exp)`,
  game server validates the HMAC with a secret shared between login-server and
  game server, no cross-process store needed. (b) is less plumbing and no shared
  mutable state; prefer it unless there's a reason to keep a server-side store.

- [ ] **AH-10. Proxy prepends the token on C->S after DTLS is up.** The proxy
  already holds the ticket (it presents it at global login). After the DTLS
  handshake completes, the proxy prepends the token to every C->S datagram. The
  ordering constraint -- token only sent post-DTLS -- is automatically satisfied
  because the token only exists inside the established DTLS channel. Mirror in the
  CLI for the gated/cleartext test path only if AH-4 keeps tests cleartext (then
  the CLI sends no token and the server, with enforcement off, requires none).

## Hard rules that still bind

- DTLS itself is a **transport wrapper** -- the bytes inside the channel are the
  same cleartext UDP frames as today, EXCEPT the deliberate C->S token prefix
  (AH-8), which is a real wire-format addition on the proxy<->server leg and must
  be mirrored in server + proxy + CLI per the "three places in sync" rule. It does
  NOT change anything the real client observes (the client never sees this leg).
- The token-enforcement + ticket-validation change (AH-9) is a server-side
  **tightening** -- it rejects inputs (unvalidated tickets, GameID spoofs) that
  the real server rejected and we currently accept. That is always-welcome under
  the server-integrity rules and needs only the primary-source citation (the
  Win32 `RegisterSectorServer` ticket handoff + `GetUsernameFromTicket` path that
  the Linux port dropped). It must NOT be gated by a dev flag that can rot on in
  prod -- the gate (AH-4) disables it ONLY for the private loopback test bridge,
  with the secure path as the public-deploy default.
- **Never loosen a server security/auth/session check** to accommodate the CLI or
  the test suite (CLAUDE.md). The DTLS/token OFF-for-tests gate is a topology
  toggle on a private bridge, which is allowed; bypassing an auth/state guard is
  not.
- One proxy source tree, two build targets. Do NOT add a second forked copy of any
  proxy translation unit.
