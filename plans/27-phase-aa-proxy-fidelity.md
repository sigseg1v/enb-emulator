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
deletion + docs) landed. Tier-2 transforms deferred with criteria below.

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

Full confirmed consumed-set (all return "handled"):
`0x100a, 0x2011, 0x2012, 0x2013, 0x2014, 0x2018, 0x2019, 0x2020, 0x2025,
0x202a, 0x202b, 0x202d, 0x202e` plus `0x1b`-when-undersize. Everything else
falls through to the `1..0xFE` forward gate. This matches the reference
control dispatcher exactly.

## 3. Client->server opcode parity (audited -- Wave 1)

`ProcessSectorServerOpcode` (`ClientToServer_linux_stubs.cpp`) already
matches the reference C->S handler for: `0x02` LOGIN, `0x06` START_ACK
(forward + 0x3004/0x3008 visibility), `0x12` TURN / `0x13` TILT (250 ms
throttle when moving), `0x14` MOVE, `0x2c` ACTION, `0x9b` WARP, `0x9f`
STARBASE_ROOM_CHANGE, `0xb9` LOGOFF, `0x3a` HANDOFF, default -> forward.

- [!] **`0x4e` starbase-exit handoff** is the one missing C->S case. The
      reference, when an in-starbase flag is set, logs "Leaving Starbase"
      and issues a launcher handoff request. Deferred: it depends on
      proxy-internal starbase state + the launcher handoff path, neither of
      which is wired on our side yet. Forwarding `0x4e` (current behaviour)
      is the safe direction. Unblock when the launcher handoff lands.

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
