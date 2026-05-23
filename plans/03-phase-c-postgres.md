# Phase C — Postgres migration

Goal: have `psql -f db/postgres/schema.sql` create the full 71-table schema cleanly. Begin the C++ call-site migration away from mysqlclient; full migration is multi-week and out of scope for one invocation.

## Items

### C1 — Schema conversion

- [ ] Write `db/postgres/convert.sh` — a sed/awk pipeline that takes `db/mysql/net7.sql` and emits Postgres-compatible DDL:
  - drop `ENGINE=InnoDB`, `DEFAULT CHARSET=...`, `COLLATE ...`
  - `\`name\`` → `"name"`
  - `int(N) unsigned` → `bigint` (safe for unsigned ranges), other `int(N)` → `integer`
  - `tinyint(1)` → `boolean` where the column is clearly a flag; else `smallint`
  - `AUTO_INCREMENT` → `GENERATED ALWAYS AS IDENTITY`
  - `datetime` → `timestamp`
  - MySQL `text` is fine in Postgres
  - drop `KEY \`name\` (cols)` from CREATE TABLE bodies → emit as separate `CREATE INDEX` statements
  - `\\0` → `E'\\x00'` in `INSERT INTO` rows (for `net7_user.sql` user-data dump)
      Touches: db/postgres/convert.sh
      Notes:
- [ ] Run convert.sh, output to `db/postgres/schema.sql` + `db/postgres/seed.sql`.
      Touches: db/postgres/schema.sql, seed.sql
      Notes:
- [ ] Validate: `docker run --rm -v $(pwd)/db/postgres:/db postgres:16 sh -c 'pg_ctl start && psql -f /db/schema.sql'` (or just `psql` against docker-compose pg).
      Touches: (validation)
      Notes:
- [ ] Document residual manual fixes needed in `db/postgres/README.md`.
      Touches: db/postgres/README.md
      Notes:

### C2 — C++ call-site survey

- [ ] `grep -rn 'mysql_query\|mysql_real_connect\|mysql_fetch_\|mysql_store_\|mysql_use_\|mysql_num_rows\|MYSQL_RES\|MYSQL_ROW\|MYSQL\*' server/src` → counts per file, in `server/db/MYSQL_CALLSITES.md`.
      Touches: server/db/MYSQL_CALLSITES.md
      Notes:
- [ ] Identify the central DB abstraction — likely `AssetDatabaseSQL.cpp`. Decide on `libpqxx` as the replacement.
      Touches: (decision in 99-decisions-log.md)
      Notes:

### C3 — Begin migration (best effort)

- [ ] Add `libpqxx` to `server/CMakeLists.txt` `find_package` and to `server/Dockerfile` apt-install.
      Touches: server/CMakeLists.txt, server/Dockerfile
      Notes:
- [ ] Migrate ONE call-site end-to-end as a worked example — pick the simplest read-only one (e.g. an asset lookup). Commit with a clear "example migration" message and document the pattern in `server/db/MIGRATION_PATTERN.md`.
      Touches: server/db/MIGRATION_PATTERN.md, one .cpp file
      Notes:
- [ ] Hand off the rest as a Phase C continuation item with the list of remaining files.
      Touches: this plan
      Notes:

## Verification

- `docker compose up postgres -d && psql -h localhost -U net7 -d net7 -f db/postgres/schema.sql` returns 0 (or a documented small residual error list).
- `server/db/MYSQL_CALLSITES.md` and `server/db/MIGRATION_PATTERN.md` committed.
- Proceed to Phase D without stopping.
