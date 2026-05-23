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

- [~] Verify server reaches a real "ready" state in docker — i.e., binds 3805 TCP + 3801 TCP + sector UDP ports inside the container.
      Status: in progress
      Touches: `server/src/MailslotManager.cpp` (POSIX replacement), `server/src/Net7.h` shim (`_beginthreadex` → pthread), `server/compat/`
      Notes: current state — server boots to "Registering sector server: port=3501, max_sectors=300", then spins in `GetMailslotInfo failed with 2` loop because mailslot IPC is stubbed AND `_beginthreadex` returns 0 (so sector threads never spawn, so `StartListener` never gets called, so UDP sector ports never bind). Only ephemeral port 0xADE9 listens inside the container; 3805/3801/3501 are unbound. Two follow-up items: (i) replace `_beginthreadex` shim with a real pthread launcher in the compat shim so `BeginSectorThread` succeeds; (ii) implement a minimal in-process mailslot replacement (queue-on-self) so the GetMailslotInfo poll quiets and `m_SectorAssignmentsComplete` flips true.

- [ ] Build login-server (Net7SSL) on Linux under OpenSSL 3.
      Status: blocked-deferred
      Touches: `login-server/`, new `login-server/CMakeLists.txt`, OpenSSL 3 SSLv2→TLS port, `_beginthreadex`/`HANDLE`/`LPTSTR`/`WSA*`/mailslot Win32 shims
      Notes: ~4500 LOC. Server boots without it — `RegisterSectorServer` calls are commented out in `ServerManager.cpp:143,166,244,245,317`. Deferred until the main server can demonstrably accept a client connection on its own.

- [ ] Test client: port Westwood RSA+RC4 handshake to a standalone C++ tool.
      Status: not started
      Touches: `tests/client/` (new), reference `server/src/Connection.cpp:215-255` (`DoClientKeyExchange`), `server/src/WestwoodRSA.h` (N=64 bytes, e=35; RC4 key=8 bytes)
      Notes: speaks UDP on sector port; first packet is an opcode 2003 key exchange.

- [ ] Test client: send Login opcode (0x02), parse server response.
      Status: not started
      Touches: `tests/client/`
      Notes: requires a working server-side login flow, which in turn requires Net7SSL or a stand-in.

- [ ] Test client: replay subset of captured packets, observe responses.
      Status: not started
      Touches: `tests/client/`, packet sources under `archive/kyp-snapshot/capturedPackets/` (RAR archives; ~120k packets per `docs/03-network-protocol.md` §8 histogram)
      Notes: scope = full replay-from-captures game client per Phase J directive.

- [ ] Wire integration tests into CI.
      Status: not started
      Touches: `.github/workflows/build.yml`, new compose service for client harness
      Notes: dependent on a working server steady-state.

- [ ] Update `plans/00-master.md` status table + `plans/99-decisions-log.md` entry for Phase J scope.
      Status: in progress (this file)
      Touches: `plans/00-master.md`, `plans/99-decisions-log.md`
