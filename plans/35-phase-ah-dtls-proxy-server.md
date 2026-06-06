# Phase AH -- DTLS for the proxy<->server UDP leg (OWNER APPROVAL REQUIRED)

**Status: NOT STARTED. Approval-gated -- do NOT begin without explicit owner
sign-off.** This is a write-up of how it would be done and the caveats, parked
here in case the owner decides later to encrypt the proxy<->server transport.
An agent must NOT start implementing any item below on its own initiative;
"continue" / "do the next phase" does NOT authorize this phase. It begins only
when the owner says, in so many words, "do the DTLS phase".

## Why this is parked, not done

The proxy<->server UDP leg is currently **cleartext**. That is faithful to how
Net7 ships: the real EnB protocol was RC4 client<->server directly, but the
Net7 proxy architecture terminates the client's RC4/RSA on the player's own
machine and re-emits cleartext UDP to the server. So the cleartext is a Net7
architecture artifact, not a client-fidelity requirement -- the real client
never sees this leg. Encrypting it therefore does NOT break client/preservation
fidelity on the client-facing wire.

But it is **probably not worth doing**, and the owner should weigh that before
greenlighting:

- **Threat model is thin.** The sensitive secret -- login credentials -- already
  rides real TLS on the auth leg (login-server :443). What the UDP leg carries
  is ship positions, sector chat, inventory, combat. For a hobby preservation
  server that is low-value to an on-path observer.
- **It kneecaps the project's own debugging workflow.** Protocol work in this
  repo depends on reading the **cleartext proxy<->server UDP** captures
  (`proxy/local-debug/`, the cleartext leg; the committed reference decodes in
  Phase AF were taken there too). Encrypt that leg and you can no longer read
  your own captures with a plain UDP dumper. (DTLS keylog / capturing inside the
  tunnel is possible but adds friction to every future packet investigation.)
- **WireGuard is the lower-risk alternative.** A network-layer tunnel encrypts
  the same leg with **zero** changes to proxy/server/CLI code, keeps the wire
  bytes byte-identical (preservation + the integration suite untouched), and you
  capture *inside* the tunnel where it is still cleartext. Its only real cost is
  per-player key distribution. **If the goal is just "don't let the hosting
  provider / an on-path observer see gameplay," do WireGuard, not this phase.**
  This phase (DTLS in the application) is the right tool only if you specifically
  want per-endpoint cert auth baked into the proxy/server binaries rather than an
  external tunnel.

So: this is documented for completeness; the default recommendation remains
**leave it cleartext, or use WireGuard if you must.**

## How DTLS would work here

DTLS is TLS adapted to datagrams (UDP). Same X.509 / PKI cert model, same
crypto shape as TLS:

- **Asymmetric handshake, symmetric bulk.** The handshake uses the cert's
  asymmetric key (RSA or ECDSA) to authenticate the endpoint and run an
  ephemeral ECDHE key exchange; that derives a shared **symmetric** session key,
  and every datagram after is encrypted with an AEAD symmetric cipher
  (AES-GCM / ChaCha20-Poly1305). Identical to TLS -- asymmetric to bootstrap,
  symmetric to carry traffic.
- **OpenSSL is already linked** in both the server and the proxy (OpenSSL 3.x,
  Phase E/O). DTLS is `DTLS_method()` (DTLS 1.2, RFC 6347) or DTLS 1.3
  (RFC 9147) instead of `TLS_method()`, plus the datagram BIO wiring. No new
  third-party dep.

### Cert strategy: self-signed + pinned, NOT Let's Encrypt

Both ends of this leg are **our own code** (we ship Net7Proxy.exe). That is
exactly the WebRTC situation: when you control both halves, you do NOT need a
public CA. Generate ONE self-signed cert, hard-pin its fingerprint (SPKI hash)
in the proxy, and verify against that pin instead of the system trust store.

- **Stronger than CA validation here** -- a pinned self-signed cert cannot be
  spoofed by any CA mis-issuance, and there is no public trust dependency.
- **No renewal treadmill** -- a long-lived (or effectively non-expiring)
  self-signed cert avoids the 90-day Let's Encrypt automation entirely.
- **Let's Encrypt is possible but pointless on this leg.** An LE cert is just a
  standard X.509 cert; DTLS would load it fine, and issuance via Route53 DNS-01
  works regardless of transport (HTTP-01/TLS-ALPN-01 do NOT, since they need an
  HTTP/TLS-on-TCP responder). But LE only earns its keep when the client is
  something you do NOT control (a browser, the launcher's auth relay validating
  against the public trust store). For a machine-to-machine link where both ends
  are our binaries, **self-signed + pin is simpler and tighter.** Reserve LE for
  the auth :443 leg, where it is genuinely required.
