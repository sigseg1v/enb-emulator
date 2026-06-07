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

### Architecture reality (mapped 2026-06-07) -- the per-peer state model

Both sides use ONE socket multiplexing many peers, NOT connected-per-peer. DTLS
needs one association per `(socket, peer addr:port)`, so a naive "wrap the fd"
does NOT fit. The concrete shape:

- **Server:** every listener is one `SOCK_DGRAM` socket; `RunRecvThread`
  (`server/src/UDPConnection.cpp:174-253`) blocks in `recvfrom` via `UDP_RecvS`
  (`:302-346`, returns `source_addr`+`source_port`), then dispatches by connection
  type (`:212-233`) to `HandleClientOpcode`/`HandleGlobalOpcode`/`HandleMVASOpcode`/
  `HandleMasterOpcode`. Send is `UDP_Send` (`:276-295`) `sendto` to the per-player
  stored `m_Player_IPAddr`/`m_Player_Port` (`Player::SetPlayerPortIP`,
  `PlayerConnection.cpp:78`). Socket create+bind at `UDPConnection.cpp:40`/`:58`.
- **Proxy:** master plane = ONE *connected* socket to `:3808`
  (`UDPClient_linux.cpp:225`); global plane = ONE *unconnected* socket that by
  design receives from TWO server ports, `:3810` AND `:3806`
  (`UDPClient.h:28-35`, `UDP_RecvFromServer` `:453-489`, source-IP filtered but
  NOT port-filtered). Send via `UDP_Send` (`:543-583`): connected `send` (master)
  or `sendto` (global / explicit port). Server IP comes from
  `ResolveGameServerIP` (`:125-160`) via `NET7_GAME_SERVER_HOST` -> `getaddrinfo`.

