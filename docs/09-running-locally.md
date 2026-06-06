# 09 - Running locally

How to bring up the dev stack and talk to it.

If you want to play the game and not run a server, you do not need any of
this -- the Linux client installer connects to the public Net-7 server.
See `08-build.md` "Linux client (game client, not server)".

## Prerequisites

- Docker and the Docker Compose v2 plugin.
- `just` (task runner).
- The dependencies in `08-build.md` if you also want host-side builds.
- ~2 GB free disk for the Postgres data volume.

## Dev stack at a glance

`docker-compose.yml` brings up four services by default and a few opt-in
profiles. The default topology:

| Service | Image | Purpose | Host port |
|---|---|---|---|
| `postgres` | `postgres:16` | The runtime database. Holds the `net7` (content) and `net7_user` (accounts) databases. The `schema-init` one-shot applies `db/postgres/schema.sql` and the seed scripts on first start. | 5434 -> 5432 |
| `proxy` | built from `proxy/Dockerfile` | Net7Proxy. Binds `MASTER_SERVER_PORT` (3801), `GLOBAL_SERVER_PORT` (3805), and `SECTOR_SERVER_PORT` (3500) for the Westwood RSA + RC4 client handshake and downstream dispatch. | 3801, 3805, 3500 |
| `login` | built from `login-server/Dockerfile` | Net7SSL. Binds `SSL_PORT` (443 internally, remapped to host **4443** so rootless docker accepts publishing it). Reads the `net7_user` database on Postgres for auth. | 4443 -> 443 |
| `server` | built from `server/Dockerfile` | The C++ Net-7 game server. Owns the dynamic sector-server TCP range (3501-3550 published) plus 3808/UDP (master to proxy). | 3501-3550, 3808/udp |

The server, proxy, and login services share an AF_UNIX SOCK_DGRAM IPC
volume mounted at `/run/net7-ipc/`. The login container's entrypoint
chmod's the directory to `0777` so the server container (running as
`net7:net7`, uid 999) can also bind a datagram socket there. Same-host
trust model -- see `common/include/net7/PosixIpc.h`.

Opt-in profiles:

- `--profile mysql-legacy`: brings up `mysql` (MySQL 8.0) loading the
  historical `db/mysql/` dumps. Reference only; not the runtime DB.
- `--profile dev-tools-postgres`: pgAdmin against the postgres container
  on `:8080`.

## Bring up the stack

```sh
just init     # first-time-only: boots Postgres and applies the schema
just dev      # = just run-stack-bg: server + proxy + login in the background
```

Tear down with `just down` (or `docker compose down`). `just nuke`
(`docker compose down -v`) also drops the volumes, including `pgdata`, so
`just init` reloads the schema next time.

The CLI-driven integration test suite verifies the running stack:

```sh
just integration-test         # runs the xUnit suite end-to-end
```

See `docs/16-integration-tests.md` for the test architecture.

## Apply / refresh the schema manually

`just init` runs the `schema-init` one-shot, which applies the schema to
the `net7` and `net7_user` databases on first boot. To re-apply against
the host:

```sh
psql -h localhost -p 5434 -U net7 -d net7 -f db/postgres/schema.sql
psql -h localhost -p 5434 -U net7 -d net7 -f db/postgres/seed.sql
```

`db/postgres/convert.sh` is the script that produced `schema.sql` and
`seed.sql` from the historical `db/mysql/net7.sql` dump. The C++ server
and login-server talk to Postgres through libpqxx (`server/src/db/sqlplus.cpp`,
the login-server's `LinuxAuth.cpp`).

## Create a test account

The fastest path for local dev is the `just seed-account` recipe, which
creates an account against the `net7_user` database with the hashing the
login flow expects:

```sh
just seed-account myuser mypass
```

To poke at the accounts database directly, `just psql-user` opens a psql
client against `net7_user`. The exact column layout is in
`docs/06-database-schema.md`. Reuse the existing login hashing logic;
do not invent a new format.

Or use the CLI client to drive the registered account-creation flow --
see `docs/15-cli-client.md`.

## Connect a client

### Linux client pointed at local server

1. Install the client per `08-build.md`:
   ```sh
   client/linux-installer/install-enb-linux.sh
   ```
2. The launcher configuration ships pointing at the public Net-7 server.
   Redirect to localhost by editing the launcher's INI inside the WINE
   prefix -- exact path is documented in `client/linux-installer/README.md`.
3. Replace the login-server host with `127.0.0.1` and the SSL port with
   `4443` (the host-side remap of the container's port 443). The proxy
   ports (3801, 3805, 3500) are published as-is.
4. Start the launcher under WINE; it should connect to the local
   login-server, advance to character select, then sector select.

### Windows client pointed at local server

Same idea: edit the launcher INI or `Config.xml` to point at the local
host. Native Windows client; no WINE involved.

### Headless / scripted client

The CLI client (`tools/cli-client/`) drives the same wire
protocol from a C# command-line binary -- useful for scripted reproduction
of bug reports, integration tests, and packet-level traces without a
graphical client. See `docs/15-cli-client.md`.

## Troubleshooting

**`docker compose up` complains about port 5434 already in use** -- you
have a host Postgres running. Either stop it or remap the port in
`docker-compose.yml` (`5435:5432` and update your DB clients).

**`docker compose up` complains about port 4443 already in use** -- same
idea, change the host side of the mapping in the `login` service.

**`schema-init` errors applying the schema** -- check
`docker compose logs schema-init`. It is idempotent: it probes for an
existing `net7.item_base` before applying `schema.sql`, so a re-run on a
populated cluster skips cleanly. A hard reset is `just nuke` followed by
`just init`.

**Server container exits immediately** -- check `docker compose logs server`.
Most common causes: missing certs in `deploy/certs/` (run `just gen-certs`),
or the login container hasn't chmod'd `/run/net7-ipc/` yet (server
depends_on login, so a slow login start can race the first attempt --
`docker compose restart server` usually fixes it).

**Client cannot find login server** -- the client connects to the hostname
embedded in the launcher INI, not to "localhost" by default. Edit the INI
to point at `127.0.0.1` (client running on the same host as the compose
stack) or your local network IP. The dev stack remaps
`local.net-7.org` -> `127.0.0.1` via `extra_hosts:` so a launcher INI
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
