# Phase J — runnable end-to-end

Goal: make `docker compose up` produce a server that actually accepts client connections, with a DB that initialises itself, and ship integration tests that drive a captured-packet replay client.

This phase exists because Phases A–I delivered a server that compiles and an image that boots, but did not validate that the server **runs to a steady-state listening state** on Linux nor that a client can complete a login handshake against it.

## Items

- [x] DB init pipeline: `mysql:8.0` service in docker-compose with bootstrap SQL + dump-load shell scripts in `db/mysql/init/`.
      Status: done
      Touches: `docker-compose.yml`, `db/mysql/init/00-create-databases.sql`, `db/mysql/init/01-load-net7.sh`, `db/mysql/init/02-load-net7-user.sh`, `db/mysql/init/README.md`
      Notes: ~6 min one-shot load of the 55K-line net7 + 3K-line net7_user dumps; subsequent boots reuse the `mysqldata` named volume.

- [x] `just init` / `just run-stack` / `just seed-account` orchestration targets.
      Status: done
      Touches: `justfile`
      Notes: seed-account took two iterations to get right (sh-vs-bash + wrong column names; 2010 schema is `status, formname, email, last_login, last_logout, warn_level`).

- [x] Dev SSL cert generation: `just gen-certs` produces `deploy/certs/local.net-7.org.{cer,pem}` via `openssl req -x509 -newkey rsa:2048 -addext "subjectAltName=DNS:local.net-7.org,DNS:localhost,IP:127.0.0.1"`.
      Status: done
      Touches: `justfile`, `.gitignore` (private keys never committed)
      Notes: server's `SSL_Listener.cpp:56-57` reads `<g_DomainName>.cer` and `.pem` from CWD; docker-compose bind-mounts these into `/app/`.

- [x] `deploy/Net7Config.cfg` template that matches the docker-compose mysql service (host `mysql:3306`, user/pass `net7/net7`, dbs `net7` + `net7_user`).
      Status: done
      Touches: `deploy/Net7Config.cfg`, `docker-compose.yml`
      Notes: parser is `strtok_s` with alternating `=`/`\n` delimiters in `Net7.cpp:100-221`.

- [x] MySQL SSL workaround: vendored `server/src/mysql/mysql.h` is 2010-era and predates `MYSQL_OPT_SSL_MODE`. libmysqlclient 8.0+ defaults to `SSL_MODE_PREFERRED`, which fails against our latin1 mysql container with `Unable to get certificate from ''`. Workaround: cast the literal enum value (35 in 8.0.x) to `enum mysql_option` and call `mysql_options` with `SSL_MODE_DISABLED` (=1).
      Status: done
      Touches: `server/src/mysql/mysqlplus.cpp:~254`
      Notes: keeps the vendored header intact; replace when we migrate to a modern MySQL connector or libpqxx.

- [x] Game content staging into `server/data/` (164 files, 4.6MB: cbasset.xml, SectorContent.xml, ItemBase.xml, EarthSector*.dat, etc.) copied verbatim from tada-o `Source Code/database/`.
      Status: done
      Touches: `server/data/`, `docker-compose.yml` (mount `./server/data:/database:ro`)
      Notes: `SERVER_DATABASE_PATH = "../database/"` (`Net7.h:143`); with `WORKDIR=/app` that resolves to `/database/`.

- [x] Server `Dockerfile` runtime stage updated for noble (`libcrypto++8t64`, `libpqxx-7.8t64`) + `/app/logs` mkdir + chown.
      Status: done
      Touches: `server/Dockerfile`
      Notes: t64 transition rename — `libcrypto++8` and `libpqxx-7.7` don't exist on ubuntu:24.04.

- [x] Verify server reaches a real "ready" state in docker — sector UDP ports bind successfully.
      Status: done (with caveat — TCP ports are a separate problem, see next item)
      Touches: `server/src/Net7.h` (quieted `GetMailslotInfo` poll spam at line 301)
      Notes: Actual state observed in container via `/proc/net/udp`: 196 UDP sockets bound, span 0x0DAD–0x0EE0 (3501–3808), covering all sector ports + UDP_MASTER (3808). The previous "_beginthreadex returns 0 so sectors don't bind" diagnosis was wrong: SectorManager calls `UDP_Connection(port, …)` synchronously after `_beginthreadex`, and `UDP_Connection`'s ctor binds the socket directly with `pthread_create` (not `_beginthreadex`) for its receive loop. The dead `_beginthreadex` return only loses the RunEventThreadAPI thread, which matters only once players are joined. The "GetMailslotInfo failed with 2" log spam was the visible symptom but not the blocker; quieted by changing the inline stub in `Net7.h:301` to return TRUE with 0 messages (MAILSLOT_NO_MESSAGE).

