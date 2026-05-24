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

- [ ] Port `ProcessGlobalServerOpcode` handlers. Group by dependency on UDP plane vs. pure-TCP — port pure-TCP ones first.
      Status: not started
      Touches: `proxy/ClientToGlobalServer.cpp`

- [ ] Port `ProcessSectorServerOpcode` handlers, same staging.
      Status: not started
      Touches: `proxy/ClientToSectorServer.cpp`

- [ ] Port the UDP proxy plane (`UDPClient`, `UDPProxyMVAS`, `UDPProxyToClient`, `UDPProxyToGlobal`). POSIX UDP sockets + `select`-driven recv loop.
      Status: not started
      Touches: `proxy/UDPClient.cpp`, `proxy/UDPProxyMVAS.cpp`, `proxy/UDPProxyToClient.cpp`, `proxy/UDPProxyToGlobal.cpp`

- [ ] Wire ticket handoff: bind SSL_LOCALCERT_LOGIN_PORT (3807) on the game server, validate ticket against the same store that net7ssl issued from. Decide: shared MySQL `tickets` table vs. AF_UNIX SOCK_DGRAM push from login to server.
      Status: not started
      Touches: `login-server/Net7SSL/LinuxAuth.cpp`, `server/src/`, `docker-compose.yml`

- [x] End-to-end opcode round-trip test against the live proxy. `tests/client/master_join_test.cpp`: post-handshake, send MasterJoin (0x0035), expect ServerRedirect (0x0036) carrying SECTOR_SERVER_PORT (3500).
      Status: done — env-gated like the other live tests (`NET7_TEST_PROXY_HOST` / `NET7_TEST_PROXY_PORT`). Builds via `gtest_discover_tests`; run with `NET7_TEST_PROXY_HOST=127.0.0.1 NET7_TEST_PROXY_PORT=3801 ctest -R MasterJoin`.
      Touches: `tests/client/master_join_test.cpp`, `tests/CMakeLists.txt`.
      Notes: Verified pass against the running proxy container — `[ OK ] MasterJoin.LiveMasterJoinReturnsServerRedirect` + proxy logs `<client> MasterJoin avatar_id=0 ToSectorID=0 FromSectorID=0`. Probes for `port=3500` at offsets {8, 20} to handle both Win32 and Linux ServerRedirect layouts (see PacketStructures.h follow-up below).

- [ ] Replay-test promotion: `tests/client/replay_test.cpp` LivePostHandshakeReplay currently asserts only "send didn't error". Now that `master_join_test` proves real opcode dispatch, promote the replay test to assert opcode-specific responses too.
      Status: not started
      Touches: `tests/client/replay_test.cpp`, `tests/client/captures/`

- [ ] Wire `master_join_test` into the `just integration-test` target. Currently `gtest_discover_tests` registers it but the recipe doesn't invoke ctest in a way that picks it up automatically (the existing suite is hardcoded). Should be one ctest invocation after the proxy/login containers are up.
      Status: not started
      Touches: `justfile`

- [ ] PacketStructures.h `long` → `int32_t` migration. The structs sent over the wire use `long`, which is 4 bytes on Win32 and 8 bytes on Linux x86_64. This makes MasterJoin 64 vs 108 bytes and ServerRedirect 10 vs 18 bytes, and `proxy/Connection.cpp` Linux SendResponse currently stamps `size = payload + sizeof(long)` (= 8) leaving a 4-byte gap between header and payload. A real Win32 client talking to a Linux proxy would fail; right now only the test client compiled on Linux works because both sides agree on long=8.
      Status: not started
      Touches: `proxy/PacketStructures.h`, `proxy/Connection.cpp` (SendResponse `length + sizeof(long)` → `length + sizeof(EnbTcpHeader)`), and any handler that memcpy's into these structs.

- [ ] Per-connection threading in net7ssl (Linux) — port `SSL_Connection`'s recv-thread model so multiple concurrent logins work.
      Status: not started
      Touches: `login-server/Net7SSL/LinuxAuth.cpp`, `login-server/Net7SSL/SSL_Listener.cpp`

- [ ] `/who.cgi` HTML output — depends on PlayerManager being reachable from net7ssl (currently it isn't).
      Status: not started
      Touches: `login-server/Net7SSL/LinuxAuth.cpp`
