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

- [ ] Stand up TCP listeners for client handshake — currently nobody listens on 3801 (MASTER_SERVER_PORT) or 3805 (GLOBAL_SERVER_PORT) or 443 (SSL_PORT).
      Status: not started — this is the actual blocker for any client connection.
      Touches: `proxy/` (port Net7Proxy to Linux) and/or `login-server/Net7SSL/`
      Notes: Topology per captured packets at `archive/kyp-snapshot/capturedPackets/capture_1.txt`:
        - Client → TCP 3801 (Net7Proxy): 4-step Westwood RSA+RC4 handshake (SYN1/ACK1+RSA/SYN2+session-key/ACK2+CORD-port). Captures show this is plain TCP, not SSL.
        - Then client → per-sector port (UDP, e.g. 3387 in capture) for game opcodes.
        - SSL on port 443 only for initial auth (Net7SSL).
      `proxy/ServerManager.cpp:61` is the listener: `TcpListener master_tcp_listener(m_IpAddressInternal, MASTER_SERVER_PORT, *this, CONNECTION_TYPE_CLIENT_TO_MASTER_SERVER)`. ~9.5K LOC of 2010 Windows code; ~11 of its 35 files have `#ifdef WIN32` Linux-aware blocks already, so port surface is real but tractable.

- [ ] Build login-server (Net7SSL) on Linux under OpenSSL 3.
      Status: deferred — secondary to Net7Proxy because captures show real game traffic uses plain TCP+Westwood on port 3801, not SSL.
      Touches: `login-server/`, new `login-server/CMakeLists.txt`, `SSLv23_server_method`→`TLS_server_method`, win32 shims
      Notes: ~6.7K LOC. `SSL_Listener.cpp` is already mostly Linux-aware (existing `#ifdef WIN32` paths). Needed for full auth flow; not needed for protocol-replay testing against the master/sector listeners.

- [ ] Test client: port Westwood RSA+RC4 handshake to a standalone C++ tool.
      Status: not started
      Touches: `tests/client/` (new), reference `proxy/WestwoodRSA.cpp` + `proxy/WestwoodRC4.cpp` (already Linux-portable), captures at `archive/kyp-snapshot/capturedPackets/capture_1.txt:216-219` (SYN1/ACK1/SYN2/ACK2 exchange)
      Notes: Client logic can be developed and unit-tested independently of the live server (against canned captures).

- [ ] Test client: send Login opcode (0x02), parse server response.
      Status: not started
      Touches: `tests/client/`
      Notes: requires a working server-side TCP listener (Net7Proxy port) plus the Westwood handshake completed.

- [ ] Test client: replay subset of captured packets, observe responses.
      Status: not started
      Touches: `tests/client/`, packet sources under `archive/kyp-snapshot/capturedPackets/` (3 RAR archives, decoded text format with hex bytes + opcode annotations)
      Notes: scope = replay engine that parses the capture text format, drives the same packet sequence, and asserts server responses match.

- [ ] Wire integration tests into CI.
      Status: not started
      Touches: `.github/workflows/build.yml`, new compose service for client harness
      Notes: dependent on Net7Proxy port + working test client.

- [ ] Update `plans/00-master.md` status table + `plans/99-decisions-log.md` entry for Phase J scope.
      Status: in progress (this file)
      Touches: `plans/00-master.md`, `plans/99-decisions-log.md`