- [x] Stand up TCP listeners for client handshake — bind 3801 (MASTER_SERVER_PORT) and 3805 (GLOBAL_SERVER_PORT) on Linux + run real Net-7 RSA+RC4 handshake.
      Status: done (bind + accept + Net-7 DoKeyExchange verified end-to-end; live tests pass against the running container).
      Touches: `proxy/CMakeLists.txt` (new), `proxy/Dockerfile` (new), `docker-compose.yml` (new `proxy` service; removed 3801/3805 from `server`), `proxy/Net7.h` (Linux includes + win32 shims), `proxy/Net7.cpp` (new Linux main; legacy client-launcher main `#ifdef WIN32`-wrapped), `proxy/Connection.cpp` (file-level `#ifdef WIN32` + Linux stub Connection class that accepts then closes), `proxy/SSL_Connection.cpp` / `proxy/SSL_Listener.cpp` / `proxy/ServerManager.cpp` / `proxy/SectorServerManager.cpp` (Linux-portability shims), `proxy/UDPClient.cpp` / `proxy/UDPProxyMVAS.cpp` / `proxy/UDPProxyToClient.cpp` / `proxy/UDPProxyToGlobal.cpp` / `proxy/ClientToMasterServer.cpp` / `proxy/ClientToGlobalServer.cpp` / `proxy/ClientToSectorServer.cpp` (file-level WIN32 guards), `proxy/SectorManager.h` (new stub forward decl), `proxy/compat/` (copy of `server/compat/` — `win32_shim.h`, `threading_shim.{h,cpp}`).
      Verification: `docker compose exec proxy ss -tlnp` shows `0.0.0.0:3500 0.0.0.0:3801 0.0.0.0:3805` all owned by `net7proxy pid=1`. Host-side `bash -c 'exec 3<>/dev/tcp/127.0.0.1/3801'` returns 0. `docker compose logs proxy` shows `Net7Proxy (stub): accept on port 3801 from 172.18.0.1 — closing` lines, confirming the accept path runs end-to-end.
      Limits / honest accounting:
        - **Handshake works**: Linux accept handler now runs `DoKeyExchange()` (74-byte pubkey send → 4+64-byte encrypted-key recv → RC4 session install). Confirmed in proxy logs: `DoKeyExchange: RC4 session established on port 3801`. Live ctest passes.
        - **Opcode dispatch still stubbed**: after handshake, the recv thread drains inbound bytes through `m_CryptIn.RC4()` and discards them. `ClientToMasterServer.cpp` / `ClientToGlobalServer.cpp` / `ClientToSectorServer.cpp` are still `#ifdef WIN32`-walled. Live replay test sends client→server packets and verifies they don't error on the wire, but server response opcodes are not yet produced.
        - **UDP proxy plane still stubbed**: `UDPProxyMVAS.cpp`, `UDPProxyToClient.cpp`, `UDPProxyToGlobal.cpp`, `UDPClient.cpp` compile to nothing on Linux. No sector-handoff path.
        - `SSL_Connection` is stubbed; the SSL listener is gated off on Linux (`g_LocalCert` false).
        - `SSLv2_client_method` (removed in OpenSSL 3) was sidestepped by making `RegisterSectorServer` return true early on Linux; the upstream-auth call site is dead code in a single-host dev stack but still merits a follow-up.
        - The `_beginthreadex` → `pthread_create` shim under `proxy/compat/` is a copy of `server/compat/` and shares the same Phase J caveat: lone-return value (thread handle) is null, which only matters for thread-handle-bearing call sites.
      Topology reference (unchanged): `archive/kyp-snapshot/capturedPackets/capture_1.txt`.

