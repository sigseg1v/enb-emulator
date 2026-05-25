# 09 - Running locally

How to bring up the dev stack and talk to it.

If you want to play the game and not run a server, you do not need any of
this — the Linux client installer connects to the public Net-7 server.
See `08-build.md` "Linux client (game client, not server)".

## Prerequisites

- Docker and the Docker Compose v2 plugin.
- `just` (task runner).
- The dependencies in `08-build.md` if you also want host-side builds.
- ~2 GB free disk for the MySQL data volume.

## Dev stack at a glance

`docker-compose.yml` brings up four services by default and a few opt-in
profiles. The default topology:

| Service | Image | Purpose | Host port |
|---|---|---|---|
| `mysql` | `mysql:8.0` | Holds `net7` (content) and `net7_user` (accounts). Auto-loads both dumps from `db/mysql/` on first start. | 3307 → 3306 |
| `proxy` | built from `proxy/Dockerfile` | Net7Proxy. Binds `MASTER_SERVER_PORT` (3801), `GLOBAL_SERVER_PORT` (3805), and `SECTOR_SERVER_PORT` (3500) for the Westwood RSA + RC4 client handshake and downstream dispatch. | 3801, 3805, 3500 |
| `login` | built from `login-server/Dockerfile` | Net7SSL. Binds `SSL_PORT` (443 internally, remapped to host **4443** so rootless docker accepts publishing it). Talks to MySQL for auth. | 4443 → 443 |
| `server` | built from `server/Dockerfile` | The C++ Net-7 game server. Owns the dynamic sector-server TCP range (3501-3550 published) plus 3808/UDP (master ↔ proxy). | 3501-3550, 3808/udp |

The four services share an AF_UNIX SOCK_DGRAM IPC volume mounted at
`/run/net7-ipc/` (the Phase M replacement for the Win32 mailslot pair).
The login container's entrypoint chmod's the directory to `0777` so the
server container (running as `net7:net7`, uid 999) can also bind a
datagram socket there. Same-host trust model — see
`common/include/net7/PosixIpc.h`.

Opt-in profiles:

- `--profile postgres`: brings up `postgres` (Postgres 16) and a
  `schema-init` one-shot that applies `db/postgres/schema.sql` and
  `seed.sql`. Staging for the eventual cutover; not the runtime DB.
- `--profile dev-tools`: phpMyAdmin against the mysql container on
  `:8081`.
- `--profile dev-tools-postgres`: pgAdmin against the postgres container
  on `:8080`.

## Bring up the stack

```sh
just init     # first-time-only — boots mysql and waits for the dumps to load
just dev      # = just run-stack-bg — server + proxy + login in the background
```

`just dev` is equivalent to `docker compose up --build -d`. Tear down with
`just down` (or `docker compose down`). Add `-v` if you also want to drop
the volumes (`mysqldata`, `net7-ipc`, `pgdata`).

The CLI-driven integration test suite verifies the running stack:

```sh
just test-integration         # runs the Phase T xUnit suite end-to-end (33/33)
```

See `docs/16-integration-tests.md` for the test architecture.

## Apply / refresh the schema manually

The MySQL dumps are auto-loaded on first stack boot; to re-apply them
manually:

```sh
docker compose exec -T mysql mysql -unet7 -pnet7 net7 < db/mysql/net7.sql
docker compose exec -T mysql mysql -unet7 -pnet7 net7_user < db/mysql/net7_user.sql
```

For the staged Postgres conversion (opt-in profile):

```sh
docker compose --profile postgres up postgres schema-init
# Or against the host:
psql -h localhost -U net7 -d net7 -f db/postgres/schema.sql
psql -h localhost -U net7 -d net7 -f db/postgres/seed.sql
```

`db/postgres/convert.sh` is the script that produced `schema.sql` and
`seed.sql` from `db/mysql/net7.sql`. The C++ DAO migration to libpqxx
happened in Phase N — see `mysqlplus.cpp`. A handful of DAOs still use
the MySQL path; Phase N Wave 3 tracks the rest.

## Create a test account

The fastest path for local dev is a direct DB insert against the live
MySQL container. The exact column layout is in
`docs/06-database-schema.md` ("`net7_user.sql` group"); the password hash
format is whatever Net-7's login flow expects. Reuse the existing
Net7Mysql / Net7SSL hashing logic — do not invent a new format.

```sh
docker compose exec mysql mysql -unet7 -pnet7 net7_user
```

Or use the CLI client (Phase S) to drive the registered account-creation
flow — see `docs/15-cli-client.md`.

## Connect a client

### Linux client pointed at local server

1. Install the client per `08-build.md`:
   ```sh
   client/linux-installer/install-enb-linux.sh
   ```
2. The launcher configuration ships pointing at the public Net-7 server.
   Redirect to localhost by editing the launcher's INI inside the WINE
   prefix — exact path is documented in `client/linux-installer/README.md`.
3. Replace the login-server host with `127.0.0.1` and the SSL port with
   `4443` (the host-side remap of the container's port 443). The proxy
   ports (3801, 3805, 3500) are published as-is.
4. Start the launcher under WINE; it should connect to the local
   login-server, advance to character select, then sector select.

### Windows client pointed at local server

Same idea: edit the launcher INI or `Config.xml` to point at the local
host. Native Windows client; no WINE involved.

### Headless / scripted client

The Phase S CLI client (`tools/cli-client/`) drives the same wire
protocol from a C# command-line binary — useful for scripted reproduction
of bug reports, integration tests, and packet-level traces without a
graphical client. See `docs/15-cli-client.md`.

## Troubleshooting

**`docker compose up` complains about port 3307 already in use** — you
have a host MySQL running. Either stop it or remap the port in
`docker-compose.yml` (`3308:3306` and update your DB clients).

**`docker compose up` complains about port 4443 already in use** — same
idea, change the host side of the mapping in the `login` service.

**MySQL logs "permission denied" on dump files** — the volume mount maps
`db/mysql/` to `/dumps/` and `db/mysql/init/` to
`/docker-entrypoint-initdb.d/` inside the container; check the file
permissions (`chmod a+r db/mysql/*.sql db/mysql/init/*.sql`).

**Server container exits immediately** — check `docker compose logs server`.
Most common causes: missing certs in `deploy/certs/` (run `just gen-certs`),
or the login container hasn't chmod'd `/run/net7-ipc/` yet (server
depends_on login, so a slow login start can race the first attempt —
`docker compose restart server` usually fixes it).

**Client cannot find login server** — the client connects to the hostname
embedded in the launcher INI, not to "localhost" by default. Edit the INI
to point at `127.0.0.1` (client running on the same host as the compose
stack) or your local network IP. The dev stack remaps
`local.net-7.org` → `127.0.0.1` via `extra_hosts:` so a launcher INI
pointing at `local.net-7.org` works inside the compose network.

## What is intentionally not here

- Production deployment guidance. The project is non-commercial; we do
  not ship operator runbooks.
- A "demo account" with a public password. Set up your own.
- A polished web admin panel. The Avalonia editors are the admin tool;
  see `07-tools-toolchain.md`.
- Scaling guidance. The original architecture sharded sector servers but
  the practical limit on modern hardware is "a handful of concurrent
  testers". Anything beyond that is unverified.
