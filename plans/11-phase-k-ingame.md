# Phase K — in-game opcode handlers + UDP proxy plane

Goal: take the Phase J "TLS terminates, handshake works, opcodes are dispatched" baseline and turn it into actually playable login → enter sector → see other players. Each item is sized to be portable independently (file-level WIN32 walls drop one at a time).

## Carry-over from Phase J

- TCP 3500 (SECTOR_SERVER_PORT) has no listener on Linux. `HandleMasterJoin` sends a hardcoded ServerRedirect there.
- `ProcessGlobalServerOpcode` (`proxy/ClientToGlobalServer.cpp`, 286 LOC, ~15 handlers) is a Linux logging stub.
- `ProcessSectorServerOpcode` (`proxy/ClientToSectorServer.cpp`, 757 LOC, ~50+ handlers) is a Linux logging stub.
- UDP proxy plane (`proxy/UDPProxyMVAS.cpp`, `UDPProxyToClient.cpp`, `UDPProxyToGlobal.cpp`, `UDPClient.cpp`) compiles to nothing on Linux.
- Login ticket handoff (login → game server) not wired. Win32 path is `RegisterSectorServer` over SSL on SSL_LOCALCERT_LOGIN_PORT (3807); endpoint not bound on Linux.
- `net7ssl` has no per-connection threading — single-request / single-response per TLS session. Fine for dev; not fine for load.
- `/who.cgi` is a 404 stub (depends on PlayerManager).

## Items

- [x] Bind TCP 3500 (SECTOR_SERVER_PORT) accept loop in the Linux proxy and publish it on the host docker-compose port map.
      Status: done — bind was already there (proxy/ServerManager.cpp:76 unconditional `TcpListener sector_comms(..., SECTOR_SERVER_PORT, ..., CLIENT_TO_SECTOR_SERVER)`); accepted sockets already route through the same Connection ctor / RC4 handshake / framed dispatch as 3801. Only missing piece was the host port publish.
      Touches: `docker-compose.yml` (added `"3500:3500/tcp"` under proxy.ports).
      Notes: Verified end-to-end: `exec 3<>/dev/tcp/127.0.0.1/3500` succeeds from the host, proxy logs `accept on port 3500 from 172.18.0.1` + `DoKeyExchange: ...` (handshake starts then fails on empty input — expected for a no-op test connection). Integration suite still 5/5.

- [ ] Port `ProcessMasterServerOpcode` chain end-to-end (avatar select → ServerRedirect). Currently `HandleMasterJoin` is the only handler exercised; full table at `proxy/ClientToMasterServer.cpp:Process…`.
      Status: not started
      Touches: `proxy/ClientToMasterServer.cpp` (already WIN32-unwalled), upstream callers

- [~] Port `ProcessGlobalServerOpcode` handlers. Group by dependency on UDP plane vs. pure-TCP — port pure-TCP ones first.
      Status: in progress — first pure-TCP handler (VersionRequest 0x0000 → VersionResponse 0x0001) ported and verified end-to-end. The five UDP-plane-dependent handlers (HandleGlobalConnect, HandleGlobalTicketRequest, HandleDeleteCharacter, HandleCreateCharacter, ProcessGlobalTicket) carry over — they all call into `g_ServerMgr->m_UDPConnection->SendTicket/DeleteCharacter/CreateCharacter/SendAvatarLogin`, which require the server-side UDP plane and the server-side AccountManager (MySQL access from the server container) to actually respond. The Linux dispatcher now lives in `proxy/ClientToServer_linux_stubs.cpp` (renamed conceptually but file kept for now); unimplemented opcodes still log + return for visibility into what real clients send.
      Touches done: `proxy/ClientToServer_linux_stubs.cpp` (real Linux ProcessGlobalServerOpcode dispatch with VersionRequest handler), `tests/client/version_request_test.cpp` (new live test), `tests/CMakeLists.txt` (build target), `justfile` (wire into `just integration-test`).
      Touches remaining: `proxy/ClientToGlobalServer.cpp` (still WIN32-walled — full removal needs the UDP plane).
      Notes: integration test 7/7 green. Proxy log confirms `<client> VersionRequest major=42 minor=0 -> status=0`.

- [ ] Port `ProcessSectorServerOpcode` handlers, same staging.
      Status: not started
      Touches: `proxy/ClientToSectorServer.cpp`

