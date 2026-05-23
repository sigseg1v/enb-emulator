# 09 - Running locally

How to bring up the dev stack and talk to it. This document is partly
aspirational -- the server does not yet build cleanly on Linux as of
Phase A. Sections marked **(Phase B)**, **(Phase C)**, etc. describe the
intended workflow once the corresponding phase lands.

If you want to play the game and not run a server, you do not need any of
this -- the Linux client installer connects to the public Net-7 server.
See `08-build.md` "Linux client (game client, not server)".

## Prerequisites

- Docker and the Docker Compose v2 plugin.
- `just` (task runner).
- The dependencies in `08-build.md` if you also want host-side builds.
- ~2 GB free disk for the Postgres data volume.

## Dev stack at a glance

`docker-compose.yml` (written in Phase A4 scaffolding) brings up three
services:

| Service | Image | Purpose | Port (host) |
|---|---|---|---|
| `postgres` | `postgres:16` | Game database. Auto-loads `db/postgres/schema.sql` and `db/postgres/seed.sql` on first start. | 5432 |
| `login` | built from `login-server/Dockerfile` | Auth and login flow. | TODO -- pending Phase B |
| `server` | built from `server/Dockerfile` | Sector / world server. | TODO -- pending Phase B |

The "TODO" ports are intentional: the original Net-7 server used
non-standard ports configured by `Config.ini`/SQL. Documenting the
canonical defaults requires a server build and a `--help`-able binary,
which is Phase B work. Once Phase B documents the port layout, this
table gets updated.

## Bring up the stack

```sh
just dev
```

Equivalent to:

```sh
docker compose up --build
```

What you should see, in order:

1. **postgres** comes up, runs the entrypoint, applies `schema.sql` and
   `seed.sql` from `db/postgres/`. Wait for `database system is ready to
   accept connections`.
2. **login** builds (slow the first time) and starts. **(Phase B)**
3. **server** builds and starts. Currently expected to fail during build
   until Phase B is far enough along; check `server/BUILD_ERRORS.md` for
   the current failure mode. **(Phase B)**

Tear down with `just down` (or `docker compose down`). Add `-v` if you
also want to drop the Postgres data volume.

## Apply / refresh the schema manually

If you want to re-apply the schema without restarting:

```sh
# Connect:
just psql                 # opens psql shell against the dev DB
# Or directly:
docker compose exec postgres psql -U net7 -d net7

# From inside psql:
\i /db/schema.sql
\i /db/seed.sql
```

Or from the host:

```sh
psql -h localhost -U net7 -d net7 -f db/postgres/schema.sql
psql -h localhost -U net7 -d net7 -f db/postgres/seed.sql
```

The Postgres conversion lives at `db/postgres/schema.sql` (produced by
`db/postgres/convert.sh` from `db/mysql/net7.sql` during Phase C). Until
Phase C lands, the MySQL dump in `db/mysql/` is the only schema source
and Postgres compatibility is documented in `06-database-schema.md`,
"Postgres migration notes".

## Create a test account

Account creation is normally done by the in-game flow plus the
`//adduser` GM command (see `11-gm-commands.md`). For local dev, the
fastest path is direct DB insert.

**(Phase C)** After Postgres migration the table layout matches
`net7_user.sql`. Insert into `accounts`:

```sql
-- TODO: confirm exact column list against migrated net7_user.sql
INSERT INTO accounts (username, password_hash, access_level, ...)
VALUES ('dev', '<hash>', 100, ...);
```

The password hash format is whatever Net-7's login flow expects. The
original code computes it inside Net7Mysql; reuse that logic, do not
invent a new format. Reference: `login-server/Net7Mysql/`.

Once the server provides a `//adduser` command at the running console
(per `11-gm-commands.md`), use that instead:

```
//adduser dev devpassword 100
```

## Connect a client

### Linux client pointed at local server

1. Install the client per `08-build.md`:
   ```sh
   client/linux-installer/install-enb-linux.sh
   ```
2. The launcher configuration ships pointing at the public Net-7 server.
   To redirect to localhost, edit the launcher's INI. The exact file
   path depends on WINE prefix layout; check
   `client/linux-installer/README.md` for the post-install layout.
3. Replace the login-server host and port with the values from the
   compose stack (see the ports table above; **TODO** until Phase B
   documents canonical ports).
4. Start the launcher under Wine; it should connect to your local
   login-server, advance to character select, then sector select.

### Windows client pointed at local server

Same idea: edit the launcher INI or `Config.xml` to point at the
local host and ports. Native Windows client; no WINE involved.

## Expected behaviour by phase

| Phase complete | What works |
|---|---|
| A (now) | Repo merged, docs in place. Nothing builds yet on Linux. Windows server build via `Net7.sln` should work as it did upstream. |
| B | Server compiles on Linux (with errors progressively removed). It may not link or run; build artifacts and shim coverage tracked in `server/compat/` and `server/BUILD_ERRORS.md`. |
| C | Postgres schema applies cleanly. One representative call site migrated end-to-end (`server/db/MIGRATION_PATTERN.md`); rest is hand-off work. |
| D | `dotnet build tools/Net7Tools.sln` succeeds on Linux and Windows. Editor runtime still Windows-only. |
| E | Server builds against OpenSSL 3 without deprecation warnings on the migrated callsites. |
| F | `-Wall -Wextra` baseline established; top warning categories fixed. |
| G | `ctest` runs at least one passing test. DB smoke test connects to compose Postgres. |
| H | Docs deepened: protocol packet table, sequence diagrams, ability internals. |
| I | OCI images publish to GHCR; CI matrix covers Ubuntu 22.04 and 24.04. |

## Troubleshooting

**`docker compose up` complains about port 5432 already in use** -- you
have a host Postgres running. Either stop it (`sudo systemctl stop
postgresql`) or remap the port in `docker-compose.yml`
(`5433:5432` and use `-p 5433` for psql).

**Postgres logs "permission denied" on schema files** -- the volume mount
maps `db/postgres/` to `/db/` inside the container; check the file
permissions and that `*.sql` is readable by anyone (`chmod a+r
db/postgres/*.sql`).

**Server container builds but exits immediately** -- check
`docker compose logs server`. The most likely cause pre-Phase-C is the
server trying to connect to a `mysql://` URL that does not exist (compose
ships Postgres, not MySQL). Phase C migrates the connection logic.

**Client cannot find login server** -- the client connects to the
hostname embedded in the launcher INI, not to "localhost" by default.
Edit the INI to point at `127.0.0.1` (Linux client running on the same
host as the compose stack) or your local network IP.

**No idea what ports to use** -- this is Phase B work. Until the server
exposes a config or `--help` we cannot document them with confidence.
The original tada-o snapshot used 3500 (login), 4500 (proxy), and 5050
(sector) historically, but those values are unverified against the
current code. Do not treat them as canonical until Phase B does.

## What is intentionally not here

- Production deployment guidance. The project is non-commercial; we do not
  ship operator runbooks.
- A "demo account" with a public password. Set up your own.
- A polished web admin panel. The C# editors are the admin tool; see
  `07-tools-toolchain.md`.
- Scaling guidance. The original architecture sharded sector servers but
  the practical limit on modern hardware is "a handful of concurrent
  testers". Anything beyond that is unverified.