- [x] Build login-server (Net7SSL) on Linux under OpenSSL 3.
      Status: done — port 443 binds, TLSv1.3 handshake completes end-to-end via `openssl s_client -connect 127.0.0.1:4443 -servername local.net-7.org`. Heavy machinery (opcode dispatch, MySQL account/character/station tables, MailManager IPC, UDPClient bus) remains WIN32-walled — same Phase J pattern as Net7Proxy.
      Touches: `login-server/Net7SSL/CMakeLists.txt` (new), `login-server/Net7SSL/Net7SSL.h` (Linux compat-shim include), `login-server/Net7SSL/Net7SSL.cpp` (file-level WIN32 wall + Linux main with SSL_Listener bind), `login-server/Net7SSL/SSL_Listener.cpp` (SSLv23 → TLS_server_method; cert-load failure downgraded to warning so listener binds without a cert; Linux accept branch drives SSL_accept() inline then closes), `login-server/Net7SSL/Connection.cpp`, `login-server/Net7SSL/ClientToGlobalServer.cpp`, `login-server/Net7SSL/ConnectionManager.cpp`, `login-server/Net7SSL/AccountManager.cpp`, `login-server/Net7SSL/UDPClient.cpp`, `login-server/Net7SSL/TcpListener_B.cpp`, `login-server/Net7SSL/SSL_ServerManager.cpp`, `login-server/Net7SSL/SSL_Connection.cpp` (all file-level `#ifdef WIN32` walls), `login-server/Net7SSL/Mutex.{h,cpp}` (new, copied from proxy/), `login-server/Net7SSL/WestwoodRSA.cpp` + `WestwoodRC4.cpp` (Net7.h → Net7SSL.h include fix), `login-server/Net7SSL/compat/` (copy of `proxy/compat/`), `login-server/Dockerfile` (rewritten — was stub), `docker-compose.yml` (real login service with cert mounts and `4443:443` map).
      Verification: `docker compose ps login` shows `Up`, `0.0.0.0:4443->443/tcp`. `docker compose exec login ss -tlnp` shows `LISTEN 0.0.0.0:443 net7ssl pid=1`. `openssl s_client -connect 127.0.0.1:4443` returns server cert + completes TLSv1.3 handshake; login logs the `SSL handshake OK (TLSv1.3)` line for each connection.
      Limits / honest accounting:
        - Linux net7ssl is a **handshake-only** stub today. After `SSL_accept()` returns, the connection is closed immediately. No HTTP/auth request parsing, no MySQL ticket lookup, no `AuthLogin`/`SectorServer`/`TouchSession` endpoint handlers.
        - The `AccountManager` (MySQL-driven), `ConnectionManager`, `MailManager` (mailslot IPC), `UDPClient` (MVAS keep-alive bus), and the entire `Connection_B` blocking-TCP machinery are WIN32-only and link to nothing on Linux.
        - The login service runs as **root** inside the container because port 443 needs CAP_NET_BIND_SERVICE; the proxy/server services keep their unprivileged user. Production should set the cap on the binary instead.
        - Host port published as **4443** (not 443) because rootless Docker forbids publishing host ports below 1024. The container still binds 443 internally so protocol behaviour matches; deployments on rootful Docker can flip the port map to `443:443`.
      Out-of-scope for Phase J (would all be a Phase K continuation): port `AuthLogin` HTTP handler + MySQL ticket store; port `ConnectionManager` cleanup loop using `std::thread`+`std::mutex`; port `UDPClient` using POSIX UDP socket; port `MailManager` over a UNIX socket or shared `std::queue`+`std::mutex` (mailslot IPC has no direct POSIX equivalent).

- [x] Test client: port Westwood RSA+RC4 handshake to a standalone C++ tool.
      Status: done (commits 7472211..ba791a0)
      Touches: `tests/client/westwood/westwood_rsa.{h,cpp}`, `tests/client/westwood/westwood_rc4.{h,cpp}`, `tests/client/capture_parser.{h,cpp}`, `tests/client/handshake_test.cpp`
      Notes: Self-contained (no Net7.h dependency). Six gtest cases pin: capture parser correctness, modulus matches captured ACK1 packet bytes 17..80, RSA encrypt/decrypt round-trip, RC4 round-trip + RFC 6229 test vector, and decrypt of captured SYN2 recovers the annotated session key at plaintext[63..56] reversed (per `proxy/Connection.cpp:230-268` DoClientKeyExchange).

