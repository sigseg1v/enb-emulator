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

- [ ] Bind TCP 3500 (SECTOR_SERVER_PORT) accept loop in the Linux proxy and route accepted sockets through the same `Connection` ctor used for 3801/3805 (with `m_ServerType=CONNECTION_TYPE_GAME_TO_SECTOR_SERVER`).
      Status: not started
      Touches: `proxy/ServerManager.cpp`, `proxy/TcpListener.cpp`, `proxy/Connection.cpp`

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

- [ ] Replay-test promotion: `tests/client/replay_test.cpp` LivePostHandshakeReplay currently asserts only "send didn't error". Once opcode dispatch lands, assert real opcode responses (e.g. MasterJoin → ServerRedirect 0x??).
      Status: not started
      Touches: `tests/client/replay_test.cpp`, `tests/client/captures/`

- [ ] Per-connection threading in net7ssl (Linux) — port `SSL_Connection`'s recv-thread model so multiple concurrent logins work.
      Status: not started
      Touches: `login-server/Net7SSL/LinuxAuth.cpp`, `login-server/Net7SSL/SSL_Listener.cpp`

- [ ] `/who.cgi` HTML output — depends on PlayerManager being reachable from net7ssl (currently it isn't).
      Status: not started
      Touches: `login-server/Net7SSL/LinuxAuth.cpp`