- Server auth only is enough (proxy verifies the server's pinned cert).
  **Mutual DTLS** (server also demands a client cert from the proxy) is optional
  and adds per-player cert provisioning -- only worth it if you want the server
  to reject proxies that don't present a known cert; skip for v1.

## Implementation sketch (when/if approved)

Each item is `[ ]` and gated. Do them in order; verify the integration suite
stays green at every step.

- [ ] **AH-1. Decide endpoints + key material.** Generate the self-signed
  server cert/key (long validity, EC P-256). Compute its SPKI pin. Decide where
  the proxy reads the pin from (compiled-in constant vs. config file shipped with
  the package). Document in `docs/17-traffic-and-ports.md`.
- [ ] **AH-2. Server side: DTLS-wrap the UDP listeners.** This is the hard part
  -- the server binds MULTIPLE UDP ports (MVAS 3806, sector 3501-3800, master
  3808, global 3810; see `common/include/net7/Ports.h`). Each datagram socket
  that the proxy talks to needs a DTLS layer (`SSL_CTX` with `DTLS_method()`,
  per-peer `SSL` over a datagram BIO, cookie-exchange anti-DoS via
  `DTLSv1_listen`). Scope which of these ports the remote proxy actually dials
  vs. which stay docker-internal -- only the externally-reachable ones need DTLS;
  do not wrap a loopback/docker-internal socket for nothing.
- [ ] **AH-3. Proxy side: DTLS-connect with pin verification.** The Win32-PE
  proxy (the one players run under WINE) opens DTLS to the server, verifies the
  server cert against the pinned SPKI hash (custom verify callback -- NOT the
  system trust store), then runs the existing cleartext UDP framing INSIDE the
  DTLS channel. Mirror in the Linux-native proxy build (one source tree, two
  targets -- do NOT fork it).
- [ ] **AH-4. CLI / integration-suite escape hatch.** The CLI client
  (`CliClient.Core`) and the Phase T suite speak **cleartext UDP directly** to
  the server (the integration stack runs proxy-less over loopback/docker). DTLS
  on the server's UDP ports breaks them unless gated. Options, in order of
  preference: (a) make DTLS a server config flag that is OFF for the docker
  integration stack and ON only for the public deploy (the cleanest -- the test
  topology never needed it); (b) teach the CLI a DTLS path. Pick (a) unless
  there is a reason to encrypt the test leg. **Do NOT** weaken any server auth/
  state guard to make the tooling's life easier (CLAUDE.md server-integrity
  rule) -- a config-gated transport wrapper is fine; loosening a check is not.
- [ ] **AH-5. Preserve the capture workflow.** Document how to still read the
  leg for protocol work once it is encrypted: either keep the config flag OFF on
  the local-debug stack (so `proxy/local-debug/` dumps stay cleartext -- the
  likely answer), or wire an `SSLKEYLOGFILE`-style keylog + a Wireshark DTLS
  decode recipe. This is non-optional: losing readable captures silently is a
  real regression on the project's core workflow.
- [ ] **AH-6. Firewall / hosting note.** Update the Phase-AH section of the
  hosting write-up (DigitalOcean + Route53) -- the externally-open UDP game
  ports are unchanged in NUMBER, they are just DTLS-wrapped now. No new ports.
- [ ] **AH-7. plans/29 client-verification entry.** Even though this leg never
  reaches the real client directly, the END-TO-END path (real client.exe ->
  local proxy -> DTLS -> server) must be confirmed working against the actual
  Win32 client before this is DONE. Add a CV-NN entry.

## Hard rules that still bind

- This is a **transport wrapper**, not a wire-format change. The bytes inside
  the DTLS channel are the SAME cleartext UDP frames as today. Do NOT change any
  opcode, struct, or framing while doing this -- if you find yourself editing a
  packet emitter, you have left this phase's scope.
- **Never loosen a server security/auth/session check** to accommodate the CLI
  or the test suite (CLAUDE.md). The DTLS-OFF-for-tests gate (AH-4a) is a
  transport toggle, which is allowed; bypassing an auth/state guard is not.
- One proxy source tree, two build targets. Do NOT add a second forked copy of
  any proxy translation unit (CLAUDE.md "proxy is ONE source tree").