- [~] Port the UDP proxy plane (`UDPClient`, `UDPProxyMVAS`, `UDPProxyToClient`, `UDPProxyToGlobal`). POSIX UDP sockets + blocking recv thread.
      Status: minimum-viable subset done — HandleMasterJoin now drives a real SendMasterLogin -> server:3808 UDP exchange.
      Touches: `proxy/UDPClient_linux.cpp` (new, ~390 LOC; POSIX socket + bind to INADDR_ANY:0 ephemeral + connect to game-server:3808, pthread-launched blocking recv thread, full subset for SendMasterLogin + MasterLoginConfirm + WaitForResponse + FixedClientComm; getenv("NET7_GAME_SERVER_HOST") default "server" via getaddrinfo, no hardcoded IP), `proxy/ClientToMasterServer.cpp` (Linux HandleMasterJoin now mirrors the Win32 path: SendMasterLogin -> set port/ip/sector -> SendServerRedirect; falls back to the Phase J option-b local ServerRedirect on -1 timeout), `proxy/Net7.cpp` (Linux main constructs `UDPClient(MVAS_LOGIN_PORT, FIXED, ip_internal)` and registers it as BOTH m_UDPConnection and m_UDPClient — Win32 used two distinct FIXED+MULTI instances; on Linux we only port FIXED so one object serves both roles).
      Notes: docker logs show "UDPClient: resolved game server 'server' -> 172.18.0.6 / UDPClient: bound UDP <ephem> (src) -> 172.18.0.6:3808". Live integration test `MasterJoin.LiveMasterJoinReturnsServerRedirect` goes green; `just integration-test` 6/6. SendMasterLogin currently times out after ~5s (server-side HandleMasterHandoff is "Unimplemented opcode" stub — see server/src/ClientToGlobalServer.cpp:488) and falls through to the local ServerRedirect; the proxy wiring is correct end-to-end. Still WIN32-walled on Linux (not on the MasterJoin->ServerRedirect path; portable independently as their opcode handlers land): position-update thread (`UDPProxyMVAS::MVASThread`), client-side server-to-client forwarding (`UDPProxyToClient.cpp` — only relevant once client UDP is bridged), avatar/ticket/global-error UDP path (`UDPProxyToGlobal.cpp` minus the SendMasterLogin slice that lives in UDPClient_linux.cpp), keep-alive / packet-sequence resend, OpenMultiPort / CLIENT_TYPE_MULTI_PORT (client-only).

- [!] Wire ticket handoff: bind SSL_LOCALCERT_LOGIN_PORT (3807) on the game server, validate ticket against the same store that net7ssl issued from. Decide: shared MySQL `tickets` table vs. AF_UNIX SOCK_DGRAM push from login to server.
      Status: blocked / re-scoped after Win32 design discovery — there's no cross-process ticket state to share. The Win32 `AccountManager::GetUsernameFromTicket` (login-server/Net7SSL/AccountManager.cpp:839) and the server-side equivalent (`server/src/AccountManager.cpp`) both validate a ticket by `strchr(ticket, '-')` and taking the prefix as the username. The format is `<username>-<rand()>`, with no signature, MAC, or shared secret. "Validation" is effectively a username extraction; anyone who knows a valid username can construct a ticket the server will accept. SSL_LOCALCERT_LOGIN_PORT 3807 is the *other* direction (game server registers itself with login, not ticket transfer) — see Win32 `SSL_Connection.cpp:628 RegisterSectorServer(m_IpAddress, port_number, num_sectors, username)` which adds the sector server to the login service's known-sectors list for ServerRedirect routing decisions.
      Reason for `[!]`: closing as "blocked" because the original framing was wrong; building real ticket sharing now would diverge from Win32 parity. Real cryptographic ticket validation belongs in a future security-hardening Phase, not Phase K. The minimum-viable port already does the right thing — login issues `<user>-<rand>`, proxy/server parses with `strtok(ticket, "-")` and trusts it.
      Touches done (parity): `login-server/Net7SSL/LinuxAuth.cpp:82 BuildTicketLocked` (issues `<user>-<rand>`), `proxy/ClientToGlobalServer.cpp:134` (Win32 path: `strtok(ticket, "-")`).
      Touches remaining (parity): port the `strtok` line into the Linux HandleGlobalConnect when that handler is implemented (carries with `ProcessGlobalServerOpcode` item).
      Open follow-up (out of scope for Phase K): replace string-format ticket with HMAC-signed token, per RFC 6750-style bearer; shared MySQL or Redis ticket store; cap-share between processes via 60-second TTL.

