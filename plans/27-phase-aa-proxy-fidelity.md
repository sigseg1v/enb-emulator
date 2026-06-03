# Phase AA - Proxy translation fidelity & single-source consolidation

The proxy is not a dumb relay (see CLAUDE.md "The proxy is NOT a dumb
relay" and docs/03-network-protocol.md §1). It strips/​re-frames, consumes
control opcodes the client never sees, drops malformed frames, rewrites a
few payloads, fabricates packets the server never sent, reassembles
splits, and encrypts the client leg. This phase makes OUR proxy's
per-opcode behaviour match a known-good reference proxy<->client stream,
and collapses the source tree to a single implementation.

**Sourcing.** Everything here is grounded in clean-room observation of a
known-good Net7Proxy<->client byte stream -- the committed retail captures
under `archive/kyp-snapshot/capturedPackets/`, plus local working captures
of the cleartext proxy<->server leg (kept in the gitignored
`proxy/local-debug/` scratch dir, not committed) -- cross-checked against our
own proxy source. No server security posture is loosened; every change either
restores behaviour we can show a good stream relied on, or TIGHTENS the
proxy toward the protocol (rejecting inputs the reference rejects), which
CLAUDE.md welcomes.

## Status

In progress -- 2026-06-03. Wave 1 (opcode parity Tier-1 + dead-twin
deletion + docs) landed. Wave 2 fixed the C->S TURN/TILT throttle (was
unconditional, now only-while-moving). Wave 3 found that Wave 2's claim
"S->C consume set matches the reference exactly" was WRONG for the
fabrication band: the reference EXPANDS `0x2012/0x2013/0x2014` (and
`0x2018/0x2019`) into full client packets and we drop them -- a real,
active regression for mining/tractor/loot beam + object-spawn effects.
The misleading code/plan comments are corrected and the reconstructed
fabrication spec is captured in §3a; implementation is in progress.

---

## 1. Single source of truth (DONE -- Wave 1)

**Problem.** `proxy/` carried two parallel-looking implementations: the
active cross-platform files and a set of `#ifdef NET7_LEGACY_WIN32`-walled
twins (`UDPProxyToClient.cpp`, `UDPProxyToGlobal.cpp`, `UDPProxyMVAS.cpp`,
`ClientToSectorServer.cpp`, `ClientToGlobalServer.cpp`, and -- found in the
finalization sweep -- `UDPClient.cpp`). This is the maintenance hazard the
project owner flagged: every wire-format fix would have to be applied twice
and the two copies WOULD drift.

**Finding.** `NET7_LEGACY_WIN32` is never defined in any build (no
CMakeLists, toolchain, Makefile, justfile, or compose file sets it -- the
Win32-PE MinGW build does NOT set it either), and the `#ifdef` is the
file-level outer guard in every one of those six files. They compiled to
**empty translation units** in both the Linux-native and the Win32-PE
(MinGW) builds. Nothing could link to a symbol inside an empty TU, and both
builds work, so the files were provably dead.

- [x] Deleted the six dead `NET7_LEGACY_WIN32` twins (the first five, then
      `UDPClient.cpp` in the finalization sweep).
- [x] Removed the now-dead `#ifndef NET7_LEGACY_WIN32` file-level guards
      from the three surviving active files (`UDPProxyToClient_linux.cpp`,
      `UDPClient_linux.cpp`, `ClientToServer_linux_stubs.cpp`) -- with the
      `#ifdef` twins gone the `#ifndef` complement was always-true scaffolding.
- [x] Removed dead client-launcher stubs the deleted twins were the last
      consumers of (`StartENBClient`, `GetProcessHandle`, `engine_open_process`,
      `engine_read_process`, `PatchClient`, `ClientStillRunning`,
      `WaitForEngineReady`) plus their `Net7.h` decls. Kept `ShutdownClient`
      (live caller in `ClientToMasterServer.cpp`) and `WaitForLogin`.
- [x] De-falsified the file-header narratives that asserted "the real
      implementation lives in [now-deleted file]" / "the WIN32 build picks up
      the real dispatch from [deleted file]" -- those perpetuated the two-impl
      model this phase kills. (The inline `// Mirrors Win32 X.cpp:NN` provenance
      cross-refs to deleted files are being neutralized per-file as each region
      is reworked for Tier-2 parity -- see §4.)
- [x] Verified the Linux-native build (`cmake --build proxy/build`) links
      clean: 16 `.cpp` (was 22), zero warnings.
- [x] Verified the Win32-PE cross-build
      (`-DCMAKE_TOOLCHAIN_FILE=cmake/mingw-w64-x86_64.toolchain.cmake`,
      static OpenSSL) still links clean -- this is the target that runs
      under WINE next to `client.exe` (Phase W). (Pre-existing MinGW-only
      `-Wtype-limits` warnings on `UDPClient_linux.cpp` unsigned-`SOCKET`
      comparisons remain; they are a Phase-W cross-build item, not Wave 1.)

**Result: ONE implementation, TWO build targets.** Both
`proxy/build` (Linux-native, docker server-side) and `proxy/build-win64`
(Win32 PE, client-side under WINE / native Windows -- how real players run
it) compile the same `proxy/*.cpp`. The maintained server<->client logic
lives in `UDPProxyToClient_linux.cpp` (S->C demux), `UDPClient_linux.cpp`,
`ClientToServer_linux_stubs.cpp` (C->S dispatch), and the shared
`Connection.cpp` / `ServerManager.cpp` / `Net7.cpp`.

- [ ] (cosmetic follow-up) The `_linux` filename suffix is now a misnomer
      -- those files are the cross-platform single source. Renaming
      `*_linux.cpp` -> `*.cpp` is low-risk (CMake GLOBs; no file is listed
      by name) but touches git blame/log-message strings, so it is queued,
      not done in this wave.
- [x] Dropped the stray MSVC `#pragma warning(disable:4786)`
      in `UDPClient.h` -- g++/MinGW both ignore it with a `-Wunknown-pragmas`
      note; it is dead.

## 2. Server->client opcode parity (Tier-1, DONE -- Wave 1)

Audited EVERY opcode the S->C path handles against a known-good reference
stream. The control dispatcher (`HandleCustomOpcode`) must consume exactly
the same set, and the forward gate must accept exactly the same range.

- [x] **0x1b AUX_DATA undersize guard.** A reference proxy DROPS an
      aux-data frame whose framed length (`[length][opcode]` header field,
      which counts the 4-byte header) is `< 8` -- it cannot hold even one
      payload word, and a client that parses a zero-payload aux record
      walks off its record array and crashes. We were forwarding it.
      `HandleCustomOpcode` now takes the framed `length` and drops the
      frame when `length < 8`; a well-formed aux frame (`>= 8`) falls
      through and is forwarded **verbatim** (the proxy does NOT rewrite aux
      payloads -- this is why the long-running 0x001B AuxPercent
      shield-form divergence is purely server-side, see Phase K Wave 337-339).
- [x] **Forward-gate tighten `1..0x0FFE` -> `1..0x00FE`.** The reference
      accepts exactly opcodes `0x01..0xFE` on the forward path and treats
      anything else as a bad opcode (recover). The highest game opcode in
      the catalog is `0x00DD` and nothing is defined in `0xFF..0xFFE`
      (verified by parsing `common/include/net7/Opcodes.h`: 209 defines,
      zero in that gap), so the tighten cannot reject any legitimate game
      opcode -- it only stops stray high opcodes from being blindly relayed
      to (and crashing) the client. Control opcodes (`>= 0x1000`) are
      consumed by `HandleCustomOpcode` before the gate and never reach it.
- [x] **Consume the `0x2025..0x202e` gate-cache band.** The reference
      consumes `0x2025`, `0x202a`, `0x202b`, `0x202d` (force gate caching),
      `0x202e` (enable gate caching) locally and forwards NOTHING to the
      client. We were missing them: they would fail the (now `1..0xFE`)
      gate, be logged as "bad opcode", and STALL the whole packet sequence.
      Added to the consume-and-drop group. We do not implement the
      proxy-side gate cache, so these are no-ops here -- gate queries go
      live to the server, which is the safe default and a valid runtime
      state of the reference proxy (its cache simply stays disabled).

Full set of opcodes the dispatcher returns "handled" for:
`0x100a, 0x2011, 0x2012, 0x2013, 0x2014, 0x2018, 0x2019, 0x2020, 0x2025,
0x202a, 0x202b, 0x202d, 0x202e` plus `0x1b`-when-undersize. Everything else
falls through to the `1..0xFE` forward gate.

**CORRECTION (Wave 3):** an earlier revision of this section claimed the
above "matches the reference control dispatcher exactly." That is FALSE for
`0x2012/0x2013/0x2014/0x2018/0x2019`. The reference returns "handled" for
those, but NOT by dropping -- it FABRICATES full client packets from each and
sends them to the client (then arms an effect-removal timer). We return
"handled" by dropping and producing nothing. Same return value, opposite
effect. This is a real S->C parity gap, now tracked as §3a. `0x100a, 0x2011,
0x2020, 0x2025, 0x202a, 0x202b, 0x202d, 0x202e` and `0x1b`-undersize DO match
the reference (consume / ack / no-op-while-cache-disabled), as does the
`1..0xFE` forward gate.

**Wave 2 -- S->C forward-handler (post-gate) per-opcode verification.**
After the custom dispatcher passes a `0x01..0xFE` opcode, the reference runs
a forward handler that decides FORWARD vs CONSUME and applies side-effects.
Diffed every case:

- `0x3a` SERVER_HANDOFF and `0xba` LOGOFF_CONFIRMATION both FORWARD to the
  client (the handler returns "forward") *after* applying connection-state
  side-effects (clear the connection-active flags, reset the sequence
  reassembly state on handoff, clear player-id + drive the UI to character
  selection on logoff). We mirror this exactly: our
  `UDPClient::IncommingOpcodePreProcessing` applies the same side-effects and
  the opcode is then forwarded verbatim through the gate. CONFIRMED MATCH --
  these are *not* consumed, contrary to a first reading.
- `0x09` / `0x0b` (timed static / obj2obj effects) are conditionally
  CONSUMED when the effect carries a positive/short-band duration (the
  reference re-emits them on its own timer). We forward both unconditionally.
  See §4 -- still deferred; forwarding-extra is the safe direction and the
  exact duration predicate wants a capture before we add a drop.
- `0x34` gate-cache timestamp REWRITE -- §4 deferred (gate-cache subsystem
  not implemented; the rewrite source is inert without it).
- `0x04` / `0x07` basset-tracking snoops are gated on the reference's
  large-packet mode flag (never enabled in our runtime), so they are inert;
  we forward both verbatim. No action.

## 3. Client->server opcode parity (Tier-1, DONE -- Wave 2)

Diffed `ProcessSectorServerOpcode` (`ClientToServer_linux_stubs.cpp`)
case-for-case against a known-good reference C->S sector dispatch. The
reference handles exactly `0x02` LOGIN, `0x06` START_ACK, `0x12` TURN,
`0x13` TILT, `0x14` MOVE, `0x2c` ACTION, `0x4e` starbase-exit, `0x9b` WARP,
`0x9f` STARBASE_ROOM_CHANGE, `0xb9` LOGOFF, and a `default -> forward`.
Our case set now matches that exactly. Confirmed-matching behaviours:
`0x02` (activate connection state + forward), `0x06` (forward + 0x3004 if
in-space / 0x3008 if station visibility kick), `0x14`/`0x2c`/`0x9b`/`0x9f`
(forward verbatim), `0xb9` (set sentinel + forward), default forward.

- [x] **`0x12` TURN / `0x13` TILT throttle now gated on MOVING.** The
      reference drops a turn/tilt ONLY when it is both within the 250 ms
      window AND the avatar is moving (the speed float at payload offset 4
      is non-zero); a stationary avatar's turns ALL pass or the ship
      stutters under in-place mouse-look. We had been throttling
      unconditionally -- dropping legitimate stationary turns -- which §3
      previously *described* as "throttle when moving" without the code
      actually doing it. Fixed: read the payload-offset-4 speed float
      (m_RecvBuffer[4], byte-aligned with the reference's payload+4) and
      drop iff `tick <= last + 250 && speed != 0.0`. This is a
      tighten-toward-fidelity change: it forwards MORE client turns (the
      exact ones the retail client+server pair exchanged), changes no wire
      bytes, and loosens no server posture (TURN is a normal gameplay
      opcode the server already accepts at any cadence).
- [x] **Removed the redundant `0x3a` C->S case.** `0x3a` SERVER_HANDOFF is
      a *server->client* opcode: the proxy consumes its side-effects in
      `UDPClient::IncommingOpcodePreProcessing` on the S->C path and then
      forwards it to the client. The reference C->S sector dispatch has NO
      `0x3a` case; ours had a no-op-then-forward case that was both
      misleading and behaviourally identical to the default forward. Dropped
      it so a (non-real) client-originated `0x3a` falls to the default
      forward exactly as the reference does.

- [!] **`0x4e` starbase-exit launcher handoff** is the one reference C->S
      side-effect we do not replicate. The reference, when an in-starbase
      flag is set, logs "Leaving Starbase" and spawns a launcher-handoff
      retransmit thread (MVAS sub-op 0x4e, up to 10x at 1s until the server
      confirms). Both the in-starbase state and the launcher handoff are
      proxy-internal machinery tied to the position feed, which only
      functions alongside the Win32 client. We still FORWARD `0x4e` to the
      server (via the default), which is the safe direction and matches the
      reference's verbatim forward of the same frame. Unblock with the
      position-feed / launcher-handoff port (Tier-2).

## 3a. S->C fabrication band (`0x2012/0x2013/0x2014/0x2018/0x2019`) -- Wave 3

The server sends these as COMPACT control opcodes and relies on the proxy to
expand each into the real client game packets. The server's own prospect path
(`server/src/PlayerSkills.cpp`) names the proxy routine `SendProspectAUX` and
comments "Just send one UDP packet to initiate prospecting" -- there is no
server-side beam emission for the start phase. The proxy builder family that
did this (`Connection::QueueObjectLinkedEffect / QueueObjectCreate /
QueueAuxPacket / QueuePosition / SendCreate ...`) was deleted with the Win32
twins; only the `Connection.h` declarations survive. So the proxy must rebuild
these emitters (against the structs already in `common/include/net7/
PacketStructures.h`) and call them from `HandleCustomOpcode` instead of
dropping.

Reconstructed spec (every target opcode maps to an existing struct; the
compact-form field order matches the server's emit code byte-for-byte):

- **`0x2012` START_PROSPECT.** Compact in: `playerGID(0) objGID(4)
  effectUID(8) prospectTick(12) drainTime(16)` (see `StartProspecting`,
  PlayerSkills.cpp:691-695). Fabricate:
  - if `playerGID == local avatar GID`: a `0x1b` prospect-skill AUX (the
    exact bytes are already hardcoded in server `SendProspectAUX` case 0 --
    `00 00 00 00 / 15 00 / 00 / 01 00 00 00 / 59 0B 00 00 / <ts> / ...`, the
    `0x59,0x0B` "always this for prospecting" marker) and a `0x1d`
    MESSAGE_STRING "Prospect ability activated."
  - always: `0x0b` OBJECT_TO_OBJECT_EFFECT with `Bitmask=3` (EffectID +
    TimeStamp present), `GameID=playerGID`, `TargetID=objGID`,
    `EffectDescID=0xbf`, `EffectID=effectUID`, `TimeStamp=proxy tick`.
  - arm a timer: after `drainTime` ms, emit `0x0f` REMOVE_EFFECT(effectUID).
- **`0x2013` TRACTOR_ORE / `0x2014` LOOT_ITEM.** Compact in: `playerGID
  articleUID articleEffectUID baseAsset tick name(LS) tractorTime
  tractorSpeed posX posY posZ` (UseTractorBeam / loot path,
  PlayerSkills.cpp:773-783 / 1144-1154). Fabricate: `0x04` CREATE for the ore
  article object, a `0x0b` "TRACTOR" effect (EffectDescID 2), a `0x1b` name
  AUX, a `0x46` positional interpolation toward the ship; arm a timer: after
  `tractorTime` ms emit `0x07` REMOVE(articleUID). (Loot differs only in the
  effect-flag ordering -- see the two reference variants.)
- **`0x2018`/`0x2019` OBJECT_CREATE.** DONE (Wave -- this session). The earlier
  "LATENT, no caller today" note was WRONG: the live capture
  `proxy/local-debug/net7-live-2026-06-02-login-undock-clicknavs-warp-logout.pcap`
  shows the server emitting **51x `0x2018`** STATIC_OBJECT_CREATE (navs /
  stations / gates / decos) and **34x `0x2019`** RESOURCE_OBJECT_CREATE
  (asteroids) on a normal Luna session -- and our proxy was DROPPING all of
  them, which is why Luna rendered empty (no navs, no planet, no roids). Now
  `UDPClient::CreateObject` expands `0x2018` -> `0x04` CREATE + `0x89`
  RELATIONSHIP + `0x40` CONSTANT_POSITIONAL_UPDATE + `0x1b` AUX-name + `0x99`
  NAVIGATION, and `UDPClient::CreateResource` expands `0x2019` -> `0x04`
  CREATE(Type=38) + `0x40` position + `0x1b` resource-name
  (`proxy/UDPProxyToClient_linux.cpp`). Byte-pinned in the CLI by
  `StaticContentFabricationSpecTests`. (Only the RELATIONSHIP ObjectID is
  big-endian -- ntohl at the server `PlayerConnection.cpp` emit site; every
  other field stays host/LE. See CLAUDE.md Trap 1.)

Open items before/while implementing:
- [x] Correct the false "consume = matches reference" comments in the proxy
      and this plan (commit 947d2296).
- [x] Implement `0x2012` START_PROSPECT (`UDPClient::StartProspecting`,
      proxy/UDPProxyToClient_linux.cpp). Emits a single 0x0b beam serialized
      byte-for-byte like the server's own `Player::SendObjectToObjectEffect`
      / `Player::ActivateProspectBeam` for a *timed* beam: Bitmask 0x07,
      EffectDescID 0x00BF, EffectID/TimeStamp/Duration. We use the Duration
      field (client self-expires) INSTEAD of the reference's separate 0x0f +
      drain-timer thread -- a deliberate divergence in mechanism, not in
      visible effect: it avoids a per-beam detached thread and its
      use-after-free hazard if the proxy<->client connection is torn down
      mid-mine. Grounded entirely in citable in-repo server code; needs NO
      external reference for the layout. The prospector-only AUX+text is NOT
      emitted yet (see next item).
- [ ] Prospector-only `0x1b` skill AUX + `0x1d` "Prospect ability activated."
      text. Needs the proxy to know whether the packet's prospectorGID is
      THIS client's own avatar. `m_PlayerID` holds the account avatar id from
      `SendMasterLogin`, which may not equal the in-space object GameID; if it
      does not, learn it from the `0x05` START id or the first self-create.
      The always-path beam does NOT need it; only this local branch does.
- [x] `0x2018`/`0x2019` OBJECT_CREATE -- expanded by `UDPClient::CreateObject`
      / `CreateResource`; byte-pinned by `StaticContentFabricationSpecTests`;
      CV-01/CV-02 queued for real-client confirmation. (This was the empty-Luna
      bug; the LATENT note above was wrong.)
- [ ] `0x2013`/`0x2014` TRACTOR/LOOT (still need the live mine/loot harness).
- [ ] **Byte-pin each fabricated packet.** No `tests/server` gtest can reach
      the proxy fabrication (it fires only when the live server emits 0x2012
      during a real mine). The byte-pin therefore lands in the new C# CLI
      phase: the CLI must parse 0x0b/0x04/0x1b/0x46/0x07/0x0f and an
      integration test mines a roid and asserts the fabricated 0x0b fields.
      Until that test exists, 0x2012 is "implemented + compiles + grounded in
      the server serializer" but NOT yet verified against a client. The
      structs in `PacketStructures.h` + the server serializers are the layout
      authority; a wrong layout CRASHES the Win32 client.

## 3b. MVAS idle keepalive (proxy->server) -- DONE (this session)

The proxy must keep a logged-in avatar's server-side idle timer alive. The
server reaps any in-game player at `LastAccessTime + 2*60000`
(`server/src/PlayerManager.cpp` `SendUDPOpcodes` / `SendUDPPlayerOpcodes` ->
`DropPlayerFromGalaxy` -> `DropPlayerFromSector`), which removes the ship from
every other client's view -- the "my ship disappeared after sitting idle" bug.

- **Confirmed mechanism (primary source = live server log)**: `clienta`
  logged in 15:17:58, `Removed from Galaxy` 15:20:09 = 2m11s, no refresh
  between.
- **Why it broke**: the upstream Win32 `UDPProxyMVAS.cpp` (ship-position
  scrape + MVAS keepalive) was never ported and is ABSENT from the repo;
  `MVASThread()` is declared but defined/started nowhere. Neither proxy build
  sent any `0x3005`/`0x1004` to the server's MVAS port, so an idle avatar
  behind the proxy was always reaped at 2 min.
- **Primary source for the fix shape**: cleartext capture
  `net7-live-2026-06-02-login-undock-clicknavs-warp-logout.pcap` (re-parsed
  2026-06-03). The real proxy emits 0x3005 on TWO synchronized ~30s streams:
  (1) bare keepalive from its MVAS socket (sport 43471) to udp/**3806**, first
  frame `0c 00 05 30 2a 99 03 40 01 00 00 00` (12-byte EnbUdpHeader, no body,
  dedicated low sequence 1,2,2,2); and (2) a 0x3005 from its main socket
  (sport 40208) to the active **sector** port (3636, then 3573 after a warp),
  on that socket's shared incrementing sequence. We replicate stream (1) only
  (server's authoritative refresh path); stream (2) is deferred -- it would
  have to ride the sector socket's live packet-sequence counter for no extra
  reaper benefit. The dead `SendCommsAlive()` -> `m_ClientPort` stub maps to
  stream (2).
- [x] `UDPClient::MVASKeepaliveThread` + `SendServerKeepalive`
      (`proxy/UDPClient_linux.cpp`, both build targets): a 30s loop started once
      per session in `FixedClientComm()` that sends the 12-byte `0x3005`
      carrying the live `m_PlayerID` to the server's MVAS port
      (`MVAS_LOGIN_PORT`/3806).
      **Port correction (2026-06-03)**: the first cut used `UDP_Send(0, ...)`,
      which `send()`s to the socket's connected peer -- but this UDPClient
      (`g_ServerMgr->m_UDPClient`) is connect()'d to `UDP_MASTER_SERVER_PORT`/
      **3808**, not 3806. The master plane has no comms-alive handler, so the
      keepalive refreshed nothing. Fixed to `UDP_Send(MVAS_LOGIN_PORT, ...)`
      (explicit `sendto(server:3806)`; Linux permits sendto to a non-peer port
      on a connected SOCK_DGRAM -- verified empirically).
      `UDP_MVAS.cpp HandleKeepCommsAlive` resolves `GetPlayer` and refreshes
      `LastAccessTime` (sends `0x100A` MVAS_TERMINATE only on an UNRESOLVED id).
      **Runtime proof (2026-06-03)**: a raw 0x3005 with bogus id 0xDEADBEEF to
      the live server:3806 returned `0x100A` echoing the id, proving 3806 is
      bound and dispatches 0x3005 -> `HandleKeepCommsAlive`.
- [x] CLI byte-pin: `MvasClientTests.BuildCommsAlive_MatchesRetailCaptureFrame`
      pins the exact capture bytes; play-cli is covered by the CLI's own direct
      `0x3005` (`StartCommsAlive`) since `MvasClient` dials 3806 directly.
- [ ] CV-04 -- real-client (play-local) confirmation: undock, idle 3+ min, ship
      must not vanish.

## 3c. CLI full-parse opcode coverage (ongoing -- this session)

Closing gaps where a server-emitted opcode fell through to `GenericRecord`
(hex dump only, no field decode). Each new record is byte-pinned against the
EXACT bytes the server emitter writes, asserting zero `???` gaps (every byte
decoded). This is the "understand before change" half of the three-places
rule -- CLI parse landing first.

- [x] **`0x0041` FORMATION_POSITIONAL_UPDATE** (`FormationPositionalUpdateRecord`,
      20B: TargetID@0, LeaderID@4, Pos[3]@8, all LE). Emitter
      PlayerConnection.cpp:1222; struct PacketStructures.h:521 (fields emit in
      declaration order -- the `this[16]`/`this[12]` comments are stale).
- [x] **`0x0083` RECUSTOMIZE_AVATAR_START** (`RecustomizeAvatarStartRecord`,
      60B: costs[14] LE @0..55, PlayerID BE @56). Emitter
      PlayerConnection.cpp:10033 (`ras.playerid = htonl(...)` -> BE, same
      convention as RELATIONSHIP ObjectID). struct PacketStructures.h:1097.
- [x] **`0x0096` JOB_ACCEPT_REPLY** (`JobAcceptReplyRecord`, 4B JobID LE, or
      empty for the generic-accept branch). Emitter PlayerConnection.cpp:10016
      / :10027.
- [x] Byte-pin tests: `SectorMiscRecordTests` (6 facts, all green; suite 634).
- [ ] **`0x0081` RECUSTOMIZE_SHIP_START** -- deferred: carries a 194-byte
      embedded `ShipData` struct with no existing CLI decoder. Needs a shared
      ShipData reader first.
- [ ] Variable-length build-loop opcodes (`0x0093` JOB_LIST, `0x0094`
      JOB_DESCRIPTION, `0x0065` UI_TRIGGER) -- higher effort, deferred.

These are pure CLI parser additions for opcodes the server ALREADY emits, so
no server/proxy change and no `KnownUnimplemented`/`TestedOpcodes` migration
(that path is only for newly-implemented server handlers).

## 4. Tier-2 deferred transforms (documented, NOT yet implemented)

These are real reference behaviours we do not yet replicate. In every case
the current behaviour (forward unmodified) is the SAFE direction, so they
are deferred rather than rushed. Each needs its own confirm-against-stream
wave.

- [ ] **`0x09` / `0x0b` conditional effect drop.** The reference runs each
      through a predicate that can drop the frame. We forward both
      unconditionally. Forwarding-extra is safe (the client tolerates the
      effect); dropping-wrongly is not. Needs the exact predicate confirmed
      against a stream before we add a drop.
- [ ] **`0x34` gate-cache timestamp injection.** The reference, when a
      pending gate-cache timestamp is queued, overwrites `payload[0..3]`
      with it. Gated on the gate-cache subsystem being enabled (`0x202e`),
      which we do not implement, so the inject is inert for us today.
      Lands with the gate-cache subsystem or not at all.
- [ ] **Proxy-side gate-cache subsystem.** The `0x202d`/`0x202e`/`0x34`
      machinery is a destination-cache optimisation (answer some gate
      queries without a server round-trip). Pure latency optimisation; no
      correctness impact while disabled. Low priority.

## 5. C# .NET 10 proxy rewrite -- FUTURE IDEA (deferred, not planned)

The project owner asked whether an exact-branch-matching C# rewrite of the
proxy (sharing the CliClient test harness + a packet-defs csproj) would be
worth it. **Decision (2026-06-03, owner-confirmed): not now -- keep the
C++ proxy.** Rationale:

- The C++ proxy is already ONE source tree that builds for BOTH Linux and
  Win32-PE, and the Win32-PE build is exactly what players run under WINE.
  A C# proxy would not remove the WINE dependency (the *client* is Win32),
  so it buys players nothing.
- A faithful rewrite would have to re-implement Westwood RSA + the SSL
  login handshake + dual RC4 + all framing/dispatch and re-validate
  byte-for-byte -- a large, high-risk effort to REPLACE something that
  works and is already exercised by the Phase-T integration suite.
- The only real upside (one language, shared unit-test harness) is largely
  already covered: the integration suite drives the live C++ proxy.

Recorded as a future idea. If it is ever revisited, the natural shape is a
`tools/` csproj holding the packet defs/formats shared with CliClient, with
the proxy logic ported branch-for-branch and pinned against the same
capture fixtures. Until then: **the C++ proxy is the single source of
truth; fix the one tree.**

## Cross-references

- CLAUDE.md "The proxy is NOT a dumb relay" (always-loaded guidance).
- docs/02-architecture.md §1 ("one implementation, two build targets" +
  "The proxy is not a dumb relay").
- docs/03-network-protocol.md §1 (capture-applicability warning).
- Phase W (23-phase-w-proxy-win32-crossbuild.md) -- the Win32-PE build.
- Phase Z (26-phase-z-emitter-fidelity.md) -- SERVER emitter fidelity
  (distinct from this phase's PROXY translation fidelity); the 0x001B
  AuxPercent shield-form divergence is a Phase-Z/K server-side item, and
  this phase confirms the proxy forwards aux >= 8 bytes unmodified, so that
  divergence is not a proxy artifact.