**The crux: the global plane is two DTLS associations on one socket** (server:3810
+ server:3806). So the transport shim must key DTLS state by peer `(ip,port)`, not
by socket. Build a small `DtlsTransport` helper used by BOTH sides:
  - holds `SSL_CTX` + a `map<peerkey, SSL*>` where peerkey = `(ip,port)`;
  - each `SSL` gets a memory-BIO pair (`BIO_s_mem`) -- we pump bytes manually
    between `recvfrom`/`sendto` and the BIOs (the standard single-socket
    multi-peer DTLS pattern; avoids `BIO_new_dgram`'s connect-per-peer model);
  - `feed(datagram, peer)` -> `BIO_write` to rbio -> `SSL_read` -> cleartext (or
    drives the handshake, including server-side `DTLSv1_listen` cookie exchange);
  - `send(cleartext, peer)` -> `SSL_write` -> `BIO_read` wbio -> `sendto`.
  This isolates ALL DTLS mechanics from the existing opcode dispatch, which keeps
  consuming cleartext exactly as today.

- [x] **AH-2. Server side: DTLS-wrap the externally-reachable UDP listeners.**
  DONE (commit b41f4218). `ServerManager::InitDtlsServerPolicy()` settles the
  policy once at the top of `RunMasterServer`/`RunSectorServer` (before any
  listener serves): opt-out sentinel -> `m_DtlsRequired=false` + LOUD warning;
  else resolve cert (`NET7_DTLS_CERT`/`_KEY` env, default `<g_DomainName>.cer`/
  `.pem`), probe-load, and `exit(EXIT_FAILURE)` on failure (fail-closed, no
  silent plaintext). `MakeServerDtlsTransport()` hands each listener a fresh
  server-role transport (one socket == one peer map). `UDP_Connection` attaches
  `m_Dtls` in `StartReceiver()` (post-policy), routes inbound through `Feed()`
  (handshake via `RawSendTo`, `DispatchDatagram` per decrypted record) and
  `UDP_Send` through `SendApp()` (drops if no association -- never cleartext
  gameplay while DTLS on). Transport-level `std::mutex` added for the concurrent
  recv/send-thread `SSL` access. 4/4 unit tests pass; 3 server TUs syntax-clean.
  AH-2a cookie-exchange (`DTLSv1_listen`) anti-DoS still TODO (interim guard:
  `SetMaxPeers` half-open cap).
  Add the `DtlsTransport` (server role) per listener. **Policy: DTLS REQUIRED by
  default, fail-closed.** At startup the server calls `DtlsPlaintextOptedOut()`
  (`common/DtlsTransport.h`, checks `NET7_DTLS_ALLOW_PLAINTEXT` ==
  `i-accept-unencrypted-udp` byte-for-byte):
    - **NOT opted out (default):** build the `DtlsTransport`, `LoadServerCert` the
      LE cert/key (`SSL_CTX_use_certificate_chain_file` + `..._PrivateKey_file`,
      mirror `login-server/Net7SSL/SSL_Listener.cpp:40-91`). If `!Ok()` (missing/
      bad cert, CTX error) -> log `FATAL` + `exit(non-zero)`. **No silent plaintext
      fallback** -- the process FAILS TO START.
    - **Opted out (sentinel set):** run the listeners cleartext exactly as today,
      but print a LOUD multi-line startup warning ("UDP ENCRYPTION DISABLED ...
      every gameplay packet is plaintext and forgeable") so it can never happen
      unnoticed.
  Hook at the `UDP_RecvS` return in `RunRecvThread` (`UDPConnection.cpp:197`): when
  DTLS is on, route the raw datagram through `DtlsTransport::Feed` and only dispatch
  the decrypted `app_data`; route `UDP_Send` (`:291`) through
  `DtlsTransport::SendApp`. Cookie-exchange (`DTLSv1_listen`) anti-DoS is the
  AH-2a follow-up; interim guard is the transport's `SetMaxPeers` half-open cap.
  Scope: only the externally-reachable listeners (sector/global/MVAS/master that
  the remote proxy dials) -- do not wrap a purely docker-internal socket for
  nothing. OpenSSL is already linked (`server/CMakeLists.txt:79`/`:181-182`). Add
  `common/DtlsTransport.cpp` to `server/CMakeLists.txt` (mirror `PosixIpc.cpp` at
  `:69`).

### VERIFIED proxy<->server UDP topology (mapped 2026-06-07) -- the in-game leg is ASYMMETRIC

Traced in code (cited), NOT assumed. This corrects the earlier AH-3 sketch,
which under-specified the in-game path. Two proxy sockets, both ephemeral-bound:

- **Master socket** (`proxy/Net7.cpp:223`, `m_Unconnected=false`, `connect()`'d
  to server:3808). SENDS to: 3808 (0x2008 handoff / 0x200F comm-port), the
  **per-sector port 3501+N** (C->S in-game opcodes via `ForwardClientOpcode` ->
  `SendResponse(m_ClientPort)`, `UDPClient_linux.cpp:537`; `m_ClientPort` is the
  sector port from the 0x2009 handoff, `ClientToMasterServer.cpp:155`), and 3806
  (0x3005 keepalive). RECEIVES from: **3808 ONLY** (connected-socket recv filter).
- **Global socket** (`proxy/Net7.cpp:253`, `m_Unconnected=true`, default peer
  3810). SENDS to: 3810 (0x2002/0x2004/0x200B/0x200D account ops) and 3806
  (keepalive / NAT punch). RECEIVES from (IP-filtered, port-UNfiltered recvfrom):
  3810 (account confirms) AND **3806 (ALL in-game S->C: 0x2010/0x2016/0x201A +
  game opcodes)**. The server routes every player's S->C through
  `player->m_UDPConnection` = the 3806 MVAS listener (`UDP_Master.cpp:96`
  `SetUDPConnection(m_UDPConnection)`), NOT the per-sector socket.

**The crux:** in-game **C->S** goes proxy-master(EPH1) -> server **3501+N**, while
in-game **S->C** comes server **3806** -> proxy-global(EPH2). The sector listener
(3501+N) NEVER sends on its own socket. So the C->S in-game leg `(EPH1, 3501+N)`
has NO return path, and even if it did, the master socket -- `connect()`'d to 3808
-- would DROP a DTLS handshake reply from 3501+N. A DTLS association needs records
flowing BOTH ways through ONE `(ip,port)<->(ip,port)` pair; this split breaks that.

**Design decision (proxy-only, ZERO server-routing change): make the in-game-C->S
proxy socket UNCONNECTED so the sector listener's DTLS handshake replies reach it.**
Each server listener already owns a `DtlsTransport` (AH-2) and already `RawSendTo`s
handshake bytes on its own socket, so the sector listener (3501+N) can complete a
handshake the moment the proxy drives one -- IF the proxy can receive its reply.
Today it cannot (master socket is `connect()`'d to 3808). Switch the master socket
to unconnected (`recvfrom`, IP-filtered like the global socket) so it accepts
datagrams from 3808 AND 3501+N. Then:
  - master socket: DTLS assoc with 3808 (account handoff) + a per-sector assoc with
    3501+N (C->S game). The sector listener only ever emits DTLS *handshake* records
    on its own socket (no app data S->C -- that stays on 3806), so after decrypt the
    only app records arriving on the master socket are 0x2009 from 3808. Clean.
  - global socket: DTLS assoc with 3810 (account) + with 3806 (S->C in-game +
    keepalive C->S) -- already bidirectional, handshakes fine.
This keeps the server's S->C-via-3806 design and the idle-reaper/keepalive design
100% intact: NO server in-game routing change, so no server-integrity/primary-source
burden beyond the DTLS wrapper itself. Cost: the proxy drives a fresh DTLS
ClientHandshake to each sector port on entry/gate-jump (one extra RTT per sector),
and the master socket switches connected->unconnected `recvfrom`. Rejected
alternative (Option A: re-route C->S game through 3806 + teach the MVAS handler to
dispatch game opcodes) -- one persistent in-game association, but it changes the
server's in-game C->S dispatch port (high hot-path blast radius, needs primary-source
proof). Proxy-only wins on risk; the server is slated for rewrite anyway.

- [x] **AH-3. Proxy side: DTLS-connect with NAME verification (per the verified
  topology above).** DONE -- commit `ea21c054`. Client-role `DtlsTransport` per
  proxy UDP socket, multiplexing one association per `(server_ip, server_port)`;
  master socket made unconnected when DTLS on; recv feeds DTLS + dispatches via
  `DispatchServerDatagram`; send routes through `SendApp` (never cleartext while
  DTLS on); handshakes kicked at `OpenFixedPort` + `SetClientPort`; fail-closed
  policy gate (`InitProxyDtlsPolicy`) + LOUD opt-out. `common/DtlsTransport.cpp`
  added to PROXY_SRC for both targets. **VALIDATED 2026-06-07:** forced DTLS on
  locally (server cert=localhost self-signed, proxy verify host=localhost,
  CA=the cert) -- the proxy logged `DTLS association ESTABLISHED to
  <server>:3808` AND `:3810` at idle, proving the full client 4-flight +
  server response + per-(ip,port) keying + name verification all work. App
  encrypt/decrypt during real gameplay + the 3806/3501+N associations are the
  CV-13 real-client check (the CLI has no DTLS client). Original sketch below:

- [ ] **AH-3 (original sketch). Proxy side: DTLS-connect with NAME verification.**
  Add a client-role `DtlsTransport` (`DtlsRole::Client`) to
  EACH proxy `UDPClient` socket, gated on the same opt-out sentinel. Verify the
  server cert against the **cert NAME** (LE) via `SetVerifyHostname(g_DomainName)`
  (-> `SSL_set1_host`) + system trust store -- NOT a pin. Steps:
    1. **Master socket -> unconnected.** Switch `proxy/Net7.cpp:223`'s UDPClient to
       `m_Unconnected=true` (or make `OpenFixedPort` skip `connect()` when DTLS is
       on) so it can `recvfrom` the sector listener's handshake replies. Apply the
       same IP-filter the global socket uses (`UDP_RecvFromServer` unconnected path,
       `:464-477`). Default-peer sends (port=0) become `sendto(m_SockAddr)`.
    2. **Per-socket DtlsTransport, peer-keyed by recvfrom (ip,port).** `Feed` each
       inbound datagram under `DtlsPeerKey(src_ip, src_port)`; dispatch only the
       decrypted `app_data`. `SendApp` each outbound under the dest `(ip,port)`.
    3. **Drive the handshake before first app send.** The proxy is the DTLS client:
       call `ClientHandshake(peerkey)` when it first learns a peer -- 3808 at master
       open, 3810 at global open, 3806 when the global keepalive/login starts, and
       **3501+N when `SetClientPort` lands the sector port** (`ClientToMasterServer.cpp:155`).
       Pump handshake `to_send`/`Feed` until `Established`, then send app data
       (`SendApp` drops if not established -- never cleartext gameplay).
    4. **`NET7_GAME_SERVER_DOMAIN`.** `UDPClient_linux.cpp` resolves by
       `NET7_GAME_SERVER_HOST` and does not see `g_DomainName`; add a
       `NET7_GAME_SERVER_DOMAIN` env (the cert SAN) read in `OpenFixedPort`, used
       ONLY for `SetVerifyHostname` while the socket still connects-by-IP.
    5. Add `common/DtlsTransport.cpp` to the proxy CMake (both Linux + MinGW
       targets). One source tree, two build targets -- do NOT fork it.

- [x] **AH-4. CLI / integration-suite escape hatch -- the LOUD explicit opt-out.**
  DONE -- wired the sentinel into the dev/test compose. `docker-compose.yml`
  (server + proxy) and `docker-compose.cli.yml` (per-unit proxy) default to
  `NET7_DTLS_ALLOW_PLAINTEXT=i-accept-unencrypted-udp` via `${VAR-default}`
  (the `-`, not `:-`, so an explicit empty in the host env flips both ends to
  DTLS together). `docker-compose.cli-online.yml`'s proxy is intentionally NOT
  opted out -- it sets `NET7_GAME_SERVER_DOMAIN=${ENB_ONLINE_HOST}` +
  `NET7_DTLS_CA=` (system trust store / LE roots) because the cloud server runs
  DTLS-on. No `just play-local/play-cli` edit needed -- they go through these
  compose files and inherit the default. **VALIDATED 2026-06-07:** rebuilt +
  booted the full dev stack -- server and proxy both print the LOUD opt-out
  banner and run cleartext (no FATAL, all containers stable); the full server
  docker image also compiled/linked clean (first full build of AH-2, not just
  -fsyntax-only). NOTE this restores bootability -- AH-2 alone left the stack
  fail-closed at server start with no DTLS env.

  **Production key-perms finding (surfaced by the AH-3 validation).** When DTLS
  is required, the server (`ServerManager::InitDtlsServerPolicy`) loads the
  cert+key as uid 999 (`net7`). A `0600` key owned by another uid -> "Permission
  denied" -> fail-closed at boot (correct, but it WILL block a deploy). The
  committed dev key `deploy/certs/localhost.pem` is `0600` host-owned, so the
  DTLS-on smoke test needed a world-readable /tmp copy. **Deploy requirement
  (AH-6): the DTLS private key must be readable by the container's net7 uid/gid
  -- group-read for gid 999, NOT world-readable.** The login container sidesteps
  this only because it runs as root.

- [ ] **AH-4 (original sketch). CLI / integration-suite escape hatch.**
  DTLS is REQUIRED by default (AH-2), so the CLI (`CliClient.Core`) and the Phase T
  suite -- which speak cleartext UDP over the loopback/docker private bridge --
  would fail to connect unless the operator EXPLICITLY opts out. The opt-out is a
  single deliberate, unmistakable sentinel, NOT a generic enable flag:

      NET7_DTLS_ALLOW_PLAINTEXT=i-accept-unencrypted-udp

  decided in `common/DtlsTransport` (`DtlsPlaintextOptedOut()` matches it
  byte-for-byte; a stray `1`/`true`/`yes` does NOT trip it -- pinned by
  `dtls_transport_test.PlaintextOptOutRequiresExactSentinel`). Set this sentinel
  ONLY in the trusted-loopback compose/launch files that need cleartext:
  `docker-compose.yml` (server + login + proxy services), `just play-local`,
  `just play-cli`, and the Phase T integration stack. Each path that runs cleartext
  prints the LOUD startup warning. The PUBLIC deploy sets nothing -> DTLS required
  -> fail-closed if the cert is missing. **Do NOT** weaken any server auth/state
  guard to make tooling easier (CLAUDE.md) -- an explicit loopback-only plaintext
  opt-out on a private bridge is a topology choice, not a loosened check. The token
  layer (AH-9) follows the same gate: when plaintext is opted out on the private
  bridge, token enforcement is off there too; the public default enforces both.

- [ ] **AH-5. Preserve the capture workflow.** Keep the config flag OFF on the
  local-debug stack so `proxy/local-debug/` dumps stay cleartext, or wire an
  `SSLKEYLOGFILE` keylog + a Wireshark DTLS decode recipe. Non-optional: losing
  readable captures silently is a real regression on the project's core workflow.

- [ ] **AH-6. Firewall / hosting note.** Update the hosting write-up -- the
  externally-open UDP game ports are unchanged in NUMBER, just DTLS-wrapped. No
  new ports. **Also document the DTLS key-perms requirement** (from the AH-4
  finding): the server container runs as uid/gid 999 (`net7`) and must be able
  to read the DTLS private key; provision the key group-readable by gid 999
  (NOT world-readable). The prod compose already mounts `${DOMAIN}.cer/.pem` at
  the path the server's `g_DomainName + ".cer"` resolves to, and prod templates
  `domain=__DOMAIN__` in `Net7Config.cfg` -> the cert path matches; the open
  item is the key file mode on the droplet. Player-side proxies under WINE need
  a CA bundle reachable for LE verification (system store, or `NET7_DTLS_CA`).

- [ ] **AH-7. plans/29 client-verification entry.** The END-TO-END path (real
  client.exe -> local proxy -> DTLS -> server, with the token on C->S) must be
  confirmed against the actual Win32 client before this is DONE. Add a CV-NN
  entry.

### Post-AH-4 test triage (2026-06-07) -- 3 reds, root-caused, NOT a DTLS regression

After AH-3/AH-4 a Verification+TlsLogin slice showed 3 failures. Run to ground:

- **`TlsLoginTests.ValidAccount_ReturnsValidTicket`** -- a stale assertion left by
  the AH-0 CSPRNG commit (`4cb31a72`). It still pinned the old `%s-%d` (glibc
  `rand()`) ticket shape; the suffix is now 32 hex chars. FIXED (commit `8a5656a6`,
  test-only): pins the 32-char all-hex CSPRNG suffix. All 3 `TlsLoginTests` green.

- **`DockHandshakeRetailParityTests` x2** (`StationHandshake_*`) -- `ProxyWedgeException:
  stalled before 0x0005 START`. PROVEN PRE-EXISTING, not an AH regression: checked
  out the pre-everything baseline `57022cc7` (= `4cb31a72^`, before CSPRNG and all
  DTLS work), rebuilt server/login/proxy, and the SAME two tests fail identically
  (2 failed / 0 passed, same wedge). So the AH commits did not cause it. Server
  logs show the player reaches "fully logged in" and the proxy reaches "SectorServer
  LOGIN -- connection active", then 18s silence -> the `0x0005 START` never reaches
  the CLI. This is the known two-stage **station** establish wedge
  (`EstablishAtStationAsync`: establish-at-home -> logout -> reestablish-at-station),
  the same flaky area as tasks #33/#35/#36/#37. One-STAGE sector establish passes
  in 8s on the same clean stack, so the server->client START relay / proxy global
  recv path are fine. **Left as a separate pre-existing defect; out of AH scope.**

- **AH proxy code proven inert in opted-out mode:** `InitDtls` sets `m_Dtls=nullptr`
  and returns; sockets keep their original connect state (master connected, global
  unconnected -- confirmed in the proxy boot log); the cleartext recv path is
  byte-identical to pre-AH-3 (`DispatchServerDatagram(received)` runs the same
  opcode switch; the global-plane src-IP drop pre-dates AH-3).

### Auth-token layer (the C->S per-packet token)

- [ ] **AH-8. Token wire envelope.** Define in `common/include/net7/` a fixed-size
  token prefix prepended to C->S datagrams *inside* the DTLS channel (so it is
  never on the wire in cleartext). The token is the binary form of the CSPRNG
  ticket suffix (16 bytes). S->C datagrams do NOT carry it. Add the struct to the
  common header so server + proxy + CLI share one definition. Pin the bytes in a
  CLI test.

- [~] **AH-9. Server binds + validates the token.** At ticket login
  (`ProcessTicketInfo`) the server stores the presented token against the
  resolved player (closing the dropped-handoff hole at the same time). Then every
  C->S datagram on the affected listeners is checked: strip the token prefix,
  constant-time compare against the bound token for that `GameID`; on mismatch
  drop the packet and log a single rate-limited warning (do NOT log the token).

  **STATUS 2026-06-07: the always-on DB ticket-suffix-validation half (the AJ-1
  keystone, task #89) is DONE and landed as one unit; the DTLS-gated per-packet
  bind half remains (tracked under AH-8/AH-10).** Done in this unit:
  `db/postgres/login_ticket.sql` (schema), `docker-compose.yml` (unconditional
  schema-init apply), `login-server/Net7SSL/LinuxAuth.cpp` `StoreLoginTicket`
  UPSERT in `HandleAuthLogin`, `server/src/AccountManager.cpp` `StoreTicketRow`
  UPSERT in `BuildTicket` + `ValidateTicketSuffix` (parameterized SELECT +
  `sodium_memcmp` + expiry), `server/src/UDP_Global.cpp` `ProcessTicketInfo`
  reject-on-invalid. Tests: `GlobalConnectTests.ValidTicket_...` (accept) +
  `GlobalConnectTests.ForgedTicketSuffix_..._ReturnsGlobalErrorTicketInvalid`
  (reject). Real-client check: `plans/29` CV-14. All 9 GlobalConnect/TlsLogin/
  SectorAction tests green; genuine login_ticket rows written with 32-hex tokens,
  zero false rejects in server logs.

  **DECIDED 2026-06-07 -- token source of truth = shared `net7_user.login_ticket`
  table (DB handoff, NOT HMAC).** Rationale: the owner pinned "token = the 16-byte
  CSPRNG ticket suffix"; a stateless HMAC token (option b) would force the suffix
  to become `nonce || HMAC`, contradicting that. Both processes ALREADY connect to
  `net7_user` and query `accounts` there (login-server: `LinuxAuth.cpp` pqxx
  `exec_params`, `BuildDsn` dbname=`net7_user`; game-server: `AccountManager.cpp:74`
  `m_SQL_Conn.connect("net7_user", ...)` with `run_query_params` `?` placeholders).
  So a shared table is zero new connections, no HMAC-key distribution, no new
  network handoff. This is the DB form of option (a) -- it restores the Win32
  `RegisterSectorServer` handoff via the database both halves already share.

  Concrete shape (all queries PARAMETERIZED per CLAUDE.md -- no string-concat):
    - **Schema:** new `net7_user.login_ticket(username TEXT PRIMARY KEY,
      token TEXT NOT NULL, expires_at BIGINT NOT NULL)` (token = the 32-hex suffix;
      `expires_at` = unix-ms, matching `kTicketExpireMs`). Add as
      `db/postgres/login_ticket.sql` with `CREATE TABLE IF NOT EXISTS`, applied by
      an UNCONDITIONAL `psql -f` step in `docker-compose.yml` schema-init (NOT gated
      on the `accounts` probe, so it lands on existing volumes too). Idempotent.
    - **login-server write** (`LinuxAuth.cpp` `BuildTicketLocked`, and the game
      server's own `AccountManager::BuildTicket` for the 0x2001 UDP issue path):
      after building the ticket, `INSERT ... ON CONFLICT (username) DO UPDATE` the
      `(token, expires_at)`. So whichever issuer minted the presented ticket, the
      row exists.
    - **game-server validate** (`UDP_Global.cpp` `ProcessTicketInfo`): after
      `strtok` username + token, `SELECT token, expires_at FROM login_ticket
      WHERE username = $1`; reject (`SendGlobalError` + return false) if no row, if
      `expires_at < now`, or if `sodium_memcmp(token, suffix)` != 0
      (constant-time). This CLOSES AJ-1 / task #89 (the impersonation hole) and is
      a pure server-side TIGHTENING -- always-on, NOT gated by the plaintext
      opt-out (the integration suite uses genuine login-server tickets, so it stays
      green with no opt-out). Primary source: the Win32 `RegisterSectorServer`
      issued-ticket handoff + `GetUsernameFromTicket` path the Linux port dropped
      (see the "intentionally NOT ported" comment in `LinuxAuth.cpp`).
    - **per-packet bind** (the AH-8 envelope half, DTLS-gated): at successful
      `ProcessTicketInfo` bind the 16 binary token bytes to the resolved player;
      every in-game C->S datagram (after the envelope strip) constant-time-compares
      its prefix against the bound token for that `GameID`. This half IS gated by
      the plaintext opt-out (no DTLS -> no confidential channel to carry the token
      -> enforcement off on the private test bridge; public default enforces both).
  **CLAUDE.md sequencing:** land the schema + login write + server validate +
  forged-suffix CLI rejection test as ONE unit (the table is never an orphan).
  Add the `plans/29` CV entry for the real-client login path.

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