- [x] End-to-end opcode round-trip test against the live proxy. `tests/client/master_join_test.cpp`: post-handshake, send MasterJoin (0x0035), expect ServerRedirect (0x0036) carrying SECTOR_SERVER_PORT (3500).
      Status: done — env-gated like the other live tests (`NET7_TEST_PROXY_HOST` / `NET7_TEST_PROXY_PORT`). Builds via `gtest_discover_tests`; run with `NET7_TEST_PROXY_HOST=127.0.0.1 NET7_TEST_PROXY_PORT=3801 ctest -R MasterJoin`.
      Touches: `tests/client/master_join_test.cpp`, `tests/CMakeLists.txt`.
      Notes: Verified pass against the running proxy container — `[ OK ] MasterJoin.LiveMasterJoinReturnsServerRedirect` + proxy logs `<client> MasterJoin avatar_id=0 ToSectorID=0 FromSectorID=0`. Probes for `port=3500` at offsets {8, 20} to handle both Win32 and Linux ServerRedirect layouts (see PacketStructures.h follow-up below).

- [ ] Replay-test promotion: `tests/client/replay_test.cpp` LivePostHandshakeReplay currently asserts only "send didn't error". Now that `master_join_test` proves real opcode dispatch, promote the replay test to assert opcode-specific responses too.
      Status: not started
      Touches: `tests/client/replay_test.cpp`, `tests/client/captures/`

- [x] Wire `master_join_test` into the `just integration-test` target.
      Status: done — added `master_join_test` to the cmake `--target` list and `MasterJoin` to the ctest `-R` filter. `just integration-test` now reports 6/6 green (5 prior tests + new MasterJoin round-trip).
      Touches: `justfile`

- [~] PacketStructures.h `long` → `int32_t` migration. The structs sent over the wire use `long`, which is 4 bytes on Win32 and 8 bytes on Linux x86_64. This makes MasterJoin 64 vs 108 bytes and ServerRedirect 10 vs 18 bytes. Half the bug also lived in `proxy/Connection.cpp` Linux SendResponse, which stamped `size = payload + sizeof(long)` (= 8) leaving a 4-byte gap between header and payload.
      Status: in progress — half the bug fixed. Linux SendResponse `sizeof(long)` → `sizeof(EnbTcpHeader)` (3 occurrences + comment) so the wire framing is correct regardless of `long` width. PacketStructures.h struct member migration still pending; touching it is multi-file (3 copies under proxy/, server/src/, login-server/Net7SSL/ — they are NOT identical, each has its own struct definitions and ordering) and affects every memcpy/reinterpret_cast across the codebase. Needs a real Win32-client test fixture to validate before merge.
      Touches done: `proxy/Connection.cpp` (Linux SendResponse), `tests/client/master_join_test.cpp` (probe offsets now {8, 16} not {8, 20} — the 4-byte gap is gone).
      Touches remaining: `proxy/PacketStructures.h`, `server/src/PacketStructures.h`, `login-server/Net7SSL/PacketStructures.h`, and any handler that memcpy's into these structs.
      Notes: integration test 6/6 green after the fix. Proxy log confirms `<client> MasterJoin avatar_id=0 ToSectorID=0 FromSectorID=0` → `SendMasterLogin timed out` → fallback ServerRedirect. The full PacketStructures.h migration is "right" but the failure mode it fixes (Win32-client interop) is not exercised today and the risk of breaking the Linux→Linux test path during a 600+-line edit across 3 files is high.

- [x] Per-connection threading in net7ssl (Linux) — detached worker thread per accepted SSL session so a slow handshake doesn't head-of-line block other connects.
      Status: done — `SSL_Listener::HandleAcceptedConnection` runs SSL_accept + read + dispatch + write + close on a detached `std::thread`. Capped at 64 concurrent workers (std::atomic counter); over-cap connections are refused with a log line so a flood can't fork-bomb. Lighter than the Win32 `SSL_Connection` model (no connection pool, no persistent thread per client) but matches the request shape — each TLS session is one round trip.
      Touches: `login-server/Net7SSL/SSL_Listener.h` (+13 LOC: atomic counter, HandleAcceptedConnection decl, <atomic> include), `login-server/Net7SSL/SSL_Listener.cpp` (-50/+65 LOC: inline accept body moves into HandleAcceptedConnection, listener launches detached thread).
      Notes: Verified end-to-end — fired 5 concurrent `curl https://.../AuthLogin` and the proxy log shows 5 interleaved "Accepted SSL connection" / "SSL handshake OK" / "HTTP: GET /AuthLogin" lines instead of strict serial ordering. `just integration-test` 6/6 still green.

- [ ] `/who.cgi` HTML output — depends on PlayerManager being reachable from net7ssl (currently it isn't).
      Status: not started
      Touches: `login-server/Net7SSL/LinuxAuth.cpp`
