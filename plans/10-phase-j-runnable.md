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

- [ ] Build login-server (Net7SSL) on Linux under OpenSSL 3.
      Status: deferred — secondary to Net7Proxy because captures show real game traffic uses plain TCP+Westwood on port 3801, not SSL.
      Touches: `login-server/`, new `login-server/CMakeLists.txt`, `SSLv23_server_method`→`TLS_server_method`, win32 shims
      Notes: ~6.7K LOC. `SSL_Listener.cpp` is already mostly Linux-aware (existing `#ifdef WIN32` paths). Needed for full auth flow; not needed for protocol-replay testing against the master/sector listeners.

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

- [ ] Update `plans/00-master.md` status table + `plans/99-decisions-log.md` entry for Phase J scope.
      Status: in progress (this file)
      Touches: `plans/00-master.md`, `plans/99-decisions-log.md`
