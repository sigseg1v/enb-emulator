# MySQL container init

These files are mounted into the `mysql` container at
`/docker-entrypoint-initdb.d/`. The official `mysql` image runs every
`.sql` / `.sh` / `.sh.gz` file there at first-boot, in lexical order, with
the `MYSQL_ROOT_PASSWORD` available as an env var.

Order of execution:

1. `00-create-databases.sql` — creates the `net7` and `net7_user`
   databases (with latin1 charset to match the 2010 dumps) and grants
   the runtime user access.
2. `01-load-net7.sh` — loads `/dumps/net7.sql` into `net7`.
3. `02-load-net7-user.sh` — loads `/dumps/net7_user.sql` into `net7_user`.

The dumps themselves are mounted read-only at `/dumps/` by
`docker-compose.yml`; they are kept verbatim in `db/mysql/*.sql` so the
original 2010 schemas are preserved.

`net7_user` ships with one pre-seeded admin row (account_id=1,
username `admin`) inherited from the upstream dump. The password hash in
that row is the original 2010 dev hash; you will almost certainly want to
overwrite it via the `just seed-account` target before testing.