- [x] Test client: full 4-step handshake driver over TCP (live + loopback).
      Status: done (commit ba791a0); upgraded to also include a Net-7-protocol variant for live tests against the actual proxy.
      Touches: `tests/client/tcp_client.{h,cpp}`, `tests/client/handshake_driver.{h,cpp}`, `tests/client/handshake_live_test.cpp`
      Notes: Two handshake variants live in `handshake_driver.cpp`:
        - `RunClientHandshake()` — Westwood-envelope (4-step SYN1/ACK1/SYN2/ACK2) matching the captures. Used by the loopback self-test where both sides speak this protocol.
        - `RunNet7Handshake()` — raw RSA exchange Net-7 actually speaks (server sends 74-byte pubkey on connect, client sends 4+64-byte encrypted-key block, no ACK). Used by the live test against the proxy container.
      The original docs incorrectly conflated the two; Net-7's `Connection::DoKeyExchange` strips the Westwood session envelope entirely. Loopback test verifies the client-chosen RC4 key matches what the synthetic server decoded; live test verifies the proxy accepts our key block without error.

- [x] Test client: replay subset of captured packets, observe responses.
      Status: done (commit 4e2d192) — login (opcode 0x02) and any other client→server opcode is subsumed by the generic replay engine, no special-case needed.
      Touches: `tests/client/replay.{h,cpp}`, `tests/client/replay_test.cpp`, `tests/client/captures/capture_1_post_handshake.txt`
      Notes: `RunReplay()` walks a filtered packet list, sends each Client→Server packet (RC4-encrypted if `apply_rc4`), and reads + decrypts the expected response for Server→Client packets. Opcode-only equality check on responses (full byte-compare would fail — server state diverges run-to-run). Offline tests pass; live test is env-gated.

- [~] Wire integration tests into CI.
      Status: partial — offline tests (handshake parser, RSA round-trip, RC4, capture parser, loopback handshake, post-handshake opcode pinning) build and run in the existing ctest CI job. Live tests pass locally via `just integration-test` against the proxy container; CI activation needs a docker-compose-up step in the ctest workflow before setting `NET7_TEST_PROXY_HOST`.
      Touches: `.github/workflows/build.yml` (added libssl-dev), `tests/CMakeLists.txt`
      Notes: 14/14 ctest pass locally (12 ran + 2 properly skipped). Adding `NET7_TEST_PROXY_HOST` to the CI step will activate the live tests once the proxy listener is reachable from the runner.

- [x] Replace Win32 mailslot IPC with AF_UNIX SOCK_DGRAM (server <-> login keepalive bus).
      Status: done
      Touches: `server/compat/posix_ipc.{h,cpp}` (new), `server/src/MailslotManager.cpp` (file-level WIN32 wall + POSIX impl), `server/src/Net7.cpp` (Linux socket path globals), `login-server/Net7SSL/compat/posix_ipc.{h,cpp}` (copy), `login-server/Net7SSL/MailslotManager.cpp` (new — restores Win32 impl that was missing from the tada-o snapshot + adds POSIX impl), `login-server/Net7SSL/Net7SSL.cpp` (Linux globals + MailMgr wired into main loop with the same 10s send / 60s recv-watchdog cadence as Win32 `main_prog`), `login-server/Net7SSL/ConnectionManager.cpp` (removed duplicate `MailManager::HandleMessage()` body — now lives portably in MailslotManager.cpp), `docker-compose.yml` (new shared named volume `net7-ipc:` bind-mounted at `/run/net7-ipc/` in both server and login containers; login entrypoint chmods to 0777 on startup so the unprivileged server user can also bind there; `depends_on: login service_started` enforces ordering).
      Notes: Datagram semantics (message-not-stream) map 1:1 onto Win32 mailslots — no framing layer needed. Wire format ceiling is 1024 bytes (matches mailslot buffer). Actual production traffic in this codebase is a single 5-byte "Ping" every 10s; the elaborate opcode/slot/length fields in the legacy wire format are defined but never dispatched. PosixIpc opens recv socket eagerly (so the peer can bind even if we're not running) and the send socket lazily (so we don't fail at startup if the peer isn't up yet). Watchdog: if no datagram seen for 60s, the loop exits — same behaviour as Win32. `LOCAL_PING_SSL_SERVER`/`LOCAL_PING_SERVER_SSL` opcodes (0x04/0x05) are still defined but unused; HandleMessage() reduces to "update g_receive_time" on both sides.

- [ ] Update `plans/00-master.md` status table + `plans/99-decisions-log.md` entry for Phase J scope.
      Status: in progress (this file)
      Touches: `plans/00-master.md`, `plans/99-decisions-log.md`
